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
    }

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

    public struct PointerSample { public Vector2 pos; public Color color; }

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

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
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
                            string colorHex = HandlePointerPost(body);
                            string json = colorHex != null
                                ? "{\"ok\":true,\"color\":\"" + colorHex + "\"}"
                                : "{\"ok\":false,\"full\":true}";
                            WriteResponse(stream, "200 OK", "application/json", json);
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

    // 戻り値はこのクライアントに割り当てられた色(#rrggbb)。満員で新規参加を断った場合は null。
    string HandlePointerPost(string body) => HandlePointerPost(body, out _);

    string HandlePointerPost(string body, out string clientId)
    {
        clientId = null;
        try
        {
            PointerMsg msg = JsonUtility.FromJson<PointerMsg>(body);
            if (msg == null || string.IsNullOrEmpty(msg.id)) return HexOf(0);
            clientId = msg.id;
            lock (dataLock)
            {
                if (!pointers.TryGetValue(msg.id, out PointerEntry entry))
                {
                    if (pointers.Count >= MaxParticipants) return null; // 満員(最大7人)
                    entry = new PointerEntry { colorIndex = nextColorIndex++ };
                    pointers[msg.id] = entry;
                }
                entry.x = Mathf.Clamp01(msg.x);
                entry.y = Mathf.Clamp01(msg.y);
                entry.active = msg.active;
                entry.lastReceiveTimeMs = Environment.TickCount;
                return HexOf(entry.colorIndex);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("PointerServer bad payload: " + e.Message);
            return HexOf(0);
        }
    }

    // ================= WebSocket (/ws) =================
    // HTTPの /pointer は1回ごとに「送る→返事を待つ」になるので、インターネット越し(Cloudflare Tunnel 等)
    // だと往復の待ちが積み重なって光がカクつく。WebSocketは1本の接続を開きっぱなしにして、
    // 返事を待たずに小さなメッセージを流せるので、遅延が小さく一定になる。
    // メッセージの中身は /pointer と同じJSON。返事は色が決まった時・満員になった時など変化した時だけ返す。
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

                    string colorHex = HandlePointerPost(text, out string id);
                    if (id != null) clientId = id;
                    string reply = colorHex != null
                        ? "{\"ok\":true,\"color\":\"" + colorHex + "\"}"
                        : "{\"ok\":false,\"full\":true}";
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
                    result.Add(new PointerSample { pos = new Vector2(kv.Value.x, kv.Value.y), color = ColorOf(kv.Value.colorIndex) });
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
<meta name=""viewport"" content=""width=device-width, initial-scale=1, user-scalable=no"">
<title>カメレオンナイト - 灯り</title>
<style>
  html,body { margin:0; padding:0; width:100%; height:100%; background:#111; overflow:hidden;
              touch-action:none; -webkit-user-select:none; user-select:none;
              -webkit-touch-callout:none; font-family:sans-serif; }
  #msg { position:absolute; top:10px; left:0; right:0; text-align:center;
         color:#8f8; font-size:13px; pointer-events:none; padding:0 8px; }
  #dot { position:absolute; width:40px; height:40px; margin:-20px 0 0 -20px;
         border-radius:50%; background:radial-gradient(circle, #ffdd88, transparent 70%);
         opacity:0; pointer-events:none; }
  #bar { position:absolute; bottom:20px; left:0; right:0; display:flex; flex-direction:column;
         align-items:center; gap:12px; }
  #bar button { padding:10px 16px; font-size:14px; border-radius:8px; border:1px solid #666;
                background:#222; color:#ccc; }
  #btnStart { padding:16px 24px !important; font-size:18px !important; background:#3a8a4a !important;
              color:#fff !important; border-color:#3a8a4a !important; }
  #btnLight { width:min(78vw, 340px); height:min(42vh, 240px); border-radius:28px !important;
              font-size:34px !important; font-weight:bold; letter-spacing:2px;
              background:#2a2a2a !important; color:#eee !important; border:3px solid #777 !important;
              touch-action:none; transition:background .08s, box-shadow .08s, transform .08s; }
  #btnLight.on { background:#fff2c0 !important; color:#222 !important; border-color:#fff !important;
                 box-shadow:0 0 40px 10px rgba(255,240,180,0.6); transform:scale(0.97); }
  #debug { position:absolute; top:54px; left:0; right:0; text-align:center;
           color:#ff8; font-size:11px; font-family:monospace; white-space:pre-wrap;
           pointer-events:none; padding:0 8px; }
  #colorBadge { position:absolute; top:104px; left:0; right:0; display:flex;
                flex-direction:column; align-items:center; gap:4px; pointer-events:none; }
  #colorSwatch { width:34px; height:34px; border-radius:50%; border:2px solid rgba(255,255,255,0.8);
                 box-shadow:0 0 14px 2px currentColor; background:#fff2c0; transition:background .2s; }
  #colorLabel { color:#ddd; font-size:11px; }
