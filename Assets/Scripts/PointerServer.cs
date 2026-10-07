using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using UnityEngine;

// Minimal HTTP server (raw TcpListener, no HttpListener) so it runs without
// admin rights / URL reservations on Windows. Serves a single page to the
// phone that reports where it is aimed (gyro) and whether its light button
// is held, via WebSocket /ws (or POST /pointer as a fallback).
// This exists purely to test whether "phone as a pointer over LAN" is
// feasible with acceptable latency for the cooperative-player flashlight.
//
// Runs both plain HTTP (port) and HTTPS with a self-signed cert (httpsPort)
// side by side. The phone page aims with the gyro, which needs
// DeviceOrientationEvent.requestPermission(), which some iOS versions
// silently deny on an insecure origin - hence the HTTPS listener, kept
// separate so a certificate/SSL problem can never take down plain HTTP.
public class PointerServer : MonoBehaviour
{
    public static PointerServer Instance { get; private set; }

    public int port = 8787;
    public int httpsPort = 8788;
    public string ServerUrl { get; private set; } = "起動中...";
    public string HttpsServerUrl { get; private set; } = "起動中...";
    public string LocalIp { get; private set; } = "取得中...";

    // -tunnel を付けて起動した時だけ、Cloudflare のトンネル(cloudflared)をゲームから起動して、
    // インターネット上の https://xxxx.trycloudflare.com/ からもスマホが参加できるようにする。
    // 準備ができたら PublicUrl に入り、ゲーム画面のQRがそのURLに切り替わる(5Gでも、別のWi-Fiでも参加可)。
    public bool TunnelRequested { get; private set; }
    public string PublicUrl { get; private set; } // 外から開けることを確認できるまでは null
    public string TunnelStatus { get; private set; } = "";
    System.Diagnostics.Process tunnelProcess;

    TcpListener listener;
    Thread acceptThread;
    TcpListener httpsListener;
    Thread httpsAcceptThread;
    X509Certificate2 serverCert;
    volatile bool running;

    readonly object dataLock = new object();

    class PointerEntry
    {
        public float x, y;
        public bool active;
        public int lastReceiveTimeMs;
        public int colorIndex;
        public int kills; // このスマホの光で倒した敵の数(ゲーム開始ごとに0に戻す)
    }

    // ゲームの様子(スマホ画面に出す): カメレオンの画面上の位置(上端原点のビューポート座標)と、プレイ中かどうか。
    // ゲームのメインスレッドから SetGameView で書き込み、通信スレッドが返事に入れて送る。
    float viewPlayerX = 0.5f, viewPlayerY = 0.5f;
    bool viewPlaying;

    // Keyed by a per-page-load client id so multiple phones can each drive
    // their own light at once. Entries are pruned after a while of silence
    // so a long session with people joining/leaving doesn't leak memory.
    readonly Dictionary<string, PointerEntry> pointers = new Dictionary<string, PointerEntry>();
    int nextColorIndex;
    const int MaxParticipants = 7; // これ以上は満員として新規参加を断る(誰か抜ければ空きが出る)

    // 誰が見ても区別しやすい、はっきりした色の並び。参加した順に1人ずつ割り当てる(人数が多ければ循環する)。
    static readonly int[] Palette = { 0xff6b6b, 0x6bdfff, 0xffe066, 0xb388ff, 0x6bff95, 0xff9f6b, 0xff6bd6, 0x8c8cff };
    static Color ColorOf(int index) => NightMeshes.Hex(Palette[((index % Palette.Length) + Palette.Length) % Palette.Length]);
    static string HexOf(int index) => "#" + (Palette[((index % Palette.Length) + Palette.Length) % Palette.Length] & 0xffffff).ToString("x6");

    public struct PointerSample { public Vector2 pos; public Color color; public string id; }

    // id のスマホの光で敵を倒した(NightGame から呼ぶ)。次にそのスマホから届いた時の返事で知らせる。
    public void AddKill(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (dataLock)
        {
            if (pointers.TryGetValue(id, out PointerEntry entry)) entry.kills++;
        }
    }

    public void ResetKills()
    {
        lock (dataLock)
        {
            foreach (var e in pointers.Values) e.kills = 0;
        }
    }

    public void SetGameView(Vector2 playerViewport, bool playing)
    {
        lock (dataLock)
        {
            viewPlayerX = Mathf.Clamp01(playerViewport.x);
            viewPlayerY = Mathf.Clamp01(playerViewport.y);
            viewPlaying = playing;
        }
    }

