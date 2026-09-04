#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""One-shot Figma → palette summary for Scheme A TokensFromFigma.uss.

Reads PAT from env FIGMA_PAT or ../../Temp/figma_pat.local (gitignored).
Prefer GET nodes / styles; variables/local needs file_variables:read scope.

Example:
  set FIGMA_PAT=figd_...
  python figma_pull_tokens.py --file-key oePgU63Mgqa8J8Ziqr0pdQ --node 30:62
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import urllib.error
import urllib.request
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[1]
TEMP = ROOT / "Temp"


def load_pat() -> str:
    env = os.environ.get("FIGMA_PAT", "").strip()
    if env:
        return env
    p = TEMP / "figma_pat.local"
    if p.is_file():
        return p.read_text(encoding="utf-8").strip()
    raise SystemExit("Missing FIGMA_PAT or Temp/figma_pat.local")


def get_json(url: str, pat: str) -> dict:
    req = urllib.request.Request(url, headers={"X-Figma-Token": pat})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode("utf-8"))


def walk(n: dict, items: list) -> None:
    if not isinstance(n, dict):
        return
    name = n.get("name") or ""
    for f in n.get("fills") or []:
        if f.get("type") == "SOLID" and f.get("visible", True) is not False:
            c = f.get("color") or {}
            r, g, b = int(c.get("r", 0) * 255), int(c.get("g", 0) * 255), int(c.get("b", 0) * 255)
            a = f.get("opacity", 1)
            if a is None:
                a = 1
            if a >= 0.5:
                items.append((r, g, b, name[:48]))
    if "cornerRadius" in n and isinstance(n["cornerRadius"], (int, float)):
        items.append(("radius", n["cornerRadius"], name[:48]))
    for ch in n.get("children") or []:
        walk(ch, items)


def sat(c):
    mx, mn = max(c), min(c)
    return (mx - mn) / (mx + 1e-6)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--file-key", default="oePgU63Mgqa8J8Ziqr0pdQ")
    ap.add_argument("--node", default="30:62")
    args = ap.parse_args()
    pat = load_pat()
    TEMP.mkdir(parents=True, exist_ok=True)

    node_q = args.node.replace(":", "%3A")
    nodes = get_json(
        f"https://api.figma.com/v1/files/{args.file_key}/nodes?ids={node_q}", pat
    )
    (TEMP / "figma_nodes_pull.json").write_text(json.dumps(nodes, indent=2), encoding="utf-8")
    doc = list((nodes.get("nodes") or {}).values())[0]["document"]
    items: list = []
    walk(doc, items)
    colors = [x for x in items if isinstance(x[0], int)]
    radii = [x[1] for x in items if x[0] == "radius"]
    cnt = Counter((r, g, b) for r, g, b, _ in colors)
    top = [c for c, _ in cnt.most_common(30)]
    surface = min(top, key=lambda c: c[0] + c[1] + c[2])
    card_cands = [c for c in top if 20 <= c[0] <= 50 and abs(c[0] - c[1]) < 15]
    card = card_cands[0] if card_cands else (30, 30, 30)
    accent_cands = sorted(
        [c for c in top if sat(c) > 0.15 and max(c) > 180], key=sat, reverse=True
    )
    accent = accent_cands[0] if accent_cands else (234, 255, 255)
    radius = int(Counter(int(r) for r in radii).most_common(1)[0][0]) if radii else 16
    summary = {
        "fileKey": args.file_key,
        "nodeId": args.node,
        "surface": surface,
        "card": card,
        "accent": accent,
        "radius": radius,
        "topColors": [{"rgb": list(c), "count": n} for c, n in cnt.most_common(8)],
    }
    (TEMP / "figma_palette_summary.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
