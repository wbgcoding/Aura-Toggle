#!/usr/bin/env python3
"""Animates the big button in the README preview screenshots.

Reads docs/preview-dark.png and docs/preview-light.png (written by tools/capture-preview.ps1) and
writes docs/preview-dark.webp and docs/preview-light.webp: the same window, animated, with its
rounding and label kept exactly as the capture drew them.

The animation is the screenshot's own pixels, shifted. EffectPainter (src/Ui.cs) paints the
spectrum as one horizontal cycle across the button and animates it by translating that cycle, so
the captured button's left and right edge carry the same colour and rolling it sideways reproduces
the app's own sweep exactly - same stops, same brightness, same wash. Nothing here re-invents the
gradient, so it cannot drift away from what the tool actually shows.

The label is lifted off the button by how far each pixel deviates from the clean gradient row, and
composited back on top of every frame, so the text stays put while the colour moves under it.

Animated WebP rather than GIF, for two reasons. Speed: a GIF frame delay is stored in hundredths
of a second and browsers force anything under 20 ms up to 100 ms, which capped the animation at a
visibly stepping 10 frames a second. And colour: GIF holds 256 of them, while one row of the
captured gradient carries about 600, so the button came out in hard vertical bands. WebP is
lossless here, keeps the alpha the rounded corners need, runs at 17 ms a frame, and still comes
out around a tenth of the GIF's size.

Usage: python tools/make-preview.py
"""

import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS = REPO_ROOT / "docs"

FRAMES = 240
FRAME_DURATION_MS = 17  # 240 x 17 ms = one sweep every 4 s at just under 60 fps
MAX_KB = 1500
SATURATION_THRESHOLD = 90  # 0-255; the button's own animated effect is far above this
LABEL_DEVIATION = 28  # how far off the clean gradient a pixel must be to count as label, 0-255
EDGE_TRIM = 2  # columns at each end of the button that blend into the window; measured as 1


def find_button_mask(rgb: np.ndarray) -> np.ndarray | None:
    """The button is the largest contiguous blob of saturated colour - filled solid, so the
    label's own low-saturation pixels (holes in the raw threshold) count as part of it too."""
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    saturated = (hsv[:, :, 1] >= SATURATION_THRESHOLD).astype(np.uint8) * 255

    contours, _ = cv2.findContours(saturated, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None

    largest = max(contours, key=cv2.contourArea)
    if cv2.contourArea(largest) < 400:  # smaller than any real button at this screenshot size
        return None

    mask = np.zeros(saturated.shape, dtype=np.uint8)
    cv2.drawContours(mask, [largest], -1, color=255, thickness=-1)
    return mask


def clean_gradient(region_rgb: np.ndarray, region_mask: np.ndarray) -> np.ndarray:
    """The button's spectrum without the label on it: one row from above the text, repeated down.

    The effect is painted horizontally, so every row of the button carries the same colours - one
    row that the label does not reach describes the whole thing.

    Both ends of that row are the button's rounded edge blending into the window behind it - one
    column each, dark on the dark theme and light on the light one. Rolled around the button they
    travelled across the effect as a stripe of window colour, so they are replaced here by the
    ramp the gradient itself would carry: it runs one full cycle across the button, so its two
    ends join through the same straight interpolation as any other pair of neighbouring stops.
    """
    h, w = region_mask.shape
    row = next((r for r in range(h // 8, h // 2) if region_mask[r].sum() >= w - 2), h // 6)
    line = region_rgb[row].astype(np.float64).copy()

    left, right = line[EDGE_TRIM], line[w - 1 - EDGE_TRIM]
    span = 2 * EDGE_TRIM + 1
    for step in range(1, span):
        weight = step / span
        line[(w - 1 - EDGE_TRIM + step) % w] = right * (1.0 - weight) + left * weight

    return np.repeat(np.round(line).astype(np.uint8)[None, :, :], h, axis=0)


def label_alpha(region_rgb: np.ndarray, clean: np.ndarray) -> np.ndarray:
    """How much of each pixel belongs to the label rather than to the effect behind it.

    The text and its shadow sit far off the gradient they cover; their antialiased edges sit
    partway. Taking that distance as an alpha keeps the edges soft instead of cutting the glyphs
    out along a hard threshold, which left a rim of the old colour around every letter.
    """
    deviation = np.abs(region_rgb.astype(np.int16) - clean.astype(np.int16)).max(axis=2)
    return np.clip(deviation / LABEL_DEVIATION, 0.0, 1.0)[:, :, None]


def build_frames(rgb: np.ndarray, alpha: np.ndarray, mask: np.ndarray) -> list[Image.Image]:
    x, y, w, h = cv2.boundingRect(mask)
    region_rgb = rgb[y:y + h, x:x + w]
    region_mask = mask[y:y + h, x:x + w] > 0

    clean = clean_gradient(region_rgb, region_mask)
    label_weight = label_alpha(region_rgb, clean)
    label = region_rgb.astype(np.float64) * label_weight

    frames = []
    for i in range(FRAMES):
        wave = np.roll(clean, round(i * w / FRAMES), axis=1).astype(np.float64)
        composed = np.round(wave * (1.0 - label_weight) + label).astype(np.uint8)

        frame = np.dstack((rgb, alpha))
        region = frame[y:y + h, x:x + w, :3]
        region[region_mask] = composed[region_mask]  # outside the rounding stays as captured
        frames.append(Image.fromarray(frame, "RGBA"))

    return frames


def build_preview(src_path: Path, dst_path: Path) -> None:
    if not src_path.exists():
        print(f"skip: {src_path.name} not found")
        return

    original = Image.open(src_path).convert("RGBA")
    captured = np.array(original)
    rgb, alpha = captured[:, :, :3], captured[:, :, 3]

    mask = find_button_mask(rgb)
    if mask is None:
        raise SystemExit(f"{src_path.name}: no button-sized saturated region found - "
                         "check the screenshot actually shows the lit button")

    frames = build_frames(rgb, alpha, mask)
    frames[0].save(
        dst_path, save_all=True, append_images=frames[1:], duration=FRAME_DURATION_MS,
        loop=0, lossless=True, quality=80, method=4,
    )

    x, y, w, h = cv2.boundingRect(mask)
    size_kb = dst_path.stat().st_size / 1024
    print(f"{dst_path.name}: {len(frames)} frames at {FRAME_DURATION_MS} ms "
          f"({1000 // FRAME_DURATION_MS} fps), {size_kb:.0f} KB, button at ({x},{y}) {w}x{h}")
    if size_kb > MAX_KB:
        print(f"  WARNING: over the {MAX_KB} KB budget - reduce FRAMES")


def main() -> None:
    build_preview(DOCS / "preview-dark.png", DOCS / "preview-dark.webp")
    build_preview(DOCS / "preview-light.png", DOCS / "preview-light.webp")


if __name__ == "__main__":
    sys.exit(main())
