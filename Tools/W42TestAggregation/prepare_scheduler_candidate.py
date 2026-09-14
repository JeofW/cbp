"""Hash-locked candidate for the source-confirmed scheduler consumer gap."""
import difflib, hashlib, json, pathlib, sys
path = pathlib.Path('runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs')
before = path.read_bytes()
assert hashlib.sha256(before).hexdigest() == 'c603c55f18ed3b8c5f57cb0b57134240d52d63082afa209110322ca8c462b9a9', 'Unexpected scheduler source'
text = before.decode()
def replace(old, new):
    global text
    assert text.count(old) == 1, 'Ambiguous source boundary: ' + old[:80]
    text = text.replace(old, new)
replace('        public bool HasAuthoritativeCompletions { get; init; }', '''        // Explicit pure snapshots retain their complete-input default. The live
        // producer below derives this flag from the actual raw/metadata owner.
        public bool HasCompleteQuestLog { get; init; } = true;
        public bool HasAuthoritativeCompletions { get; init; }''')
replace('            var accepted = me.QuestLog.GetAllQuests()', '''            QuestLog questLog = me.QuestLog;
            QuestLogSnapshot observation = questLog.CaptureSnapshot();
            var accepted = observation.Quests''')
replace('                HasAuthoritativeCompletions = authoritative,', '''                HasCompleteQuestLog = observation.IsComplete,
                HasAuthoritativeCompletions = authoritative,''')
replace('''            QuestScheduleResult candidate = MaterializeSchedule(
''', '''            // Raw observations are samples, not a native transaction/session lease.
            // Recheck the same sample at each fallible publication boundary. The
            // nested lease check also protects replacement work from reentrant
            // player/world observations; an obsolete failure must not revoke it.
            bool TryApplyObserved(Action apply)
            {
                bool applied = false;
                tryApplyPublication(() =>
                {
                    bool current = snapshot.HasCompleteQuestLog && questLog.IsSnapshotCurrent(observation);
                    tryApplyPublication(() =>
                    {
                        if (!current)
                        {
                            InvalidatePublishedWork("Quest observations incomplete or changed; waiting for a fresh scan.");
                            return;
                        }
                        apply();
                        applied = true;
                    });
                });
                return applied;
            }

            // Admission precedes recovery selection/marks and navigation probes.
            if (!TryApplyObserved(() => { }))
                return false;
            QuestScheduleResult candidate = MaterializeSchedule(
''')
replace('            if (!tryApplyPublication(() => candidate = ApplyScanExpansionBeforeFallback(candidate)))', '            if (!TryApplyObserved(() => candidate = ApplyScanExpansionBeforeFallback(candidate)))')
replace('                if (!tryApplyPublication(() => path = _profileBuilder.WriteProfile(xml)))', '                if (!TryApplyObserved(() => path = _profileBuilder.WriteProfile(xml)))')
replace('''            bool published = false;
            tryApplyPublication(() =>''', '''            bool published = false;
            TryApplyObserved(() =>''')
replace('''                tryApplyPublication(() =>
                {
                    _lastRecoveryContext = context;''', '''                TryApplyObserved(() =>
                {
                    _lastRecoveryContext = context;''')
replace('''            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));

            var completed''', '''            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
            if (!snapshot.HasCompleteQuestLog || snapshot.AcceptedQuests == null)
            {
                return new QuestScheduleResult
                {
                    FallbackMode = QuestFallbackMode.TimedIdle,
                    EarliestRetryUtc = snapshot.UtcNow.Add(ScanCooldown),
                    Status = "Quest observations incomplete; waiting for a fresh scan."
                };
            }

            var completed''')
after = text.encode()
path.write_bytes(after)
out = pathlib.Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
(out/'scheduler.diff').write_text(''.join(difflib.unified_diff(before.decode().splitlines(True), text.splitlines(True),fromfile='a/'+str(path),tofile='b/'+str(path))), encoding='utf-8')
(out/'QuestScheduler.cs').write_bytes(after)
(out/'candidate.json').write_text(json.dumps({'path':str(path),'preimage_sha256':hashlib.sha256(before).hexdigest(),'sha256':hashlib.sha256(after).hexdigest(),'blob':hashlib.sha1(b'blob '+str(len(after)).encode()+b'\0'+after).hexdigest(),'game_attached':False},indent=2))
print((out/'candidate.json').read_text())
