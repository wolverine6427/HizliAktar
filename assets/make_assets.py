# kaynak.jpg'den uygulama simgelerini, arka plan filigranlarını ve APK indirme QR kodunu üretir.
# Çalıştırma: python assets/make_assets.py   (gerekenler: pip install pillow qrcode)
from pathlib import Path
from PIL import Image
import qrcode

ROOT = Path(__file__).resolve().parent.parent
src = Image.open(ROOT / "assets" / "kaynak.jpg").convert("RGB")
APK_URL = "https://github.com/wolverine6427/HizliAktar/releases/latest/download/HizliAktar.apk"

def out(rel):
    p = ROOT / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    return p

def watermark(img, opacity):
    """Beyaz zemini saydam yapar, kalan logoyu verilen opaklıkta bırakır."""
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, _ = px[x, y]
            a = 255 - min(r, g, b)
            a = 0 if a < 25 else a
            px[x, y] = (r, g, b, int(a * opacity))
    return img

mark = src.crop((105, 125, 370, 375))
full = src.crop((105, 125, 1110, 375))
win_tile = src.crop((1280, 126, 1554, 400))
and_tile = src.crop((1626, 126, 1900, 400))

# Android: başlatıcı simgesi ve arka plan filigranı
and_tile.resize((192, 192), Image.LANCZOS).save(out("android/res/mipmap-xxxhdpi/ic_launcher.png"))
and_tile.resize((96, 96), Image.LANCZOS).save(out("android/res/mipmap-xhdpi/ic_launcher.png"))
watermark(mark.resize((520, 490), Image.LANCZOS), 0.10).save(out("android/res/drawable-nodpi/logo_bg.png"))
watermark(full.resize((600, 149), Image.LANCZOS), 1.0).save(out("android/res/drawable-nodpi/logo_full.png"))

# PC: exe simgesi, sürükle-bırak alanı filigranı, QR
win_tile.save(out("pc/app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
watermark(full.resize((460, 114), Image.LANCZOS), 0.16).save(out("pc/logo_bg.png"))
watermark(full.resize((300, 75), Image.LANCZOS), 1.0).save(out("pc/logo_small.png"))
q = qrcode.QRCode(border=2, box_size=8, error_correction=qrcode.constants.ERROR_CORRECT_M)
q.add_data(APK_URL)
q.make_image(fill_color="black", back_color="white").save(out("pc/qr_apk.png"))

# README
full.save(out("docs/logo.png"))
print("varliklar uretildi")
