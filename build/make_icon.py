"""Thunderstore icon: 256x256 PNG. Two portal rings joined by a line that blends from one
biome colour to the other — the mod's whole idea in one shape. Bold, so it survives 64px."""
from PIL import Image, ImageDraw, ImageFilter
import math

S = 256
SS = 4                     # supersample for clean curves
W = S * SS

img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

WOOD_DARK = (38, 27, 19)
WOOD_MID = (74, 53, 36)
LIME = (158, 255, 77)      # Meadows end
TEAL = (26, 173, 140)      # Black Forest end
OUTLINE = (12, 9, 6)

# rounded-square ground, slightly lighter towards the middle
r = 44 * SS
d.rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=WOOD_DARK)
glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse(
    [W * 0.05, W * 0.15, W * 0.95, W * 0.95], fill=(WOOD_MID[0], WOOD_MID[1], WOOD_MID[2], 170))
glow = glow.filter(ImageFilter.GaussianBlur(28 * SS))
mask = Image.new("L", (W, W), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=255)
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (W, W), (0, 0, 0, 0)), mask))
d = ImageDraw.Draw(img)

# the two portals: lower-left and upper-right
a = (W * 0.27, W * 0.72)
b = (W * 0.73, W * 0.28)
ring_r = W * 0.135
ring_w = int(W * 0.052)

def lerp(c0, c1, t):
    return tuple(int(c0[i] + (c1[i] - c0[i]) * t) for i in range(3))

# soft coloured glow under each ring
for c, col in ((a, LIME), (b, TEAL)):
    g = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    ImageDraw.Draw(g).ellipse([c[0] - ring_r * 1.6, c[1] - ring_r * 1.6, c[0] + ring_r * 1.6, c[1] + ring_r * 1.6],
                              fill=(col[0], col[1], col[2], 110))
    g = g.filter(ImageFilter.GaussianBlur(18 * SS))
    img = Image.alpha_composite(img, Image.composite(g, Image.new("RGBA", (W, W), (0, 0, 0, 0)), mask))
d = ImageDraw.Draw(img)

# the link: shortened so it starts and ends at the ring edges, gradient along its length
dx, dy = b[0] - a[0], b[1] - a[1]
L = math.hypot(dx, dy)
ux, uy = dx / L, dy / L
p0 = (a[0] + ux * ring_r, a[1] + uy * ring_r)
p1 = (b[0] - ux * ring_r, b[1] - uy * ring_r)
line_w = int(W * 0.06)
outline_w = line_w + int(W * 0.028)

# Overlapping discs one pixel apart: seamless, round-capped, and the colour can change per disc.
def stroke(width, colour_at):
    n = int(L)
    for i in range(n + 1):
        t = i / n
        q = (p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t)
        d.ellipse([q[0] - width / 2, q[1] - width / 2, q[0] + width / 2, q[1] + width / 2], fill=colour_at(t))

stroke(outline_w, lambda t: OUTLINE)
stroke(line_w, lambda t: lerp(LIME, TEAL, t))

# rings, outlined
for c, col in ((a, LIME), (b, TEAL)):
    o = int(W * 0.014)
    d.ellipse([c[0] - ring_r - o, c[1] - ring_r - o, c[0] + ring_r + o, c[1] + ring_r + o],
              outline=OUTLINE, width=ring_w + 2 * o)
    d.ellipse([c[0] - ring_r, c[1] - ring_r, c[0] + ring_r, c[1] + ring_r], outline=col, width=ring_w)
    # a bright inner spark, the portal's active glow
    sr = ring_r * 0.42
    spark = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    ImageDraw.Draw(spark).ellipse([c[0] - sr, c[1] - sr, c[0] + sr, c[1] + sr], fill=(col[0], col[1], col[2], 230))
    spark = spark.filter(ImageFilter.GaussianBlur(6 * SS))
    img = Image.alpha_composite(img, spark)
    d = ImageDraw.Draw(img)

out = img.resize((S, S), Image.LANCZOS)
out.save("thunderstore/icon.png")
print("wrote thunderstore/icon.png", out.size)
