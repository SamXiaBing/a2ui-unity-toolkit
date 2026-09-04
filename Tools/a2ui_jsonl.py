#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Shared A2UI v0.8 JSONL helpers for Ollama / bench push."""

from __future__ import annotations

import json
import re
from typing import Iterable

ALLOWED_MSG = {
    "surfaceUpdate",
    "dataModelUpdate",
    "beginRendering",
    "deleteSurface",
}

KNOWN_TYPES = {
    "Text",
    "Image",
    "Icon",
    "Video",
    "AudioPlayer",
    "Row",
    "Column",
    "List",
    "Card",
    "Tabs",
    "Divider",
    "Modal",
    "Button",
    "CheckBox",
    "TextField",
    "DateTimeInput",
    "MultipleChoice",
    "Slider",
    "MediaMiniBar",
    "ClimateStep",
    "RestBanner",
    "ControlledClimate",
}


def strip_meta(text: str) -> str:
    out = []
    for line in text.splitlines():
        t = line.strip().lower()
        if t.startswith("# prompt:") or t.startswith("#prompt:"):
            continue
        if t.startswith("# theme:") or t.startswith("#theme:"):
            continue
        out.append(line)
    return "\n".join(out).rstrip() + "\n"


def extract_prompt(text: str) -> str:
    for line in text.splitlines():
        t = line.strip()
        if t.lower().startswith("# prompt:"):
            return t.split(":", 1)[1].strip()
    return ""


def _brace_split(line: str) -> list[str]:
    """拆分单行串接的 JSON 对象，例如 '{"a":1}{"b":2}' 或 '{"a":1},{"b":2}'。"""
    depth = 0
    start = 0
    results: list[str] = []
    for i, ch in enumerate(line):
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                candidate = line[start : i + 1]
                try:
                    json.loads(candidate)
                    results.append(candidate)
                except json.JSONDecodeError:
                    pass
                start = i + 1
                while start < len(line) and line[start] in ", \t\n\r":
                    start += 1
    return results


def _split_multi_key(s: str) -> list[str]:
    """把 '{"surfaceUpdate":{...},"beginRendering":{...}}' 拆成单 key 行。"""
    try:
        obj = json.loads(s)
    except json.JSONDecodeError:
        return []
    if not isinstance(obj, dict) or len(obj) <= 1:
        return [s]
    out = []
    for key, val in obj.items():
        out.append(json.dumps({key: val}, ensure_ascii=False))
    return out


def extract_jsonl(text: str) -> str:
    text = (text or "").strip()
    if "```" in text:
        text = re.sub(r"```(?:jsonl|json)?", "", text).replace("```", "")
    lines: list[str] = []
    for line in text.splitlines():
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        if not s.startswith("{"):
            continue
        # 1) 正常单对象行
        try:
            json.loads(s)
            lines.append(s)
            continue
        except json.JSONDecodeError:
            pass
        # 2) 可能多对象串在一行：{"a":1}{"b":2} 或 {"a":1},{"b":2}
        split = _brace_split(s)
        if split:
            lines.extend(split)
    # 3) 多 key 合并在一行的对象拆成单 key 行
    final: list[str] = []
    for item in lines:
        final.extend(_split_multi_key(item))
    return "\n".join(final) + ("\n" if final else "")


def validate_jsonl(jsonl: str, *, check_types: bool = True) -> None:
    n = 0
    keys_seen: list[str] = []
    for line in jsonl.splitlines():
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        obj = json.loads(s)
        if not isinstance(obj, dict) or len(obj) != 1:
            raise ValueError(f"each line must be single-key object: {s[:80]}")
        key = next(iter(obj.keys()))
        if key not in ALLOWED_MSG:
            raise ValueError(f"unknown message type: {key}")
        keys_seen.append(key)
        if check_types and key == "surfaceUpdate":
            comps = obj["surfaceUpdate"].get("components") or []
            for c in comps:
                wrap = c.get("component") or {}
                if len(wrap) != 1:
                    raise ValueError(f"component {c.get('id')} must have one type key")
                typ = next(iter(wrap.keys()))
                if typ not in KNOWN_TYPES:
                    raise ValueError(f"unknown component type: {typ}")
        n += 1
    if n == 0:
        raise ValueError("empty jsonl")
    # 不再强制要求 surfaceUpdate / beginRendering，以支持增量包：
    #   - 纯 dataModelUpdate（只改数据，不发 surfaceUpdate）
    #   - 局部 surfaceUpdate（追加/删除组件，root 不变，可不带 beginRendering）
    # C# 端对增量包是放行的，这里与之一致。

    # 交叉检查：仅当同包内既有 surfaceUpdate 又有 beginRendering 时，
    # beginRendering.root 必须在 surfaceUpdate.components 里
    su_comp_ids: dict[str, set[str]] = {}  # surfaceId → component ids
    br_roots: dict[str, str] = {}  # surfaceId → root id
    for line in jsonl.splitlines():
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        obj = json.loads(s)
        key = next(iter(obj.keys()))
        if key == "surfaceUpdate":
            su = obj["surfaceUpdate"]
            sid = su.get("surfaceId", "")
            if sid not in su_comp_ids:
                su_comp_ids[sid] = set()
            for c in (su.get("components") or []):
                cid = c.get("id")
                if cid:
                    su_comp_ids[sid].add(str(cid))
        elif key == "beginRendering":
            br = obj["beginRendering"]
            sid = br.get("surfaceId", "")
            root = br.get("root", "")
            br_roots[sid] = str(root)

    for sid, root in br_roots.items():
        comps = su_comp_ids.get(sid, set())
        if sid in su_comp_ids and root not in comps:
            avail = ", ".join(sorted(comps)) if comps else "NONE"
            raise ValueError(
                f"beginRendering.root '{root}' 不在 surfaceUpdate.components 里 "
                f"(surfaceId='{sid}', 可用的id: {avail})"
            )


def load_text(path) -> str:
    return path.read_text(encoding="utf-8")


def join_system(parts: Iterable[str]) -> str:
    return "\n\n".join(p.strip() for p in parts if p and p.strip())
