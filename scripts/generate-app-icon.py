"""Generate ChatGPT Wrapper application icon (app.ico)."""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

# Theme-adjacent palette — flat, minimal
BG = (20, 20, 22, 255)          # deep neutral tile
ACCENT = (107, 171, 224, 255)   # slightly lifted accent for clarity on dark

SIZES = (16, 24, 32, 48, 64, 128, 256)
OUT = Path(__file__).resolve().parents[1] / "ChatGPTWrapper.WinUI" / "Assets" / "app.ico"


def _scale(value: float, size: int) -> int:
    return max(1, round(value * size))


def draw_tile(draw: ImageDraw.ImageDraw, size: int) -> None:
    """Squircle on an opaque canvas — no transparent corners (breaks taskbar)."""
    pad = _scale(0.06, size)
    radius = _scale(0.26, size)
    draw.rounded_rectangle(
        (pad, pad, size - pad - 1, size - pad - 1),
        radius=radius,
        fill=BG,
    )


def stroke_round(draw: ImageDraw.ImageDraw, p1: tuple[int, int], p2: tuple[int, int], width: int, color: tuple[int, ...]) -> None:
    """Monoline segment with round caps."""
    draw.line([p1, p2], fill=color, width=width)
    r = width // 2
    for x, y in (p1, p2):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=color)


def draw_minimal_w(draw: ImageDraw.ImageDraw, size: int) -> None:
    """Thin monoline W — centered, generous whitespace."""
    stroke = max(2, _scale(0.065, size))

    left = _scale(0.30, size)
    right = size - left
    center = size // 2
    valley_l = _scale(0.405, size)
    valley_r = size - valley_l

    y_top = _scale(0.36, size)
    y_valley = _scale(0.66, size)
    y_crest = _scale(0.50, size)

    stroke_round(draw, (left, y_top), (valley_l, y_valley), stroke, ACCENT)
    stroke_round(draw, (valley_l, y_valley), (center, y_crest), stroke, ACCENT)
    stroke_round(draw, (center, y_crest), (valley_r, y_valley), stroke, ACCENT)
    stroke_round(draw, (valley_r, y_valley), (right, y_top), stroke, ACCENT)


def render(size: int) -> Image.Image:
    # Fully opaque canvas — Windows taskbar/shell mishandles ICO alpha holes.
    img = Image.new("RGBA", (size, size), BG)
    draw = ImageDraw.Draw(img)
    draw_tile(draw, size)
    draw_minimal_w(draw, size)
    return img.convert("RGB")


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    frames = [render(s) for s in sorted(SIZES, reverse=True)]
    frames[0].save(OUT, format="ICO", append_images=frames[1:])
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes, {len(SIZES)} sizes)")


if __name__ == "__main__":
    main()
