using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.IO;

internal static class GroupObservationLuaRegressionTests
{
 [ModuleInitializer] internal static void Run()
 {
  string root=Root();
  string query=(string)typeof(Styx.Logic.GroupObservation).GetField("Query",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
  using var lua=new RewardLua51Boundary.StockLua51(root);
  int passed=0,total=0;
  void Case(string name,string setup,string[] expected)
  {
   total++;using var session=lua.BeginSession(setup);
   var result=session.Execute(query,(uint)Encoding.UTF8.GetByteCount(query));
   if(result.Load!=0||result.Call!=0||!result.Values.SequenceEqual(expected))throw new InvalidOperationException(name+": "+result.Error+" values="+string.Join("|",result.Values));
   passed++;Console.WriteLine("PASS group Lua51: "+name);
  }
  string Setup(string raid,string party,string guid)=>"function GetNumRaidMembers() return "+raid+" end;function GetNumPartyMembers() return "+party+" end;function UnitGUID(u) "+guid+" end;";
  Case("known solo",Setup("0","0","return '0x1'"),new[]{"group-v1","0","0","0x1"});
  Case("party tokens retain self and all members",Setup("0","2","return u=='player' and '0x1' or u=='party1' and '0xA' or u=='party2' and '0xB'"),new[]{"group-v1","0","2","0x1","0xA","0xB"});
  Case("missing token retains empty positional field",Setup("0","2","return u=='player' and '0x1' or u=='party2' and '0xB' or nil"),new[]{"group-v1","0","2","0x1","","0xB"});
  Case("raid chooses raid tokens",Setup("2","1","return u=='player' and '0x1' or u=='raid1' and '0x1' or u=='raid2' and '0xA' or nil"),new[]{"group-v1","2","1","0x1","0x1","0xA"});
  Case("negative count rejected",Setup("-1","0","return '0x1'"),Array.Empty<string>());
  Case("fractional count rejected",Setup("0","1.5","return '0x1'"),Array.Empty<string>());
  Case("missing count rejected",Setup("nil","0","return '0x1'"),Array.Empty<string>());
  Case("oversized count rejected",Setup("41","0","return '0x1'"),Array.Empty<string>());
  Console.WriteLine($"Group Lua51 observation: {passed}/{total}; actual host query, stock Lua5.1, controlled original API leaves; no game attached.");
 }
 private static string Root(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;throw new InvalidOperationException("checkout required");}
}
