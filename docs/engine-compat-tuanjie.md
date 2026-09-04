# Tuanjie 2022.3.55t4 (Unity 2022.3 fork) UI Toolkit 兼容矩阵

实测于 Tuanjie 1.5.3（`2022.3.55t4 (1cb69ecfd405)`，URP 14.1.0）。
每一条都是真实踩坑验证过的，**不是**从文档抄的。升级引擎版本后请重跑回归确认。

## USS 属性支持

| 特性 | 状态 | 现象 / 替代方案 |
|------|------|----------------|
| `transform: translate()/scale()/…` | ❌ 崩溃 | 控制台报 "Unknown property 'transform'"，解析器生成 0 值残缺样式条目；样式更新遍历到该条目时 `StylePropertyReader.GetValue → ApplyGlobalKeyword → ArgumentOutOfRangeException`。**替代：C# `ve.style.translate / scale`** |
| `transition-*` + `transform` 组合 | ❌ 崩溃 | 同上。`transition-duration: var(...)` 单独使用也会触发值越界，**时长必须写字面值** |
| `:last-child` 伪类 | ❌ | 解析告警 + 残缺条目。**替代：C# 给末位子元素挂 `.a2ui-last-child` 类** |
| `overflow-x` / `overflow-y` | ❌ | 不识别。**只用 `overflow`** |
| `box-shadow` | ❌ | 不支持。**替代：`border-bottom-width: 3px` + 深色 border 模拟卡片阴影** |
| CSS 自定义属性多阴影值（逗号分隔） | ❌ | `--elev-1: 0 1px 2px rgba(...), 0 1px 3px rgba(...)` 会导致解析异常。**删掉** |
| `gap` | ❌ | 不支持。**替代：直接子元素 margin**（`margin-right` / `margin-bottom`） |
| `var()` 常规取值 | ⚠️ **仅限无 fallback 形式** | `var(--x)` 正常；**`var(--x, 36px)` 带逗号 fallback 的声明被解析器静默丢弃**——不报错、该属性不生效（同规则内无 var 的属性仍生效，极具迷惑性）。曾导致 M3 主题全部字号回落引擎默认 14px（"文字模糊"）。**字号/间距用字面量；颜色 var() 异常时先查 fallback 逗号** |
| 后代选择器 `.a.b .c` | ⚠️ 偶发失效 | 本引擎下有时不生效（渲染时机相关）。**关键观感样式用 C# 内联兜底** |

## Flexbox / Yoga 差异

| 特性 | 状态 | 说明 |
|------|------|------|
| `flex-shrink` 默认值 | ⚠️ 与 CSS 相反 | **UITK 默认 0（CSS 默认 1）**——子元素永不收缩，长文本/输入框直接把卡片撑破。**所有容器子元素显式写 `min-width: 0; flex-shrink: 1`** |
| Column 容器 `flex-wrap: wrap` | ⚠️ 语义陷阱 | Column 的"换行"= 溢出子元素**横向堆到右侧**（假并列布局），不是预期的纵向换行。**Column 一律 NoWrap；只有 Row 允许 Wrap** |
| 绝对定位 `left+right` 双钉边 | ⚠️ | 宽度恒等于两锚点间距，与内容无关。要内容自适应宽用 `left + max-width`（shrink-to-fit） |

## 生命周期 / 程序化操作

| 特性 | 状态 | 说明 |
|------|------|------|
| 对 detached 元素挂带 USS 规则的 class | ❌ 崩溃 | 元素未挂到 panel 就 `AddToClassList`（class 命中含布局/动画属性的 USS 规则），后续 attach 时样式遍历越界。**必须先 `parent.Add(el)` 再挂 class**（本项目 `ApplyEntranceAnimation` 因此从 `Build()` 移到挂载后调用） |
| USS 后代选择器热切主题 | ⚠️ | 主题切换后部分后代选择器不重算。**主题关键色用 C# `PaintThemeInline` 内联刷** |

## 字体

| 做法 | 结果 |
|------|------|
| 直接把 `UnityEngine.Font` 塞进 `unityFontDefinition` | ❌ `MissingReferenceException: m_AtlasTextures of FontAsset doesn't exist anymore`（UITK 走 TextCore，需要带 atlas 的 FontAsset） |
| 直接把 `TMPro.TMP_FontAsset` 塞进去 | ❌ 类型不兼容（Tuanjie 的 `TextCore.Text.FontAsset` 与 TMP 版不是同一类型） |
| **`FontAsset.CreateFontAsset(legacyFont)` 运行时从 TTF 创建** | ✅ 正确姿势（见 `A2uiSchemeAHost.LoadMiSansFont`） |
| USS `-unity-font-definition: resource(...)` | ❌ 本引擎会崩（样式读取器） |
| USS `-unity-font-style: bold` | ✅ 可用（引擎对字体做加粗模拟，观感一般但稳定） |

