# a2ui-unity-toolkit 开发计划（2026-09-07 → 2026-10-12，v0.2.0）

> 一个月中期迭代计划，20 个工作日、逐日一个小迭代。
> 产线：v0.1.0（已开源）→ **v0.2.0**。方法：Agent 团队协作产出（2 调研 + 1 起草 + 3 对抗审查 + 1 轮优化）。
> 版本：v3（2026-09-07，在 v2 基础上完成**第二轮**协议对齐/工程可行性/产品排期三方对抗审查（独立重跑），增量修订见文末审查记录·第二轮；v1 问题与采纳记录同节）。

---

## 1. 产品迭代方向（主线叙事）

当前状态：21 组件（18 标准 + 3 座舱）、v0.8/v0.9 双栈、5 主题热切、约 280 组合回归、座舱门禁。对照官方 google/A2UI（协议已至 v1.0-RC）与社区 Compose 渲染器，差距集中在**输入不回写、媒体占位、无流式、防护上限缺失**。

本月四周主题，顺序由依赖关系决定：

| 周 | 主题 | 一句话 |
|---|---|---|
| W1 | 协议对齐与安全上限 | 堵住防护边界（上限/官方 error 封套），补真 v0.9.1 字段——改动小、风险低，为交互闭环铺可观测性地基 |
| W2 | 交互闭环 | 输入类双向绑定 → Modal → DateTimeInput，让界面从"能渲染"变"能操作、能写回 DataModel" |
| W3 | 流式与媒体 | 流式增量渲染（TTFC）+ Video/Audio 真播放——媒体是车机 GenUI 最高频演示卡，先于列表虚拟化 |
| W4 | 列表方案、内置函数与发版 | 列表虚拟化 PoC 决策（保底分页）、求值器全集、conformance 对齐，收口发 v0.2.0 |

**日历基线**：9/25（周五）中秋节休；10/1-10/7 国庆休；10/8、10/9 为工作日；D20 排 10/12（周一）。国办通知已确认 **9/20（周日）与 10/10（周六）为调休上班日**（第二轮审查核实）：9/20 列为列表 PoC 提前/补欠账弹性位，10/10 列为发布前最后缓冲。10/3-10/7 列为备用弹性产能。

---

## 2. 逐日计划（Day 1-20）

> 常规验证 = `python Tools/run_regression.py`（约 280 组合，无 GPU 环境 `--only-geometry`）。
> **基线冻结点：D5 / D9 / D14 / D18**（GPU 全量回归 + `--update-baselines`）；平时改动只对受影响样例局部刷新基线，防止像素 diff 全红。
> 改 Mapper/USS 前必读 `docs/engine-compat-tuanjie.md`（USS 一律字面量、禁用 transition+transform 组合、gap 用 margin 等铁律）。
> **新增样式一律 C# 内联兜底**（第二轮审查补充）：Inputs/Controls/Overlays.uss 目前仅覆盖 DS 主题（M3/Figma 无对应文件），且引擎后代选择器偶发失效——错误态、弹层、滑柄等新样式优先 C# 内联，避免 5 主题 USS 同步成本。

### W1 协议对齐与安全上限（9/7 一 → 9/11 五）

