#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Local Ollama → A2UI v0.8 JSONL → Editor or bench.

用法：
  # 交互多轮（推荐演示）：直接说话，上下文自动保持
  python ollama_a2ui_chat.py --target editor
  # 单条（兼容原用法）：
  python ollama_a2ui_chat.py --target editor --prompt "开宠物模式…"
  # 跳过模型直接推金标，保证演示不冷场：
  python ollama_a2ui_chat.py --fallback pet --target editor
  # 仅校验不推送：
  python ollama_a2ui_chat.py --dry-run --prompt "…"
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import urllib.request
from urllib.parse import quote

PORT = 18766
HTTP_A2UI = f"http://127.0.0.1:{PORT}/a2ui"
HTTP_THEME = f"http://127.0.0.1:{PORT}/theme"
OLLAMA_GEN = "http://127.0.0.1:11434/api/generate"
DEFAULT_MODEL = "a2ui-cabin"
TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

from a2ui_jsonl import (  # noqa: E402
    extract_jsonl,
    extract_prompt,
    join_system,
    load_text,
    strip_meta,
    validate_jsonl,
)
SPEC = TOOLS / "a2ui_ollama"
OUT = ROOT / "Temp" / "A2UISchemeA" / "ollama_out.jsonl"

FALLBACKS = {
    "pet": SPEC / "fewshot" / "pet_preference_grow.v0.8.jsonl",
    "board": SPEC / "fewshot" / "my_pet_board.v0.8.jsonl",
    "wash": ROOT / "Assets" / "A2UISchemeA" / "Samples" / "scenarios" / "agent_07_wash_exception.v0.8.jsonl",
}

# 用户话里命中这些词就额外 POST /theme（Host 端也会按提示词兜底，两条路并存）
THEME_KEYWORDS = {
    "pink": ["粉", "粉色", "粉红", "pink", "可爱", "萌"],
    "beach": ["海滩", "沙滩", "beach", "度假", "阳光"],
    "ice": ["冰蓝", "冰", "ice", "冷色", "科技"],
}


def build_system(prior_jsonl: str | None = None) -> str:
    parts = [
        load_text(SPEC / "SYSTEM.md"),
        load_text(SPEC / "CATALOG.md"),
        load_text(SPEC / "PUSH.md"),
    ]
    fs = SPEC / "fewshot" / "pet_preference_grow.v0.8.jsonl"
    if fs.is_file():
        parts.append("## Few-shot example (pet preference grow)\n" + load_text(fs))
    fs2 = SPEC / "fewshot" / "pet_incremental.v0.8.jsonl"
    if fs2.is_file():
        parts.append("## Few-shot example (pet incremental: append module / edit value)\n" + load_text(fs2))
    if prior_jsonl and prior_jsonl.strip():
        parts.append(
            "## 当前面板（上一轮已渲染的 JSONL）\n"
            "如果你要增量修改，请复用其中组件的 id，只输出变化的部分；\n"
            "不要重新输出整张卡。\n"
            + prior_jsonl.strip()
        )
    return join_system(parts)


def derive_theme(text: str) -> str | None:
    t = text.lower()
    for theme, kws in THEME_KEYWORDS.items():
        if any(k.lower() in t for k in kws):
            return theme
    return None


