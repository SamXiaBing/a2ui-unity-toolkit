#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""跨渲染器设计还原度量：Figma 组件渲染 PNG vs Unity 截图。

不做逐像素比对（字体渲染差异噪声 dominated）。度量三件事：
  1. 几何比 ratio = Figma 内容高(设计px) / Unity 内容高(面板px)
     —— Unity 卡宽恒 640 面板px，由截图卡宽反推屏幕缩放；Figma PNG 为 2x 设计px
     —— ratio≈1.0 即字号/组件高度还原正确（L2/L3 直接量化）
  2. 主色差：内容区主导色（非底色）RGB 距离（L1）
  3. 底色差：surface 底色 RGB 距离（L1）

bbox 采用「行/列密度裁剪」：圆角 AA 弧、孤立杂点是稀疏伪内容，
按行/列内容像素数阈值剔除，否则 Unity 卡角 AA 会把 bbox 撑成整卡。

用法：
  python Tools/figma_visual_diff.py [--names card,btnP,...]
输出：
  TestResults/figma_calib/diff/metrics.json + pairs/<name>.png（设计空间 1:1 并排图）
"""
from __future__ import annotations

import argparse
import glob
import json
import pathlib
import re
from collections import Counter

import numpy as np
from PIL import Image

SURFACE = (0xFF, 0xFB, 0xFE)   # M3 surface #FFFBFE
CONTENT_TOL = 26               # 内容判定容差：#E9EAEA 级别的容器细边框（色差 22）归为底色，
                               # Chip #E7E0EC（色差 27）仍算内容
UNITY_CARD_PANEL_W = 640.0     # 布局合同：内卡恒 640 面板px
FIGMA_SCALE = 2.0              # Figma images API scale=2

# 已知跨渲染器系统性噪声（标定结论，不判回归）：
#   字体形状差异（Roboto vs MiSans）、Unity 文本元素含字体行高余量
THRESH = {
    "text":    (0.70, 1.35),   # 字号类：字体度量余量 ±35%
    "button":  (0.85, 1.15),   # 按钮/输入框：几何合同 ±15%
    "frame":   (0.85, 1.15),
    "default": (0.70, 1.35),
}


def close(arr, color, tol):
    d = np.abs(arr[..., :3].astype(int) - np.array(color))
    return d.max(axis=-1) <= tol


def content_mask(arr, tol):
    return ~close(arr, SURFACE, tol)


def bbox_trim(mask, min_px=5, rounds=3):
    """包围盒 + 密度裁剪。阈值 = 轴长的 3%（下限 5px）：
    圆角 AA 弧行/列只有 ≤10px（全宽 423 的 ~2.4%），必被剔除；
    文字行（≥13px）与分割线（全宽）保留。相对峰值阈值会切进大字号稀疏顶部，故用轴长比例。"""
    ys, xs = np.where(mask)
    if len(xs) == 0:
        return None
    x0, x1 = int(xs.min()), int(xs.max())
    y0, y1 = int(ys.min()), int(ys.max())
    for _ in range(rounds):
        sub = mask[y0:y1 + 1, x0:x1 + 1]
        if sub.size == 0:
            return None
        rows = sub.sum(axis=1)
        cols = sub.sum(axis=0)
        rthr = max(min_px, int(0.03 * sub.shape[1]))
        cthr = max(min_px, int(0.03 * sub.shape[0]))
        ry = np.where(rows >= rthr)[0]
        cx = np.where(cols >= cthr)[0]
        # 某轴裁不动（如 1px 细线的列密度只有 1）就保留该轴原范围
        if len(ry) == 0 and len(cx) == 0:
            break
        if len(ry) > 0:
            ny0, ny1 = y0 + int(ry.min()), y0 + int(ry.max())
        else:
            ny0, ny1 = y0, y1
        if len(cx) > 0:
            nx0, nx1 = x0 + int(cx.min()), x0 + int(cx.max())
        else:
            nx0, nx1 = x0, x1
        if (ny0, ny1, nx0, nx1) == (y0, y1, x0, x1):
            break
        y0, y1, x0, x1 = ny0, ny1, nx0, nx1
    return x0, y0, x1, y1


# Figma 节点渲染 PNG 本身就是节点精确边界（无噪声）。几何比对跳过这些：
# Figma 只给轨道/线本体，Unity 是完整控件（含拖柄/交互区），高度语义不同
SKIP_GEO = {"div", "slider"}
# ink 比对语义豁免：slider 拖柄是交互件，Figma 模板只有轨道，ink 必然不同
SKIP_INK = {"slider"}


def dominant_ink(arr, mask):
    """内容像素的主导色（过滤抗锯齿边缘：只取距底色 > 60 的像素）。"""
    px = arr[mask]
    if len(px) == 0:
        return None
    strong = px[np.abs(px.astype(int) - np.array(SURFACE)).max(axis=-1) > 60]
    use = strong if len(strong) > 20 else px
    cnt = Counter(map(tuple, (use // 8 * 8).tolist()))
    return np.array(cnt.most_common(1)[0][0], dtype=int)


def verdict_for(name: str, ratio: float, ink_d) -> str:
    if name in SKIP_INK:
        return "OK"  # ink 语义不同（见 SKIP_INK），几何也不可比
    lo, hi = THRESH.get(name, THRESH["default"])
    ok = lo <= ratio <= hi and (ink_d is None or ink_d <= 48)
    return "OK" if ok else "CHECK"


def analyze(fig_path: pathlib.Path, uni_path: pathlib.Path, out_dir: pathlib.Path):
    name = re.search(r"figma_comp_(\w+)\.png$", fig_path.name).group(1)

    # ---- Figma 侧：节点渲染 PNG 即精确节点边界，整图就是内容 ----
    fig = Image.open(fig_path).convert("RGBA")
    bg = Image.new("RGBA", fig.size, SURFACE + (255,))
    bg.alpha_composite(fig)
    fa = np.array(bg.convert("RGB"))
    fmask = content_mask(fa, tol=CONTENT_TOL)
    if not fmask.any():
        return {"name": name, "error": "figma no content"}
    fcrop_arr = fa
    f_h_design = fa.shape[0] / FIGMA_SCALE
    f_ink = dominant_ink(fa, fmask)

    # ---- Unity 侧 ----
    ua = np.array(Image.open(uni_path).convert("RGB"))
    cmask = close(ua, SURFACE, 26)
    ys, xs = np.where(cmask)
    if len(xs) < 500:
        return {"name": name, "error": "unity card not found"}
    card_box = (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))
    card = ua[card_box[1]:card_box[3] + 1, card_box[0]:card_box[2] + 1]
    panel_scale = card.shape[1] / UNITY_CARD_PANEL_W  # 屏幕px per 面板px

    # 卡片实际底色取众数（Host 会对 figma-* 内联覆盖 USS 的 surface 值），
    # 边框 AA 弧距卡底色仅 ~20，而正文/分割线/Chip 都在 47+ —— 容差 12 干净分离
    cnt = Counter(map(tuple, (card.reshape(-1, 3) // 2 * 2).tolist()))
    card_bg = np.array(cnt.most_common(1)[0][0], dtype=int)

    inset = max(2, int(round(2 * panel_scale)))
    inner = card[inset:card.shape[0] - inset, inset:card.shape[1] - inset]

    inner_mask = ~close(inner, card_bg, tol=12)
    ub = bbox_trim(inner_mask)
    if ub is None:
        return {"name": name, "error": "unity no content"}
    ucrop = inner[ub[1]:ub[3] + 1, ub[0]:ub[2] + 1]
    u_h_panel = ucrop.shape[0] / panel_scale
    u_ink = dominant_ink(ucrop, ~close(ucrop, card_bg, tol=12))

    ratio = f_h_design / u_h_panel
    ink_d = int(np.abs(f_ink.astype(int) - u_ink.astype(int)).max()) if f_ink is not None and u_ink is not None else None
    verdict = ("OK" if ink_d is None or ink_d <= 48 else "CHECK") if name in SKIP_GEO \
        else verdict_for(name, ratio, ink_d)

    # ---- 并排图（设计空间 1:1）----
    (out_dir / "pairs").mkdir(parents=True, exist_ok=True)
    f_img = Image.fromarray(fcrop_arr)
    u_img = Image.fromarray(ucrop).resize(
        (max(1, round(ucrop.shape[1] / panel_scale)), max(1, round(ucrop.shape[0] / panel_scale))),
        Image.LANCZOS)
    h = max(f_img.height, u_img.height)
    pair = Image.new("RGB", (f_img.width + u_img.width + 12, h), (30, 32, 40))
    pair.paste(f_img, (0, 0))
    pair.paste(u_img, (f_img.width + 12, 0))
    pair.save(out_dir / "pairs" / f"{name}.png")

    return {
        "name": name,
        "fig_h_design_px": round(f_h_design, 1),
        "unity_h_panel_px": round(u_h_panel, 1),
        "ratio": round(ratio, 3),
        "fig_ink": f_ink.tolist() if f_ink is not None else None,
        "unity_ink": u_ink.tolist() if u_ink is not None else None,
        "ink_delta_maxchan": ink_d,
        "panel_scale": round(panel_scale, 3),
        "verdict": verdict,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--figma-glob", default="TestResults/figma_calib/figma/figma_comp_*.png")
    ap.add_argument("--unity-dir", default="TestResults/figma_calib/unity")
    ap.add_argument("--out", default="TestResults/figma_calib/diff")
    ap.add_argument("--names", default="")
    args = ap.parse_args()

    names = {n for n in args.names.split(",") if n}
    out = pathlib.Path(args.out)
    rows = []
    for fp in sorted(glob.glob(args.figma_glob)):
        fpp = pathlib.Path(fp)
        n = re.search(r"figma_comp_(\w+)\.png$", fpp.name).group(1)
        if names and n not in names:
            continue
        up = pathlib.Path(args.unity_dir) / f"{n}.png"
        rows.append(analyze(fpp, up, out) if up.is_file() else {"name": n, "error": "unity missing"})

    print(f"{'name':10s} {'figH/dp':>8s} {'uniH/pp':>8s} {'ratio':>6s} {'inkΔ':>5s}  verdict")
    for r in rows:
        if "error" in r:
            print(f"{r['name']:10s} ERROR {r['error']}")
        else:
            print(f"{r['name']:10s} {r['fig_h_design_px']:8.1f} {r['unity_h_panel_px']:8.1f} "
                  f"{r['ratio']:6.3f} {str(r['ink_delta_maxchan']):>5s}  {r['verdict']}")

    out.mkdir(parents=True, exist_ok=True)
    (out / "metrics.json").write_text(json.dumps(rows, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\nmetrics + pairs → {out}")


if __name__ == "__main__":
    main()
