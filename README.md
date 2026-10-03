# カメレオンナイト プロトタイプ

高専祭専門展の企画「カメレオンナイト」の個人プロトタイプです。目的は以下2点の検証です。

- スマホをポインタ(灯り)として使う運用が実現可能か

Unity 2D (Built-in Render Pipeline)。外部アセット不使用、図形は全てコードで生成しています。

## 動かし方

1. Unity Hubで「開く」→ このフォルダ(`ChameleonNight_Prototype`)を選択
2. `Assets/Scenes/ChameleonPrototype.unity` を開く
3. 再生ボタン(▶)を押す

操作: **WASD/矢印キー**で移動。止まると足元の色に擬態します。

## スマホを接続する(初回のみPC側の設定が必要)

再生すると、Game viewの左上にスマホ接続用のURL/QRコードが表示されます。ただし**初めてこのプロジェクトを動かすPCでは、事前にWindowsファイアウォールでポートを許可する必要があります**。これをやらないと、PC自身では動いているように見えても、スマホなど他の端末からは接続がタイムアウトします。

### 手順(PCごとに1回だけ)

1. スタートボタンを右クリック → **「ターミナル(管理者)」** を選ぶ
2. 次のコマンドを貼り付けて実行

```powershell
New-NetFirewallRule -DisplayName "ChameleonNight PointerServer" -Direction Inbound -Protocol TCP -LocalPort 8787 -Action Allow -Profile Any
New-NetFirewallRule -DisplayName "ChameleonNight PointerServer HTTPS" -Direction Inbound -Protocol TCP -LocalPort 8788 -Action Allow -Profile Any
```

3. Unity側で初回再生時、Windows Defenderファイアウォールの許可ダイアログが出た場合は「アクセスを許可する」を選ぶ

### 接続手順

1. PCとスマホを**同じWi-Fi**に接続する
2. Game view左上のQRコードをスマホのカメラで読み取る
   - **タッチ操作のみで良い場合**: HTTP版のURL/QRで十分
   - **傾き操作(ジャイロ)も使いたい場合**: HTTPS版のQRを使う(iOSはHTTP接続だとセンサー権限を許可しないため)
3. HTTPS版を開くと証明書の警告(自己署名のため)が出るので、「詳細」→「アクセスする」のように進める
4. 画面下のボタンで「タッチで操作」「傾きで操作」を切り替え可能
   - 傾き操作は、スマホを水平に持ち、左右は回転(時計回り/反時計回り)、上下は傾きで狙う方式です
   - 「中心をリセット」で、狙いたい向きを基準に取り直せます
5. 複数人が同時に接続すると、参加人数に反比例して各自の光の明るさ・範囲が自動的に小さくなります

## トラブルシューティング

| 症状 | 主な原因 |
|---|---|
| スマホでQRコード/URLを開けない、タイムアウトする | 上記のファイアウォール設定が未実施。PCとスマホが別のWi-Fiに繋がっている可能性もあり |
| タッチしてもライトが反応しない | Unity再生前にスクリプトを直した場合は、一度停止して再生し直す(コード変更が反映されていない) |
| QRコード自体が画面に出ない | Unityの「Game」タブを見ているか確認(「Scene」タブにはUI要素は映りません) |
| iOSで「傾きで操作」を押しても許可ダイアログが出ない/`denied`になる | HTTPS版のURLを使っているか確認。Safariで一度「履歴とWebサイトデータを消去」してから開き直す |
| ルーターが同じWi-Fi内の端末同士の通信を遮断している | ルーターの管理画面で「プライバシーセパレーター」「AP間通信規制」を確認 |

## 技術メモ

- スマホ連携は外部ライブラリ不使用の自作HTTPサーバー(`Assets/Scripts/PointerServer.cs`、生の`TcpListener`)。管理者権限やURL予約が不要なようにこの実装にしています
- HTTPS用の自己署名証明書は、UnityのMonoランタイムが証明書生成に対応していないため、生成のみWindows標準のPowerShell(`New-SelfSignedCertificate`)に委譲し、Unity側は読み込みだけ行っています(Windows専用)
- QRコードは外部ライブラリ不使用の自作エンコーダ(`Assets/Scripts/QRCodeGenerator.cs`)