    // スマホへの返事。変化した時だけ送るので、位置は0.01刻みに丸めて細かい揺れでは送らないようにする。
    // dataLock の中で呼ぶこと。
    string BuildReply(PointerEntry entry)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return "{\"ok\":true,\"color\":\"" + HexOf(entry.colorIndex) + "\",\"kills\":" + entry.kills +
               ",\"play\":" + (viewPlaying ? "true" : "false") +
               ",\"px\":" + viewPlayerX.ToString("0.00", ci) + ",\"py\":" + viewPlayerY.ToString("0.00", ci) + "}";
    }
    const string FullReply = "{\"ok\":false,\"full\":true}";

    public const int MaxPlayers = MaxParticipants;

    // 接続中(直近2.5秒以内に何か受信した)のスマホの色を、参加順に返す。画面に参加者を表示するための一覧。
    // スマホ側は触っていなくても定期的に送ってくるので、画面を触っていない人も「接続中」に数えられる。
    public List<Color> GetConnectedColors()
    {
        var list = new List<KeyValuePair<int, Color>>();
        int now = Environment.TickCount;
        lock (dataLock)
        {
            foreach (var kv in pointers)
                if (now - kv.Value.lastReceiveTimeMs < 2500)
                    list.Add(new KeyValuePair<int, Color>(kv.Value.colorIndex, ColorOf(kv.Value.colorIndex)));
        }
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        var result = new List<Color>();
        foreach (var e in list) result.Add(e.Value);
        return result;
    }

    [Serializable]
    class PointerMsg
    {
        public string id;
        public float x;
        public float y;
        public bool active;
    }

    bool isDuplicate;

    // シーンを読み直しても(リトライ・タイトルへ戻る)サーバーは止めずに使い続ける。
    // 止めると接続中のスマホが全員切れ、-tunnel の時はURLまで変わってQRを読み直しになるため。
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            isDuplicate = true; // 読み直したシーンに入っている2つ目は使わない
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (isDuplicate) return;
        string ip = GetLocalIPv4();
        LocalIp = ip;

        // WebSocketの接続はつながっている間ずっとスレッドを1本使い続ける。最大人数ぶん同時につながっても
        // 新しい接続の受付が待たされないよう、スレッドプールの最低数を多めに確保しておく。
        ThreadPool.GetMinThreads(out int minWorker, out int minIo);
        ThreadPool.SetMinThreads(Math.Max(minWorker, 32), Math.Max(minIo, 32));

        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            running = true;
            acceptThread = new Thread(() => AcceptLoop(listener, false));
            acceptThread.IsBackground = true;
            acceptThread.Start();
            ServerUrl = $"http://{ip}:{port}/";
            Debug.Log("PointerServer listening at " + ServerUrl);
        }
        catch (Exception e)
        {
            ServerUrl = "起動失敗: " + e.Message;
            Debug.LogError("PointerServer (HTTP) failed to start: " + e);
        }

        try
        {
            serverCert = CreateSelfSignedCert(ip);
            httpsListener = new TcpListener(IPAddress.Any, httpsPort);
            httpsListener.Start();
            httpsAcceptThread = new Thread(() => AcceptLoop(httpsListener, true));
            httpsAcceptThread.IsBackground = true;
            httpsAcceptThread.Start();
            HttpsServerUrl = $"https://{ip}:{httpsPort}/";
            Debug.Log("PointerServer (HTTPS) listening at " + HttpsServerUrl);
        }
        catch (Exception e)
        {
            HttpsServerUrl = "起動失敗: " + e.Message;
            Debug.LogError("PointerServer (HTTPS) failed to start: " + e);
        }

        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-tunnel") >= 0)
        {
            TunnelRequested = true;
            int i = Array.IndexOf(args, "-cloudflared");
            StartTunnel(i >= 0 && i + 1 < args.Length ? args[i + 1] : null);
        }
    }

    // ---------------------------------------------------------------- Cloudflare トンネル

    // cloudflared.exe の場所。-cloudflared で指定されていなければ、よくあるインストール先と PATH を探す。
    static string FindCloudflared(string explicitPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(explicitPath)) candidates.Add(explicitPath);
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        string home = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
        candidates.Add(Path.Combine(local, @"Microsoft\WinGet\Links\cloudflared.exe")); // winget install Cloudflare.cloudflared
        candidates.Add(Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", @"cloudflared\cloudflared.exe"));
        candidates.Add(Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "", @"cloudflared\cloudflared.exe"));
        candidates.Add(Path.Combine(home, @"tools\cloudflared\cloudflared.exe"));
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (dir.Length > 0) candidates.Add(Path.Combine(dir.Trim('"'), "cloudflared.exe"));
        foreach (string c in candidates)
        {
            try { if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    void StartTunnel(string explicitPath)
    {
        string exe = FindCloudflared(explicitPath);
        if (exe == null)
        {
            TunnelStatus = "cloudflared が見つかりません";
            Debug.LogError("PointerServer: -tunnel が指定されましたが cloudflared.exe が見つかりません。" +
                "「winget install --id Cloudflare.cloudflared」で入れるか、-cloudflared <パス> で指定してください。");
            return;
        }
        TunnelStatus = "5G用URLを準備中…";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("tunnel");
        psi.ArgumentList.Add("--no-autoupdate");
        psi.ArgumentList.Add("--url");
        psi.ArgumentList.Add("http://127.0.0.1:" + port); // localhost だと先に IPv6(::1) へつなぎに行って失敗するので IPv4 を直接指定

        string foundUrl = null;
        bool checking = false;
        var urlPattern = new System.Text.RegularExpressions.Regex(@"https://[a-z0-9-]+\.trycloudflare\.com");
        System.Diagnostics.DataReceivedEventHandler onLine = (s, e) =>
        {
            if (e.Data == null) return;
            if (foundUrl == null)
            {
                var m = urlPattern.Match(e.Data);
                if (m.Success) foundUrl = m.Value + "/";
            }
            // Cloudflare側とつながったら、そのURLが本当に外から開けるか確かめてから QR に出す
            if (foundUrl != null && !checking && e.Data.Contains("Registered tunnel connection"))
            {
                checking = true;
                string url = foundUrl;
                new Thread(() => WaitUntilReachable(url)) { IsBackground = true }.Start();
            }
        };
        try
        {
            tunnelProcess = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
            tunnelProcess.ErrorDataReceived += onLine; // cloudflared のログは標準エラーに出る
            tunnelProcess.OutputDataReceived += onLine;
            tunnelProcess.Exited += (s, e) =>
            {
                PublicUrl = null;
                if (running) TunnelStatus = "トンネルが止まりました";
            };
            tunnelProcess.Start();
            tunnelProcess.BeginErrorReadLine();
            tunnelProcess.BeginOutputReadLine();
            Debug.Log("PointerServer: Cloudflare トンネルを起動しました (" + exe + ")");
        }
        catch (Exception e)
        {
            TunnelStatus = "トンネル起動失敗";
            Debug.LogError("PointerServer: cloudflared の起動に失敗しました: " + e);
        }
    }

    // 作られた直後のURLは、DNSに行き渡るまで数秒〜十数秒は開けないことがある。
    // その間にスマホが読み取ると「見つからない」が端末に記憶されて、しばらく開けなくなるので、
    // PC自身から開けることを確認してから PublicUrl に入れる(=QRを切り替える)。
    void WaitUntilReachable(string url)
    {
        int startedAt = Environment.TickCount;
        int until = startedAt + 90000;
        while (running && Environment.TickCount < until)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 15000; // 回線が遅い所では、作られたばかりのURLの1回目は10秒以上かかることがある
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    if (res.StatusCode == HttpStatusCode.OK)
                    {
                        PublicUrl = url;
                        TunnelStatus = "";
                        Debug.Log("PointerServer: インターネットから参加できます(" + (Environment.TickCount - startedAt) / 1000 + "秒で確認): " + url);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("PointerServer: トンネルのURLはまだ開けません(" + e.Message + ")。再確認します。");
            }
            Thread.Sleep(2000);
        }
        if (running)
        {
            // 確認できなくても、URL自体はできているので出しておく(PC側のDNSだけが遅れている場合もある)
            PublicUrl = url;
            TunnelStatus = "";
            Debug.LogWarning("PointerServer: " + url + " をPCから確認できませんでしたが、QRに表示します。");
        }
    }

    // Unity's Mono runtime doesn't support CertificateRequest.CreateSelfSigned
    // on Windows (throws PlatformNotSupportedException), so cert *generation*
    // is delegated to Windows' own PowerShell (New-SelfSignedCertificate),
    // which has no such gap. We only ever *load* the resulting PFX from C#,
    // which works fine. Windows-only; that matches this project's dev machine.
    static X509Certificate2 CreateSelfSignedCert(string ip)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ChameleonNightCert");
        Directory.CreateDirectory(tempDir);
        string pfxPath = Path.Combine(tempDir, "server.pfx");
        string scriptPath = Path.Combine(tempDir, "makecert.ps1");
        string password = "chameleon";

        File.WriteAllText(scriptPath, PfxGeneratorScript);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-Ip");
        psi.ArgumentList.Add(ip);
        psi.ArgumentList.Add("-PfxPath");
        psi.ArgumentList.Add(pfxPath);
        psi.ArgumentList.Add("-Password");
        psi.ArgumentList.Add(password);

        using (var proc = System.Diagnostics.Process.Start(psi))
        {
            proc.WaitForExit(15000);
            if (proc.ExitCode != 0 || !File.Exists(pfxPath))
            {
                string err = proc.StandardError.ReadToEnd();
                throw new Exception("PowerShell cert generation failed (exit " + proc.ExitCode + "): " + err);
            }
        }

        byte[] pfxBytes = File.ReadAllBytes(pfxPath);
        var cert = new X509Certificate2(pfxBytes, password, X509KeyStorageFlags.Exportable);
        try { File.Delete(pfxPath); } catch { }
        return cert;
    }

    const string PfxGeneratorScript = @"
