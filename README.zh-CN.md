# A2UI Unity Toolkit

**让 Agent（LLM）用一份 JSONL 描述界面，Unity / Tuanjie 运行时直接渲染成原生 UI Toolkit 组件。不经 HTML、不经 WebView、不做像素流。**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Engine](https://img.shields.io/badge/Tuanjie-2022.3.62t13-5C2D91)
![Protocol](https://img.shields.io/badge/A2UI-v0.8%20%2B%20v0.9-4285F4)
[![a2ui.org](https://img.shields.io/badge/protocol-a2ui.org-8AB4F8)](https://a2ui.org)

A2UI 是 Google 开源的开放协议（Apache 2.0，官网 [a2ui.org](https://a2ui.org)。官方渲染器覆盖 Angular、Flutter/GenUI、Lit，社区还有 Compose 渲染器 `lmee/A2UI-Android`）。Agent 逐行输出 JSONL 消息描述界面，本工程是这个协议的 Unity UI Toolkit 运行时。把 Agent（云端或本地 Ollama）接到你的应用上，它输出的就是原生、可换肤的 UI Toolkit 组件。面向车机座舱、游戏，以及一切由 AI 驱动的 Unity 前端。英文版见 [README.md](README.md)。

![demo](screenshots/demo_v09_full_control_scroll.mp4)

| M3 Light | M3 Dark |
|---|---|
| ![M3 Light](screenshots/theme_m3light.png) | ![M3 Dark](screenshots/theme_m3dark.png) |

```
Agent 输出 JSONL ──HTTP/TCP──▶ Host 热推 ──▶ 校验 G0 ──▶ Processor 状态机 ──▶ CatalogMapper ──▶ UI Toolkit 渲染
   createSurface /            (127.0.0.1:18766)   │          (surface 生命周期)   (协议→UITK)     (USS 主题换肤)
   updateComponents /                             └─ 坏包拒收保留上一帧
   updateDataModel /
   deleteSurface
```

## 它解决什么问题

GenUI（Agent 生成界面）在座舱场景有三个痛点，这个运行时逐个处理。

1. **协议边界。** Agent 只产出 A2UI JSONL（每行一个消息、组件白名单），不产出样式不产出代码。结构由协议约束，注入被 G0 拒收。
2. **渲染归一。** 同一份 JSONL 在多套 USS 主题（DS 设计系统、M3 Light 与 Dark、Figma 导出皮肤）下热切换，结构与皮肤正交。
3. **可回归。** 全部主题 × 全部样例（500+ 组合）的布局断言加截图像素 diff，一条命令验证没有改坏任何角落。

**协议支持 v0.8 与 v0.9 双栈。** 引擎按行自动识别格式。旧 v0.8（`surfaceUpdate`/`beginRendering`、组件嵌套）与现行 v0.9（`createSurface`/`updateComponents`、组件平铺、text 直接值）都能渲染；`A2uiV09Normalizer` 把 v0.9 归一化进内部模型，Mapper 无版本感知。样例库双格式并存（`*.v0.8.jsonl` / `*.v0.9.jsonl`），转换脚本在 `Tools/v08_to_v09.py`。

## 快速开始

要求 **Tuanjie 2022.3.62t13**（实测版本；Unity 2022.3 兼容 fork，标准 Unity 未验证），工具链需要 **Python 3.10+**。

```bash
git clone https://github.com/SamXiaBing/a2ui-unity-toolkit.git
# 用 Tuanjie 打开工程（clone 即测）
```

打开 `Assets/Scenes/A2UITestBed.unity`，按 Play，然后三个入口任选。

**方式 A，编辑器测试面板。** 菜单 `A2UI Scheme A → 测试发送面板`，选样例点「发送」（按目录分组折叠、带 v0.8/v0.9 徽标），主题下拉热切换。

**方式 B，脚本热推**（模拟 Agent 推流）。

```bash
python Tools/push_a2ui_bench.py --jsonl-file Assets/A2UISchemeA/Samples/demos/full_control_center.v0.9.jsonl --prompt "全控件演示"
```

**方式 C，一键回归。**

```bash
python Tools/run_regression.py --editor "C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t13/Editor/Tuanjie.exe"
# 无独显环境用 --only-geometry 跳过截图；--update-baselines 刷新基准
```

回归覆盖全部主题 × 全部样例（v0.8/v0.9 全进矩阵）约 500+ 组合，逐组合做 worldBound 几何断言（文字不越出卡片），可选输出截图与 baselines 像素 diff（报告在 `TestResults/report.md`；baselines 需 GUI 编辑器跑 `A2UI_CAPTURE=1` 采集）。

## 仓库结构

```
Assets/A2UISchemeA/
├── Runtime/    协议处理器（v0.8/v0.9 双栈）、CatalogMapper、双宿主、主题注册表
├── Editor/     测试发送面板、USS 编辑器、测试床生成器
├── Styles/     USS：DS 设计系统（15 份）、M3 Tokens、FigmaExport 皮肤
├── Samples/    56 份样例（双格式）：demos/scenarios/features/components/edge/timeline_bench
├── Tests/      全矩阵布局回归 + Mapper/协议单元测试
├── Scenes/     验证场景
├── Design/     a2ui-design-spec.html（token 表 + 组件规范）、Figma 组件模板插件脚本
└── Resources/  MiSans 字体（自带）+ 图标集（120+ SVG）
Tools/          Python 工具链：热推、回归、diff、Figma 导入、v0.8→v0.9 转换器
docs/           引擎兼容矩阵（改 USS/Mapper 前必读）、Figma 管线状态
screenshots/    演示录制
```

## 主题系统

- 内置 **DS**（设计系统，默认）、**M3 Light**、**M3 Dark**，外加自动发现的 **Figma Export**
- **零代码扩展。** 在 `Styles/` 任意子目录放一份 `FigmaTokens.uss`（可选加 `FigmaComponents.uss`），注册表自动发现为新主题，面板与下拉同步长出条目
- 主题是 USS 作用域类加 C# 内联兜底色（引擎后代选择器偶发不生效，见兼容矩阵）
- DS 主题与 120 个图标源自 [sinanata/unity-ui-toolkit-design-system](https://github.com/sinanata/unity-ui-toolkit-design-system)（MIT），署名见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- 装饰主题（ice/beach/pink/green/aaos/cloud）已裁剪，HTTP `/theme` 传入旧键自动回落 `ds`

## Figma → USS 管线

Figma 设计稿到可热切主题的完整链路已实现（插件 → 导出 → token/组件 USS → 主题发现）。

1. **模板。** `Design/a2ui-design-spec.html` 是 token 表与组件规范；`Tools/figma_plugin/` 在 Figma 里生成 A2UI 组件模板
2. **导出。** `Tools/figma_api_export.py` 走 Figma REST API（样例板在 `Tools/figma_samples/`）
3. **转换。** `Tools/figma_pull_tokens.py` 产出 `FigmaTokens.uss`；`Tools/figma_to_uss.py` 产出 `FigmaComponents.uss`
4. **消费。** `Assets/A2UISchemeA/Styles/FigmaExport/` 被自动发现为可热切主题；视觉测试在 `A2uiFigmaExportVisualTest`

当前还原度（诚实自评见 [docs/figma_pipeline_status.md](docs/figma_pipeline_status.md)）。L1 色彩 ✅，L2 字号/间距 ⚠️，L3 布局约束 ❌（组件 USS 值为生成器硬编码，尚未解析 Figma Auto Layout）。

## 测试策略

| 层 | 测试 | 固化的历史事故 |
|------|--------------|------|
| Mapper | `A2uiMapperUnitTests`（7 用例） | Column wrap 假并列、Tab 均分撑破、`:last-child` 缺失、detached 动画崩溃、按钮文案来源、绑定优先级、List 末项 |
| 协议 | `A2uiValidatorProcessorTests` | 坏包拒收、surface 生命周期、path patch、数组下标 |
| 布局 | `A2uiLayoutRegressionTest` 全矩阵 | 任何主题 × 任何样例的文字越界 + Image 塌陷 |
| 视觉 | `Tools/regression_diff.py` 截图 diff | USS 改动导致的观感回归（0.5% 像素阈值） |

## 健壮性与安全边界

Agent 生成的 JSONL 是不可信输入。渲染器按浏览器处理网页的标准做防御，安全基线与 A2UI Compose 参考渲染器一致。

| 防御 | 上限 / 策略 | 行为 |
|------|------------|------|
| 渲染深度 | `MAX_RENDER_DEPTH = 50` | 超深嵌套渲染占位符，不栈溢出 |
| 组件 ID 校验 | 协议 ID 规则 | 非法 ID 跳过该组件，不影响其余 |
| 坏包 G0 拒收 | 结构校验（消息类型/必填字段） | 拒收并保留上一帧，不白屏 |
| 未知组件降级 | `A2uiDegrade.UnknownTypeFallback` | 占位卡片，不崩、不吞整帧 |
| 缺失子组件 | `Placeholder(missing:id)` | 局部占位，兄弟节点正常渲染 |
| URL scheme 白名单 | Image/Video/AudioPlayer 仅 `http(s)`，本地走 `resources://` | 阻断 `file://` 与自定义 scheme 注入 |
| JSON 解析容错 | 单行解析失败定位到行号 | 报错行号，不静默吞 |
| 样式解析隔离 | 残缺 USS 条目不进运行时 | 引擎样式遍历永不越界 |

> 布局合同。宿主定宽（640px 标准卡，max-width 96%），组件用 `flex-shrink:1` 填满，与 Compose 参考渲染器的 `fillMaxWidth()` 约定一致。超出视口的内容在卡内 ScrollView 滚动。固定组件尺寸（图片高度等）与参考 dp 值逐项对齐。

## Roadmap

剩余增强项参照 A2UI Compose 参考渲染器。

- [ ] 长列表虚拟化（对应 LazyColumn/LazyRow；当前 ScrollView 全量渲染，车机卡片规模下够用）
- [ ] TextField 双向 DataModel 绑定（当前经 action 回写通道）
- [ ] 消息大小上限（参考实现 1MB）与单 surface 组件数上限（参考 1000）
- [ ] 标准组件尺寸与参考渲染器的自动化一致性测试
- [ ] 截图基准全量采集（需 GUI 编辑器跑 Test Runner，`A2UI_CAPTURE=1`）

## 重要文档

- **[Tuanjie UITK 兼容矩阵](docs/engine-compat-tuanjie.md)**。transform 崩溃、flex-shrink 默认 0、var() fallback 被丢弃、Column wrap 陷阱等 30+ 实测坑位。改 USS/Mapper 前必读
- [Figma 管线状态](docs/figma_pipeline_status.md) · [协议 v0.8→v0.9 升级评估](docs/protocol_upgrade_v0_9.md) · [MiSans 字体许可](docs/FONT-LICENSE-MiSans.md)
- 英文版 [README.md](README.md)，子目录说明见 [Assets/A2UISchemeA/README.md](Assets/A2UISchemeA/README.md)

## License

MIT（见 [LICENSE](LICENSE)）。第三方资产（MiSans 字体、DS 主题与图标集）按其自身许可再分发，署名见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
