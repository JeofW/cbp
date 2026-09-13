"""Temporary exact-content repair; emits one verified source identity."""
from pathlib import Path
import hashlib, json
p = Path('runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs')
def blob(data): return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
assert blob(p.read_bytes()) == 'b2899a46fa9966a4d8f96002ede8516215757827'
s = p.read_bytes().decode('utf-8')
for old, new, count in [
    ('_restingPaused || StyxWoW.Me.IsResting ||', '_restingPaused ||', 1),
    ('_restingPaused || me?.IsResting == true ||', '_restingPaused ||', 1),
    ('_restingPaused || me.IsResting ||', '_restingPaused ||', 2),
]:
    assert s.count(old) == count
    s = s.replace(old, new)
p.write_bytes(s.encode('utf-8'))
assert blob(p.read_bytes()) == 'a03f3eb9c20fe47e2b51aad5c77762149590659f'
print(json.dumps({'path': p.as_posix(), 'sha': blob(p.read_bytes()), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}))
