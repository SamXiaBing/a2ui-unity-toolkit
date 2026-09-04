import sys
from pathlib import Path

sys.path.insert(0, ".")
from a2ui_jsonl import validate_jsonl, strip_meta, load_text

inc = (
    '{"surfaceUpdate":{"surfaceId":"pet","components":['
    '{"id":"col","component":{"Column":{"children":{"explicitList":["a","media"]},"alignment":"stretch"}}},'
    '{"id":"media","component":{"MediaMiniBar":{"title":{"literalString":"x"}}}}'
    ']}}\n'
    '{"beginRendering":{"surfaceId":"pet","root":"col","catalogId":"c"}}'
)
dm = (
    '{"dataModelUpdate":{"surfaceId":"pet","contents":['
    '{"key":"countdown","valueMap":[{"key":"minutes","valueNumber":15}]}'
    ']}}'
)
gold = strip_meta(load_text(Path("Assets/A2UISchemeA/Tools/a2ui_ollama/fewshot/pet_preference_grow.v0.8.jsonl")))


def check(name, jsonl):
    try:
        validate_jsonl(jsonl)
        print(name, "-> PASS")
    except Exception as e:
        print(name, "-> FAIL:", e)


check("incremental surfaceUpdate (局部+beginRendering)", inc)
check("pure dataModelUpdate (只改数据)", dm)
check("fallback pet gold (首轮完整包)", gold)
