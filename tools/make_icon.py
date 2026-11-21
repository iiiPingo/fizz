# Fizz icon: white soda can with a frametime pulse, on a red -> pink gradient tile.
import sys
from PIL import Image, ImageDraw
RED, PINK = (190, 28, 84), (232, 70, 138)  # deeper cherry, close to the original #FF5C9E logo

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def icon(size):
    S = 8; W = size * S
    small = size <= 24
    # gradient tile (diagonal, top-left red -> bottom-right pink)
    grad = Image.new("RGBA", (W, W))
    gp = grad.load()
    for y in range(W):
        for x in range(W):
            t = (x + y) / (2 * W - 2)
            gp[x, y] = lerp(RED, PINK, t) + (255,)
    mask = Image.new("L", (W, W), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([0, 0, W - 1, W - 1], radius=int(W * (0.22 if not small else 0.2)), fill=255)
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    im.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(im)
    k = W / 160.0
    # soda can: tapered neck, pull tab, body, rim lines
    cx = 80 * k
    cw = (80 if small else 66) * k
    top, bot = (30 if small else 34) * k, (136 if small else 130) * k
    neck = (10 if small else 9) * k
    white = (255, 255, 255, 255)
    tint = (255, 214, 230, 255)
    d.polygon([(cx - cw / 2, top + neck), (cx - cw * 0.38, top), (cx + cw * 0.38, top), (cx + cw / 2, top + neck)], fill=white)
    d.rounded_rectangle([cx - cw / 2, top + neck - 1, cx + cw / 2, bot], radius=int(9 * k), fill=white)
    d.rounded_rectangle([cx - cw * 0.16, top - 9 * k, cx + cw * 0.16, top + 1 * k], radius=int(4 * k), fill=tint)  # pull tab
    rim = int((5 if small else 3.5) * k)
    d.line([(cx - cw / 2 + 4 * k, top + neck + 3 * k), (cx + cw / 2 - 4 * k, top + neck + 3 * k)], fill=tint, width=rim)
    d.line([(cx - cw / 2 + 4 * k, bot - 9 * k), (cx + cw / 2 - 4 * k, bot - 9 * k)], fill=tint, width=rim)
    # frametime pulse across the can
    mid = (top + bot) / 2 + 4 * k
    x0, x1 = cx - cw / 2, cx + cw / 2
    pts = [(x0, mid), (x0 + cw * .22, mid), (x0 + cw * .36, mid - 22 * k), (x0 + cw * .54, mid + 24 * k), (x0 + cw * .68, mid - 10 * k), (x0 + cw * .78, mid), (x1, mid)]
    lw = int((13 if small else 8) * k)
    d.line(pts, fill=RED + (255,), width=lw, joint="curve")
    for p in pts[1:-1]:
        d.ellipse([p[0] - lw / 2, p[1] - lw / 2, p[0] + lw / 2, p[1] + lw / 2], fill=RED + (255,))
    if not small:  # bubbles
        for bx, by, r in ((128, 34, 8), (138, 56, 5)):
            d.ellipse([(bx - r) * k, (by - r) * k, (bx + r) * k, (by + r) * k], fill=(255, 255, 255, 230))
    return im.resize((size, size), Image.LANCZOS)

def tray(size):
    """tray icon: just the can (no pink tile), white with the pulse in Fizz pink, filling the square"""
    S = 8; W = size * S
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    k = W / 100.0
    cx, cw = 50 * k, 70 * k
    top, bot = 12 * k, 96 * k
    neck = 8 * k
    white = (255, 255, 255, 255)
    pink = (255, 92, 158, 255)
    d.polygon([(cx - cw / 2, top + neck), (cx - cw * 0.38, top), (cx + cw * 0.38, top), (cx + cw / 2, top + neck)], fill=white)
    d.rounded_rectangle([cx - cw / 2, top + neck - 1, cx + cw / 2, bot], radius=int(8 * k), fill=white)
    d.rounded_rectangle([cx - cw * 0.18, top - 9 * k, cx + cw * 0.18, top + 1 * k], radius=int(3 * k), fill=white)  # pull tab
    mid = (top + bot) / 2 + 3 * k
    x0, x1 = cx - cw / 2, cx + cw / 2
    pts = [(x0, mid), (x0 + cw * .20, mid), (x0 + cw * .36, mid - 18 * k), (x0 + cw * .54, mid + 20 * k), (x0 + cw * .70, mid - 8 * k), (x0 + cw * .80, mid), (x1, mid)]
    lw = int(11 * k)
    d.line(pts, fill=pink, width=lw, joint="curve")
    for p in pts[1:-1]:
        d.ellipse([p[0] - lw / 2, p[1] - lw / 2, p[0] + lw / 2, p[1] + lw / 2], fill=pink)
    # thin dark edge under the can so it still reads on a light taskbar
    from PIL import ImageFilter
    a = im.split()[3].filter(ImageFilter.MaxFilter(int(W * 0.07) | 1))
    edge = Image.new("RGBA", (W, W), (24, 24, 24, 200))
    out = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    out.paste(edge, (0, 0), a)
    out.alpha_composite(im)
    return out.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
if len(sys.argv) > 3:   # make_icon.py app.ico sheet.png tray.ico
    tsz = [16, 20, 24, 32, 40, 48, 64]
    timgs = [tray(s) for s in tsz]
    timgs[-1].save(sys.argv[3], format="ICO", sizes=[(s, s) for s in tsz], append_images=timgs[:-1])
imgs = [icon(s) for s in sizes]
imgs[-1].save(sys.argv[1], format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
sheet = Image.new("RGBA", (sum(sizes) + 10 * len(sizes) + 10, 276), (32, 32, 32, 255))
x = 10
for s, im in zip(sizes, imgs):
    sheet.alpha_composite(im, (x, 10)); x += s + 10
sheet.save(sys.argv[2])
