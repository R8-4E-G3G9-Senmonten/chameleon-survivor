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
// phone that reports touch position, and accepts POST /pointer updates.
// This exists purely to test whether "phone as a pointer over LAN" is
// feasible with acceptable latency for the cooperative-player flashlight.
//
// Runs both plain HTTP (port) and HTTPS with a self-signed cert (httpsPort)
// side by side. Touch mode works fine over plain HTTP. Gyro mode needs
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

    // スマホの画面でもゲームと同じ RocknRoll One を使うため、StreamingAssets のフォントを配る
    static byte[] fontBytes;
    const string FontFile = "RocknRollOne-Regular.ttf";

    void Start()
    {
        try { fontBytes = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, FontFile)); }
        catch (Exception e) { Debug.LogWarning("PointerServer: フォントを読み込めませんでした(" + e.Message + ")。スマホは端末のフォントで表示します。"); }
        string ip = GetLocalIPv4();
        LocalIp = ip;

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
                        ReadRequest(stream, out string requestLine, out string body);
                        if (string.IsNullOrEmpty(requestLine)) break;

                        string[] parts = requestLine.Split(' ');
                        string method = parts.Length > 0 ? parts[0] : "GET";
                        string path = parts.Length > 1 ? parts[1] : "/";

                        if (method == "POST" && path == "/pointer")
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
                        else if (method == "GET" && path == "/" + FontFile && fontBytes != null)
                        {
                            WriteResponse(stream, "200 OK", "font/ttf", fontBytes, "Cache-Control: max-age=86400\r\n");
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

    void ReadRequest(Stream stream, out string requestLine, out string body)
    {
        requestLine = null;
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

        int contentLength = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
            }
        }

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
    string HandlePointerPost(string body)
    {
        try
        {
            PointerMsg msg = JsonUtility.FromJson<PointerMsg>(body);
            if (msg == null || string.IsNullOrEmpty(msg.id)) return HexOf(0);
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

    void WriteResponse(Stream stream, string status, string contentType, string content)
        => WriteResponse(stream, status, contentType, Encoding.UTF8.GetBytes(content), "");

    void WriteResponse(Stream stream, string status, string contentType, byte[] bodyBytes, string extraHeaders)
    {
        string header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            extraHeaders +
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
    }

    void OnDestroy() => StopServer();
    void OnApplicationQuit() => StopServer();

    const string PhoneHtml = @"<!DOCTYPE html>
<html lang=""ja""><head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1, user-scalable=no"">
<title>カメレオンナイト - 灯り</title>
<style>
  @font-face { font-family:'RocknRoll One'; src:url('/RocknRollOne-Regular.ttf') format('truetype'); font-display:swap; }
  html,body { margin:0; padding:0; width:100%; height:100%; background:#111; overflow:hidden;
              touch-action:none; -webkit-user-select:none; user-select:none;
              font-family:'RocknRoll One', sans-serif; }
  button { font-family:inherit; }
  #msg { position:absolute; top:10px; left:0; right:0; text-align:center;
         color:#8f8; font-size:16px; pointer-events:none; padding:0 8px; }
  #dot { position:absolute; width:40px; height:40px; margin:-20px 0 0 -20px;
         border-radius:50%; background:radial-gradient(circle, #ffdd88, transparent 70%);
         opacity:0; pointer-events:none; }
  #bar { position:absolute; bottom:16px; left:0; right:0; display:flex;
         justify-content:center; gap:10px; }
  #bar button { padding:12px 18px; font-size:16px; border-radius:8px; border:1px solid #666;
                background:#222; color:#ccc; }
  #bar button.active { background:#3a8a4a; color:#fff; border-color:#3a8a4a; }
  #debug { position:absolute; top:34px; left:0; right:0; text-align:center;
           color:#ff8; font-size:11px; font-family:monospace; white-space:pre-wrap;
           pointer-events:none; padding:0 8px; }
  #colorBadge { position:absolute; top:56px; left:0; right:0; display:flex;
                flex-direction:column; align-items:center; gap:4px; pointer-events:none; }
  #colorSwatch { width:34px; height:34px; border-radius:50%; border:2px solid rgba(255,255,255,0.8);
                 box-shadow:0 0 14px 2px currentColor; background:#fff2c0; transition:background .2s; }
  #colorLabel { color:#ddd; font-size:14px; }
