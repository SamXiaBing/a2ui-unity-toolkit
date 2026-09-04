#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""PC Ollama → A2UI v0.8 JSONL → Unity Scheme A (18766).

Requires: ollama serve + a light model (default qwen2.5:1.5b).

Examples:
  python ollama_a2ui_loop.py --once
  python ollama_a2ui_loop.py --interval 12 --beats 4
  python ollama_a2ui_loop.py --fallback-only   # no model, push timeline samples
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
import time
import urllib.error
import urllib.request

PORT = 18766
URL = f"http://127.0.0.1:{PORT}/a2ui"
OLLAMA = "http://127.0.0.1:11434/api/generate"
DEFAULT_MODEL = "qwen2.5:1.5b"

SYSTEM = """You output ONLY A2UI v0.8 JSONL lines for an automotive HMI card.
Allowed component types: Text, Row, Column, Card, Button, List, Divider, Icon, MediaMiniBar, RestBanner.
Messages must include surfaceUpdate then beginRendering. Optional dataModelUpdate.
No markdown fences. No commentary. surfaceId must be \"bench\".
Keep under 25 components. Chinese UI copy is OK.
"""

BEAT_PROMPTS = [
    "场景：行车中媒体条，标题夜航星图，一个暂停按钮。",
    "场景：系统事件低电 18%，去补能与稍后两个按钮。",
    "场景：附近三家店的列表（可用 explicitList 三行 Text，或 template）。",
    "场景：休憩模式横幅 + 开启勿扰按钮。",
]


def samples_dir() -> pathlib.Path:
    return pathlib.Path(__file__).resolve().parent.parent / "Samples" / "timeline_bench"


def fallback_paths() -> list[pathlib.Path]:
    d = samples_dir()
    return [
        d / "01_media.v0.8.jsonl",
        d / "02_low_battery.v0.8.jsonl",
        d / "03_poi_list.v0.8.jsonl",
        d / "04_rest.v0.8.jsonl",
    ]


def strip_meta(text: str) -> str:
    out = []
    for line in text.splitlines():
        t = line.strip()
        if t.lower().startswith("# prompt:"):
            continue
        out.append(line)
    return "\n".join(out).rstrip() + "\n"


def extract_jsonl(text: str) -> str:
    text = text.strip()
    if "```" in text:
        text = re.sub(r"```(?:jsonl|json)?", "", text).replace("```", "")
    lines = []
    for line in text.splitlines():
        s = line.strip()
        if not s:
            continue
        if s.startswith("{") and s.endswith("}"):
            try:
                json.loads(s)
                lines.append(s)
            except json.JSONDecodeError:
                continue
    return "\n".join(lines) + ("\n" if lines else "")


def send_http(payload: str, prompt: str) -> None:
    body = payload.encode("utf-8")
    req = urllib.request.Request(URL, data=body, method="POST")
    req.add_header("Content-Type", "application/jsonl; charset=utf-8")
    try:
        prompt.encode("latin-1")
        req.add_header("X-A2UI-Prompt", prompt)
    except UnicodeEncodeError:
        req.add_header("X-A2UI-Prompt", prompt.encode("unicode_escape").decode("ascii"))
    with urllib.request.urlopen(req, timeout=5) as resp:
        print("Unity:", resp.read().decode("utf-8"))


def send_inbox(payload: str) -> None:
    root = pathlib.Path(__file__).resolve().parents[1]
    inbox = root / "Temp" / "A2UISchemeA" / "inbox.jsonl"
    inbox.parent.mkdir(parents=True, exist_ok=True)
    inbox.write_text(payload, encoding="utf-8")
    print("Wrote inbox", inbox)


def push(payload: str, prompt: str) -> None:
    try:
        send_http(payload, prompt)
    except Exception as e:
        print("HTTP failed, fallback inbox:", e)
        send_inbox(("# prompt: " + prompt + "\n" if prompt else "") + payload)


def ollama_generate(model: str, user: str) -> str:
    body = json.dumps(
        {
            "model": model,
            "prompt": user,
            "system": SYSTEM,
            "stream": False,
            "options": {"temperature": 0.2},
        }
    ).encode("utf-8")
    req = urllib.request.Request(OLLAMA, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=180) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return data.get("response") or ""


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--interval", type=float, default=12.0)
    ap.add_argument("--beats", type=int, default=4)
    ap.add_argument("--once", action="store_true")
    ap.add_argument("--fallback-only", action="store_true")
    args = ap.parse_args()

    n = 1 if args.once else max(1, args.beats)
    for i in range(n):
        prompt = BEAT_PROMPTS[i % len(BEAT_PROMPTS)]
        payload = ""
        if not args.fallback_only:
            try:
                raw = ollama_generate(args.model, prompt)
                payload = extract_jsonl(raw)
                if "surfaceUpdate" not in payload or "beginRendering" not in payload:
                    print("Model output invalid, use fallback")
                    payload = ""
            except Exception as e:
                print("Ollama failed:", e)
                payload = ""

        if not payload:
            fb = fallback_paths()[i % len(fallback_paths())]
            text = fb.read_text(encoding="utf-8")
            payload = strip_meta(text)
            prompt = f"fallback:{fb.name}"

        print(f"--- beat {i+1}/{n} · {prompt[:40]}")
        push(payload, prompt)
        if args.once or i + 1 >= n:
            break
        time.sleep(args.interval)


if __name__ == "__main__":
    main()
