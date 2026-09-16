using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Styx.Logic.Inventory.Frames.Merchant;

// Compile and extract the real merchant Lua producers, without a native executor.
// These are emitted-script contracts, not a mocked seller or a server sale test.
// The exported scripts also support separate execution in a controlled Lua runtime.
internal static class MerchantQuestItemLuaRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        var single = typeof(MerchantFrame).GetMethod("BuildSellNextItemLua", Hidden)!;
        var filters = typeof(MerchantFrame).GetMethod("BuildSaleFilters", Hidden)!;
        var bulk = typeof(MerchantFrame).GetMethod("SellItemQualities")!;
        var literals = ReadStringLiterals(bulk).Where(s => s.StartsWith("if not MerchantFrame", StringComparison.Ordinal) && s.Contains("UseContainerItem(b,s)")).ToArray();
        if (literals.Length != 1) throw new InvalidOperationException("Expected exactly one actual bulk-sale format literal, not a fabricated script.");
        foreach (bool protectedItems in new[] { false, true })
        {
            var names = protectedItems ? new[] { "Protected Name" } : Array.Empty<string>();
            var ids = protectedItems ? new uint[] { 9001 } : Array.Empty<uint>();
            object[] args = { ItemQuality.Poor | ItemQuality.Common, names, ids, null!, null! };
            filters.Invoke(null, args);
            foreach (bool one in new[] { true, false })
            {
                string label = (one ? "single" : "bulk") + (protectedItems ? "/protected" : "/plain");
                string lua = one ? (string)single.Invoke(null, new object[] { args[0], names, ids })!
                    : string.Format(CultureInfo.InvariantCulture, literals[0], args[3], args[4]);
                Console.WriteLine("MERCHANT_LUA_EXPORT:" + JsonSerializer.Serialize(new {
                    name=label, lua, sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lua))).ToLowerInvariant()
                }));
                void Add(string name, Action check) => cases.Add((label + ": " + name, check));
                Add("client quest query precedes item submission", () => Check(Before(lua,"GetContainerItemQuestInfo","UseContainerItem(b,s)"), "no quest observation before submission"));
                Add("query absence and exceptions cannot authorize selling", () => Check(lua.Contains("pcall(") && lua.Contains("type(GetContainerItemQuestInfo)"), "missing fail-closed client-query boundary"));
                Add("quest-starting identity supplements the quest flag", () => Check(lua.Contains("isQuestItem or questId"), "quest-starting item has no independent veto"));
                Add("merchant visibility gate remains", () => Check(Before(lua,"MerchantFrame:IsShown()","UseContainerItem(b,s)"), "merchant guard removed"));
                Add("locked stack guard remains", () => Check(Before(lua,"GetContainerItemInfo(b,s)","UseContainerItem(b,s)"), "lock guard removed"));
                Add("item metadata is required", () => Check(Before(lua,"GetItemInfo(itemLink)","UseContainerItem(b,s)") && lua.Contains("quality"), "metadata/quality guard removed"));
                Add("both inventory bounds remain", () => Check(lua.Contains("for b=0,4") && lua.Contains("GetContainerNumSlots(b)"), "bag/slot iteration contract changed"));
                Add("protected IDs and names remain effective inputs", () => Check(!protectedItems || lua.Contains("9001") && lua.Contains("protected name"), "explicit protections lost"));
                Add("one-stack versus legacy bulk contract remains", () => Check(one ? lua.Contains("UseContainerItem(b,s) return 'ok',1") && lua.EndsWith("return 'ok',0",StringComparison.Ordinal) : !lua.Contains("UseContainerItem(b,s) return 'ok',1"), "sale-step yield contract changed"));
            }
        }
        cases.Add(("empty quality mask is still a no-op", () => Check((string)single.Invoke(null,new object[]{ItemQuality.None,Array.Empty<string>(),Array.Empty<uint>()})! == "return 'ok',0", "empty mask changed")));
        int passed=0, assertions=0, unexpected=0;
        foreach(var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS merchant Lua contract: "+item.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL merchant Lua contract assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR merchant Lua contract fixture: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Merchant Lua contract scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual compiled script producers; no client/item effect.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Merchant Lua contracts: assertions={assertions}; unexpected={unexpected}");
    }
    private static IEnumerable<string> ReadStringLiterals(MethodInfo method)
    {
        var codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode))
            .Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(c=>unchecked((ushort)c.Value));
        byte[] bytes=method.GetMethodBody()!.GetILAsByteArray()!;
        for(int at=0;at<bytes.Length;)
        {
            ushort value=bytes[at++]; if(value==0xfe) value=(ushort)(0xfe00|bytes[at++]);
            OpCode code=codes[value];
            if(code.OperandType==OperandType.InlineString) yield return method.Module.ResolveString(BitConverter.ToInt32(bytes,at));
            int width=code.OperandType switch {
                OperandType.InlineNone=>0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
                OperandType.InlineVar=>2,
                OperandType.InlineI8 or OperandType.InlineR=>8,
                OperandType.InlineSwitch=>checked(4+4*BitConverter.ToInt32(bytes,at)),
                _=>4
            };
            at=checked(at+width); if(at>bytes.Length) throw new InvalidOperationException("Truncated IL operand");
        }
    }
    private static bool Before(string text,string first,string second) => text.IndexOf(first,StringComparison.Ordinal)>=0 && text.IndexOf(first,StringComparison.Ordinal)<text.IndexOf(second,StringComparison.Ordinal);
    private static void Check(bool value,string text) { if(!value) throw new AssertionFailure(text); }
}
