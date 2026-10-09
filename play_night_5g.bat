@echo off
REM 5G・別のWi-Fiのスマホからも参加できる版(Cloudflare のトンネルを使う)。それ以外は play_night.bat と同じ。
REM インターネットにつながっているPCで使うこと。URLの準備に10～30秒ほどかかり、準備できるとゲーム画面のQRが切り替わる。

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
REM -tunnel: ゲームが Cloudflare のトンネル(cloudflared)を起動し、画面のQRをインターネット用のURLにする
REM          (5G・別のWi-Fiのスマホからも参加できる。cloudflared は winget install --id Cloudflare.cloudflared で入れる)
set CF_ARG=
if exist "%PROJECT_DIR%tools\cloudflared.exe" set CF_ARG=-cloudflared "%PROJECT_DIR%tools\cloudflared.exe"
start "" "%PROJECT_DIR%Build\NightForest\ChameleonNight.exe" -tunnel %CF_ARG%

REM ゲームウィンドウが別のウィンドウの裏に隠れて見えないことがあるため、
REM 少し待ってから前面に出す。
timeout /t 2 /nobreak >nul
powershell -NoProfile -Command "$p = Get-Process ChameleonNight -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class W { [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr h); }'; [W]::SetForegroundWindow($p.MainWindowHandle) }"

echo 起動しました。ウィンドウが見当たらない場合はタスクバーの「ChameleonNight_Prototype」を確認してください。
