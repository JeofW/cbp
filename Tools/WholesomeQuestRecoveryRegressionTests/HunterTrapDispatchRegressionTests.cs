using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete trap factories -> complete named Singular cast -> complete manager
// dispatch -> recorded executor arguments. Only external observations and the
// terminal executor are controlled; local submission is not server acceptance.
internal static class HunterTrapDispatchRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (typeof(Styx.WoWInternals.WoWObjects.WoWObject).GetProperty("Distance")!.PropertyType != typeof(double)
            || typeof(Styx.WoWInternals.WoWObjects.WoWObject).GetProperty("DistanceSqr")!.PropertyType != typeof(double))
            throw new InvalidOperationException("The controlled distance boundary must match the actual public double-valued observation");
        string root = Root();
        string Load(string name) => File.ReadAllText(Path.Combine(root, name));
        var common = CSharpSyntaxTree.ParseText(Load("runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Hunter/Common.cs")).GetRoot();
        var methods = common.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.ValueText.StartsWith("CreateHunterTrap", StringComparison.Ordinal)
            || m.Identifier.ValueText.StartsWith("IsHunterTrap", StringComparison.Ordinal)).ToArray();
        if (methods.Count(m => m.Identifier.ValueText == "CreateHunterTrapBehavior") != 4
            || methods.Count(m => m.Identifier.ValueText == "CreateHunterTrapOnAddBehavior") != 1)
            throw new InvalidOperationException("The complete existing Hunter overload set is required");
        var spell = CSharpSyntaxTree.ParseText(Load("runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs")).GetRoot();
        var spellMethods = spell.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.ValueText is "MeleeRangeFor" or "CanCastNamedSpell" or "CanSelectNamedSpell" || m.Identifier.ValueText is "Cast" or "CastWithRecovery"
            && m.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString() == "string").ToArray();
        if (spellMethods.Count(m => m.Identifier.ValueText != "CanSelectNamedSpell") != 8)
            throw new InvalidOperationException("Complete named cast overload/admission/recovery region required");
        var manager = CSharpSyntaxTree.ParseText(Load("Styx/Logic/Combat/SpellManager.cs")).GetRoot();
        var names = new HashSet<string> { "HasSpell", "GetSpellByName", "Cast", "CastSpellById", "TryCastSpellById",
            "CaptureSpellObservation", "PrepareCooldownContext", "TrackDeadline", "BeginCastSelectionPulse",
            "TryClaimCastCandidate", "RecordCastCandidateResult" };
        var managerMethods = manager.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).ToArray();
        var fields = new HashSet<string> { "_knownSpells", "_cooldownSync", "_castVerificationUntilTicks", "CastAttemptVerificationDelayMs",
            "_cooldownReadyAtTicks", "_readinessProbeNotBeforeTicks", "_cooldownContext", "_cooldownEpoch", "_lastCooldownObservationTicks",
            "_failedCastCandidates", "_seenCastCandidates", "_castSelectionClaimed", "_claimedCastCandidateId", "_castSelectionBot", "_castSelectionRun" };
        string managerState = string.Join("\n", manager.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Variables.Any(v => fields.Contains(v.Identifier.ValueText))).Select(f => f.ToFullString()));
        managerState += manager.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "Spells").ToFullString();
        managerState += string.Join("\n", manager.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.ValueText == "SpellObservationContext").Select(c => c.ToFullString()));
        var specializationSources = new[] { "BeastMaster", "Marksman", "Survival" }.Select(name =>
            CSharpSyntaxTree.ParseText(Load("runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Hunter/" + name + ".cs"))
                .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name).ToFullString()).ToArray();
        string source = Prefix + "public static class SpellManager {\n" + managerState
            + string.Join("\n", managerMethods.Select(m => m.ToFullString())) + ManagerBoundary + "}\n"
            + "public static partial class Spell {\n" + string.Join("\n", spellMethods.Select(m => m.ToFullString())) + "}\n"
            + "public static partial class Common {\n" + string.Join("\n", methods.Select(m => m.ToFullString())) + "}\n"
            + string.Join("\n", specializationSources) + HunterTrapSpecializationFixture.Source + Cases;
        // Region labels are editor trivia spanning methods outside the selected
        // owner set. Remove only those labels; keep every executable token and
        // all conditional compilation directives intact.
        source = System.Text.RegularExpressions.Regex.Replace(source,
            @"^[ \t]*#(?:end)?region\b[^\r\n]*(?:\r?\n|$)", "",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Console.WriteLine("Hunter trap complete-source SHA256: " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
        string directory = Path.Combine(Path.GetTempPath(), "cb-hunter-trap-dispatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), source, Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "Attributes.cs"), Load("runtime-snapshot/Routines/Singular wotlk/Dynamics/Attributes.cs"), Encoding.UTF8);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Hunter dispatch fixture did not compile: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("HunterCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }

    private const string Prefix = """
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Styx.Helpers;
using TreeSharp;
using CommonBehaviors.Actions;
using Singular.Dynamics;
using Singular.Managers;
using Styx.Combat.CombatRoutine;
using Action = TreeSharp.Action;
using HunterTrapOwner = Common;
public delegate WoWUnit UnitSelectionDelegate(object context);
public delegate bool SimpleBooleanDelegate(object context);
public class WoWUnit
{
    public ulong Guid; public bool IsValid=true,IsAlive=true,IsFriendly,IsMoving,IsPlayer,Combat=true,Mounted,IsCasting;
    public WoWUnit? CurrentTarget; public double Distance=3; public float CombatReach=1; public bool Sight=true,IsTargetingMeOrPet;
    public double ManaPercent=100,HealthPercent=100,HappinessPercent=100;
    public System.Numerics.Vector3 Location=new(10,10,10);
    public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;
    public bool HasAura(string name)=>false;
    public bool HasAnyAura(params string[] names)=>false;
    public double DistanceSqr=>Distance*Distance;
    public bool IsMe=>ReferenceEquals(this,StyxWoW.Me);
    public bool InLineOfSpellSight=>Sight;
    public string SafeName()=>"controlled unit";
}
public sealed class LocalPlayer:WoWUnit
{
    public uint BaseAddress=4096,MapId=530;
    public WoWUnit? Pet;public bool GotAlivePet=>Pet!=null&&Pet.IsValid&&Pet.IsAlive;
    public WoWSpell CastingSpell=new();public int CurrentCastId;
}
public sealed class WoWSpell
{
    public int Id;public string Name="";public uint AttributesEx;public bool Cooldown;
    public uint SpellRangeId=1;public float MinRange=0,MaxRange=40;public bool IsFunnel,IsChanneled;
    public TimeSpan CooldownTimeLeft=>Cooldown?TimeSpan.FromMinutes(1):TimeSpan.Zero;
}
public static class World
{
    public static bool Ready=true,Safe=true,Gcd,Mana=true,Silenced;
    public static string Mode="success";public static System.Action? DuringLog,DuringSetup;
    public static int Resets,Errors,Admissions;
}
public sealed class ExecutorRand
{
    public object AssemblyLock=new();public List<string> Lines=new();public List<(int Spell,ulong Target)> Completed=new();public int Attempts;
    public void Clear(){if(World.Mode=="clear-exception")throw new InvalidOperationException("controlled preparation failure");Lines.Clear();}
    public void AddLine(string value,params object[] args){Lines.Add(string.Format(CultureInfo.InvariantCulture,value,args));}
    public void Execute()
    {
        Attempts++;
        if(World.Mode=="execute-exception")throw new InvalidOperationException("controlled executor failure; native effects remain unknown");
        int id=int.Parse(Lines[7].Substring(5),CultureInfo.InvariantCulture);
        ulong guid=((ulong)uint.Parse(Lines[4].Substring(5),CultureInfo.InvariantCulture)<<32)|uint.Parse(Lines[5].Substring(5),CultureInfo.InvariantCulture);
        Completed.Add((id,guid));
    }
}
public static class ObjectManager { public static ExecutorRand? Executor; public static object Wow=new object(); }
public static class RecoveryActions
{
    public static bool TryCast(string name,WoWUnit target,bool heal,bool aura,string owner)=>SpellManager.Cast(name,target);
    public static bool BeforeSpellSubmission(int id,ulong target)=>true;
    public static void RethrowControlFlow(Exception error)=>Styx.Logic.Combat.RecoveryActions.RethrowControlFlow(error);
}
public static class TreeRoot
{
    public static object Current=new object(),RunIdentity=new object();
    public static bool IsRunning=true,CurrentThreadIsBotThread;
    public static void VerifyPulseOwner(object bot,bool worker)
    {
        if(worker&&(!IsRunning||!ReferenceEquals(bot,Current)))throw new OperationCanceledException("controlled Hunter dispatch Stop");
    }
}
public static class StyxWoW { public static LocalPlayer Me=new(); public static void ResetAfk(){World.Resets++;} }
public static class Logging { public static void WriteDebug(string message,params object[] args){} public static void WriteException(Exception e){World.Errors++;} }
public static class Patchables { public static class GlobalOffsets { public const uint Spell_C__CastSpell=12345; } }
public static class LegacySpellManager { public static void CastSpellById(int id)=>SpellManager.CastSpellById(id); }
public static class Unit
{
    public static readonly List<WoWUnit> NearbyUnfriendlyUnits=new();
    public static IEnumerable<WoWUnit> UnfriendlyUnitsNearTarget(float range)=>NearbyUnfriendlyUnits;
    public static bool IsCombatActionSafe(string name,WoWUnit target)=>World.Safe&&target!=null&&target.IsValid;
}
public static class Logger { public static void Write(string message){var callback=World.DuringLog;World.DuringLog=null;callback?.Invoke();} }
public sealed class SetupAction:Composite
{
    protected override IEnumerable<RunStatus> Execute(object context)
    {
        yield return RunStatus.Running;
        var callback=World.DuringSetup;World.DuringSetup=null;callback?.Invoke();
        StyxWoW.Me.Mounted=false;
        yield return RunStatus.Success;
    }
}
public static partial class Common { public static Composite CreateDismount(string reason)=>new SetupAction(); }
public static partial class Movement
{
    public static bool NeedsOffTargetCastSetup(WoWUnit target)=>false;
    public static Composite CreateEnsureTargetAndFaceBehavior(UnitSelectionDelegate select)=>new SetupAction();
}
""";

    private const string ManagerBoundary = """
public static void FixtureReset(string name,int id)
{
    _knownSpells.Clear();lock(_cooldownSync){_castVerificationUntilTicks.Clear();_failedCastCandidates.Clear();_seenCastCandidates.Clear();_castSelectionClaimed=false;_claimedCastCandidateId=0;_castSelectionBot=null;_castSelectionRun=null;}
    _knownSpells[name]=new WoWSpell{Id=id,Name=name};
}
public static bool CanCast(string name,WoWUnit target,bool range,bool movement)
{
    World.Admissions++;
    return World.Ready&&!World.Gcd&&World.Mana&&!World.Silenced&&!StyxWoW.Me.IsCasting
        &&target!=null&&target.IsValid&&target.IsAlive&&_knownSpells.TryGetValue(name,out var spell)&&!spell.Cooldown
        &&(!_castVerificationUntilTicks.TryGetValue(spell.Id,out var until)||Environment.TickCount64>=until);
}
""";

    private const string Cases = """
public static class HunterCases
{
    private sealed class Failure(string message):Exception(message){}
    private static int count,passed,assertions,unexpected;
    private static readonly (string Name,int[] Ids)[] Traps={
        ("Immolation Trap",new[]{13795,14302,14303,14304,14305,27023,49055,49056}),
        ("Freezing Trap",new[]{1499,14310,14311}),
        ("Explosive Trap",new[]{13813,14316,14317,27025,49066,49067}),
        ("Frost Trap",new[]{13809}), ("Snake Trap",new[]{34600})};
    public static void Run()
    {
        foreach(var trap in Traps)
        {
            foreach(int id in trap.Ids)
            foreach(int overload in Enumerable.Range(0,5))
                Case($"{trap.Name}/{id}/overload{overload} uses learned rank",()=>{
                    Reset(trap.Name,id);Tick(Build(trap.Name,overload));Expect(id);Check(World.Admissions==1,"one cast decision crossed strict admission more than once");
                });
            foreach(int overload in new[]{0,4})
            {
                void Add(string label,System.Action change)=>Case(trap.Name+"/"+overload+"/"+label,()=>{
                    Reset(trap.Name,trap.Ids[0]);change();Tick(Build(trap.Name,overload));Expect();
                });
                Add("unlearned",()=>SpellManager.Spells.Clear());
                Add("cooldown",()=>SpellManager.Spells[trap.Name].Cooldown=true);
                Add("global cooldown",()=>World.Gcd=true);
                Add("insufficient mana",()=>World.Mana=false);
                Add("silenced",()=>World.Silenced=true);
                Add("casting",()=>StyxWoW.Me.IsCasting=true);
                Add("unavailable observation",()=>World.Ready=false);
                Add("combat safety rejects",()=>World.Safe=false);
                Add("dead actor",()=>StyxWoW.Me.IsAlive=false);
                Add("invalid actor",()=>StyxWoW.Me.IsValid=false);
                Add("zero actor identity",()=>StyxWoW.Me.Guid=0);
                Add("dead candidate",()=>Candidate(overload).IsAlive=false);
                Add("invalid candidate",()=>Candidate(overload).IsValid=false);
                Add("zero candidate identity",()=>Candidate(overload).Guid=0);
                Add("out of existing range",()=>Candidate(overload).Distance=40);
                foreach(string mode in new[]{"no-executor","clear-exception","execute-exception"})
                    Case(trap.Name+"/"+overload+"/"+mode+" yields instead of fake success",()=>{
                        Reset(trap.Name,trap.Ids[0]);World.Mode=mode;
                        if(mode=="no-executor")ObjectManager.Executor=null;
                        int fallback=0;
                        Tick(new PrioritySelector(Build(trap.Name,overload),new Action(_=>{fallback++;return RunStatus.Success;})));
                        Expect();Check(fallback==1,"failed dispatch suppressed fallback");
                    });
                foreach(bool setup in new[]{false,true})
                foreach(string mutation in new[]{"replace-actor","replace-target","dead-target","changed-guid","range","safety","availability"})
                    Case(trap.Name+"/"+overload+"/"+(setup?"setup":"log")+"/"+mutation,()=>{
                        Reset(trap.Name,trap.Ids[0]);
                        void Change()
                        {
                            var selected=Candidate(overload);
                            if(mutation=="replace-actor")StyxWoW.Me=new LocalPlayer{Guid=123,CurrentTarget=StyxWoW.Me.CurrentTarget};
                            else if(mutation=="replace-target") {if(overload==4)Unit.NearbyUnfriendlyUnits.Clear();else StyxWoW.Me.CurrentTarget=new WoWUnit{Guid=999};}
                            else if(mutation=="dead-target")selected.IsAlive=false;
                            else if(mutation=="changed-guid")selected.Guid++;
                            else if(mutation=="range")selected.Distance=50;
                            else if(mutation=="safety")World.Safe=false;
                            else World.Ready=false;
                        }
                        if(setup){StyxWoW.Me.Mounted=true;World.DuringSetup=Change;}else World.DuringLog=Change;
                        Tick(Build(trap.Name,overload));Expect();
                    });
                Case(trap.Name+"/"+overload+"/readiness recovery uses next decision",()=>{
                    Reset(trap.Name,trap.Ids[0]);World.Ready=false;Tick(Build(trap.Name,overload));Expect();
                    World.Ready=true;Tick(Build(trap.Name,overload));Expect(trap.Ids[0]);
                });
                Case(trap.Name+"/"+overload+"/completed local submission is not trigger credit",()=>{
                    Reset(trap.Name,trap.Ids[0]);Tick(Build(trap.Name,overload));Expect(trap.Ids[0]);
                    Check(ObjectManager.Executor!.Attempts==1,"unexpected request count");
                    Check(ObjectManager.Executor.Completed[0].Target is 0 or 1,"ground trap was dispatched at a hostile GUID");
                });
            }
        }
        Case("unknown trap kind never falls through to arbitrary spell",()=>{
            Reset("Fake Trap",123);Tick(HunterTrapOwner.CreateHunterTrapBehavior("Fake Trap"));Expect();
        });
        Case("null selector remains a denied decision",()=>{
            Reset("Freezing Trap",1499);Tick(HunterTrapOwner.CreateHunterTrapBehavior("Freezing Trap",false,null!));Expect();
        });
        Case("no add does not borrow current target",()=>{
            Reset("Freezing Trap",1499);Unit.NearbyUnfriendlyUnits.Clear();Tick(HunterTrapOwner.CreateHunterTrapOnAddBehavior("Freezing Trap"));Expect();
        });
        Case("failed fallback candidates do not amplify strict admission in one pulse",()=>{
            Reset("Freezing Trap",1499);World.Ready=false;
            SpellManager.Spells["Steady Shot"]=new WoWSpell{Id=56641,Name="Steady Shot"};
            Tick(new PrioritySelector(Spell.Cast("Freezing Trap"),Spell.Cast("Steady Shot")));
            Expect();Check(World.Admissions==1,"one tree pulse performed strict admission for multiple fallback candidates");
        });
        Case("failed priority pass advances and wraps without an idle pulse",()=>{
            Reset("Freezing Trap",1499);World.Ready=false;
            SpellManager.Spells["Steady Shot"]=new WoWSpell{Id=56641,Name="Steady Shot"};
            var choices=new PrioritySelector(Spell.Cast("Freezing Trap"),Spell.Cast("Steady Shot"));
            Tick(choices);Expect();Check(World.Admissions==1,"first pulse did not bound strict admission");
            Tick(choices);Expect();Check(World.Admissions==2,"lower priority did not receive the next pulse");
            World.Ready=true;Tick(choices);Expect(1499);
            Check(World.Admissions==3,"exhausted pass inserted an idle pulse or repeated admission");
        });
        Case("run replacement cannot inherit failed candidate suppression",()=>{
            Reset("Freezing Trap",1499);World.Ready=false;Tick(Spell.Cast("Freezing Trap"));Expect();
            TreeRoot.RunIdentity=new object();World.Ready=true;
            Tick(Spell.Cast("Freezing Trap"));Expect(1499);
        });
        var owners=new (string Name,Func<Composite> Create,string Trap)[]{
            ("BM/normal",BeastMaster.CreateBeastMasterHunterNormalPullAndCombat,"Freezing Trap"),
            ("BM/battleground",BeastMaster.CreateBeastMasterHunterPvPPullAndCombat,"Freezing Trap"),
            ("BM/instance",BeastMaster.CreateBeastMasterHunterInstancePullAndCombat,"Explosive Trap"),
            ("MM/normal",Marksman.CreateMarksmanHunterNormalPullAndCombat,"Freezing Trap"),
            ("MM/battleground",Marksman.CreateMarksmanHunterPvPPullAndCombat,"Freezing Trap"),
            ("MM/instance",Marksman.CreateMarksmanHunterInstancePullAndCombat,"Explosive Trap"),
            ("SV/normal",Survival.CreateSurvivalHunterNormalPullAndCombat,"Freezing Trap"),
            ("SV/battleground",Survival.CreateSurvivalHunterPvPPullAndCombat,"Freezing Trap"),
            ("SV/instance",Survival.CreateSurvivalHunterInstancePullAndCombat,"Explosive Trap")};
        int prior=count;
        foreach(var owner in owners)
        foreach(int rank in Traps.Single(trap=>trap.Name==owner.Trap).Ids)
        {
            Case(owner.Name+" complete caller learned rank="+rank,()=>{
                Reset(owner.Trap,rank);StyxWoW.Me.CurrentTarget!.Distance=20;
                TickUntilSubmission(owner.Create());Expect(rank);
            });
            Case(owner.Name+" complete caller cooldown fallback rank="+rank,()=>{
                Reset(owner.Trap,rank);StyxWoW.Me.CurrentTarget!.Distance=20;
                SpellManager.Spells[owner.Trap].Cooldown=true;
                SpellManager.Spells["Steady Shot"]=new WoWSpell{Id=56641,Name="Steady Shot"};
                TickUntilSubmission(owner.Create());Expect(56641);
            });
            Case(owner.Name+" complete caller global cooldown rank="+rank,()=>{
                Reset(owner.Trap,rank);StyxWoW.Me.CurrentTarget!.Distance=20;World.Gcd=true;
                SpellManager.Spells["Steady Shot"]=new WoWSpell{Id=56641,Name="Steady Shot"};
                Tick(owner.Create());Expect();
            });
            Case(owner.Name+" complete caller missing executor rank="+rank,()=>{
                Reset(owner.Trap,rank);StyxWoW.Me.CurrentTarget!.Distance=20;ObjectManager.Executor=null;
                int fallbacks=0;
                Tick(new PrioritySelector(owner.Create(),new Action(_=>{fallbacks++;return RunStatus.Success;})));
                Expect();Check(fallbacks==1,"complete specialization swallowed failed dispatch");
            });
        }
        Console.WriteLine($"Complete Hunter specialization trap consumers: {count-prior} scenarios across {owners.Length} unchanged normal/battleground/instance factories; actual shared spell and executor path; unrelated pet/buff/navigation boundaries controlled.");
        Console.WriteLine($"Hunter trap dispatch scenarios: {passed}/{count}; assertions={assertions}; unexpected={unexpected}; complete trap/named-cast/manager owners and actual executor arguments; controlled readiness/world/backend; no native or server acceptance.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Hunter trap dispatch regressions failed");
    }
    private static void Reset(string name,int id)
    {
        World.Ready=World.Mana=World.Safe=true;World.Gcd=World.Silenced=false;World.Mode="success";
        World.DuringLog=World.DuringSetup=null;World.Admissions=World.Errors=World.Resets=0;
        StyxWoW.Me=new LocalPlayer{Guid=1,CurrentTarget=new WoWUnit{Guid=2}};
        Unit.NearbyUnfriendlyUnits.Clear();Unit.NearbyUnfriendlyUnits.Add(new WoWUnit{Guid=3});
        ObjectManager.Executor=new ExecutorRand();SpellManager.FixtureReset(name,id);
    }
    private static WoWUnit Candidate(int overload)=>overload==4?Unit.NearbyUnfriendlyUnits[0]:StyxWoW.Me.CurrentTarget!;
    private static Composite Build(string name,int overload)=>overload switch{
        0=>HunterTrapOwner.CreateHunterTrapBehavior(name),
        1=>HunterTrapOwner.CreateHunterTrapBehavior(name,false),
        2=>HunterTrapOwner.CreateHunterTrapBehavior(name,_=>StyxWoW.Me.CurrentTarget!),
        3=>HunterTrapOwner.CreateHunterTrapBehavior(name,true,_=>StyxWoW.Me.CurrentTarget!),
        _=>HunterTrapOwner.CreateHunterTrapOnAddBehavior(name)};
    private static void Tick(Composite root)
    {
        root.Start(null!);try{int ticks=0;while(true){typeof(SpellManager).GetMethod("BeginCastSelectionPulse")?.Invoke(null,null);if(root.Tick(null!)!=RunStatus.Running)break;if(++ticks>20)throw new Failure("unbounded cast setup");}}finally{root.Stop(null!);}
    }
    private static void TickUntilSubmission(Composite root)
    {
        for(int decision=0;decision<64&&ObjectManager.Executor?.Completed.Count==0;decision++)Tick(root);
    }
    private static void Expect(params int[] ids)
    {
        int[] actual=ObjectManager.Executor?.Completed.Select(row=>row.Spell).ToArray()??Array.Empty<int>();
        Check(actual.SequenceEqual(ids),"expected ["+string.Join(',',ids)+"] got ["+string.Join(',',actual)+"]");
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
    private static void Case(string name,System.Action action)
    {
        count++;try{action();passed++;}
        catch(Failure error){assertions++;Console.WriteLine("FAIL Hunter "+name+": "+error.Message);}
        catch(Exception error){unexpected++;Console.WriteLine("ERROR Hunter "+name+": "+error);}
    }
}
""";
}
