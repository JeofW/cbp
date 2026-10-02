using System;
using System.Globalization;

namespace Styx.Logic.Combat
{
    /// <summary>Original 3.3.5 event fields; private bounded queue with explicit managed acknowledgement.</summary>
    internal static class RecoveryActionLua
    {
        internal static string Install(string token, ulong actor) => Header(token, actor) + InstallBody;
        internal static string Poll(string token, ulong actor, long after)
        {
            if (after < 0 || after > 9007199254740991L) throw new ArgumentOutOfRangeException(nameof(after));
            return Header(token, actor) + "local after=" + after.ToString(CultureInfo.InvariantCulture) + "; " + PollBody;
        }
        internal static string Dispose(string token)
        {
            ValidateToken(token);
            return "local s=__CBRecoveryObserved335; if type(s)=='table' and s.tag=='CBRecovery335/v1' and s.token=='"
                + token + "' and s.frame then s.frame:UnregisterAllEvents(); s.frame:SetScript('OnEvent',nil); "
                + "if __CBRecoveryObserved335==s then s.ready=false end; return 'recovery-disposed','"
                + token + "' end return 'unavailable','collector owner changed'";
        }
        internal static string SpellIdentity(int spell, ulong actor)
        {
            if (spell <= 0) throw new ArgumentOutOfRangeException(nameof(spell));
            return ActorHeader(actor) + "local id=" + spell.ToString(CultureInfo.InvariantCulture) + "; " + """
local name,rank,icon,cost,funnel,power,cast=GetSpellInfo(id); local now=GetTime()
if not text(name,false) or not text(rank,true) or not finite(cast) or not finite(now) or not current() then
 return 'unavailable','spell identity or cast time unavailable'
end
return 'recovery-spell',id,actor,name,rank,cast,now
""";
        }
        internal static string ItemSnapshot(uint entry, ulong actor)
        {
            if (entry == 0) throw new ArgumentOutOfRangeException(nameof(entry));
            return ActorHeader(actor) + "local id=" + entry.ToString(CultureInfo.InvariantCulture) + "; " + """
local name=GetItemInfo(id); local count=GetItemCount(id,false); local start,duration,enabled=GetItemCooldown(id); local now=GetTime()
if not text(name,false) or not integer(count) or not finite(start) or not finite(duration) or not finite(now)
 or start>now or not finite(start+duration) or (enabled~=0 and enabled~=1) or not current() then
 return 'unavailable','item identity, stock or cooldown unavailable'
end
return 'recovery-item',id,actor,count,start,duration,enabled,now
""";
        }

