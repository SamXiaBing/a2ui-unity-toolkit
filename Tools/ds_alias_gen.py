#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ds_alias_gen.py — 生成 A2uiAlias.uss

把 DS 设计系统（.ds-*）的样式规则别名到 A2UI 组件类名（.a2ui-*）上，
作用域限定 .ds-root，这样 DS 皮肤挂载点下 a2ui 组件自动获得 ds 视觉，
而 Mapper 代码零改动。DS 原文件保持原样，对方升级后可重新生成本文件。

用法: python ds_alias_gen.py
输入: ../Styles/DS/*.uss
输出: ../Styles/DS/A2uiAlias.uss
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DS_DIR = os.path.normpath(os.path.join(HERE, "..", "Styles", "DS"))
OUT = os.path.join(DS_DIR, "A2uiAlias.uss")

# ds 类 → a2ui 类（一个 ds 类可映射多个 a2ui 类）
MAPPING = {
    ".ds-card": [".a2ui-card"],
    ".ds-btn": [".a2ui-btn"],
    ".ds-btn--primary": [".a2ui-btn--primary"],
    ".ds-btn--secondary": [".a2ui-btn--secondary"],
    ".ds-h1": [".a2ui-text--h1"],
    ".ds-h2": [".a2ui-text--h2"],
    ".ds-h3": [".a2ui-text--h3", ".a2ui-text--h4", ".a2ui-text--h5", ".a2ui-card__title"],
    ".ds-body-1": [".a2ui-text", ".a2ui-card__body"],
    ".ds-body-2": [".a2ui-card__subtitle"],
    ".ds-caption": [".a2ui-text--caption", ".a2ui-image__caption", ".a2ui-checkbox__state"],
    ".ds-input": [".a2ui-textfield"],
    ".ds-tabs": [".a2ui-tabs"],
    ".ds-tab": [".a2ui-tabs__tab"],
    ".ds-chip": [".a2ui-chip", ".a2ui-choice__chip"],
    ".ds-check": [".a2ui-checkbox"],
    ".ds-check__label": [".a2ui-checkbox__heading"],
    ".ds-slider": [".a2ui-slider"],
    ".ds-icon": [".a2ui-icon"],
    ".ds-badge": [".a2ui-token-badge"],
    ".ds-dialog": [".a2ui-modal__content"],
    ".ds-backdrop": [".a2ui-modal__backdrop"],
    ".ds-skeleton": [".a2ui-skeleton"],
    ".ds-empty": [".a2ui-placeholder"],
}

# 长的类名先替换，避免 .ds-btn 吃掉 .ds-btn--primary 的前缀
DS_CLASSES = sorted(MAPPING.keys(), key=len, reverse=True)
CLASS_RE = re.compile(r"\.ds-[a-z0-9_-]+")
RULE_RE = re.compile(r"([^{}]+)\{([^{}]*)\}", re.S)
COMMENT_RE = re.compile(r"/\*.*?\*/", re.S)


def alias_selector(sel):
    """若选择器含映射类，返回别名变体列表（每个含 .ds-root 前缀）。"""
    found = CLASS_RE.findall(sel)
    mapped = [c for c in found if c in MAPPING]
    if not mapped:
        return []
    # 逐目标展开：对每个映射类取它的每个 a2ui 目标做完整替换
    variants = []
    # 收集所有 (ds类 -> a2ui目标) 组合；简单起见只支持"每个类同时替换为同一目标组合"
    # 多数情况一个选择器只有 1~2 个映射类，逐一目标展开：
    targets_per_class = [MAPPING[c] for c in mapped]
    # 以第一个映射类的目标数为主轴（其他类取第一个目标，够用且避免组合爆炸）
    n = max(len(t) for t in targets_per_class)
    for i in range(n):
        v = sel
        for c in mapped:
            targets = MAPPING[c]
            v = v.replace(c, targets[min(i, len(targets) - 1)])
        variants.append(".ds-root " + v.strip())
    return variants


def main():
    out_rules = []
    files = sorted(f for f in os.listdir(DS_DIR)
                   if f.endswith(".uss") and f != "A2uiAlias.uss")
    for fname in files:
        path = os.path.join(DS_DIR, fname)
        with open(path, "r", encoding="utf-8") as fp:
            text = COMMENT_RE.sub("", fp.read())
        file_rules = 0
        for m in RULE_RE.finditer(text):
            sel_text, body = m.group(1), m.group(2).strip()
            if not body:
                continue
            selectors = [s.strip() for s in sel_text.split(",") if s.strip()]
            aliased = []
            for s in selectors:
                aliased.extend(alias_selector(s))
            if aliased:
                out_rules.append((fname, aliased, body))
                file_rules += 1
        print(f"{fname}: {file_rules} 条规则生成别名")

    with open(OUT, "w", encoding="utf-8", newline="\n") as fp:
        fp.write("/* ============================================================\n")
        fp.write(" * A2uiAlias.uss — 由 Tools/ds_alias_gen.py 自动生成，请勿手改\n")
        fp.write(" * 作用：.ds-root 作用域内，把 ds 组件样式别名到 a2ui 类名上\n")
        fp.write(" * ========================================================== */\n\n")
        for fname, selectors, body in out_rules:
            fp.write(f"/* from {fname} */\n")
            fp.write(",\n".join(selectors) + " {\n")
            for line in body.splitlines():
                line = line.strip()
                if line:
                    fp.write("    " + line + "\n")
            fp.write("}\n\n")
    print(f"共 {len(out_rules)} 条别名规则 → {OUT}")


if __name__ == "__main__":
    sys.exit(main())
