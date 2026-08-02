# Regenerates og.png (the social-preview card). Run only when the wording changes:
#   python src/Lex.Web/wwwroot/make-og.py
# Kept in-tree so the image is reproducible rather than a mystery binary.
import pathlib

from PIL import Image, ImageDraw, ImageFont

W, H = 1200, 630
BG, FG, MUTED, ACCENT = (16, 19, 23), (232, 234, 238), (154, 163, 175), (138, 180, 248)
here = pathlib.Path(__file__).parent


def font(size, bold=False):
    for name in (("seguisb.ttf", "segoeui.ttf") if bold else ("segoeui.ttf",)) + ("arial.ttf",):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default(size)


img = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(img)
d.rectangle([0, 0, W, 8], fill=ACCENT)

d.text((72, 92), "Lex", font=font(64, True), fill=FG)
d.text((72, 186), "What did the rule say", font=font(58, True), fill=FG)
d.text((72, 254), "on a given date?", font=font(58, True), fill=ACCENT)

d.text((72, 356), "Point-in-time Luxembourg + EU law — grounded AI answers,", font=font(30), fill=MUTED)
d.text((72, 398), "per-article history, and provenance you can verify yourself.", font=font(30), fill=MUTED)

chips = ["signed indexes", "8 MCP tools", "open data (CC-BY)", "honest refusals"]
x = 72
for c in chips:
    w = d.textlength(c, font=font(24)) + 34
    d.rounded_rectangle([x, 476, x + w, 526], radius=25, outline=(42, 47, 54), width=2)
    d.text((x + 17, 489), c, font=font(24), fill=MUTED)
    x += w + 14

d.text((72, 566), "law.soufien.lu", font=font(28, True), fill=FG)

img.save(here / "og.png", optimize=True)
print("wrote", here / "og.png", img.size)