        private static string Header(string token, ulong actor)
        {
            ValidateToken(token);
            return ActorHeader(actor) + "local token='" + token + "'; ";
        }
        private static string ActorHeader(ulong actor)
        {
            if (actor == 0) throw new ArgumentOutOfRangeException(nameof(actor));
            return Validation + "\nlocal actor='0x" + actor.ToString("x16", CultureInfo.InvariantCulture)
                + "'; local function current() local g=UnitGUID('player'); return guid(g) and string.lower(g)==actor end; "
                + "if not current() then return 'unavailable','actor ownership unavailable' end; ";
        }
        private static void ValidateToken(string token)
        {
            if (token == null || token.Length != 32) throw new ArgumentException("A complete collector token is required", nameof(token));
            foreach (char c in token)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))
                    throw new ArgumentException("A hexadecimal collector token is required", nameof(token));
        }
        private const string Validation = """
local function finite(v) return type(v)=='number' and v==v and v>=0 and v<math.huge end
local function integer(v) return finite(v) and v<=9007199254740991 and v==math.floor(v) end
local function guid(v) return type(v)=='string' and #v>2 and #v<=18 and string.match(string.lower(v),'^0x%x+$') and string.match(string.sub(v,3),'[1-9a-fA-F]')~=nil end
local function text(v,empty) return type(v)=='string' and #v<=256 and (empty or #v>0) end
""";
        private const string InstallBody = """
local now=GetTime(); if not finite(now) then return 'unavailable','client clock unavailable' end
local old=__CBRecoveryObserved335
if old~=nil then
 if type(old)~='table' or old.tag~='CBRecovery335/v1' or not old.frame then return 'unavailable','foreign collector namespace' end
 if old.token==token and old.actor==actor and old.ready then
  if not integer(old.seq) or not integer(old.lost) or not finite(old.clock) or now<old.clock then return 'unavailable','collector clock or sequence unavailable' end
  return 'recovery-events',token,now,old.seq,old.lost,0
 end
 old.frame:UnregisterAllEvents(); old.frame:SetScript('OnEvent',nil)
end
local frame=old and old.frame or CreateFrame('Frame'); local s={tag='CBRecovery335/v1',token=token,actor=actor,frame=frame,q={},seq=0,lost=0,ack=0,clock=now,ready=false}
local function lost() s.lost=math.min(s.lost+1,9007199254740991) end
local function record(event,...)
 if __CBRecoveryObserved335~=s or not s.ready then return end
 local at=GetTime(); if not finite(at) or at<s.clock or not current() then lost(); return end
 s.clock=at
 local kind,name,rank,counter,source,target,spell,amount,overheal
 if event=='COMBAT_LOG_EVENT_UNFILTERED' then
  local stamp,subevent,src,srcName,srcFlags,dst,dstName,dstFlags,id,spellName,school,heal,over=...
  if subevent~='SPELL_HEAL' then return end
  if not guid(src) then lost(); return end
  if string.lower(src)~=actor then return end
  if not guid(dst) or not integer(id) or id<=0 or id>2147483647 or not finite(heal) or not finite(over) then lost(); return end
  kind='HEAL'; name=''; rank=''; counter=0; source=actor; target=string.lower(dst); spell=id; amount=heal; overheal=over
 else
  local unit,n,r,id=...
  if unit~='player' then return end
  if not text(n,false) or not text(r,true) or not integer(id) then lost(); return end
  if event=='UNIT_SPELLCAST_START' then kind='START'
  elseif event=='UNIT_SPELLCAST_SUCCEEDED' then kind='SUCCEEDED'
  elseif event=='UNIT_SPELLCAST_FAILED' then kind='FAILED'
  elseif event=='UNIT_SPELLCAST_INTERRUPTED' then kind='INTERRUPTED'
  else return end
  name=n; rank=r; counter=id; source=actor; target=''; spell=0; amount=0; overheal=0
 end
 if s.seq>=9007199254740991 then lost(); return end
 s.seq=s.seq+1
 if #s.q>=128 then table.remove(s.q,1); lost() end
 s.q[#s.q+1]={s.seq,at,kind,name,rank,counter,source,target,spell,amount,overheal}
end
if not current() or __CBRecoveryObserved335~=old then frame:UnregisterAllEvents(); frame:SetScript('OnEvent',nil); return 'unavailable','owner changed while installing collector' end
__CBRecoveryObserved335=s
local ok=pcall(function()
 frame:SetScript('OnEvent',function(self,event,...) if self~=s.frame then return end; local handled=pcall(record,event,...); if not handled then lost() end end)
 for _,event in ipairs({'UNIT_SPELLCAST_START','UNIT_SPELLCAST_SUCCEEDED','UNIT_SPELLCAST_FAILED','UNIT_SPELLCAST_INTERRUPTED','COMBAT_LOG_EVENT_UNFILTERED'}) do frame:RegisterEvent(event) end
end)
if not ok or not current() or __CBRecoveryObserved335~=s then
 frame:UnregisterAllEvents(); frame:SetScript('OnEvent',nil)
 return 'unavailable','collector registration unavailable or replaced'
end
s.ready=true
return 'recovery-events',token,now,0,0,0
""";
        private const string PollBody = """
local s=__CBRecoveryObserved335; local now=GetTime()
if type(s)~='table' or __CBRecoveryObserved335~=s or s.tag~='CBRecovery335/v1' or not s.ready or s.token~=token or s.actor~=actor or type(s.q)~='table'
 or not finite(now) or not finite(s.clock) or now<s.clock or not integer(s.seq) or not integer(s.lost)
 or not integer(s.ack) or after<s.ack or after>s.seq then return 'unavailable','collector owner or sequence unavailable' end
-- Only acknowledge a previous complete managed read. Returning the next batch
-- does not consume it: a failed native read can request these same events again.
while s.q[1] and s.q[1][1]<=after do table.remove(s.q,1) end
s.ack=after
local count=math.min(#s.q,5); local values={'recovery-events',token,now,s.seq,s.lost,count}
for i=1,count do for field=1,11 do values[#values+1]=s.q[i][field] end end
if not current() or __CBRecoveryObserved335~=s then return 'unavailable','owner changed during collector read' end
s.clock=now
return unpack(values,1,6+count*11)
""";
    }
}
