"""在 Linux 上渲染 AppIcons 的路径数据，用于人工核对图标形状（WPF 无法在此运行）。

用法：python3 tools/render-icons.py  → 生成 tools/icons-preview.png
依赖：Pillow（仅开发期用，不参与构建）

支持 M/L/H/V/C/A/Z（绝对命令）——与 AppIcons.cs 的约定一致。
"""
import math
import os
import re
import sys
from PIL import Image, ImageDraw

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                   "src", "ElasticDesktopManager.Core", "Ui", "AppIcons.cs")

TOKEN = re.compile(r"[MmLlHhVvCcAaZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?")


def arc_points(x1, y1, rx, ry, phi_deg, large, sweep, x2, y2, steps=64):
    if rx == 0 or ry == 0:
        return [(x1, y1), (x2, y2)]
    phi = math.radians(phi_deg)
    cp, sp = math.cos(phi), math.sin(phi)
    dx2, dy2 = (x1 - x2) / 2.0, (y1 - y2) / 2.0
    x1p = cp * dx2 + sp * dy2
    y1p = -sp * dx2 + cp * dy2
    rx, ry = abs(rx), abs(ry)
    lam = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry)
    if lam > 1:
        s = math.sqrt(lam)
        rx *= s
        ry *= s
    num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
    den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
    co = math.sqrt(max(0.0, num / den))
    if large == sweep:
        co = -co
    cxp = co * rx * y1p / ry
    cyp = -co * ry * x1p / rx
    cx = cp * cxp - sp * cyp + (x1 + x2) / 2
    cy = sp * cxp + cp * cyp + (y1 + y2) / 2

    def ang(ux, uy, vx, vy):
        n = math.hypot(ux, uy) * math.hypot(vx, vy)
        if n == 0:
            return 0.0
        a = math.acos(max(-1.0, min(1.0, (ux * vx + uy * vy) / n)))
        return -a if ux * vy - uy * vx < 0 else a

    t1 = ang(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry)
    dt = ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    out = []
    for i in range(steps + 1):
        t = t1 + dt * i / steps
        out.append((cp * rx * math.cos(t) - sp * ry * math.sin(t) + cx,
                    sp * rx * math.cos(t) + cp * ry * math.sin(t) + cy))
    return out


def parse_path(d):
    """返回折线列表 [(points, closed)]。语法错误会抛异常。"""
    toks = TOKEN.findall(d)
    i = 0
    cx = cy = 0.0
    sx = sy = 0.0
    polys = []
    cur = []
    closed = False
    cmd = None
    arity = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "A": 7, "Z": 0}

    def flush():
        nonlocal cur, closed
        if cur:
            polys.append((cur, closed))
        cur = []
        closed = False

    while i < len(toks):
        t = toks[i]
        if re.match(r"[A-Za-z]", t):
            cmd = t
            i += 1
        elif cmd is None:
            raise ValueError(f"路径以数字开头：{t}")
        elif cmd in "Mm":
            cmd = "L" if cmd == "M" else "l"
        if cmd in "Zz":
            if cur:
                cur.append((sx, sy))
                closed = True
            flush()
            cx, cy = sx, sy
            continue
        n = arity[cmd.upper()]
        if i + n > len(toks):
            raise ValueError(f"{cmd} 参数不足（需要 {n} 个）")
        a = [float(x) for x in toks[i:i + n]]
        i += n
        if cmd == "M":
            flush()
            cx, cy = a
            sx, sy = cx, cy
            cur = [(cx, cy)]
        elif cmd == "L":
            cx, cy = a
            cur.append((cx, cy))
        elif cmd == "H":
            cx = a[0]
            cur.append((cx, cy))
        elif cmd == "V":
            cy = a[0]
            cur.append((cx, cy))
        elif cmd == "C":
            p1 = (cx, cy)
            p2, p3 = (a[0], a[1]), (a[2], a[3])
            end = (a[4], a[5])
            for k in range(1, 33):
                u = k / 32
                v = 1 - u
                x = (v ** 3) * p1[0] + 3 * (v ** 2) * u * p2[0] + 3 * v * (u ** 2) * p3[0] + (u ** 3) * end[0]
                y = (v ** 3) * p1[1] + 3 * (v ** 2) * u * p2[1] + 3 * v * (u ** 2) * p3[1] + (u ** 3) * end[1]
                cur.append((x, y))
            cx, cy = end
        elif cmd == "A":
            pts = arc_points(cx, cy, a[0], a[1], a[2], int(a[3]), int(a[4]), a[5], a[6])
            cur.extend(pts[1:])
            cx, cy = a[5], a[6]
        else:
            raise ValueError(f"不支持的命令：{cmd}")
    flush()
    return polys


