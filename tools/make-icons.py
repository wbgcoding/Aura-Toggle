#!/usr/bin/env python3
"""Regenerates assets/aura.ico and assets/aura-off.ico from a colour ring sampled off the
existing icon - see WORK.md Phase D. Ben's brief: transparent inside the ring, only the colour
ring itself paints; the "off" icon is the same ring at the same colours, darkened to about 35%
instead of the old flat grey disc; the ring is drawn thicker for 16/24/32 px so it does not thin
out to a thread in the notification area, and keeps today's proportions from 48 px up.

The ring is described analytically (an angle -> colour table, sampled off the existing 256 px
icon) rather than resized from the source bitmap, so it can be redrawn at any thickness without
also inheriting the dark disc filling the middle of the source image today.

Usage: python tools/make-icons.py
"""

import math
import sys
from pathlib import Path

from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent
ASSETS = REPO_ROOT / "assets"
SOURCE_ICON = ASSETS / "aura.ico"

SIZES = (16, 24, 32, 48, 64, 128, 256)
SUPERSAMPLE = 4
SAMPLE_RADIUS_FRACTION = 0.68  # where the ring colour is read off the source icon
OUTER_FRACTION = 0.875
INNER_FRACTION_LARGE = 0.477  # 48 px and up - matches the source icon's own proportions
INNER_FRACTION_SMALL = 0.34  # 16/24/32 px - a thicker ring so it survives downscaling
SMALL_SIZE_CUTOFF = 48
OFF_DARKEN = 0.35


def sample_ring_colours(source_path: Path) -> list[tuple[int, int, int]]:
    """One RGB colour per degree, read off the source icon's 256 px frame at
    SAMPLE_RADIUS_FRACTION of its radius - partway through the ring, clear of both its own
    antialiased edges and the dark disc filling the icon's centre today."""
    source = Image.open(source_path)
    source.size = (256, 256)
    source.load()
    frame = source.convert("RGBA")

    centre = 128.0
    radius = SAMPLE_RADIUS_FRACTION * centre
    colours = []
    for degree in range(360):
        angle = math.radians(degree)
        x = round(centre + radius * math.cos(angle))
        y = round(centre - radius * math.sin(angle))
        r, g, b, _a = frame.getpixel((x, y))
        colours.append((r, g, b))
    return colours


def ring_colour(colours: list[tuple[int, int, int]], degree: float) -> tuple[float, float, float]:
    """Linear blend between the two neighbouring sampled degrees, so the ring does not show 360
    flat wedges at small sizes."""
    lo = int(degree) % 360
    hi = (lo + 1) % 360
    weight = degree - int(degree)
    lo_c, hi_c = colours[lo], colours[hi]
    return tuple(lo_c[i] * (1 - weight) + hi_c[i] * weight for i in range(3))


def draw_ring(colours: list[tuple[int, int, int]], size: int) -> Image.Image:
    canvas = size * SUPERSAMPLE
    centre = canvas / 2.0
    outer = OUTER_FRACTION * centre
    inner = (INNER_FRACTION_SMALL if size < SMALL_SIZE_CUTOFF else INNER_FRACTION_LARGE) * centre

    big = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    pixels = big.load()
    for py in range(canvas):
        dy = py + 0.5 - centre
        for px in range(canvas):
            dx = px + 0.5 - centre
            r = math.hypot(dx, dy)
            if inner <= r <= outer:
                angle = math.degrees(math.atan2(-dy, dx)) % 360
                red, green, blue = ring_colour(colours, angle)
                pixels[px, py] = (round(red), round(green), round(blue), 255)

    return big.resize((size, size), Image.LANCZOS)


def darken(image: Image.Image, factor: float) -> Image.Image:
    r, g, b, a = image.split()
    r = r.point(lambda v: round(v * factor))
    g = g.point(lambda v: round(v * factor))
    b = b.point(lambda v: round(v * factor))
    return Image.merge("RGBA", (r, g, b, a))


def write_ico(images: dict[int, Image.Image], dst: Path) -> None:
    # Pillow's ICO writer only emits entries up to the size of the image save() is called on -
    # asking a 16x16 base for a 256x256 entry silently drops it instead of upscaling. Largest
    # first avoids that; append_images then supplies the rest, matched to `sizes` by their own
    # pixel size rather than resized from the base.
    ordered = [images[size] for size in sorted(SIZES, reverse=True)]
    ordered[0].save(dst, format="ICO", sizes=[(size, size) for size in SIZES],
                     append_images=ordered[1:])


def main() -> None:
    if not SOURCE_ICON.exists():
        raise SystemExit(f"{SOURCE_ICON} not found")

    colours = sample_ring_colours(SOURCE_ICON)

    on_images = {size: draw_ring(colours, size) for size in SIZES}
    off_images = {size: darken(on_images[size], OFF_DARKEN) for size in SIZES}

    write_ico(on_images, ASSETS / "aura.ico")
    write_ico(off_images, ASSETS / "aura-off.ico")

    print(f"aura.ico, aura-off.ico: {len(SIZES)} sizes ({', '.join(str(s) for s in SIZES)})")


if __name__ == "__main__":
    sys.exit(main())
