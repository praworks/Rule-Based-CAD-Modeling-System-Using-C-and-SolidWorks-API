#!/usr/bin/env python3
"""
Basic validator for the JSONL produced by convert_samples_to_jsonl.py

Checks:
 - Each line is valid JSON with `prompt` and `result` keys
 - `result` contains `steps` which is a list
 - Each step has an `op` string and required params for known ops

Usage:
  python validate_samples.py samples.jsonl
"""
import sys
import json
from pathlib import Path


REQUIRED_PARAMS = {
    'rectangle_center': ['w', 'h'],
    'circle_center': ['diameter'],
    'extrude': ['depth']
}


def validate_item(obj):
    if 'prompt' not in obj or 'result' not in obj:
        return False, 'Missing prompt or result'
    res = obj['result']
    if not isinstance(res, dict):
        return False, 'result is not an object'
    steps = res.get('steps')
    if not isinstance(steps, list):
        return False, 'steps missing or not a list'
    for i, s in enumerate(steps):
        if not isinstance(s, dict):
            return False, f'step {i} is not an object'
        op = s.get('op')
        if not isinstance(op, str):
            return False, f'step {i} missing op'
        if op in REQUIRED_PARAMS:
            for p in REQUIRED_PARAMS[op]:
                if p not in s:
                    return False, f'step {i} op={op} missing param {p}'
    return True, 'ok'


def main():
    if len(sys.argv) < 2:
        print('Usage: python validate_samples.py samples.jsonl')
        return 2
    path = Path(sys.argv[1])
    if not path.exists():
        print('File not found:', path)
        return 3
    ok = True
    with path.open('r', encoding='utf-8') as f:
        for idx, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except Exception as e:
                print(f'Line {idx}: invalid json: {e}')
                ok = False
                continue
            valid, msg = validate_item(obj)
            if not valid:
                print(f'Line {idx}: validation failed: {msg}')
                ok = False
            else:
                print(f'Line {idx}: OK')
    return 0 if ok else 1


if __name__ == '__main__':
    raise SystemExit(main())
