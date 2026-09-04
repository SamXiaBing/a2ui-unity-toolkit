import sys
sys.path.insert(0, 'Assets/A2UISchemeA/Tools')
from a2ui_jsonl import validate_jsonl, strip_meta, load_text
from pathlib import Path

files = [
    'Assets/A2UISchemeA/Tools/a2ui_ollama/fewshot/pet_preference_grow.v0.8.jsonl',
    'Assets/A2UISchemeA/Tools/a2ui_ollama/fewshot/pet_incremental.v0.8.jsonl',
]
for f in files:
    try:
        validate_jsonl(strip_meta(load_text(Path(f))))
        print('PASS', f)
    except Exception as e:
        print('FAIL', f, repr(e))
