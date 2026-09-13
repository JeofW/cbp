"""Temporary byte-exact adapter patch; no credentials, network or ref operations."""
from pathlib import Path
import hashlib,json,subprocess
spec = {
 'Styx/Helpers/WoWPlayerExtensions.cs': ('92b270aefa75aa58ea1649dcb6bac41d4a741852','8ae09de2e067f760417255595dfa7fd2993f6e4d',[(b'''Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0)''',b'''LegacyGroupRoles.GetAssignedRole("player")''',3)]),
 'Styx/WoWInternals/WoWObjects/WoWPartyMember.cs': ('28fe6237ed5573ddea86b0d18cbe4189b0b6afcf','67e282799623eac12ed554cfaf158839551c9c6e',[(b'''// UnitGroupRolesAssigned returns "TANK", "HEALER", "DAMAGER", or "NONE"''',b'''// Normalize original 3.3.5a tank/healer/damage flags at the shared owner.''',1),(b'''Lua.GetReturnVal<string>($"return UnitGroupRolesAssigned('{_unitId}')", 0)''',b'''LegacyGroupRoles.GetAssignedRole(_unitId)''',1)]),
 'runtime-snapshot/Bots/CombatBot.cs': ('2806449bececc63eeb541edfcffb497d6ca6bfec','8743e476cab9c1c0468cfaa1d2239821274bae0f',[(b'''Lua.GetReturnVal<string>(string.Format("return UnitGroupRolesAssigned('party{0}')", i), 0)''',b'''LegacyGroupRoles.GetAssignedRole("party" + i)''',1)])}
def blob(b): return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
for name,(before,after,replacements) in spec.items():
 p=Path(name); b=p.read_bytes()
 assert blob(b)==before, 'Unexpected source '+name
 for old,new,count in replacements:
  assert b.count(old)==count,(name,count)
  b=b.replace(old,new)
 assert blob(b)==after, 'Unexpected patched content '+name
 p.write_bytes(b)
subprocess.run(['git','diff','--check'],check=True)
Path('role-blobs.json').write_text(json.dumps({name:v[1] for name,v in spec.items()}))
