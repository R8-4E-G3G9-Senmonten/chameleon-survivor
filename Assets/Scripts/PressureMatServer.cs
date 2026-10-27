using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// メインプレイヤー(カメレオン)役の、ESP32コントローラーからの入力を受信する。
// 届け方は2通りあり、どちらで届いても同じように扱う:
//   - USB:   USBケーブルのシリアル通信 (esp32/controller/controller.ino)
//            1バイト形式。踏み状態が変わった瞬間 + 200msごとに送ってくる
//   - Wi-Fi: UDP で <このPCのIP>:5005 へ (esp32/pressure_mat/pressure_mat.ino)
//            "前,後,左,右" の文字形式(例: 1,0,0,1)
// 形式の解読は ControllerStreamParser。USB でも文字形式を送るスケッチはそのまま使える。
// USBはCOMポートを自動で探し、抜き差しされても探し直す。
//
// 入力が届いていない間は HasMatInput が false になるので、
// プレイヤー側(NightPlayer / SurvivorPlayerController)はその間 WASD だけで動かせる。
// マットとWASDはどちらか片方を選ぶのではなく、常に両方の入力をOR(合成)して使う設計にしてあるので、
// 展示中でも開発中でも同じビルドでテストできる。
//
// USBシリアルには System.IO.Ports が必要なので、Player Settings の
// Api Compatibility Level は .NET Framework にしてある(.NET Standard 2.1 には入っていない)。
public class PressureMatServer : MonoBehaviour
{
    public static PressureMatServer Instance { get; private set; }

    [Header("Wi-Fi (UDP)")]
    public int port = 5005;

    [Header("USB シリアル")]
    public bool useSerial = true;
    [Tooltip("例: COM3。空欄ならCOMポートを順に開いて、1,0,0,0 のような行を送ってくるものを自動で探す")]
    public string serialPortName = "";
    public int baudRate = 115200;

    const int StaleMs = 500; // このミリ秒を超えて新しいパケットが届かなければ「踏んでいない」とみなす
    const int ConnectedStaleMs = 2000; // 接続表示用。多少パケットが飛んでもチラつかないよう緩めの猶予を持たせる
    const int SerialProbeMs = 4000; // ポートを開いてから有効な行が来るまで待つ時間(ESP32は開いた瞬間に再起動することがある)
    const int SerialRescanMs = 2000; // ESP32が見つからない/抜かれた時に探し直す間隔
    const int SerialConfirmFrames = 3; // この回数ぶん正しい形式を受け取ったらESP32とみなす

    UdpClient udp;
    Thread thread;
    Thread serialThread;
    volatile bool running;
    volatile SerialPort serial;

    readonly object dataLock = new object();
    bool rxFront, rxBack, rxLeft, rxRight;
    int lastReceiveTimeMs;
    bool everReceived; // 一度でも受信したか(未受信の時刻を int.MinValue にすると、引き算があふれて「今受信した」扱いになるため)
    string rxSource = "";
    string serialStatus = "";

    public bool Front { get; private set; }
    public bool Back { get; private set; }
    public bool Left { get; private set; }
    public bool Right { get; private set; }
    public bool HasMatInput { get; private set; } // 直近にESP32からパケットを受信できているか(入力用、短い猶予)
    public bool Connected { get; private set; } // 画面表示用の接続状態(長めの猶予でチラつきを抑える)
    public string Source { get; private set; } = ""; // 直近の入力の届き方。"USB COM3" / "Wi-Fi"
    public string SerialStatus { get; private set; } = ""; // USB側の状態(探索中・使用中のポートなど)

    void Awake()
    {
        Instance = this;
        running = true;
        try
        {
            udp = new UdpClient(port);
            thread = new Thread(ReceiveLoop) { IsBackground = true };
            thread.Start();
            Debug.Log("PressureMatServer: UDPポート " + port + " で圧力マットの入力を待機中です。");
        }
        catch (Exception ex)
        {
            Debug.LogError("PressureMatServer: ポート " + port + " の待機に失敗しました(" + ex.Message +
                ")。他のプロセスがポートを使っていないか確認してください。");
        }

        if (useSerial)
        {
            serialThread = new Thread(SerialLoop) { IsBackground = true };
            serialThread.Start();
        }
    }

    // 解読した入力(ControllerStreamParser のビット)を最新状態にする。受信スレッドから呼ばれる。
    void Apply(int bits, string source)
    {
        lock (dataLock)
        {
            rxFront = (bits & ControllerStreamParser.Front) != 0;
            rxBack = (bits & ControllerStreamParser.Back) != 0;
            rxLeft = (bits & ControllerStreamParser.Left) != 0;
            rxRight = (bits & ControllerStreamParser.Right) != 0;
            lastReceiveTimeMs = Environment.TickCount;
            everReceived = true;
            rxSource = source;
        }
    }

