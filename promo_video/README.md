# カメレオンサバイバー 紹介動画(Remotion)

1分・横型(1920×1080・30fps)の紹介動画です。BGM の拍(172BPM)に合わせて場面を切り替えます。
台本と演出は `src/Promo.tsx`、演出の部品は `src/fx.tsx`、スマホと圧力マットの絵は `src/mocks.tsx` にあります。

## 必要なもの

- Node.js(`winget install OpenJS.NodeJS.LTS`)
- このフォルダで `npm install`

## 作り方

1. ゲームの録画を撮る(字幕なしのデモ撮影モード。終わると自動で閉じる。本物のランキングには記録されない)

   ```
   Build/NightForest/ChameleonNight.exe -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -noTunnel -nightAutoShot <このフォルダ>/capture -nightDemoVideo -nightDemoClean
   ```

   `capture/frames` に連番画像、`capture/chapters.txt` に各場面が始まったフレーム番号が出る。

2. 連番画像を動画にまとめる

   ```
   npx remotion ffmpeg -y -framerate 30 -i capture/frames/f_%05d.jpg -vf scale=1920:1080:flags=lanczos -c:v libx264 -crf 18 -pix_fmt yuv420p -g 15 public/footage.mp4
   ```

   録画を撮り直すと場面の位置がずれるので、`src/Promo.tsx` の `from={…}`(録画の何フレーム目か)を `chapters.txt` を見て合わせる。

3. BGM・効果音を落とす: `sh download_audio.sh`
4. 確認しながら直す: `npm run studio`(ブラウザでプレビュー)
5. 書き出す: `npm run render` → `out/promo.mp4`

## フォント

`public/fonts` の Dela Gothic One と Mochiy Pop One(どちらも SIL Open Font License)を使っています。

## 音の素材

BGM・効果音はすべて [Springin' Sound Stock](https://www.springin.org/sound-stock/) の素材です(商用利用無料・クレジット任意)。
素材ファイル自体の再配布は禁止なので、Git には入れていません。