| Day | 日期 | 目标 | 改动点 | 验证 | 依赖 |
|---|---|---|---|---|---|
| D1 | 9/7 一 | ✅ 消息 1MB / 单 surface 组件 1000 / surface 50 三上限（2026-09-07 完成：6 例单测 + 全矩阵回归 47/47 绿；手工 HTTP 冒烟并入下次 GUI 会话） | 上限**收进 `A2uiV08Validator.cs` 单点内部**（自动覆盖双入口：`A2uiSchemeAHost.cs` L639 与 `A2uiLauncherSurfaceHost.cs` L331） | 超限拒收单测×双入口 + HTTP 主通道手工推超限包 + TCP/inbox 冒烟 | 无 |
| D2 | 9/8 二 | Path 深度 10 / 键长 50 / 错误条数 100；surfaceId 唯一性；theme.primaryColor 解析 | Validator 加限制；**重复 createSurface 未删先建拒绝**（官方 Processing rules 明确 surfaceId 会话内全局唯一，Runtime 现无重复判定，防御性收口）；`A2uiV09Normalizer.cs` 通用解析 primaryColor→USS 变量覆盖（**仅 v0.9 栈，timebox 半日**；v1.0 已删该字段，不做深度主题映射） | 单测（含重复建面拒绝）+ 5 主题回归 | D1 |
| D3 | 9/9 三 | **官方形态** error 消息上报 | 顶层封套 `{version, error:{code,surfaceId,message,path}}`（path 用 JSON Pointer），独立 SendError 通道，**不混用 action 通道**；宿主落 JSONL | 单测 + 坏包样例→agent 侧可解析 | D1 |
| D4 | 9/10 四 | 真 v0.9.1 字段补齐 | ChoicePicker.chips 完善（`A2uiV08CatalogMapper.cs` L792 已半实现）、DateTimeInput.min/max 校验、TextField.obscured（**@index/null删键/steps/placeholder 是 v1.0 项，移出**） | 单测 + 样例截图 | D1 |
| D5 | 9/11 五 | Slider 拖柄主题化 + W1 收口 | tracker/dragger USS 选择器修复（显性视觉 bug 提前）；**若后代选择器仍未命中（矩阵已知偶发失效），改 C# 内联 dragger 样式兜底**；W1 新场景样例入库；**M1 基线冻结（GPU 全量）**，当天打预发布 tag `v0.1.1` | `run_regression.py` 全绿 | W1 |

### W2 交互闭环（9/14 一 → 9/18 五）

| Day | 日期 | 目标 | 改动点 | 验证 | 依赖 |
|---|---|---|---|---|---|
| D6 | 9/14 一 | 双向绑定 a：写回通道 | `A2uiV08Processor.cs` 增 SetStateByPath + 变更事件；TextField/Slider/MultipleChoice/CheckBox 按 PathValue 回写 DataModel（**回写语义按官方：本地立即生效、不触发网络消息，action 时经 context 上报**——第二轮审查核实规范无 changeValue 网络事件，勿自造事件契约） | PlayMode 写回单测 | D4 |
| D7 | 9/15 二 | 双向绑定 b：局部刷新 | 最小表达式求值器（路径抽取 + formatString，绑定明示依赖）+ 同路径 Text 实时刷新（v0.9.1 reactivity）+ 防回环（忽略自身回声）+ `A2uiPolicyGate` 行驶禁写 | 手工：改值→局部刷新；驾驶态拦截 | D6 |
| D8 | 9/16 三 | Modal a：遮罩 + 焦点 | 遮罩层 + Tab 循环焦点拦截（**不做全树禁焦**） | 截图回归 | D4 |
| D9 | 9/17 四 | Modal b：交互与动画 | 外点关 / Esc 关；C# 入场动画（复用 ApplyEntranceAnimation，**禁用 USS transition+transform 组合**） | 三交互手工 + **基线冻结②（GPU）** | D8 |
| D10 | 9/18 五 | DateTimeInput 选择器 + M2 验收 | 年/月/日三段 DropdownField 弹层（UITK 运行时无原生 DatePicker；min/max 约束下月）；**弹层容器不在 ds-root 作用域内，样式必须 C# 内联（后代选择器命中不了 popup 容器）；detached 元素严禁先 AddToClassList（先 parent.Add 再挂类）**（第二轮审查补充引擎坑）；**M2 验收清单逐项过检 + 录屏存档**（写回/局部刷新/驾驶禁写/Modal 三交互/DateTime 选择），当天打预发布 tag `v0.1.2` | 过检记录 + GPU 回归 | D7 D9 |

### W3 流式与媒体（9/21 一 → 9/24 四；9/25 五中秋休）

| Day | 日期 | 目标 | 改动点 | 验证 | 依赖 |
|---|---|---|---|---|---|
| D11 | 9/21 一 | Video 真播放 a | `MapVideo`（L417）去占位：VideoPlayer→RenderTexture→PanelImage 管线 + posterUrl + 本地/HTTP mp4 | 手工播放 + 截图 | D4 |
| D12 | 9/22 二 | 媒体收尾 | `MapAudioPlayer`（**L432**）AudioSource 播放；Video/Audio 接 `A2uiPolicyGate` 行驶禁播/音频焦点联动；媒体样例入库 | 手工 + 5 主题回归 | D11 |
| D13 | 9/23 三 | 流式 a：增量应用 | `A2uiV08Processor.cs` patch 级更新（免全量 rebuild） | 增量单测 + **增量前后界面 diff/日志对照（保当天可演示）** | D7 |
| D14 | 9/24 四 | 流式 b：TTFC 指标 | `push_a2ui_bench.py` / `ollama_a2ui_loop.py` 测首组件时间，分片演示，数据落盘 | bench 数据 + **基线冻结③（GPU 全量）**，当天打预发布 tag `v0.1.3`（次日中秋假） | D13 |

