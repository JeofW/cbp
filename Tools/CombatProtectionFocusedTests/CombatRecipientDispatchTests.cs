using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The complete native manager entrypoint is unchanged in the probe. The actual
// player admission implementation is included when present. Reentry is injected
// at the assembler's final prepared instruction and before native execution.
internal static class CombatRecipientDispatchTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = FindRoot();
        var manager = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/Combat/SpellManager.cs"))).GetRoot();
        var methods = new HashSet<string> { "TryCastSpellById", "CaptureSpellObservation", "PrepareCooldownContext", "TrackDeadline" };
        var fields = new HashSet<string> { "_cooldownSync", "_castVerificationUntilTicks", "CastAttemptVerificationDelayMs",
            "_cooldownReadyAtTicks", "_readinessProbeNotBeforeTicks", "_cooldownContext", "_cooldownEpoch", "_lastCooldownObservationTicks" };
        string members = string.Join("\n", manager.DescendantNodes().Where(n =>
            n is MethodDeclarationSyntax m && methods.Contains(m.Identifier.ValueText)
            || n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => fields.Contains(v.Identifier.ValueText))
            || n is ClassDeclarationSyntax c && c.Identifier.ValueText == "SpellObservationContext").Select(n => n.ToString()));
        string safetyPath = Path.Combine(root, "Styx/Logic/Combat/CombatRecipientSafety.cs");
        string safety = File.Exists(safetyPath) ? CSharpSyntaxTree.ParseText(File.ReadAllText(safetyPath)).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "CombatRecipientSafety").ToString() : "";
        var boundary = CSharpSyntaxTree.ParseText(Boundary).GetCompilationUnitRoot();
        var fakeRoster = boundary.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "GroupObservation");
        boundary = boundary.RemoveNode(fakeRoster, SyntaxRemoveOptions.KeepNoTrivia)!;
        string roster = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/GroupObservation.cs"))).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "TryReadMemberGuids").ToString();
        string attacks = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/Combat/CombatAttackSafety.cs"))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "CombatAttackSafety").ToString();
        string source = boundary.ToFullString()
            + "public sealed class ObservationUnavailableException:Exception {public ObservationUnavailableException(string kind,string message):base(message){} public static ObservationUnavailableException Find(Exception e)=>e as ObservationUnavailableException;}"
            + "public static class ObservationFailureDiagnostics {public static void Report(ObservationUnavailableException e,string source){}}"
            + "public static class GroupObservation {" + roster + "}" + safety + attacks + "\npublic static class SpellManager {" + members
            + "public static bool Dispatch(int id,ulong target)=>TryCastSpellById(id,target);}" + Cases;
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Styx.StyxWoW).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var compile = CSharpCompilation.Create("CombatRecipient_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compile.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        try { assembly.GetType("Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    internal const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using System.Globalization;
using Styx.Helpers;using Styx.Logic.Combat;
public readonly record struct WoWPoint(float X,float Y,float Z) {public float DistanceSqr(WoWPoint p)=>(X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z);}
public class WoWObject {public string Name="Fixture";public double Distance;}
public class WoWUnit:WoWObject {
 public WoWUnit CurrentTarget;public bool Combat;
 public ulong Guid,DescriptorGuid;public uint BaseAddress=4096,MapId=530,Type=3,FactionId=1;
 public ulong CharmedByGuid,SummonedByGuid,CreatedByGuid;public bool PlayerControlled,Possessed,IsValid=true,IsAlive=true;
 public bool Friendly;public bool IsFriendly {get {if(World.Prepared)throw new Exception("native reaction inside prepared guard");World.Reactions++;World.Hook("reaction");return Friendly;}}
 public bool IsMe=>ReferenceEquals(this,StyxWoW.Me);public bool IsPlayer=>Type==4;public bool IsInMyPartyOrRaid=>StyxWoW.Me.PartyMemberGuids.Contains(Guid)||StyxWoW.Me.RaidMemberGuids.Contains(Guid);
 public WoWPoint Location;public uint DuelTeam;public ulong DuelArbiterGuid;
 // Raw ID coverage is a complete memory read, without spell metadata or native queries.
 public bool TryGetRawAuras(out List<Aura> values){values=World.Auras.ToList();return World.AurasKnown;}
}
public class WoWPlayer:WoWUnit {}
public sealed class LocalPlayer:WoWPlayer {
 public Map CurrentMap=new();public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public WoWUnit Pet;public bool IsAutoAttacking;public int AutoRepeatingSpellId;
 public bool IsInParty,IsInRaid;public List<ulong> PartyMemberGuids=new(),RaidMemberGuids=new();
}
public sealed class Map {public bool IsDungeon=true,IsRaid;}
public sealed class Aura {public int SpellId;public bool IsActive=true,IsHarmful;}
public sealed class WoWSpell {
 public int Id;public string Name;public SpellEntry InternalInfo;
 public static WoWSpell FromId(int id) {if(World.Prepared)throw new Exception("metadata query inside prepared guard");World.Hook("metadata");return World.Spells.GetValueOrDefault(id);}
}
public static class StyxWoW {public static LocalPlayer Me;public static Memory Memory=>ObjectManager.Wow;public static bool IsInGame=true;public static void ResetAfk(){}}
public sealed class Memory {public IntPtr ProcessHandle=new(1);public IDisposable TemporaryCacheState(bool value)=>new Scope();public IDisposable AcquireFrame()=>new Scope();public byte[] ReadBytes(uint address,int count)=>World.RawRead(address,count);}
public sealed class Scope:IDisposable {public void Dispose(){}}
public static class ObjectManager {
 public static Memory Wow;public static ExecutorRand Executor;public static LocalPlayer Me=>StyxWoW.Me;
 public static List<WoWUnit> Units=new();public static T GetObjectByGuid<T>(ulong id)where T:WoWUnit
  =>(id==StyxWoW.Me.Guid?StyxWoW.Me:Units.FirstOrDefault(u=>u.Guid==id)) as T;
 public static List<T> GetObjectsOfType<T>(bool allowInheritance=false,bool includeMeIfFound=false)where T:WoWUnit
  =>Units.Where(u=>(allowInheritance?u is T:u.GetType()==typeof(T))&&(includeMeIfFound||!u.IsMe)).Cast<T>().ToList();
}
public sealed class ExecutorRand {
 public object AssemblyLock=new();public readonly List<string> Lines=new();
 public Memory Memory;public void Clear(){World.Prepared=false;Lines.Clear();}
 public void AddLine(string format,params object[] args){Lines.Add(string.Format(CultureInfo.InvariantCulture,format,args));if(format=="retn"){World.Prepared=true;World.Hook("prepared");}}
 public void Execute(){World.Hook("native");World.Attempts++;World.Prepared=false;}
}
public static class TreeRoot {public static object Current=new(),RunIdentity=new();public static bool IsRunning=true,CurrentThreadIsBotThread;public static void VerifyPulseOwner(object bot,bool worker){if(worker&&!IsRunning)throw new OperationCanceledException();}}
public static class RecoveryActions {public static bool BeforeSpellSubmission(int id,ulong target){World.Hook("submission");return true;}public static void RethrowControlFlow(Exception error){if(error is OperationCanceledException)throw error;}}
public static class Lua {public static List<string> GetObservedReturnValues(string script,Func<bool> admitted){World.Hook("lua-entry");if(!admitted())return new(){"0"};World.Scripts.Add(script);return new(){"1"};}}
public static class Logging {public static void WriteDebug(string format,params object[] args){}public static void WriteException(Exception error){World.Errors.Add(error.Message);}}
public static class Patchables {public static class GlobalOffsets {public const uint Spell_C__CastSpell=1;}}
public static class GroupCombatSafety {
 public static bool IsInInstance=>StyxWoW.Me.CurrentMap.IsDungeon||StyxWoW.Me.CurrentMap.IsRaid;
 public static bool IsProtectedPlayer(WoWUnit unit)=>unit!=null&&(unit is WoWPlayer||unit.IsPlayer)&&(unit.IsMe||IsInInstance||unit.IsInMyPartyOrRaid);
 public static bool MayAttack(WoWUnit unit)=>unit!=null&&unit.IsValid&&unit.IsAlive&&!IsProtectedPlayer(unit);
}
public static class GroupObservation {
 public static bool TryReadMemberGuids(LocalPlayer actor,out ulong[] guids){guids=actor.PartyMemberGuids.Concat(actor.RaidMemberGuids).Append(actor.Guid).Distinct().OrderBy(id=>id).ToArray();return true;}
}
public static class World {
 public static int Attempts,Reactions,ShortAddress;public static bool Prepared,AurasKnown=true;public static Action<string> Callback;
 public static int? RaidCount;public static Action<uint> DuringRawRead;
 public static List<string> Errors=new(),Scripts=new();public static List<Aura> Auras=new();public static Dictionary<int,WoWSpell> Spells=new();
 public static void Hook(string stage){Callback?.Invoke(stage);}
 public static WoWUnit Target;public static WoWPlayer Member;
 public static byte[] RawRead(uint address,int count){DuringRawRead?.Invoke(address);if(address==ShortAddress)return new byte[Math.Max(0,count-1)];var actor=StyxWoW.Me;
  if(address==12498440)return BitConverter.GetBytes(RaidCount??actor.RaidMemberGuids.Count);
  if(address==12392776){var bytes=new byte[32];for(int i=0;i<Math.Min(4,actor.PartyMemberGuids.Count);i++)BitConverter.GetBytes(actor.PartyMemberGuids[i]).CopyTo(bytes,i*8);return bytes;}
  if(address==12498280){var bytes=new byte[160];for(int i=0;i<Math.Min(40,actor.RaidMemberGuids.Count);i++)BitConverter.GetBytes((uint)(0x100000+i*256)).CopyTo(bytes,i*4);return bytes;}
  if(address>=0x100000&&address<0x100000+40*256&&(address-0x100000)%256==0)return BitConverter.GetBytes(actor.RaidMemberGuids[(int)((address-0x100000)/256)]);
  return Array.Empty<byte>();}
 public static void Reset(){Attempts=Reactions=0;ShortAddress=-1;RaidCount=null;DuringRawRead=null;Prepared=false;AurasKnown=true;Callback=null;Errors.Clear();Scripts.Clear();Auras.Clear();
  StyxWoW.Me=new(){Guid=1,DescriptorGuid=1,Type=4,BaseAddress=4096,Friendly=true};ObjectManager.Wow=new();ObjectManager.Executor=new(){Memory=ObjectManager.Wow};
  Target=new(){Guid=2,DescriptorGuid=2,BaseAddress=8192,Location=new(3,0,0)};Member=new(){Guid=40,DescriptorGuid=40,Type=4,BaseAddress=16384,Friendly=true,Location=new(2,0,0)};
  StyxWoW.Me.CurrentTarget=Target;ObjectManager.Units=new(){Target,Member};StyxWoW.Me.IsInParty=true;StyxWoW.Me.PartyMemberGuids=new(){40};
  Spells=new(){[35395]=Spell(35395,"Crusader Strike",31,6),[20924]=Spell(20924,"Consecration",27,18,16),[635]=Spell(635,"Holy Light",10,21),
   [20217]=Spell(20217,"Blessing of Kings",6,21),[498]=Spell(498,"Divine Protection",6,1),[47540]=Spell(47540,"Penance",3,25),[20473]=Spell(20473,"Holy Shock",3,25)};
 }
 private static WoWSpell Spell(int id,string name,uint effect,uint targetA,uint targetB=0)=>new(){Id=id,Name=name,InternalInfo=new(){Id=(uint)id,Effect=new[]{effect,0U,0U},EffectImplicitTargetA=new[]{targetA,0U,0U},EffectImplicitTargetB=new[]{targetB,0U,0U},EffectRadiusIndex=new uint[3],EffectChainTarget=new uint[3],EffectTriggerSpell=new uint[3],EffectApplyAuraName=new uint[3]}};
}
""";
    private const string Cases = """
public static class Cases {
 private sealed class Failure(string text):Exception(text){}
 private static void Check(bool value,string why){if(!value)throw new Failure(why);}
 public static void Run(){int total=0,passed=0;var failures=new List<string>();
  void Case(string name,Action test){total++;World.Reset();try{test();passed++;Console.WriteLine("PASS combat recipient: "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL combat recipient: "+name+": "+e.Message);}}
  void Denied(int id,ulong guid){bool result=SpellManager.Dispatch(id,guid);Check(!result&&World.Attempts==0,"harmful or stale admission reached native spell submission");Check(!World.Errors.Any(e=>e.Contains("inside prepared guard")),"guard performed a native query after ASM preparation");}
  void Allowed(int id,ulong guid){Check(SpellManager.Dispatch(id,guid)&&World.Attempts==1,"legitimate NPC damage or friendly support was denied: "+string.Join(";",World.Errors));}
  foreach(bool friendly in new[]{false,true})foreach(bool raid in new[]{false,true})
   Case("instance player never admits enemy spell/friendly="+friendly+"/raid="+raid,()=>{World.Member.Friendly=friendly;StyxWoW.Me.CurrentMap.IsDungeon=!raid;StyxWoW.Me.CurrentMap.IsRaid=raid;Denied(35395,World.Member.Guid);});
  foreach(int spell in new[]{635,20217,47540,20473})Case("friendly support retained/"+spell,()=>Allowed(spell,World.Member.Guid));
  Case("enemy NPC strike retained",()=>Allowed(35395,World.Target.Guid));
  Case("party pack Consecration retained",()=>Allowed(20924,StyxWoW.Me.Guid));
  Case("hostile controlled player vetoes ground damage",()=>{World.Member.Friendly=false;World.Member.CharmedByGuid=99;Denied(20924,StyxWoW.Me.Guid);});
  Case("active Command proc protects collateral players",()=>{World.Member.Friendly=false;World.Auras.Add(new(){SpellId=20375});Denied(35395,World.Target.Guid);});
  Case("single-target attack without cleave retains safe NPC",()=>{World.Member.Friendly=false;Allowed(35395,World.Target.Guid);});
  Case("unknown proc coverage cannot prove safe strike beside controlled player",()=>{World.Member.Friendly=false;World.AurasKnown=false;Denied(35395,World.Target.Guid);});
  Case("unknown metadata cannot harm a protected player",()=>Denied(999999,World.Member.Guid));
  Case("caster defense remains usable with controlled player selected",()=>{World.Member.Friendly=false;StyxWoW.Me.CurrentTarget=World.Member;Allowed(498,0);});
  Case("caster defense remains usable with controlled player explicitly selected",()=>{World.Member.Friendly=false;Allowed(498,World.Member.Guid);});
  Case("late Command aura invalidates a previously safe direct strike",()=>{World.Member.Friendly=false;World.Callback=stage=>{if(stage=="prepared")World.Auras.Add(new(){SpellId=20375});};Denied(35395,World.Target.Guid);});
  Case("late raw-aura coverage loss does not prove safe direct strike",()=>{World.Member.Friendly=false;World.Callback=stage=>{if(stage=="prepared")World.AurasKnown=false;};Denied(35395,World.Target.Guid);});
  Case("friendly Dispel Magic remains support",()=>{
   World.Spells[527]=new(){Id=527,Name="Dispel Magic",InternalInfo=new(){Id=527,Effect=new uint[]{38,0,0},EffectImplicitTargetA=new uint[]{25,0,0},EffectImplicitTargetB=new uint[3],EffectRadiusIndex=new uint[3],EffectChainTarget=new uint[3],EffectApplyAuraName=new uint[3],EffectTriggerSpell=new uint[3]}};
   Allowed(527,World.Member.Guid);});
  Case("offensive Dispel Magic cannot target a controlled player",()=>{
   World.Spells[527]=new(){Id=527,Name="Dispel Magic",InternalInfo=new(){Id=527,Effect=new uint[]{38,0,0},EffectImplicitTargetA=new uint[]{25,0,0},EffectImplicitTargetB=new uint[3],EffectRadiusIndex=new uint[3],EffectChainTarget=new uint[3],EffectApplyAuraName=new uint[3],EffectTriggerSpell=new uint[3]}};
   World.Member.Friendly=false;Denied(527,World.Member.Guid);});
  foreach(bool harmful in new[]{false,true})Case("self periodic spell is not automatically harmless/"+harmful,()=>{
   World.Spells[1949]=new(){Id=1949,Name=harmful?"Hellfire":"Controlled recovery",InternalInfo=new(){Id=1949,Effect=new uint[]{6,6,6},EffectImplicitTargetA=new uint[]{1,1,1},EffectImplicitTargetB=new uint[3],EffectRadiusIndex=new uint[3],EffectChainTarget=new uint[3],EffectApplyAuraName=new[]{23U,harmful?3U:8U,77U},EffectTriggerSpell=new uint[3]}};
   World.Member.Friendly=false;if(harmful)Denied(1949,StyxWoW.Me.Guid);else Allowed(1949,StyxWoW.Me.Guid);});
  foreach(int address in new[]{12498440,12392776,12498280,0x100000})Case("incomplete original group memory is UNKNOWN/"+address,()=>{
   if(address==12498280||address==0x100000){StyxWoW.Me.IsInRaid=true;StyxWoW.Me.RaidMemberGuids=new(){1,40};}
   World.ShortAddress=address;Denied(35395,World.Target.Guid);});
  foreach(int count in new[]{-1,41,1,3})Case("raid count must match complete roster/"+count,()=>{
   StyxWoW.Me.IsInRaid=true;StyxWoW.Me.RaidMemberGuids=new(){1,40};World.RaidCount=count;Denied(35395,World.Target.Guid);});
  Case("duplicate party GUIDs do not prove a complete roster",()=>{StyxWoW.Me.PartyMemberGuids=new(){40,40};Denied(35395,World.Target.Guid);});
  Case("raid roster must contain current actor",()=>{StyxWoW.Me.IsInRaid=true;StyxWoW.Me.RaidMemberGuids=new(){40,50};Denied(35395,World.Target.Guid);});
  Case("complete raid roster preserves friendly support",()=>{StyxWoW.Me.IsInRaid=true;StyxWoW.Me.RaidMemberGuids=new(){1,40};Allowed(635,World.Member.Guid);});
  Case("raw roster replacement after command preparation revokes admission",()=>{World.Callback=stage=>{if(stage=="prepared")StyxWoW.Me.RaidMemberGuids=new(){1,50};};Denied(35395,World.Target.Guid);});
  Case("owned autoattack starts only the exact current NPC",()=>Check(CombatAttackSafety.TryStartAttack(World.Target)&&World.Scripts.Count==1,"legitimate attack did not submit"));
  Case("owned autoattack refuses a current controlled member",()=>{World.Member.Friendly=false;Check(!CombatAttackSafety.TryStartAttack(World.Member)&&World.Scripts.Count==0,"controlled member attack submitted");});
  foreach(string change in new[]{"actor","target","run","faction"})Case("autoattack final managed owner/"+change,()=>{
   World.Callback=stage=>{if(stage!="lua-entry")return;switch(change){case "actor":StyxWoW.Me=new(){Guid=1,DescriptorGuid=1};break;case "target":World.Target.DescriptorGuid=40;break;case "run":TreeRoot.RunIdentity=new();break;case "faction":World.Member.CharmedByGuid=99;break;}};
   Check(!CombatAttackSafety.TryStartAttack(World.Target)&&World.Scripts.Count==0,"replaced command entered Lua");});
  foreach(bool petOnly in new[]{false,true})Case("ongoing attack stops after teammate becomes hostile/pet="+petOnly,()=>{
   World.Member.Friendly=false;if(petOnly)StyxWoW.Me.Pet=new(){Guid=5,DescriptorGuid=5,BaseAddress=32768,Combat=true,CurrentTarget=World.Member};
   else{StyxWoW.Me.IsAutoAttacking=true;StyxWoW.Me.CurrentTarget=World.Member;}
   CombatAttackSafety.StopUnsafeAttacks();Check(World.Scripts.Count==1&&World.Scripts[0].Contains(petOnly?"PetFollow()":"StopAttack()"),"ongoing attack was left active");});
  Case("safe ongoing NPC attack is undisturbed",()=>{StyxWoW.Me.IsAutoAttacking=true;CombatAttackSafety.StopUnsafeAttacks();Check(World.Scripts.Count==0,"safe NPC attack was stopped");});
  Case("ongoing Command strike stops near a controlled teammate",()=>{StyxWoW.Me.IsAutoAttacking=true;World.Member.Friendly=false;World.Auras.Add(new(){SpellId=20375});CombatAttackSafety.StopUnsafeAttacks();Check(World.Scripts.Count==1,"passive cleave continued against protected collateral recipient");});
  Case("stale stop cannot cancel a successor target",()=>{StyxWoW.Me.IsAutoAttacking=true;StyxWoW.Me.CurrentTarget=World.Member;World.Member.Friendly=false;
   World.Callback=stage=>{if(stage=="lua-entry")StyxWoW.Me.CurrentTarget=World.Target;};CombatAttackSafety.StopUnsafeAttacks();Check(World.Scripts.Count==0,"old stop canceled the new target");});
  foreach(string stage in new[]{"metadata","prepared","submission"})foreach(string change in new[]{"target-player","target-descriptor","target-wrapper","implicit-target","player-charmer","player-faction","roster","actor"})
   Case("native entry revokes replacement/"+stage+"/"+change,()=>{
    bool applied=false;World.Callback=point=>{if(applied||point!=stage)return;applied=true;
     switch(change){case "target-player":World.Target.Type=4;break;case "target-descriptor":World.Target.DescriptorGuid=40;break;
      case "target-wrapper":ObjectManager.Units[0]=new(){Guid=2,DescriptorGuid=2,BaseAddress=8192};break;
      case "implicit-target":StyxWoW.Me.CurrentTarget=World.Member;break;
      case "player-charmer":World.Member.CharmedByGuid=99;World.Member.Friendly=false;break;
      case "player-faction":World.Member.FactionId=2;World.Member.Friendly=false;break;
      case "roster":StyxWoW.Me.PartyMemberGuids=new(){50};break;
      case "actor":StyxWoW.Me=new(){Guid=1,DescriptorGuid=1,BaseAddress=4096};break;}}
    ;Denied(change=="player-charmer"||change=="player-faction"?20924:35395,change=="implicit-target"?0:World.Target.Guid);
    Check(applied,"replacement boundary was not reached");});
  Console.WriteLine($"Combat recipient dispatch: {passed}/{total}; failures={failures.Count}; actual native manager and admission, controlled original-client leaves.");
  if(failures.Count>0)throw new InvalidOperationException("combat recipient dispatch regressions");
 }
}
""";
}
