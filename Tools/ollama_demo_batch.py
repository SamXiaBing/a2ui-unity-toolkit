"""
一次跑 3 个场景，输出 JSONL 并自动推到 Unity Editor。
用法:
    python ollama_demo_batch.py --target editor
    python ollama_demo_batch.py --target file          # 只存文件不推
"""
import subprocess, argparse, time
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
CHAT_SCRIPT = TOOL_DIR / "ollama_a2ui_chat.py"

SCENARIOS = [
    {
        "prompt": "给我一个音乐播放面板，歌名叫夜航星图，歌手是银河快线，有封面图、播放暂停按钮、上一首下一首",
        "name": "scenario1_music",
    },
    {
        "prompt": "车里面有点热，做一个空调控制面板，显示当前温度24度，有降温升温按钮和一个温度滑块",
        "name": "scenario2_climate",
    },
    {
        "prompt": "做一个休憩模式的提示界面，大字写休憩模式，小字提示已弱化多媒体与次要入口，下面放一个开启勿扰20分钟的按钮",
        "name": "scenario3_rest",
    },
]


def main():
    parser = argparse.ArgumentParser(description="批量 Ollama → A2UI JSONL")
    parser.add_argument("--target", default="editor", choices=["editor", "file"],
                        help="editor=推到Unity, file=只存文件")
    parser.add_argument("--model", default="a2ui-cabin",
                        help="Ollama 模型名（默认 a2ui-cabin）")
    parser.add_argument("--delay", type=float, default=2.0,
                        help="场景间隔秒数（默认 2s）")
    args = parser.parse_args()

    output_dir = TOOL_DIR.parent / "Samples" / "_ollama_batch"
    output_dir.mkdir(parents=True, exist_ok=True)

    for i, s in enumerate(SCENARIOS):
        print(f"\n{'='*60}")
        print(f"  [{i+1}/{len(SCENARIOS)}] Prompt: {s['prompt']}")
        print(f"{'='*60}")

        out_file = output_dir / f"{s['name']}.jsonl"

        cmd = [
            "python", str(CHAT_SCRIPT),
            "--prompt", s["prompt"],
            "--model", args.model,
            "--target", args.target,
            "--out-file", str(out_file),
        ]

        result = subprocess.run(cmd, capture_output=False)
        if result.returncode != 0:
            print(f"  !! 场景 {s['name']} 失败了 (exit={result.returncode})")

        if i < len(SCENARIOS) - 1:
            print(f"\n  — 等 {args.delay}s 再跑下一个场景 —")
            time.sleep(args.delay)

    print(f"\n{'='*60}")
    print(f"  全部 {len(SCENARIOS)} 个场景跑完了")
    print(f"  输出目录: {output_dir}")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
