#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Push A2UI v0.8 JSONL to bench Launcher Runtime via adb forward (no Editor render).

  adb forward tcp:18766 tcp:18766
  python push_a2ui_bench.py --jsonl-file x.jsonl --prompt "给我一个音乐面板"
  python push_a2ui_bench.py --jsonl-file x.jsonl --theme beach
  type x.jsonl | python push_a2ui_bench.py --stdin
"""

from __future__ import annotations

import argparse
import json
import pathlib
import socket
import subprocess
import sys
import urllib.error
import urllib.request

PORT = 18766
HTTP_A2UI = f"http://127.0.0.1:{PORT}/a2ui"
HTTP_THEME = f"http://127.0.0.1:{PORT}/theme"

ALLOWED = {
    "surfaceUpdate",
    "dataModelUpdate",
    "beginRendering",
    "deleteSurface",
}


def ensure_adb_forward() -> None:
    try:
        out = subprocess.check_output(["adb", "forward", "--list"], text=True, stderr=subprocess.STDOUT)
    except Exception as e:
        print("adb not available:", e)
        return
    needle = f"tcp:{PORT}"
    if needle in out and f"tcp:{PORT}" in out:
        # already forwarded somewhere; ok if present
        if f"tcp:{PORT} tcp:{PORT}" in out.replace(" ", " "):
            print("adb forward already set")
            return
    try:
        subprocess.check_call(["adb", "forward", f"tcp:{PORT}", f"tcp:{PORT}"])
        print(f"adb forward tcp:{PORT} tcp:{PORT}")
    except Exception as e:
        print("adb forward failed:", e)


def extract_prompt(text: str) -> str:
    for line in text.splitlines():
        t = line.strip()
        if t.lower().startswith("# prompt:"):
            return t.split(":", 1)[1].strip()
    return ""


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


def validate_jsonl(jsonl: str) -> None:
    n = 0
    for line in jsonl.splitlines():
        s = line.strip()
        if not s:
            continue
        obj = json.loads(s)
        if not isinstance(obj, dict) or len(obj) != 1:
            raise ValueError(f"each line must be single-key object: {s[:80]}")
        key = next(iter(obj.keys()))
        if key not in ALLOWED:
            raise ValueError(f"unknown message type: {key}")
        n += 1
    if n == 0:
        raise ValueError("empty jsonl")


def send_http_a2ui(payload: str, prompt: str) -> None:
    body = payload.encode("utf-8")
    req = urllib.request.Request(HTTP_A2UI, data=body, method="POST")
    req.add_header("Content-Type", "application/jsonl; charset=utf-8")
    if prompt:
        try:
            prompt.encode("latin-1")
            req.add_header("X-A2UI-Prompt", prompt)
        except UnicodeEncodeError:
            req.add_header("X-A2UI-Prompt", prompt.encode("unicode_escape").decode("ascii"))
    with urllib.request.urlopen(req, timeout=5) as resp:
        print("HTTP /a2ui:", resp.read().decode("utf-8"))


def send_http_theme(theme: str) -> None:
    body = json.dumps({"theme": theme}).encode("utf-8")
    req = urllib.request.Request(HTTP_THEME, data=body, method="POST")
    req.add_header("Content-Type", "application/json; charset=utf-8")
    with urllib.request.urlopen(req, timeout=5) as resp:
        print("HTTP /theme:", resp.read().decode("utf-8"))


def send_tcp(payload: str, prompt: str, theme: str | None) -> None:
    """Frame: optional #theme / #prompt lines then JSONL, end with blank line."""
    chunks = []
    if theme:
        chunks.append(f"# theme: {theme}\n")
    if prompt:
        chunks.append(f"# prompt: {prompt}\n")
    chunks.append(payload if payload.endswith("\n") else payload + "\n")
    chunks.append("\n")
    data = "".join(chunks).encode("utf-8")
    with socket.create_connection(("127.0.0.1", PORT), timeout=5) as sock:
        sock.sendall(data)
        sock.shutdown(socket.SHUT_WR)
        try:
            ack = sock.recv(256)
            print("TCP:", ack.decode("utf-8", errors="replace"))
        except Exception:
            print("TCP: sent (no ack)")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--jsonl-file")
    ap.add_argument("--stdin", action="store_true")
    ap.add_argument("--prompt", default="")
    ap.add_argument("--theme", default="", help="a|b|dark|ice|beach|pink|green|aaos|cloud（dark=M3 暗色）")
    ap.add_argument("--skip-adb", action="store_true")
    args = ap.parse_args()

    if not args.skip_adb:
        ensure_adb_forward()

    if args.stdin:
        text = sys.stdin.read()
    elif args.jsonl_file:
        text = pathlib.Path(args.jsonl_file).read_text(encoding="utf-8")
    else:
        raise SystemExit("need --jsonl-file or --stdin")

    prompt = args.prompt or extract_prompt(text)
    jsonl = strip_meta(text)
    validate_jsonl(jsonl)
    theme = (args.theme or "").strip().lower() or None

    http_ok = False
    try:
        if theme:
            send_http_theme(theme)
        send_http_a2ui(jsonl, prompt)
        http_ok = True
    except Exception as e:
        print("HTTP failed:", e)

    if not http_ok:
        print("Trying TCP...")
        send_tcp(jsonl, prompt, theme)


if __name__ == "__main__":
    main()
