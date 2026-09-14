"""Prepare three hash-locked candidate files; no network or remote writes."""
from pathlib import Path
import hashlib, json, subprocess, sys
root=Path(sys.argv[1]).resolve(); out=Path(sys.argv[2]).resolve();out.mkdir(parents=True,exist_ok=True)
paths=['Styx/Logic/Questing/QuestLog.cs','Styx/Logic/Questing/QuestLogSnapshot.cs','runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs']
expected={'Styx/Logic/Questing/QuestLog.cs':'cc6c64d1445ac82cda8161accbc1a54adb89e2bea04fdd4e36984df6561c10b5','runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs':'d2db91b93392f325df7bd7f3fce7214a05b48f09351cc0c47faf3f92ee12498a'}
post={'Styx/Logic/Questing/QuestLog.cs':'23cebfa18702de2d08ddb3f4aba7d1d3e2f107f014d4b72429e357b60b8dc51a','Styx/Logic/Questing/QuestLogSnapshot.cs':'bb28e5c2f811d523ca0b54adde58bd3aac61b3a369ef5f2deb2eb06abf75fa08','runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs':'bd8d7eea211159b3742f7359d14aa8157bd234a67f5379f12bdbd22c2dab3ad7'}
for name,digest in expected.items():
    if hashlib.sha256((root/name).read_bytes()).hexdigest()!=digest:raise RuntimeError('Preimage mismatch: '+name)
if (root/paths[1]).exists():raise RuntimeError('Refusing to overwrite existing raw observation owner')
def once(s,old,new):
    if s.count(old)!=1:raise RuntimeError('Expected one source boundary: '+old[:100])
    return s.replace(old,new)
p=root/paths[0];s=p.read_text(encoding='utf-8');s=once(s,'public class QuestLog\n','public partial class QuestLog\n');p.write_text(s,encoding='utf-8',newline='\n')
p=root/paths[2];s=p.read_text(encoding='utf-8')
s=once(s,'        private HashSet<int> _lastReadyQuestIds;','        private HashSet<int> _lastReadyQuestIds;\n        private QuestLogSnapshot _lastReadyQuestSnapshot;')
s=once(s,'        internal void ResetRecoveryLifecycleState()\n        {','        internal void ResetRecoveryLifecycleState()\n        {\n            _lastReadyQuestIds = null;\n            _lastReadyQuestSnapshot = null;')
s=once(s,'        public override void Pulse()\n        {\n            if (_stopped)','        public override void Pulse()\n        {\n            // Do not carry ready-history authority across an observed world/run gap,\n            // including early returns below. This is not a continuous session lease.\n            if (_stopped || !StyxWoW.IsInGame || StyxWoW.Me == null || !TreeRoot.IsRunning)\n            {\n                _lastReadyQuestIds = null;\n                _lastReadyQuestSnapshot = null;\n            }\n            if (_stopped)')
start=s.index('                var currentReady = new HashSet<int>(');end=s.index('                _lastReadyQuestIds = currentReady;',start)+len('                _lastReadyQuestIds = currentReady;')
s=s[:start]+'                if (ObserveReadyQuestLog())\n                    return;'+s[end:]
method='''        private bool ObserveReadyQuestLog()
        {
            var previous = _lastReadyQuestSnapshot;
            var previousReady = _lastReadyQuestIds;
            // Clear before observing so failures/cancellation cannot retain authority.
            _lastReadyQuestSnapshot = null;
            _lastReadyQuestIds = null;
            if (!StyxWoW.IsInGame || StyxWoW.Me == null)
                return false;

            var current = StyxWoW.Me.QuestLog.CaptureSnapshot();
            if (!current.IsIdentityComplete)
                return false;

            _lastReadyQuestSnapshot = current;
            _lastReadyQuestIds = new HashSet<int>(current.ReadyQuestIds.Select(id => (int)id));
            if (previous == null || previousReady == null || !previous.HasSameOwner(current))
                return false;

            var accepted = new HashSet<uint>(current.AcceptedQuestIds);
            var departed = previousReady.Where(id => !accepted.Contains((uint)id)).ToList();
            if (departed.Count == 0)
                return false;

            // A raw departure requests fresh scheduling. It does not prove that a
            // server turn-in succeeded or grant completed-quest history authority.
            Log($"Previously ready quest(s) left the observed log: {string.Join(",", departed)} — triggering rescan");
            RequestRefresh("Previously ready quest left the observed log; queuing one scheduler rebuild.");
            return true;
        }

'''
s=once(s,'        private void ObserveRecoveryActivation()',method+'        private void ObserveRecoveryActivation()');p.write_text(s,encoding='utf-8',newline='\n')
(root/paths[1]).write_bytes((Path(__file__).parent/'QuestLogSnapshot.cs.txt').read_bytes())
subprocess.run(['git','add','-N','--',paths[1]],cwd=root,check=True)
manifest=[]
for name in paths:
    data=(root/name).read_bytes();digest=hashlib.sha256(data).hexdigest()
    if digest!=post[name]:raise RuntimeError('Postimage mismatch: '+name)
    manifest.append({'path':name,'sha256':digest,'blob':hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()})
(out/'candidate-blobs.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
(out/'source-commit.txt').write_text(subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True),encoding='utf-8')
print('Prepared three exact candidate sources; no tests or remote refs changed.')
