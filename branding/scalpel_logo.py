"""Scalpel logo generator -- Clinical edition.

Vector source of truth: branding/scalpel-icon.svg
  A steel Fluent squircle holding a tilted document, with a scalpel whose
  cutting edge glows surgical-red (#E11D38) -- the redesign's accent.

There are two vector sources:
  scalpel-icon.svg    full mark  (used at sizes > 56px)
  scalpel-glyph.svg   simplified glyph (used at sizes <= 56px, e.g. small .ico
                      frames and the 44/50 MSIX tiles) so the scalpel stays
                      legible in the taskbar / alt-tab.

Pillow can't rasterise SVG, so high-res master PNGs are rendered from each SVG
(headless browser, transparent) and committed as:
  branding/scalpel-master-1024.png         <-- from scalpel-icon.svg
  branding/scalpel-glyph-master-1024.png   <-- from scalpel-glyph.svg
Re-render either with Chrome/Edge after editing its SVG:
  chrome --headless=new --default-background-color=00000000 \
         --window-size=1024,1024 --screenshot=<dest> <served 1024 wrapper>

This script slices those masters into every downstream brand asset and (with
--export) deploys them to their destinations:
  Resources/scalpel.ico                  app / window / EXE icon
  packaging/Assets/*.png                 MSIX Store tiles
  store-assets/StoreListingLogo_300x300  Store listing logo
Run from the repo root:
  python branding/scalpel_logo.py            # QA previews only
  python branding/scalpel_logo.py --export   # regenerate + deploy everything
"""
import sys, os, shutil, io, struct
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def P(*parts):
    return os.path.join(ROOT, *parts)


MASTER = P("branding", "scalpel-master-1024.png")
GLYPH_MASTER = P("branding", "scalpel-glyph-master-1024.png")
ICO_FRAMES = (16, 24, 32, 48, 64, 128, 256)
GLYPH_MAX = 56  # at/below this px the simplified glyph reads better than the full mark

# MSIX square tiles (px) -> filename
SQUARE_TILES = {
    "Square44x44Logo.png": 44,
    "Square71x71Logo.png": 71,
    "Square150x150Logo.png": 150,
    "Square310x310Logo.png": 310,
    "StoreLogo.png": 50,
}


def load_1024(path):
    im = Image.open(path).convert("RGBA")
    if im.size != (1024, 1024):
        im = im.resize((1024, 1024), Image.LANCZOS)
    return im


def down(img, size):
    return img.resize((size, size), Image.LANCZOS)


def tile(full, glyph, size):
    """Downscale to `size`, picking the simplified glyph at small sizes."""
    return down(glyph if size <= GLYPH_MAX else full, size)


def rect_asset(full, w, h, frac):
    """Centre the full mark on a transparent w*h canvas (Wide tile / Splash)."""
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    m = int(min(w, h) * frac)
    canvas.alpha_composite(down(full, m), ((w - m) // 2, (h - m) // 2))
    return canvas


def build_ico(path, frames):
    """Assemble a PNG-backed .ico from {size: PIL.Image} (modern Windows reads PNG entries)."""
    items = sorted(frames.items())
    blobs = []
    for size, im in items:
        buf = io.BytesIO()
        im.save(buf, "PNG")
        blobs.append((size, buf.getvalue()))
    out = io.BytesIO()
    out.write(struct.pack("<HHH", 0, 1, len(blobs)))   # reserved, type=icon, count
    offset = 6 + 16 * len(blobs)
    for size, data in blobs:
        b = 0 if size >= 256 else size
        out.write(struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    for _, data in blobs:
        out.write(data)
    with open(path, "wb") as fh:
        fh.write(out.getvalue())


def main():
    full = load_1024(MASTER)
    glyph = load_1024(GLYPH_MASTER)

    # QA previews (full mark)
    down(full, 512).save(P("branding", "preview_transparent.png"))
    card = Image.new("RGBA", (512, 512), (12, 14, 19, 255))
    card.alpha_composite(down(full, 512))
    card.convert("RGB").save(P("branding", "preview_darkbg.png"))
    print("wrote previews")

    if "--export" not in sys.argv:
        return

    # .ico — small frames use the simplified glyph, large frames the full mark
    build_ico(P("branding", "scalpel.ico"),
              {s: tile(full, glyph, s) for s in ICO_FRAMES})

    os.makedirs(P("branding", "tiles"), exist_ok=True)
    for name, sz in SQUARE_TILES.items():
        tile(full, glyph, sz).save(P("branding", "tiles", name))
    rect_asset(full, 310, 150, 0.80).save(P("branding", "tiles", "Wide310x150Logo.png"))
    rect_asset(full, 620, 300, 0.55).save(P("branding", "tiles", "SplashScreen.png"))
    down(full, 1024).save(P("branding", "scalpel-1024.png"))

    # deploy to destinations
    shutil.copy(P("branding", "scalpel.ico"), P("Resources", "scalpel.ico"))
    for name in list(SQUARE_TILES) + ["Wide310x150Logo.png", "SplashScreen.png"]:
        shutil.copy(P("branding", "tiles", name), P("packaging", "Assets", name))
    down(full, 300).save(P("store-assets", "StoreListingLogo_300x300.png"))
    print("wrote ico + all tiles + deployed to Resources/, packaging/Assets/, store-assets/")


if __name__ == "__main__":
    main()
