#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""A2UI v0.8 JSONL → v0.9 JSONL 批量转换器。

映射依据 docs/protocol_upgrade_v0_9.md：

  消息级
    surfaceUpdate+beginRendering  → createSurface（首个，含 catalogId）+ updateComponents
    dataModelUpdate contents[]    → updateDataModel {path, value}
    deleteSurface                 → 原样（version 字段补 v0.9）
    beginRendering                → 丢弃（v0.9 root = updateComponents 里 id="root"）
  组件级
    {"component":{"Type":{...}}}  → {"component":"Type", ...}（平铺）
    text:{literalString:x}        → text:"x"（literal 直接值）
    children:{explicitList:[..]}  → children:[..]
    children:{template:{dataBinding,componentId}} → children:{path,componentId}
    distribution / alignment      → justify / align
    usageHint(Text)               → variant
    Button primary:true           → variant:"primary"
    action:{name,context:[{key,value}]} → action:{event:{name,context:{k:v}}}

用法：
  python Tools/v08_to_v09.py A/B.v0.8.jsonl [更多路径...]
  python Tools/v08_to_v09.py --all   # 转换 Samples/ 全部 .v0.8.jsonl（默认只转换内置白名单）

输出写到同目录同名 .v0.9.jsonl。
"""
import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SAMPLES = REPO / "Assets" / "A2UISchemeA" / "Samples"

# 默认转换白名单：8 个代表性样例（demos 全部 + 2 场景 + 2 功能）
DEFAULT_TARGETS = [
    "demos/catalog_all.v0.8.jsonl",
    "demos/coverage_tour.v0.8.jsonl",
    "demos/figma_button_demo.v0.8.jsonl",
    "demos/full_control_center.v0.8.jsonl",
    "scenarios/agent_01_charge_pick.v0.8.jsonl",
    "scenarios/app_01_login_screen.v0.8.jsonl",
    "features/climate_control.v0.8.jsonl",
    "features/list_template.v0.8.jsonl",
]


def unliteral(v):
    """{literalString:x}/{literalNumber}/{literalBoolean}/{literalArray} → 直接值；path 绑定保留。"""
    if isinstance(v, dict):
        for k in ("literalString", "literalNumber", "literalBoolean"):
            if k in v:
                return v[k]
        if "literalArray" in v:
            return [deep_unliteral(x) for x in v["literalArray"]]
        if set(v.keys()) == {"path"}:
            return v  # 纯 path 绑定原样保留
        if "path" in v:
            return v  # {path, literalNumber} 首帧兜底组合原样保留
        return {k: deep_unliteral(x) for k, x in v.items()}
    return v


def deep_unliteral(v):
    """递归剥 literal 壳：数组/嵌套对象逐层展开（tabItems、checks 等）。"""
    if isinstance(v, list):
        return [deep_unliteral(x) for x in v]
    return unliteral(v)


# beginRendering 里的 catalogId（surfaceId → catalogId），转换 createSurface 时取用
_pending_catalog = {}


def conv_action(a):
    """v0.8 {name, context:[{key,value}]} → v0.9 {event:{name, context:{k:v}}}；已是 v0.9 则原样。"""
    if not isinstance(a, dict) or "event" in a:
        return a
    ctx = {}
    for item in a.get("context") or []:
        if isinstance(item, dict) and "key" in item:
            ctx[item["key"]] = unliteral(item.get("value"))
    ev = {"name": a.get("name")}
    if ctx:
        ev["context"] = ctx
    return {"event": ev}


def conv_component(comp):
    """v0.8 嵌套组件 → v0.9 平铺组件。"""
    if not isinstance(comp, dict) or "component" not in comp:
        return comp
    out = {"id": comp["id"]}
    wrapper = comp["component"]
    ctype, props = next(iter(wrapper.items()))
    out["component"] = ctype
    for k, v in props.items():
        if k == "children":
            ch = v
            if "explicitList" in ch:
                out["children"] = ch["explicitList"]
            elif "template" in ch:
                t = ch["template"]
                out["children"] = {"path": t.get("dataBinding", "/"),
                                   "componentId": t.get("componentId")}
            else:
                out["children"] = ch
        elif k == "text":
            out["text"] = unliteral(v)
        elif k == "usageHint" and ctype == "Text":
            out["variant"] = v
        elif k == "primary" and ctype == "Button":
            if v:
                out["variant"] = "primary"
        elif k == "distribution":
            out["justify"] = v
        elif k == "alignment":
            out["align"] = v
        elif k == "action":
            out["action"] = conv_action(v)
        elif k == "minValue":
            out["min"] = unliteral(v)
        elif k == "maxValue":
            out["max"] = unliteral(v)
        else:
            out[k] = deep_unliteral(v)
    return out


def entry_value(e):
    if "valueString" in e:
        return e["valueString"]
    if "valueNumber" in e:
        return e["valueNumber"]
    if "valueBoolean" in e:
        return e["valueBoolean"]
    if "valueMap" in e:
        d = {i["key"]: entry_value(i) for i in e["valueMap"] if isinstance(i, dict)}
        keys = list(d.keys())
        # 数组在 v0.8 contents 里被展开为 "0","1",... 键；还原为 JSON 数组
        if keys and keys == [str(i) for i in range(len(keys))]:
            return [d[str(i)] for i in range(len(keys))]
        return d
    return None


def contents_to_value(contents):
    root = {}
    for e in contents or []:
        root[e["key"]] = entry_value(e)
    return root


def conv_message(msg, created):
    """一条 v0.8 消息 → 0..N 条 v0.9 消息。created 记录已 createSurface 的 surfaceId。
    注意：v0.8 允许 surfaceUpdate 与 beginRendering 合并在同一条消息里，必须先判 surfaceUpdate。"""
    if "surfaceUpdate" in msg:
        su = msg["surfaceUpdate"]
        sid = su["surfaceId"]
        out = []
        if sid not in created:
            cs = {"surfaceId": sid, "sendDataModel": True}
            catalog = _pending_catalog.get(sid)
            if catalog:
                cs["catalogId"] = catalog
            out.append({"version": "v0.9", "createSurface": cs})
            created.add(sid)
        comps = [conv_component(c) for c in su.get("components", []) if isinstance(c, dict)]
        out.append({"version": "v0.9",
                    "updateComponents": {"surfaceId": sid, "components": comps}})
        return out
    if "dataModelUpdate" in msg:
        dm = msg["dataModelUpdate"]
        value = contents_to_value(dm.get("contents"))
        path = dm.get("path", "/")
        return [{"version": "v0.9", "updateDataModel":
                 {"surfaceId": dm["surfaceId"], "path": path, "value": value}}]
    if "deleteSurface" in msg:
        return [{"version": "v0.9", "deleteSurface": msg["deleteSurface"]}]
    if "beginRendering" in msg:
        return []  # v0.9 root 声明在 updateComponents
    return [msg]


def convert_file(src: Path) -> Path:
    dst = src.with_suffix(".jsonl").with_name(src.name.replace(".v0.8.jsonl", ".v0.9.jsonl"))
    created = set()
    _pending_catalog.clear()
    out_lines = []

    # 两遍扫描：先收集所有 beginRendering 的 catalogId（beginRendering 常在文件末尾，
    # 也可能与 surfaceUpdate 合并在同一条消息里），再按序转换
    parsed = []
    with src.open("r", encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\r\n")
            stripped = line.strip()
            if not stripped:
                continue
            if stripped.startswith("#"):
                parsed.append(("comment", line))
                continue
            msg = json.loads(stripped)
            br = msg.get("beginRendering")
            if isinstance(br, dict) and br.get("surfaceId") and br.get("catalogId"):
                _pending_catalog[br["surfaceId"]] = br["catalogId"]
            parsed.append(("msg", msg))

    for kind, item in parsed:
        if kind == "comment":
            out_lines.append(item)
        else:
            for m in conv_message(item, created):
                out_lines.append(json.dumps(m, ensure_ascii=False))
    dst.write_text("\n".join(out_lines) + "\n", encoding="utf-8")
    return dst


def main():
    ap = argparse.ArgumentParser(description="A2UI v0.8 → v0.9 sample converter")
    ap.add_argument("paths", nargs="*", help="v0.8 jsonl 文件或目录")
    ap.add_argument("--all", action="store_true", help="转换 Samples/ 全部 *.v0.8.jsonl")
    args = ap.parse_args()

    if args.all:
        targets = sorted(SAMPLES.rglob("*.v0.8.jsonl"))
    elif args.paths:
        targets = []
        for p in args.paths:
            pp = Path(p)
            if pp.is_dir():
                targets += sorted(pp.rglob("*.v0.8.jsonl"))
            else:
                targets.append(pp)
    else:
        targets = [SAMPLES / rel for rel in DEFAULT_TARGETS]

    for t in targets:
        if not t.exists():
            print(f"SKIP(不存在): {t}", file=sys.stderr)
            continue
        out = convert_file(t)
        # 自检：每行必须是合法 JSON
        with out.open("r", encoding="utf-8") as f:
            for i, line in enumerate(f, 1):
                if line.strip() and not line.strip().startswith("#"):
                    json.loads(line)
        print(f"OK {t.name} -> {out.name} ({len(out.read_text(encoding='utf-8').splitlines())} 行)")


if __name__ == "__main__":
    main()
