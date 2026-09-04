"""
诊断脚本：直接推一段已知正确的 JSONL 到 Unity Editor。
如果这段也没反应，说明问题在 Unity 端（命令服务器/校验/渲染）。
如果这段有反应，说明问题在 Ollama 生成的 JSONL 内容。

用法: python diag_push.py
"""
import urllib.request
import json
from urllib.parse import quote

HTTP_A2UI = "http://127.0.0.1:18766/a2ui"

# 一段已知合法的 JSONL（组件用 component 包裹格式，不是 type 格式）
PAYLOAD = """{"surfaceUpdate":{"surfaceId":"diag","components":[{"id":"card","component":{"Card":{"children":["title","body","btn"]}}},{"id":"title","component":{"Text":{"literal":"QQ 诊断卡片","usageHint":"h3"}}},{"id":"body","component":{"Text":{"literal":"HTTP 链路正常，不经过 Ollama。"}}},{"id":"btn","component":{"Button":{"action":"click","literal":"点我试试"},"children":[]}}]}}
{"dataModelUpdate":{"surfaceId":"diag","contents":[]}}
{"beginRendering":{"surfaceId":"diag","root":"card"}}
"""

prompt = quote("诊断脚本：直接推送测试卡片", safe="")

body = PAYLOAD.encode("utf-8")
req = urllib.request.Request(HTTP_A2UI, data=body, method="POST")
req.add_header("Content-Type", "application/jsonl; charset=utf-8")
req.add_header("X-A2UI-Prompt", prompt)

try:
    with urllib.request.urlopen(req, timeout=5) as resp:
        result = resp.read().decode("utf-8")
        print(f"HTTP {resp.status} · 响应: {result}")
        r = json.loads(result)
        if r.get("ok"):
            print("✅ 诊断推送成功！请看 Unity Editor 的 Console 日志。")
        else:
            print("⚠️ Unity 返回 ok=false:", r)
except ConnectionRefusedError:
    print("❌ 连接被拒绝！请先: Unity Editor → 打开 A2UISchemeA 场景 → 点 Play")
except urllib.error.URLError as e:
    print(f"❌ 连接失败: {e}")
except Exception as e:
    print(f"❌ 其他错误: {e}")
