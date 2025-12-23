#!/usr/bin/env python3
"""
Convert a plaintext sample file (Prompt/Result pairs) into JSONL lines.

Usage:
  python convert_samples_to_jsonl.py path/to/Sample.txt out.jsonl

If no args provided, defaults to the `Dont Delete/Sample.txt` file in workspace.
"""
import sys
import json
import re
from pathlib import Path


def extract_pairs(text):
    # Find occurrences of 'Prompt:' followed by 'Result:' and JSON
    pairs = []
    # Regex to find 'Prompt:' lines
    prompt_re = re.compile(r"^Prompt:\s*(.*)$", re.MULTILINE)
    for m in prompt_re.finditer(text):
        prompt = m.group(1).strip()
        # Find 'Result:' after this position
        res_idx = text.find('\nResult:', m.end())
        if res_idx == -1:
            continue
        # start of JSON is after 'Result:' newline
        json_start = text.find('{', res_idx)
        if json_start == -1:
            continue
        # find matching brace by simple stack
        i = json_start
        depth = 0
        while i < len(text):
            if text[i] == '{':
                depth += 1
            elif text[i] == '}':
                depth -= 1
                if depth == 0:
                    json_end = i + 1
                    break
            i += 1
        else:
            continue
        json_text = text[json_start:json_end]
        try:
            obj = json.loads(json_text)
        except Exception:
            # try to fix common issues: replace fancy spaces
            try:
                cleaned = json_text.replace('\u00A0', ' ')
                obj = json.loads(cleaned)
            except Exception:
                obj = None
        pairs.append((prompt, obj))
    return pairs


def main():
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent / 'Dont Delete' / 'Sample.txt'
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).parent / 'samples.jsonl'
    if not src.exists():
        print('Source file not found:', src)
        return 2
    text = src.read_text(encoding='utf-8')
    pairs = extract_pairs(text)
    if not pairs:
        print('No pairs found in', src)
        return 3
    with out.open('w', encoding='utf-8') as f:
        for prompt, obj in pairs:
            line = {'prompt': prompt, 'result': obj}
            f.write(json.dumps(line, ensure_ascii=False) + '\n')
    print(f'Wrote {len(pairs)} items to {out}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
