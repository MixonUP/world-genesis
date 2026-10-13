"""Wrap a diagnostic core state in the existing native save envelope, without changing the world."""
import hashlib
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = (root / sys.argv[1]).resolve()
target = (root / sys.argv[2]).resolve()
assert source.is_relative_to(root) and target.is_relative_to(root / 'Builds')
document = json.loads(source.read_text(encoding='utf-8-sig'))
already_wrapped = document.get('Format') == 'WorldGenesis' and 'World' in document
world = document['World'] if already_wrapped else document
assert world.get('Width', 0) > 0 and world.get('Height', 0) > 0, 'Expected world or WorldGenesis save envelope'
target.parent.mkdir(parents=True, exist_ok=True)
if already_wrapped:
    target.write_bytes(source.read_bytes())
else:
    target.write_text(json.dumps({'Schema': 1, 'Format': 'WorldGenesis', 'World': world}, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
receipt = {'source': source.relative_to(root).as_posix(), 'target': target.relative_to(root).as_posix(), 'sourceSHA256': hashlib.sha256(source.read_bytes()).hexdigest(), 'targetSHA256': hashlib.sha256(target.read_bytes()).hexdigest(), 'operation': 'Existing envelope copied unchanged; native validation pending.' if already_wrapped else 'Envelope only; native SaveStore.Validate is still required.'}
(root / 'Evidence/Development' / (target.stem + '-envelope.json')).write_text(json.dumps(receipt, indent=2), encoding='utf-8')
print(json.dumps(receipt))
