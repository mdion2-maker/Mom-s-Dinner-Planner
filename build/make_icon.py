"""Draws app\\icon.ico (and a PNG preview) for Mom's Dinner Planner --
the letters "MDP" in the same accent green as the app's "Pick my dinners"
button (--accent background, --accent-ink text), on a rounded square.

    python build\\make_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

SIZE = 1024
ACCENT = (76, 188, 160, 255)      # --accent (dark theme) - the button's teal
ACCENT_INK = (255, 255, 255, 255)  # white text
FONT_PATH = r"C:\Windows\Fonts\segoeuib.ttf"  # bold Segoe UI - the app's own font fallback


def draw_icon():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = 36
    d.rounded_rectangle([pad, pad, SIZE - pad, SIZE - pad], radius=210, fill=ACCENT)

    text = "MDP"
    font_size = 380
    font = ImageFont.truetype(FONT_PATH, font_size)
    box = d.textbbox((0, 0), text, font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    x = (SIZE - tw) / 2 - box[0]
    y = (SIZE - th) / 2 - box[1]
    d.text((x, y), text, font=font, fill=ACCENT_INK)

    return img


def draw_fullbleed(size):
    """Same lettering on a solid square with no transparent corners. iPhones paint
    transparent pixels black and round the corners themselves, so the home-screen
    icon has to be full-bleed. The lettering stays inside the centre 60% so it
    also survives Android's circular 'maskable' crop."""
    img = Image.new("RGBA", (SIZE, SIZE), ACCENT)
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT_PATH, 380)
    box = d.textbbox((0, 0), "MDP", font=font)
    x = (SIZE - (box[2] - box[0])) / 2 - box[0]
    y = (SIZE - (box[3] - box[1])) / 2 - box[1]
    d.text((x, y), "MDP", font=font, fill=ACCENT_INK)
    return img.resize((size, size), Image.LANCZOS).convert("RGB")


def main():
    root = Path(__file__).resolve().parent.parent
    img = draw_icon()
    preview = root / "build" / "icon_preview.png"
    img.save(preview)
    out = root / "app" / "icon.ico"
    sizes = [16, 24, 32, 48, 64, 128, 256]
    img.save(out, sizes=[(s, s) for s in sizes])
    pwa = root / "app" / "pwa"
    pwa.mkdir(exist_ok=True)
    for name, size in (("icon-180.png", 180), ("icon-192.png", 192),
                       ("icon-512.png", 512), ("icon-maskable-512.png", 512)):
        draw_fullbleed(size).save(pwa / name)
    print(f"Wrote {preview}, {out} and the iPhone/PWA icons in {pwa}")


if __name__ == "__main__":
    main()