### W4 列表方案、函数全集与发版（9/28 一 → 10/12 一；10/1-10/7 国庆休）

| Day | 日期 | 目标 | 改动点 | 验证 | 依赖 |
|---|---|---|---|---|---|
| D15 | 9/28 一 | 列表虚拟化 PoC 决策 | 原生 **ListView**（itemsSource+bindItem 自带回收）包 List 组件 spike：回收正确性/滚动手感/ScrollView chrome 复位；产出结论 = ListView 方案 **or** 分页保底 | PoC 样例 + 决策记录 | D4（可并行提前） |
| D16 | 9/29 二 | 列表方案落地 | 按 D15 结论接入 List 组件 + 交互状态随回收保持 | 500 行样例构建耗时对比 + 滚动回归 | D15 |
| D17 | 9/30 三 | 内置函数求值器全集 + callFunction 拒绝路径 | formatDate/formatNumber/formatCurrency/pluralize/and/or/not（**v0.9.1 已有，非 v1.0 新增**）接入 D7 求值器；**callFunction 未注册函数最小拒绝路径**：回 error（含 functionCallId，与 surfaceId 互斥），不静默吞（第二轮审查补充，官方要求必答，半天量级） | 单测全集 + 拒绝路径单测 | D7 |
| D18 | 10/8 四 | conformance 对齐 | 官方 v0.9.1 校验用例（76 条）择要 30+，**JSON 数据驱动 + 参数化 C# runner**（勿逐条手写）；测试文件头保留 Apache-2.0 归属声明 | 全绿 + **基线冻结④（GPU）** | D17 |
| D19 | 10/9 五 | **整日缓冲** | 补前序欠账；若全绿 → v1.0 门控字段预研（placeholder/steps/@index/null删键，仅调研设计不动代码）；官方 37 示例（catalogs/basic/examples 00-36）全量导入评估（**第二轮审查核实官方示例为 37 个**） | — | — |
| D20 | 10/12 一 | **v0.2.0 发版** | CHANGELOG / 双语 README 更新 / 回归终跑 / `Tools/prepare_github_release.py`（走 release 仓库推送） | tag + 发布产物 + M3 验收 | W1-W3 全绿 |

---

## 3. 里程碑

| 检查点 | 日期 | 验收标准 |
|---|---|---|
| **M1 协议安全对齐** | 9/11 | 三类上限生效（双入口）、官方 error 封套可被 agent 解析、真 v0.9.1 字段样例全过、滑柄拖柄主题化可见；回归全绿 + 基线冻结 |
| **M2 交互闭环** | 9/18 | 验收清单逐项过检并录屏：四类输入写回 DataModel、同路径局部刷新、防回环、驾驶态禁写、Modal 遮罩/外点/Esc/动画、DateTime 三段选择 |
| **M3 v0.2.0 发布** | 10/12 | 流式 TTFC 数据落盘、Video/Audio 真播放 + 行驶禁播、列表方案落地（含耗时对比数据）、conformance 30+ 用例绿；v0.2.0 tag + Release |

> **预发布 tag 与冻结点对齐**（第二轮审查建议）：9/11 `v0.1.1`、9/18 `v0.1.2`、9/24 `v0.1.3`——月内多个可回退点，对外每周有可见进展；发布物料（README/CHANGELOG/Roadmap 勾选）10/8-10/9 预备，**发布日只发不写**。

## 4. Top5 风险与对策

