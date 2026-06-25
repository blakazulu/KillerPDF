"""Scalpel logo generator -- Clinical edition.

Vector source of truth: branding/scalpel-icon.svg
  A steel Fluent squircle holding a tilted document, with a scalpel whose
  cutting edge glows surgical-red (#E11D38) -- the redesign's accent.

Pillow can't rasterise SVG, so a high-res master PNG is rendered from the SVG
(headless browser, transparent) and committed as:
  branding/scalpel-master-1024.png       <-- edit the SVG, re-render this
To regenerate it from the SVG with Chrome/Edge:
  chrome --headless=new --default-background-color=00000000 \
         --window-size=1024,1024 --screenshot=branding/scalpel-master-1024.png \
         <served scalpel-icon.svg or a 1024 wrapper>

This script slices that master into every downstream brand asset and (with
--export) deploys them to their destinations:
  Resources/scalpel.ico                  app / window / EXE icon
  packaging/Assets/*.png                 MSIX Store tiles
  store-assets/StoreListingLogo_300x300  Store listing logo
Run from the repo root:
  python branding/scalpel_logo.py            # QA previews only
  python branding/scalpel_logo.py --export   # regenerate + deploy everything
"""
import sys, os, shutil
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def P(*parts):
    return os.path.join(ROOT, *parts)


MASTER = P("branding", "scalpel-master-1024.png")
ICO_SIZES = [(s, s) for s in (16, 24, 32, 48, 64, 128, 256)]

# MSIX square tiles (px) -> filename
SQUARE_TILES = {
    "Square44x44Logo.png": 44,
    "Square71x71Logo.png": 71,
    "Square150x150Logo.png": 150,
    "Square310x310Logo.png": 310,
    "StoreLogo.png": 50,
}


def load_master():
    im = Image.open(MASTER).convert("RGBA")
    if im.size != (1024, 1024):
        im = im.resize((1024, 1024), Image.LANCZOS)
    return im


def down(img, size):
    return img.resize((size, size), Image.LANCZOS)


def rect_asset(master, w, h, frac):
    """Centre the square mark on a transparent w*h canvas (Wide tile / Splash)."""
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    m = int(min(w, h) * frac)
    canvas.alpha_composite(down(master, m), ((w - m) // 2, (h - m) // 2))
    return canvas


def main():
    master = load_master()

    # QA previews
    down(master, 512).save(P("branding", "preview_transparent.png"))
    card = Image.new("RGBA", (512, 512), (12, 14, 19, 255))
    card.alpha_composite(down(master, 512))
    card.convert("RGB").save(P("branding", "preview_darkbg.png"))
    print("wrote previews")

    if "--export" not in sys.argv:
        return

    # .ico (multi-resolution) — master output + deploy to the app
    down(master, 256).save(P("branding", "scalpel.ico"), sizes=ICO_SIZES)

    os.makedirs(P("branding", "tiles"), exist_ok=True)
    for name, sz in SQUARE_TILES.items():
        down(master, sz).save(P("branding", "tiles", name))
    rect_asset(master, 310, 150, 0.80).save(P("branding", "tiles", "Wide310x150Logo.png"))
    rect_asset(master, 620, 300, 0.55).save(P("branding", "tiles", "SplashScreen.png"))
    down(master, 1024).save(P("branding", "scalpel-1024.png"))

    # deploy to destinations
    shutil.copy(P("branding", "scalpel.ico"), P("Resources", "scalpel.ico"))
    for name in list(SQUARE_TILES) + ["Wide310x150Logo.png", "SplashScreen.png"]:
        shutil.copy(P("branding", "tiles", name), P("packaging", "Assets", name))
    down(master, 300).save(P("store-assets", "StoreListingLogo_300x300.png"))
    print("wrote ico + all tiles + deployed to Resources/, packaging/Assets/, store-assets/")


if __name__ == "__main__":
    main()
