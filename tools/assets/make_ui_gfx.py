"""Default interface images (the look of panels, buttons, bars, menu icons...).

Writes into <game>/gfx/interface/ui/*.png (including the left menu icons menu_*.png). The images reproduce the engine's
built-in ornate style, so a project can open them in any image editor and change the look. interface/theme.txt points
at them (textures = { ... }); delete a line there to go back to the drawn style.

Usage: python3 tools/assets/make_ui_gfx.py <game folder>
"""
import os
import sys

from PIL import Image, ImageDraw

GAME = sys.argv[1]
UI = os.path.join(GAME, 'gfx', 'interface', 'ui')
os.makedirs(UI, exist_ok=True)

SS = 4  # supersampling for smooth edges


def lighter(c, f):
    return tuple(min(255, int(v + (255 - v) * f)) for v in c[:3]) + tuple(c[3:])


def darker(c, f):
    return tuple(int(v * (1 - f)) for v in c[:3]) + tuple(c[3:])


def gradient(w, h, top, bottom, alpha=255):
    im = Image.new('RGBA', (w, h))
    px = im.load()
    for y in range(h):
        t = y / max(1, h - 1)
        col = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (alpha,)
        for x in range(w):
            px[x, y] = col
    return im


def rect_line(d, box, col, width=1):
    x0, y0, x1, y1 = box
    d.rectangle([x0, y0, x1, y1], outline=col, width=width)


def save(im, name, folder=UI):
    im.save(os.path.join(folder, name + '.png'))


PANEL_BORDER = (168, 136, 72)
PANEL_INNER = (92, 78, 50)
GOLD = (236, 200, 100)
TEXT = (236, 230, 214)
BAD = (236, 104, 92)


def panel(name, bg, alpha):
    w = h = 64
    im = gradient(w, h, lighter(bg, 0.05), darker(bg, 0.18), alpha)
    d = ImageDraw.Draw(im)
    rect_line(d, (0, 0, w - 1, h - 1), (0, 0, 0, 210))
    rect_line(d, (1, 1, w - 2, h - 2), darker(PANEL_BORDER, 0.35) + (255,))
    rect_line(d, (3, 3, w - 4, h - 4), PANEL_INNER + (255,))
    # corner studs (diamonds), drawn large and scaled down
    big = im.resize((w * SS, h * SS), Image.NEAREST)
    bd = ImageDraw.Draw(big)
    r = 3.4 * SS
    for cx, cy in [(3, 3), (w - 4, 3), (3, h - 4), (w - 4, h - 4)]:
        X, Y = (cx + 0.5) * SS, (cy + 0.5) * SS
        bd.polygon([(X, Y - r), (X + r, Y), (X, Y + r), (X - r, Y)], fill=PANEL_BORDER + (255,))
    save(big.resize((w, h), Image.LANCZOS), name)


def button(name, bg, border, shine):
    w = h = 32
    im = gradient(w, h, lighter(bg, 0.10), darker(bg, 0.28))
    d = ImageDraw.Draw(im)
    d.line([(1, 1), (w - 2, 1)], fill=(255, 255, 255, shine))
    rect_line(d, (0, 0, w - 1, h - 1), border + (255,))
    save(im, name)


def close_button(name, top, bottom, border):
    w = h = 20
    big = gradient(w * SS, h * SS, top, bottom)
    d = ImageDraw.Draw(big)
    rect_line(d, (0, 0, w * SS - 1, h * SS - 1), border + (255,), SS)
    p = 6 * SS
    d.line([(p, p), (w * SS - p, h * SS - p)], fill=TEXT + (255,), width=2 * SS)
    d.line([(w * SS - p, p), (p, h * SS - p)], fill=TEXT + (255,), width=2 * SS)
    save(big.resize((w, h), Image.LANCZOS), name)


