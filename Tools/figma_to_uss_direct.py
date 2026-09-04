#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""figma_to_uss_direct.py — Figma JSON → USS 直通模式（不过 M3 角色映射）。

与 figma_to_uss.py 的区别：
  - 不做 M3 启发式映射（primary/surface/outline...）
  - 按节点名前缀 a2ui- 直接匹配组件类型
  - 颜色/圆角/字号/padding 从 Figma 节点真实值直接写 USS
  - 支持多输入文件合并（覆盖率模式）

用法：
  python figma_to_uss_direct.py --input file1.json file2.json --outdir Styles/FigmaExport
"""
from __future__ import annotations

import argparse
import json
import pathlib
import statistics
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUTDIR = ROOT / "Assets/A2UISchemeA/Styles/FigmaExport"


def hex_color(rgb_tuple):
    r, g, b = rgb_tuple
    return f"#{r:02X}{g:02X}{b:02X}"


def to_255(c):
    return (round(c.get("r", 0) * 255), round(c.get("g", 0) * 255), round(c.get("b", 0) * 255))


def get_solid_fill(node):
    """从节点的 fills 数组取第一个 visible SOLID 颜色。"""
    for f in node.get("fills") or []:
        if f.get("type") == "SOLID" and f.get("visible", True) is not False:
            op = f.get("opacity", 1)
            if op is None:
                op = 1
            if op >= 0.5:
                return to_255(f.get("color") or {})
    return None


def find_document(data):
    if "nodes" in data and data.get("nodes"):
        nodes = data["nodes"]
        first = next(iter(nodes.values())) if isinstance(nodes, dict) else nodes[0]
        if isinstance(first, dict) and "document" in first:
            return first["document"]
        return first if isinstance(first, dict) else {}
    if "document" in data:
        return data["document"]
    return data


# ---------- 节点收集（按名称前缀分类） ----------
def collect_by_name(doc, registry):
    """递归遍历，按节点名前缀收集到 registry。"""
    if not isinstance(doc, dict):
        return

    name = (doc.get("name") or "").lower().strip()
    typ = doc.get("type") or ""
    fills = doc.get("fills") or []
    style = doc.get("style") or {}
    bb = doc.get("absoluteBoundingBox") or {}

    solid_fill = get_solid_fill(doc)

    # 收集子 TEXT 节点的颜色（用于按钮文字色等）
    child_text_color = None
    for ch in (doc.get("children") or []):
        if isinstance(ch, dict) and ch.get("type") == "TEXT":
            ctc = get_solid_fill(ch)
            if ctc:
                child_text_color = ctc
                break

    node_data = {
        "name": doc.get("name", ""),
        "type": typ,
        "width": bb.get("width"),
        "height": bb.get("height"),
        "cornerRadius": doc.get("cornerRadius"),
        "paddingLeft": doc.get("paddingLeft"),
        "paddingRight": doc.get("paddingRight"),
        "paddingTop": doc.get("paddingTop"),
        "paddingBottom": doc.get("paddingBottom"),
        "itemSpacing": doc.get("itemSpacing"),
        "layoutMode": doc.get("layoutMode"),
        "fillColor": solid_fill,
        "childTextColor": child_text_color,
        "fontSize": style.get("fontSize"),
        "fontWeight": style.get("fontWeight", 400),
        "textAlign": style.get("textAlignHorizontal"),
        "characters": doc.get("characters"),
    }

    # 按名称前缀匹配（长前缀优先，避免 a2ui-text 吃掉 a2ui-textfield）
    matched = False
    for prefix in sorted(registry, key=len, reverse=True):
        if name.startswith(prefix):
            registry[prefix].append(node_data)
            matched = True
            break  # 只归入第一个匹配的分类

    # Fallback：没有 a2ui- 前缀时用启发式分类
    if not matched:
        classified = classify_heuristic(node_data)
        if classified and classified in registry:
            registry[classified].append(node_data)

    for ch in doc.get("children") or []:
        collect_by_name(ch, registry)


def classify_heuristic(nd):
    """无 a2ui- 前缀时的启发式分类：按节点类型/颜色/字号推断组件类型。"""
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
        # 按字号分档
        if fs >= 36:
            return "a2ui-text-h1"
        if fs >= 24:
            return "a2ui-text-h2"
        if fs >= 16:
            return "a2ui-text-body"
        return "a2ui-text-caption"

    # 带主色 fill 的按钮
    if fill and typ in ("FRAME", "INSTANCE", "COMPONENT", "RECTANGLE"):
        r, g, b = fill
        mx, mn = max(fill), min(fill)
        is_accent = (mx - mn >= 40) and not (mx > 200 and mn > 200)  # 不是白色/浅灰
        if is_accent and h >= 40 and h <= 80:
            return "a2ui-btn-primary"
        # 浅色背景可能是输入框
        if mx > 240 and h >= 40 and h <= 80 and w > 200:
            return "a2ui-textfield"

    # 分隔线
    if typ in ("RECTANGLE", "LINE") and h <= 2:
        return "a2ui-divider"

    # 圆角大的可能是 chip
    if cr >= 100 and typ in ("FRAME", "RECTANGLE") and h <= 40:
        return "a2ui-chip"

    # FRAME 有 layoutMode → 容器
    if typ == "FRAME":
        layout = nd.get("layoutMode")
        if layout == "HORIZONTAL":
            return "a2ui-row"
        if layout == "VERTICAL":
            # 大的是卡片，小的是列
            if w and h and w > 300 and h > 100:
                return "a2ui-card"
            return "a2ui-column"

    return None


# ---------- 从收集的节点取代表值 ----------
def pick(nodes, key, method="median", default=None):
    """从节点列表取某个属性的代表值。"""
    vals = [n[key] for n in nodes if isinstance(n.get(key), (int, float)) and n[key] is not None and n[key] > 0]
    if not vals:
        return default
    if method == "median":
        return int(statistics.median(vals))
    if method == "mode":
        return int(statistics.mode(vals))
    return int(vals[0])


def pick_color(nodes, default=(128, 128, 128)):
    """从节点列表取第一个有 fillColor 的颜色。"""
    for n in nodes:
        fc = n.get("fillColor")
        if fc and all(isinstance(v, (int, float)) for v in fc):
            return fc
    return default


def pick_child_text_color(nodes, default=(0, 0, 0)):
    """从节点列表中取子 TEXT 节点的颜色（按钮文字色等）。"""
    for n in nodes:
        ctc = n.get("childTextColor")
        if ctc and all(isinstance(v, (int, float)) for v in ctc):
            return ctc
    return default


# ---------- 生成 USS ----------
def gen_uss(reg, scope):
    L = []
    L.append("/* =============================================================================")
    L.append(" * FigmaExport.uss — 由 Figma 设计稿直接生成（直通模式，不过 M3 角色映射）")
    L.append(f" * 作用域：.{scope}")
    L.append(f" * 颜色/圆角/字号/padding 均来自 Figma 节点真实值，1:1 还原设计稿。")
    L.append(" * ========================================================================== */")
    L.append("")

    # ---- 从所有节点汇总全局值 ----
    all_nodes = []
    for v in reg.values():
        all_nodes.extend(v)

    # 全局颜色
    bg_color = pick_color(reg.get("a2ui-card", []), pick_color(reg.get("a2ui-frame", []), (27, 31, 39)))
    text_color = pick_color(reg.get("a2ui-text-body", []), pick_color(reg.get("a2ui-text", []), (232, 234, 237)))
    caption_color = pick_color(reg.get("a2ui-text-caption", []), (154, 162, 177))
    h1_color = pick_color(reg.get("a2ui-text-h1", []), text_color)
    h2_color = pick_color(reg.get("a2ui-text-h2", []), text_color)

    primary_color = pick_color(reg.get("a2ui-btn-primary", []), (255, 92, 0))
    primary_text_color = (255, 255, 255)  # 白字在主色按钮上
    secondary_color = pick_color(reg.get("a2ui-btn-secondary", []), (237, 238, 239))
    secondary_text_color = pick_child_text_color(reg.get("a2ui-btn-secondary", []), (4, 4, 21))

    surface_color = bg_color
    surface_variant = pick_color(reg.get("a2ui-list-item", []), pick_color(reg.get("a2ui-chip", []), (237, 238, 239)))
    outline_color = pick_color(reg.get("a2ui-divider", []), pick_color(reg.get("a2ui-textfield", []), (200, 200, 200)))

    # 字号
    h1_fs = pick(reg.get("a2ui-text-h1", []), "fontSize", default=38)
    h2_fs = pick(reg.get("a2ui-text-h2", []), "fontSize", default=28)
    body_fs = pick(reg.get("a2ui-text-body", []), "fontSize", default=16)
    caption_fs = pick(reg.get("a2ui-text-caption", []), "fontSize", default=12)

    # 圆角
    all_radii = [n["cornerRadius"] for n in all_nodes if isinstance(n.get("cornerRadius"), (int, float)) and 0 < n["cornerRadius"] < 900]
    radius_md = int(statistics.mode(all_radii)) if all_radii else 10
    card_cr = pick(reg.get("a2ui-card", []), "cornerRadius", "mode", radius_md)
    btn_cr = pick(reg.get("a2ui-btn-primary", []), "cornerRadius", "mode", radius_md)
    input_cr = pick(reg.get("a2ui-textfield", []), "cornerRadius", "mode", radius_md)
    chip_cr = pick(reg.get("a2ui-chip", []), "cornerRadius", "mode", 999)

    # padding / spacing
    card_pad = pick(reg.get("a2ui-card", []), "paddingLeft", default=16)
    container_spacing = pick(reg.get("a2ui-card", []), "itemSpacing", default=16)

    # 按钮高度
    btn_h = pick(reg.get("a2ui-btn-primary", []), "height", default=48)
    input_h = pick(reg.get("a2ui-textfield", []), "height", default=52)

    # ---- 根作用域 ----
    L.append(f".{scope} {{")
    L.append(f"  color: {hex_color(text_color)};")
    L.append(f"  font-size: {body_fs}px;")
    L.append("  -unity-font-style: normal;")
    L.append("}")
    L.append("")

    # ---- 卡片 ----
    L.append(f".{scope} .a2ui-card {{")
    L.append(f"  background-color: {hex_color(surface_color)};")
    L.append(f"  border-radius: {card_cr}px;")
    L.append(f"  border-top-width: 1px; border-bottom-width: 3px; border-left-width: 1px; border-right-width: 1px;")
    L.append(f"  border-top-color: {hex_color(outline_color)}; border-bottom-color: {hex_color(outline_color)};")
    L.append(f"  border-left-color: {hex_color(outline_color)}; border-right-color: {hex_color(outline_color)};")
    L.append(f"  padding: {card_pad}px;")
    L.append("}")
    L.append("")

    # ---- 文本 ----
    L.append(f".{scope} .a2ui-text {{ color: {hex_color(text_color)}; font-size: {body_fs}px; white-space: normal; }}")
    L.append(f".{scope} .a2ui-text--h1 {{ color: {hex_color(h1_color)}; font-size: {h1_fs}px; -unity-font-style: bold; }}")
    L.append(f".{scope} .a2ui-text--h2 {{ color: {hex_color(h2_color)}; font-size: {h2_fs}px; -unity-font-style: bold; }}")
    L.append(f".{scope} .a2ui-text--h3 {{ color: {hex_color(h1_color)}; font-size: {h2_fs}px; -unity-font-style: bold; }}")
    L.append(f".{scope} .a2ui-text--caption {{ color: {hex_color(caption_color)}; font-size: {caption_fs}px; }}")
    L.append("")

    # ---- 按钮 ----
    L.append(f".{scope} .a2ui-btn {{")
    L.append(f"  min-height: {btn_h}px; border-radius: {btn_cr}px;")
    L.append(f"  font-size: {body_fs}px; -unity-text-align: middle-center;")
    L.append(f"  border-top-width: 0; border-bottom-width: 0; border-left-width: 0; border-right-width: 0;")
    L.append("}")
    L.append(f".{scope} .a2ui-btn--primary {{ background-color: {hex_color(primary_color)}; color: {hex_color(primary_text_color)}; -unity-font-style: bold; }}")
    L.append(f".{scope} .a2ui-btn--secondary {{ background-color: {hex_color(secondary_color)}; color: {hex_color(secondary_text_color)}; }}")
    L.append("")

    # ---- 输入框 ----
    L.append(f".{scope} .a2ui-textfield {{")
    L.append(f"  min-height: {input_h}px; background-color: {hex_color(surface_variant)};")
    L.append(f"  border-radius: {input_cr}px;")
    L.append(f"  border-top-width: 1px; border-bottom-width: 1px; border-left-width: 1px; border-right-width: 1px;")
    L.append(f"  border-top-color: {hex_color(outline_color)}; border-bottom-color: {hex_color(outline_color)};")
    L.append(f"  border-left-color: {hex_color(outline_color)}; border-right-color: {hex_color(outline_color)};")
    L.append(f"  padding: 12px 16px; font-size: {body_fs}px; color: {hex_color(text_color)};")
    L.append("}")
    L.append("")

    # ---- 分隔线 ----
    div_color = pick_color(reg.get("a2ui-divider", []), outline_color)
    L.append(f".{scope} .a2ui-divider {{ background-color: {hex_color(div_color)}; }}")
    L.append(f".{scope} .a2ui-divider--horizontal {{ height: 1px; width: 100%; margin: {container_spacing}px 0; }}")
    L.append("")

    # ---- 列表项 ----
    li_cr = pick(reg.get("a2ui-list-item", []), "cornerRadius", "mode", radius_md)
    li_fill = pick_color(reg.get("a2ui-list-item", []), surface_variant)
    L.append(f".{scope} .a2ui-list__item {{ background-color: {hex_color(li_fill)}; border-radius: {li_cr}px; padding: 16px; margin-bottom: 8px; }}")
    L.append("")

    # ---- Chip ----
    chip_fill = pick_color(reg.get("a2ui-chip", []), surface_variant)
    L.append(f".{scope} .a2ui-chip {{ background-color: {hex_color(chip_fill)}; color: {hex_color(text_color)}; border-radius: {chip_cr}px; font-size: {caption_fs}px; border-width: 0; }}")
    L.append(f".{scope} .a2ui-chip:checked {{ background-color: {hex_color(primary_color)}; color: {hex_color(primary_text_color)}; }}")
    L.append("")

    # ---- Toggle/Checkbox ----
    cb_fill = pick_color(reg.get("a2ui-checkbox", []), surface_variant)
    L.append(f".{scope} .a2ui-choice__option .unity-toggle__input {{")
    L.append(f"  width: 24px; height: 24px; border-radius: {input_cr}px;")
    L.append(f"  background-color: {hex_color(cb_fill)};")
    L.append(f"  border-top-width: 2px; border-bottom-width: 2px; border-left-width: 2px; border-right-width: 2px;")
    L.append(f"  border-top-color: {hex_color(outline_color)}; border-bottom-color: {hex_color(outline_color)};")
    L.append(f"  border-left-color: {hex_color(outline_color)}; border-right-color: {hex_color(outline_color)};")
    L.append(f"  margin-right: 8px;")
    L.append("}")
    L.append(f".{scope} .a2ui-choice__option .unity-toggle__input:checked {{ background-color: {hex_color(primary_color)}; border-color: {hex_color(primary_color)}; }}")
    L.append(f".{scope} .a2ui-choice__option .unity-toggle__label,")
    L.append(f".{scope} .a2ui-choice__option .unity-label {{ color: {hex_color(text_color)}; font-size: {body_fs}px; }}")
    L.append("")

    # ---- Slider ----
    slider_h = pick(reg.get("a2ui-slider", []), "height", default=40)
    L.append(f".{scope} .a2ui-slider {{ height: {slider_h}px; }}")
    L.append(f".{scope} .a2ui-slider .unity-base-slider__tracker {{ background-color: {hex_color(outline_color)}; border-radius: {radius_md}px; }}")
    L.append(f".{scope} .a2ui-slider .unity-base-slider__dragger {{ background-color: {hex_color(primary_color)}; border-radius: {radius_md}px; }}")
    L.append("")

    # ---- Tabs ----
    tab_fill = pick_color(reg.get("a2ui-tab-inactive", []), surface_variant)
    tab_active_fill = pick_color(reg.get("a2ui-tab-active", []), primary_color)
    L.append(f".{scope} .a2ui-tabs__header {{ flex-direction: row; border-bottom-width: 1px; border-bottom-color: {hex_color(outline_color)}; padding-bottom: 8px; }}")
    L.append(f".{scope} .a2ui-tabs__tab {{ padding: 10px 24px; border-radius: {radius_md}px; background-color: {hex_color(tab_fill)}; color: {hex_color(caption_color)}; font-size: {caption_fs}px; border-width: 0; }}")
    L.append(f".{scope} .a2ui-tabs__tab.a2ui-tabs__tab--active {{ background-color: {hex_color(tab_active_fill)}; color: {hex_color(primary_text_color)}; }}")
    L.append("")

    # ---- Icon ----
    icon_size = pick(reg.get("a2ui-icon", []), "width", default=36)
    L.append(f".{scope} .a2ui-icon {{ width: {icon_size}px; height: {icon_size}px; color: {hex_color(caption_color)}; -unity-text-align: middle-center; font-size: {int(icon_size * 0.7)}px; }}")
    L.append("")

    # ---- Image ----
    img_cr = pick(reg.get("a2ui-image", []), "cornerRadius", "mode", radius_md)
    L.append(f".{scope} .a2ui-image {{ width: 100%; height: 180px; border-radius: {img_cr}px; background-color: {hex_color(surface_variant)}; }}")
    L.append("")

    # ---- 布局 ----
    L.append(f".{scope} .a2ui-row > * {{ margin-right: {container_spacing}px; }}")
    L.append(f".{scope} .a2ui-row > .a2ui-last-child {{ margin-right: 0; }}")
    L.append(f".{scope} .a2ui-column > * {{ margin-bottom: {container_spacing}px; }}")
    L.append(f".{scope} .a2ui-column > .a2ui-last-child {{ margin-bottom: 0; }}")
    L.append("")

    return "\n".join(L)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", nargs="+", required=True, help="一个或多个 Figma JSON 文件")
    ap.add_argument("--outdir", default=str(OUTDIR))
    ap.add_argument("--scope", default="a2ui-skin--figma-export")
    args = ap.parse_args()

    # 注册表：节点名前缀 → 收集列表
    registry = {
        "a2ui-card": [],
        "a2ui-text-h1": [],
        "a2ui-text-h2": [],
        "a2ui-text-h3": [],
        "a2ui-text-body": [],
        "a2ui-text-caption": [],
        "a2ui-text": [],
        "a2ui-btn-primary": [],
        "a2ui-btn-secondary": [],
        "a2ui-btn": [],
        "a2ui-divider": [],
        "a2ui-list-item": [],
        "a2ui-list": [],
        "a2ui-textfield": [],
        "a2ui-checkbox": [],
        "a2ui-slider": [],
        "a2ui-tabs-header": [],
        "a2ui-tab-active": [],
        "a2ui-tab-inactive": [],
        "a2ui-chip": [],
        "a2ui-icon": [],
        "a2ui-image": [],
        "a2ui-frame": [],
        "a2ui-row": [],
        "a2ui-column": [],
    }

    for inp in args.input:
        p = pathlib.Path(inp)
        if not p.exists():
            print(f"WARNING: 文件不存在: {inp}")
            continue
        data = json.loads(p.read_text(encoding="utf-8"))
        doc = find_document(data)
        collect_by_name(doc, registry)
        print(f"  已加载: {p.name}")

    # 统计
    total = sum(len(v) for v in registry.values())
    print(f"\n== 收集结果 ==")
    print(f"总节点数: {total}")
    for k, v in registry.items():
        if v:
            print(f"  {k}: {len(v)} 个")

    # 生成 USS
    uss = gen_uss(registry, args.scope)
    outdir = pathlib.Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    outpath = outdir / "FigmaExport.uss"
    outpath.write_text(uss, encoding="utf-8")
    print(f"\n== USS 已生成: {outpath} ==")

    # 摘要
    covered = [k for k, v in registry.items() if v]
    missing = [k for k, v in registry.items() if not v and k.startswith("a2ui-")]
    print(f"已覆盖组件: {', '.join(covered)}")
    if missing:
        print(f"未覆盖组件: {', '.join(missing)}")


if __name__ == "__main__":
    main()
