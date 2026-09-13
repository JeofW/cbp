"""Temporary, exact-preimage source promotion. No network, commits or refs."""
from pathlib import Path
import hashlib,json,subprocess
root=Path.cwd()
expected={
 'runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Common.cs':('ea7c9d09d42c6114beed9f06686fe1f9f4bf4561','4f2b172d92a5cbf9c3744ae014d4073e8846cc19'),
 'runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Retribution.cs':('ead82cc961c69fd6772592235fd5e44af48eae5a','be7e021f03cc68f55c2abf5218984d075b96ab94'),
 'runtime-snapshot/Routines/Singular wotlk/Settings/PaladinSettings.cs':('6a76b4c276a8a76c5d38c71388753925ca88ef3a','de233a32a1f42c0313f7f9facc32fb7bd0787aa6'),
 'Tools/PaladinDecisionRegressionTests/BoundaryFixture.cs':('2bbe755db711866780519b1ff453b92462d8cb2a','571bb1d6f4f8b84aa8d8628781f1cfb6f5097eb9'),
 '.github/workflows/audit-integrated.yml':('fac9ea5121a11f9c3b66a2beaf372d1dd822aa8b','776686edb0cc7aed50671d6cc7a01f72638221ff')}
def blob(p):
 b=p.read_bytes();return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
for name,(before,_) in expected.items():
 if blob(root/name)!=before: raise RuntimeError('Unexpected preimage: '+name)
p=root/'runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Common.cs'
s=p.read_text().replace('public class Common','public partial class Common')
s=s.replace('// This won\'t run, but it\'s here for changes in the future. We NEVER run this method if we\'re mounted.\n                    Spell.BuffSelf("Crusader Aura", ret => StyxWoW.Me.Mounted),','CreatePaladinDispelBehavior(),\n                    CreatePaladinAuraBehavior(),')
s=s.replace('                            Spell.BuffSelf("Concentration Aura", ret => SingularSettings.Instance.Paladin.Aura == PaladinAura.Auto),\n','')
a=s.index('                            Spell.BuffSelf(\n                                "Devotion Aura",');b=s.index('                            // Select seal added',a);s=s[:a]+s[b:]
a=s.index('                    new Decorator(\n                        ret => SingularSettings.Instance.Paladin.Aura != PaladinAura.Auto,')
s=s[:a].rstrip().rstrip(',')+'\n                    )));\n        }\n\n'+s[s.index('        private static Composite CreatePaladinBlessBehavior()',a):]
a=s.index('        private static Composite CreatePaladinBlessBehavior()')
s=s[:a]+'''        private static Composite CreatePaladinBlessBehavior() =>
            CreateSupportBehavior(FindBlessingAction, "Blessing of Kings", "Blessing of Might", "Blessing of Wisdom");
    }
}
'''
s=s.replace('                new PrioritySelector(\n                CreatePaladinDispelBehavior(),','                new Decorator(ret => CanMaintainSupport(), new PrioritySelector(\n                    CreatePaladinDispelBehavior(),')
if 'new Decorator(ret => CanMaintainSupport()' not in s: s=s.replace('                new PrioritySelector(\n                ', '                new Decorator(ret => CanMaintainSupport(), new PrioritySelector(\n                ',1)
s=s.replace('                    )));\n        }','                    ))));\n        }',1);p.write_bytes(s.encode())
p=root/'runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Retribution.cs';s=p.read_text();old='                                  !StyxWoW.Me.HasAura("Forbearance")),\n'
assert s.count(old)==1;s=s.replace(old,old+'                Common.CreatePaladinDispelBehavior(),\n',1);p.write_bytes(s.encode())
p=root/'runtime-snapshot/Routines/Singular wotlk/Settings/PaladinSettings.cs';s=p.read_text(encoding='utf-8-sig');marker='        #region Common\n'
s=s.replace(marker,marker+'''        [Setting]
        [DefaultValue(true)]
        [Category("Common")]
        [DisplayName("Dispel Debuffs")]
        [Description("Use learned Purify/Cleanse for safe removable effects. Disable for encounter-specific assignments.")]
        public bool DispelDebuffs { get; set; }

        [Setting]
        [DefaultValue(true)]
        [Category("Common")]
        [DisplayName("Dispel Party and Raid")]
        [Description("Include visible friendly group members in automatic cleansing. Self cleansing remains independent.")]
        public bool DispelParty { get; set; }

''',1);p.write_bytes(s.encode('utf-8-sig'))
p=root/'Tools/PaladinDecisionRegressionTests/BoundaryFixture.cs';s=p.read_text()+'''\n// The support suite links the actual Common owner; rotation-only tests isolate it.
namespace Singular.ClassSpecific.Paladin
{
    public static class Common
    {
        public static Composite CreatePaladinDispelBehavior() => Fixture.Nothing();
    }
}
''';p.write_bytes(s.encode())
p=root/'.github/workflows/audit-integrated.yml';s=p.read_text().replace('          python -m pip install',"          dotnet run --project Tools/PaladinSupportRegressionTests -c Release *> (Join-Path $out 'PaladinSupport-run.txt')\n          $results += @{ suite='PaladinSupport'; build=0; run=$LASTEXITCODE; game_attached=$false }\n          python -m pip install",1).replace('$results.Count -ne 11','$results.Count -ne 12');p.write_bytes(s.encode())
for name,(_,after) in expected.items():
 if blob(root/name)!=after: raise RuntimeError('Unexpected postimage: '+name)
subprocess.run(['git','diff','--check'],check=True)
# Execute the exact combined step that will be committed, not a smaller substitute.
lines=s.splitlines();start=lines.index('        run: |')+1;body=[]
for line in lines[start:]:
 if line and not line.startswith('          '): break
 body.append(line[10:])
Path('support-combined.ps1').write_text('\n'.join(body))
Path('support-blobs.json').write_text(json.dumps({name:after for name,(_,after) in expected.items()}))
