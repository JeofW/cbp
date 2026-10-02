using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

// Execute the production script generator in stock Lua 5.1. Only client clock
// and dismount effects are controlled; no game process or unmount is simulated.
internal static class GroundDismountLuaRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        MethodInfo builder = typeof(Styx.Logic.Mount).GetMethod("BuildGroundDismountLua", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actual production dismount script generator is required.");
        const string owner = "0123456789abcdef0123456789abcdef";
        const string nextOwner = "fedcba9876543210fedcba9876543210";
        string Script(string action = "Dismount()", string token = owner)
            => (string)builder.Invoke(null, new object[] { action, token })!;
        using var lua = new RewardLua51Boundary.StockLua51(root);
        int total = 0, passed = 0, assertions = 0, unexpected = 0;
        void Case(string name, Action<RewardLua51Boundary.StockLua51.Session> body)
        {
            total++;
            using var session = lua.BeginSession(Setup);
            try { body(session); passed++; Console.WriteLine("PASS dismount Lua51: " + name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL dismount Lua51: " + name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR dismount Lua51: " + name + ": " + error); }
        }
        RewardLua51Boundary.Observation Execute(RewardLua51Boundary.StockLua51.Session session, string code)
            => session.Execute(code, checked((uint)Encoding.UTF8.GetByteCount(code)));
        void Set(RewardLua51Boundary.StockLua51.Session session, string code)
        {
            var result = Execute(session, code);
            Check(result.Load == 0 && result.Call == 0, "controlled observation setup failed: " + result.Error);
        }
        void Receipt(RewardLua51Boundary.StockLua51.Session session, string code, string expected, int calls)
        {
            var result = Execute(session, code);
            Check(result.Load == 0 && result.Call == 0 && result.Values.SequenceEqual(new[] { expected }) && result.Clicks == calls,
                "receipt=" + string.Join("|", result.Values) + "; calls=" + result.Clicks + "; error=" + result.Error);
        }

        Case("one submitted request and stable pending owner", session =>
        {
            Receipt(session, Script(), "cb-ground-dismount-submitted", 1);
            Receipt(session, Script(), "cb-ground-dismount-pending", 1);
            Set(session, "now=111.999");
            Receipt(session, Script(), "cb-ground-dismount-pending", 1);
            Check(Execute(session, "return mounted and 'mounted' or 'unmounted'").Values.Single() == "mounted",
                "local submission fabricated observed mount removal");
        });
        Case("client lease permits one new request only at expiry", session =>
        {
            Receipt(session, Script(), "cb-ground-dismount-submitted", 1);
            Set(session, "now=112");
            Receipt(session, Script(), "cb-ground-dismount-submitted", 2);
            Receipt(session, Script(), "cb-ground-dismount-pending", 2);
        });
        Case("form removal preserves the same one-shot lease", session =>
        {
            Receipt(session, Script("CancelShapeshiftForm()"), "cb-ground-dismount-submitted", 1);
            Receipt(session, Script("CancelShapeshiftForm()"), "cb-ground-dismount-pending", 1);
            Check(Execute(session, "return forms,dismounts").Values.SequenceEqual(new[] { "1", "0" }),
                "flight form dispatched a different action");
        });
        Case("post-entry action error keeps the pending lease", session =>
        {
            Set(session, "failAction=true");
            var failed = Execute(session, Script());
            Check(failed.Load == 0 && failed.Call != 0 && failed.Clicks == 1, "controlled effect did not fail after entry");
            Set(session, "failAction=false");
            Receipt(session, Script(), "cb-ground-dismount-pending", 1);
            Set(session, "now=113");
            Receipt(session, Script(), "cb-ground-dismount-submitted", 2);
        });
        Case("fresh actor-session token does not inherit old authority", session =>
        {
            Receipt(session, Script(), "cb-ground-dismount-submitted", 1);
            Receipt(session, Script(token: nextOwner), "cb-ground-dismount-submitted", 2);
            Receipt(session, Script(token: nextOwner), "cb-ground-dismount-pending", 2);
        });
        foreach (string clock in new[] { "nil", "'unknown'", "-1", "0/0", "math.huge", "-math.huge" })
        {
            string value = clock;
            Case("invalid client clock is unavailable/" + value, session =>
            {
                Set(session, "now=" + value);
                var result = Execute(session, Script());
                Check(result.Load == 0 && result.Call != 0 && result.Clicks == 0, "invalid client clock authorized a dismount");
                Check(Execute(session, "return _G.CopilotBuddy_GroundDismountLease==nil and 'absent' or 'present'").Values.Single() == "absent",
                    "failed clock created a pending client lease");
            });
        }
        foreach (string foreign in new[] { "'foreign'", "42", "{owner='foreign',untilAt=999}" })
        {
            string value = foreign;
            Case("foreign global is preserved/" + value, session =>
            {
                Set(session, "foreign=" + value + "; _G.CopilotBuddy_GroundDismountLease=foreign");
                var result = Execute(session, Script());
                Check(result.Load == 0 && result.Call != 0 && result.Clicks == 0, "foreign state authorized an effect or was overwritten");
                Check(Execute(session, "return _G.CopilotBuddy_GroundDismountLease==foreign and 'same' or 'replaced'").Values.Single() == "same",
                    "foreign Lua global was clobbered");
            });
        }
        Case("backwards clock cannot resubmit an expired-looking lease", session =>
        {
            Receipt(session, Script(), "cb-ground-dismount-submitted", 1);
            Set(session, "now=90");
            var result = Execute(session, Script());
            Check(result.Load == 0 && result.Call != 0 && result.Clicks == 1, "clock reversal became an ordinary pending or submitted receipt");
        });
        Case("corrupt retained expiry stays unavailable", session =>
        {
            Receipt(session, Script(), "cb-ground-dismount-submitted", 1);
            Set(session, "_G.CopilotBuddy_GroundDismountLease.untilAt=0/0");
            var result = Execute(session, Script());
            Check(result.Load == 0 && result.Call != 0 && result.Clicks == 1, "malformed pending state allowed a duplicate effect");
        });
        Console.WriteLine($"Ground dismount Lua51 scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; production script generator and stock Lua5.1; controlled client clock/action only, no native game dispatch or observed unmount.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Ground dismount Lua51 regression");
    }

    private static void Check(bool condition, string reason) { if (!condition) throw new Failure(reason); }
    private static string Root()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "CopilotBuddy.csproj"))) return path.FullName;
        throw new InvalidOperationException("Tracked checkout required.");
    }
    private const string Setup = """
now=100
clicks=0
dismounts=0
forms=0
mounted=true
failAction=false
function GetTime() return now end
function Dismount() clicks=clicks+1;dismounts=dismounts+1;if failAction then error('controlled post-entry failure') end end
function CancelShapeshiftForm() clicks=clicks+1;forms=forms+1;if failAction then error('controlled post-entry failure') end end
""";
}
