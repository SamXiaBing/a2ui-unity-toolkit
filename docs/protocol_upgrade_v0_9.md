# A2UI 协议版本升级评估：v0.8 → v0.9.1

## 现状

本仓库当前实现 **A2UI v0.8**（ChatGPT 时代的第一版规范）。
上游 a2ui-project 当前版本：**v0.9.1（生产发布）**，v1.0（Release Candidate）。

## v0.8 → v0.9 核心变化

| 变化 | v0.8 | v0.9 | 迁移量 |
|------|------|------|--------|
| 消息类型 | `surfaceUpdate` / `beginRendering` / `dataModelUpdate` / `deleteSurface` | `createSurface` / `updateComponents` / `updateDataModel` / `deleteSurface` | Processor + Validator |
| version 字段 | 无 | 每条消息必须 `"version": "v0.9"` | Validator |
| 组件结构 | `{"id":"r", "component":{"Card":{"child":"c"}}}`（类型为嵌套 key） | `{"id":"r", "component":"Card", "child":"c"}`（类型为平铺字段） | Processor + Mapper |
| 容器子节点 | `{"children":{"explicitList":["a","b"]}}` | `"children": ["a","b"]`（直接数组） | Processor + Mapper |
| template 子节点 | `{"children":{"template":{"componentId":"t","dataBinding":"/path"}}}` | `"children":{"path":"/path","componentId":"t"}` | Processor |
| 文本属性 | `text:{literalString:"..."}` 或 `text:{path:"/x"}` | `text:"..."` 或 `text:{path:"/x"}`（字面值直接写） | Mapper |
| 布局属性名 | `distribution` / `alignment` | `justify` / `align` | Mapper |
| 字号 | `usageHint` | `variant` | Mapper |
| createSurface | 不存在（surfaceUpdate + beginRendering 两步） | 一步创建（含 catalogId + theme + sendDataModel） | Processor |
| beginRendering | 独立消息，声明 root | **移除**——root 是 updateComponents 中 id="root" 的组件 | Processor |
| 双向绑定 | 不支持（需 action 回传） | 输入组件直接写 DataModel（协议级） | Mapper + Processor |
| theme | 每主题独立 USS 文件 | `createSurface.theme.primaryColor` JSON 内联 | Mapper |

## 迁移策略：双栈过渡（推荐）

不做断裂式替换，而是**Processor 同时接受 v0.8 和 v0.9 格式**：

```
IngestMessage(msg):
  if msg["createSurface"] or msg["updateComponents"]:
      → v0.9 路径（先归一化为内部模型）
  elif msg["surfaceUpdate"] or msg["beginRendering"]:
      → v0.8 路径（现有逻辑不变）
```

归一化层把 v0.9 的平铺组件结构展开为与 v0.8 相同的内部 `Dictionary<string, JObject>`，
后续 Mapper **完全不需要改**。

### 工作量估算

| 模块 | 变化 | 预估 |
|------|------|------|
| A2uiV08Processor | 增加 v0.9 格式识别 + 归一化到内部模型 | ~150 行 |
| A2uiV08Validator | 接受两种消息 key + version 字段 | ~40 行 |
| A2uiV08CatalogMapper | 不变（内部模型不变） | 0 |
| Samples（47 个） | 渐进迁移到 v0.9 格式（旧格式仍可跑） | 脚本化批量 |
| Tests | 增加双格式覆盖 | ~60 行 |

## 不迁移的风险

- 上游 A2UI 规范已标记 v0.8 为 legacy；新 Agent（Gemini/ADK）原生输出 v0.9
- Compose 参考渲染器已迁移到 v0.9+（README 标注 v0.10）
- 开源用户如果用 v0.9 Agent 推流，当前渲染器会 G0 拒收

## 结论

**必须迁移**。双栈过渡方案工作量可控（~250 行），且不破坏现有 517 组合回归。
建议作为下一个 major commit。
