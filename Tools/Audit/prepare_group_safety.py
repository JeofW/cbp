"""Temporary exact-source preflight. Does not write commits or references."""
from pathlib import Path
import hashlib,json,subprocess
root=Path.cwd()
expected={
'Styx/Logic/Combat/LegacySpellManager.cs':('73f23e690479fc037ceeccda68d226e26bc9ad5f','8fb6cd3ee2ac05e46f093fabe1d0daf59e152ffc'),
'runtime-snapshot/Bots/CombatBot.cs':('b803c4f45be28d4330bc153571c9fd08de020ed6','2806449bececc63eeb541edfcffb497d6ca6bfec'),
'runtime-snapshot/Routines/Singular wotlk/Helpers/Common.cs':('0a716cc199bf063d5aa185f2b7d7ba14824ff5af','f3a79862ec86f2699d55451061dab76c55a2f0a9'),
'runtime-snapshot/Routines/Singular wotlk/Helpers/DungeonEngagementPolicy.cs':('48245236c6098a3109703aeb3ea822f20c8ad579','0d1761071db1f14f6d4118b8b85f55783677ff77'),
'runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs':('8f1aae0c1d85d04366049dac7aa786796d18956b','dbfd158726d2e70805a09ac137ccf8a9f83f0248'),
'runtime-snapshot/Routines/Singular wotlk/Helpers/Unit.cs':('44f6b015c3dd0d21815c73d9119a1c0863a8562d','906cc602dd2c57e4d86bf21e1577f23657d2b55a'),
'.github/workflows/audit-integrated.yml':('fac9ea5121a11f9c3b66a2beaf372d1dd822aa8b','eeed5a6b35115b044869059bc0531e8eae29d0aa'),
'Tools/GroupEngagementRegressionTests/BoundaryFixture.cs':('79c7282de7fca2c130ba073e1c6c03bbeec572ec','459806d90232358ef6d61e44a70e6e9bd4714585')}
def blob(p):
 b=p.read_bytes();return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
for name,(before,_) in expected.items():
 if blob(root/name)!=before:raise RuntimeError('Unexpected preimage: '+name)
def load(n):return (root/n).read_text(encoding='utf-8-sig')
def save(n,s):
 p=root/n;encoding='utf-8-sig' if p.read_bytes().startswith(b'\xef\xbb\xbf') else 'utf-8';p.write_bytes(s.encode(encoding))
n='runtime-snapshot/Routines/Singular wotlk/Helpers/Unit.cs';s=load(n);a=s.index('        public static bool IsDungeonCombatBotTargetingRestricted');b=s.index('        public static bool IsAreaEffectSafe(',a)
s=s[:a]+'''        public static bool IsDungeonCombatBotTargetingRestricted => GroupCombatSafety.IsRestricted;

        public static bool IsEligibleDungeonCombatTarget(this WoWUnit unit) => GroupCombatSafety.MayAttack(unit);

        // Recheck at dispatch after dismount/face/setup has yielded. Friendly support
        // remains possible without a hostile target; self-centered AoE is still checked.
        public static bool IsCombatActionSafe(string spellName, WoWUnit target)
        {
            if (!IsDungeonCombatBotTargetingRestricted) return true;
            return target != null
                && (target.IsMe || target.IsFriendly || target.IsEligibleDungeonCombatTarget())
                && IsAreaEffectSafe(spellName, target);
        }

        public static bool IsCombatActionSafe(int spellId, WoWUnit target)
        {
            if (!IsDungeonCombatBotTargetingRestricted) return true;
            var spell = WoWSpell.FromId(spellId);
            return spell != null && IsCombatActionSafe(spell.Name, target);
        }

'''+s[b:];save(n,s)
n='runtime-snapshot/Routines/Singular wotlk/Helpers/DungeonEngagementPolicy.cs';s=load(n).replace('targetsPartyMember || targetsRaidMember || taggedByMe || isAssistTarget;','targetsPartyMember || targetsRaidMember || taggedByMe;');save(n,s)
n='runtime-snapshot/Bots/CombatBot.cs';s=load(n);a=s.index('        private static bool IsEligibleDungeonTarget(');b=s.index('        #endregion',a)
s=s[:a]+'''        private static bool IsEligibleDungeonTarget(WoWUnit unit) => GroupCombatSafety.MayAttack(unit);

'''+s[b:]
s=s.replace('if (target == null)\n                return false;','if (target == null || !IsEligibleDungeonTarget(target))\n                return false;',1)
s=s.replace('new Decorator(ctx => RoutineManager.Current.CombatBehavior != null,','new Decorator(ctx => IsEligibleDungeonTarget(StyxWoW.Me.CurrentTarget) && RoutineManager.Current.CombatBehavior != null,')
s=s.replace('new Action(ret => RoutineManager.Current.Combat())))','''new Action(ret =>
                                {
                                    if (!IsEligibleDungeonTarget(StyxWoW.Me.CurrentTarget)) return RunStatus.Failure;
                                    RoutineManager.Current.Combat();
                                    return RunStatus.Success;
                                })))''');save(n,s)