</style>
</head>
<body>
<div id=""msg"">スマホを傾けて狙い、「ライトON」を押している間だけ照らせます</div>
<div id=""debug""></div>
<div id=""colorBadge""><div id=""colorSwatch""></div><div id=""colorLabel"">あなたの光の色</div></div>
<div id=""dot""></div>
<div id=""bar"">
  <button id=""btnStart"" style=""display:none"">タップして始める</button>
  <button id=""btnRecenter"">中心をリセット</button>
  <button id=""btnLight"">ライトON</button>
</div>
<script>
var dot = document.getElementById('dot');
var msg = document.getElementById('msg');
var debugEl = document.getElementById('debug');
var btnStart = document.getElementById('btnStart');
var btnLight = document.getElementById('btnLight');
var btnRecenter = document.getElementById('btnRecenter');
var colorSwatch = document.getElementById('colorSwatch');

// サーバーから割り当てられた自分の光の色を反映する(光の点・スウォッチ両方)
function applyMyColor(hex) {
  if (!hex) return;
  colorSwatch.style.background = hex;
  colorSwatch.style.color = hex;
  dot.style.background = 'radial-gradient(circle, ' + hex + ', transparent 70%)';
}

function setDebug(text) { debugEl.textContent = text; }

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
var aimX = 0.5, aimY = 0.5; // 今狙っている位置(ライトを消している間も更新し続ける)
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

// ---- 通信: WebSocket(/ws)を優先し、つながらない間はHTTP(/pointer)で送る ----
// WebSocketは接続を開きっぱなしにして、返事を待たずに送れるので、インターネット越しでも遅延が小さく一定になる。
var ws = null, wsReady = false, rttMs = null;
function transportLabel() {
  return (wsReady ? 'WebSocket' : 'HTTP') + (rttMs !== null ? ' / 往復 ' + Math.round(rttMs) + 'ms' : '');
}
function statusLine() {
  return '通信: ' + transportLabel() + ' / ライト: ' + (lightOn ? 'ON' : 'OFF' + (offReason ? '(' + offReason + ')' : ''));
}
function handleReply(data) {
  if (!data) return;
  if (data.t === 'pong') { rttMs = performance.now() - data.n; return; }
  okCount++;
  if (data.full) {
    msg.textContent = '満員です(最大7人)。誰か抜けるまでお待ちください。';
    dot.style.opacity = 0;
    return;
  }
  if (data.color) applyMyColor(data.color);
}
function connectWs() {
  if (!('WebSocket' in window)) return;
  try {
    ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws');
  } catch (e) { ws = null; return; }
  ws.onopen = function () {
    wsReady = true;
    send(lastMsg.x, lastMsg.y, lastMsg.active, true); // つながった時点の状態をすぐ知らせる(色ももらう)
  };
  ws.onmessage = function (ev) {
    try { handleReply(JSON.parse(ev.data)); } catch (e) {}
  };
  ws.onclose = function () {
    wsReady = false; ws = null;
    setTimeout(connectWs, 2000); // 切れたら2秒後につなぎ直す(その間はHTTPで送る)
  };
  ws.onerror = function () { /* onclose が続けて呼ばれるので、そこで再接続する */ };
}
connectWs();
// 2秒ごとに往復時間を測って表示する
setInterval(function () {
  if (wsReady) ws.send(JSON.stringify({t:'ping', n:performance.now()}));
}, 2000);

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
    setDebug('送信エラー(' + errCount + '/' + sendCount + '): ' + err);
  });
}

// ボタンを押したまま狙いを止めている間も、0.25秒ごとに最後の状態を送り続ける。
// これでゲーム画面に「接続中」と表示され続け、狙いを止めても光が消えない。
setInterval(function () {
  if (Date.now() - last > 250) send(aimX, aimY, lightOn);
}, 250);
send(0.5, 0.5, false); // ページを開いた時点で接続を知らせる

