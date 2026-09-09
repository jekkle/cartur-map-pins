"""Generates the 256x256 Thunderstore icon from the pin icon sheet.

Thunderstore rejects anything that is not exactly 256x256 PNG, so this is scripted
rather than hand-cropped: rerun it if Assets/pin_icons.png changes.

    python tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SHEET = os.path.join(ROOT, "src", "Assets", "pin_icons.png")
OUT = os.path.join(ROOT, "package", "icon.png")

SIZE = 256
COLUMNS = 10
PARCHMENT = (242, 230, 206, 255)

# Four of the icons the mod actually places, picked to read at thumbnail size:
# mining, cave mouth, chest, beehive.
PICKS = (24, 18, 45, 6)

sheet = Image.open(SHEET).convert("RGBA")
cell = sheet.width / COLUMNS


def icon_at(index):
    col, row = index % COLUMNS, index // COLUMNS
    box = (round(col * cell), round(row * cell), round((col + 1) * cell), round((row + 1) * cell))
    return sheet.crop(box)


icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 255))
draw = ImageDraw.Draw(icon)

# Dark slate ground, warmer towards the top.
for y in range(SIZE):
    t = y / (SIZE - 1)
    draw.line([(0, y), (SIZE, y)], fill=(int(46 - 24 * t), int(44 - 24 * t), int(38 - 22 * t), 255))

# Faint contour arcs, so the ground reads as a map rather than a plain tile.
for radius in range(40, 260, 26):
    draw.arc([70 - radius, 200 - radius, 70 + radius, 200 + radius], 250, 360,
             fill=(74, 70, 58, 255), width=2)

# The pins themselves, 2x2. Pasted as-is: the sheet art is a white glyph with its own
# darker outline, and flattening it to one colour turns each icon into a blob.
for slot, index in enumerate(PICKS):
    art = icon_at(index).resize((96, 96), Image.LANCZOS)
    alpha = art.split()[3]
    x = 14 + (slot % 2) * 116
    y = 14 + (slot // 2) * 116
    shadow = Image.composite(Image.new("RGBA", (96, 96), (0, 0, 0, 160)),
                             Image.new("RGBA", (96, 96), (0, 0, 0, 0)), alpha)
    icon.alpha_composite(shadow, (x + 3, y + 3))
    icon.alpha_composite(art, (x, y))
    # Warm the white towards parchment without losing the glyph's own shading.
    wash = Image.composite(Image.new("RGBA", (96, 96), PARCHMENT[:3] + (60,)),
                           Image.new("RGBA", (96, 96), (0, 0, 0, 0)), alpha)
    icon.alpha_composite(wash, (x, y))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
icon.convert("RGB").save(OUT, "PNG")
print(f"{OUT} {icon.size}")
