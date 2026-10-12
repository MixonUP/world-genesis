"""Wrap a diagnostic core state in the existing native save envelope, without changing the world."""
import hashlib
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = (root / sys.argv[1]).resolve()
target = (root / sys.argv[2]).resolve()
assert source.is_relative_to(root) and target.is_relative_to(root / 'Builds')
world = json.loads(source.read_text(encoding='utf-8-sig'))
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps({'Schema': 1, 'Format': 'WorldGenesis', 'World': world}, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
receipt = {'source': source.relative_to(root).as_posix(), 'target': target.relative_to(root).as_posix(), 'sourceSHA256': hashlib.sha256(source.read_bytes()).hexdigest(), 'targetSHA256': hashlib.sha256(target.read_bytes()).hexdigest(), 'operation': 'Envelope only; native SaveStore.Validate is still required.'}
(root / 'Evidence/Development' / (target.stem + '-envelope.json')).write_text(json.dumps(receipt, indent=2), encoding='utf-8')
print(json.dumps(receipt))