1. **列表方案不确定**（自研回收实测需 4-6 天）→ D15 先 PoC 决策，保底分页；全量虚拟化移出本月主菜。
2. **UITK 无原生件 + 引擎坑密集**（Modal/DatePicker 全自建；transition+transform 崩溃、var() 逗号 fallback 静默丢弃）→ 动画走 C#、USS 一律字面量、改前读兼容矩阵。
3. **像素基线失效**（20 天连续改 Mapper/USS，只冻一次必全红）→ 4 个里程碑冻结点 + 日常受影响样例局部刷新。
4. **上游 v1.0-RC 漂移** → 本月只做 v0.9.1 确认项与官方 error 封套；v1.0 门控字段仅调研不实现。
5. **假期节奏**（9/25 中秋 + 10/1-10/7 国庆，自然工作日仅 19 个）→ D20 排 10/12、D19 整日缓冲、假期列备用弹性；不赌调休安排。

## 5. 明确不在本月范围

a11y 全面化（AccessibilityAttributes 仅解析）、C# agent SDK、图表组件、callRendererFunction/callAgentFunction 完整 RPC（**仅保留未注册拒绝路径**，见 D17）、WebSocket/SSE+重连、状态持久化恢复、120+ SVG 矢量图标替换、主题编辑器、v1.0 门控字段实现（仅预研）、列表全量自研虚拟化回收、官方 37 示例（00-36）全量导入回归矩阵（本期只进 conformance 用例与发布 smoke）。

---

## 6. 审查记录（v1 → v2 修订依据）

三方对抗审查（2026-09-07 并行），**判定均为打回**；以下 P0/P1 全部采纳：

| 审查员 | 判定 | 关键发现（采纳情况） |
|---|---|---|
| A 协议对齐 | 打回 | D3 自造私有 error 格式 → 改官方顶层封套+JSON Pointer（D3 重写）；D4 字段归属错（@index/steps/placeholder 是 v1.0）→ 缩为真 v0.9.1 三项、v1.0 项移出（D4 重写）；theme.primaryColor v1.0 已删 → 限 v0.9 栈+timebox（D2 降级）；内置函数是 v0.9.1 非 v1.0，且求值器是 D7 前置 → D7 加最小求值器、D17 改"全集扩展"（依赖链修正）；conformance 实为 v0.9.1=76 条 → 数据驱动参数化 runner（D18 改法）；Apache-2.0 归属头（D18 加） |
| B 工程可行性 | 打回 | `MapAudioPlayer` 实为 L432 非 L441（修正）；ValidateJsonl 存在第二入口 `A2uiLauncherSurfaceHost.cs` L331 → 上限收进 Validator 单点（D1 改法）；虚拟化两天严重低估 → 降级 PoC 决策+分页保底（D15/D16 重写）；D7 局部刷新与回收冲突 → 先定方案再刷新（依赖修正）；基线只冻一次必全红 → 4 冻结点（验证策略改）；Modal 动画禁 USS transition+transform → C# 复用 ApplyEntranceAnimation（D9 改）；DatePicker 无原生 → 三段 DropdownField（D10 改）；--only-geometry 盲区 → 每周 GPU 全量（验证策略加） |
| C 产品排期 | 打回 | **9/25 周五中秋撞 D15** → 全月日历重排（P0）；D19 无落点 → 10/8 调研、发版独占 10/12；滑柄主题化显性 bug 应提前 → D5（W1）；媒体应先于虚拟化 → W3 重排（D11↔D15 对调）；D17/D11 依赖强凑 → 依赖链修正；M2 无验收清单 → 逐项过检+录屏（D10/M2 加）；D13 无 UI 可见变化 → 界面 diff/日志对照（D13 加）；缓冲偏薄 → D19 整日缓冲 |

审查中核实为真的 v1 事实：文件/行号引用 4 处属实、9/7 确为周一、10/1 周四/10/2 周五/10/9 周五、D1 可当天收工（前提已并入 D1 改法）。

### 第二轮审查（2026-09-07 本会话独立重跑）

第二轮 = 独立重跑同一流水线（explore 盘点 Unity 现状 38 次检索 → plan agent 起草 → 3×general-purpose 对抗审查，含 specification 只读核对、Runtime 行号搜索、官方规范 JSON 解析），与 v2 第一轮结论交叉印证后做增量修订（v2→v3）。**两轮独立审查在核心 blocker 上完全收敛**：日历撞期（9/25 中秋）、虚拟化低估（ListView PoC 决策）、基线冻结点、官方 error 封套、v1.0 字段移出、ChoicePicker 命名、上限收口 Validator 单点、MapVideo L417/MapAudioPlayer L432/双入口 L639+L331 行号逐一属实。

