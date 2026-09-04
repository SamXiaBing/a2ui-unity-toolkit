#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Figma REST API -> A2UI USS（Scheme A 转换链·真实文件版）。

和 figma_plugin/ 配合：插件在 Figma 浏览器里点一下导出 JSON（路径 A）；
本脚本走 REST API，用 Personal Access Token 直接拉真实节点 JSON（路径 B），
两条路产物都喂给 figma_to_uss.py 出 USS，再进 Unity 测试，互不冲突。

前置：
  - Figma PAT（Personal Access Token），从 figma 账号 Settings -> Security 生成
  - 文件 key（URL 里 figma.com/file/<KEY>/... 那段）
  - 节点 id（选中组件/画板后 URL 末尾 #<NODE_ID>，或右键 Copy link to selection）

用法：
  # 列出文件里所有节点（找你要导出的 node id）
  python figma_api_export.py --token $FIGMA_TOKEN --file-key <KEY> --discover

  # 拉某个节点 JSON 并直接转 USS
  python figma_api_export.py --token $FIGMA_TOKEN --file-key <KEY> --node-id <NODE> --convert

  # 也可以从环境变量读 token
  set FIGMA_TOKEN=figd_xxx
  python figma_api_export.py --file-key <KEY> --node-id <NODE> --convert
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
SAMPLES = ROOT / "Tools/figma_samples"
OUTDIR = ROOT / "Assets/A2UISchemeA/Styles/FigmaExport"
CONVERTER = ROOT / "Tools/figma_to_uss.py"
API = "https://api.figma.com/v1"


def api_get(tok: str, path: str):
    req = urllib.request.Request(API + path, headers={"X-Figma-Token": tok})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def discover(tok: str, key: str, depth: int = 3) -> None:
    d = api_get(tok, f"/files/{key}?depth={depth}")
    doc = d.get("document", {})
    print(f"文件: {doc.get('name')}  (root id={doc.get('id')})")
    print("节点树（id 可直接喂 --node-id）:")

    def walk(n: dict, lv: int) -> None:
        if lv > depth:
            return
        sid = n.get("id")
        name = n.get("name")
        typ = n.get("type")
        print(f"{'  ' * lv}[{typ}] {sid}  {name!r}")
        for ch in n.get("children", [])[:40]:
            walk(ch, lv + 1)

    walk(doc, 0)


def pull(tok: str, key: str, node_id: str, depth: int, out: pathlib.Path) -> dict:
    d = api_get(tok, f"/files/{key}/nodes?ids={node_id}&depth={depth}")
    out.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
    return d


def run_converter(in_path: pathlib.Path, scope: str) -> None:
    cmd = [sys.executable, str(CONVERTER), "--input", str(in_path),
           "--outdir", str(OUTDIR), "--scope", scope]
    print(">> 运行转换器:", " ".join(cmd))
    subprocess.run(cmd, check=True)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--token", default=os.environ.get("FIGMA_TOKEN"),
                    help="Figma PAT（默认读环境变量 FIGMA_TOKEN）")
    ap.add_argument("--file-key", required=True, help="文件 key")
    ap.add_argument("--node-id", help="要导出的节点 id（discover 时不用）")
    ap.add_argument("--depth", type=int, default=4, help="导出子树深度")
    ap.add_argument("--out", default=None, help="JSON 落盘路径，默认 figma_samples/figma_<node>.json")
    ap.add_argument("--scope", default="a2ui-skin--figma-export", help="USS 作用域类")
    ap.add_argument("--discover", action="store_true", help="只打印节点树，不导出")
    ap.add_argument("--no-convert", action="store_true", help="只拉 JSON，不跑转换器")
    args = ap.parse_args()

    if not args.token:
        ap.error("缺少 token：传 --token 或设置环境变量 FIGMA_TOKEN")
    if args.discover:
        discover(args.token, args.file_key, args.depth)
        return
    if not args.node_id:
        ap.error("导出需要 --node-id（先用 --discover 找）")

    SAMPLES.mkdir(parents=True, exist_ok=True)
    out = pathlib.Path(args.out) if args.out else SAMPLES / f"figma_{args.node_id}.json"
    print(f">> 拉取节点 {args.node_id} ...")
    pull(args.token, args.file_key, args.node_id, args.depth, out)
    print(f">> 已存 {out}")

    if not args.no_convert:
        run_converter(out, args.scope)
        print(">> 完成。USS 在:", OUTDIR)


if __name__ == "__main__":
    main()
