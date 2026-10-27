@echo off
REM 夜の森モードをビルドして起動するショートカット(5G版)。ダブルクリックするだけで最新のコードで遊べる。
REM ゲーム画面のQRは、5G・別のWi-Fiのスマホからも参加できるURL(Cloudflare のトンネル)になる。
REM インターネットがない・cloudflared が入っていない時は、自動で同じWi-Fi用のQRになる。
REM インターネットがない会場では play_night_wifi.bat を使うと、QRがすぐ出る。

setlocal
set UNITY_EXE="C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe"
set PROJECT_DIR=%~dp0
set LOGFILE=%PROJECT_DIR%unity_build_night.log

echo [1/2] Unityでビルド中...(1分ほどかかります。ウィンドウが真っ黒のまま進んでいるように見えても正常です)
echo       ログ: %LOGFILE%
%UNITY_EXE% -batchmode -nographics -quit ^
  -projectPath "%PROJECT_DIR%." ^
  -executeMethod BuildScript.BuildNight ^
  -logFile "%LOGFILE%"

if %ERRORLEVEL% NEQ 0 (
    echo ビルドに失敗しました。%LOGFILE% を確認してください。
    pause
    exit /b 1
)

echo [2/2] 起動します...
REM tools\cloudflared.exe を置いてあればそれを使う(なければ winget で入れたもの等を自動で探す)
set CF_ARG=
if exist "%PROJECT_DIR%tools\cloudflared.exe" set CF_ARG=-cloudflared "%PROJECT_DIR%tools\cloudflared.exe"
start "" "%PROJECT_DIR%Build\NightForest\ChameleonNight.exe" %CF_ARG%

REM ゲームウィンドウが別のウィンドウの裏に隠れて見えないことがあるため、
REM 少し待ってから前面に出す。
timeout /t 2 /nobreak >nul
powershell -NoProfile -Command "$p = Get-Process ChameleonNight -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class W { [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr h); }'; [W]::SetForegroundWindow($p.MainWindowHandle) }"

echo 起動しました。ウィンドウが見当たらない場合はタスクバーの「カメレオンサバイバー」を確認してください。