</style>
</head>
<body>
<div id=""msg"">画面を指でなぞって照らしてください</div>
<div id=""debug""></div>
<div id=""colorBadge""><div id=""colorSwatch""></div><div id=""colorLabel"">あなたの光の色</div></div>
<div id=""dot""></div>
<div id=""bar"">
  <button id=""btnTouch"" class=""active"">タッチで操作</button>
  <button id=""btnGyro"">傾きで操作</button>
  <button id=""btnRecenter"" style=""display:none"">中心をリセット</button>
</div>
<script>
var dot = document.getElementById('dot');
var msg = document.getElementById('msg');
var debugEl = document.getElementById('debug');
var btnTouch = document.getElementById('btnTouch');
var btnGyro = document.getElementById('btnGyro');
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

(function () {
  var hasApi = (typeof DeviceOrientationEvent !== 'undefined');
  var hasRequestPermission = hasApi && (typeof DeviceOrientationEvent.requestPermission === 'function');
  setDebug('センサーAPI: ' + (hasApi ? 'あり' : 'なし') +
            ' / 許可リクエスト: ' + (hasRequestPermission ? '必要(iOS)' : '不要') +
            ' / HTTPS: ' + (location.protocol === 'https:' ? 'あり' : 'なし(http)'));
})();

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
});

var mode = 'touch';
var gyroBase = null;      // calibration point; tilt/rotation is measured relative to this
var lastBeta = null, lastAlpha = null;
var gyroListenerAdded = false;
var sensitivity = 35; // degrees of tilt/rotation (from gyroBase) mapped to the full 0..1 range

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
function send(x, y, active) {
  var now = Date.now();
  if (now - last < 50) return; // throttle ~20Hz
  last = now;
  lastMsg = {x:x, y:y, active:active};
  sendCount++;
  fetch('/pointer', {
    method: 'POST',
    headers: {'Content-Type':'application/json'},
    body: JSON.stringify({id:clientId, x:x, y:y, active:active})
  }).then(function(r){ return r.json(); }).then(function(data){
    okCount++;
    if (data && data.full) {
      msg.textContent = '満員です(最大7人)。誰か抜けるまでお待ちください。';
      dot.style.opacity = 0;
      return;
    }
    if (data && data.color) applyMyColor(data.color);
    if (mode === 'touch') setDebug('送信中: x=' + x.toFixed(2) + ' y=' + y.toFixed(2) + ' (成功 ' + okCount + '/' + sendCount + ')');
  }).catch(function(err){
    errCount++;
    setDebug('送信エラー(' + errCount + '/' + sendCount + '): ' + err);
  });
}

// 触っていない間も、指を止めている間も、0.25秒ごとに最後の状態を送り続ける。
// これでゲーム画面に「接続中」と表示され続け、指を止めても光が消えない。
setInterval(function () {
  if (Date.now() - last > 250) send(lastMsg.x, lastMsg.y, lastMsg.active);
}, 250);
send(0.5, 0.5, false); // ページを開いた時点で接続を知らせる

function showAt(x, y, active) {
  dot.style.left = (x * window.innerWidth) + 'px';
  dot.style.top = (y * window.innerHeight) + 'px';
  dot.style.opacity = active ? 1 : 0;
  send(x, y, active);
}

// Taps on the mode buttons must never be swallowed as light input.
function isOnBar(e) {
  return !!(e.target && e.target.closest && e.target.closest('#bar'));
}

