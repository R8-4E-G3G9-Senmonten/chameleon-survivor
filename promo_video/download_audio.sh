#!/bin/sh
# 紹介動画の BGM・効果音(Springin' Sound Stock https://www.springin.org/sound-stock/ の素材)を public/audio に落とす。
# 素材ファイルは再配布できないので Git には入れない(.gitignore)。動画を作り直す人はこれを実行する。
set -e
cd "$(dirname "$0")"
mkdir -p public/audio
U=https://www.springin.org/wp-content/uploads
get() { [ -f "public/audio/$1.mp3" ] || curl -sSf -A "Mozilla/5.0" -e "https://www.springin.org/sound-stock/" -o "public/audio/$1.mp3" "$U/$2"; }
get bgm_dnb1      2022/10/Drumnbass_01.mp3                                                  # ドラムンベース1
get bgm_future1   2023/06/Future_1.mp3                                                      # フューチャー1
get jingle_result 2023/02/jingle_11.mp3                                                     # ジングル11-リザルト-
get hit3          2022/06/%E6%89%93%E6%92%833.mp3                                           # 打撃3
get impact        2022/06/%E4%BC%9A%E5%BF%83%E3%81%AE%E4%B8%80%E6%92%831.mp3                 # 会心の一撃1
get whoosh        2022/06/%E9%AB%98%E9%80%9F%E7%A7%BB%E5%8B%95.mp3                          # 高速移動
get kyupiin2      2022/06/%E3%82%AD%E3%83%A5%E3%83%92%E3%82%9A%E3%83%BC%E3%83%B32.mp3       # キュピーン2
get scratch       2022/07/%E3%82%B9%E3%82%AF%E3%83%A9%E3%83%83%E3%83%811.mp3                # スクラッチ1
get select3       2022/06/%E9%81%B8%E6%8A%9E3.mp3                                           # 選択3
get kettei1       2022/06/%E6%B1%BA%E5%AE%9A1.mp3                                           # 決定1
get seikai1       2022/06/%E6%AD%A3%E8%A7%A31.mp3                                           # 正解1
get jajan         2022/06/%E3%82%B7%E3%82%99%E3%83%A3%E3%82%B7%E3%82%99%E3%83%A3%E3%83%BC%E3%83%B31.mp3  # ジャジャーン1
get pafu          2022/06/%E3%83%8F%E3%82%9A%E3%83%95%E3%83%831.mp3                         # パフッ1
get don           2022/06/%E5%A4%AA%E9%BC%93.mp3                                            # ドン
get dodon         2022/06/%E5%A4%AA%E9%BC%932%E9%80%A3%E6%89%93.mp3                         # ドドン
get gliss         2022/06/%E3%82%AF%E3%82%99%E3%83%AD%E3%83%83%E3%82%B1%E3%83%B3%E3%82%AF%E3%82%99%E3%83%AA%E3%83%83%E3%82%B5%E3%83%B3%E3%83%88%E3%82%991.mp3  # グロッケングリッサンド1
echo done
