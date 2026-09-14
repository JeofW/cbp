"""Temporary, exact-preimage W42 candidate preparation; never edits tests or refs."""
import hashlib
import json
from pathlib import Path
import sys

PREFIX = 'runtime-snapshot/Bots/WholesomeAutoQuest-master/'
EXPECTED = {
    PREFIX+'QuestScheduler.cs': 'c99fab132e5ba3ba52b117a8ff48b6c249421292',
    PREFIX+'WholesomeAutoQuest.cs': '8e92bb6f4b7018e022a5ab335b204aee86db1e4a',
    'Styx/Logic/Profiles/ProfileManager.cs': 'dda92e3f78543d8b7c35740845539d3ab8062a0b',
}

def blob(data):
    return hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()

def once(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError('Expected exactly one patch anchor: '+old[:100])
    return text.replace(old, new, 1)

def prepare(repo):
    sources = {}
    for name, expected in EXPECTED.items():
        data=(repo/name).read_bytes()
        if blob(data) != expected:
            raise RuntimeError('Preimage changed; reconcile instead of overwriting: '+name)
        sources[name] = data.decode('utf-8')
    name=PREFIX+'QuestScheduler.cs';s=sources[name]
    s=once(s, '''        public bool ScanAndRefresh(LocalPlayer me, string validatedGrindProfilePath = null)
        {
''', '''        // Retain the generate-only API for standalone callers. The running bot uses
        // the lease-fenced overload and the host's actual load-acceptance result.
        public bool ScanAndRefresh(LocalPlayer me, string validatedGrindProfilePath = null) =>
            ScanAndRefresh(me, validatedGrindProfilePath, apply => { apply(); return true; }, null);

        internal bool ScanAndRefresh(
            LocalPlayer me,
            string validatedGrindProfilePath,
            Func<System.Action, bool> tryApplyPublication,
            Func<string, bool> tryLoadProfile)
        {
            if (tryApplyPublication == null)
                throw new ArgumentNullException(nameof(tryApplyPublication));
''')
    s=once(s, '                InvalidatePublishedWork("Quest observations unavailable: no player was supplied.");',
             '                tryApplyPublication(() => InvalidatePublishedWork("Quest observations unavailable: no player was supplied."));')
    s=once(s, '''            _lastScan = DateTime.Now;
            QuestDatabase db = _dataLoader.Database;''', '''            int scanThreshold = 0;
            if (!tryApplyPublication(() =>
            {
                _lastScan = DateTime.Now;
                scanThreshold = _scanThreshold;
            }))
                return false;
            QuestDatabase db = _dataLoader.Database;''')
    s=once(s, '                InvalidatePublishedWork("No quest data loaded");',
             '                tryApplyPublication(() => InvalidatePublishedWork("No quest data loaded"));')
    s=once(s, '                InvalidatePublishedWork("Quest observations unavailable: the current player is not ready.");',
             '                tryApplyPublication(() => InvalidatePublishedWork("Quest observations unavailable: the current player is not ready."));')
    s=once(s, '            InvalidatePublishedWork("Refreshing quest observations; prior work is not authorized.");',
             '''            if (!tryApplyPublication(() => InvalidatePublishedWork("Refreshing quest observations; prior work is not authorized.")))
                return false;''')
    s=once(s, '            _lastRecoveryContext = context;\n', '')
    s=once(s, '            LastSchedule = ApplyScanExpansionBeforeFallback(MaterializeSchedule(', '            QuestScheduleResult candidate = MaterializeSchedule(')
    s=once(s, '''                _settings.MaxQuestsPerProfile,
                _scanThreshold,
                _settings.MinQuestLevelOffset,''', '''                _settings.MaxQuestsPerProfile,
                scanThreshold,
                _settings.MinQuestLevelOffset,''')
    start=s.index('                reportDataFailure: outcome => QuestRecoveryManager.Instance.Report(outcome, context)));')
    end=s.index('\n        internal void InvalidatePublishedWork',start)
    s=s[:start]+'''                reportDataFailure: outcome => QuestRecoveryManager.Instance.Report(outcome, context));

            // Preparation can invoke external navigation/player owners. An obsolete
            // continuation must not change a replacement's scan state or output file.
            if (!tryApplyPublication(() => candidate = ApplyScanExpansionBeforeFallback(candidate)))
                return false;

            string path = null;
            bool hasWork = candidate.Selected.Count > 0 || candidate.FallbackMode == QuestFallbackMode.ValidatedGrind;
            if (candidate.Selected.Count > 0)
            {
                string xml = _profileBuilder.BuildProfileXml(
                    candidate.Plan, db, me.ZoneText, me.Name, me.Level, CurrentVendors);
                if (!tryApplyPublication(() => path = _profileBuilder.WriteProfile(xml)))
                    return false;
            }
            else if (candidate.FallbackMode == QuestFallbackMode.ValidatedGrind)
            {
                path = candidate.ValidatedGrindProfilePath;
            }

            bool published = false;
            tryApplyPublication(() =>
            {
                // Null output is the builder's supported no-output mode, not an
                // instruction to reuse the prior profile or authorize its old child.
                if (hasWork && (string.IsNullOrWhiteSpace(path) ||
                    (tryLoadProfile != null && !tryLoadProfile(path))))
                    return;

                // Loading raises synchronous host events. They can stop/restart the
                // bot reentrantly, even while the refresh monitor is held. Recheck the
                // real lease after those events, rather than revoking in a late catch.
                tryApplyPublication(() =>
                {
                    _lastRecoveryContext = context;
                    LastStatus = candidate.Status;
                    LastQuestCount = candidate.Selected.Count;
                    ActiveQuestIds = new HashSet<int>(candidate.Selected.Select(item => (int)item.QuestId));
                    CurrentProfilePath = path;
                    LastSchedule = candidate; // Execution permission is published last.
                    published = true;
                });
            });
            return published && hasWork;
        }
''' + s[end:]
    sources[name]=s
    name=PREFIX+'WholesomeAutoQuest.cs';s=sources[name]
    s=once(s, '''                            scheduler?.InvalidatePublishedWork("Quest observations unavailable; waiting for the current world and data.");''', '''                            _refreshGate.TryApply(lease, () =>
                                scheduler?.InvalidatePublishedWork("Quest observations unavailable; waiting for the current world and data."));''')
    s=once(s, '''                        scheduler.InvalidatePublishedWork("Refreshing quest observations; waiting for vendor and quest data.");
                        _lastScanTime = DateTime.Now;''', '''                        if (!_refreshGate.TryApply(lease, () =>
                        {
                            scheduler.InvalidatePublishedWork("Refreshing quest observations; waiting for vendor and quest data.");
                            _lastScanTime = DateTime.Now;
                        }))
                            return false;''')
    s=once(s, '                            scheduler.CurrentVendors = _vendorLoader.GetNearestVendors', '                            var vendors = _vendorLoader.GetNearestVendors')
    s=once(s, '''                                .Concat(_vendorLoader.GetNearestVendors(StyxWoW.Me, "Train", 2, bl))
                                .ToList();''', '''                                .Concat(_vendorLoader.GetNearestVendors(StyxWoW.Me, "Train", 2, bl))
                                .ToList();
                            if (!_refreshGate.TryApply(lease, () => scheduler.CurrentVendors = vendors))
                                return false;''')
    s=once(s, '''                        refreshed = scheduler.ScanAndRefresh(StyxWoW.Me);''', '''                        refreshed = scheduler.ScanAndRefresh(
                            StyxWoW.Me, null,
                            apply => _refreshGate.TryApply(lease, apply),
                            path => ProfileManager.TryLoadNew(path, true));''')
    s=once(s, '''                            if (!string.IsNullOrWhiteSpace(scheduler.CurrentProfilePath))
                                ProfileManager.LoadNew(scheduler.CurrentProfilePath);
''', '')
    sources[name]=s
    name='Styx/Logic/Profiles/ProfileManager.cs';s=sources[name]
    s=once(s, '\t\tprivate static void LoadProfileForLevel()', '\t\tprivate static Profile? LoadProfileForLevel()')
    s=once(s, '''\t\t\t\tLogging.WriteDebug("Selected sub-profile: {0} (L{1}-{2})", profile.Name, profile.MinLevel, profile.MaxLevel);
\t\t}''', '''\t\t\t\tLogging.WriteDebug("Selected sub-profile: {0} (L{1}-{2})", profile.Name, profile.MinLevel, profile.MaxLevel);
\t\t\treturn profile;
\t\t}''')
    s=once(s, '''\t\tpublic static void LoadNew(string path, bool rememberMe)
\t\t{''', '''\t\tpublic static void LoadNew(string path, bool rememberMe)
\t\t{
\t\t\tTryLoadNew(path, rememberMe);
\t\t}

\t\t/// <summary>
\t\t/// Reports actual compilation and selection acceptance without changing the
\t\t/// existing void LoadNew API. File/parse/cancellation exceptions still propagate.
\t\t/// A profile replaced by a synchronous load event is not this call's success.
\t\t/// </summary>
\t\tpublic static bool TryLoadNew(string path, bool rememberMe)
\t\t{''')
    s=once(s, '''\t\t\tCurrentOuterProfile = new Profile(path, null);
\t\t\tif (!CompileProfileCode(CurrentOuterProfile))
\t\t\t{
\t\t\t\tTreeRoot.Stop();
\t\t\t\treturn;
\t\t\t}
\t\t\tLoadProfileForLevel();''', '''\t\t\tvar candidate = new Profile(path, null);
\t\t\tCurrentOuterProfile = candidate;
\t\t\tif (!ReferenceEquals(_currentOuterProfile, candidate))
\t\t\t\treturn false;
\t\t\tif (!CompileProfileCode(candidate))
\t\t\t{
\t\t\t\tTreeRoot.Stop();
\t\t\t\treturn false;
\t\t\t}
\t\t\tif (!ReferenceEquals(_currentOuterProfile, candidate))
\t\t\t\treturn false;
\t\t\tProfile? selected = LoadProfileForLevel();
\t\t\treturn selected != null && ReferenceEquals(_currentOuterProfile, candidate)
\t\t\t\t&& ReferenceEquals(_currentProfile, selected);''')
    sources[name]=s
    result=[]
    for name,text in sources.items():
        data=text.encode('utf-8');(repo/name).write_bytes(data)
        result.append({'path':name,'preimage_blob':EXPECTED[name],'blob':blob(data),'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)})
    return result

if __name__ == '__main__':
    repo=Path(sys.argv[1]).resolve();out=Path(sys.argv[2]).resolve();out.mkdir(parents=True,exist_ok=True)
    (out/'candidate-blobs.json').write_text(json.dumps(prepare(repo),indent=2)+'\n')
