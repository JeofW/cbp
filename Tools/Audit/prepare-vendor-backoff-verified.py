"""Correct the compiler-identified missing namespace without changing behavior."""
import contextlib, hashlib, io, json, runpy
from pathlib import Path
capture = io.StringIO()
with contextlib.redirect_stdout(capture):
    runpy.run_path('Tools/Audit/prepare-vendor-backoff.py', run_name='__main__')
records = json.loads(capture.getvalue())
p = Path('runtime-snapshot/Bots/WholesomeAutoQuest-master/VendorDataLoader.cs')
def blob(data): return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
assert blob(p.read_bytes()) == 'c0b0ae4b21fc2afebfd1662eb189fa76f28b50fd'
p.write_bytes(p.read_bytes().replace(b'using Styx.WoWInternals;', b'using Styx;\nusing Styx.WoWInternals;'))
assert blob(p.read_bytes()) == 'a4fefb0277fe5e8d02da086d418e5e3f858a4891'
for record in records:
    if record['path'] == p.as_posix():
        record['sha'] = blob(p.read_bytes())
        record['sha256'] = hashlib.sha256(p.read_bytes()).hexdigest()
print(json.dumps(records, indent=2))
