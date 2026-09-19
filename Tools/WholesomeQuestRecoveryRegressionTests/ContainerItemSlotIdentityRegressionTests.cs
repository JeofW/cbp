using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.WoWInternals.WoWObjects;

// Shared container-slot safety contract. Pure helpers use controlled GUID arrays;
// source-wiring assertions ensure the public owner no longer submits BagIndex/BagSlot
// independently. No Lua, inventory mutation, cursor action or game process is used.
internal static class ContainerItemSlotIdentityRegressionTests
{
    private sealed class Failure(string message):Exception(message){}
    private const BindingFlags Hidden =
        BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;

    [ModuleInitializer]
    internal static void Run()
    {
        MethodInfo? resolve=typeof(WoWItem).GetMethod("TryResolveContainerLocation",Hidden);
        MethodInfo? lua=typeof(WoWItem).GetMethod("BuildValidatedContainerUseLua",Hidden);

        var cases=new List<(string Name,Action Test)>
        {
            ("shared GUID resolver and validated Lua builder exist",()=>{
                Check(resolve!=null&&lua!=null,
                    "WoWItem lacks a shared slot-identity submission boundary");
            }),
            ("backpack item resolves to Lua bag0 and one-based slot",()=>{
                int bag,slot;
                Check(Resolve(resolve,22,new ulong[]{11,22,0},Array.Empty<ulong[]>(),out bag,out slot)
                    && bag==0&&slot==2,
                    "backpack GUID did not resolve to bag0/slot2");
            }),
            ("equipped bag item resolves to WoW Lua bag numbering",()=>{
                int bag,slot;
                var bags=new[]{new ulong[]{0,33},new ulong[]{44},Array.Empty<ulong>(),new ulong[]{55}};
                Check(Resolve(resolve,33,Array.Empty<ulong>(),bags,out bag,out slot)
                    && bag==1&&slot==2,"first physical bag did not map to Lua bag1");
                Check(Resolve(resolve,55,Array.Empty<ulong>(),bags,out bag,out slot)
                    && bag==4&&slot==1,"fourth physical bag did not map to Lua bag4");
            }),
            ("missing item never aliases backpack bag0 slot0",()=>{
                int bag,slot;
                Check(!Resolve(resolve,99,new ulong[]{11,22},new[]{new ulong[]{33}},out bag,out slot)
                    && bag==-1&&slot==-1,
                    "unresolved GUID became a usable container position");
            }),
            ("zero GUID is never a container identity",()=>{
                int bag,slot;
                Check(!Resolve(resolve,0,new ulong[]{0,0},new[]{new ulong[]{0}},out bag,out slot),
                    "empty slot GUID became an item identity");
            }),
            ("duplicate GUID snapshot fails closed",()=>{
                int bag,slot;
                Check(!Resolve(resolve,22,new ulong[]{22},new[]{new ulong[]{22}},out bag,out slot),
                    "non-atomic duplicate observation selected one arbitrary slot");
            }),
            ("validated Lua submission checks expected entry before use",()=>{
                string code=BuildLua(lua,2,3,7586);
                Check(code.Contains("GetContainerItemLink(2,3)",StringComparison.Ordinal)
                    && code.Contains("7586",StringComparison.Ordinal)
                    && code.Contains("UseContainerItem(2,3)",StringComparison.Ordinal)
                    && code.IndexOf("GetContainerItemLink",StringComparison.Ordinal)
                       < code.IndexOf("UseContainerItem",StringComparison.Ordinal),
                    "Lua owner does not validate the observed slot before item use");
            }),
            ("public UseContainerItem no longer submits independent BagIndex and BagSlot reads",()=>{
                string source=File.ReadAllText(Path.Combine(Root(),
                    "Styx","WoWInternals","WoWObjects","WoWItem.cs"));
                int start=source.IndexOf("public void UseContainerItem()",StringComparison.Ordinal);
                Check(start>=0,"public UseContainerItem owner is missing");
                string region=source.Substring(start,Math.Min(1600,source.Length-start));
                Check(region.Contains("TryUseContainerItem",StringComparison.Ordinal)
                    && !region.Contains("BagIndex + 1",StringComparison.Ordinal)
                    && !region.Contains("BagSlot + 1",StringComparison.Ordinal),
                    "public owner still derives the two slot coordinates independently");
            }),
            ("live submission boundary revalidates GUID before Lua entry check",()=>{
                string source=File.ReadAllText(Path.Combine(Root(),
                    "Styx","WoWInternals","WoWObjects","WoWItem.cs"));
                int start=source.IndexOf("public bool TryUseContainerItem()",StringComparison.Ordinal);
                Check(start>=0,"TryUseContainerItem owner is missing");
                string region=source.Substring(start,Math.Min(3200,source.Length-start));
                int revalidate=region.IndexOf("IsContainerLocationCurrent",StringComparison.Ordinal);
                int luaCall=region.IndexOf("Lua.GetReturnVal<bool>",StringComparison.Ordinal);
                Check(revalidate>=0&&luaCall>revalidate,
                    "container GUID slot is not revalidated before Lua submission");
            })
        };

        int pass=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try{c.Test();pass++;Console.WriteLine("PASS container slot identity: "+c.Name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL container slot identity assertion: "+c.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR container slot identity fixture: "+c.Name+": "+e);}
        }
        Console.WriteLine($"Container slot identity scenarios: {pass}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; pure GUID snapshots/source wiring; no Lua/game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Container slot identity regressions");
    }

    private static bool Resolve(MethodInfo? method,ulong guid,ulong[] backpack,ulong[][] bags,out int bag,out int slot)
    {
        if(method==null)throw new Failure("TryResolveContainerLocation is missing");
        object?[] args={guid,backpack,bags,-1,-1};
        bool ok=(bool)(method.Invoke(null,args)??false);
        bag=(int)args[3]!;slot=(int)args[4]!;
        return ok;
    }

    private static string BuildLua(MethodInfo? method,int bag,int slot,uint entry)
    {
        if(method==null)throw new Failure("BuildValidatedContainerUseLua is missing");
        return Convert.ToString(method.Invoke(null,new object[]{bag,slot,entry}),
            System.Globalization.CultureInfo.InvariantCulture)??"";
    }

    private static string Root()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;
        throw new Failure("tracked checkout required");
    }

    private static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
}
