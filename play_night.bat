@echo off
REM 光の球収集版(マップ選択・光の球・門)を、Unityエディタを開かずにビルドして即起動するショートカット。
REM ダブルクリックするだけで最新のコードでビルドされます(数十秒～数分かかります)。

setlocal
set UNITY_EXE="C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe"
set PROJECT_DIR=%~dp0
set LOGFILE=%PROJECT_DIR%unity_build_night.log

echo [1/2] Unity でビルド中... (ログ: %LOGFILE%)
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
start "" "%PROJECT_DIR%Build\NightForest\ChameleonNight.exe"
