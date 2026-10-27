# カメレオンサバイバー デモ版(10/05)

高専祭専門展「カメレオンサバイバー」のデモ版です。

- **メインプレイヤー(カメレオン役)**: 圧力マット(ESP32コントローラー)を踏んで、前後左右に移動する
- **灯り役(最大7人)**: 自分のスマホを懐中電灯にして、敵を照らして倒したり、石を照らして蛍を集めたりする

## このリポジトリに入っているもの

| 場所 | 中身 |
|---|---|
| `Assets/` ほか | Unityプロジェクト本体(Unity **6000.4.3f1**) |
| `play_night.bat` | ダブルクリックでビルドして起動する(夜の森モード、デモはこちら)。スマホは5G・どのWi-Fiからでも参加できる |
| `play_night_wifi.bat` | 同上。インターネットがない会場向け(スマホはPCと同じWi-Fiから参加) |
| `play_survivor.bat` | 同上(サバイバーモード) |
| `esp32/controller/controller.ino` | **ESP32のスケッチ(Arduino IDE用)。デモではこれを使う** |
| `esp32/pressure_mat/pressure_mat.ino` | 参考: Wi-Fiで送る版のスケッチ |
| `docs/` | 解説資料。**敵キャラの詳細は [`docs/enemies.md`](docs/enemies.md)**、仕組みの解説は `system-guide.html` |
| `blender/chameleon/` | 主人公カメレオンを作る Blender スクリプト |
| `blender/enemies/` | 敵モデル(Meshy AI)に骨とアニメを自動で付ける Blender スクリプト |
| `tools/` | QR印刷・紹介動画づくりのスクリプト。5G用QRに使う `cloudflared.exe` もここに置く(Gitには入れない) |
| `mockup/` | HTMLで作った試作・UI案 |
| `reference/` | 参考資料(試作の動画・画像、元の Meshy モデルなど) |

---

## 当日の手順

### 1. PCをインターネットにつなぐ

`play_night.bat` で起動すると、ゲームが Cloudflare のトンネルでインターネット上のURL(`https://〜.trycloudflare.com/`)を作り、そのQRを出します。**スマホは5G(モバイル通信)でも、どのWi-Fiからでも参加できます。**

- PCがインターネットにつながっていれば、スマホとPCが同じWi-Fiである必要はありません
- PCに cloudflared が入っている必要があります(下の「最初に1回だけやる準備」)
- URLの準備に30秒〜1分ほどかかります。その間、QRの場所に「QRコードを作成中」と出ます
- URLは起動するたびに変わります。印刷したQRは使えないので、ゲーム画面のQRを読み取ってください

**インターネットがない会場では** `play_night.bat` の代わりに **`play_night_wifi.bat`** を使います。QRがすぐ出ますが、PCと全員のスマホを同じWi-Fiにつなぐ必要があります。

- スマホのモバイル通信(4G/5G)のままだと参加できません
- 「ゲスト用」のWi-Fiや、端末同士の通信を禁止しているWi-Fi(ルーターの「プライバシーセパレーター」「AP間通信規制」など)では、同じWi-Fiでもつながりません

`play_night.bat` でも、インターネットにつながらない・cloudflared が入っていない時は、少し待つと自動で同じWi-Fi用のQRになります(QRの下に「同じWi-Fiにつないで参加」と出ます)。

### 2. ESP32をPCにUSBでつなぐ

ESP32(圧力マットのコントローラー)を、**USBケーブルでPCに接続**します。ESP32側はWi-Fiを使わないので、Wi-Fiの設定は不要です。

- **データ通信に対応したUSBケーブルを使う**(充電専用ケーブルだとPCが認識しません)
- **Arduino IDEのシリアルモニタは閉じておく**(開いているとゲームがESP32と通信できません)
- ゲームの起動前・起動後のどちらにつないでも、自動で見つけて接続します

### 3. ゲームを起動する

1. Unity Editorを開いている場合は閉じる
2. `play_night.bat` をダブルクリックする
   - 自動でビルドしてからゲームが起動します(1分ほど。**そのPCで初めて動かすときは十数分かかることがあります**)
3. 画面左上に **「● コントローラー: 接続中(USB COMx)」** と出ていればESP32はOK

### 4. スマホで参加する

1. ゲーム画面のQRコードを、スマホのカメラで読み取る(アプリのインストールは不要)
2. 開いたページで「タップして始める」を押し、スマホを傾けて狙いを動かす。大きな **「ライトON」ボタンを押している間だけ** 灯りがつく
   - iPhoneは傾きセンサーの許可を聞かれるので「許可」を選んでください
   - 同じWi-Fi用のQRの時は、証明書の警告が出ることがあります。「詳細」→「アクセスする」のように進めてください(PC内で作った証明書のため)
   - 狙いがずれてきたら「中心をリセット」を押すと、今の向きが画面の中央になります