# panels and bars
panel('panel', (26, 28, 32), 246)
panel('menu_bar', (22, 22, 26), 236)
hdr = gradient(32, 24, lighter((58, 46, 30), 0.10), darker((58, 46, 30), 0.25))
d = ImageDraw.Draw(hdr)
d.line([(0, 22), (31, 22)], fill=PANEL_BORDER + (255,), width=2)
save(hdr, 'panel_header')
top = gradient(32, 32, lighter((40, 34, 26), 0.08), darker((40, 34, 26), 0.45), 250)
d = ImageDraw.Draw(top)
d.line([(0, 30), (31, 30)], fill=PANEL_BORDER + (255,), width=2)
save(top, 'top_bar')
tip = Image.new('RGBA', (16, 16), (16, 16, 20, 248))
rect_line(ImageDraw.Draw(tip), (0, 0, 15, 15), PANEL_BORDER + (255,))
save(tip, 'tooltip')

# buttons
button('button', (56, 58, 64), (96, 86, 66), 34)
button('button_hover', (84, 74, 52), GOLD, 34)
button('button_selected', (150, 112, 46), PANEL_BORDER, 34)
button('button_disabled', (40, 41, 45), (96, 86, 66), 12)
close_button('close_button', lighter((56, 58, 64), 0.1), darker((56, 58, 64), 0.2), (96, 86, 66))
close_button('close_button_hover', lighter(BAD, 0.1), darker(BAD, 0.3), GOLD)

# progress bars: the fill is white-ish and takes the bar's color
bar = Image.new('RGBA', (8, 8), (14, 14, 16, 230))
rect_line(ImageDraw.Draw(bar), (0, 0, 7, 7), (0, 0, 0, 160))
save(bar, 'bar')
save(gradient(8, 8, (255, 255, 255), (200, 200, 200)), 'bar_fill')

# text fields, scroll bars, list cards, section underline
for name, border in [('text_field', (96, 86, 66)), ('text_field_focused', GOLD)]:
    tf = Image.new('RGBA', (12, 12), (12, 12, 14, 230))
    rect_line(ImageDraw.Draw(tf), (0, 0, 11, 11), border + (255,))
    save(tf, name)
save(Image.new('RGBA', (4, 8), (0, 0, 0, 90)), 'scrollbar')
save(Image.new('RGBA', (4, 8), PANEL_BORDER + (190,)), 'scroll_thumb')
card = gradient(16, 16, (115, 115, 115), (72, 72, 72))
rect_line(ImageDraw.Draw(card), (0, 0, 15, 15), (0, 0, 0, 150))
save(card, 'card')
card = gradient(16, 16, lighter((78, 62, 34), 0.06), darker((78, 62, 34), 0.15))
rect_line(ImageDraw.Draw(card), (0, 0, 15, 15), PANEL_BORDER + (255,))
save(card, 'card_selected')
line = Image.new('RGBA', (64, 3), (0, 0, 0, 0))
ImageDraw.Draw(line).line([(0, 1), (63, 1)], fill=PANEL_INNER + (200,))
save(line, 'section_line')

# ------------------------------------------------------------------ menu icons (white: the game tints them)
N = 64 * SS
C = N / 2
H = N * 0.39
T = max(2, int(N * 0.045))
WHITE = (255, 255, 255, 255)


def icon(name, draw):
    im = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw(d)
    # a soft dark shadow under the shape so it reads on light buttons
    shadow = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    alpha = im.split()[3].point(lambda a: int(a * 0.55))
    shadow.paste((0, 0, 0, 255), (SS * 2, SS * 2), alpha)
    out = Image.alpha_composite(shadow, im)
    save(out.resize((64, 64), Image.LANCZOS), 'menu_' + name)


def P(dx, dy):
    return (C + dx * H, C + dy * H)


def sector(d, cx, cy, r, fill):
    d.pieslice([cx - r, cy - r, cx + r, cy + r], 180, 360, fill=fill)


def military(d):
    for s in (-1, 1):
        a, b = P(-s, 1), P(0.85 * s, -0.85)
        d.line([a, b], fill=WHITE, width=T)
        g = (a[0] + (b[0] - a[0]) * 0.22, a[1] + (b[1] - a[1]) * 0.22)
        vx, vy = b[0] - a[0], b[1] - a[1]
        ln = (vx * vx + vy * vy) ** 0.5
        px, py = -vy / ln * H * 0.28, vx / ln * H * 0.28
        d.line([(g[0] - px, g[1] - py), (g[0] + px, g[1] + py)], fill=WHITE, width=T)


