"""Compile the exact current sale method, not a handwritten behavior model."""
from pathlib import Path
import hashlib,sys
root=Path(__file__).resolve().parents[2]
path=root/'runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs'
source=path.read_text(encoding='utf-8-sig')
start=source.index('        private void SellByQuality()')
end=source.index('        private static string FindProfilePath()',start)
method=source[start:end].strip()
if source.count('private void SellByQuality()')!=1 or not method.endswith('}'):
    raise RuntimeError('Sale method extraction is ambiguous')
body='using System; using System.Collections.Generic; using System.Linq; using Styx; using Styx.Logic.Profiles;\nnamespace WholesomeAQ { public partial class WholesomeAutoQuest {\n'+method+'\n} }\n'
out=Path(sys.argv[1]); out.parent.mkdir(parents=True,exist_ok=True);out.write_text(body,encoding='utf-8')
print('Actual SaleByQuality source SHA-256:',hashlib.sha256(path.read_bytes()).hexdigest())