3. 最大7人まで同時に参加できます(人数が増えると1人あたりの灯りは小さくなります)

ESP32やスマホがなくても、キーボード(WASD/矢印キー)で移動、マウスで灯りを動かして遊べます。

---

## 最初に1回だけやる準備

### PC: ファイアウォールでポートを開ける(PCごとに1回)

これをしないと、PCでは動いて見えてもスマホからつながりません(タイムアウトします)。

1. スタートボタンを右クリック → **「ターミナル(管理者)」** を開く
2. 次の2行を貼り付けて実行する

```powershell
New-NetFirewallRule -DisplayName "ChameleonNight PointerServer" -Direction Inbound -Protocol TCP -LocalPort 8787 -Action Allow -Profile Any
New-NetFirewallRule -DisplayName "ChameleonNight PointerServer HTTPS" -Direction Inbound -Protocol TCP -LocalPort 8788 -Action Allow -Profile Any
```

3. ゲームの初回起動時にWindowsのファイアウォールの確認が出たら「アクセスを許可する」を選ぶ

### PC: 必要なソフト

- Unity Hub と **Unity 6000.4.3f1**(`play_night.bat` はこのバージョンを使います)
- **cloudflared**(5G・どのWi-Fiからでも参加できるQRを作るため。アカウント登録は不要)。**これが無いと、QRは同じWi-Fi用になります。** 次のどちらかで入れる:
  - ターミナルで `winget install --id Cloudflare.cloudflared`
  - winget が無いPCでは、[公式のリリース](https://github.com/cloudflare/cloudflared/releases/latest)から `cloudflared-windows-amd64.exe` をダウンロードし、名前を `cloudflared.exe` に変えて、このフォルダの `tools\` に置く。PowerShell なら次の1行(このフォルダで実行):
    ```powershell
    Invoke-WebRequest https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe -OutFile tools\cloudflared.exe
    ```
  - 入ったかどうかは、ゲームのログ `%USERPROFILE%\AppData\LocalLow\DefaultCompany\ChameleonNight_Prototype\Player.log` に「Cloudflare トンネルを起動しました」と出るかで分かる(「cloudflared.exe が見つからない」と出たら入っていない)
- ESP32に書き込む場合は Arduino IDE

### ESP32: スケッチを書き込む(ESP32ごとに1回)

1. Arduino IDE のボードマネージャで **「esp32 by Espressif Systems」** をインストールする
2. `esp32/controller/controller.ino` を開く
3. ツール → ボード → esp32 → **「ESP32 Dev Module」** を選ぶ
4. ESP32のポートを選んで書き込む
5. **書き込みが終わったらシリアルモニタを閉じる**

圧力マット(スイッチ)の配線: 片側をESP32のピン、もう片側をGNDにつなぎます。

| 方向 | ピン |
|---|---|
| 前 | 26 |
| 後 | 25 |
| 左 | 32 |
| 右 | 27 |

動作確認したいときは、スケッチ先頭の `OUTPUT_TEXT` を `true` にして書き込むと、シリアルモニタに `1,0,0,1`(前,後,左,右)のように表示されます(この形式でもゲームは動きます)。

---

## うまくいかないとき

| 症状 | 確認すること |
|---|---|
| QRの下に「同じWi-Fiにつないで参加」と出る(5G用のQRにならない) | PCがインターネットにつながっているか / cloudflared を入れたか(`winget install --id Cloudflare.cloudflared`) |
| スマホでQRを読んでもページが開かない・タイムアウトする | (同じWi-Fi用のQRの時)PCとスマホが同じWi-Fiか / ファイアウォールの準備をしたか / ゲスト用・端末間通信禁止のWi-Fiでないか |
| 左上が「コントローラー: 未接続」のまま | ESP32がUSBでつながっているか / ケーブルがデータ通信対応か / Arduino IDEのシリアルモニタを閉じたか |
| マットを踏んでも反応しない | `OUTPUT_TEXT = true` で書き込み、シリアルモニタで踏んだ方向が `1` になるか確認(配線・ピン番号) |
| `play_night.bat` で「ビルドに失敗しました」と出る | Unity Editorを閉じたか / Unity 6000.4.3f1 が入っているか / 同じフォルダの `unity_build_night.log` を確認 |
| ゲーム画面が出てこない | タスクバーの「カメレオンサバイバー」を確認(他のウィンドウの裏に隠れていることがある) |