v3 增量采纳（第二轮新发现）：

| 来源 | 发现 | 处置 |
|---|---|---|
| 协议对齐 | surfaceId 会话内全局唯一、重复 createSurface 未删先建应拒绝（Runtime 无此判定） | → D2 |
| 协议对齐 | callFunction 未注册函数官方要求**必答** error{INVALID_FUNCTION_CALL, functionCallId}（与 surfaceId 互斥），不答即协议违约 | → D17（最小拒绝路径，半天） |
| 协议对齐 | 双向绑定规范语义=本地立即回写、不触发网络，action 时经 context 上报；**规范无 changeValue 网络事件**，自造事件契约属协议私造 | → D6 语义修正 |
| 协议对齐 | 官方示例实为 **37 个**（00-36）非 24+；v1.0 可执行一致性资产为 v1_0/test/cases 17 个 JSON（run_tests.py），数据驱动思路与 D18 一致 | → D19 / §7 |
| 工程可行性 | DropdownField 弹层容器不在 ds-root 作用域，USS 后代选择器命中不了；detached 元素先 AddToClassList 会崩（矩阵铁律） | → D10 坑位注记 |
| 工程可行性 | Slider tracker/dragger 现有选择器本身即后代选择器（偶发失效），纯 USS 修复不可押注 | → D5 C# 内联兜底 |
| 工程可行性 | Inputs/Controls/Overlays.uss 仅覆盖 DS 1/5 主题；新增样式走 USS 需 5 主题同步 | → §2 全局注记（C# 内联优先） |
| 产品排期 | 国办通知确认 9/20（日）、10/10（六）调休上班（非"若安排"） | → 日历基线坐实，9/20/10/10 为弹性位 |
| 产品排期 | 月内仅 v0.2.0 单发点太粗；发布物料应提前起草 | → 周更预发布 tag v0.1.1/v0.1.2/v0.1.3；物料 10/8-10/9 预备 |
| 产品排期 | W1 全协议无可感知产出 | → M1 含滑柄可见成果 + tag v0.1.1 + 官方示例端到端演示（已由 v2 覆盖大半，v3 补 tag 节奏） |

未采纳：将 DateTimeInput 降级回纯文本输入（v2 的三段 DropdownField 方案坑位已有兜底，保留）；发版提前到 9/30（维持 10/12——假期后缓冲更稳，且 10/10 调休日可作最后补救位）。

## 7. 附：调研依据（Agent 团队产出摘要）

- **Unity 现状盘点**（explore agent，38 次检索）：21 组件、双栈归一化、G0+深度 50、PolicyGate 六类拦截、单向绑定成熟/输入类不回写、图标 23 程序化纹理、消息与组件上限未实现、Video/Audio/DateTime/Modal 四占位、回归 5 主题×56 样例≈280。
- **参考实现分析**（general-purpose agent，读官方 google/A2UI monorepo + 社区 lmee/A2UI-Android）：协议 v0.9.1 稳定/v1.0-RC（双向 RPC、多 catalog、@index、error 上报）；conformance 用例 v0.9.1=76/v1.0=153；Compose 渲染器具备双向回写/ExoPlayer/原生日期弹层/LazyColumn/Dialog/上限族（1MB/1000/50/深度 10/键长 50）；官方 Q3'26 出 Compose/SwiftUI 渲染器+性能专项，Q4'26 v1.0 定稿+渲染器认证；无 C# SDK。
- 计划起草：plan agent（源码核实后产出 v1）；审查：3×general-purpose agent（含 specification 只读核对与行号搜索验证）。
- **第二轮复核**（3×审查 agent，独立重跑）：Runtime 行号（MapVideo L417 / MapAudioPlayer L432 / 双入口 L639+L331）逐一属实；国办 2026 假期安排（中秋 9/25-27、国庆 10/1-7、调休 9/20 与 10/10）核实；官方规范核对确认 ChoicePicker 仅 v0.9.x 命名（MultipleChoice 仅 v0.8）、14 个 catalog 函数中仅 5 个是验证函数（returnType=validationResult），formatString/formatNumber/formatDate/pluralize 为动态值函数且共用同一 FunctionCall 求值器（与 D7→D17 依赖链一致）；@index 限模板迭代 Collection Scope 且界外报错（D19 预研注记）。