function handleTouch(e, active) {
  if (mode !== 'touch' || isOnBar(e)) return;
  e.preventDefault();
  var t = e.touches && e.touches.length ? e.touches[0] : e;
  showAt(t.clientX / window.innerWidth, t.clientY / window.innerHeight, active);
}
document.body.addEventListener('touchstart', function(e){ handleTouch(e, true); }, {passive:false});
document.body.addEventListener('touchmove', function(e){ handleTouch(e, true); }, {passive:false});
document.body.addEventListener('touchend', function(e){ if (mode==='touch' && !isOnBar(e)){ send(0.5,0.5,false); dot.style.opacity=0; } }, {passive:false});
document.body.addEventListener('mousedown', function(e){ handleTouch(e, true); });
document.body.addEventListener('mousemove', function(e){ if (e.buttons) handleTouch(e, true); });
document.body.addEventListener('mouseup', function(e){ if (mode==='touch' && !isOnBar(e)){ send(0.5,0.5,false); dot.style.opacity=0; } });

// The phone's top edge acts like the tip of the flashlight: whatever
// orientation it has when first switching to gyro mode (or after tapping
// the recenter button) becomes dead-center, and tilting the top of the
// phone away from that pose aims the light in that direction, proportionally.
// This is deliberately absolute (not re-centered every activation) so the
// mapping between physical aim and light position stays consistent.
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
  if (mode === 'gyro') {
    setDebug('受信中: beta=' + e.beta.toFixed(1) + ' alpha=' + e.alpha.toFixed(1) +
              ' (基準 beta=' + gyroBase.beta.toFixed(1) + ' alpha=' + gyroBase.alpha.toFixed(1) + ')');
  }
  if (mode !== 'gyro') return;

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
  x = Math.max(0, Math.min(1, x));
  y = Math.max(0, Math.min(1, y));
  showAt(x, y, true);
}

function setMode(m) {
  mode = m;
  btnTouch.className = (m === 'touch') ? 'active' : '';
  btnGyro.className = (m === 'gyro') ? 'active' : '';
  btnRecenter.style.display = (m === 'gyro') ? 'inline-block' : 'none';
  msg.textContent = (m === 'gyro')
    ? 'スマホを水平に持ち、左右は回転(時計回り/反時計回り)、上下は傾きで狙ってください(必要なら「中心をリセット」)'
    : '画面を指でなぞって照らしてください';
  if (m !== 'gyro') { dot.style.opacity = 0; send(0.5, 0.5, false); }
}

btnTouch.addEventListener('click', function(e){ e.preventDefault(); setMode('touch'); });

btnGyro.addEventListener('click', function(e){
  e.preventDefault();
  function start() {
    if (!gyroListenerAdded) {
      window.addEventListener('deviceorientation', onOrientation);
      gyroListenerAdded = true;
    }
    setMode('gyro');
    setDebug('許可OK。センサーデータ待ち…');
    setTimeout(function () {
      if (mode === 'gyro' && !gotOrientationEvent) {
        setDebug('⚠ 許可はされましたが、5秒たってもセンサーデータが届いていません。\n' +
                  '低電力モード/画面ロック回転オフ/機内モード等を確認するか、\n' +
                  '別のブラウザ(Safari等)で試してください。');
      }
    }, 5000);
  }
  if (typeof DeviceOrientationEvent === 'undefined') {
    setDebug('この端末・ブラウザには DeviceOrientationEvent 自体がありません(傾き操作は使えません)');
    return;
  }
  if (typeof DeviceOrientationEvent.requestPermission === 'function') {
    DeviceOrientationEvent.requestPermission().then(function(state){
      setDebug('requestPermission() の結果: ' + state);
      if (state === 'granted') start();
    }).catch(function(err){
      setDebug('requestPermission() でエラー: ' + err + '\n(HTTPでの利用が拒否されている可能性があります)');
    });
  } else {
    start();
  }
});

btnRecenter.addEventListener('click', function(e){
  e.preventDefault();
  if (lastBeta !== null) gyroBase = { beta: lastBeta, alpha: lastAlpha };
});
</script>
</body></html>";
}
