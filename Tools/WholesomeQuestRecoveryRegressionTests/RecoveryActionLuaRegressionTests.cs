using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual generated collector/query scripts and observed-return wrapper execute in
// stock Lua5.1. Event arguments and client API replies are controlled leaves.
internal static class RecoveryActionLuaRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string path = Path.Combine(root, "Styx/Logic/Combat/RecoveryActionLua.cs");
        if (!File.Exists(path)) throw new InvalidOperationException("Private recovery event collector is absent; structural gate only.");
        var lua = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"))).GetRoot();
        string wrapper = lua.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "BuildObservedReturnScript").ToFullString();
        string bridge = "using System;using System.Collections.Generic;namespace Styx.Logic.Combat {public static class RecoveryLuaBridge {"
            + wrapper + "public static string Wrap(string s)=>BuildObservedReturnScript(s);"
            + "public static bool Parse(IReadOnlyList<string> values,string token)=>RecoveryActionEvidence.TryParseBatch(values,token,out _);}}";
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compile = CSharpCompilation.Create("RecoveryLua_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(path)),
                CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/Combat/RecoveryActionEvidence.cs"))),
                CSharpSyntaxTree.ParseText(bridge) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compile.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        Type builders = assembly.GetType("Styx.Logic.Combat.RecoveryActionLua", true)!;
        var wrap = (Func<string, string>)assembly.GetType("Styx.Logic.Combat.RecoveryLuaBridge", true)!.GetMethod("Wrap")!.CreateDelegate(typeof(Func<string, string>));
        var parse = (Func<IReadOnlyList<string>, string, bool>)assembly.GetType("Styx.Logic.Combat.RecoveryLuaBridge", true)!.GetMethod("Parse")!
            .CreateDelegate(typeof(Func<IReadOnlyList<string>, string, bool>));
        string Build(string name, params object[] args) => (string)builders.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        const string token = "0123456789abcdef0123456789abcdef", next = "abcdef0123456789abcdef0123456789";
        int total = 0, passed = 0, failed = 0, unexpected = 0;
        using var runtime = new RewardLua51Boundary.StockLua51(root);
        void Case(string name, Action<RewardLua51Boundary.StockLua51.Session> body)
        {
            total++;
            using var session = runtime.BeginSession(Setup);
            try { body(session); passed++; }
            catch (Failure error) { failed++; Console.Error.WriteLine("FAIL recovery Lua: " + name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR recovery Lua: " + name + ": " + error); }
        }
        List<string> Query(RewardLua51Boundary.StockLua51.Session s, string text)
        {
            string query = wrap(text);
            var reply = s.Execute(query, (uint)Encoding.UTF8.GetByteCount(query));
            Check(reply.Load == 0 && reply.Call == 0, "actual Lua request failed: " + reply.Error);
            return reply.Values;
        }
        void Start(RewardLua51Boundary.StockLua51.Session s)
            => Check(parse(Query(s, Build("Install", token, 1UL)), token), "collector did not establish a complete baseline");
        List<string> Poll(RewardLua51Boundary.StockLua51.Session s, long after = 0)
        {
            var values = Query(s, Build("Poll", token, 1UL, after));
            Check(parse(values, token), "collector output failed the actual parser");
            return values;
        }
        Case("new collector establishes one owned frame", s => { Start(s); Start(s); Check(Query(s, "return #frames")[0] == "1", "repeated install duplicated callbacks"); });
        Case("original cast and healing fields reach the parser", s =>
        {
            Start(s);
            Query(s, "Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',42); Emit('UNIT_SPELLCAST_SUCCEEDED','player','Holy Light','Rank 1',42); Emit('COMBAT_LOG_EVENT_UNFILTERED',100,'SPELL_HEAL',playerGuid,'Player',0,targetGuid,'Target',0,101,'Holy Light',2,500,0)");
            var v = Poll(s); Check(v[5] == "3" && v[8] == "START" && v[11] == "42" && v[19] == "SUCCEEDED" && v[30] == "HEAL" && v[35] == targetGuid && v[36] == "101" && v[37] == "500", "original event argument positions changed");
        });
        Case("unranked spells retain a known empty rank", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_START','player','Unranked','',42)"); var v=Poll(s); Check(v[10]=="", "empty rank was changed"); });
        Case("SENT and STOP are not completion evidence", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_SENT','player','Holy Light','Rank 1','Target'); Emit('UNIT_SPELLCAST_STOP','player','Holy Light','Rank 1',42)"); Check(Poll(s)[5]=="0", "non-authoritative cast events entered the collector"); });
        Case("another actor's spell event is ignored", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_START','target','Holy Light','Rank 1',42)"); Check(Poll(s)[5]=="0", "foreign cast collected"); });
        Case("another actor's heal is ignored", s => { Start(s); Query(s, "Emit('COMBAT_LOG_EVENT_UNFILTERED',100,'SPELL_HEAL',targetGuid,'Target',0,playerGuid,'Player',0,101,'Holy Light',2,500,0)"); Check(Poll(s)[5]=="0", "foreign healer acknowledged our action"); });
        Case("periodic healing is not direct cast acknowledgement", s => { Start(s); Query(s, "Emit('COMBAT_LOG_EVENT_UNFILTERED',100,'SPELL_PERIODIC_HEAL',playerGuid,'Player',0,targetGuid,'Target',0,101,'Holy Light',2,500,0)"); Check(Poll(s)[5]=="0", "periodic tick was treated as a direct heal"); });
        foreach (string args in new[] { "'player','Holy Light',nil,42", "'player','Holy Light','Rank 1',nil", "'player','Holy Light','Rank 1',0/0", "'player','Holy Light','Rank 1',1.5", "'player',string.rep('x',257),'',42" })
            Case("malformed cast inputs / " + args, s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_START',"+args+")"); var v=Poll(s); Check(v[4]=="1"&&v[5]=="0", "missing metadata became an apparently complete observation"); });
        Case("zero cast counter is retained as known but uncorrelated", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_SUCCEEDED','player','Holy Light','Rank 1',0)"); Check(Poll(s)[11]=="0", "collector invented a cast counter"); });
        Case("a failed reply does not consume queued evidence", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',42)"); var first=Poll(s); Check(Poll(s).SequenceEqual(first), "read removed events before managed acknowledgement"); });
        Case("acknowledged sequence advances without a duplicate", s => { Start(s); Query(s, "for i=1,7 do Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',i) end"); var first=Poll(s); Check(first[5]=="5"&&first.Count==61,"return-vector bound changed"); var second=Poll(s,5); Check(second[5]=="2"&&second[6]=="6","known processed sequence was not acknowledged"); });
        Case("queue overflow retains a counted observation gap", s => { Start(s); Query(s, "for i=1,140 do Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',i) end"); var v=Poll(s); Check(v[3]=="140"&&v[4]=="12"&&v[5]=="5"&&v[6]=="13", "overflow was unbounded or silently complete"); });
        Case("foreign poll does not consume current evidence", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',42)"); Check(!parse(Query(s,Build("Poll",next,1UL,0L)),next),"foreign token read accepted"); Check(Poll(s)[5]=="1","foreign token altered current queue"); });
        Case("future acknowledgement does not clear pending events", s => { Start(s); Query(s, "Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',42)"); Check(!parse(Query(s,Build("Poll",token,1UL,99L)),token),"future acknowledgement accepted"); Check(Poll(s)[5]=="1","future acknowledgement discarded evidence"); });
        Case("replacement collector reuses its owned frame", s => { Start(s); Check(parse(Query(s,Build("Install",next,1UL)),next),"replacement collector failed"); Check(Query(s,"return #frames")[0]=="1","replacement allocated another persistent client frame"); Query(s,"Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',42)"); var v=Query(s,Build("Poll",next,1UL,0L));Check(parse(v,next)&&v[5]=="1","reused frame lost its new owned callback"); });
        Case("stale cleanup cannot remove replacement collector", s => { Start(s); Query(s,Build("Install",next,1UL)); Query(s,Build("Dispose",token)); Query(s,"Emit('UNIT_SPELLCAST_START','player','Holy Light','Rank 1',42)"); Check(parse(Query(s,Build("Poll",next,1UL,0L)),next),"stale cleanup removed new collector"); });
        Case("owned cleanup detaches callbacks", s => { Start(s); Query(s,Build("Dispose",token)); Check(Query(s,"return frames[1].retired")[0]=="true","owned callbacks survived cleanup"); });
        Case("actor replacement invalidates old collection", s => { Start(s); Query(s,"playerGuid=targetGuid"); Check(!parse(Query(s,Build("Poll",token,1UL,0L)),token),"another actor's world read was accepted"); });
        Case("foreign global value is preserved", s => { Query(s,"__CBRecoveryObserved335={foreign=true}"); Check(!parse(Query(s,Build("Install",token,1UL)),token),"foreign namespace was replaced"); Check(Query(s,"return __CBRecoveryObserved335.foreign")[0]=="true","foreign global value mutated"); });
        Case("reentrant installation preserves replacement namespace", s=> {Query(s,"installChangesNamespace=true");Check(!parse(Query(s,Build("Install",token,1UL)),token),"revoked installation was published");Check(Query(s,"return __CBRecoveryObserved335.foreign,frames[1].retired").SequenceEqual(new[]{"true","true"}),"stale install overwrote replacement or leaked its frame");});
        Case("reentrant poll rejects a replaced collector", s=> {Start(s);Query(s,"clockChangesNamespace=true");Check(!parse(Query(s,Build("Poll",token,1UL,0L)),token),"replaced collector supplied an authoritative empty reply");});
        Case("partial event registration failure detaches its owned frame",s=>{Query(s,"failRegistration=true");var script=wrap(Build("Install",token,1UL));var reply=s.Execute(script,(uint)Encoding.UTF8.GetByteCount(script));Check(reply.Call!=0||!parse(reply.Values,token),"partial registration was declared complete");Check(Query(s,"return #frames,frames[1].retired or false").SequenceEqual(new[]{"1","true"}),"failed installation leaked event registrations");Query(s,"failRegistration=false");Start(s);Check(Query(s,"return #frames")[0]=="1","retry leaked another persistent frame");});
        Case("collector advances its monotonic observation time on polling",s=>{Start(s);Query(s,"now=110");Poll(s);Query(s,"now=109");Check(!parse(Query(s,Build("Poll",token,1UL,0L)),token),"clock rollback after successful poll was not detected");});
        Case("known spell metadata preserves original rank and cast time", s => { var v=Query(s,Build("SpellIdentity",101,1UL)); Check(v.SequenceEqual(new[]{"recovery-spell","101",playerGuid,"Holy Light","Rank 1","2500","100"}),"spell metadata protocol differs"); });
        foreach(string mutation in new[]{"spellMissing=true", "spellRank=nil", "castTime=0/0", "castTime=-1", "spellChangesActor=true"})
            Case("unavailable spell metadata / "+mutation, s=> {Query(s,mutation);Check(Query(s,Build("SpellIdentity",101,1UL))[0]!="recovery-spell","unavailable metadata was usable");});
        Case("known item snapshot", s => { var v=Query(s,Build("ItemSnapshot",123U,1UL)); Check(v.SequenceEqual(new[]{"recovery-item","123",playerGuid,"4","0","0","1","100"}),"item baseline values changed"); });
        foreach(string mutation in new[]{"itemMissing=true", "itemCount=nil", "itemCount=0/0", "itemCount=-1", "cdStart=nil", "cdDuration=-1", "cdEnabled=nil", "cdEnabled=2", "itemChangesActor=true"})
            Case("unavailable item metadata / "+mutation, s=> {Query(s,mutation);Check(Query(s,Build("ItemSnapshot",123U,1UL))[0]!="recovery-item","unavailable item reply was usable");});
        Case("disabled item is known but unavailable for use", s=> {Query(s,"cdEnabled=0");Check(Query(s,Build("ItemSnapshot",123U,1UL))[6]=="0","disabled cooldown became ready");});
        foreach(string bad in new[]{"", "bad'code", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"})
            Case("invalid collector token / "+bad, s=> {bool rejected=false;try{Build("Install",bad,1UL);}catch(TargetInvocationException error)when(error.InnerException is ArgumentException){rejected=true;}Check(rejected,"invalid token entered generated Lua");});
        Console.WriteLine($"Recovery Lua collector/query cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; actual generated Lua, actual observed wrapper and parser, stock Lua5.1 x86; no game.");
        if(failed+unexpected!=0)throw new InvalidOperationException("Recovery Lua collector/query regressions failed");
    }
    private const string playerGuid="0x0000000000000001", targetGuid="0x0000000000000009";
    private static string Root()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;
        throw new InvalidOperationException("Tracked source required");
    }
    private const string Setup="""
clicks=0; frames={}; now=100; playerGuid='0x0000000000000001'; targetGuid='0x0000000000000009'
function GetTime() if clockChangesNamespace then __CBRecoveryObserved335={foreign=true} end return now end
function UnitGUID(unit) if unit=='player' then return playerGuid end return targetGuid end
function CreateFrame(kind)
 local f={events={}}; function f:RegisterEvent(name) self.events[name]=true; if failRegistration and name=='UNIT_SPELLCAST_SUCCEEDED' then error('controlled registration failure') end end
 function f:SetScript(name,fn) self.fn=fn end
 function f:UnregisterAllEvents() self.events={}; self.retired=true end
 frames[#frames+1]=f; if installChangesNamespace then __CBRecoveryObserved335={foreign=true} end return f
end
function Emit(name,...) for _,f in ipairs(frames) do if f.events[name] and f.fn then f.fn(f,name,...) end end end
spellRank='Rank 1';castTime=2500
function GetSpellInfo(id) if spellMissing then return nil end if spellChangesActor then playerGuid=targetGuid end return 'Holy Light',spellRank,'icon',0,0,0,castTime,0,40 end
itemCount=4; cdStart=0;cdDuration=0;cdEnabled=1
function GetItemInfo(id) if itemChangesActor then playerGuid=targetGuid end if itemMissing then return nil end return 'Potion' end
function GetItemCount(id,bank) return itemCount end
function GetItemCooldown(id) return cdStart,cdDuration,cdEnabled end
""";
}
