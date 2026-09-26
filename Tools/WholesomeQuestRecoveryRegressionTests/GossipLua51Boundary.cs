using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

// Uses the complete owner already compiled by GossipEventLifetimeRegressionTests.
// Only external frame/world observations are controlled. All generated requests
// run in one stock Lua5.1 state with actual lua_tolstring and managed conversion.
internal static class GossipLua51Boundary
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private sealed class Failure(string message) : Exception(message) { }
    internal static void Run(Assembly probe, string repository)
    {
        using var lua = new RewardLua51Boundary.StockLua51(repository);
        Type cases = probe.GetType("GossipLifetimeCases", true)!;
        Type ownerType = probe.GetType("Styx.Bot.Quest_Behaviors.GossipEvent.GossipEvent", true)!;
        Type bridge = probe.GetType("RewardRecordedBridge", true)!;
        FieldInfo observe = bridge.GetField("Observe")!;
        var size = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
        object Owner() => cases.GetField("owner", Hidden)!.GetValue(null)!;
        object? Invoke(string method, params object[] args) => ownerType.GetMethod(method, Hidden)!.Invoke(Owner(), args);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        RewardLua51Boundary.StockLua51.Session? session = null;
        Func<string, List<string>, List<string>>? transport = null;
        List<string> Execute(string script)
        {
            var result = session!.Execute(script, size(script));
            if (result.Load != 0 || result.Call != 0) throw new InvalidOperationException("Lua request failed: " + result.Error);
            return result.Values;
        }
        void Check(bool value, string why) { if (!value) throw new Failure(why); }
        void Case(string label, Action action)
        {
            total++;
            try
            {
                session = lua.BeginSession(Setup);
                transport = null;
                cases.GetMethod("Reset", Hidden)!.Invoke(null, null);
                observe.SetValue(null, new Func<string, List<string>>(script =>
                {
                    List<string> values = Execute(script);
                    return transport == null ? values : transport(script, values);
                }));
                cases.GetMethod("Open", Hidden)!.Invoke(null, new object[] { false });
                action(); passed++; Console.WriteLine("PASS gossip Lua51: " + label);
            }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL gossip Lua51: " + label + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR gossip Lua51: " + label + ": " + e); }
            finally
            {
                observe.SetValue(null, null);
                cases.GetField("OnMenu", Hidden)!.SetValue(null, null);
                session?.Dispose(); session = null;
            }
        }
        void Capture() => Check((bool)Invoke("TryCaptureGossipMenu")!, "healthy capture was rejected");
        void Counts(int selections, int closes)
        {
            var values = Execute("return selections, closes");
            Check(values.SequenceEqual(new[] { selections.ToString(), closes.ToString() }), "mutation counts=" + string.Join("/", values));
        }
        void NotSubmitted() => Check((long)ownerType.GetField("_lastSubmissionUtc", Hidden)!.GetValue(Owner())! < 0, "refused request marked submitted");

        foreach (string operation in new[] { "select", "retry", "defer" })
        {
            string op = operation;
            void Act()
            {
                if (op == "select") Invoke("TickBehavior");
                else if (op == "retry") Invoke("ResetForRetry");
                else Invoke("DeferAuthoritativeAttempt", "controlled");
            }
            Case(op + " unchanged generation", () =>
            {
                Capture(); Act(); Counts(op == "select" ? 1 : 0, op == "select" ? 0 : 1);
                if (op == "select") Check((long)ownerType.GetField("_lastSubmissionUtc", Hidden)!.GetValue(Owner())! >= 0, "healthy submission not retained");
            });
            foreach (var change in new[] {
                (Name:"identical close/reopen", Script:"Emit('GOSSIP_CLOSED'); Emit('GOSSIP_SHOW')"),
                (Name:"identical replacement show", Script:"Emit('GOSSIP_SHOW')"),
                (Name:"world reentry", Script:"Emit('PLAYER_ENTERING_WORLD')"),
                (Name:"missing observer", Script:"CB_GossipObservation=nil"),
                (Name:"replacement observer copying fields", Script:"local old=CB_GossipObservation; if old then local replacement=CreateFrame('Frame'); for k,v in pairs(old) do replacement[k]=v end; CB_GossipObservation=replacement end"),
                (Name:"revoked token", Script:"if CB_GossipObservation then CB_GossipObservation.cbToken=nil end"),
                (Name:"replaced event handler", Script:"if CB_GossipObservation then CB_GossipObservation:SetScript('OnEvent', function() end) end"),
                (Name:"unregistered show event", Script:"if CB_GossipObservation then CB_GossipObservation:UnregisterEvent('GOSSIP_SHOW') end"),
                (Name:"changed greeting", Script:"greeting='Different greeting'"),
                (Name:"changed option text", Script:"options[1]='Different option'"),
                (Name:"changed option type", Script:"options[2]='vendor'"),
                (Name:"changed option order", Script:"options={options[3],options[4],options[1],options[2]}"),
                (Name:"changed available quest", Script:"available[1]='Other quest'"),
                (Name:"changed active completion", Script:"active[4]=true"),
                (Name:"event during final read", Script:"beforeRead=function() Emit('GOSSIP_CLOSED'); Emit('GOSSIP_SHOW') end"),
                (Name:"observer removed during final read", Script:"beforeRead=function() CB_GossipObservation=nil end") })
            {
                var mutation = change;
                Case(op + " " + mutation.Name, () => { Capture(); Execute(mutation.Script); Act(); Counts(0, 0); NotSubmitted(); });
            }
        }
        Case("event during managed option observation", () =>
        {
            Capture();
            bool reached = false;
            cases.GetField("OnMenu", Hidden)!.SetValue(null, new Action(() => { reached = true; Execute("Emit('GOSSIP_SHOW')"); }));
            Invoke("TickBehavior"); Check(reached, "actual option observation not reached"); Counts(0, 0); NotSubmitted();
        });
        Case("event during initial capture", () =>
        {
            Execute("beforeRead=function() Emit('GOSSIP_SHOW') end");
            Check(!(bool)Invoke("TryCaptureGossipMenu")!, "mixed-generation capture accepted");
            Invoke("ResetForRetry"); Counts(0, 0);
        });
        Case("long unicode menu survives complete transport", () =>
        {
            Execute("greeting=string.rep('新しい会話 quote\\\' slash\\\\',70)");
            Capture(); Invoke("TickBehavior"); Counts(1, 0);
        });
        foreach (string fault in new[] { "missing", "short", "length" })
        {
            string f = fault;
            Case("capture transport " + f, () =>
            {
                transport = (script, values) =>
                {
                    if (!script.Contains("return unpack(chunks)", StringComparison.Ordinal)) return values;
                    var broken = values.ToList();
                    if (f == "missing") broken.RemoveAt(broken.Count - 1);
                    else if (f == "short") broken[1] = broken[1].Substring(1);
                    else broken[0] = "1";
                    return broken;
                };
                Check(!(bool)Invoke("TryCaptureGossipMenu")!, "incomplete capture accepted");
                Invoke("ResetForRetry"); Counts(0, 0);
            });
        }
        foreach (string value in new[] { "garbage", "true", "", "2" })
        {
            string malformed = value;
            Case("selection receipt " + malformed, () =>
            {
                Capture();
                transport = (script, values) => script.Contains("SelectGossipOption(", StringComparison.Ordinal) ? new List<string> { malformed } : values;
                Check(!(bool)Invoke("TrySelectGossipOption", 0)!, "malformed receipt accepted");
                // The controlled API did execute. Refusing its malformed receipt
                // must not be described as proof that no request took place.
                Counts(1, 0); NotSubmitted();
            });
        }
        Case("observer reused across retry captures", () =>
        {
            Capture(); Invoke("ResetForRetry");
            cases.GetMethod("Open", Hidden)!.Invoke(null, new object[] { false });
            Execute("shown=true; Emit('GOSSIP_SHOW')"); Capture(); Invoke("TickBehavior");
            Counts(1, 1); Check(Execute("return created").Single() == "1", "observer frame recreated on retry");
        });
        Case("disposed owner does not mutate or recreate observer", () =>
        {
            Capture(); Invoke("Dispose"); Execute("CB_GossipObservation=nil");
            Invoke("TickBehavior"); Invoke("ResetForRetry"); Counts(0, 0);
        });
        Console.WriteLine($"Gossip Lua51 generation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; full tracked owner, persistent Lua5.1/events, actual conversion; controlled world; no original-client/native concurrency/server proof.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Gossip Lua51 generation regression");
    }
    private const string Setup = """
clicks=0; selections=0; closes=0; created=0; shown=true; frames={}
greeting='Source-bound interaction'
options={'Proceed','gossip','Leave','gossip'}
available={'Available quest',10,false,false,false}; active={'Active quest',10,false,false}
function CreateFrame(kind)
 assert(kind=='Frame'); created=created+1
 local frame={events={},scripts={}}
 function frame:RegisterEvent(event) self.events[event]=true end
 function frame:UnregisterEvent(event) self.events[event]=nil end
 function frame:IsEventRegistered(event) return self.events[event] or false end
 function frame:SetScript(name,handler) self.scripts[name]=handler end
 function frame:GetScript(name) return self.scripts[name] end
 frames[#frames+1]=frame; return frame
end
function Emit(event)
 for _,frame in ipairs(frames) do
  if frame.events[event] and frame.scripts.OnEvent then frame.scripts.OnEvent(frame,event) end
 end
end
GossipFrame={IsShown=function() return shown end}
function UnitGUID(kind) return kind=='npc' and '0x0000000000000002' or '0x0000000000000001' end
function GetGossipText() return greeting end
function GetGossipOptions() return unpack(options) end
function GetGossipAvailableQuests() return unpack(available) end
function GetGossipActiveQuests()
 local pending=beforeRead; beforeRead=nil; if pending then pending() end
 return unpack(active)
end
function SelectGossipOption(index) assert(index==1);selections=selections+1;clicks=clicks+1 end
function CloseGossip() closes=closes+1;shown=false;Emit('GOSSIP_CLOSED') end
""";
}
