# -*- coding: utf-8 -*-
"""Generate one JSONL unit sample per Catalog type."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "Assets" / "A2UISchemeA" / "Samples" / "components"
CATALOG = "https://a2ui.org/specification/v0_8/json/standard_catalog_definition.json"


def write(name: str, lines: list[str]) -> None:
    ROOT.mkdir(parents=True, exist_ok=True)
    path = ROOT / f"{name}.v0.8.jsonl"
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("wrote", path.name)


def begin(surface: str, root: str = "root") -> str:
    return (
        '{"beginRendering":{"surfaceId":"%s","root":"%s","catalogId":"%s"}}'
        % (surface, root, CATALOG)
    )


def main() -> None:
    write(
        "Text",
        [
            "# prompt: unit Text",
            '{"surfaceUpdate":{"surfaceId":"u-text","components":[{"id":"root","component":{"Column":{"children":{"explicitList":["h1","body","cap"]},"alignment":"stretch"}}},{"id":"h1","component":{"Text":{"text":{"literalString":"标题 H1"},"usageHint":"h1"}}},{"id":"body","component":{"Text":{"text":{"path":"/msg"},"usageHint":"body"}}},{"id":"cap","component":{"Text":{"text":{"literalString":"caption 说明文字"},"usageHint":"caption"}}}]}}',
            '{"dataModelUpdate":{"surfaceId":"u-text","contents":[{"key":"msg","valueString":"这是 body，绑定 dataModel。"}]}}',
            begin("u-text"),
        ],
    )
    write(
        "Image",
        [
            "# prompt: unit Image",
            '{"surfaceUpdate":{"surfaceId":"u-image","components":[{"id":"root","component":{"Column":{"children":{"explicitList":["img"]},"alignment":"stretch"}}},{"id":"img","component":{"Image":{"url":{"literalString":"https://picsum.photos/seed/a2ui-unit/640/360"},"altText":{"literalString":"单元测封面"},"fit":"cover","usageHint":"mediumFeature"}}}]}}',
            begin("u-image"),
        ],
    )
    write(
        "Icon",
        [
            "# prompt: unit Icon",
            '{"surfaceUpdate":{"surfaceId":"u-icon","components":[{"id":"root","component":{"Row":{"children":{"explicitList":["i1","i2","i3"]},"distribution":"spaceAround","alignment":"center"}}},{"id":"i1","component":{"Icon":{"name":{"literalString":"home"}}}},{"id":"i2","component":{"Icon":{"name":{"literalString":"settings"}}}},{"id":"i3","component":{"Icon":{"name":{"literalString":"warning"}}}}]}}',
            begin("u-icon"),
        ],
    )
    write(
        "Video",
        [
            "# prompt: unit Video",
            '{"surfaceUpdate":{"surfaceId":"u-video","components":[{"id":"root","component":{"Video":{"url":{"literalString":"https://example.com/demo.mp4"}}}]}}',
            begin("u-video"),
        ],
    )
    write(
        "AudioPlayer",
        [
            "# prompt: unit AudioPlayer",
            '{"surfaceUpdate":{"surfaceId":"u-audio","components":[{"id":"root","component":{"AudioPlayer":{"url":{"literalString":"https://example.com/track.mp3"},"description":{"literalString":"夜航星图"}}}]}}',
            begin("u-audio"),
        ],
    )
    write(
        "Row",
        [
            "# prompt: unit Row",
            '{"surfaceUpdate":{"surfaceId":"u-row","components":[{"id":"root","component":{"Row":{"children":{"explicitList":["a","b","c"]},"distribution":"spaceBetween","alignment":"center"}}},{"id":"a","component":{"Text":{"text":{"literalString":"A"},"usageHint":"h3"}}},{"id":"b","component":{"Text":{"text":{"literalString":"B"},"usageHint":"h3"}}},{"id":"c","component":{"Text":{"text":{"literalString":"C"},"usageHint":"h3"}}}]}}',
            begin("u-row"),
        ],
    )
    write(
        "Column",
        [
            "# prompt: unit Column",
            '{"surfaceUpdate":{"surfaceId":"u-col","components":[{"id":"root","component":{"Column":{"children":{"explicitList":["a","b"]},"distribution":"start","alignment":"stretch"}}},{"id":"a","component":{"Text":{"text":{"literalString":"上"},"usageHint":"h3"}}},{"id":"b","component":{"Text":{"text":{"literalString":"下"},"usageHint":"body"}}}]}}',
            begin("u-col"),
        ],
    )
    write(
        "List",
        [
            "# prompt: unit List",
            '{"surfaceUpdate":{"surfaceId":"u-list","components":[{"id":"root","component":{"List":{"direction":"vertical","alignment":"stretch","children":{"explicitList":["i1","i2","i3"]}}}},{"id":"i1","component":{"Text":{"text":{"literalString":"列表项 1"},"usageHint":"body"}}},{"id":"i2","component":{"Text":{"text":{"literalString":"列表项 2"},"usageHint":"body"}}},{"id":"i3","component":{"Text":{"text":{"literalString":"列表项 3"},"usageHint":"body"}}}]}}',
            begin("u-list"),
        ],
    )
    write(
        "Card",
        [
            "# prompt: unit Card",
            '{"surfaceUpdate":{"surfaceId":"u-card","components":[{"id":"root","component":{"Card":{"child":"inner"}}},{"id":"inner","component":{"Text":{"text":{"literalString":"卡片内容"},"usageHint":"h3"}}}]}}',
            begin("u-card"),
        ],
    )
    write(
        "Tabs",
        [
            "# prompt: unit Tabs",
            '{"surfaceUpdate":{"surfaceId":"u-tabs","components":[{"id":"root","component":{"Tabs":{"tabItems":[{"title":{"literalString":"页签A"},"child":"a"},{"title":{"literalString":"页签B"},"child":"b"}]}}},{"id":"a","component":{"Text":{"text":{"literalString":"内容 A"},"usageHint":"body"}}},{"id":"b","component":{"Text":{"text":{"literalString":"内容 B"},"usageHint":"body"}}}]}}',
            begin("u-tabs"),
        ],
    )
    write(
        "Divider",
        [
            "# prompt: unit Divider",
            '{"surfaceUpdate":{"surfaceId":"u-div","components":[{"id":"root","component":{"Column":{"children":{"explicitList":["t1","d","t2"]}}}},{"id":"t1","component":{"Text":{"text":{"literalString":"上方"},"usageHint":"body"}}},{"id":"d","component":{"Divider":{"axis":"horizontal"}}},{"id":"t2","component":{"Text":{"text":{"literalString":"下方"},"usageHint":"body"}}}]}}',
            begin("u-div"),
        ],
    )
    write(
        "Modal",
        [
            "# prompt: unit Modal",
            '{"surfaceUpdate":{"surfaceId":"u-modal","components":[{"id":"root","component":{"Modal":{"entryPointChild":"open","contentChild":"panel"}}},{"id":"open","component":{"Button":{"child":"ot","primary":true,"action":{"name":"noop"}}}},{"id":"ot","component":{"Text":{"text":{"literalString":"打开弹层"},"usageHint":"body"}}},{"id":"panel","component":{"Column":{"children":{"explicitList":["msg"]}}}},{"id":"msg","component":{"Text":{"text":{"literalString":"这是 Modal 内容（点击入口切换显示）"},"usageHint":"h4"}}}]}}',
            begin("u-modal"),
        ],
    )
    write(
        "Button",
        [
            "# prompt: unit Button",
            '{"surfaceUpdate":{"surfaceId":"u-btn","components":[{"id":"root","component":{"Row":{"children":{"explicitList":["p","s"]},"distribution":"start"}}},{"id":"p","component":{"Button":{"child":"pt","primary":true,"action":{"name":"primary_click","context":[{"key":"x","value":{"literalNumber":1}}]}}}},{"id":"pt","component":{"Text":{"text":{"literalString":"主按钮"},"usageHint":"body"}}},{"id":"s","component":{"Button":{"child":"st","primary":false,"action":{"name":"secondary_click"}}}},{"id":"st","component":{"Text":{"text":{"literalString":"次按钮"},"usageHint":"body"}}}]}}',
            begin("u-btn"),
        ],
    )
    write(
        "CheckBox",
        [
            "# prompt: unit CheckBox",
            '{"surfaceUpdate":{"surfaceId":"u-cb","components":[{"id":"root","component":{"CheckBox":{"label":{"literalString":"勿扰模式"},"value":{"path":"/on"}}}]}}',
            '{"dataModelUpdate":{"surfaceId":"u-cb","contents":[{"key":"on","valueBoolean":true}]}}',
            begin("u-cb"),
        ],
    )
    write(
        "TextField",
        [
            "# prompt: unit TextField",
            '{"surfaceUpdate":{"surfaceId":"u-tf","components":[{"id":"root","component":{"Column":{"children":{"explicitList":["s","l","o","n"]}}}},{"id":"s","component":{"TextField":{"label":{"literalString":"短文本"},"text":{"literalString":"hello"},"textFieldType":"shortText","validationRegexp":"^[a-z]+$"}}},{"id":"l","component":{"TextField":{"label":{"literalString":"长文本"},"text":{"literalString":"多行内容"},"textFieldType":"longText"}}},{"id":"o","component":{"TextField":{"label":{"literalString":"密码"},"text":{"literalString":"secret"},"textFieldType":"obscured"}}},{"id":"n","component":{"TextField":{"label":{"literalString":"数字"},"text":{"literalString":"42"},"textFieldType":"number"}}}]}}',
            begin("u-tf"),
        ],
    )
    write(
        "DateTimeInput",
        [
            "# prompt: unit DateTimeInput",
            '{"surfaceUpdate":{"surfaceId":"u-dt","components":[{"id":"root","component":{"DateTimeInput":{"value":{"literalString":"2026-07-29T18:00:00"},"enableDate":true,"enableTime":true}}}]}}',
            begin("u-dt"),
        ],
    )
    write(
        "MultipleChoice",
        [
            "# prompt: unit MultipleChoice",
            '{"surfaceUpdate":{"surfaceId":"u-mc","components":[{"id":"root","component":{"MultipleChoice":{"selections":{"literalArray":["b"]},"options":[{"label":{"literalString":"选项A"},"value":"a"},{"label":{"literalString":"选项B"},"value":"b"},{"label":{"literalString":"选项C"},"value":"c"}],"maxAllowedSelections":2,"variant":"chips","filterable":true}}}]}}',
            begin("u-mc"),
        ],
    )
    write(
        "Slider",
        [
            "# prompt: unit Slider",
            '{"surfaceUpdate":{"surfaceId":"u-sl","components":[{"id":"root","component":{"Slider":{"label":{"literalString":"音量"},"value":{"literalNumber":0.4},"minValue":0,"maxValue":1}}}]}}',
            begin("u-sl"),
        ],
    )
    write(
        "MediaMiniBar",
        [
            "# prompt: unit MediaMiniBar",
            '{"surfaceUpdate":{"surfaceId":"u-mm","components":[{"id":"root","component":{"MediaMiniBar":{"title":{"literalString":"夜航星图"},"child":"play"}}},{"id":"play","component":{"Button":{"child":"pt","primary":true,"action":{"name":"toggle_play"}}}},{"id":"pt","component":{"Text":{"text":{"literalString":"播放"},"usageHint":"body"}}}]}}',
            begin("u-mm"),
        ],
    )
    write(
        "ClimateStep",
        [
            "# prompt: unit ClimateStep",
            '{"surfaceUpdate":{"surfaceId":"u-cl","components":[{"id":"root","component":{"ClimateStep":{"tempLabel":{"literalString":"24°C"},"child":"row"}}},{"id":"row","component":{"Row":{"children":{"explicitList":["down","up"]}}}},{"id":"down","component":{"Button":{"child":"dt","primary":false,"action":{"name":"temp_down"}}}},{"id":"dt","component":{"Text":{"text":{"literalString":"-"},"usageHint":"body"}}},{"id":"up","component":{"Button":{"child":"ut","primary":true,"action":{"name":"temp_up"}}}},{"id":"ut","component":{"Text":{"text":{"literalString":"+"},"usageHint":"body"}}}]}}',
            begin("u-cl"),
        ],
    )
    write(
        "RestBanner",
        [
            "# prompt: unit RestBanner",
            '{"surfaceUpdate":{"surfaceId":"u-rb","components":[{"id":"root","component":{"RestBanner":{"text":{"literalString":"休憩模式已开启"}}}]}}',
            begin("u-rb"),
        ],
    )
    print("total", len(list(ROOT.glob("*.jsonl"))))


if __name__ == "__main__":
    main()