def recruit(d):
    cx, cy = P(0, 0.25)
    sector(d, cx, cy, H * 0.85, WHITE)
    d.rectangle([C - H, C + H * 0.2, C + H, C + H * 0.2 + T], fill=WHITE)
    green = (126, 214, 120, 255)
    d.line([P(0.55, -0.85), P(0.55, -0.05)], fill=green, width=T)
    d.line([P(0.15, -0.45), P(0.95, -0.45)], fill=green, width=T)


def characters(d):
    x, y = P(0, -0.38)
    r = H * 0.36
    d.ellipse([x - r, y - r, x + r, y + r], fill=WHITE)
    x, y = P(0, 0.75)
    sector(d, x, y, H * 0.78, WHITE)


def production(d):
    d.rectangle([C - H, C - H * 0.1, C + H, C + H * 0.9], fill=WHITE)
    d.polygon([P(-1, -0.1), P(-0.33, -0.1), P(-1, -0.55)], fill=WHITE)
    d.polygon([P(-0.33, -0.1), P(0.33, -0.1), P(-0.33, -0.55)], fill=WHITE)
    d.rectangle([C + H * 0.45, C - H, C + H * 0.8, C - H * 0.05], fill=WHITE)


def designer(d):
    for gx in range(3):
        for gy in range(3):
            x0, y0 = C - H + gx * H * 0.7, C - H + gy * H * 0.7
            box = [x0, y0, x0 + H * 0.55, y0 + H * 0.55]
            if gx + gy < 4:
                d.rectangle(box, fill=WHITE)
            else:
                d.rectangle(box, outline=WHITE, width=max(2, T // 2))


def navy(d):
    d.polygon([P(-1, 0.05), P(-0.65, 0.55), P(0.7, 0.55), P(1, 0.05)], fill=WHITE)
    d.rectangle([C - H * 0.45, C - H * 0.3, C + H * 0.35, C + H * 0.05], fill=WHITE)
    d.line([P(-0.05, -0.3), P(-0.05, -0.9)], fill=WHITE, width=T)
    d.line([P(-1, 0.8), P(1, 0.8)], fill=(150, 112, 46, 255), width=max(2, T // 2))


def air(d):
    d.line([P(0, -0.95), P(0, 0.85)], fill=WHITE, width=int(T * 1.6))
    d.polygon([P(0, -0.35), P(-1, 0.15), P(1, 0.15)], fill=WHITE)
    d.polygon([P(0, 0.5), P(-0.45, 0.85), P(0.45, 0.85)], fill=WHITE)


def population(d):
    for dx, sc in [(-0.55, 0.75), (0.55, 0.75), (0, 1.0)]:
        x, y = P(dx, -0.35 * sc)
        r = H * 0.28 * sc
        d.ellipse([x - r, y - r, x + r, y + r], fill=WHITE)
        x, y = P(dx, 0.75)
        sector(d, x, y, H * 0.55 * sc, WHITE)


def politics(d):
    d.polygon([P(0, -0.95), P(-1, -0.45), P(1, -0.45)], fill=WHITE)
    for i in range(4):
        x0 = C - H * 0.8 + i * H * 0.48
        d.rectangle([x0, C - H * 0.38, x0 + H * 0.2, C + H * 0.57], fill=WHITE)
    d.rectangle([C - H, C + H * 0.62, C + H, C + H * 0.92], fill=WHITE)


def decisions(d):
    x0, y0, x1, y1 = C - H * 0.7, C - H * 0.85, C + H * 0.7, C + H * 0.85
    d.rectangle([x0, y0, x1, y1], fill=WHITE, outline=(0, 0, 0, 160), width=SS)
    for i in range(4):
        ly = y0 + (y1 - y0) * (0.22 + i * 0.18)
        d.line([(x0 + (x1 - x0) * 0.18, ly), (x0 + (x1 - x0) * (0.55 if i == 3 else 0.82), ly)], fill=(0, 0, 0, 160), width=max(2, int(SS * 1.4)))


for n, f in [('military', military), ('recruit', recruit), ('characters', characters), ('production', production), ('designer', designer),
             ('navy', navy), ('air', air), ('population', population), ('politics', politics), ('decisions', decisions)]:
    icon(n, f)
print('ui gfx ok')
