# Figma → Unity 还原管线状态

## 你的原始目标

在 Figma 里按 A2UI 规范画基本组件 → 导出 → Unity 完美还原风格/属性/约束。

## 当前管线（已存在的代码路径）

```
Figma 设计稿
    │
    ├─ figma_pull_tokens.py     从 Figma Variables API 抽取颜色/字号/间距 token
    │   └─ 产出：Styles/FigmaExport/FigmaTokens.uss
    │
    ├─ figma_to_uss.py          从设计稿节点数据生成组件 USS
    │   └─ 产出：Styles/FigmaExport/FigmaComponents.uss
    │
    ├─ figma_api_export.py      直连 Figma REST API（--discover 找节点 / --no-convert 只拉 JSON）
    │
    └─ Unity 渲染：FigmaTokens/FigmaComponents 放进 Styles/<目录> 即被 A2uiThemeRegistry
        自动发现为可热切主题（figma-<目录名>）
```

## 提取模式：两层互补（2026-08-31 起）

`figma_to_uss.py` 按节点命名自动选择提取模式，同一份代码兼容两类输入：

| 模式 | 触发条件 | 行为 |
|------|----------|------|
| **名字规范驱动** | 节点按规范命名 `别名 / 组件[:variant]`（如 `big / Text:h1`、`playBtn / Button:primary`、`root / Card`） | **确定性提取**：语义色取自命名实例的 fill、字号取 `Text:<variant>` 的 fontSize、几何（padding/圆角/派生高度/gap）取实例 Auto Layout 属性。换任何一张按规范命名的稿子，数值自动跟随 |
| **启发式兜底** | 无规范命名的老稿 | 频率/亮度/饱和度猜语义色 + 字号排序分配 + 模板几何值（历史行为，输出与旧版转换器逐字节一致） |

命名规范见 `Design/a2ui-design-spec.html`；Figma 侧生成规范命名的组件模板用 `Tools/figma_plugin/`。

## 三个层级完成度（2026-08-31 复核）

| 层级 | 含义 | 状态 |
|------|------|------|
| **L1 色彩还原** | primary/surface/error 等语义色与 Figma 一致 | ✅ 名字驱动后确定性成立（此前启发式在浅色稿上会明暗反转，已修） |
| **L2 字号/间距还原** | 字号梯度与间距网格与设计稿一致 | ✅ 数值验证通过：M3 标准模板逐项对照 44/36/30/24/20/15 全部 1:1（此前靠排序猜，变体缺失即错位） |
| **L3 布局约束还原** | Auto Layout 约束（padding/gap/圆角/高度）→ USS | ✅（规范稿）解析实例属性：按钮 48px+pill、Card 16px/24px、Chip pill/8·16 等全部来自节点真值；❌（老稿）仍为模板值，需按命名规范重画后重跑管线 |

> 注意：仓库里已提交的 `Styles/FigmaExport/` 主题来自 CarStore 橙稿（`#FF5C00`，启发式路径产物），保持不动；
> 新皮肤按「规范命名画稿 → `figma_api_export.py` 拉取 → `figma_to_uss.py` 转换 → 放入 `Styles/<新目录>`」流程生成。

## 验证记录（2026-08-31）

- **对象**：M3 标准模板（Figma 文件 key `bl985lo1stxpBBV94SQlY6`，画布 `16:73` "A2UI Components"，7 组件区齐全）
- **方法**：`figma_api_export.py --no-convert` 拉节点 JSON → 提取真值 → 新转换器输出 → 逐项对照
- **结果**：primary `#6750A4`、surface `#FFFBFE`、onSurface `#1C1B1F`、onSurfaceVariant `#49454F`、outline `#79747E`、outlineVariant(Divider) `#CAC4D0`、error `#B3261E`、surfaceVariant(Chip未选) `#E7E0EC`、onPrimary(主按钮内文字) `#FFFFFF`、字号全梯度、按钮 `min-height:48 + padding:12/24 + radius:999`、TextField `12/16 + r8`、Chip `8/16 + pill`、Card `r16 + pad24`、MediaMiniBar→cabin `r16 + pad24` —— 全部与设计稿一致
- **老稿回归**：cabin_board / carstore_home（无规范命名）走启发式路径，Tokens 输出与旧版转换器逐字节一致；Components 唯一差异是移除了「无 Logo 稿也硬塞 76px 圆形卡」的泄漏规则（该规则现在只在稿内真有圆形主色块时生成）
- **自检**：`figma_to_uss.py --selfcheck-radius` ALL OK