param([string]$Ip, [string]$PfxPath, [string]$Password)
$ErrorActionPreference = 'Stop'
$cert = New-SelfSignedCertificate -DnsName $Ip, 'localhost', '127.0.0.1' `
    -CertStoreLocation 'cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears(5) `
    -KeyExportPolicy Exportable `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
    -KeySpec Signature
$securePwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $securePwd | Out-Null
Remove-Item -Path (""cert:\CurrentUser\My\"" + $cert.Thumbprint) -DeleteKey
";

    void AcceptLoop(TcpListener l, bool useSsl)
    {
        while (running)
        {
            TcpClient client;
            try
            {
                client = l.AcceptTcpClient();
            }
            catch (SocketException)
            {
                break; // listener was stopped
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            ThreadPool.QueueUserWorkItem(_ => HandleClient(client, useSsl));
        }
    }

    void HandleClient(TcpClient client, bool useSsl)
    {
        try
        {
            client.NoDelay = true; // Nagleアルゴリズムを無効化。光の座標のような小さいパケットを即座に送る
            client.ReceiveTimeout = 30000;
            using (client)
            {
                Stream stream = client.GetStream();
                SslStream ssl = null;
                if (useSsl)
                {
                    ssl = new SslStream(stream, false);
                    ssl.AuthenticateAsServer(serverCert, false, SslProtocols.Tls12, false);
                    stream = ssl;
                }

                using (stream)
                {
                    // Keep-Alive: 毎リクエストでTCP(HTTPSならTLSハンドシェイクも)を繋ぎ直すと、
                    // スマホが20Hzで送ってくる /pointer のたびにその接続コストがそのまま遅延になってしまう。
                    // 同じ接続を使い回して複数リクエストを処理し続けることで、この遅延をほぼ無くせる。
                    while (running)
                    {
                        ReadRequest(stream, out string requestLine, out Dictionary<string, string> headers, out string body);
                        if (string.IsNullOrEmpty(requestLine)) break;

                        string[] parts = requestLine.Split(' ');
                        string method = parts.Length > 0 ? parts[0] : "GET";
                        string path = parts.Length > 1 ? parts[1] : "/";

                        if (method == "GET" && path == "/ws" && IsWebSocketUpgrade(headers))
                        {
                            // ここから先はこの接続をWebSocketとして使い続ける(終わったら接続ごと閉じる)
                            RunWebSocket(client, stream, headers["Sec-WebSocket-Key"]);
                            break;
                        }
                        else if (method == "POST" && path == "/pointer")
                        {
                            WriteResponse(stream, "200 OK", "application/json", HandlePointerPost(body));
                        }
                        else if (method == "GET" && (path == "/" || path == "/phone.html"))
                        {
                            WriteResponse(stream, "200 OK", "text/html; charset=utf-8", PhoneHtml);
                        }
                        else
                        {
                            WriteResponse(stream, "404 Not Found", "text/plain", "not found");
                        }
                    }
                }
            }
        }
        catch (ObjectDisposedException) { /* サーバー終了処理中 */ }
        catch (IOException) { /* Keep-Alive接続がアイドルタイムアウト/相手に閉じられた。通常の動作 */ }
        catch (Exception e)
        {
            Debug.LogWarning("PointerServer client error: " + e.Message);
        }
    }

    void ReadRequest(Stream stream, out string requestLine, out Dictionary<string, string> headers, out string body)
    {
        requestLine = null;
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        body = "";

        var raw = new MemoryStream();
        byte[] buf = new byte[4096];
        int headerEnd = -1;
        int totalRead = 0;

        // Read until we have the blank line that ends the headers.
        while (true)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            raw.Write(buf, 0, n);
            totalRead += n;

            byte[] data = raw.GetBuffer();
            headerEnd = IndexOfDoubleCrlf(data, (int)raw.Length);
            if (headerEnd >= 0) break;
            if (totalRead > 65536) break; // safety cap
        }

        if (headerEnd < 0) return;

        byte[] allBytes = raw.GetBuffer();
        int len = (int)raw.Length;
        string headerText = Encoding.UTF8.GetString(allBytes, 0, headerEnd);
        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        requestLine = lines.Length > 0 ? lines[0] : "";

        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
        }

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out string cl)) int.TryParse(cl, out contentLength);

        int bodyStart = headerEnd + 4;
        int bodyBytesSoFar = len - bodyStart;

        var bodyBuffer = new MemoryStream();
        if (bodyBytesSoFar > 0)
            bodyBuffer.Write(allBytes, bodyStart, bodyBytesSoFar);

        while (bodyBuffer.Length < contentLength)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            bodyBuffer.Write(buf, 0, n);
        }

        body = Encoding.UTF8.GetString(bodyBuffer.GetBuffer(), 0, (int)bodyBuffer.Length);
    }

    static int IndexOfDoubleCrlf(byte[] data, int length)
    {
        for (int i = 0; i + 3 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                return i;
        }
        return -1;
    }

    // 戻り値はスマホへ返すJSON(割り当てた色・倒した数・ゲームの様子)。満員で新規参加を断った場合は FullReply。
    string HandlePointerPost(string body) => HandlePointerPost(body, out _);

    string HandlePointerPost(string body, out string clientId)
    {
        clientId = null;
        try
        {
            PointerMsg msg = JsonUtility.FromJson<PointerMsg>(body);
            if (msg == null || string.IsNullOrEmpty(msg.id)) return "{\"ok\":false}";
            clientId = msg.id;
            lock (dataLock)
            {
                if (!pointers.TryGetValue(msg.id, out PointerEntry entry))
                {
                    if (pointers.Count >= MaxParticipants) return FullReply; // 満員(最大7人)
                    entry = new PointerEntry { colorIndex = nextColorIndex++ };
                    pointers[msg.id] = entry;
                }
                entry.x = Mathf.Clamp01(msg.x);
                entry.y = Mathf.Clamp01(msg.y);
                entry.active = msg.active;
                entry.lastReceiveTimeMs = Environment.TickCount;
                return BuildReply(entry);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("PointerServer bad payload: " + e.Message);
            return "{\"ok\":false}";
        }
    }

    // ================= WebSocket (/ws) =================
    // HTTPの /pointer は1回ごとに「送る→返事を待つ」になるので、インターネット越し(Cloudflare Tunnel 等)
    // だと往復の待ちが積み重なって光がカクつく。WebSocketは1本の接続を開きっぱなしにして、
    // 返事を待たずに小さなメッセージを流せるので、遅延が小さく一定になる。
    // メッセージの中身は /pointer と同じJSON。返事(色・倒した数・ゲームの様子)は変化した時だけ返す。
    // {"t":"ping","n":...} が来たら {"t":"pong","n":...} をそのまま返す(スマホ側で往復時間を表示するため)。

    const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    const int WebSocketMaxPayload = 64 * 1024;

    static bool IsWebSocketUpgrade(Dictionary<string, string> headers)
    {
        return headers.TryGetValue("Upgrade", out string up)
               && up.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0
               && headers.ContainsKey("Sec-WebSocket-Key");
    }

    void RunWebSocket(TcpClient client, Stream stream, string key)
    {
        string accept;
        using (var sha1 = SHA1.Create())
            accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
        byte[] handshake = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
        stream.Write(handshake, 0, handshake.Length);
        stream.Flush();

        // スマホは触っていなくても0.25秒ごとに送ってくるので、10秒何も来なければ切れたとみなす
        client.ReceiveTimeout = 10000;

        string clientId = null;
        string lastReply = null;
        var message = new MemoryStream();
        try
        {
            while (running)
            {
                if (!ReadWebSocketFrame(stream, out bool fin, out int opcode, out byte[] payload)) break;

                if (opcode == 0x8) // close
                {
                    WriteWebSocketFrame(stream, 0x8, payload);
                    break;
                }
                if (opcode == 0x9) { WriteWebSocketFrame(stream, 0xA, payload); continue; } // ping → pong
                if (opcode == 0xA) continue; // pong

                // テキスト(0x1)とその続き(0x0)をつなげて、1メッセージになったら処理する
                if (opcode == 0x1 || opcode == 0x0)
                {
                    message.Write(payload, 0, payload.Length);
                    if (message.Length > WebSocketMaxPayload) break;
                    if (!fin) continue;

                    string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);

                    if (text.IndexOf("\"t\":\"ping\"", StringComparison.Ordinal) >= 0)
                    {
                        WriteWebSocketText(stream, text.Replace("\"t\":\"ping\"", "\"t\":\"pong\""));
                        continue;
                    }

                    string reply = HandlePointerPost(text, out string id);
                    if (id != null) clientId = id;
                    if (reply != lastReply)
                    {
                        WriteWebSocketText(stream, reply);
                        lastReply = reply;
                    }
                }
            }
        }
        finally
        {
            // 接続が切れたら、そのスマホの光をすぐ消す(HTTPのように0.5秒待たない)
            if (clientId != null)
            {
                lock (dataLock)
                {
                    if (pointers.TryGetValue(clientId, out PointerEntry entry)) entry.active = false;
                }
            }
        }
    }

    // 1フレーム読む。クライアントからのフレームは必ずマスクされている。接続が閉じたら false。
    static bool ReadWebSocketFrame(Stream stream, out bool fin, out int opcode, out byte[] payload)
    {
        fin = false; opcode = 0; payload = null;
        byte[] head = new byte[2];
        if (!ReadExactly(stream, head, 2)) return false;
        fin = (head[0] & 0x80) != 0;
        opcode = head[0] & 0x0F;
        bool masked = (head[1] & 0x80) != 0;
        long len = head[1] & 0x7F;
        if (len == 126)
        {
            byte[] ext = new byte[2];
            if (!ReadExactly(stream, ext, 2)) return false;
            len = (ext[0] << 8) | ext[1];
        }
        else if (len == 127)
        {
            byte[] ext = new byte[8];
            if (!ReadExactly(stream, ext, 8)) return false;
            len = 0;
            for (int i = 0; i < 8; i++) len = (len << 8) | ext[i];
        }
        if (len > WebSocketMaxPayload) return false;

        byte[] mask = new byte[4];
        if (masked && !ReadExactly(stream, mask, 4)) return false;
        payload = new byte[len];
        if (len > 0 && !ReadExactly(stream, payload, (int)len)) return false;
        if (masked)
            for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
        return true;
    }

    static bool ReadExactly(Stream stream, byte[] buf, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = stream.Read(buf, got, count - got);
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }

    static void WriteWebSocketText(Stream stream, string text) =>
        WriteWebSocketFrame(stream, 0x1, Encoding.UTF8.GetBytes(text));

    // サーバーからのフレームはマスクしない
    static void WriteWebSocketFrame(Stream stream, int opcode, byte[] payload)
    {
        payload = payload ?? new byte[0];
        var frame = new MemoryStream(payload.Length + 10);
        frame.WriteByte((byte)(0x80 | opcode));
        if (payload.Length < 126)
        {
            frame.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= 0xFFFF)
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)payload.Length);
        }
        else
        {
            frame.WriteByte(127);
            for (int i = 7; i >= 0; i--) frame.WriteByte((byte)((long)payload.Length >> (8 * i)));
        }
        frame.Write(payload, 0, payload.Length);
        stream.Write(frame.GetBuffer(), 0, (int)frame.Length);
        stream.Flush();
    }

    void WriteResponse(Stream stream, string status, string contentType, string content)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(content);
        string header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    // Returns one (x,y) per client currently sending active=true within the
    // last 500ms. Also opportunistically prunes clients that have gone
    // fully silent for a while (tab closed, phone walked away) so a long
    // session doesn't accumulate stale entries forever.
    public List<Vector2> GetActivePointers()
    {
        var result = new List<Vector2>();
        foreach (var s in GetActiveSamples()) result.Add(s.pos);
        return result;
    }

    // GetActivePointers と同じ対象を、各クライアントに割り当てた色つきで返す
    // (ライトの色をスマホ役ごとに変えるため)。
    public List<PointerSample> GetActiveSamples()
    {
        var result = new List<PointerSample>();
        int now = Environment.TickCount;
        List<string> stale = null;

        lock (dataLock)
        {
            foreach (var kv in pointers)
            {
                int age = now - kv.Value.lastReceiveTimeMs;
                if (kv.Value.active && age < 500)
                {
                    result.Add(new PointerSample { pos = new Vector2(kv.Value.x, kv.Value.y), color = ColorOf(kv.Value.colorIndex), id = kv.Key });
                }
                if (age > 10000)
                {
                    if (stale == null) stale = new List<string>();
                    stale.Add(kv.Key);
                }
            }
            if (stale != null)
            {
                foreach (var key in stale) pointers.Remove(key);
            }
        }
        return result;
    }

    static string GetLocalIPv4()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    void StopServer()
    {
        if (isDuplicate) return;
        running = false;
        try { listener?.Stop(); } catch { }
        try { httpsListener?.Stop(); } catch { }
        try { if (tunnelProcess != null && !tunnelProcess.HasExited) tunnelProcess.Kill(); } catch { }
        tunnelProcess = null;
    }

    void OnDestroy() => StopServer();
    void OnApplicationQuit() => StopServer();

    const string PhoneHtml = @"<!DOCTYPE html>
<html lang=""ja""><head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1, user-scalable=no, viewport-fit=cover"">
<meta name=""theme-color"" content=""#050b09"">
<meta name=""color-scheme"" content=""dark"">
<meta name=""mobile-web-app-capable"" content=""yes"">
<meta name=""apple-mobile-web-app-capable"" content=""yes"">
<meta name=""apple-mobile-web-app-status-bar-style"" content=""black-translucent"">
<title>カメレオンナイト - 灯り役</title>
<style>
  /* ---- デザイントークン(暗い画面で光が映える OLED 向けダーク。文字はどれも背景とのコントラスト 6:1 以上) ---- */
  :root {
    --c:#fff2c0;                       /* 自分のライトの色(サーバーから割り当て) */
    --bg:#050b09; --surface:#0d1a16; --surface-2:#142621; --line:rgba(190,255,215,.16);
    --text:#e6f5ec; --muted:#a3c4b5; --good:#9dffb0; --warn:#ffcf6b;
    --r-sm:12px; --r-md:16px; --r-lg:28px;
    --s1:4px; --s2:8px; --s3:12px; --s4:16px; --s5:24px;
    --fast:120ms; --base:220ms; --ease-out:cubic-bezier(.2,.8,.2,1);
  }
  * { box-sizing:border-box; }
  html,body { margin:0; padding:0; width:100%; height:100%; background:var(--bg); color:var(--text); overflow:hidden;
              touch-action:none; -webkit-user-select:none; user-select:none; -webkit-touch-callout:none;
              -webkit-tap-highlight-color:transparent;
              font-family:-apple-system, BlinkMacSystemFont, 'Hiragino Sans', 'Noto Sans JP', sans-serif; }
  button { font-family:inherit; color:inherit; cursor:pointer; -webkit-tap-highlight-color:transparent; touch-action:manipulation; }
  button:focus-visible { outline:3px solid var(--text); outline-offset:3px; }
  svg.icon { width:24px; height:24px; flex:none; fill:none; stroke:currentColor; stroke-width:2;
             stroke-linecap:round; stroke-linejoin:round; }
  .sr-only { position:absolute; width:1px; height:1px; margin:-1px; overflow:hidden; clip:rect(0 0 0 0); white-space:nowrap; border:0; }

  /* ---- 画面の縁: 自分のライトの色(--fc)。敵を倒すと縁の線が「白 ⇄ ライトの色」と素早く3回点滅する ----
     速く点滅するのは細い縁の線だけ。内側の大きな光(#frameGlow)は点滅させず1回ふわっと消えるだけにして、
     画面の広い範囲がチカチカしないようにしている(光に敏感な人への配慮)。点滅中に倒しても重ねない。 */
  :root { --fc:var(--c); }
  body.hit-white { --fc:#ffffff; }
  #frame { position:fixed; inset:0; z-index:20; pointer-events:none; border:6px solid var(--fc);
           box-shadow:inset 0 0 18px -6px var(--fc); } /* 画面の形に合わせた長方形(角は丸めない) */
  body.hit-white #frame { border-width:9px; }
  #frameGlow { position:fixed; inset:0; z-index:19; pointer-events:none; opacity:0;
               box-shadow:inset 0 0 70px 18px var(--c); }
  #frameGlow.flash { animation:glow 700ms var(--ease-out); }
  @keyframes glow { 0% { opacity:0; } 10% { opacity:1; } 100% { opacity:0; } }

  #app { position:fixed; inset:0; display:flex; flex-direction:column;
         padding:calc(var(--s4) + 8px + env(safe-area-inset-top)) calc(var(--s4) + 6px) calc(var(--s4) + 6px + env(safe-area-inset-bottom)); }

  /* ---- 上半分: 自分の情報とミニ画面 ---- */
  #top { flex:0 0 50%; display:flex; flex-direction:column; gap:var(--s3); min-height:0; }
  #head { display:flex; align-items:center; justify-content:space-between; gap:var(--s3); }
  #who { display:flex; align-items:center; gap:var(--s3); min-width:0; }
  #swatch { width:32px; height:32px; flex:none; border-radius:50%; background:var(--c);
            box-shadow:0 0 12px 1px var(--c); border:2px solid rgba(255,255,255,.85); transition:background var(--base), box-shadow var(--base); }
  #whoText { display:flex; flex-direction:column; gap:2px; line-height:1.25; min-width:0; }
  #whoTitle { font-size:17px; font-weight:700; }
  #whoSub { font-size:13px; color:var(--muted); }
  #whoSub b { color:var(--c); font-weight:700; }
  #killBox { position:relative; display:flex; align-items:baseline; gap:var(--s2); padding:var(--s2) var(--s4);
             border-radius:var(--r-md); background:var(--surface); border:1px solid var(--line); }
  #killLabel { font-size:13px; color:var(--muted); }
  #killNum { font-size:36px; font-weight:800; line-height:1; color:var(--c); min-width:1ch; text-align:right;
             font-variant-numeric:tabular-nums; transition:color var(--base); }
  #killUnit { font-size:13px; color:var(--muted); }
  #killNum.pop { animation:pop 420ms var(--ease-out); }
  @keyframes pop { 0% { transform:scale(1.45); } 100% { transform:scale(1); } }
  #killPlus { position:absolute; right:var(--s3); top:-10px; font-size:16px; font-weight:800; color:var(--c); opacity:0; pointer-events:none; }
  #killPlus.show { animation:plus 800ms var(--ease-out); }
  @keyframes plus { 0% { opacity:1; transform:translateY(0); } 100% { opacity:0; transform:translateY(-22px); } }

  #miniWrap { flex:1; min-height:0; display:flex; align-items:center; justify-content:center; }
  #mini { position:relative; width:100%; aspect-ratio:16/9; max-height:100%; border-radius:var(--r-md); overflow:hidden;
          container-type:size;
          --ax:.5; --ay:.5; --px:.5; --py:.5;
          background:radial-gradient(ellipse at 50% 45%, #1a362c 0%, #0e221b 55%, #06110d 100%);
          border:1px solid var(--line); }
  #mini::before { content:''; position:absolute; inset:0; opacity:.45;
                  background-image:linear-gradient(var(--line) 1px, transparent 1px), linear-gradient(90deg, var(--line) 1px, transparent 1px);
                  background-size:12.5% 22.2%; }
  #miniLabel { position:absolute; left:var(--s3); top:var(--s2); font-size:12px; color:var(--muted); }
  /* 位置は transform で動かす(left/top を動かすと毎回レイアウトの計算が走るため) */
  .marker { position:absolute; left:0; top:0; }
  #me { width:16px; height:16px; transform:translate(calc(var(--px) * 100cqw - 50%), calc(var(--py) * 100cqh - 50%));
        transition:transform 150ms linear; }
  #meDot { width:16px; height:16px; border-radius:50%; background:#6bdc8a; border:2px solid #d9ffe4;
           box-shadow:0 0 10px 2px rgba(107,220,138,.6); }
  #meLabel { position:absolute; left:50%; top:20px; transform:translateX(-50%); font-size:12px; color:#c8f7d7; white-space:nowrap; }
  #aim { width:26cqh; height:26cqh; transform:translate(calc(var(--ax) * 100cqw - 50%), calc(var(--ay) * 100cqh - 50%));
         border-radius:50%; border:2px dashed var(--c); opacity:.8; transition:opacity var(--fast), background var(--fast); }
  #aim::after { content:''; position:absolute; left:50%; top:50%; width:6px; height:6px; margin:-3px 0 0 -3px;
                border-radius:50%; background:var(--c); }
  #aim.on { border-style:solid; opacity:1; background:radial-gradient(circle, var(--c) 0%, transparent 70%);
            box-shadow:0 0 22px 4px var(--c); }

  #statusRow { display:flex; align-items:center; justify-content:space-between; gap:var(--s2); font-size:13px; color:var(--muted); }
  #gameState { display:flex; align-items:center; gap:6px; }
  #gameState::before { content:''; width:8px; height:8px; border-radius:50%; background:#5d7a6d; }
  #gameState.play { color:var(--good); }
  #gameState.play::before { background:var(--good); }
  #net { text-align:right; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; font-variant-numeric:tabular-nums; }

  /* ---- 下半分: ボタン(主役はライトONの1つだけ) ---- */
  #bottom { flex:1; display:flex; flex-direction:column; gap:var(--s3); padding-top:var(--s4); min-height:0; }
  #subRow { display:flex; justify-content:space-between; align-items:center; gap:var(--s3); }
  #hint { font-size:13px; color:var(--muted); line-height:1.5; }
  .subBtn { flex:none; display:flex; align-items:center; gap:var(--s2); min-height:48px; padding:0 var(--s4);
            font-size:14px; font-weight:600; border-radius:var(--r-sm); border:1px solid var(--line);
            background:var(--surface); transition:background var(--fast); }
  .subBtn[hidden] { display:none; }
  .subBtn svg { width:20px; height:20px; }
  .subBtn:active { background:var(--surface-2); }
  /* 全画面ボタンが出ている間は、説明文の代わりにそれを置く(狭い画面で詰まらないように) */
  #subRow.fsOffer #hint { display:none; }
  #btnLight { position:relative; flex:1; min-height:120px; width:100%; border-radius:var(--r-lg); border:2px solid rgba(255,255,255,.16);
              background:linear-gradient(180deg, #172a24, #0d1a15); color:var(--text);
              display:flex; flex-direction:column; align-items:center; justify-content:center; gap:var(--s2);
              touch-action:none; transition:background var(--fast), box-shadow var(--fast), transform var(--fast), color var(--fast); }
  #btnLight .icon { width:56px; height:56px; stroke-width:1.75; color:var(--c); transition:color var(--fast); }
  #lightText { font-size:32px; font-weight:800; letter-spacing:.06em; }
  #lightSub { font-size:13px; color:var(--muted); }
  #btnLight.on { background:var(--c); color:#111; border-color:#fff; transform:scale(.98);
                 box-shadow:0 0 44px 6px var(--c), inset 0 0 36px rgba(255,255,255,.5); }
  #btnLight.on .icon { color:#111; }
  #btnLight.on #lightSub { color:rgba(0,0,0,.72); }

  /* ---- お知らせ(中心リセット・接続切れ) ---- */
  #toast { position:fixed; left:50%; top:calc(var(--s4) + 14px + env(safe-area-inset-top)); z-index:25;
           transform:translate(-50%, -8px); opacity:0; pointer-events:none;
           padding:var(--s2) var(--s4); border-radius:999px; background:var(--surface-2); border:1px solid var(--line);
           font-size:14px; font-weight:600; transition:opacity var(--base), transform var(--base) var(--ease-out); }
  #toast.show { opacity:1; transform:translate(-50%, 0); }
  #offline { position:fixed; left:var(--s5); right:var(--s5); top:calc(var(--s4) + 14px + env(safe-area-inset-top)); z-index:26;
             display:none; align-items:center; gap:var(--s2); padding:var(--s3) var(--s4); border-radius:var(--r-sm);
             background:#2a1f08; border:1px solid var(--warn); color:var(--warn); font-size:14px; font-weight:600; }
  #offline.show { display:flex; }

  /* ---- はじめる画面・満員 ---- */
  .overlay { position:fixed; inset:0; z-index:30; display:flex; flex-direction:column; align-items:center; justify-content:center;
             gap:var(--s5); padding:calc(32px + env(safe-area-inset-top)) 28px calc(32px + env(safe-area-inset-bottom)); text-align:center;
             background:radial-gradient(ellipse at 50% 30%, #143128 0%, #050b09 70%); }
  .overlay h1 { margin:0; font-size:28px; font-weight:800; line-height:1.3; }
  .overlay p { margin:0; font-size:15px; line-height:1.7; color:var(--muted); }
  .overlay .tag { font-size:13px; letter-spacing:.3em; color:var(--good); }
  #steps { list-style:none; margin:0; padding:0; display:flex; flex-direction:column; gap:var(--s3); text-align:left; width:min(100%, 320px); }
  #steps li { display:flex; align-items:center; gap:var(--s3); padding:var(--s3) var(--s4); border-radius:var(--r-sm);
              background:var(--surface); border:1px solid var(--line); font-size:15px; line-height:1.4; }
  #steps .num { flex:none; width:28px; height:28px; border-radius:50%; display:grid; place-items:center;
                background:var(--surface-2); color:var(--good); font-weight:800; font-size:14px; }
  #btnStart { min-height:56px; padding:0 40px; font-size:19px; font-weight:800; border-radius:var(--r-md); border:none;
              background:var(--good); color:#06200f; box-shadow:0 0 26px rgba(157,255,176,.35); transition:transform var(--fast); }
  #btnStart:active { transform:scale(.97); }
  #startNote { font-size:13px !important; }
  #fullOverlay { display:none; }
  #debug { position:fixed; left:var(--s4); right:var(--s4); bottom:calc(var(--s2) + env(safe-area-inset-bottom)); z-index:21; text-align:center;
           font-size:12px; color:var(--warn); white-space:pre-wrap; pointer-events:none; }

  /* ---- 横向きにした時: 左に情報、右にボタン ---- */
  @media (orientation: landscape) {
    #app { flex-direction:row; gap:var(--s5); }
    #top { flex:0 0 55%; }
    #bottom { padding-top:0; }
  }

  /* ---- 動きを減らす設定の端末: 光る・弾む・浮かぶ演出をやめ、色と文字だけで知らせる ---- */
  @media (prefers-reduced-motion: reduce) {
    *, *::before, *::after { animation:none !important; transition:none !important; }
    #frameGlow.flash { opacity:.6; }
  }
</style>
</head>
<body>
<div id=""frameGlow"" aria-hidden=""true""></div>
<div id=""frame"" aria-hidden=""true""></div>
<div id=""killStatus"" class=""sr-only"" role=""status"" aria-atomic=""true""></div>

<div id=""app"">
  <section id=""top"" aria-label=""あなたの状況"">
    <div id=""head"">
      <div id=""who"">
        <div id=""swatch"" aria-hidden=""true""></div>
        <div id=""whoText"">
          <div id=""whoTitle"">灯り役</div>
          <div id=""whoSub"">あなたの光: <b id=""colorName"">準備中</b></div>
        </div>
      </div>
      <div id=""killBox"" aria-hidden=""true"">
        <span id=""killLabel"">撃破</span><span id=""killNum"">0</span><span id=""killUnit"">体</span>
        <span id=""killPlus"">+1</span>
      </div>
    </div>
    <div id=""miniWrap"">
      <div id=""mini"" role=""img"" aria-label=""ゲーム画面の中の、カメレオンとあなたの狙いの位置"">
        <div id=""miniLabel"">ゲーム画面</div>
        <div id=""me"" class=""marker""><div id=""meDot""></div><div id=""meLabel"">カメレオン</div></div>
        <div id=""aim"" class=""marker""></div>
      </div>
    </div>
    <div id=""statusRow"">
      <div id=""gameState"">ゲーム開始を待っています</div>
      <div id=""net"">接続中…</div>
    </div>
  </section>

  <section id=""bottom"" aria-label=""操作"">
    <div id=""subRow"">
      <div id=""hint"">スマホを傾けて狙う<br>押している間だけ点灯</div>
      <button id=""btnFullscreen"" class=""subBtn"" type=""button"" hidden>
        <svg class=""icon"" viewBox=""0 0 24 24"" aria-hidden=""true""><path d=""M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3""/></svg>
        全画面
      </button>
      <button id=""btnRecenter"" class=""subBtn"" type=""button"">
        <svg class=""icon"" viewBox=""0 0 24 24"" aria-hidden=""true""><circle cx=""12"" cy=""12"" r=""9""/><circle cx=""12"" cy=""12"" r=""2""/><path d=""M12 1v4M12 19v4M1 12h4M19 12h4""/></svg>
        中心をリセット
      </button>
    </div>
    <button id=""btnLight"" type=""button"" aria-pressed=""false"">
      <svg class=""icon"" viewBox=""0 0 24 24"" aria-hidden=""true""><path d=""M7 2h10v4l-2 4v11a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V10L7 6z""/><path d=""M7 6h10""/><path d=""M12 13v3""/></svg>
      <div id=""lightText"">ライトON</div>
      <div id=""lightSub"">押している間だけ照らす</div>
    </button>
  </section>
</div>

<div id=""toast"" role=""status"" aria-live=""polite""></div>
<div id=""offline"" role=""alert"">
  <svg class=""icon"" viewBox=""0 0 24 24"" aria-hidden=""true""><path d=""M12 9v4M12 17h.01""/><path d=""M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z""/></svg>
  ゲームとの接続が切れています。つなぎ直しています…
</div>

<div id=""startOverlay"" class=""overlay"">
  <div class=""tag"">CHAMELEON NIGHT</div>
  <h1>灯り役で参加</h1>
  <ol id=""steps"">
    <li><span class=""num"">1</span>スマホを縦向きに持つ</li>
    <li><span class=""num"">2</span>傾けて、ゲーム画面の狙いを動かす</li>
    <li><span class=""num"">3</span>「ライトON」を押している間だけ、敵を照らせる</li>
  </ol>
  <button id=""btnStart"" type=""button"">タップして始める</button>
  <p id=""startNote"">iPhoneは画面の回転をロックしておくと安定します</p>
</div>
<div id=""fullOverlay"" class=""overlay"" role=""alert"">
  <h1>満員です</h1>
  <p>灯り役は最大7人までです。<br>誰かが抜けると参加できます。</p>
</div>
<div id=""debug"" aria-live=""polite""></div>

<script>
var $ = function (id) { return document.getElementById(id); };
var frameGlow = $('frameGlow'), killNum = $('killNum'), killPlus = $('killPlus'), killStatus = $('killStatus');
var aimEl = $('aim'), mini = $('mini'), colorNameEl = $('colorName');
var gameStateEl = $('gameState'), netEl = $('net'), debugEl = $('debug'), toastEl = $('toast'), offlineEl = $('offline');
var btnStart = $('btnStart'), btnLight = $('btnLight'), lightText = $('lightText'), btnRecenter = $('btnRecenter');

function setDebug(text) { debugEl.textContent = text; }

var toastTimer = null;
function toast(text) {
  toastEl.textContent = text;
  toastEl.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(function () { toastEl.classList.remove('show'); }, 1400);
}

// サーバーから割り当てられた自分の光の色を、画面の縁・ボタン・ミニ画面などすべてに反映する。
// 色だけに頼らず、色の名前も文字で出す(色の見分けがつきにくい人でも、ゲーム画面の自分の光がわかるように)。
var ColorNames = { '#ff6b6b':'赤', '#6bdfff':'水色', '#ffe066':'黄色', '#b388ff':'紫', '#6bff95':'緑',
                   '#ff9f6b':'オレンジ', '#ff6bd6':'ピンク', '#8c8cff':'青' };
var myColor = null;
function applyMyColor(hex) {
  if (!hex || hex === myColor) return;
  myColor = hex;
  document.documentElement.style.setProperty('--c', hex);
  colorNameEl.textContent = ColorNames[hex.toLowerCase()] || hex;
}

// One id per page load so the server can tell multiple simultaneous phones
// apart and give each of them their own light.
var clientId = 'p' + Date.now().toString(36) + Math.random().toString(36).slice(2);

// 画面が自動で消灯すると(特にAndroidで)Wi-Fiが省電力モードに入り、送信が数百ms単位で遅れることがある。
// Screen Wake Lock で画面を点灯させ続けることで、この遅延スパイクをできるだけ避ける。
var wakeLock = null;
function requestWakeLock() {
  if (!('wakeLock' in navigator)) return;
  navigator.wakeLock.request('screen').then(function(lock){ wakeLock = lock; }).catch(function(){});
}
requestWakeLock();
document.addEventListener('visibilitychange', function () {
  if (document.visibilityState === 'visible') requestWakeLock();
  else { lightTouchId = null; setLight(false, '別の画面に切り替わった'); } // 指を離したことが届かない場合に備えて消す
});

var gyroBase = null;      // calibration point; tilt/rotation is measured relative to this
var lastBeta = null, lastAlpha = null;
var gyroListenerAdded = false;
var sensitivity = 35; // degrees of tilt/rotation (from gyroBase) mapped to the full 0..1 range
var aimX = 0.5, aimY = 0.5; // 今狙っている位置(ゲーム画面の左上が0,0。ライトを消している間も更新し続ける)
var lightOn = false;        // 「ライトON」ボタンを押している間だけ true

// alpha (compass-style heading) wraps at 0/360, so a plain subtraction can
// jump by ~360 right when you rotate past the wrap point. This normalizes
// the difference to (-180, 180].
function angleDiff(a, b) {
  var d = a - b;
  while (d > 180) d -= 360;
  while (d < -180) d += 360;
  return d;
}

var last = 0;
var lastMsg = {x:0.5, y:0.5, active:false};
var sendCount = 0, okCount = 0, errCount = 0;
var lastHeard = Date.now(); // 最後にサーバーから何か返ってきた時刻(接続切れの表示に使う)

// ---- 通信: WebSocket(/ws)を優先し、つながらない間はHTTP(/pointer)で送る ----
// WebSocketは接続を開きっぱなしにして、返事を待たずに送れるので、インターネット越しでも遅延が小さく一定になる。
var ws = null, wsReady = false, rttMs = null;
function updateNet() {
  netEl.textContent = (wsReady ? 'WebSocket' : 'HTTP') + (rttMs !== null ? ' ' + Math.round(rttMs) + 'ms' : '') +
    (lightOn || !offReason ? '' : ' / 消灯: ' + offReason);
}

// ---- 撃破数と縁の光 ----
var kills = null, blinking = false;
function restart(el, cls) { el.classList.remove(cls); void el.offsetWidth; el.classList.add(cls); }
// 縁の線を「白 → ライトの色」と0.08秒ずつ、白を3回点滅させる(全部で約0.5秒)。
// 点滅中に続けて倒しても最初からやり直さない(速い点滅が続かないように)。
// 端末で「視差効果を減らす」がオンなら、点滅させずに1秒だけ白くする。
function blinkFrame() {
  if (blinking) return;
  blinking = true;
  var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
  var steps = reduce ? [true] : [true, false, true, false, true, false];
  var i = 0;
  restart(frameGlow, 'flash');
  (function step() {
    if (i >= steps.length) {
      document.body.classList.remove('hit-white');
      frameGlow.classList.remove('flash');
      blinking = false;
      return;
    }
    document.body.classList.toggle('hit-white', steps[i++]);
    setTimeout(step, reduce ? 1000 : 80);
  })();
}
// ---- 倒した時の音 ----
// 音声ファイルは使わず、ブラウザの中で「ピロン」という短い2音を作って鳴らす。
// 高さは自分の光の色ごとに変える(何人かで遊んでいても、自分のスマホの音だとわかりやすいように)。
// スマホのブラウザは、画面を一度タップするまで音を出せないので、「タップして始める」やライトONを押した時に準備する。
// (iPhone はマナーモード(消音スイッチ)だと鳴らない)
var audioCtx = null, lastSoundAt = 0;
var ColorPitch = { '#ff6b6b':523, '#6bdfff':587, '#ffe066':659, '#b388ff':784, '#6bff95':880,
                   '#ff9f6b':1047, '#ff6bd6':1175, '#8c8cff':1319 }; // ド レ ミ ソ ラ ド レ ミ
function unlockAudio() {
  try {
    if (!audioCtx) {
      var AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      audioCtx = new AC();
    }
    if (audioCtx.state === 'suspended') audioCtx.resume();
  } catch (e) {}
}
function tone(freq, start, dur, vol) {
  var osc = audioCtx.createOscillator(), gain = audioCtx.createGain();
  osc.type = 'triangle';
  osc.frequency.setValueAtTime(freq, start);
  gain.gain.setValueAtTime(0.0001, start);
  gain.gain.exponentialRampToValueAtTime(vol, start + 0.008);
  gain.gain.exponentialRampToValueAtTime(0.0001, start + dur);
  osc.connect(gain); gain.connect(audioCtx.destination);
  osc.start(start); osc.stop(start + dur + 0.02);
}
function playKillSound() {
  if (!audioCtx || audioCtx.state !== 'running') return;
  var now = Date.now();
  if (now - lastSoundAt < 90) return; // 同時に何体も倒した時に音が重なりすぎないように
  lastSoundAt = now;
  var base = ColorPitch[(myColor || '').toLowerCase()] || 784;
  var t = audioCtx.currentTime;
  tone(base, t, 0.16, 0.22);
  tone(base * 1.5, t + 0.07, 0.28, 0.2); // 5度上の音を重ねて「ピロン」
}

function onKills(n) {
  if (kills !== null && n > kills) {
    killPlus.textContent = '+' + (n - kills);
    restart(killNum, 'pop'); restart(killPlus, 'show');
    blinkFrame();
    playKillSound();
    if (navigator.vibrate) navigator.vibrate([30, 50, 30]);
    killStatus.textContent = n + '体撃破しました';
  }
  kills = n;
  killNum.textContent = n;
}

// ---- ゲームの様子(カメレオンの位置・プレイ中か) ----
function onGameView(play, px, py) {
  gameStateEl.className = play ? 'play' : '';
  gameStateEl.textContent = play ? 'ゲーム中' : 'ゲーム開始を待っています';
  mini.style.setProperty('--px', px);
  mini.style.setProperty('--py', py);
}

function handleReply(data) {
  if (!data) return;
  lastHeard = Date.now();
  if (data.t === 'pong') { rttMs = performance.now() - data.n; updateNet(); return; }
  okCount++;
  if (data.full) { $('fullOverlay').style.display = 'flex'; return; }
  $('fullOverlay').style.display = 'none';
  if (data.color) applyMyColor(data.color);
  if (typeof data.kills === 'number') onKills(data.kills);
  if (typeof data.px === 'number') onGameView(!!data.play, data.px, data.py);
}
function connectWs() {
  if (!('WebSocket' in window)) return;
  try {
    ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws');
  } catch (e) { ws = null; return; }
  ws.onopen = function () {
    wsReady = true; updateNet();
    send(lastMsg.x, lastMsg.y, lastMsg.active, true); // つながった時点の状態をすぐ知らせる(色ももらう)
  };
  ws.onmessage = function (ev) {
    try { handleReply(JSON.parse(ev.data)); } catch (e) {}
  };
  ws.onclose = function () {
    wsReady = false; ws = null; updateNet();
    setTimeout(connectWs, 2000); // 切れたら2秒後につなぎ直す(その間はHTTPで送る)
  };
  ws.onerror = function () { /* onclose が続けて呼ばれるので、そこで再接続する */ };
}
connectWs();
// 2秒ごとに往復時間を測って表示する。返事が5秒以上ない時は「接続が切れています」を出す
setInterval(function () {
  if (wsReady) ws.send(JSON.stringify({t:'ping', n:performance.now()}));
  offlineEl.classList.toggle('show', Date.now() - lastHeard > 5000);
}, 1000);

function send(x, y, active, force) {
  var now = Date.now();
  // WebSocketなら約30回/秒、HTTPなら約20回/秒まで
  if (!force && now - last < (wsReady ? 33 : 50)) return;
  last = now;
  lastMsg = {x:x, y:y, active:active};
  var body = JSON.stringify({id:clientId, x:x, y:y, active:active});
  if (wsReady) {
    // 回線が詰まって送信待ちが溜まっている時は、古い位置を積み増さずに今回は飛ばす(遅れが雪だるま式に増えないように)
    // ただしライトのON/OFF切り替え(force)は取りこぼすと困るので必ず送る
    if (!force && ws.bufferedAmount > 2048) return;
    sendCount++;
    ws.send(body);
    return;
  }
  sendCount++;
  fetch('/pointer', {
    method: 'POST',
    headers: {'Content-Type':'application/json'},
    body: body
  }).then(function(r){ return r.json(); }).then(function(data){
    handleReply(data);
  }).catch(function(err){
    errCount++;
    netEl.textContent = '送信エラー(' + errCount + '/' + sendCount + ')';
  });
}

// ボタンを押したまま狙いを止めている間も、0.25秒ごとに最後の状態を送り続ける。
// これでゲーム画面に「接続中」と表示され続け、狙いを止めても光が消えない。撃破数などの返事もこれで届く。
setInterval(function () {
  if (Date.now() - last > 250) send(aimX, aimY, lightOn);
}, 250);
send(0.5, 0.5, false); // ページを開いた時点で接続を知らせる

function updateAim() {
  mini.style.setProperty('--ax', aimX);
  mini.style.setProperty('--ay', aimY);
  aimEl.className = 'marker' + (lightOn ? ' on' : '');
}
updateAim();

// ---- ライトON ボタン: 押している間だけ照らす ----
// ボタンに置いた指を(スマホを動かした拍子に)ずらしても、その指を離すまでは点けたままにする。
// そのため、ポインターイベントではなくタッチイベントで「最初にボタンに置いた指」を追いかける。
var lightTouchId = null;
var offReason = ''; // 直前に消えた理由(画面に出して、予期せず消えた時の原因を探せるようにする)
function setLight(on, reason) {
  if (lightOn === on) return;
  lightOn = on;
  if (!on) offReason = reason || '';
  btnLight.className = on ? 'on' : '';
  btnLight.setAttribute('aria-pressed', on ? 'true' : 'false');
  lightText.textContent = on ? '点灯中' : 'ライトON';
  if (on && navigator.vibrate) navigator.vibrate(15);
  updateAim();
  updateNet();
  send(aimX, aimY, on, true); // 切り替えは間引かずにすぐ送る
}
btnLight.addEventListener('touchstart', function (e) {
  e.preventDefault(); // 長押しメニュー・文字選択・スクロール等のブラウザの動作を始めさせない(始まるとタッチが中断される)
  if (lightTouchId === null) {
    lightTouchId = e.changedTouches[0].identifier;
    setLight(true);
  }
  unlockAudio(); // 別のアプリから戻った時などに止まった音を、ここで再開する
}, {passive:false});
function endLightTouch(e, reason) {
  for (var i = 0; i < e.changedTouches.length; i++) {
    if (e.changedTouches[i].identifier === lightTouchId) {
      lightTouchId = null;
      setLight(false, reason);
    }
  }
}
// 指がボタンの外へずれてから離されても届くよう、ページ全体で受ける
document.addEventListener('touchend', function (e) { endLightTouch(e, '指を離した'); }, {passive:false});
document.addEventListener('touchcancel', function (e) { endLightTouch(e, 'タッチ中断(画面の回転など)'); }, {passive:false});
// PCのブラウザで試す時用(マウス・キーボードのスペース/Enter長押し)
btnLight.addEventListener('mousedown', function (e) { e.preventDefault(); unlockAudio(); setLight(true); });
window.addEventListener('mouseup', function () { if (lightTouchId === null) setLight(false, 'マウスを離した'); });
btnLight.addEventListener('keydown', function (e) { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); setLight(true); } });
btnLight.addEventListener('keyup', function (e) { if (e.key === ' ' || e.key === 'Enter') setLight(false, 'キーを離した'); });
btnLight.addEventListener('contextmenu', function (e) { e.preventDefault(); }); // 長押しメニューを出さない
window.addEventListener('blur', function () { lightTouchId = null; setLight(false, '別の画面に切り替わった'); });

// The phone's top edge acts like the tip of the flashlight: whatever
// orientation it has when the sensor starts (or after tapping the recenter
// button) becomes dead-center, and tilting the top of the phone away from
// that pose aims the light in that direction, proportionally.
// This is deliberately absolute (not re-centered on every button press) so
// the mapping between physical aim and light position stays consistent.
var gotOrientationEvent = false;

function onOrientation(e) {
  if (e.beta === null || e.alpha === null) {
    setDebug('傾きセンサーの値が取得できません(この端末では使えない可能性があります)');
    return;
  }
  if (!gotOrientationEvent) setDebug('');
  gotOrientationEvent = true;
  lastBeta = e.beta;
  lastAlpha = e.alpha;
  if (gyroBase === null) gyroBase = { beta: e.beta, alpha: e.alpha };

  // tan()-based mapping so this behaves like an actual ray extending from
  // the phone's tip, hitting a virtual wall out in front: near dead-center
  // it moves slowly, and it sweeps faster the further off-axis you go -
  // like aiming a remote/laser pointer, not a linear joystick.
  // Left/right comes from alpha: lay the phone flat and turn it clockwise/
  // counter-clockwise like a dial. Up/down still comes from beta (tilt).
  var maxDeg = 80; // clamp before tan() so near-90 degrees can't blow up
  var dBeta = Math.max(-maxDeg, Math.min(maxDeg, e.beta - gyroBase.beta));
  var dAlpha = Math.max(-maxDeg, Math.min(maxDeg, angleDiff(e.alpha, gyroBase.alpha)));
  var tanSens = Math.tan(sensitivity * Math.PI / 180);
  var x = 0.5 - (Math.tan(dAlpha * Math.PI / 180) / tanSens) * 0.5;
  var y = 0.5 - (Math.tan(dBeta * Math.PI / 180) / tanSens) * 0.5;
  aimX = Math.max(0, Math.min(1, x));
  aimY = Math.max(0, Math.min(1, y));
  updateAim();
  send(aimX, aimY, lightOn);
}

function startGyro() {
  if (!gyroListenerAdded) {
    window.addEventListener('deviceorientation', onOrientation);
    gyroListenerAdded = true;
  }
  setTimeout(function () {
    if (!gotOrientationEvent) {
      setDebug('傾きセンサーのデータが届いていません。別のブラウザ(Safari等)で試してください。ライトは画面中央を照らします。');
    }
  }, 5000);
}

// ---- 全画面 ----
// 「タップして始める」で全画面にする(アドレスバー等が消えて、画面いっぱいに使える)。
// 全画面にできれば縦向きにも固定する(スマホを傾けた拍子に画面が回転し、押している指が「中断」扱いになって
// ライトが消えるのを防ぐ)。Android の Chrome などで使える。
// iPhone の Safari はWebページを全画面にできない(動画だけ)ので、ボタンも出さない。
// その代わり、画面の回転はコントロールセンターの「画面縦向きのロック」で止めてもらう。
var docEl = document.documentElement;
var fsRequest = docEl.requestFullscreen || docEl.webkitRequestFullscreen;
var canFullscreen = !!fsRequest && (document.fullscreenEnabled || document.webkitFullscreenEnabled);
var btnFullscreen = $('btnFullscreen'), subRow = $('subRow');
function isFullscreen() { return !!(document.fullscreenElement || document.webkitFullscreenElement); }
function enterFullscreen() {
  if (!canFullscreen || isFullscreen()) return;
  try {
    var p = fsRequest.call(docEl, { navigationUI: 'hide' });
    if (p && p.then) {
      p.then(function () {
        if (screen.orientation && screen.orientation.lock) return screen.orientation.lock('portrait');
      }).catch(function () {});
    }
  } catch (err) {}
}
// 全画面から抜けた時(画面の端からスワイプした等)は「全画面」ボタンを出して、すぐ戻れるようにする
function updateFullscreenButton() {
  var offer = canFullscreen && !isFullscreen() && $('startOverlay').style.display === 'none';
  btnFullscreen.hidden = !offer;
  subRow.classList.toggle('fsOffer', offer);
}
document.addEventListener('fullscreenchange', updateFullscreenButton);
document.addEventListener('webkitfullscreenchange', updateFullscreenButton);
btnFullscreen.addEventListener('click', function (e) { e.preventDefault(); enterFullscreen(); });

// 「タップして始める」で全画面にし、iPhone(iOS 13以降)は傾きセンサーの許可をもらう。
var hasSensorApi = (typeof DeviceOrientationEvent !== 'undefined');
var needsPermission = hasSensorApi && (typeof DeviceOrientationEvent.requestPermission === 'function');
if (hasSensorApi && !needsPermission) startGyro(); // Androidはすぐ狙えるようにしておく
if (!/iPhone|iPad|iPod/.test(navigator.userAgent)) $('startNote').style.display = 'none';
btnStart.addEventListener('click', function (e) {
  e.preventDefault();
  enterFullscreen();
  requestWakeLock();
  unlockAudio();
  $('startOverlay').style.display = 'none';
  setTimeout(updateFullscreenButton, 600); // 全画面にできなかった時(断られた等)はボタンを出す
  btnLight.focus({preventScroll:true});
  if (!hasSensorApi) { setDebug('この端末・ブラウザには傾きセンサーがありません。ライトは画面中央を照らします。'); return; }
  if (!needsPermission) return;
  DeviceOrientationEvent.requestPermission().then(function (state) {
    if (state === 'granted') startGyro();
    else setDebug('傾きセンサーが許可されませんでした。ページを開き直して「許可」を選んでください。');
  }).catch(function (err) {
    setDebug('傾きセンサーを使えませんでした(' + err + ')');
  });
});

btnRecenter.addEventListener('click', function(e){
  e.preventDefault();
  if (lastBeta === null) { toast('傾きセンサーの準備中です'); return; }
  gyroBase = { beta: lastBeta, alpha: lastAlpha };
  if (navigator.vibrate) navigator.vibrate(10);
  toast('今の向きを中心にしました');
});
</script>
</body></html>";
}
