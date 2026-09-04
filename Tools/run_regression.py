#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""A2UI 一键回归：batchmode 跑 PlayMode 全矩阵测试 → 截图 diff → 汇总。

用法：
  python run_regression.py --editor "D:/Program Files/Tuanjie 2022.3.55t4/Editor/Tuanjie.exe"
  python run_regression.py --editor ... --update-baselines   # 刷新截图基准
  python run_regression.py --editor ... --only-geometry      # 跳过截图（快速/无独显环境）
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESULTS = ROOT / "TestResults"


def run_tests(editor: str, capture: bool) -> int:
    RESULTS.mkdir(exist_ok=True)
    env = dict(os.environ)
    env["A2UI_CAPTURE"] = "1" if capture else "0"
    cmd = [
        editor,
        "-batchmode",
        "-projectPath", str(ROOT),
        "-logFile", str(RESULTS / "regression.log"),
        "-runTests",
        "-testPlatform", "PlayMode",
        "-testResults", str(RESULTS / "results.xml"),
        "-screen-width", "1920",
        "-screen-height", "1080",
        "-screen-fullscreen", "0",
    ]
    print(">>", " ".join(cmd))
    proc = subprocess.run(cmd, env=env)
    return proc.returncode


def parse_results() -> tuple[int, int, list[str]]:
    xml = RESULTS / "results.xml"
    if not xml.exists():
        return 0, -1, ["results.xml 未生成"]
    tree = ET.parse(xml)
    run = tree.getroot()
    total = int(run.get("total", 0))
    passed = int(run.get("passed", 0))
    fails = []
    for case in run.iter("test-case"):
        if case.get("result") == "Failed":
            msg = ""
            f = case.find("failure/message")
            if f is not None:
                msg = (f.text or "")[:200]
            fails.append(f"{case.get('fullname', '?')}: {msg}")
    return total, passed, fails


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--editor", required=True, help="Tuanjie.exe 路径")
    ap.add_argument("--update-baselines", action="store_true")
    ap.add_argument("--only-geometry", action="store_true",
                    help="跳过截图 diff（batchmode 无 GPU 渲染帧时的推荐模式）")
    args = ap.parse_args()

    # batchmode 下 ReadPixels/onPostRender 无渲染帧，截图多数 CI 环境无输出；
    # 几何断言始终执行。截图基准请在编辑器 GUI 跑 Test Runner（A2UI_CAPTURE=1）后 --update-baselines。
    capture = not args.only_geometry
    code = run_tests(args.editor, capture)
    print(f"editor exit: {code}")

    total, passed, fails = parse_results()
    print(f"测试：{passed}/{total} 通过")
    for f in fails[:10]:
        print("  FAIL", f)

    if capture and total > 0 and passed >= 0:
        diff_cmd = [sys.executable, str(ROOT / "Tools" / "regression_diff.py")]
        if args.update_baselines:
            diff_cmd.append("--update")
        dc = subprocess.run(diff_cmd).returncode
        if not args.update_baselines and dc != 0:
            print("截图 diff 存在差异（见 TestResults/report.md）")
            return 1

    ok = total > 0 and len(fails) == 0
    print("回归结果：", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