复跑验证：

```powershell
$env:FIGMA_TOKEN = (Get-Content D:\AIWorkSpace\.figmakey -Raw).Trim()
python Tools/figma_api_export.py --file-key bl985lo1stxpBBV94SQlY6 --node-id "16:73" --depth 8 --no-convert --out Temp/figma_l2_check.json
python Tools/figma_to_uss.py --input Temp/figma_l2_check.json --outdir Temp/figma_l2_uss --scope a2ui-skin--figma-export
```

## 已知边界（诚实记录）

- 未覆盖的约束类型：`layoutGrow`/`alignSelf` 复杂对齐、约束参考线——当前映射 Row/Column 的 gap、padding、圆角、派生高度
- `figma_to_uss_direct.py`（直连 REST 的旧雏形）保留但不再是主路径

## 跨渲染器视觉校准（2026-08-31 建成）

工具：`Tools/figma_visual_diff.py`——Figma images API 渲染的组件 PNG vs Unity Game View 截图，按设计空间几何比（ratio≈1.0 即还原）+ 主色差度量，产出 `TestResults/figma_calib/diff/pairs/` 并排图。**不做逐像素 diff**（Roboto vs MiSans 字体噪声 dominated）；截图走「删除→CaptureScreenshot→轮询尺寸稳定→哑截图屏障」配方（桥接双执行会串帧，详见兼容矩阵）。

**12 组件校准结果（M3 标准模板）：9 OK / 3 CHECK，3 个 CHECK 均有 resolvedStyle 权威数据背书为伪影或语义差：**

| 组件 | ratio | 判定 | 说明 |
|------|-------|------|------|
| h1/h2/h3/h4/body/caption | 1.10~1.33 | ✅ | 字号 44/36/30/24/20/15 全部 resolved 正确 |
| btnP | 0.922, inkΔ=0 | ✅ | 填充色逐位一致；pill 圆角=高度一半（999px 会被 Tuanjie 拉成椭圆，转换器已钳制） |
| btnS | 0.922, inkΔ=144 | ⚠️ 伪影 | resolved bg=#E8DEF8 与 Figma 逐位一致；inkΔ 来自淡色填充下文字 AA 环（1x MiSans vs 2x Roboto 渲染重量差） |
| caption | 1.334, inkΔ=80 | ⚠️ 伪影 | USS 色 #49454F 精确；15px 小字 AA 灰阶主导了采样 |
| card / field / div | 0.82~0.91 | ✅ | 圆角/内边距/分割线全部来自实例真值 |
| slider | — | ⚠️ 语义差 | Figma 只有轨道，Unity 含拖柄；且 Tuanjie 的 slider tracker/dragger 选择器未命中主题（已知缺口，拖柄保持默认灰） |

校准过程中修复的三个真缺陷（都已固化在转换器/宿主里）：
1. **pill 圆角拉成椭圆**：Tuanjie border-radius 横纵独立钳制 → 转换器把 999px 钳到 `height/2`
2. **h2/h4 字号回退**：生成器只输出 h1/h3/caption 选择器 → 现在输出全部 8 个变体（Mapper 类名是 `a2ui-text--<hint>` 动态拼接）
3. **横向滚动条露出**：ScrollView chrome + mode=Vertical 不隐藏横向 Scroller → `ScrollerVisibility.Hidden` + USS chrome 复位