def ollama_generate(model: str, system: str, user: str, timeout: int = 300) -> str:
    body = json.dumps(
        {
            "model": model,
            "prompt": user,
            "system": system,
            "stream": False,
            "options": {"temperature": 0.2},
        }
    ).encode("utf-8")
    req = urllib.request.Request(OLLAMA_GEN, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return data.get("response") or ""


# ---------- 云端模型（路子 A）：OpenAI 兼容 /chat/completions ----------
# 密钥绝不进命令行参数/历史，只从 .env 或环境变量读取；.env 已被 gitignore。

def load_env() -> dict:
    env: dict = {}
    dot = TOOLS / ".env"
    if dot.is_file():
        for line in dot.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            env[k.strip()] = v.strip().strip('"').strip("'")
    return {**os.environ, **env}


def generate_cloud(system: str, user: str, *, url: str, key: str, model: str,
                   timeout: int = 300) -> str:
    """调 OpenAI 兼容接口（messages=system+user）。url 形如 https://x/v1（不含末尾 /chat/completions）。"""
    base = url.rstrip("/")
    endpoint = base + "/chat/completions" if base.endswith("/v1") else base
    body = json.dumps(
        {
            "model": model,
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            "temperature": 0.2,
            "stream": False,
        }
    ).encode("utf-8")
    req = urllib.request.Request(endpoint, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Authorization", f"Bearer {key}")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return data["choices"][0]["message"]["content"] or ""


def send_http(payload: str, prompt: str, verbose: bool = False) -> None:
    if verbose:
        print(f"\n  === 推送到 Unity 的完整 JSONL ({len(payload)} 字符) ===")
        print(payload)
        print("  === JSONL 结束 ===\n")
    else:
        print(f"  → 推送 {len([l for l in payload.splitlines() if l.strip()])} 行 JSONL 到 Unity")
    body = payload.encode("utf-8")
    req = urllib.request.Request(HTTP_A2UI, data=body, method="POST")
    req.add_header("Content-Type", "application/jsonl; charset=utf-8")
    if prompt:
        # UTF-8 → percent-encode 放进 header，C# 端 HttpUtility.UrlDecode 还原中文
        req.add_header("X-A2UI-Prompt", quote(prompt, safe=""))
    with urllib.request.urlopen(req, timeout=5) as resp:
        print("  Unity:", resp.read().decode("utf-8").strip())


def send_theme(theme: str) -> None:
    body = json.dumps({"theme": theme}).encode("utf-8")
    req = urllib.request.Request(HTTP_THEME, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            print("  主题热切:", resp.read().decode("utf-8").strip())
    except Exception as e:
        print("  主题推送失败:", e)


def push_target(target: str, jsonl_path: pathlib.Path, prompt: str, verbose: bool) -> None:
    if target == "editor":
        send_http(jsonl_path.read_text(encoding="utf-8"), prompt, verbose)
        return
    if target == "bench":
        script = TOOLS / "push_a2ui_bench.py"
        subprocess.check_call(
            [sys.executable, str(script), "--jsonl-file", str(jsonl_path), "--prompt", prompt]
        )
        return
    raise SystemExit(f"unknown target: {target}")


def generate_with_retry(model: str, system: str, user: str, retries: int, verbose: bool,
                         *, cloud: bool = False, cloud_url: str = "", cloud_key: str = "",
                         cloud_model: str = "") -> str:
    last_err = ""
    prompt = user
    for i in range(retries + 1):
        if verbose:
            print(f"  System prompt: {system.count(chr(10)) + 1} 行")
        print(f"  · 调用模型 ({i+1}/{retries+1}) …", end="", flush=True)
        if cloud:
            raw = generate_cloud(system, prompt, url=cloud_url, key=cloud_key, model=cloud_model)
        else:
            raw = ollama_generate(model, system, prompt)
        print(f" 返回 {len(raw)} 字符")
        if verbose:
            print(raw)
        jsonl = extract_jsonl(raw)
        try:
            validate_jsonl(jsonl)
            print(f"  · 校验通过（{len([l for l in jsonl.splitlines() if l.strip()])} 行）")
            return jsonl
        except Exception as e:
            last_err = str(e)
            print(f"  · 校验未过: {last_err[:160]}")
            prompt = (
                user
                + "\n\nPrevious output failed validation: "
                + last_err
                + "\nFix and output ONLY valid A2UI v0.8 JSONL."
            )
    raise RuntimeError("model failed validation: " + last_err)


def run_repl(target: str, model: str, retries: int, verbose: bool, use_smoke: bool, dry_run: bool,
             *, cloud: bool = False, cloud_url: str = "", cloud_key: str = "", cloud_model: str = "") -> int:
    mdl = "qwen2.5:1.5b" if use_smoke else model
    history: list[tuple[str, str]] = []
    print("\n=== A2UI 生成式 UI · 交互模式 ===")
    print("直接说话即可生成 / 修改面板；输入 exit / quit / q 退出。")
    print("示例：")
    print('  我需要给我的宠物鹦鹉打造一个宠物模式的 UI，因为我要离开车了')
    print("  倒计时改成 15 分钟")
    print("  放点轻音乐给鹦鹉听")
    print("  换个粉色皮肤\n")
    while True:
        try:
            user = input("你> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\n再见。")
            break
        if user.lower() in ("exit", "quit", "q"):
            print("再见。")
            break
        if not user:
            continue

        prior = history[-1][1] if history else None
        system = build_system(prior)
        try:
            jsonl = generate_with_retry(
                mdl, system, user, retries, verbose,
                cloud=cloud, cloud_url=cloud_url, cloud_key=cloud_key, cloud_model=cloud_model,
            )
        except Exception as e:
            print("模型生成失败:", e)
            print("提示: ollama create a2ui-cabin -f Tools/a2ui_ollama/Modelfile")
            continue

        OUT.parent.mkdir(parents=True, exist_ok=True)
        OUT.write_text(f"# prompt: {user}\n" + jsonl, encoding="utf-8")

        if dry_run:
            print("dry-run ok · lines=", len([l for l in jsonl.splitlines() if l.strip()]))
        else:
            push_target(target, OUT, user, verbose)
            theme = derive_theme(user)
            if theme:
                send_theme(theme)
        history.append((user, jsonl))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Ollama → A2UI → Editor/bench")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--prompt", default="", help="单条用户话；留空进入交互模式")
    ap.add_argument("--target", choices=("editor", "bench"), default="editor")
    ap.add_argument("--dry-run", action="store_true", help="校验/写文件，不推送")
    ap.add_argument("--retries", type=int, default=2)
    ap.add_argument("--fallback", choices=sorted(FALLBACKS.keys()), help="跳过模型推金标")
    ap.add_argument("--base-model-smoke", action="store_true", help="用 qwen2.5:1.5b")
    ap.add_argument("--verbose", action="store_true", help="打印模型原文与完整 JSONL")
    ap.add_argument("--cloud", action="store_true",
                    help="云端模型模式（OpenAI 兼容接口）。url/key 从 .env 或环境变量读取，只指定 --cloud-model 覆盖模型名")
    ap.add_argument("--cloud-model", default="", help="云端模型名（覆盖 .env 的 A2UI_CLOUD_MODEL）")
    args = ap.parse_args()

    # 解析云端配置（密钥只从 .env / 环境变量，绝不进命令行参数）
    cloud_url = cloud_key = cloud_model = ""
    if args.cloud:
        env = load_env()
        cloud_url = env.get("A2UI_CLOUD_URL", "")
        cloud_key = env.get("A2UI_CLOUD_KEY", "")
        cloud_model = args.cloud_model or env.get("A2UI_CLOUD_MODEL", "")
        if not (cloud_url and cloud_key and cloud_model):
            print("云端模式缺少配置：请在 Tools/.env 设置 "
                  "A2UI_CLOUD_URL / A2UI_CLOUD_KEY / A2UI_CLOUD_MODEL，或用环境变量")
            return 4

    if args.fallback:
        path = FALLBACKS[args.fallback]
        text = load_text(path)
        prompt = args.prompt or extract_prompt(text) or args.fallback
        jsonl = strip_meta(text)
        validate_jsonl(jsonl)
        OUT.parent.mkdir(parents=True, exist_ok=True)
        OUT.write_text((f"# prompt: {prompt}\n" if prompt else "") + jsonl, encoding="utf-8")
        print("wrote", OUT)
        if args.dry_run:
            print("dry-run ok")
            return 0
        push_target(args.target, OUT, prompt, args.verbose)
        theme = derive_theme(prompt)
        if theme:
            send_theme(theme)
        return 0

    if not args.prompt:
        return run_repl(
            args.target, args.model, args.retries, args.verbose, args.base_model_smoke, args.dry_run,
            cloud=args.cloud, cloud_url=cloud_url, cloud_key=cloud_key, cloud_model=cloud_model,
        )

    # 单条模式
    system = build_system(None)
    model = "qwen2.5:1.5b" if args.base_model_smoke else args.model
    try:
        jsonl = generate_with_retry(
            model, system, args.prompt, args.retries, args.verbose,
            cloud=args.cloud, cloud_url=cloud_url, cloud_key=cloud_key, cloud_model=cloud_model,
        )
    except Exception as e:
        print("Ollama generate failed:", e)
        print("Hint: ollama create a2ui-cabin -f Tools/a2ui_ollama/Modelfile")
        return 2
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(f"# prompt: {args.prompt}\n" + jsonl, encoding="utf-8")
    print("wrote", OUT)
    if args.dry_run:
        print("dry-run ok · lines=", len([l for l in jsonl.splitlines() if l.strip()]))
        return 0
    try:
        push_target(args.target, OUT, args.prompt, args.verbose)
    except Exception as e:
        print("push failed:", e)
        return 3
    theme = derive_theme(args.prompt)
    if theme:
        send_theme(theme)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
