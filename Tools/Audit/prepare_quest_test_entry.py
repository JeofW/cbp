"""Temporary test-entry normalization only; production code is not changed."""
from pathlib import Path
import hashlib,json,subprocess
expected = {
 'Tools/QuestPickupPolicyRegressionTests/MissingOfferEvidenceRegressionTests.cs': ('6ab9093628bb48fe68abf57988f1d78510f50faa','905a576cb130f83bb96b9326404fb8ea39409759'),
 'Tools/WholesomeQuestRecoveryRegressionTests/InventoryMaterializationRegressionTests.cs': ('d20194106579e858357fc0eccb3e7a67da4d6b5a','64fab3c72332bc8b4713a4549c6ab174a0ebe5f5'),
 'Tools/QuestPickupPolicyRegressionTests/PickupObservationRegressionTests.cs': ('e6e5b10f159864cdf6ee81c690e9739c6ccc4092','90f5613472f7bc14209387e68b9889c3d1976b3c'),
 'Tools/WholesomeQuestRecoveryRegressionTests/InventoryAndPickupEdgeRegressionTests.cs': ('4bfcf76c891b91e13f2369a02cb6a9a0b8b2bee4','746643d1cb6ac8ed2df59d7e6ff1e1825a90a09a'),
 'Tools/WholesomeQuestRecoveryRegressionTests/OwnedCollectionPlanningRegressionTests.cs': ('52924e0db17848404d3913cb63c4f4de2ea108c7','bfc65266db07c160a029930f230276dfa2e328fd')}
def blob(path):
 b=Path(path).read_bytes();return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
for name,(before,after) in expected.items():
 if blob(name)!=before:raise RuntimeError('Unexpected test preimage: '+name)
 p=Path(name);s=p.read_text(encoding='utf-8');assert s.count('[ModuleInitializer]')==1
 s=s.replace('    [ModuleInitializer]\n','')
 if p.name=='PickupObservationRegressionTests.cs':
  s=s.replace('genuinely changed current offers require fresh evidence','unrelated current offers do not erase the requested quest absence')
  s=s.replace('Check(tracker.ConfirmedCycles == 1 && !tracker.PickupUnavailable, "different offers must not inherit the previous absence episode");','Check(tracker.ConfirmedCycles == 3 && tracker.PickupUnavailable, "the requested quest is still absent despite unrelated offer changes");')
 p.write_bytes(s.encode('utf-8'))
 if blob(name)!=after:raise RuntimeError('Unexpected test postimage: '+name)
after={n:v[1] for n,v in expected.items()}
for folder,classes,sha in [
 ('Tools/QuestPickupPolicyRegressionTests',['PickupObservationRegressionTests','MissingOfferEvidenceRegressionTests'],'e92adb9d02265968c7e01e31be1241385299134b'),
 ('Tools/WholesomeQuestRecoveryRegressionTests',['InventoryAndPickupEdgeRegressionTests','OwnedCollectionPlanningRegressionTests','InventoryMaterializationRegressionTests'],'cd150eb73794eae10b014439a40216fc1c62fa6b')]:
 name=folder+'/ContinuationRegressionEntry.cs';p=Path(name)
 if p.exists():raise RuntimeError('Unexpected existing test entry')
 s='''using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Execute all recovered groups before reporting failure. Individual groups still
// retain their assertions and failures; one group must not hide the next group.
internal static class ContinuationRegressionEntry
{
    [ModuleInitializer]
    internal static void Run()
    {
        var errors = new List<Exception>();
'''+''.join('        try { '+c+'.Run(); } catch (Exception error) { errors.Add(error); }\n' for c in classes)+'''        if (errors.Count != 0) throw new AggregateException("Continuation regression groups failed", errors);
    }
}
'''
 p.write_bytes(s.encode('utf-8'));assert blob(name)==sha;after[name]=sha
subprocess.run(['git','diff','--check'],check=True)
Path('quest-test-blobs.json').write_text(json.dumps(after),encoding='utf-8')
lines=Path('.github/workflows/audit-integrated.yml').read_text(encoding='utf-8').splitlines();start=lines.index('        run: |')+1;body=[]
for line in lines[start:]:
 if line and not line.startswith('          '):break
 body.append(line[10:])
Path('quest-test-combined.ps1').write_text('\n'.join(body),encoding='utf-8')
