@echo off
REM 夜の森モードをビルドして起動するショートカット(同じWi-Fi版)。インターネットがない会場向け。
REM 5G用のURLを作らないので、QRがすぐ出る。スマホはこのPCと同じWi-Fiにつないでから読み取る。

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
start "" "%PROJECT_DIR%Build\NightForest\ChameleonNight.exe" -noTunnel

REM ゲームウィンドウが別のウィンドウの裏に隠れて見えないことがあるため、
REM 少し待ってから前面に出す。
timeout /t 2 /nobreak >nul
powershell -NoProfile -Command "$p = Get-Process ChameleonNight -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class W { [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr h); }'; [W]::SetForegroundWindow($p.MainWindowHandle) }"

echo 起動しました。ウィンドウが見当たらない場合はタスクバーの「カメレオンサバイバー」を確認してください。
