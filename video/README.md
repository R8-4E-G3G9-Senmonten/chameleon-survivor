# 紹介動画(Remotion)

ゲームの映像に、大見出し・数字のカウントアップ・切り替えの光などの演出を重ねた紹介動画を作る。

## 作り方

1. ゲームをビルドし、字幕なしで映像を撮る(連番画像と場面の区切り `chapters.json` が保存される)

   ```bash
   Build/NightForest/ChameleonNight.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -nightAutoShot demo_video/clean -nightDemoVideo -nightDemoClean
   ```

2. 映像と場面の区切りをこのフォルダにコピーする

   ```bash
   cp -r demo_video/clean/frames video/public/frames
   cp demo_video/clean/chapters.json video/src/chapters.json
   ```

3. 書き出す(最初だけ `npm install`)

   ```bash
   cd video
   npm install
   npm run render
   ```

   `out/chameleon_night_promo.mp4` ができる。Discord(10MBまで)に載せるときは ffmpeg で圧縮する。

## 文言を変えるとき

`src/Promo.tsx` の `COPY`(場面ごとの大見出し・説明・数字)を書き換える。`npm run studio` でブラウザで確認しながら直せる。

映像に映るPCのIPアドレス入りのQRコードは `PrivacyMasks` でぼかしている。
