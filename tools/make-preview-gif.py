#!/usr/bin/env python3
"""Overlays the app's own animated rainbow on the big button in the README preview screenshots.

Reads docs/preview-dark.png and docs/preview-light.png, finds the button automatically (the one
contiguous area of high colour saturation - no coordinates hardcoded, so a new screenshot needs
no changes here), and writes docs/preview-dark.gif and docs/preview-light.gif: the same image,
animated, with the button's own rounding and label kept exactly as the source PNG drew them.

The wave is the same spectrum EffectPainter.Spectrum paints in the app (src/Ui.cs): the same
colour stops, one cycle across the button, horizontal, one full sweep every 3 seconds - so the
README shows what the tool actually does, not a lookalike.

The window's rounded corners stay transparent in the GIF, and any desktop background the
screenshot caught along an edge is replaced by the window's own colour, so the preview sits on
the README page with no grey box around it.

Usage: python tools/make-preview-gif.py
"""

import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS = REPO_ROOT / "docs"

FRAMES = 40
FRAME_DURATION_MS = 70  # GIF delays are whole centiseconds: 40 x 70 ms ~ the app's 3 s period
MAX_KB = 1500
SATURATION_THRESHOLD = 90  # 0-255; the button's own animated effect is far above this
LABEL_SATURATION_CEILING = 60  # text/shadow pixels inside the button are near-greyscale
EDGE_MARGIN = 16  # how far in from each edge stray desktop background is looked for
EDGE_TOLERANCE = 20  # per-channel distance from the window colour that counts as "not the window"
PALETTE_COLOURS = 128  # per frame; the spectrum dithers cleanly at this size and stays small
TRANSPARENT_INDEX = 255  # palette slot kept free for the rounded corners

# EffectPainter.SpectrumStops, src/Ui.cs - evenly spaced, first stop repeated last so it wraps.
SPECTRUM_STOPS = np.array([
    (255, 0, 0), (255, 200, 0), (120, 255, 0), (0, 255, 140), (0, 200, 255),
    (60, 90, 255), (190, 60, 255), (255, 0, 160), (255, 0, 0),
], dtype=np.float64)


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
    """One row of the app's spectrum, repeated down the button: a straight left-to-right sweep
    of EffectPainter's stops, one cycle across the full width, shifted right by `offset`.

    Mirrors Spectrum() in src/Ui.cs, which tiles a gradient of the same stops over the button
    width and translates it by the phase - so the colour at x is the blend at (x/width - offset).
    """
    pos = ((np.arange(width) / width) - offset) % 1.0
    scaled = pos * (len(SPECTRUM_STOPS) - 1)
    lower = scaled.astype(int)
    t = (scaled - lower)[:, None]

    row = SPECTRUM_STOPS[lower] * (1 - t) + SPECTRUM_STOPS[lower + 1] * t
    return np.repeat(np.round(row).astype(np.uint8)[None, :, :], height, axis=0)


def clean_edges(rgb: np.ndarray, opaque: np.ndarray) -> None:
    """Replaces desktop background the screenshot caught along an edge with the window's own
    colour, in place. The capture kept a few columns of the desktop on the left; without this
    they show up as a grey strip beside the window in the README."""
    window = np.median(rgb[opaque], axis=0)

    def is_window(line: np.ndarray, mask: np.ndarray) -> bool:
        visible = line[mask]
        return (len(visible) == 0
                or np.max(np.abs(np.median(visible, axis=0) - window)) <= EDGE_TOLERANCE)

    for axis in (0, 1):  # rows, then columns through a transposed view of the same buffer
        view = rgb if axis == 0 else rgb.transpose(1, 0, 2)
        mask = opaque if axis == 0 else opaque.T
        for step, start in ((1, 0), (-1, len(view) - 1)):
            edge = start
            while abs(edge - start) < EDGE_MARGIN and not is_window(view[edge], mask[edge]):
                edge += step
            # Fill what was stripped with the first genuine line, so a border or gradient on
            # that edge carries on instead of ending in a hard step.
            for i in range(start, edge, step):
                view[i][mask[i]] = view[edge][mask[i]]