function updateDot() {
  dot.style.left = (aimX * window.innerWidth) + 'px';
  dot.style.top = (aimY * window.innerHeight) + 'px';
  dot.style.opacity = lightOn ? 1 : 0;
}

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
  btnLight.textContent = on ? '点灯中' : 'ライトON';
  if (on && navigator.vibrate) navigator.vibrate(15);
  updateDot();
  send(aimX, aimY, on, true); // 切り替えは間引かずにすぐ送る
  setDebug(statusLine());
}
btnLight.addEventListener('touchstart', function (e) {
  e.preventDefault(); // 長押しメニュー・文字選択・スクロール等のブラウザの動作を始めさせない(始まるとタッチが中断される)
  if (lightTouchId === null) {
    lightTouchId = e.changedTouches[0].identifier;
    setLight(true);
  }
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
// PCのブラウザで試す時用(マウス)
btnLight.addEventListener('mousedown', function (e) { e.preventDefault(); setLight(true); });
window.addEventListener('mouseup', function () { if (lightTouchId === null) setLight(false, 'マウスを離した'); });
btnLight.addEventListener('contextmenu', function (e) { e.preventDefault(); }); // 長押しメニューを出さない
window.addEventListener('blur', function () { lightTouchId = null; setLight(false, '別の画面に切り替わった'); });
window.addEventListener('orientationchange', function () { setDebug(statusLine() + '\n画面が回転しました(画面の回転をロックすると安定します)'); });

// The phone's top edge acts like the tip of the flashlight: whatever
// orientation it has when the sensor starts (or after tapping the recenter
// button) becomes dead-center, and tilting the top of the phone away from
// that pose aims the light in that direction, proportionally.
// This is deliberately absolute (not re-centered on every button press) so
// the mapping between physical aim and light position stays consistent.
var gotOrientationEvent = false;

function onOrientation(e) {
  if (e.beta === null || e.alpha === null) {
    setDebug('センサーイベントは届いていますが beta/alpha が null です(この端末では取得できない可能性)');
    return;
  }
  gotOrientationEvent = true;
  lastBeta = e.beta;
  lastAlpha = e.alpha;
  if (gyroBase === null) gyroBase = { beta: e.beta, alpha: e.alpha };
  setDebug(statusLine() + '\n傾き: beta=' + e.beta.toFixed(1) + ' alpha=' + e.alpha.toFixed(1) +
            ' (基準 beta=' + gyroBase.beta.toFixed(1) + ' alpha=' + gyroBase.alpha.toFixed(1) + ')');

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
  updateDot();
  send(aimX, aimY, lightOn);
}

function startGyro() {
  if (!gyroListenerAdded) {
    window.addEventListener('deviceorientation', onOrientation);
    gyroListenerAdded = true;
  }
  if (typeof DeviceOrientationEvent.requestPermission === 'function') btnStart.style.display = 'none';
  setTimeout(function () {
    if (!gotOrientationEvent) {
      setDebug('⚠ 5秒たっても傾きセンサーのデータが届いていません。\n' +
                '低電力モード/画面ロック回転オフ/機内モード等を確認するか、\n' +
                '別のブラウザ(Safari等)で試してください。(ライトは画面中央を照らします)');
    }
  }, 5000);
}

// スマホを傾けると画面が横向きに回転し、その瞬間に押している指が「中断」扱いになってライトが消える。
// Android は全画面にすると縦向きに固定できるので、「タップして始める」の時に固定する。
// (iPhone はWebページから固定できないので、コントロールセンターの「画面縦向きのロック」を使ってもらう)
function lockPortrait() {
  var el = document.documentElement;
  var req = el.requestFullscreen || el.webkitRequestFullscreen;
  if (!req || !screen.orientation || !screen.orientation.lock) return;
  try {
    var p = req.call(el);
    if (p && p.then) p.then(function () { return screen.orientation.lock('portrait'); }).catch(function () {});
  } catch (err) {}
}

// 「タップして始める」で、iPhone(iOS 13以降)は傾きセンサーの許可をもらい、Androidは縦向きに固定する。
if (typeof DeviceOrientationEvent === 'undefined') {
  setDebug('この端末・ブラウザには傾きセンサーがありません(ライトは画面中央を照らします)');
} else {
  var needsPermission = (typeof DeviceOrientationEvent.requestPermission === 'function');
  if (!needsPermission) startGyro(); // Androidはすぐ狙えるようにしておく(固定はボタンを押した時)
  btnStart.style.display = '';
  btnStart.addEventListener('click', function (e) {
    e.preventDefault();
    lockPortrait();
    if (!needsPermission) { btnStart.style.display = 'none'; return; }
    DeviceOrientationEvent.requestPermission().then(function (state) {
      if (state === 'granted') startGyro();
      else setDebug('傾きセンサーが許可されませんでした(' + state + ')。ページを開き直して許可してください。');
    }).catch(function (err) {
      setDebug('requestPermission() でエラー: ' + err + '\n(HTTPでの利用が拒否されている可能性があります)');
    });
  });
}

btnRecenter.addEventListener('click', function(e){
  e.preventDefault();
  if (lastBeta !== null) gyroBase = { beta: lastBeta, alpha: lastAlpha };
});
</script>
</body></html>";
}