n='runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs';s=load(n)
s=s.replace('requirements(ret) && Unit.IsAreaEffectSafe(name, target)','requirements(ret) && Unit.IsCombatActionSafe(name, target)')
s=s.replace('target != null && requirements(ret) && SpellManager.CanCast(spellId, target, true)','target != null && requirements(ret) && Unit.IsCombatActionSafe(spellId, target) && SpellManager.CanCast(spellId, target, true)')
s=s.replace('if (target == null)\n                                return RunStatus.Failure;\n                            Logger.Write("Casting " + name','if (target == null || !Unit.IsCombatActionSafe(name, target))\n                                return RunStatus.Failure;\n                            Logger.Write("Casting " + name',1)
s=s.replace('if (target == null)\n                                return RunStatus.Failure;\n                            Logger.Write("Casting " + spellId','if (target == null || !Unit.IsCombatActionSafe(spellId, target))\n                                return RunStatus.Failure;\n                            Logger.Write("Casting " + spellId',1);save(n,s)
n='runtime-snapshot/Routines/Singular wotlk/Helpers/Common.cs';s=load(n)
s=s.replace('                            StyxWoW.Me.ToggleAttack();','                            if (!GroupCombatSafety.MayAttackCurrentTarget()) return RunStatus.Failure;\n                            StyxWoW.Me.ToggleAttack();',1)
s=s.replace('                            PetManager.CastPetAction("Attack");','                            if (!GroupCombatSafety.MayAttackCurrentTarget()) return RunStatus.Failure;\n                            PetManager.CastPetAction("Attack");',1)
s=s.replace('new Action(ret => SpellManager.Cast("Shoot"))','''new Action(ret =>
                    {
                        if (!GroupCombatSafety.MayAttackCurrentTarget()) return RunStatus.Failure;
                        return SpellManager.Cast("Shoot") ? RunStatus.Success : RunStatus.Failure;
                    })''',1);save(n,s)
n='Styx/Logic/Combat/LegacySpellManager.cs';s=load(n);a=s.index('            // In WotLK 3.3.5a, ground-targeted spells');b=s.index('\n        }',a)
s=s[:a]+'''            // Reuse the existing original-client terrain-click implementation.
            // Cursor/player camera commands do not encode the requested location.
            SpellManager.ClickRemoteLocation(location);'''+s[b:];save(n,s)
n='Tools/GroupEngagementRegressionTests/BoundaryFixture.cs';s=load(n).replace('public sealed class WoWSpell { public string Name','public sealed class WoWSpell { public static WoWSpell FromId(int id) => new() { Name = id == 53385 ? "Divine Storm" : "Crusader Strike" }; public string Name');save(n,s)
n='.github/workflows/audit-integrated.yml';s=load(n).replace('          python -m pip install',"          dotnet run --project Tools/GroupEngagementRegressionTests -c Release *> (Join-Path $out 'GroupEngagement-run.txt')\n          $results += @{ suite='GroupEngagement'; build=0; run=$LASTEXITCODE; game_attached=$false }\n          python -m pip install",1).replace('$results.Count -ne 11','$results.Count -ne 12');save(n,s)
for name,(_,after) in expected.items():
 if blob(root/name)!=after:raise RuntimeError('Unexpected postimage: '+name)
subprocess.run(['git','diff','--check'],check=True)
lines=s.splitlines();start=lines.index('        run: |')+1;body=[]
for line in lines[start:]:
 if line and not line.startswith('          '):break
 body.append(line[10:])
Path('group-combined.ps1').write_text('\n'.join(body),encoding='utf-8')
Path('group-blobs.json').write_text(json.dumps({n:a for n,(_,a) in expected.items()}),encoding='utf-8')
