#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Figma node JSON -> A2UI USS (Scheme A 转换链)。

输入：Figma 节点导出 JSON（两种来源都行）
  1) Figma 插件（figma_plugin/code.js）在 Figma 里选中节点后导出的 .json
  2) Figma REST API：GET /v1/files/{key}/nodes?ids={id} 的返回
输出：Styles/FigmaExport/ 下两套 USS
  - FigmaTokens.uss    设计抽出的 M3 语义令牌（颜色/圆角/字号），挂在 .a2ui-skin--figma-export
  - FigmaComponents.uss 消费令牌的组件皮肤（A2UI 既有类名，深色座舱调校）

设计原则（与项目规矩一致）：Figma 只喂 令牌/字号/圆角，不喂运行时组件树；
组件布局与行为仍由 C# Mapper + 既有 USS 决定。本脚本产出的组件皮肤只是「皮」，
颜色全部走 var(--a2ui-color-*)，切主题只改变量、组件零改动。

用法：
  python figma_to_uss.py --input figma_samples/cabin_board.json
  python figma_to_uss.py --input figma_nodes_pull.json --outdir ../../Assets/A2UISchemeA/Styles/FigmaExport
"""
from __future__ import annotations

import argparse
import json
import math
import pathlib
import re
import statistics
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "Tools/figma_samples/cabin_board.json"
OUTDIR = ROOT / "Assets/A2UISchemeA/Styles/FigmaExport"


# ---------- 颜色工具 ----------
def to_255(c: dict) -> tuple:
    return (round(c.get("r", 0) * 255), round(c.get("g", 0) * 255), round(c.get("b", 0) * 255))


def rgb(r, g, b) -> str:
    # 输出十六进制：本引擎（团结/Unity 2022.3）USS 解析器拒绝自定义属性值里的
    # rgb() 函数，会导致 var() 取到无效值、整条样式失效。统一用 hex 最稳妥。
    return f"#{r:02X}{g:02X}{b:02X}"


def lum(r, g, b) -> float:
    def f(v):
        v /= 255.0
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)


def sat(r, g, b) -> float:
    mx, mn = max(r, g, b), min(r, g, b)
    if mx == 0:
        return 0.0
    return (mx - mn) / mx


def hsl_sat(r, g, b) -> float:
    mx, mn = max(r, g, b), min(r, g, b)
    l = (mx + mn) / 2
    if mx == mn:
        return 0.0
    d = mx - mn
    return d / (255 - abs(mx + mn - 255)) if l > 127.5 else d / (mx + mn)


def colorful(c: tuple) -> bool:
    """判断是否为「有彩色的强调色」：排除近白/近黑，且 RGB 色差足够大。
    （HSL 饱和度对近白色会虚高，改用 max-min 差值更稳。）"""
    r, g, b = c
    mx, mn = max(c), min(c)
    d = mx - mn
    l = lum(*c)
    if l > 0.9 or l < 0.08:
        return False
    return d >= 40


def mix(a, b, t) -> tuple:
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


# ---------- 遍历抽取 ----------
def walk(n: dict, colors: list, radii: list, sizes: list, nodes: list = None, path: str = "root") -> None:
    if not isinstance(n, dict):
        return
    for f in (n.get("fills") or []):
        if f.get("type") == "SOLID" and f.get("visible", True) is not False:
            op = f.get("opacity", 1)
            if op is None:
                op = 1
            if op >= 0.5:
                colors.append(to_255(f.get("color") or {}))
    cr = n.get("cornerRadius")
    if isinstance(cr, (int, float)) and cr > 0:
        radii.append(cr)
    st = n.get("style") or {}
    if isinstance(st.get("fontSize"), (int, float)):
        sizes.append(st["fontSize"])

    # 收集节点级属性快照
    if nodes is not None:
        bb = n.get("absoluteBoundingBox") or {}
        solid_fill = None
        for f in (n.get("fills") or []):
            if f.get("type") == "SOLID" and f.get("visible", True) is not False:
                solid_fill = to_255(f.get("color") or {})
                break
        nodes.append({
            "name": n.get("name", ""),
            "type": n.get("type", ""),
            "path": path,
            "width": bb.get("width"),
            "height": bb.get("height"),
            "cornerRadius": cr,
            "paddingLeft": n.get("paddingLeft"),
            "paddingRight": n.get("paddingRight"),
            "paddingTop": n.get("paddingTop"),
            "paddingBottom": n.get("paddingBottom"),
            "itemSpacing": n.get("itemSpacing"),
            "layoutMode": n.get("layoutMode"),
            "primaryAxisAlignItems": n.get("primaryAxisAlignItems"),
            "counterAxisAlignItems": n.get("counterAxisAlignItems"),
            "fillColor": solid_fill,
            "fontSize": st.get("fontSize"),
            "fontWeight": (st.get("fontWeight") or 400),
            "textAlignHorizontal": st.get("textAlignHorizontal"),
            "characters": n.get("characters"),
        })

    child_path = path + "/" + str(n.get("name", ""))
    for ch in n.get("children") or []:
        walk(ch, colors, radii, sizes, nodes, child_path)


def find_document(data: dict) -> dict:
    # 兼容三种来源：
    #  - Figma REST API：{"nodes": {"<id>": {"document": {...}}}}
    #  - 本插件导出：    {"nodes": [{"document": {...}}]}
    #  - 裸节点 / 手写：  {"document": {...}} 或本身就是节点
    if "nodes" in data and data.get("nodes"):
        nodes = data["nodes"]
        first = next(iter(nodes.values())) if isinstance(nodes, dict) else nodes[0]
        if isinstance(first, dict) and "document" in first:
            return first["document"]
        return first if isinstance(first, dict) else {}
    if "document" in data:
        return data["document"]
    return data


def extract(data: dict) -> dict:
    doc = find_document(data)
    colors, radii, sizes, nodes = [], [], [], []
    walk(doc, colors, radii, sizes, nodes)
    return {"colors": colors, "radii": radii, "sizes": sizes, "nodes": nodes}


# ---------- 名字规范扫描：'big / Text:h1' 'playBtn / Button:primary' ----------
# 模板规范（Design/a2ui-design-spec.html）：组件节点命名 '别名 / 组件[:variant]'。
# 命名给出确定性语义映射（variant 在名字里、真值在实例属性里），
# 没有规范名的老稿自动退回启发式——两层互补，同一份代码兼容两类输入。
SPEC_RE = re.compile(r"^([A-Za-z][A-Za-z0-9]*)(?::([A-Za-z0-9]+))?$")
COMP_FRAME_TYPES = {"FRAME", "RECTANGLE", "COMPONENT", "INSTANCE"}
# 这些组件的子树文本是「组件内文字」（onPrimary/onSecondaryContainer 等），不算内容文本
TEXT_HOSTING_SPECS = {"Button", "Chip", "Toggle", "TextField", "Slider", "Icon"}


def parse_spec_name(name: str):
    """'playBtn / Button:primary' -> ('Button', 'primary')；无规范名返回 None。"""
    if not name or "/" not in name:
        return None
    last = name.split("/")[-1].strip()
    m = SPEC_RE.match(last)
    if not m:
        return None
    return m.group(1), (m.group(2) or "")


def _solid_fill(n: dict):
    for f in (n.get("fills") or []):
        if f.get("type") == "SOLID" and f.get("visible", True) is not False:
            op = f.get("opacity", 1)
            if op is None:
                op = 1
            if op >= 0.5:
                return to_255(f.get("color") or {})
    return None


def scan_specs(data: dict) -> dict:
    """扫描名字规范节点。
    frames[(组件, variant)] -> [节点]；texts[(组件, variant)] -> [条目]，
    条目 = {size, lh, color, comp}，comp = 祖先组件 spec 链（判定「组件内文字」）。"""
    frames: dict = {}
    texts: dict = {}

    def walk(n: dict, comp_stack: tuple) -> None:
        name, typ = n.get("name") or "", n.get("type") or ""
        spec = parse_spec_name(name)
        pushed = None
        if spec:
            a2, var = spec
            if typ == "TEXT":
                st = n.get("style") or {}
                texts.setdefault((a2, var), []).append({
                    "size": st.get("fontSize"),
                    "lh": st.get("lineHeightPx"),
                    "color": _solid_fill(n),
                    "comp": comp_stack,
                })
            elif typ in COMP_FRAME_TYPES:
                frames.setdefault((a2, var), []).append(n)
                if a2 in TEXT_HOSTING_SPECS:
                    pushed = (a2, var)
        for ch in n.get("children") or []:
            walk(ch, comp_stack + ((pushed,) if pushed else ()))

    walk(find_document(data), ())
    return {"frames": frames, "texts": texts}


def _first_fill(nodes: list):
    for nd in nodes:
        fc = _solid_fill(nd)
        if fc:
            return fc
    return None


def _top_text(entries: list, want: str):
    """want='size'|'color'：优先取「组件外」的内容文本，其次任意。"""
    if not entries:
        return None
    for top_only in (True, False):
        for e in entries:
            if top_only and e["comp"]:
                continue
            v = e.get(want)
            if isinstance(v, (int, float)) or (want == "color" and v):
                return e
    return None


def apply_spec_tokens(pal: dict, t: dict, spec: dict) -> tuple:
    """名字规范驱动的令牌覆盖。无规范节点时原样返回（老稿走启发式）。"""
    frames = spec.get("frames", {})
    texts = spec.get("texts", {})
    if not frames and not texts:
        return pal, t

    def in_comp(e, a2, var=None):
        c = e.get("comp")
        return bool(c) and c[0][0] == a2 and (var is None or c[0][1] == var)

    # ---- 颜色：从命名实例取真值 ----
    btn_primary = frames.get(("Button", "primary")) or []
    btn_plain = frames.get(("Button", "")) or []
    chip_unchecked = frames.get(("Chip", "")) or []
    toggle_unchecked = frames.get(("Toggle", "")) or []

    c = _first_fill(btn_primary) or _first_fill(chip_unchecked + toggle_unchecked)
    if c:
        pal["primary"] = c
    for entries in (texts.get(("Text", "body")), texts.get(("Text", ""))):
        for e in entries or []:
            if in_comp(e, "Button", "primary") and e["color"]:
                pal["onPrimary"] = e["color"]
                break
        else:
            continue
        break
    c = _first_fill(btn_plain)
    if c:
        pal["secondaryContainer"] = c
        for e in texts.get(("Text", "body")) or []:
            if in_comp(e, "Button", "") and e["color"]:
                pal["onSecondaryContainer"] = e["color"]
                break
    c = _first_fill(frames.get(("Card", "")) or [])
    if c:
        pal["surface"] = c
    e = _top_text(texts.get(("Text", "body")), "color")
    if e and e["color"]:
        pal["onSurface"] = e["color"]
    e = _top_text(texts.get(("Text", "caption")), "color")
    if e and e["color"]:
        pal["onSurfaceVariant"] = e["color"]
    # surfaceVariant：Chip 未选中实体的 fill（Toggle 命名给在整行 Frame 上，fill 是内容底色，
    # 其复选框矩形反而无命名——Chip 的容器 fill 才是 M3 surfaceVariant 语义）
    c = _first_fill(chip_unchecked) or _first_fill(toggle_unchecked)
    if c:
        pal["surfaceVariant"] = c
        pal["surfaceContainer"] = mix(c, pal["surface"], 0.5)
    c = _first_fill(frames.get(("Divider", "")) or [])
    if c:
        pal["outlineVariant"] = c
    for entries in texts.values():
        for e2 in entries:
            if in_comp(e2, "TextField") and e2["color"]:
                pal["outline"] = e2["color"]
                break
        else:
            continue
        break
    # background 跟随 surface：组件卡即应用底色；画板演示灰底不进令牌
    pal["background"] = pal["surface"]
    pal["onBackground"] = pal["onSurface"]
    pal["inverseSurface"] = pal["onSurface"]
    pal["inverseOnSurface"] = pal["background"]
    pal["inversePrimary"] = pal["primary"]
    # error：全部文本色里找真红（M3 模板把 error 色用在警示 caption 上）
    for entries in texts.values():
        for e2 in entries:
            c2 = e2["color"]
            if c2 and c2[0] > 150 and c2[1] < c2[0] * 0.6 and c2[2] < c2[0] * 0.5:
                pal["error"] = c2
                pal["onError"] = (255, 255, 255)
                pal["errorContainer"] = mix(c2, pal["background"], 0.7)
                pal["onErrorContainer"] = c2
                break
        else:
            continue
        break

    # ---- 字号：Text:<variant> 实例真值，不再靠排序猜 ----
    def spec_size(var: str):
        e3 = _top_text(texts.get(("Text", var)), "size")
        if e3 and isinstance(e3.get("size"), (int, float)):
            return int(e3["size"])
        return None

    for var in ("h1", "h2", "h3", "h4", "body", "caption"):
        v = spec_size(var)
        if v:
            t[var] = v
    if not texts.get(("Text", "display")):
        t["display"] = t["h1"]
    if not texts.get(("Text", "h5")):
        t["h5"] = max(12, min(t["body"], t["h4"]) - 4)
    return pal, t


def spec_geometry(spec: dict) -> dict:
    """从命名实例提取组件几何（padding/圆角/派生高度），换稿即跟随、不再硬编码。"""
    frames = spec.get("frames", {})
    texts = spec.get("texts", {})
    geo: dict = {}

    def pads(nds: list):
        def med(key):
            vals = [nd.get(key) for nd in nds if isinstance(nd.get(key), (int, float))]
            return statistics.median(vals) if vals else None
        return med("paddingTop"), med("paddingBottom"), med("paddingLeft"), med("paddingRight")

    def cr(nds: list):
        vals = [nd.get("cornerRadius") for nd in nds if isinstance(nd.get("cornerRadius"), (int, float))]
        return int(statistics.mode(vals)) if vals else None

    def body_lh_in(a2: str):
        vals = [e["lh"] for e in texts.get(("Text", "body")) or []
                if e["comp"] and e["comp"][0][0] == a2 and isinstance(e.get("lh"), (int, float))]
        return max(vals) if vals else None

    btns = (frames.get(("Button", "primary")) or []) + (frames.get(("Button", "")) or [])
    if btns:
        pt, pb, pl, pr = pads(btns)
        lh = body_lh_in("Button") or 23.0
        if pt is not None and pb is not None:
            geo["btn_h"] = int(math.ceil((pt + pb + lh) / 4) * 4)
            geo["btn_pad_tb"] = int(pt)
            geo["btn_pad_lr"] = int(pl)
        v = cr(btns)
        if v:
            geo["btn_cr"] = v
        c = _first_fill(frames.get(("Button", "primary")) or [])
        if c:
            geo["btn_fill"] = c

    tfs = frames.get(("TextField", "")) or []
    if tfs:
        pt, pb, pl, pr = pads(tfs)
        if pt is not None and pb is not None:
            lh = body_lh_in("TextField") or 23.0
            geo["tf_h"] = int(math.ceil((pt + pb + lh) / 4) * 4)
            geo["tf_pad_tb"] = int(pt)
            geo["tf_pad_lr"] = int(pl)
        v = cr(tfs)
        if v:
            geo["tf_cr"] = v
        c = _first_fill(tfs)
        if c:
            geo["tf_fill"] = c

    chips = (frames.get(("Chip", "")) or []) + (frames.get(("Chip", "checked")) or [])
    if chips:
        pt, pb, pl, pr = pads(chips)
        if pt is not None and pl is not None:
            geo["chip_pad_tb"] = int(pt)
            geo["chip_pad_lr"] = int(pl)
        v = cr(chips)
        if v:
            geo["chip_cr"] = v

    cards = frames.get(("Card", "")) or []
    if cards:
        pt, pb, pl, pr = pads(cards)
        if pt is not None:
            geo["card_pad_tb"] = int(pt)
            geo["card_pad_lr"] = int(pl if pl is not None else pr)
        v = cr(cards)
        if v:
            geo["card_cr"] = v

    # MediaMiniBar → cabin 座舱卡（radius/padding 实例真值）
    minibars = (frames.get(("MediaMiniBar", "")) or []) + (frames.get(("MediaMiniBar", "checked")) or [])
    if minibars:
        pt, pb, pl, pr = pads(minibars)
        if pt is not None:
            geo["cabin_pad_tb"] = int(pt)
            geo["cabin_pad_lr"] = int(pl if pl is not None else pr)
        v = cr(minibars)
        if v:
            geo["cabin_cr"] = v

    return geo


# ---------- 颜色 -> M3 角色映射（启发式，适配深色座舱） ----------
def map_palette(colors: list) -> dict:
    uniq = sorted({c for c in colors if all(0 <= v <= 255 for v in c)})
    if not uniq:
        uniq = [(14, 17, 22), (28, 34, 48), (45, 212, 191), (230, 237, 243)]
    lumd = [(c, lum(*c)) for c in uniq]
    darks = [c for c, l in lumd if l < 0.25]
    lights = [c for c, l in lumd if l > 0.6]
    darks.sort(key=lambda c: lum(*c))
    lights.sort(key=lambda c: lum(*c))
    cnt = Counter(colors)

    # 背景/表面：取「最频繁的中性色」（低饱和），亮稿自然抽到白、暗稿抽到黑；
    # 不再硬把暗色当背景，这样亮色 App 稿也能正确抽令牌。
    neutrals = [c for c in uniq if sat(*c) < 0.30]
    if neutrals:
        # 背景 = 最高频中性色；surface 取同调性下、与背景差一档的真实灰，
        # 避免把稿子里的深色组件（如导航条）误当通用表面
        background = max(neutrals, key=lambda c: cnt[c])
        if lum(*background) < 0.5:  # 暗稿
            cand = sorted([c for c in neutrals if lum(*c) >= lum(*background) - 1e-6],
                          key=lambda c: lum(*c))
            surface = cand[1] if len(cand) > 1 else mix(background, (255, 255, 255), 0.06)
        else:  # 亮稿
            cand = sorted([c for c in neutrals if lum(*c) <= lum(*background) + 1e-6],
                          key=lambda c: lum(*c), reverse=True)
            surface = cand[1] if len(cand) > 1 else mix(background, (0, 0, 0), 0.05)
        surface_container = mix(surface, background, 0.5)
        surface_variant = mix(surface, background, 0.25)
    else:
        background = darks[0] if darks else uniq[0]
        surface = darks[1] if len(darks) > 1 else background
        surface_container = darks[2] if len(darks) > 2 else surface
        surface_variant = darks[3] if len(darks) > 3 else surface_container

    # outline = 中性色里偏暗的；否则由背景混白得到
    gray_neutrals = [c for c in neutrals if lum(*c) < 0.6]
    outline = gray_neutrals[0] if gray_neutrals else mix(background, (230, 237, 243), 0.18)
    outline_variant = mix(background, (230, 237, 243), 0.30)

    # 前景取与背景对比的相反亮度（亮稿→黑字，暗稿→白字）
    on_surface = (0, 0, 0) if lum(*background) > 0.5 else (255, 255, 255)
    on_surface_variant = mix(on_surface, background, 0.30) if lum(*background) > 0.5 else mix(on_surface, background, 0.35)

    # 主色按「出现频次优先、其次色度」：座舱里青色用得最多，应作主色而非最艳的琥珀
    accents = sorted(((c, cnt[c], max(c) - min(c)) for c in uniq if colorful(c)),
                     key=lambda x: (x[1], x[2]), reverse=True)
    acc = [c for c, _, _ in accents]
    primary = acc[0] if acc else (45, 212, 191)
    secondary = acc[1] if len(acc) > 1 else mix(primary, (91, 141, 239), 0.5)
    tertiary = acc[2] if len(acc) > 2 else (245, 166, 35)

    # error = 真正的红（r 高、g/b 都明显偏低），避免把琥珀色误判为红
    reds = [c for c in uniq if c[0] > 150 and c[1] < c[0] * 0.6 and c[2] < c[0] * 0.5]
    error = reds[0] if reds else (255, 90, 95)

    def on(c):
        return background if lum(*c) > 0.5 else (255, 255, 255)

    return {
        "primary": primary, "onPrimary": on(primary),
        "primaryContainer": surface_variant, "onPrimaryContainer": on_surface,
        "secondary": secondary, "onSecondary": on(secondary),
        "secondaryContainer": surface_variant, "onSecondaryContainer": on_surface,
        "tertiary": tertiary, "onTertiary": on(tertiary),
        "error": error, "onError": (255, 255, 255),
        "errorContainer": mix(error, background, 0.7), "onErrorContainer": error,
        "background": background, "onBackground": on_surface,
        "surface": surface, "onSurface": on_surface,
        "surfaceVariant": surface_variant, "onSurfaceVariant": on_surface_variant,
        "surfaceContainer": surface_container, "onSurfaceContainer": on_surface,
        "outline": outline, "outlineVariant": outline_variant,
        "inverseSurface": on_surface, "inverseOnSurface": background,
        "inversePrimary": primary,
        "scrim": (0, 0, 0), "shadow": (0, 0, 0),
    }


def derive_radius(radii: list) -> dict:
    if not radii:
        return {"sm": 8, "md": 12, "lg": 16, "xl": 28, "pill": 999}
    # 真实设计中会出现的 cornerRadius：10/16/24/44，偶尔 100（Home Indicator 手势条）
    # 方案：取「非 100 的整数值」去重排序，用众数做 md，最小做 sm，最大做 lg。
    # pill 只在设计稿真有 ≥900 的胶囊时给 999，否则用最大真实值，避免把矩形硬画成椭圆。
    real = [int(r) for r in radii if r > 0 and r < 900 and r != 100]
    if not real:
        real = [int(r) for r in radii if r > 0 and r != 100]
    has_pill = any(r >= 900 for r in radii)
    uniq = sorted(set(real))
    md = int(statistics.mode(real)) if real else 12
    md = max(4, min(24, md))
    sm = min(uniq) if uniq else md
    sm = max(2, min(12, sm))
    lg = max(uniq) if uniq else md
    lg = max(md, min(48, lg))
    xl = 0
    tail = [r for r in uniq if r > lg]
    if tail:
        xl = min(tail[0], 64)
    else:
        xl = lg + 4
    pill = 999 if has_pill else max(uniq, default=lg) + 8
    return {"sm": sm, "md": md, "lg": lg, "xl": xl, "pill": pill}


# ---------- 圆角档位自测（--selfcheck-radius 参数） ----------
def selfcheck_radius() -> int:
    cases = [
        # (输入 radii, 期望 md) —— 用真实 CarStore 案例
        ([10, 10, 10, 16, 24, 44, 100], 10),   # 无 pill：md=10，lg=44，pill≠999
        ([8, 8, 16, 32], 8),
        ([12, 12, 12, 999], 12),               # 有真 pill：pill=999
        ([], 12),                              # 无数据回退
    ]
    ok = True
    for radii, want_md in cases:
        r = derive_radius(radii)
        status = "OK" if r["md"] == want_md else "FAIL"
        if status == "FAIL":
            ok = False
        print(f"[{status}] radii={radii} -> sm={r['sm']} md={r['md']} lg={r['lg']} xl={r['xl']} pill={r['pill']} (want md={want_md})")
    print("SELFCHECK:", "ALL OK" if ok else "HAS FAILURES")
    return 0 if ok else 1


# 字号梯度：>=8 个不同值才从设计推导，否则用 M3 默认（已贴合座舱可读性）
M3_TYPE = {
    "display": 46, "h1": 44, "h2": 36, "h3": 30, "h4": 24, "h5": 19,
    "body": 20, "caption": 15,
}


def derive_type(sizes: list) -> dict:
    uniq = sorted({int(s) for s in sizes if isinstance(s, (int, float))}, reverse=True)
    # 阈值放宽到 >=6：真实设计只要有 6 个不同字号就被采纳（之前 >=8 太苛刻，
    # CarStore 这种 7 个档位的设计稿会被永久锁死成 M3 默认梯度，"保护真实设计"失败）。
    if len(uniq) >= 6:
        def pick(i, fallback):
            return uniq[i] if i < len(uniq) else fallback
        return {
            "display": pick(0, 46), "h1": pick(1, 44), "h2": pick(2, 36),
            "h3": pick(3, 30), "h4": pick(4, 24), "h5": pick(5, 19),
            "body": pick(6, 20), "caption": pick(7, 15),
        }
    return dict(M3_TYPE)


# ---------- 生成 USS ----------
def gen_tokens(scope: str, pal: dict, r: dict, t: dict) -> str:
    L = []
    L.append("/* =============================================================================")
    L.append(" * FigmaTokens.uss — 由 Figma 设计稿抽出的 M3 语义令牌（Scheme A 转换链产物）")
    L.append(" * 作用域类：." + scope)
    L.append(" * 组件皮肤见 FigmaComponents.uss；两者都用 var(--a2ui-*) 解耦。")
    L.append(" * 颜色随主题走（本文件一份），字号/间距/圆角/阴影/动效全主题通用只定义一次。")
    L.append(" * ========================================================================== */")
    L.append("")
    L.append(f".{scope} {{")
    # 字号梯度
    L.append("  /* 字号梯度（车机远距离可读性） */")
    for k in ("display", "h1", "h2", "h3", "h4", "h5", "body", "caption"):
        L.append(f"  --a2ui-type-{k}: {t[k]}px;")
    # 间距 8dp 网格
    L.append("  /* 间距：8dp 网格节奏 */")
    for k, v in (("1", 8), ("2", 16), ("3", 24), ("4", 32), ("5", 48)):
        L.append(f"  --a2ui-space-{k}: {v}px;")
    # 圆角
    L.append("  /* 圆角档位 */")
    for k in ("sm", "md", "lg", "xl", "pill"):
        L.append(f"  --a2ui-radius-{k}: {r[k]}px;")
    # 动效
    L.append("  /* 动效：M3 Motion 时长与缓动曲线 */")
    L.append("  --a2ui-motion-duration-fast: 150ms;")
    L.append("  --a2ui-motion-duration-std: 250ms;")
    L.append("  --a2ui-motion-duration-slow: 400ms;")
    L.append("  --a2ui-motion-ease-standard: cubic-bezier(0.2, 0, 0, 1);")
    L.append("  --a2ui-motion-ease-emphasized: cubic-bezier(0.3, 0, 0, 1);")
    # 颜色
    L.append("")
    L.append("  /* ---- 颜色角色（来自 Figma 设计稿抽取） ---- */")
    for k in ("primary", "onPrimary", "primaryContainer", "onPrimaryContainer",
              "secondary", "onSecondary", "secondaryContainer", "onSecondaryContainer",
              "tertiary", "onTertiary", "error", "onError", "errorContainer", "onErrorContainer",
              "background", "onBackground", "surface", "onSurface", "surfaceVariant",
              "onSurfaceVariant", "surfaceContainer", "onSurfaceContainer", "outline",
              "outlineVariant", "inverseSurface", "inverseOnSurface", "inversePrimary",
              "scrim", "shadow"):
        v = pal[k]
        L.append(f"  --a2ui-color-{k}: {rgb(*v)};")
    L.append("}")
    L.append("")
    return "\n".join(L)


# ---------- 节点分类 → A2UI 组件类型 ----------
def classify_node(nd: dict, pal: dict) -> str | None:
    """根据 Figma 节点属性推断对应 A2UI 组件类型。返回 None 表示跳过。"""
    name = (nd.get("name") or "").lower()
    typ = nd.get("type") or ""
    fill = nd.get("fillColor")
    h = nd.get("height") or 0
    w = nd.get("width") or 0
    cr = nd.get("cornerRadius") or 0
    fs = nd.get("fontSize") or 0
    chars = nd.get("characters") or ""

    # TEXT 节点
    if typ == "TEXT":
        if any(kw in name or kw in chars.lower() for kw in ("forgot", "sign up", "continue with", "don't", "dont")):
            return "link"
        if fs >= 30:
            return "text_h1"
        if fs >= 20:
            return "text_h3"
        if fs >= 16:
            return "text_body"
        return "text_caption"

    # 带主色 fill 的 RECTANGLE → 按钮或 Logo
    if typ == "RECTANGLE" and fill:
        primary = pal.get("primary")
        if primary and _color_close(fill, primary):
            if w and h and abs(w - h) < 10 and cr >= 30:
                return "logo"  # 圆形 logo
            return "button_primary"

    # 白底/浅色 RECTANGLE → 输入框
    if typ == "RECTANGLE" and fill:
        if _color_close(fill, (255, 255, 255)) or _color_close(fill, (250, 251, 252)):
            if h >= 50 and w and w > 200:
                return "input_field"

    # FRAME 有 layoutMode → 容器
    if typ == "FRAME":
        layout = nd.get("layoutMode")
        if layout == "HORIZONTAL":
            return "row"
        if layout == "VERTICAL":
            return "column"
        return "frame"

    # GROUP → 容器
    if typ == "GROUP":
        return "container"

    # INSTANCE → 按钮或图标
    if typ == "INSTANCE":
        if any(kw in name for kw in ("button", "btn")):
            return "button_primary"
        return "icon"

    return None


def _color_close(a: tuple, b: tuple, tol: int = 25) -> bool:
    return all(abs(a[i] - b[i]) <= tol for i in range(3))


def _median(vals: list, default=0) -> int:
    nums = [int(v) for v in vals if isinstance(v, (int, float)) and v > 0]
    if not nums:
        return default
    return int(statistics.median(nums))


def _mode(vals: list, default=0) -> int:
    nums = [int(v) for v in vals if isinstance(v, (int, float)) and v > 0]
    if not nums:
        return default
    return int(statistics.mode(nums))


# ---------- 生成组件 USS（从真实节点数据推导） ----------
def gen_components(scope: str, ext: dict, pal: dict, r: dict, t: dict, spec: dict = None) -> str:
    nodes = ext.get("nodes") or []
    # 分类
    classified = {}  # category -> list of node dicts
    for nd in nodes:
        cat = classify_node(nd, pal)
        if cat:
            classified.setdefault(cat, []).append(nd)

    # 提取各组代表值
    btn_h = _median([nd["height"] for nd in classified.get("button_primary", [])], 48)
    btn_w = _median([nd["width"] for nd in classified.get("button_primary", [])], 0)
    btn_cr = _mode([nd["cornerRadius"] for nd in classified.get("button_primary", [])], r["md"])
    input_h = _median([nd["height"] for nd in classified.get("input_field", [])], 56)
    input_cr = _mode([nd["cornerRadius"] for nd in classified.get("input_field", [])], r["sm"])
    input_fill = _hex_from_nodes(classified.get("input_field", []), (255, 255, 255))
    btn_fill = _hex_from_nodes(classified.get("button_primary", []), pal["primary"])

    # ---- 名字规范驱动覆盖：'X / Type:variant' 命名时走确定性几何 ----
    spec = spec or {}
    geo = spec_geometry(spec) if (spec.get("frames") or spec.get("texts")) else {}
    btn_h = geo.get("btn_h", btn_h)
    # Tuanjie UITK 的 border-radius 横纵独立钳制：999px 会被拉成椭圆而非胶囊。
    # 圆角一律钳到「高度一半」= 真正的 pill。
    btn_cr = min(geo.get("btn_cr", btn_cr), max(4, btn_h // 2))
    input_h = geo.get("tf_h", input_h)
    input_cr = min(geo.get("tf_cr", input_cr), max(4, input_h // 2))
    if "btn_fill" in geo:
        btn_fill = rgb(*geo["btn_fill"])
    if "tf_fill" in geo:
        input_fill = rgb(*geo["tf_fill"])
    card_cr = geo.get("card_cr", r["md"])
    btn_pad_line = (f"  padding: {geo['btn_pad_tb']}px {geo['btn_pad_lr']}px;"
                    if "btn_pad_tb" in geo else None)
    tf_pad_line = (f"  padding: {geo['tf_pad_tb']}px {geo['tf_pad_lr']}px;"
                   if "tf_pad_tb" in geo else None)
    chip_cr = geo.get("chip_cr", r["md"])
    chip_h_est = 2 * geo.get("chip_pad_tb", 8) + int(t["caption"] * 1.45)
    chip_cr = min(chip_cr, max(4, chip_h_est // 2))
    chip_pad_attr = (f" padding: {geo['chip_pad_tb']}px {geo['chip_pad_lr']}px;"
                     if "chip_pad_tb" in geo else "")

    # 容器间距
    containers = classified.get("column", []) + classified.get("container", [])
    item_spacing = _median([nd["itemSpacing"] for nd in containers], 16)
    pad_left = _median([nd["paddingLeft"] for nd in containers], 16)
    pad_right = _median([nd["paddingRight"] for nd in containers], 16)
    pad_top = _median([nd["paddingTop"] for nd in containers], 24)
    pad_bottom = _median([nd["paddingBottom"] for nd in containers], 24)

    # 文本
    text_h1_fs = _median([nd["fontSize"] for nd in classified.get("text_h1", [])], t["h1"])
    text_h3_fs = _median([nd["fontSize"] for nd in classified.get("text_h3", [])], t["h3"])
    text_body_fs = _median([nd["fontSize"] for nd in classified.get("text_body", [])], t["body"])
    text_caption_fs = _median([nd["fontSize"] for nd in classified.get("text_caption", [])], t["caption"])
    text_h1_color = _hex_from_nodes(classified.get("text_h1", []), pal["onSurface"])
    text_body_color = _hex_from_nodes(classified.get("text_body", []), pal["onSurface"])
    text_caption_color = _hex_from_nodes(classified.get("text_caption", []), pal["onSurfaceVariant"])

    # 名字规范稿：字号/文字色直接取 Text:<variant> 真值。
    # 排序启发式在变体缺失/混排时会错位（sectionTitle 混进 body 桶导致取到灰色等）。
    if spec.get("texts"):
        text_h1_fs, text_h3_fs = t["h1"], t["h3"]
        text_body_fs, text_caption_fs = t["body"], t["caption"]
        text_h1_color = rgb(*pal["onSurface"])
        text_body_color = rgb(*pal["onSurface"])
        text_caption_color = rgb(*pal["onSurfaceVariant"])

    L = []
    L.append("/* =============================================================================")
    L.append(" * FigmaComponents.uss — 由 Figma 设计稿节点数据自动生成（精确转换）")
    L.append(f" * 作用域：.{scope}")
    L.append(" * 按钮高度={0}px 圆角={1}px 输入框高度={2}px 容器间距={3}px".format(btn_h, btn_cr, input_h, item_spacing))
    L.append(" * ========================================================================== */")
    L.append("")

    # 覆盖层容器：布局统一由 Host.uss 管理（left:10% + max-width:84% 收缩适配），
    # 主题侧不覆盖 width/margin——否则与其他主题卡片宽度不一致。
    L.append(f"/* 覆盖层容器布局由 Host.uss 统一管理，{scope} 不覆盖 width/margin。 */")
    L.append("")

    # 内层卡片：宽度由 Host.uss 统一管理（640px 标准卡），不覆盖 width
    L.append(f".{scope} .a2ui-overlay-card__inner {{")
    L.append(f"  align-items: stretch;")
    L.append(f"  background-color: {rgb(*pal['surface'])};")
    L.append(f"  border-radius: {r['xl']}px;")
    L.append(f"  border-top-width: 1px; border-bottom-width: 1px; border-left-width: 1px; border-right-width: 1px;")
    L.append(f"  border-top-color: {rgb(*pal['outlineVariant'])}; border-bottom-color: {rgb(*pal['outlineVariant'])};")
    L.append(f"  border-left-color: {rgb(*pal['outlineVariant'])}; border-right-color: {rgb(*pal['outlineVariant'])};")
    # 卡内边距：名字规范稿取 Card 实例真值，老稿退回容器中位数
    L.append(f"  padding: {geo.get('card_pad_tb', pad_top)}px {geo.get('card_pad_lr', pad_right)}px;")
    L.append("}")
    L.append("")

    # 根作用域
    L.append(f".{scope} {{")
    L.append(f"  color: {rgb(*pal['onSurface'])};")
    L.append(f"  font-size: {text_body_fs}px;")
    L.append("  -unity-font-style: normal;")
    L.append("}")
    L.append("")

    # 布局
    L.append(f".{scope} .a2ui-row {{ flex-direction: row; align-items: center; }}")
    L.append(f".{scope} .a2ui-row > * {{ margin-right: {item_spacing}px; }}")
    L.append(f".{scope} .a2ui-row > .a2ui-last-child {{ margin-right: 0; }}")
    L.append("")
    L.append(f".{scope} .a2ui-col,")
    L.append(f".{scope} .a2ui-column {{ flex-direction: column; align-items: stretch; width: 100%; }}")
    L.append(f".{scope} .a2ui-col > *,")
    L.append(f".{scope} .a2ui-column > * {{ margin-bottom: {item_spacing}px; flex-shrink: 0; }}")
    L.append(f".{scope} .a2ui-col > .a2ui-last-child,")
    L.append(f".{scope} .a2ui-column > .a2ui-last-child {{ margin-bottom: 0; }}")
    L.append("")

    # 按钮
    L.append(f".{scope} .a2ui-btn {{")
    L.append(f"  width: 100%; min-height: {btn_h}px;")
    if btn_pad_line:
        L.append(btn_pad_line)
    L.append(f"  border-radius: {btn_cr}px;")
    L.append(f"  font-size: {text_body_fs}px; -unity-text-align: middle-center;")
    L.append(f"  border-top-width: 0; border-bottom-width: 0; border-left-width: 0; border-right-width: 0;")
    L.append(f"  flex-shrink: 0;")
    L.append("}")
    L.append(f".{scope} .a2ui-btn--primary {{ background-color: {btn_fill}; color: {rgb(*pal['onPrimary'])}; -unity-font-style: bold; }}")
    # 次按钮：名字规范稿有 Button 无 variant 实例 → 用它的 fill（M3 secondaryContainer）与标签色
    btn2_bg = rgb(*pal["secondaryContainer"]) if spec.get("frames", {}).get(("Button", "")) else "transparent"
    btn2_fg = rgb(*pal.get("onSecondaryContainer", pal["onSurface"])) if btn2_bg != "transparent" else rgb(*pal["onSurface"])
    L.append(f".{scope} .a2ui-btn--secondary {{ background-color: {btn2_bg}; color: {btn2_fg}; }}")
    L.append("")

    # 输入框
    L.append(f".{scope} .a2ui-textfield,")
    L.append(f".{scope} .a2ui-textfield.unity-text-field {{")
    L.append(f"  flex-direction: column; align-items: stretch; width: 100%;")
    L.append(f"  min-height: {input_h}px;")
    L.append(f"  background-color: {input_fill};")
    L.append(f"  border-radius: {input_cr}px;")
    L.append(f"  border-top-width: 1px; border-bottom-width: 1px; border-left-width: 1px; border-right-width: 1px;")
    L.append(f"  border-top-color: {rgb(*pal['outlineVariant'])}; border-bottom-color: {rgb(*pal['outlineVariant'])};")
    L.append(f"  border-left-color: {rgb(*pal['outlineVariant'])}; border-right-color: {rgb(*pal['outlineVariant'])};")
    tf_pad_attr = tf_pad_line if tf_pad_line else "  padding: 12px 16px;"
    L.append(f"{tf_pad_attr} font-size: {text_body_fs}px; color: {rgb(*pal['onSurface'])}; flex-shrink: 0;")
    L.append("}")
    L.append(f".{scope} .a2ui-textfield > .unity-text-field__label,")
    L.append(f".{scope} .a2ui-textfield.unity-text-field > .unity-text-field__label {{")
    L.append(f"  width: 100%; align-self: flex-start; margin-bottom: 4px; margin-top: 0; padding: 0;")
    L.append(f"  color: {text_caption_color}; font-size: {text_body_fs}px; -unity-font-style: normal;")
    L.append("}")
    L.append(f".{scope} .a2ui-textfield > .unity-text-field__input,")
    L.append(f".{scope} .a2ui-textfield.unity-text-field > .unity-text-field__input {{")
    L.append(f"  width: 100%; align-self: stretch; background-color: transparent;")
    L.append(f"  border-top-width: 0; border-bottom-width: 0; border-left-width: 0; border-right-width: 0;")
    L.append(f"  color: {rgb(*pal['onSurface'])}; font-size: {text_body_fs}px; padding: 0;")
    L.append("}")
    L.append("")

    # 文本
    # Mapper 的类名是 "a2ui-text--" + usageHint 动态拼接，h2/h4/h5/display 缺失会
    # 回退到基础 .a2ui-text 字号——必须把全部变体都生成出来。
    L.append(f".{scope} .a2ui-text {{ color: {text_body_color}; font-size: {text_body_fs}px; white-space: normal; }}")
    for var, fs, color, bold in (
        ("display", t["display"], text_h1_color, True),
        ("h1", t["h1"], text_h1_color, True),
        ("h2", t["h2"], rgb(*pal["onSurface"]), True),
        ("h3", t["h3"], text_h3_fs and rgb(*pal["onSurface"]), True),
        ("h4", t["h4"], rgb(*pal["onSurface"]), True),
        ("h5", t["h5"], rgb(*pal["onSurface"]), True),
        ("body", t["body"], text_body_color, False),
        ("caption", t["caption"], text_caption_color, False),
    ):
        style = " -unity-font-style: bold;" if bold else ""
        L.append(f".{scope} .a2ui-text--{var} {{ color: {color}; font-size: {fs}px;{style} }}")
    L.append("")

    # 链接（Forgot Password / Sign Up 等）
    L.append(f".{scope} .a2ui-btn--secondary .a2ui-text {{ color: {rgb(*pal['primary'])}; }}")
    L.append("")

    # 分隔线
    L.append(f".{scope} .a2ui-divider {{ background-color: {rgb(*pal['outlineVariant'])}; }}")
    L.append(f".{scope} .a2ui-divider--horizontal {{ height: 1px; width: 100%; margin: {item_spacing}px 0; flex-shrink: 0; }}")
    L.append("")

    # 卡片
    L.append(f".{scope} .a2ui-card {{")
    L.append(f"  background-color: {rgb(*pal['surface'])}; border-radius: {card_cr}px;")
    L.append(f"  border-width: 1px; border-color: {rgb(*pal['outlineVariant'])}; border-style: solid;")
    L.append("}")
    L.append("")

    # Logo（圆形）——只在稿子里真有圆形主色块时生成（老橙稿特例），规范稿不泄漏
    logo_nodes = classified.get("logo", [])
    if logo_nodes:
        logo_size = _median([nd["width"] for nd in logo_nodes], 76)
        logo_fill = _hex_from_nodes(logo_nodes, pal["primary"])
        L.append(f".{scope} .a2ui-card.a2ui-type--card {{ width: {logo_size}px; height: {logo_size}px; border-radius: {logo_size}px; padding: 0; border-width: 0; align-self: center; background-color: {logo_fill}; }}")
        L.append("")

    # 图标
    L.append(f".{scope} .a2ui-icon {{ width: 36px; height: 36px; color: {rgb(*pal['onSurfaceVariant'])}; -unity-text-align: middle-center; font-size: 28px; }}")
    L.append("")

    # 图片
    L.append(f".{scope} .a2ui-image {{ width: 100%; height: 180px; border-radius: {r['md']}px; background-color: {rgb(*pal['surfaceVariant'])}; }}")
    L.append("")

    # 其余组件兜底
    L.append(f".{scope} .a2ui-slider {{ height: 40px; }}")
    L.append(f".{scope} .a2ui-slider .unity-base-slider__tracker {{ background-color: {rgb(*pal['outlineVariant'])}; border-radius: {r['md']}px; }}")
    L.append(f".{scope} .a2ui-slider .unity-base-slider__dragger {{ background-color: {btn_fill}; border-radius: {r['md']}px; }}")
    L.append(f".{scope} .a2ui-tabs__header {{ flex-direction: row; border-bottom-width: 1px; border-bottom-color: {rgb(*pal['outlineVariant'])}; padding-bottom: 8px; }}")
    L.append(f".{scope} .a2ui-tabs__tab {{ padding: 10px 24px; border-radius: {r['md']}px; background-color: {rgb(*pal['surfaceVariant'])}; color: {rgb(*pal['onSurfaceVariant'])}; font-size: {text_caption_fs}px; -unity-text-align: middle-center; border-width: 0; }}")
    L.append(f".{scope} .a2ui-tabs__tab.a2ui-tabs__tab--active {{ background-color: {btn_fill}; color: {rgb(*pal['onPrimary'])}; }}")
    L.append(f".{scope} .a2ui-list__item {{ background-color: {rgb(*pal['surfaceVariant'])}; border-radius: {r['sm']}px; padding: 16px; margin-bottom: 8px; border-left-width: 4px; border-left-color: {btn_fill}; border-left-style: solid; }}")
    L.append(f".{scope} .a2ui-chip {{ background-color: {rgb(*pal['surfaceVariant'])}; color: {rgb(*pal['onSurface'])}; border-radius: {chip_cr}px;{chip_pad_attr} font-size: {text_caption_fs}px; border-width: 0; }}")
    L.append(f".{scope} .a2ui-chip:checked {{ background-color: {btn_fill}; color: {rgb(*pal['onPrimary'])}; }}")
    cabin_cr = geo.get("cabin_cr", r["md"])
    cabin_pad_attr = (f" padding: {geo['cabin_pad_tb']}px {geo['cabin_pad_lr']}px;"
                      if "cabin_pad_tb" in geo else " padding: 24px;")
    L.append(f".{scope} .a2ui-cabin {{ background-color: {rgb(*pal['surfaceVariant'])}; border-radius: {cabin_cr}px;{cabin_pad_attr} }}")
    L.append(f".{scope} .a2ui-cabin__tag {{ background-color: {btn_fill}; color: {rgb(*pal['onPrimary'])}; border-radius: {r['md']}px; }}")

    return "\n".join(L)


def _hex_from_nodes(nodes: list, fallback: tuple) -> str:
    """从节点列表的第一个有 fillColor 的节点取颜色，否则用 fallback。"""
    for nd in nodes:
        fc = nd.get("fillColor")
        if fc and all(isinstance(v, (int, float)) for v in fc):
            return rgb(*fc)
    return rgb(*fallback)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", default=str(SAMPLE))
    ap.add_argument("--outdir", default=str(OUTDIR))
    ap.add_argument("--scope", default=None,
                    help="组件皮肤作用域类；缺省时按输出目录名推导为 a2ui-skin--figma-<目录名小写>")
    ap.add_argument("--selfcheck-radius", action="store_true",
                    help="只跑圆角派生自测，不转换（用于回归）")
    args = ap.parse_args()

    if args.selfcheck_radius:
        raise SystemExit(selfcheck_radius())

    # 作用域与运行时注册表(A2uiThemeRegistry)保持一致：目录名 D -> figma-<D小写> -> a2ui-skin--figma-<D小写>
    scope = args.scope or ("a2ui-skin--figma-" + pathlib.Path(args.outdir).name.lower())

    data = json.loads(pathlib.Path(args.input).read_text(encoding="utf-8"))
    ext = extract(data)
    spec = scan_specs(data)
    pal = map_palette(ext["colors"])
    r = derive_radius(ext["radii"])
    t = derive_type(ext["sizes"])
    pal, t = apply_spec_tokens(pal, t, spec)

    outdir = pathlib.Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)

    tokens = gen_tokens(scope, pal, r, t)
    components = gen_components(scope, ext, pal, r, t, spec=spec)
    (outdir / "FigmaTokens.uss").write_text(tokens, encoding="utf-8")
    (outdir / "FigmaComponents.uss").write_text(components, encoding="utf-8")

    # 摘要
    print("== Figma -> USS 转换完成 ==")
    print(f"输入: {args.input}")
    print(f"输出: {outdir}/FigmaTokens.uss , FigmaComponents.uss")
    print(f"作用域: .{scope}")
    mode = "名字规范驱动" if (spec.get("frames") or spec.get("texts")) else "启发式兜底（无规范命名）"
    print(f"提取模式: {mode}")
    print(f"抽取颜色 {len(ext['colors'])} 个，圆角样本 {len(ext['radii'])}，字号样本 {len(ext['sizes'])}，节点 {len(ext.get('nodes',[]))} 个")
    print("主要令牌:")
    for k in ("primary", "secondary", "tertiary", "surface", "surfaceVariant", "background", "onSurface", "error"):
        print(f"  --a2ui-color-{k}: {rgb(*pal[k])}")
    print(f"  圆角 md/lg/pill: {r['md']}/{r['lg']}/{r['pill']}")
    print(f"  字号 h1/body/caption: {t['h1']}/{t['body']}/{t['caption']}")


if __name__ == "__main__":
    main()
