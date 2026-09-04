"""
在 Ollama 输入框里敲提示词 → Unity A2UISchemeA 场景显示半透卡片。
主题根据提示词关键词自动选择（冰/海滩/粉色）。

用法:
    python ollama_overlay.py "给我一个沙滩风格的音乐面板"
    python ollama_overlay.py "做一个可爱风的空调面板"
    python ollama_overlay.py --model a2ui-cabin "冰雪主题的休憩界面"
"""
from __future__ import annotations

import subprocess, sys, pathlib

TOOLS = pathlib.Path(__file__).resolve().parent
CHAT = TOOLS / "ollama_a2ui_chat.py"


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    model = "a2ui-cabin"
    prompt_parts = []
    i = 1
    while i < len(sys.argv):
        if sys.argv[i] == "--model" and i + 1 < len(sys.argv):
            model = sys.argv[i + 1]
            i += 2
        else:
            prompt_parts.append(sys.argv[i])
            i += 1

    prompt = " ".join(prompt_parts)

    print(f" Ollama 模型: {model}")
    print(f" 提示词: {prompt}")
    print()
    print(" 正在生成 JSONL...")

    result = subprocess.run(
        ["python", str(CHAT), "--model", model, "--prompt", prompt, "--target", "editor"],
        capture_output=False,
    )

    if result.returncode == 0:
        print()
        print(" 已推送到 Unity · 检查 A2UISchemeA 场景的 Display 1 覆盖层")


if __name__ == "__main__":
    main()
