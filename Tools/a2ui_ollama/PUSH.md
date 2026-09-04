# 推送到 Editor / 台架

生成合法 JSONL 后，由本机脚本推送。模型**不要**自己发明端口协议。

## Editor（默认联调）

Unity Play，Launcher + A2UI 叠层，Game → Display 2，监听 `127.0.0.1:18766`。

```powershell
python Tools/send_a2ui.py --jsonl-file Temp/A2UISchemeA/ollama_out.jsonl --prompt "<用户原话>"
```

或：

```powershell
python Tools/ollama_a2ui_chat.py --target editor --prompt "<用户原话>"
```

**禁止**此时使用 `adb forward`（会把端口拐到设备）。

## 台架 Runtime

仅当明确台架/adb：

```powershell
adb forward tcp:18766 tcp:18766
python Tools/push_a2ui_bench.py --jsonl-file Temp/A2UISchemeA/ollama_out.jsonl --prompt "<用户原话>"
```

或：`ollama_a2ui_chat.py --target bench --prompt "..."`

## 主题（可选）

协议无颜色字段。需要海滩/粉色时由推送加 `--theme beach|pink`（`push_a2ui_bench.py`）。
