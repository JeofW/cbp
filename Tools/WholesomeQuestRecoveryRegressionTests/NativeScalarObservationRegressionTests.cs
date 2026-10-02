using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the real native-byte cache/typed reader and scalar/map-cache owners.
// Only ReadProcessMemory, address/world ownership and Map construction are leaves.
internal static class NativeScalarObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root=Root();
        SyntaxNode Parse(string path)=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,path))).GetRoot();
        var memory=Parse("GreenMagic/Memory.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="Memory");
        var methods=memory.Members.OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText is "Read" or "ReadInternal"
            ||m.Identifier.ValueText=="ReadBytes"&&m.ParameterList.Parameters.Count==2&&m.ParameterList.Parameters[0].Type!.ToString()=="uint"&&m.ParameterList.Parameters[1].Type!.ToString()=="int").ToArray();
        if(methods.Length!=3)throw new InvalidOperationException("Actual complete typed/byte reader chain required");
        string fields=string.Join("\n",memory.Members.OfType<FieldDeclarationSyntax>().Where(f=>f.Declaration.Variables.Any(v=>v.Identifier.ValueText is "_cache" or "_cacheEnabled")).Select(f=>f.ToString()));
        string fastSize=Parse("GreenMagic/FastSize.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="FastSize").ToString();
        var obj=Parse("Styx/WoWInternals/WoWObjects/WoWObject.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="WoWObject");
        string objMembers=string.Join("\n",obj.Members.OfType<PropertyDeclarationSyntax>().Where(p=>p.Identifier.ValueText is "ObjectFlags" or "IsValid" or "IsDisabled").Select(p=>p.ToString()))
            +string.Join("\n",obj.Members.OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText=="ReadObservedUInt32").Select(m=>m.ToString()));
        string objFields=string.Join("\n",obj.Members.OfType<FieldDeclarationSyntax>().Where(f=>f.Declaration.Variables.Any(v=>v.Identifier.ValueText is "ObjectFlagsOffset" or "TypeOffset")).Select(f=>f.ToString()));
        var player=Parse("Styx/WoWInternals/WoWObjects/LocalPlayer.cs").DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="LocalPlayer");
        string playerMembers=string.Join("\n",player.Members.OfType<PropertyDeclarationSyntax>().Where(p=>p.Identifier.ValueText is "MapId" or "CurrentMap").Select(p=>p.ToString()))
            +string.Join("\n",player.Members.OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText is "ReadCurrentMapId" or "RequireMapOwner").Select(m=>m.ToString()));
        string playerFields=string.Join("\n",player.Members.OfType<FieldDeclarationSyntax>().Where(f=>f.Declaration.Variables.Any(v=>v.Identifier.ValueText is "MapIdPtr" or "_currentMap" or "_currentMapCachedId")).Select(f=>f.ToString()));
        string source=Prefix+fastSize+"\npublic sealed class ProbeMemory {\n"+fields+MemoryLeaves+string.Join("\n",methods.Select(m=>m.ToString()))+"}\n"
            +"public class WoWObject {public uint BaseAddress=0x1000;protected ProbeMemory Memory=>ObjectManager.Wow;"+objFields+objMembers+"}\n"
            +"public sealed class LocalPlayer:WoWObject {"+playerFields+playerMembers+"}\n"+Cases;
        string trusted=(string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references=trusted.Split(Path.PathSeparator).Append(typeof(Styx.Helpers.ObservationUnavailableException).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("NativeScalar_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(output.ToArray());
        try{assembly.GetType("ScalarCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static string Root(){for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,"CopilotBuddy.csproj")))return dir.FullName;throw new InvalidOperationException("Tracked source required");}
    private const string Prefix="""
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Text;using System.Runtime.InteropServices;using System.Reflection;using Styx.Helpers;
public static class Transfer {
 public const uint MapAddress=0xBD088C,FlagsAddress=0x10BC,TypeAddress=0x1014;
 public static uint Map=530,Flags,Type=4,Target;public static string Mode="complete";public static int Reads;public static Action AfterRead;
 public static readonly List<uint> MapsCreated=new();public static Action DuringMap;
}
public static class ObjectManager {public static ProbeMemory Wow;public static LocalPlayer Me;}
public sealed class Map {public uint Id;public Map(uint id){Id=id;Transfer.MapsCreated.Add(id);var callback=Transfer.DuringMap;Transfer.DuringMap=null;callback?.Invoke();}}
public static class Imports {
 public static bool ReadProcessMemory(IntPtr handle,uint address,byte[] buffer,int count,out int bytesRead){
  if(handle!=new IntPtr(1)||count!=4)throw new InvalidOperationException("Unexpected scalar native boundary");
  Transfer.Reads++;bytesRead=0;string mode=Transfer.Target==address?Transfer.Mode:"complete";
  if(mode=="cancel")throw new OperationCanceledException("scalar cancellation");
  if(mode=="wrapped-cancel")throw new TargetInvocationException(new OperationCanceledException("scalar cancellation"));
  if(mode=="throw")throw new InvalidOperationException("controlled native ownership failure");
  if(mode=="missing")return false;
  uint value=address==Transfer.MapAddress?Transfer.Map:address==Transfer.FlagsAddress?Transfer.Flags:address==Transfer.TypeAddress?Transfer.Type:throw new InvalidOperationException("Unexpected scalar address");
  bytesRead=mode=="oversized"?5:mode.Contains('-')?int.Parse(mode.Split('-')[1]):4;
  Array.Copy(BitConverter.GetBytes(value),buffer,Math.Min(bytesRead,4));
  var callback=Transfer.AfterRead;Transfer.AfterRead=null;callback?.Invoke();
  return !mode.StartsWith("failed",StringComparison.Ordinal);
 }
}
""";
    private const string MemoryLeaves="""
 public IntPtr ProcessHandle=new IntPtr(1);
 private string ReadString(Encoding encoding,uint address)=>throw new NotSupportedException("String observations are outside this fixture");
 public void Clear(){_cache.Value.Clear();_cacheEnabled.Value=false;}
 public void Cache(uint address,byte[] data){_cacheEnabled.Value=true;_cache.Value[new IntPtr(unchecked((int)address))]=data;}
""";
    private const string Cases="""
public static class ScalarCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static LocalPlayer Reset(){var memory=new ProbeMemory();memory.Clear();ObjectManager.Wow=memory;ObjectManager.Me=new LocalPlayer();Transfer.Map=530;Transfer.Flags=0;Transfer.Type=4;Transfer.Target=0;Transfer.Mode="complete";Transfer.Reads=0;Transfer.AfterRead=null;Transfer.DuringMap=null;Transfer.MapsCreated.Clear();return ObjectManager.Me;}
 private static object Read(LocalPlayer actor,string name)=>name=="map"?actor.MapId:name=="cache"?actor.CurrentMap.Id:name=="flags"?actor.ObjectFlags:name=="disabled"?actor.IsDisabled:actor.IsValid;
 private static void Unknown(LocalPlayer actor,string name){bool unknown=false;try{_=Read(actor,name);}catch(ObservationUnavailableException){unknown=true;}catch(Exception error){throw new Failure("Expected controlled UNKNOWN; observed "+error.GetType().Name+": "+error.Message);}Check(unknown,"incomplete scalar became a usable "+name+" value");}
 public static void Run(){int total=0,passed=0,failed=0,unexpected=0;
  void Case(string name,Action test){total++;try{test();passed++;Console.WriteLine("PASS native scalar: "+name);}catch(Failure e){failed++;Console.Error.WriteLine("FAIL native scalar: "+name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR native scalar: "+name+": "+e);}}
  foreach(string reader in new[]{"map","cache"})foreach(uint id in new uint[]{0,1,530,571})
   Case("complete map/"+reader+"/"+id,()=>{var actor=Reset();Transfer.Map=id;Check((uint)Read(actor,reader)==id,"valid map identity changed");});
  Case("known flags and valid type",()=>{var actor=Reset();Check(actor.ObjectFlags==0&&!actor.IsDisabled&&actor.IsValid,"valid complete actor was rejected");});
  Case("known disabled",()=>{var actor=Reset();Transfer.Flags=0x10000;Check(actor.IsDisabled&&!actor.IsValid,"known disabled actor became valid");});
  foreach(uint type in new uint[]{0,11,uint.MaxValue})Case("known invalid type/"+type,()=>{var actor=Reset();Transfer.Type=type;Check(!actor.IsValid,"complete invalid type became valid");});
  Case("invalid zero address retains known invalidity",()=>{var actor=Reset();actor.BaseAddress=0;Check(!actor.IsValid&&Transfer.Reads==0,"invalid object caused a read or became valid");});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})foreach(string mode in new[]{"missing","partial-0","partial-1","partial-2","partial-3","failed-4","oversized"})
   Case("unavailable/"+reader+"/"+mode,()=>{var actor=Reset();Transfer.Target=reader=="map"||reader=="cache"?Transfer.MapAddress:Transfer.FlagsAddress;Transfer.Mode=mode;Unknown(actor,reader);if(reader=="cache")Check(Transfer.MapsCreated.Count==0,"missing map created a cache entry");});
  foreach(string mode in new[]{"missing","partial-1","failed-4"})Case("unknown type/"+mode,()=>{var actor=Reset();Transfer.Target=Transfer.TypeAddress;Transfer.Mode=mode;Unknown(actor,"valid");});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})foreach(string mode in new[]{"cancel","wrapped-cancel"})
   Case("cancellation/"+reader+"/"+mode,()=>{var actor=Reset();Transfer.Target=reader=="map"||reader=="cache"?Transfer.MapAddress:Transfer.FlagsAddress;Transfer.Mode=mode;bool cancelled=false;try{_=Read(actor,reader);}catch(Exception error){cancelled=error is OperationCanceledException;}Check(cancelled,"scalar swallowed cancellation or retained its reflection wrapper");});
  foreach(string mode in new[]{"cancel","wrapped-cancel"})Case("type-read cancellation/"+mode,()=>{var actor=Reset();Transfer.Target=Transfer.TypeAddress;Transfer.Mode=mode;bool cancelled=false;try{_=actor.IsValid;}catch(Exception error){cancelled=error is OperationCanceledException;}Check(cancelled,"validity swallowed type-read cancellation or retained its reflection wrapper");});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})foreach(string change in new[]{"memory","address"})
   Case("changed owner/"+reader+"/"+change,()=>{var actor=Reset();Transfer.AfterRead=()=>{if(change=="memory"){var next=new ProbeMemory();next.Clear();ObjectManager.Wow=next;}else actor.BaseAddress=0x2000;};Unknown(actor,reader);});
  foreach(string reader in new[]{"map","cache"})Case("replaced map actor/"+reader,()=>{var actor=Reset();Transfer.AfterRead=()=>ObjectManager.Me=new LocalPlayer();Unknown(actor,reader);});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})Case("missing memory/"+reader,()=>{var actor=Reset();ObjectManager.Wow=null;Unknown(actor,reader);});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})Case("closed process remains fatal/"+reader,()=>{var actor=Reset();ObjectManager.Wow.ProcessHandle=IntPtr.Zero;bool fatal=false;try{_=Read(actor,reader);}catch(InvalidOperationException e)when(e is not ObservationUnavailableException){fatal=true;}Check(fatal,"fatal ownership loss became ordinary ready/unknown");});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})Case("process closes during transfer/"+reader,()=>{var actor=Reset();Transfer.AfterRead=()=>ObjectManager.Wow.ProcessHandle=IntPtr.Zero;bool fatal=false;try{_=Read(actor,reader);}catch(InvalidOperationException e)when(e is not ObservationUnavailableException){fatal=true;}Check(fatal,"closed process still published its scalar");});
  foreach(string reader in new[]{"map","cache","flags","disabled","valid"})Case("native process generation changes/"+reader,()=>{var actor=Reset();Transfer.AfterRead=()=>ObjectManager.Wow.ProcessHandle=new IntPtr(2);Unknown(actor,reader);});
  Case("generic default compatibility is untouched",()=>{_=Reset();Transfer.Target=Transfer.MapAddress;Transfer.Mode="missing";Check(ObjectManager.Wow.Read<uint>(Transfer.MapAddress)==0,"generic legacy contract was changed");});
  Case("missing observation cannot replace last map cache",()=>{var actor=Reset();Map original=actor.CurrentMap;Transfer.Target=Transfer.MapAddress;Transfer.Mode="missing";Unknown(actor,"cache");Transfer.Mode="complete";Check(ReferenceEquals(original,actor.CurrentMap)&&Transfer.MapsCreated.SequenceEqual(new uint[]{530}),"unknown read replaced or rebuilt a valid map cache");});
  Case("map construction callback cannot publish old ownership",()=>{var actor=Reset();Transfer.DuringMap=()=>ObjectManager.Me=new LocalPlayer();Unknown(actor,"cache");});
  Case("map changes during construction require a fresh observation",()=>{var actor=Reset();Transfer.DuringMap=()=>Transfer.Map=0;Unknown(actor,"cache");Check(actor.CurrentMap.Id==0,"fresh valid map-zero observation failed");});
  Case("nested same-world map publication is retained",()=>{var actor=Reset();Map nested=null;Transfer.DuringMap=()=>nested=actor.CurrentMap;Map observed=actor.CurrentMap;Check(nested!=null&&ReferenceEquals(observed,nested),"older map construction overwrote the nested current publication");});
  Case("fresh complete read recovers after missing",()=>{var actor=Reset();Transfer.Target=Transfer.MapAddress;Transfer.Mode="missing";Unknown(actor,"map");Transfer.Mode="complete";Check(actor.MapId==530,"unknown was negatively cached");});
  Console.WriteLine($"Native scalar observation cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; actual byte/typed/getter/cache owners; no game.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Native scalar observation regressions failed");
 }
}
""";
}
