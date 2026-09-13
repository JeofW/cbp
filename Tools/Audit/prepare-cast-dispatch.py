"""One-use, exact-preimage source preparation; no ref/commit writes or test claims."""
import hashlib
from pathlib import Path

path = Path('runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs')
def blob(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
before = path.read_bytes()
assert blob(before) == '92d963a878d7ffff4d12d3e18d05a3e2f3ed85d1', 'Unexpected source preimage'
text = before.decode('utf-8')
for identifier, guard in [('name', 'string.IsNullOrWhiteSpace(name)'), ('spellId', 'spellId <= 0')]:
    old = f'''                            Logger.Write("Casting " + {identifier} + " on " + onUnit(ret).SafeName());
                            SpellManager.Cast({identifier}, onUnit(ret));'''
    new = f'''                            // Setup may have yielded since the outer predicate. Resolve once
                            // here, and never turn a rejected submission into tree success.
                            if ({guard} || onUnit == null)
                                return RunStatus.Failure;
                            var target = onUnit(ret);
                            if (target == null)
                                return RunStatus.Failure;
                            Logger.Write("Casting " + {identifier} + " on " + target.SafeName());
                            return SpellManager.Cast({identifier}, target)
                                ? RunStatus.Success
                                : RunStatus.Failure;'''
    assert text.count(old) == 1, identifier
    text = text.replace(old, new)
after = text.encode('utf-8')
assert blob(after) == '8f1aae0c1d85d04366049dac7aa786796d18956b', 'Unexpected source postimage'
path.write_bytes(after)
print('Prepared exactly two dispatch actions; production commit/test verification remains required.')
