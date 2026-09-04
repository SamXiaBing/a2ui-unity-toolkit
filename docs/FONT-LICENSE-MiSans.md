# MiSans 字体许可

本仓库内置 `Assets/A2UISchemeA/Resources/MiSans-Regular.ttf`（小米开源字体 MiSans 的 Regular 字重），用于 UI Toolkit 的中文渲染。

- **许可**：MiSans 由小米公司在 [OPPO Sans 的许可基础上](https://hyperos.mi.com/font/)发布，允许免费商用与再分发（含于开源项目中分发），需随附本许可说明。
- **来源**：从小米官方渠道下载。
- **要求**：再分发时保留字体来源与版权声明；不应对字体文件本身单独收费。

如需更换字体：替换 `Resources/MiSans-Regular.ttf` 并保持文件名不变（运行时按 `Resources.Load<Font>("MiSans-Regular")` 加载，详见 `A2uiSchemeAHost.LoadMiSansFont` 与 `docs/engine-compat-tuanjie.md` 字体一节）。
