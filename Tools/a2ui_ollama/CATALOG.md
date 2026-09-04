# A2UI Catalog（与 Unity Validator 对齐）

仅使用下列 type（`component` 包装内唯一键）。

## Standard

`Text` `Image` `Icon` `Video` `AudioPlayer` `Row` `Column` `List` `Card` `Tabs` `Divider` `Modal` `Button` `CheckBox` `TextField` `DateTimeInput` `MultipleChoice` `Slider`

### 常用属性（精简）

| type | 要点 |
|------|------|
| Text | `text` 绑定；`usageHint`: caption/body/h1/h2/h3/h4 |
| Card | `child` |
| Column/Row | `children.explicitList`；`alignment` |
| Button | `child`；`primary`；`action.name` |
| CheckBox | `label`；`value` literalBoolean |
| Slider | `label`；`min`/`max`/`value` literalNumber |
| MultipleChoice | `options[]`；`selections`；`maxAllowedSelections` |
| Divider | `{}` |
| Image | `url` 或绑定（Demo 可用 picsum/占位语义文案） |
| List | `children` explicitList 或 template |

## 座舱扩展（本工程已映射）

| type | 用途 |
|------|------|
| MediaMiniBar | `title`/`text` + `child`（播放区） |
| ClimateStep | `tempLabel`/`text` + `child` |
| RestBanner | 休憩横幅 |

未知 type 会在客户端降级，但生成时**不要**发明新 type。

## action.name 建议白名单（闭环/ack）

`toggle_play` `prev_track` `next_track` `confirm_yes` `confirm_no` `dismiss` `nav_charge` `choose_route_a` `choose_route_b` `enable_dnd` `climate_cooler` `climate_warmer`