    void ReceiveLoop()
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref remote);
                // 1パケット = 1回分。末尾に改行が無くても読めるよう、足してから解読する。
                var parser = new ControllerStreamParser();
                parser.Feed(data, data.Length, bits => Apply(bits, "Wi-Fi"));
                parser.Feed(NewLineByte, 1, bits => Apply(bits, "Wi-Fi"));
            }
            catch (ObjectDisposedException)
            {
                // OnDestroy で Close() した時にここへ来る。終了処理なので何もしない。
            }
            catch (SocketException)
            {
                if (running) { /* 一時的な受信エラーは無視して待ち受けを続ける */ }
            }
        }
    }

    static readonly byte[] NewLineByte = { (byte)'\n' };

    // ---------------------------------------------------------------- USB シリアル

    void SetSerialStatus(string s)
    {
        lock (dataLock) serialStatus = s;
    }

    void SerialLoop()
    {
        while (running)
        {
            List<string> candidates = string.IsNullOrEmpty(serialPortName)
                ? CandidatePorts()
                : new List<string> { serialPortName };

            if (candidates.Count == 0) SetSerialStatus("USB機器が見つかりません");
            foreach (string name in candidates)
            {
                if (!running) return;
                SetSerialStatus(name + " を確認中");
                RunPort(name);
            }
            if (running && candidates.Count > 0) SetSerialStatus("ESP32を探しています");

            int until = Environment.TickCount + SerialRescanMs;
            while (running && Environment.TickCount < until) Thread.Sleep(100);
        }
    }

    // 1つのCOMポートを開き、ESP32の入力が届く限り読み続ける。違う機器・抜かれた・開けない時は戻る。
    // 別の機器の雑多なデータを入力と取り違えないよう、正しい形式を SerialConfirmFrames 回
    // 受け取るまではゲームに反映しない(ESP32なら1秒以内に揃う)。
    void RunPort(string name)
    {
        SerialPort sp = null;
        try
        {
            sp = new SerialPort(name, baudRate)
            {
                ReadTimeout = 500,
                DtrEnable = false,
                RtsEnable = false,
            };
            sp.Open();
            serial = sp;

            var parser = new ControllerStreamParser();
            var buffer = new byte[256];
            int frames = 0;
            string source = "USB " + name;
            int probeUntil = Environment.TickCount + SerialProbeMs;
            while (running)
            {
                int count;
                try
                {
                    count = sp.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    count = 0; // 見つけた後の無音は StaleMs で「入力なし」になるので、そのまま待つ
                }

                parser.Feed(buffer, count, bits =>
                {
                    frames++;
                    if (frames >= SerialConfirmFrames) Apply(bits, source);
                    if (frames == SerialConfirmFrames)
                    {
                        SetSerialStatus(name + " で受信中");
                        Debug.Log("PressureMatServer: USBシリアル " + name + " でESP32を見つけました。");
                    }
                });

                if (frames < SerialConfirmFrames && Environment.TickCount > probeUntil) return; // ESP32ではなさそう
            }
        }
        catch (Exception)
        {
            // 使用中(シリアルモニタ等)・抜かれた・開けないポート。次の候補へ。
        }
        finally
        {
            serial = null;
            try { sp?.Close(); } catch { }
        }
    }

    // 自動探索の候補。Windowsではレジストリから実在のCOMポートを取り、
    // Bluetoothの仮想COMポート(開くと長時間固まることがある)を除外する。
    static List<string> CandidatePorts()
    {
        var result = new List<string>();
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
            {
                if (key != null)
                {
                    foreach (string device in key.GetValueNames())
                    {
                        if (device.IndexOf("Bth", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (key.GetValue(device) is string com && !result.Contains(com)) result.Add(com);
                    }
                    return result;
                }
            }
        }
        catch (Exception)
        {
            // レジストリが読めない環境では下の一覧に任せる
        }
        try { result.AddRange(SerialPort.GetPortNames()); } catch (Exception) { }
        return result;
    }

    void Update()
    {
        lock (dataLock)
        {
            int sinceMs = everReceived ? Environment.TickCount - lastReceiveTimeMs : int.MaxValue;
            HasMatInput = sinceMs < StaleMs;
            Connected = sinceMs < ConnectedStaleMs;
            Front = HasMatInput && rxFront;
            Back = HasMatInput && rxBack;
            Left = HasMatInput && rxLeft;
            Right = HasMatInput && rxRight;
            Source = Connected ? rxSource : "";
            SerialStatus = serialStatus;
        }
    }

    void OnDestroy()
    {
        running = false;
        try { udp?.Close(); } catch { /* 終了時のクローズ失敗は無視してよい */ }
        try { serial?.Close(); } catch { }
        thread?.Join(200);
        serialThread?.Join(700);
    }
}
