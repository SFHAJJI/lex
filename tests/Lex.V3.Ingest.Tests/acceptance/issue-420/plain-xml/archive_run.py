"""Archive a completed canary and independently reopen every archived member.

Usage: python archive_run.py RUN_DIRECTORY OUTPUT_PREFIX
Publisher bytes remain binary archive members, immune to Git newline conversion.
"""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

run = Path(sys.argv[1]).resolve(strict=True)
prefix = Path(sys.argv[2])
index_bytes = (run / 'evidence-index.json').read_bytes()
index = json.loads(index_bytes)
assert index['schema'] == 'lex-lu-live-adapter-canary-evidence/1'
members = []
archive = prefix.with_suffix('.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as target:
    for path in sorted(run.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(run).as_posix()
        content = path.read_bytes()
        digest = hashlib.sha256(content).hexdigest()
        if path.parent.name in ('nightly-floor-90d', 'legal-hold'):
            assert path.name == digest, relative
        target.writestr(relative, content)
        members.append({'path': relative, 'sha256': digest, 'bytes': len(content)})
with zipfile.ZipFile(archive) as reopened:
    assert len(reopened.namelist()) == len(members)
    for member in members:
        content = reopened.read(member['path'])
        assert len(content) == member['bytes'], member['path']
        assert hashlib.sha256(content).hexdigest() == member['sha256'], member['path']
receipt = {
    'schema': 'lex-lu-plain-xml-archive/1',
    'status': index['status'],
    'archive': archive.name,
    'archiveSha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
    'indexSha256': hashlib.sha256(index_bytes).hexdigest(),
    'members': members,
    'limitations': 'Exact archived-byte verification; not production retention or whole Luxembourg scope.'
}
prefix.with_suffix('.json').write_bytes((json.dumps(receipt, indent=2) + '\n').encode())
print(json.dumps({key: value for key, value in receipt.items() if key != 'members'}))
print(f'Reopened and verified {len(members)} archive members')
