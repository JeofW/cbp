"""Extract the actual ready-log production block, before or after owner extraction."""
from pathlib import Path
import hashlib,sys
root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'Tools/QuestObservationBoundaryRegressionTests'))
from extract_owners import extract_block
path=root/'runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs'
s=path.read_text(encoding='utf-8-sig')
marker='        private bool ObserveReadyQuestLog()'
if marker in s:
    # A neighboring method can be inserted without becoming part of this owner.
    # Retain the complete actual declaration/body using the existing bounded
    # lexical extractor; do not silently include unrelated rest/navigation code.
    body=extract_block(s,'private bool ObserveReadyQuestLog()')
    fields='private QuestLogSnapshot _lastReadyQuestSnapshot;'
    wrapper='public void RunReady(){ ObserveReadyQuestLog(); }'
else:
    start=s.index('                var currentReady = new HashSet<int>(')
    end=s.index('                _lastReadyQuestIds = currentReady;',start)+len('                _lastReadyQuestIds = currentReady;')
    body='private void ObserveReadyOriginal(){\n'+s[start:end]+'\n}'
    fields=''
    wrapper='public void RunReady(){ ObserveReadyOriginal(); }'
code='using System;using System.Collections.Generic;using System.Linq;using Styx;using Styx.Logic.Questing;\nnamespace WholesomeAQ { public partial class WholesomeAutoQuest {\nprivate HashSet<int> _lastReadyQuestIds;'+fields+'\npublic int Refreshes;private void RequestRefresh(string reason){Refreshes++;}\n'+body+'\n'+wrapper+'\n} }\n'
out=Path(sys.argv[1]);out.parent.mkdir(parents=True,exist_ok=True);out.write_text(code,encoding='utf-8')
print('Actual ready-log owner SHA256:',hashlib.sha256(path.read_bytes()).hexdigest())
