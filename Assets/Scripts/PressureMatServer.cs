using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// メインプレイヤー(カメレオン)役の、ESP32+圧力マットからの入力を受信する。
// ESP32側(esp32_pressure_mat.ino)は "前,後,左,右" を 0/1 のCSVにして
// 50msごとに UDP で <このPCのIP>:5005 へ送ってくる。ここではそれを受け取るだけの
// 単純なUDPサーバーを立て、最新の踏み状態をスレッドセーフに公開する。
//
// 圧力マットが繋がっていない/届いていない間は HasMatInput が false になるので、
// プレイヤー側(NightPlayer / SurvivorPlayerController)はその間 WASD だけで動かせる。
// マットとWASDはどちらか片方を選ぶのではなく、常に両方の入力をOR(合成)して使う設計にしてあるので、
// 展示中でも開発中でも同じビルドでテストできる。
public class PressureMatServer : MonoBehaviour
{
    public static PressureMatServer Instance { get; private set; }

    public int port = 5005;
    const int StaleMs = 500; // このミリ秒を超えて新しいパケットが届かなければ「踏んでいない」とみなす
    const int ConnectedStaleMs = 2000; // 接続表示用。多少パケットが飛んでもチラつかないよう緩めの猶予を持たせる

    UdpClient udp;
    Thread thread;
    volatile bool running;

    readonly object dataLock = new object();
    bool rxFront, rxBack, rxLeft, rxRight;
    int lastReceiveTimeMs = int.MinValue;

    public bool Front { get; private set; }
    public bool Back { get; private set; }
    public bool Left { get; private set; }
    public bool Right { get; private set; }
    public bool HasMatInput { get; private set; } // 直近にESP32からパケットを受信できているか(入力用、短い猶予)
    public bool Connected { get; private set; } // 画面表示用の接続状態(長めの猶予でチラつきを抑える)

    void Awake()
    {
        Instance = this;
        try
        {
            udp = new UdpClient(port);
            running = true;
            thread = new Thread(ReceiveLoop) { IsBackground = true };
            thread.Start();
            Debug.Log("PressureMatServer: UDPポート " + port + " で圧力マットの入力を待機中です。");
        }
        catch (Exception ex)
        {
            Debug.LogError("PressureMatServer: ポート " + port + " の待機に失敗しました(" + ex.Message +
                ")。他のプロセスがポートを使っていないか確認してください。");
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
                string text = Encoding.UTF8.GetString(data).Trim();
                string[] parts = text.Split(',');
                if (parts.Length < 4) continue;

                bool f = parts[0].Trim() == "1";
                bool b = parts[1].Trim() == "1";
                bool l = parts[2].Trim() == "1";
                bool r = parts[3].Trim() == "1";
                lock (dataLock)
                {
                    rxFront = f; rxBack = b; rxLeft = l; rxRight = r;
                    lastReceiveTimeMs = Environment.TickCount;
                }
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

    void Update()
    {
        lock (dataLock)
        {
            int sinceMs = Environment.TickCount - lastReceiveTimeMs;
            HasMatInput = sinceMs < StaleMs;
            Connected = sinceMs < ConnectedStaleMs;
            Front = HasMatInput && rxFront;
            Back = HasMatInput && rxBack;
            Left = HasMatInput && rxLeft;
            Right = HasMatInput && rxRight;
        }
    }

    void OnDestroy()
    {
        running = false;
        try { udp?.Close(); } catch { /* 終了時のクローズ失敗は無視してよい */ }
        thread?.Join(200);
    }
}
