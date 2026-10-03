# Make a printable QR code PNG for the phone (light operator) page of the CURRENT network.
# Usage: python tools/make_qr.py [IP]    (IP is auto-detected when omitted)
import socket, subprocess, sys, os

try:
    import qrcode
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "-q", "qrcode[pil]"])
    import qrcode
from PIL import Image, ImageDraw, ImageFont

def detect_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))  # no packet is sent; just picks the outgoing interface
        return s.getsockname()[0]
    finally:
        s.close()

ip = sys.argv[1] if len(sys.argv) > 1 else detect_ip()
url = "https://%s:8788/" % ip
qr = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_M, box_size=24, border=4)
qr.add_data(url)
qr.make(fit=True)
img = qr.make_image(fill_color="black", back_color="white").convert("RGB")
w, h = img.size
canvas = Image.new("RGB", (w, h + 140), "white")
canvas.paste(img, (0, 0))
d = ImageDraw.Draw(canvas)
try:
    f = ImageFont.truetype("C:/Windows/Fonts/meiryo.ttc", 40)
    f2 = ImageFont.truetype("C:/Windows/Fonts/meiryo.ttc", 30)
except Exception:
    f = f2 = ImageFont.load_default()
d.text((w // 2, h + 20), "灯り役はスマホでQRを読み取り", fill="black", font=f, anchor="mt")
d.text((w // 2, h + 85), url, fill="black", font=f2, anchor="mt")

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "qr_print")
os.makedirs(out_dir, exist_ok=True)
out = os.path.abspath(os.path.join(out_dir, "qr_%s.png" % ip))
canvas.save(out, dpi=(300, 300))
print("URL :", url)
print("File:", out)