def match_gain(region_rgb: np.ndarray, wave_mask: np.ndarray) -> float:
    """How bright the button's spectrum sits in this screenshot, relative to the raw stops.

    The app dims the effect under the label (EffectButton's wash), so the raw stops would come
    out brighter than every other pixel of the same capture. Rather than hardcoding that factor,
    the wave is fitted to the button as photographed: the best-matching phase, then a least
    squares gain. A screenshot taken hovering, or with a different wash, still lines up.
    """
    h, w = wave_mask.shape
    row = h // 6  # above the label, so the fit sees the effect and not the text
    taken = wave_mask[row]
    if taken.sum() < w // 2:
        return 1.0

    target = region_rgb[row][taken].astype(np.float64)
    best = (1.0, None)
    for step in range(720):
        wave = rainbow_wave(w, 1, step / 720)[0][taken].astype(np.float64)
        gain = float((wave * target).sum() / (wave * wave).sum())
        error = np.abs((wave * gain) - target).mean()
        if best[1] is None or error < best[1]:
            best = (gain, error)

    return min(best[0], 1.0)


def quantise(frame_rgb: np.ndarray, opaque: np.ndarray) -> Image.Image:
    """One palette frame whose transparent pixels sit on a reserved index, so the window's
    rounded corners stay see-through instead of turning into grey nubs on the README."""
    flat = frame_rgb.copy()
    flat[~opaque] = frame_rgb[opaque][0]  # keep the corners out of the palette's colour budget

    quantised = Image.fromarray(flat, "RGB").convert(
        "P", palette=Image.ADAPTIVE, colors=PALETTE_COLOURS)

    indices = np.array(quantised)
    indices[~opaque] = TRANSPARENT_INDEX
    out = Image.fromarray(indices, "P")
    out.putpalette((quantised.getpalette() + [0, 0, 0] * 256)[:768])
    return out


def build_gif(src_path: Path, dst_path: Path) -> None:
    if not src_path.exists():
        print(f"skip: {src_path.name} not found")
        return

    original = Image.open(src_path).convert("RGBA")
    rgb = np.array(original.convert("RGB"))
    opaque = np.array(original)[:, :, 3] >= 128  # the corners are the only transparent pixels

    clean_edges(rgb, opaque)

    mask = find_button_mask(rgb)
    if mask is None:
        raise SystemExit(f"{src_path.name}: no button-sized saturated region found - "
                          "check the screenshot actually shows the lit button")

    x, y, w, h = cv2.boundingRect(mask)
    region_rgb = rgb[y:y + h, x:x + w]
    region_mask = mask[y:y + h, x:x + w] > 0

    region_hsv = cv2.cvtColor(region_rgb, cv2.COLOR_RGB2HSV)
    label_mask = region_mask & (region_hsv[:, :, 1] < LABEL_SATURATION_CEILING)

    gain = match_gain(region_rgb, region_mask & ~label_mask)

    frames = []
    for i in range(FRAMES):
        wave = (rainbow_wave(w, h, i / FRAMES) * gain).astype(np.uint8)
        composed = region_rgb.copy()
        composed[region_mask] = wave[region_mask]
        composed[label_mask] = region_rgb[label_mask]  # the label, drawn back over the wave

        frame_rgb = rgb.copy()
        frame_rgb[y:y + h, x:x + w] = composed
        frames.append(quantise(frame_rgb, opaque))

    frames[0].save(
        dst_path, save_all=True, append_images=frames[1:], duration=FRAME_DURATION_MS,
        loop=0, optimize=True, disposal=2, transparency=TRANSPARENT_INDEX,
    )

    size_kb = dst_path.stat().st_size / 1024
    print(f"{dst_path.name}: {len(frames)} frames, {size_kb:.0f} KB, button at "
          f"({x},{y}) {w}x{h}, spectrum at {gain:.0%}")
    if size_kb > MAX_KB:
        print(f"  WARNING: over the {MAX_KB} KB budget - reduce FRAMES or palette colours")


def main() -> None:
    build_gif(DOCS / "preview-dark.png", DOCS / "preview-dark.gif")
    build_gif(DOCS / "preview-light.png", DOCS / "preview-light.gif")


if __name__ == "__main__":
    sys.exit(main())
