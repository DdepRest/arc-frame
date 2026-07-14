"""
A.R.C. Frame — icon regeneration script (v3.43.4 visual refresh).

Outputs:
  • A.R.C.Icon.png                              (256×256 — repo root, source of truth per AGENTS.md)
  • MosquitoNetCalculator/Resources/app_icon.png   (256×256 — embedded build copy)
  • MosquitoNetCalculator/Resources/app_icon.ico   (multi-size: 256/128/64/48/32/16 — Windows .exe icon)

Design rationale (v3.43.4 vector refresh of "A.R.C. + mosquito net" motif):
  • Brand-color rounded square background (#005FB8 — accent) for instant
    recognition in the Windows tray at 16×16.
  • Bold geometric "A" letterform in white: outer triangle + inner cut +
    white crossbar — carries the "A.R.C." initial at every size.
  • Subtle mosquito-net horizontal line band at the bottom, fading to
    background at small sizes (only drawn for size ≥ 64) so the tray
    icon stays uncluttered.

Run:  python icon_generate.py
"""
from PIL import Image, ImageDraw

# Brand tokens (mirror Brushes.xaml SolidColorBrush Accent / OnAccent)
ACCENT = (0, 95, 184, 255)         # #005FB8 — Windows 11 Fluent blue
WHITE = (255, 255, 255, 255)       # #FFFFFF
WHITE_FAINT = (255, 255, 255, 70)  # ~27% opacity — net grid decoration


def render(size: int) -> Image.Image:
    """Render the icon at the given pixel size."""
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = size / 256.0  # design coordinates live in 256-space; scale down

    # ─── 1. Rounded-square background (Fluent icon corner radius ~17%) ──
    radius = max(1, int(44 * s))
    inset = max(1, int(2 * s))
    d.rounded_rectangle(
        [(inset, inset), (size - 1 - inset, size - 1 - inset)],
        radius=radius,
        fill=ACCENT,
    )

    # ─── 2. "A" letter — outer triangle (white) ────────────────────────
    apex = (int(128 * s), int(30 * s))
    foot_l = (int(52 * s), int(220 * s))
    foot_r = (int(204 * s), int(220 * s))
    d.polygon([apex, foot_l, foot_r], fill=WHITE)

    # ─── 3. Inner blue triangle (carves the A's hollow) ────────────────
    # v3.43.4 (review fix #3): Skip inner cut + crossbar for size < 64.
    # At 16 / 32 px the inner Accent triangle collapses to a checker dot and
    # the crossbar disappears entirely; the silhouette becomes ambiguous and
    # loses the "A" reading in the Windows tray. Full A only at >= 64 px.
    if size >= 64:
        inner_apex = (int(128 * s), int(82 * s))
        inner_l = (int(94 * s), int(220 * s))
        inner_r = (int(162 * s), int(220 * s))
        d.polygon([inner_apex, inner_l, inner_r], fill=ACCENT)

        cb_y1 = int(155 * s)
        cb_y2 = int(169 * s)
        cb_x1 = int(96 * s)
        cb_x2 = int(160 * s)
        if cb_x2 > cb_x1 + 1 and cb_y2 > cb_y1 + 1:
            d.rounded_rectangle(
                [(cb_x1, cb_y1), (cb_x2, cb_y2)],
                radius=max(1, int(3 * s)),
                fill=WHITE,
            )

    # ─── 5. Mosquito-net motif (only visible at larger sizes) ──────────
    # Small icons (16, 32) drop the decoration so the silhouette stays
    # crisp at 100% Windows DPI scaling in the tray.
    if size >= 64:
        # Two thin vertical lines (frame jambs) + one horizontal mid-rail,
        # all low-opacity white. Reads as "net grid" without competing
        # with the bold A.
        line_w = max(1, int(2 * s))
        grid_top = int(228 * s)
        grid_bot = int(250 * s)
        for fx in [96, 128, 160]:
            d.line(
                [(int(fx * s), grid_top), (int(fx * s), grid_bot)],
                fill=WHITE_FAINT, width=line_w,
            )
        d.line(
            [(int(96 * s), int(239 * s)), (int(160 * s), int(239 * s))],
            fill=WHITE_FAINT, width=line_w,
        )

    return img


def main():
    # Design sizes — generate each independently so the small ones are
    # pixel-perfect (no nearest-neighbour downscale of 256).
    ico_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]

    # 1. Root source — what AGENTS.md names as the canonical icon
    img_256 = render(256)
    img_256.save('A.R.C.Icon.png', format='PNG')
    print(f"  A.R.C.Icon.png  (256x256)")

    # 2. Build-bundle PNG copy in Resources/
    img_256.save('MosquitoNetCalculator/Resources/app_icon.png', format='PNG')
    print(f"  MosquitoNetCalculator/Resources/app_icon.png  (256x256)")

    # 3. Multi-size ICO bundle for Windows ApplicationIcon
    img_256.save(
        'MosquitoNetCalculator/Resources/app_icon.ico',
        format='ICO',
        sizes=ico_sizes,
    )
    print(f"  MosquitoNetCalculator/Resources/app_icon.ico  (multi-size: "
          f"{', '.join(f'{w}x{h}' for w, h in ico_sizes)})")


if __name__ == '__main__':
    main()
