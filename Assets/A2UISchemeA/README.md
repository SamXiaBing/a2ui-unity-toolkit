# A2UI Scheme A · 验证床（A2UITestBed）

Unity / Tuanjie UI Toolkit 直连 **A2UI v0.8 + v0.9 双栈**协议（方案 A：单宿主全功能）。
主入口与架构说明见仓库根 [README](../../README.md) / [README.zh-CN](../../README.zh-CN.md)。

## 目录

```
Runtime/    协议处理器（V08Processor/V08Validator/V09Normalizer）、CatalogMapper、
            双宿主（SchemeAHost/LauncherSurfaceHost）、主题注册表、动作路由/门禁/会话录制
Editor/     测试发送面板（A2uiTestPusherWindow）、USS 编辑器、原子组件工作台
Styles/     DS 设计系统（15 份 USS）、M3 Tokens（Crafted/Tokens/Motion）、FigmaExport 皮肤
Samples/    56 份样例（v0.8/v0.9 双格式）：demos/scenarios/features/components/edge/timeline_bench
Tests/      全矩阵布局回归 + Mapper/协议单元测试
Scenes/     A2UISchemeA / A2UILauncherHost / A2UIAtomicWorkbench
Design/     a2ui-design-spec.html（token 与组件规范）、Figma 组件模板插件脚本
Resources/  MiSans 字体 + 图标集 + OverlayConfig
```

## 运行

1. 打开 `Assets/Scenes/A2UITestBed.unity` → Play；或菜单 `A2UI Scheme A → 测试发送面板`（可不依赖 Play 发送到 HTTP/inbox）
2. **Launcher 3D + JSONL 叠层**：`A2UI Scheme A / 打开 Launcher + A2UI 叠层` → Play
3. **脚本热推**（模拟 Agent 推流，工具链在仓库根 `Tools/`）：

```powershell
python Tools/push_a2ui_bench.py --jsonl-file Assets/A2UISchemeA/Samples/demos/full_control_center.v0.9.jsonl --prompt "全控件演示"
python Tools/send_a2ui.py --sample climate
python Tools/ollama_overlay.py "给我一个音乐面板"   # PC 本地 Ollama 演示
```

## 主题

主题由 `A2uiThemeRegistry` 统一管理，内置 4 套 + 自动发现：

| 键 | 名称 | 来源 |
|----|------|------|
| `ds` | DS 设计系统（默认） | Styles/DS/ |
| `a` | M3 Light | Styles/Tokens.uss |
| `dark` | M3 Dark | Styles/Tokens.uss |
| `figma-<目录>` | Figma 导出皮肤 | Styles/ 任意子目录含 `FigmaTokens.uss` 即被自动发现 |

HTTP `/theme` 传入已裁剪的装饰主题（ice/beach/pink/green/aaos/cloud）会回落 `ds`；
新增一套 USS 皮肤目录，测试面板与场景下拉会自动长出对应条目，无需改代码。

## 协议双栈

- **v0.8**：`surfaceUpdate` / `beginRendering` / `dataModelUpdate` / `deleteSurface`（组件嵌套结构）
- **v0.9**：`createSurface` / `updateComponents` / `updateDataModel` / `deleteSurface`（组件平铺、children 数组、text 直接值、justify/align、variant），每条消息带 `"version": "v0.9"`
- 引擎按消息 key 自动识别版本，`A2uiV09Normalizer` 把 v0.9 归一化为内部模型，Mapper 无版本感知
- v0.9 样例：`Samples/**/*.v0.9.jsonl`；转换脚本 `Tools/v08_to_v09.py`（`--all` 可全量转换）

## 座舱 Catalog 扩展类型

`MediaMiniBar` `ClimateStep` `RestBanner`（客户端实现，非上游 Standard 伪装）。

## 测试

见根 README「测试策略」。布局回归自动扫描 `Samples/**/*.jsonl`（v0.8 与 v0.9 全部进矩阵）。
