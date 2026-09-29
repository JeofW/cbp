using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// CarriedItems includes equipped objects. These are exact tracked admission,
// acknowledgement and pending-tick helpers with controlled player/inventory/Lua
// boundaries. No game, native executor or server acknowledgement is simulated.
internal static class EquipmentAlreadyAcknowledgedRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        int passed = 0, total = 0, assertions = 0, unexpected = 0;
        foreach (var owner in new[] {
            (Path:"runtime-snapshot/Quest Behaviors/EquipItem.cs", Quest:true),
            (Path:"runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs", Quest:false) })
        {
            string source = File.ReadAllText(Path.Combine(root, owner.Path));
            string[] names = { "TickPendingEquip", "CanEquipNow", "OwnsPendingEquipContext", "SubmitOwnedCursorEquip",
                "IsPendingEquipAcknowledged", "ReturnDisplacedCursorToSource", "RestoreOwnedCursorToSource",
                "CursorHasAnyItem", "ResetPendingEquip", "BeginEquip" };
            string methods = string.Join("\n", CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>().Where(m => names.Contains(m.Identifier.ValueText)).Select(m => m.ToString()));
            string begin = owner.Quest ? "private void Begin(){TickPendingEquip();}" :
                "private void Begin(){BeginEquip(World.Candidate,Slot,Slot==InventorySlot.None);}";
            string directory = Path.Combine(Path.GetTempPath(), "cb-already-equipped-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + methods + begin + Suffix);
                Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
                object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
                var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
                var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
                var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
                try
                {
                    var counts = (int[])assembly.GetType("OwnerProbe", true)!.GetMethod("Run")!.Invoke(null,
                        new object[] { owner.Quest ? "EquipItem" : "AutoEquip" })!;
                    passed += counts[0]; total += counts[1]; assertions += counts[2]; unexpected += counts[3];
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
            }
            finally { Directory.Delete(directory, true); }
        }
        Console.WriteLine($"Pre-existing equipment acknowledgement: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; both tracked owners; controlled observations; no game/native/server execution.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Pre-existing equipment acknowledgement regression");
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System; using System.Collections.Generic; using System.Linq;
public enum InventorySlot { None=0, HeadSlot=1, MainHandSlot=16 }
public enum RunStatus { Success, Failure, Running }
public sealed class WoWItem {
 public ulong Guid=200; public uint Entry=100; public bool IsValid=true;
 public bool TryPickUp(out int bag,out int slot){World.Pickups++;bag=0;slot=1;return World.InBag;}
}
public sealed class Equipment {public WoWItem[] Items=new WoWItem[23];}
public sealed class Inventory {public Equipment Equipped=new Equipment();}
public sealed class LocalPlayer {
 public ulong Guid=7;public bool IsValid=true,IsAlive=true,IsGhost,Combat;
 public Inventory Inventory=new Inventory();public List<WoWItem> CarriedItems=new List<WoWItem>();
}
public static class StyxWoW {public static LocalPlayer Me;public static bool IsInGame=true;}
public static class ObjectManager {public static LocalPlayer Me=>StyxWoW.Me;}
public static class TreeRoot {public static bool IsRunning=true;}
public static class Battlegrounds {public static bool IsInsideBattleground;}
public static class World {
 public static WoWItem Candidate;public static bool InBag,CursorBusy,UnknownCursor;
 public static int Pickups,Arms,Submits,ByNames,Cancels,Queries;
 public static void Reset(){
  Candidate=new WoWItem();InBag=true;CursorBusy=UnknownCursor=false;
  Pickups=Arms=Submits=ByNames=Cancels=Queries=0;
  StyxWoW.Me=new LocalPlayer();StyxWoW.Me.CarriedItems.Add(Candidate);
  StyxWoW.IsInGame=true;TreeRoot.IsRunning=true;Battlegrounds.IsInsideBattleground=false;
 }
}
public static class Lua {
 public static bool BeginEquipCursorOwnership(uint entry,string owner){World.Arms++;return true;}
 public static bool TryEquipCursorItem(ulong guid,uint entry,int slot,string owner){World.Submits++;return true;}
 public static bool TryCancelEquipCursorItem(ulong guid,uint entry,string owner){World.Cancels++;return true;}
 public static void DoString(string format,params object[] args){World.ByNames++;}
 public static T GetReturnVal<T>(string script,uint index){World.Queries++;return (T)(object)(World.UnknownCursor?0:World.CursorBusy?1:2);}
}
public sealed class OwnerProbe {
 private sealed class Failure(string message):Exception(message){}
 private bool _isBehaviorDone,_isDisposed,_pendingEquipSubmitted;
 private ulong _pendingEquipGuid,_pendingEquipPlayerGuid;
 private uint _pendingEquipEntry;
 private LocalPlayer _pendingEquipPlayer;
 private InventorySlot _pendingEquipSlot=InventorySlot.None;
 private int _pendingSourceBag=-1,_pendingSourceSlot=-1;
 private string _pendingCursorOwner;
 private DateTime _pendingEquipSince;
 private static readonly TimeSpan EquipTimeout=TimeSpan.FromSeconds(10);
 private bool HasPendingEquip=>_pendingEquipGuid!=0&&_pendingEquipEntry!=0;
 private int ItemId=>100;private int QuestId=>900001;private int QuestRequirementInLog=>1;private int QuestRequirementComplete=>0;
 private InventorySlot Slot=InventorySlot.HeadSlot;
 private bool UtilIsProgressRequirementsMet(int quest,int inLog,int complete)=>true;
 private void LogMessage(string level,string format,params object[] args){}
 private void Log(string format,params object[] args){}
""";
    private const string Suffix = """
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static void NoRequests(){Check(World.Pickups==0&&World.Arms==0&&World.Submits==0&&World.ByNames==0&&World.Cancels==0,"already-satisfied or revoked work started a cursor/equip mutation");}
 public static int[] Run(string ownerName){
  int passed=0,assertions=0,unexpected=0,total=0;
  foreach(string mode in new[]{"already-exact","already-busy","already-unknown","already-by-name","normal-explicit","normal-by-name","foreign-same-entry","missing-equipment","revoked-actor"}){
   total++;
   try{
    World.Reset();var owner=new OwnerProbe();bool already=mode.StartsWith("already-");
    if(mode.EndsWith("by-name"))owner.Slot=InventorySlot.None;
    int destination=owner.Slot==InventorySlot.None?15:0;
    if(already){World.InBag=false;StyxWoW.Me.Inventory.Equipped.Items[destination]=World.Candidate;}
    if(mode=="foreign-same-entry")StyxWoW.Me.Inventory.Equipped.Items[0]=new WoWItem{Guid=201,Entry=100};
    if(mode=="missing-equipment")StyxWoW.Me.Inventory.Equipped.Items=null;
    if(mode=="revoked-actor")StyxWoW.Me.IsAlive=false;
    World.CursorBusy=mode=="already-busy";World.UnknownCursor=mode=="already-unknown";
    owner.Begin();
    if(mode=="revoked-actor"){
     NoRequests();Check(!owner.HasPendingEquip,"revoked actor retained intent");
    }else if(already){
     NoRequests();Check(owner.HasPendingEquip,"existing equipment result lost its pending completion/deadline");
     DateTime since=owner._pendingEquipSince;owner.TickPendingEquip();
     if(World.CursorBusy||World.UnknownCursor){
      Check(owner.HasPendingEquip&&owner._pendingEquipSince==since,"busy/unknown cursor reset the original deadline or completed early");
      owner._pendingEquipSince=DateTime.UtcNow-TimeSpan.FromSeconds(11);owner.TickPendingEquip();
      Check(!owner.HasPendingEquip,"already-equipped busy cursor remained pending past the deadline");NoRequests();
     }else{
      Check(!owner.HasPendingEquip&&World.Queries==1,"already-equipped empty cursor did not complete through normal observation");NoRequests();
     }
    }else{
     Check(owner.HasPendingEquip,"ordinary new equip was suppressed");
     // W108: default destination must retain the captured physical copy too.
     Check(World.Pickups==1&&World.Arms==1&&World.Submits==1&&World.ByNames==0,"ordinary request lost owned pickup or performed an independent by-name lookup");
     StyxWoW.Me.Inventory.Equipped.Items=new WoWItem[23];StyxWoW.Me.Inventory.Equipped.Items[destination]=World.Candidate;
     owner.TickPendingEquip();Check(!owner.HasPendingEquip,"new equipment acknowledgement lost normal completion");
    }
    passed++;Console.WriteLine("PASS pre-existing equip: "+ownerName+"/"+mode);
   }catch(Failure e){assertions++;Console.Error.WriteLine("FAIL pre-existing equip: "+ownerName+"/"+mode+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR pre-existing equip: "+ownerName+"/"+mode+": "+e);}
  }
  Console.WriteLine($"Pre-existing equipment acknowledgement {ownerName}: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; exact tracked admission/tick/ack/context/cleanup helpers; controlled observations; no game/native/server execution.");
  return new[]{passed,total,assertions,unexpected};
 }
}
""";
}
