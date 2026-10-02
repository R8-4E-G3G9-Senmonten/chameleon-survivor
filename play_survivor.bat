@echo off
REM サバイバーモードを、Unityエディタを開かずにビルドして即起動するショートカット。
REM コード/シーンを直したあとに動作確認したいときは、これをダブルクリックするだけでよい。
REM (初回や変更後はビルドに数十秒～数分かかります。実行中はコンソールが開いたままになります)

setlocal
set UNITY_EXE="C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe"
set PROJECT_DIR=%~dp0
set LOGFILE=%PROJECT_DIR%unity_build_survivor.log

echo [1/2] Unity でビルド中... (ログ: %LOGFILE%)
%UNITY_EXE% -batchmode -nographics -quit ^
  -projectPath "%PROJECT_DIR%." ^
  -executeMethod BuildScript.BuildSurvivor ^
  -logFile "%LOGFILE%"

if %ERRORLEVEL% NEQ 0 (
    echo ビルドに失敗しました。%LOGFILE% を確認してください。
    pause
    exit /b 1
)

echo [2/2] 起動します...
start "" "%PROJECT_DIR%Build\ChameleonSurvivor.exe"
