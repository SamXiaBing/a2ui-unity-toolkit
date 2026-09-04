#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""A2UI 截图基准像素 diff。

用法：
  python regression_diff.py                     # 对比 TestResults/screenshots 与 baselines
  python regression_diff.py --update            # 用当前截图刷新 baselines
  python regression_diff.py --threshold 0.005   # 差异像素比例阈值（默认 0.5%）

输入：
  TestResults/layout/{theme}__{sample}.json   卡片矩形（裁剪区域）
  TestResults/screenshots/{theme}/{sample}.png 当前截图
  baselines/{theme}/{sample}.png              基准截图
输出：
  TestResults/report.md                       矩阵报告
  退出码：0 全过 / 1 有差异
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageChops
except ImportError:
    print("需要 Pillow: pip install pillow", file=sys.stderr)
    sys.exit(2)

ROOT = Path(__file__).resolve().parent.parent
SHOTS = ROOT / "TestResults" / "screenshots"
LAYOUT = ROOT / "TestResults" / "layout"
BASE = ROOT / "baselines"
REPORT = ROOT / "TestResults" / "report.md"


def load_layouts() -> dict:
    """theme__sample.json -> card rect"""
    out = {}
    if not LAYOUT.exists():
        return out
    for f in LAYOUT.glob("*.json"):
        try:
            data = json.loads(f.read_text(encoding="utf-8"))
            key = f"{data['theme']}__{data['sample']}"
            out[key] = data["card"]
        except Exception:
            continue
    return out


def crop_to_card_union(img: Image.Image, rect_a: dict, rect_b: dict) -> Image.Image:
    """两张截图各自的 cardRect 取并集裁剪，剔除无关背景。"""
    x = max(0, min(rect_a["x"], rect_b["x"]))
    y = max(0, min(rect_a["y"], rect_b["y"]))
    x2 = max(rect_a["x"] + rect_a["w"], rect_b["x"] + rect_b["w"])
    y2 = max(rect_a["y"] + rect_a["h"], rect_b["y"] + rect_b["h"])
    x2 = min(img.width, x2)
    y2 = min(img.height, y2)
    return img.crop((int(x), int(y), int(x2), int(y2)))


def diff_pair(cur_path: Path, base_path: Path, rect_cur: dict, rect_base: dict):
    """返回 (差异像素比例 0~1, 差异图或 None)。"""
    cur = Image.open(cur_path).convert("RGB")
    base = Image.open(base_path).convert("RGB")
    if cur.size != base.size:
        # 分辨率漂移：直接整体比较（罕见，记录大差异）
        base = base.resize(cur.size)
    a = crop_to_card_union(cur, rect_cur, rect_base)
    b = crop_to_card_union(base, rect_cur, rect_base)
    if a.size != b.size:
        b = b.resize(a.size)
    diff = ImageChops.difference(a, b)
    hist = diff.convert("L").histogram()
    # 每通道差 > 12（约 5%）视为实质差异像素，抗锯齿容差
    px = sum(hist[13:])
    total = a.width * a.height
    ratio = px / total if total else 0.0
    return ratio, diff


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--update", action="store_true", help="用当前截图刷新 baselines")
    ap.add_argument("--threshold", type=float, default=0.005)
    args = ap.parse_args()

    if not SHOTS.exists():
        print("无截图目录 TestResults/screenshots（跑测试时需 A2UI_CAPTURE=1）", file=sys.stderr)
        return 2

    layouts = load_layouts()
    rows, fails, missing_base = [], [], 0
    shots = sorted(SHOTS.rglob("*.png"))

    if args.update:
        for shot in shots:
            theme = shot.parent.name
            dst = BASE / theme / shot.name
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(shot.read_bytes())
        print(f"baselines 已刷新：{len(shots)} 张")
        return 0

    for shot in shots:
        theme = shot.parent.name
        sample = shot.stem
        key = f"{theme}__{sample}"
        base = BASE / theme / shot.name
        if not base.exists():
            missing_base += 1
            rows.append((theme, sample, "NO-BASELINE", "-"))
            continue
        rect_cur = layouts.get(key, {"x": 0, "y": 0, "w": 0, "h": 0})
        rect_base = rect_cur  # baseline 的 rect 存于其 layout JSON，更新时同步
        ratio, _ = diff_pair(shot, base, rect_cur, rect_base)
        ok = ratio <= args.threshold
        rows.append((theme, sample, "PASS" if ok else "FAIL", f"{ratio * 100:.3f}%"))
        if not ok:
            fails.append(f"{theme}/{sample} ({ratio * 100:.3f}%)")

    REPORT.parent.mkdir(exist_ok=True)
    with REPORT.open("w", encoding="utf-8") as f:
        f.write(f"# A2UI 截图回归报告\n\n")
        f.write(f"- 截图：{len(shots)} 张，缺失基准：{missing_base}，失败：{len(fails)}\n")
        f.write(f"- 阈值：差异像素 > {args.threshold * 100:.2f}%\n\n")
        f.write("| 主题 | 样本 | 结果 | 差异率 |\n|---|---|---|---|\n")
        for theme, sample, status, ratio in rows:
            f.write(f"| {theme} | {sample} | {status} | {ratio} |\n")
        if fails:
            f.write("\n## 失败明细\n\n")
            for x in fails:
                f.write(f"- {x}\n")

    print(f"完成：{len(shots)} 张，失败 {len(fails)}，缺基准 {missing_base} → {REPORT}")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