## 测试 / batchmode

| 特性 | 状态 | 说明 |
|------|------|------|
| `-runTests -testPlatform PlayMode` batchmode | ✅ | 正常出 NUnit XML |
| `ScreenCapture.CaptureScreenshotAsTexture()` batchmode | ⚠️ | 需带显卡环境；无独显 CI 用 `--only-geometry` 跳过截图 |
| asmdef `overrideReferences: true` | ⚠️ | 预编译 DLL（Newtonsoft.Json.dll）必须列进 `precompiledReferences`，放 `references` 无效 |
| 手写 `.meta` 的 GUID | ⚠️ | 必须 32 位 hex；多一位 Unity 会静默忽略该资产（编译无它，且只报"找不到类型"） |

## ScrollView / 滚动

| 特性 | 状态 | 说明 |
|------|------|------|
| 普通 VisualElement `overflow: auto` | ❌ 无滚动条 | 只做裁剪，**不产生滚动条**（没有 Scroller）。要滚动必须用 ScrollView |
| ScrollView `mode = Vertical` | ⚠️ 不隐藏横向 Scroller | 即使锁 Vertical，横向 Scroller 仍会实体化（占 24px 高）。**必须 `horizontalScrollerVisibility = ScrollerVisibility.Hidden`** |
| `ScrollView.ScrollerVisibility` 嵌套枚举 | ❌ 不存在 | 本引擎把枚举拎到顶层 `UnityEngine.UIElements.ScrollerVisibility`（Auto/AlwaysVisible/Hidden）。写 `ScrollView.ScrollerVisibility.Hidden` 编译错 CS0117 |
| ScrollView 自带 chrome（边框/底色/圆角） | ⚠️ | Unity 默认主题给 ScrollView 和 content-viewport 画灰色圆角边框，嵌在自定义主题卡上会显形。**用 USS 复位：透明底 + border-width:0 + 隐藏横向 scroller** |
| percent `max-height` | ⚠️ 需父级定高 | `max-height:100%` 只在父级高度 definite 时解析；父级 auto 高度下被当 undefined。**用 top/bottom 双钉边给条带定高** |

## 截图 / 帧捕获

| 做法 | 结果 |
|------|------|
| `ScreenCapture.CaptureScreenshot(path, 1)` | ✅ 唯一可靠路径（异步写盘，轮询文件出现+尺寸稳定后再继续；建议再请求一张哑截图做 FIFO 屏障） |
| `ScreenCapture.CaptureScreenshotAsTexture()` | ❌ 返回空纹理（"Passed in texture is invalid"） |
| `ReadPixels` + `WaitForEndOfFrame` | ❌ 读到全黑背缓冲（UI Toolkit overlay 内容不在其中） |

## 本仓库的防御措施

以上坑在代码里都有对应防御，改这些地方前先想清楚：

- `Crafted.uss`：所有容器子元素 `min-width:0 + flex-shrink:1`；卡片 `overflow:hidden`
- `A2uiV08CatalogMapper.MapFlex`：Column 强制 NoWrap
- `A2uiV08CatalogMapper.MapTabs`：tab 按钮内联 `flexGrow=0 + NoWrap`（防 DS 主题均分撑破）
- `A2uiV08CatalogMapper.gen text 变体`：Mapper 的类名是 `a2ui-text--<usageHint>` 动态拼接，**皮肤 USS 必须生成全部字号变体**（display/h1..h5/body/caption），缺谁谁回退基础字号（`figma_to_uss.py` 生成器已修）
- `A2uiSchemeAHost.RenderOverlay`：内容包 ScrollView 时 `horizontalScrollerVisibility = ScrollerVisibility.Hidden`（横向滚动条在 fillMaxWidth 合同下永不该出现）+ Host.uss 复位 ScrollView 默认 chrome
- `A2uiDragManipulator.ApplyClamped`：**逐轴钳制**——某轴可动范围退化（卡片≥父容器，如定高条带里的高卡片）就还原该轴自由拖拽（dc6542c 语义在条带定高后由逐轴逻辑延续）；`IsInteractive` 不把 ScrollView 算交互件（否则卡片内容区完全无法启动拖拽；滚轮滚动走 PointerWheelEvent 不冲突）
- `A2uiV08CatalogMapper.Build`：不内置动画；`A2uiSchemeAHost.RenderOverlay` 在 attach 后调 `ApplyEntranceAnimation`
- `A2uiSchemeAHost.LoadMiSansFont`：`FontAsset.CreateFontAsset` 正确姿势
- 回归测试 `A2uiMapperUnitTests` 把这些规则固化成断言，改坏了会红
