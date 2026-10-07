"""紹介動画づくり: ゲームの「デモ撮影モード」で保存した連番画像(JPG)を、1本の動画(MP4 / WebM)にまとめる。

使い方:
    1. ゲームを撮影モードで起動する(連番画像が <フォルダ>/frames に保存され、終わると自動で閉じる)
         Build/NightForest/ChameleonNight.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -nightAutoShot <フォルダ> -nightDemoVideo
    2. このスクリプトで動画にする
         python tools/make_demo_video.py <フォルダ>/frames 出力.mp4      … Discord用(10MB以下のMP4。ffmpeg が必要)
         python tools/make_demo_video.py <フォルダ>/frames 出力.webm [幅] [fps] … 高画質(ファイルは大きい)

    MP4 は、動画の長さから計算したビットレートで2回に分けて変換し(2パス)、9.5MB 以下に収める。
    音声は入らない。
必要なもの: Python 3 と OpenCV(pip install opencv-python)、MP4 にするときは ffmpeg(scoop install ffmpeg)
"""
import glob
import os
import shutil
import subprocess
import sys

import cv2

TARGET_MB = 9.5  # Discord の無料アカウントの上限(10MB)より少し小さく


def make_mp4(src, out, count):
    """ffmpeg で、指定サイズに収まる H.264 の MP4 を作る(2パス)。"""
    ffmpeg = shutil.which("ffmpeg")
    scoop = os.path.join(os.path.expanduser("~"), "scoop", "shims", "ffmpeg.exe")  # PATH に無くても Scoop の場所を見る
    if ffmpeg is None and os.path.exists(scoop):
        ffmpeg = scoop
    if ffmpeg is None:
        print("ffmpeg が見つかりません。scoop install ffmpeg で入れてください。")
        sys.exit(1)
    seconds = count / 30.0
    kbps = int(TARGET_MB * 8 * 1024 / seconds * 0.95)  # 入れ物(MP4)の分を少し引く
    pattern = os.path.join(src, "f_%05d.jpg")
    log = os.path.join(os.path.dirname(os.path.abspath(out)), "x264pass")
    common = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-framerate", "30", "-i", pattern,
              "-c:v", "libx264", "-preset", "slow", "-b:v", "%dk" % kbps, "-pix_fmt", "yuv420p", "-an",
              "-passlogfile", log]
    subprocess.run(common + ["-pass", "1", "-f", "mp4", os.devnull], check=True)
    subprocess.run(common + ["-pass", "2", "-movflags", "+faststart", out], check=True)
    for f in os.listdir(os.path.dirname(log)):
        if f.startswith("x264pass"):
            os.remove(os.path.join(os.path.dirname(log), f))
    print("書き出しました: %s (%.1f秒, %dkbps, %.1fMB)" % (out, seconds, kbps, os.path.getsize(out) / 1024 / 1024))


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    src, out = sys.argv[1], sys.argv[2]
    width = int(sys.argv[3]) if len(sys.argv) > 3 else 1280
    fps_out = int(sys.argv[4]) if len(sys.argv) > 4 else 30
    files = sorted(glob.glob(os.path.join(src, "f_*.jpg")))
    if not files:
        print("連番画像が見つかりません: " + src)
        sys.exit(1)

    if out.lower().endswith(".mp4"):
        make_mp4(src, out, len(files))
        return

    first = cv2.imread(files[0])
    h0, w0 = first.shape[:2]
    height = int(round(h0 * width / w0 / 2) * 2)
    step = max(1, round(30 / fps_out))  # 撮影は30fps。fpsを下げるときは間引く
    writer = cv2.VideoWriter(out, cv2.VideoWriter_fourcc(*"VP80"), 30 / step, (width, height))
    if not writer.isOpened():
        print("動画を書き出せませんでした(OpenCV の VP8 が使えません)")
        sys.exit(1)
    for i, f in enumerate(files):
        if i % step:
            continue
        img = cv2.imread(f)
        if img.shape[1] != width:
            img = cv2.resize(img, (width, height), interpolation=cv2.INTER_AREA)
        writer.write(img)
    writer.release()
    size = os.path.getsize(out) / 1024 / 1024
    print("書き出しました: %s (%d枚, %.1f秒, %.1fMB)" % (out, len(files[::step]), len(files) / 30.0, size))


if __name__ == "__main__":
    main()
