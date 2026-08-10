#!/usr/bin/env python3
"""Overlays an animated rainbow wave on the big button in the README preview screenshots.

Reads docs/preview-dark.png and docs/preview-light.png, finds the button automatically (the one
contiguous area of high colour saturation - no coordinates hardcoded, so a new screenshot needs
no changes here), and writes docs/preview-dark.gif and docs/preview-light.gif: the same image,
animated, with the button's own rounding and label kept exactly as the source PNG drew them.

Usage: python tools/make-preview-gif.py
"""

import colorsys
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS = REPO_ROOT / "docs"

FRAMES = 40
FRAME_DURATION_MS = 50
MAX_KB = 1500
SATURATION_THRESHOLD = 90  # 0-255; the button's own animated effect is far above this
LABEL_SATURATION_CEILING = 60  # text/shadow pixels inside the button are near-greyscale


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


def rainbow_wave(width: int, height: int, offset: float) -> np.ndarray:
    """A diagonal hue sweep, the same kind of moving gradient the app's own rainbow effect
    paints - not a pixel-perfect match of its renderer, close enough for a preview GIF."""
    xs, ys = np.meshgrid(np.arange(width), np.arange(height))
    period = max(width, height) * 0.9
    hue = (((xs + ys * 0.35) / period) + offset) % 1.0

    frame = np.empty((height, width, 3), dtype=np.uint8)
    # colorsys has no vectorised form; the button region is small enough that a Python loop
    # over its unique hue buckets (256, quantised) is fast enough without a numpy HSV path.
    lut = np.array(
        [colorsys.hsv_to_rgb(h / 255, 1.0, 1.0) for h in range(256)], dtype=np.float64
    ) * 255
    frame = lut[(hue * 255).astype(np.uint8)].astype(np.uint8)
    return frame


def build_gif(src_path: Path, dst_path: Path) -> None:
    if not src_path.exists():
        print(f"skip: {src_path.name} not found")
        return

    original = Image.open(src_path).convert("RGBA")
    rgb = np.array(original.convert("RGB"))
    alpha = np.array(original)[:, :, 3]

    mask = find_button_mask(rgb)
    if mask is None:
        raise SystemExit(f"{src_path.name}: no button-sized saturated region found - "
                          "check the screenshot actually shows the lit button")

    x, y, w, h = cv2.boundingRect(mask)
    region_rgb = rgb[y:y + h, x:x + w]
    region_mask = mask[y:y + h, x:x + w] > 0

    region_hsv = cv2.cvtColor(region_rgb, cv2.COLOR_RGB2HSV)
    label_mask = region_mask & (region_hsv[:, :, 1] < LABEL_SATURATION_CEILING)

    frames = []
    for i in range(FRAMES):
        wave = rainbow_wave(w, h, i / FRAMES)
        composed = region_rgb.copy()
        composed[region_mask] = wave[region_mask]
        composed[label_mask] = region_rgb[label_mask]  # the label, drawn back over the wave

        frame_rgb = rgb.copy()
        frame_rgb[y:y + h, x:x + w] = composed

        frame = Image.fromarray(frame_rgb, "RGB")
        frame.putalpha(Image.fromarray(alpha, "L"))
        frames.append(frame.convert("P", palette=Image.ADAPTIVE, colors=128))

    frames[0].save(
        dst_path, save_all=True, append_images=frames[1:], duration=FRAME_DURATION_MS,
        loop=0, optimize=True, disposal=2,
    )

    size_kb = dst_path.stat().st_size / 1024
    print(f"{dst_path.name}: {len(frames)} frames, {size_kb:.0f} KB, button at "
          f"({x},{y}) {w}x{h}")
    if size_kb > MAX_KB:
        print(f"  WARNING: over the {MAX_KB} KB budget - reduce FRAMES or palette colours")


def main() -> None:
    build_gif(DOCS / "preview-dark.png", DOCS / "preview-dark.gif")
    build_gif(DOCS / "preview-light.png", DOCS / "preview-light.gif")


if __name__ == "__main__":
    sys.exit(main())
