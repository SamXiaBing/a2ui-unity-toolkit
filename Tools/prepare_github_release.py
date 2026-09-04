#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 GitHub 发布用的新仓库：当前 HEAD 的干净快照 + 单个初始提交。

为什么需要它：本仓库的 git 历史包含 142MB 的旧资源对象与内部工作邮箱
（xiabing7@faw.com.cn）的提交署名，一旦 push 无法从历史移除。本脚本用
`git archive` 导出当前 HEAD 的全部 tracked 文件（天然排除 .git 与未跟踪
的临时文件），在新目录里以个人身份重建单提交历史。

用法（在仓库根目录执行）：
  python Tools/prepare_github_release.py                # 默认输出 ../a2ui-unity-toolkit-release
  python Tools/prepare_github_release.py --dest D:/path/to/dir

产出后手动步骤：
  cd <dest>
  git remote add origin https://github.com/SamXiaBing/a2ui-unity-toolkit.git
  git push -u origin main --tags
然后在 GitHub 建仓库（建议名 a2ui-unity-toolkit，勿初始化 README）、
基于 v0.1.0 tag 发 Release、设置 topics：
  unity, tuanjie, a2ui, genui, agent, llm, ui-toolkit, cockpit
"""
from __future__ import annotations

import argparse
import pathlib
import subprocess
import sys

AUTHOR_NAME = "SamXiaBing"
AUTHOR_EMAIL = "SamXiaBing@users.noreply.github.com"  # GitHub 隐私转发地址，可在 GitHub Settings→Emails 确认
TAG = "v0.1.0"


def run(cmd: list[str], cwd: pathlib.Path | None = None) -> None:
    print(">>", " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=str(cwd) if cwd else None)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dest", default=None, help="新仓库目录（默认 ../a2ui-unity-toolkit-release）")
    ap.add_argument("--name", default=AUTHOR_NAME)
    ap.add_argument("--email", default=AUTHOR_EMAIL)
    args = ap.parse_args()

    src = pathlib.Path.cwd()
    head = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    dirty = subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True, check=True).stdout.strip()
    if dirty:
        print("工作区有未提交改动，先提交或 stash 再运行。", file=sys.stderr)
        return 1

    dest = pathlib.Path(args.dest) if args.dest else src.parent / (src.name + "-release")
    if dest.exists() and any(dest.iterdir()):
        print(f"目标目录非空: {dest}", file=sys.stderr)
        return 1
    dest.mkdir(parents=True, exist_ok=True)

    # 1) 导出 HEAD 干净快照（tar 走系统 bsdtar，Windows 10+ 自带）
    archive = dest.parent / (dest.name + ".tar")
    run(["git", "archive", "--format=tar", head, "-o", str(archive.resolve())], cwd=src)
    run(["tar", "-xf", str(archive.resolve())], cwd=dest)
    archive.unlink()

    # 2) 新仓库 + main 分支 + 单提交（个人身份）
    run(["git", "init", "-b", "main"], cwd=dest)
    run(["git", "add", "-A"], cwd=dest)
    run(["git", "-c", f"user.name={args.name}", "-c", f"user.email={args.email}",
         "commit", "-m", "feat: A2UI Unity Toolkit v0.1.0 — v0.8/v0.9 dual-stack UI Toolkit renderer\n\n"
         "Render Agent-generated A2UI JSONL natively with Unity/Tuanjie UI Toolkit:\n"
         "protocol host (HTTP/TCP/inbox) + G0 validation + surface FSM + CatalogMapper,\n"
         "hot-switchable USS themes (DS / M3 Light & Dark / Figma-export pipeline),\n"
         "full-matrix layout regression (500+ combos), editor push panel, and a\n"
         "12-component Figma visual-calibration harness. MIT licensed."], cwd=dest)
    run(["git", "tag", TAG], cwd=dest)
    run(["git", "log", "--format=full", "-n", "1"], cwd=dest)

    print(f"""
完成：{dest}
  分支: main · tag: {TAG} · 作者: {args.name} <{args.email}>
下一步：
  1. GitHub 上建空仓库（不要初始化 README/LICENSE）
  2. cd {dest}
  3. git remote add origin https://github.com/SamXiaBing/a2ui-unity-toolkit.git
  4. git push -u origin main --tags
  5. Releases → Draft new release → 选 {TAG} → 标题 "v0.1.0" → Publish
  6. About → Topics: unity, tuanjie, a2ui, genui, agent, llm, ui-toolkit, cockpit
""")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
