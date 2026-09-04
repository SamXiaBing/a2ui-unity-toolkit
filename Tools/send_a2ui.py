#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Send standard A2UI v0.8 JSONL to Scheme A Unity host.

Examples:
  python send_a2ui.py --sample media
  python send_a2ui.py --sample climate
  python send_a2ui.py --sample rest
  python send_a2ui.py --jsonl-file path/to/surface.jsonl --prompt "换首歌"
"""

from __future__ import annotations

import argparse
import pathlib
import sys
import urllib.error
import urllib.request

PORT = 18766
URL = f"http://127.0.0.1:{PORT}/a2ui"

SAMPLES = {
    "media": "features/media_player.v0.8.jsonl",
    "climate": "features/climate_control.v0.8.jsonl",
    "rest": "features/rest_banner.v0.8.jsonl",
    "tour": "demos/coverage_tour.v0.8.jsonl",
    "catalog": "demos/catalog_all.v0.8.jsonl",
    "poi": "scenarios/app_03_poi_nearby.v0.8.jsonl",
    "unknown": "edge/unknown_type.v0.8.jsonl",
    "bad": "edge/bad_packet.v0.8.jsonl",
    "login": "scenarios/app_01_login_screen.v0.8.jsonl",
    "charge_v09": "scenarios/agent_01_charge_pick.v0.9.jsonl",
    "all_v09": "demos/full_control_center.v0.9.jsonl",
}


def project_root() -> pathlib.Path:
    return pathlib.Path(__file__).resolve().parents[1]


def samples_dir() -> pathlib.Path:
    return project_root() / "Assets" / "A2UISchemeA" / "Samples"


def extract_prompt(text: str) -> str:
    for line in text.splitlines():
        t = line.strip()
        if t.lower().startswith("# prompt:"):
            return t.split(":", 1)[1].strip()
    return ""


def strip_meta(text: str) -> str:
    out = []
    for line in text.splitlines():
        t = line.strip()
        if t.lower().startswith("# prompt:"):
            continue
        out.append(line)
    return "\n".join(out).rstrip() + "\n"


def send_http(payload: str, prompt: str) -> None:
    body = payload.encode("utf-8")
    req = urllib.request.Request(URL, data=body, method="POST")
    req.add_header("Content-Type", "application/jsonl; charset=utf-8")
    if prompt:
        # Latin-1 header safe: keep ASCII/Chinese via UTF-8 percent if needed — use UTF-8 bytes in header may fail
        try:
            prompt.encode("latin-1")
            req.add_header("X-A2UI-Prompt", prompt)
        except UnicodeEncodeError:
            req.add_header("X-A2UI-Prompt", prompt.encode("unicode_escape").decode("ascii"))
    with urllib.request.urlopen(req, timeout=3) as resp:
        print(resp.read().decode("utf-8"))


def send_file(payload: str, prompt: str) -> None:
    inbox_dir = project_root() / "Temp" / "A2UISchemeA"
    inbox_dir.mkdir(parents=True, exist_ok=True)
    inbox = inbox_dir / "inbox.jsonl"
    lines = []
    if prompt:
        lines.append(f"# prompt: {prompt}")
    lines.append(payload.rstrip("\n"))
    text = "\n".join(lines) + "\n"
    inbox.write_text(text, encoding="utf-8")
    print(f"wrote {inbox}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Send A2UI v0.8 JSONL to Scheme A")
    parser.add_argument("--sample", choices=sorted(SAMPLES.keys()), help="Built-in sample name")
    parser.add_argument("--jsonl-file", type=pathlib.Path, help="Custom JSONL file")
    parser.add_argument("--prompt", default="", help="Override prompt shown in Unity")
    parser.add_argument("--file-only", action="store_true", help="Only write inbox, skip HTTP")
    args = parser.parse_args()

    if not args.sample and not args.jsonl_file:
        parser.error("need --sample or --jsonl-file")

    if args.jsonl_file:
        path = args.jsonl_file
        text = path.read_text(encoding="utf-8")
    else:
        path = samples_dir() / SAMPLES[args.sample]
        text = path.read_text(encoding="utf-8")

    prompt = args.prompt or extract_prompt(text)
    jsonl = strip_meta(text)
    # Prefer embedding prompt as meta line for inbox + HTTP body so Unity always sees it
    payload = (f"# prompt: {prompt}\n" if prompt else "") + jsonl

    print(f"source={path}")
    print(f"prompt={prompt}")
    if args.file_only:
        send_file(jsonl, prompt)
        return 0

    try:
        send_http(payload, prompt)
    except (urllib.error.URLError, OSError) as e:
        print(f"HTTP failed ({e}); fallback to inbox", file=sys.stderr)
        send_file(jsonl, prompt)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