def extract_icons():
    text = open(SRC, encoding="utf-8").read()
    icons = []
    for m in re.finditer(r"public const string (\w+)\s*=\s*((?:\s*\"[^\"]*\"\s*\+?)+);", text):
        name = m.group(1)
        data = "".join(re.findall(r'"([^"]*)"', m.group(2)))
        icons.append((name, data))
    return icons


def bounds(polys):
    xs = [p[0] for poly, _ in polys for p in poly]
    ys = [p[1] for poly, _ in polys for p in poly]
    return min(xs), min(ys), max(xs), max(ys)


def main():
    icons = extract_icons()
    print(f"解析到 {len(icons)} 个图标")
    cell, pad, cols, scale = 96, 10, 6, 3
    rows = (len(icons) + cols - 1) // cols
    W = cols * (cell + pad) + pad
    H = rows * (cell + pad + 18) + pad
    sheet = Image.new("RGB", (W, H), "white")
    d = ImageDraw.Draw(sheet)

    for idx, (name, data) in enumerate(icons):
        try:
            polys = parse_path(data)
        except Exception as ex:  # noqa: BLE001
            print(f"  !! {name}: 路径语法错误 → {ex}")
            continue
        x0, y0, x1, y1 = bounds(polys)
        if not (0 <= x0 and 0 <= y0 and x1 <= 24 and y1 <= 24):
            print(f"  !! {name}: 坐标超出 0..24 视图框 → ({x0:.2f},{y0:.2f})-({x1:.2f},{y1:.2f})")

        ox = pad + (idx % cols) * (cell + pad)
        oy = pad + (idx // cols) * (cell + pad + 18)
        box = Image.new("RGB", (cell, cell), "white")
        db = ImageDraw.Draw(box)
        for poly, closed in polys:
            pts = [(p[0] / 24 * (cell - 8) + 4, p[1] / 24 * (cell - 8) + 4) for p in poly]
            if closed and len(pts) > 2:
                pts = pts + [pts[0]]
            if len(pts) > 1:
                db.line(pts, fill=(40, 44, 52), width=max(2, int(1.6 * scale)), joint="curve")
                r = max(1, int(0.8 * scale))
                for e in (pts[0], pts[-1]):
                    db.ellipse([e[0] - r, e[1] - r, e[0] + r, e[1] + r], fill=(40, 44, 52))
        sheet.paste(box, (ox, oy))
        d.text((ox, oy + cell + 2), f"{name}  [{x1-x0:.0f}x{y1-y0:.0f}]", fill=(20, 20, 20))

    # 真实尺寸（18px）单独一行，检查小尺寸辨识度
    strip = Image.new("RGB", (18 * len(icons) + 8 * (len(icons) + 1), 42), "white")
    ds = ImageDraw.Draw(strip)
    xx = 8
    for name, data in icons:
        try:
            polys = parse_path(data)
        except Exception:  # noqa: BLE001
            continue
        for poly, closed in polys:
            pts = [(p[0] / 24 * 18, p[1] / 24 * 18) for p in poly]
            if closed and len(pts) > 2:
                pts = pts + [pts[0]]
            if len(pts) > 1:
                ds.line([(xx + p[0], 12 + p[1]) for p in pts], fill=(40, 44, 52), width=2, joint="curve")
        xx += 18 + 8
    out = Image.new("RGB", (max(W, strip.width), H + strip.height + 8), "white")
    out.paste(sheet, (0, 0))
    out.paste(strip, (0, H + 8))
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons-preview.png")
    out.save(path)
    print("已保存:", path, out.size)


main()
