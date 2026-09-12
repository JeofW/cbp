using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.WoWInternals;

internal static class SpellLifecycleRegressionTests
{
    private const string Learned = "LEARNED_SPELL_IN_TAB";
    private const string Talent = "ACTIVE_TALENT_GROUP_CHANGED";
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly FieldInfo StartHandlers = typeof(BotEvents).GetField("_onBotStart", PrivateStatic)!;
    private static readonly FieldInfo LuaManager = typeof(Lua).GetField("_events", PrivateStatic)!;
    private static readonly FieldInfo LastCount = typeof(SpellManager).GetField("_lastKnownSpellCount", PrivateStatic)!;
    private static readonly MethodInfo Initialize = typeof(SpellManager).GetMethod("Initialize", PrivateStatic)!;
    private static readonly MethodInfo Shutdown = typeof(SpellManager).GetMethod("Shutdown", PrivateStatic)!;

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action<Fixture> Test)[]
        {
            ("initialization never accumulates bot-start callbacks", fixture =>
            {
                for (int i = 0; i < 13; i++) Initialize.Invoke(null, null);
                Check(OwnedStartCount() == 0, "TreeRoot already calls Initialize on every start; SpellManager must not subscribe a second owner");
            }),
            ("first initialization registers both spellbook events", fixture =>
            {
                Initialize.Invoke(null, null);
                Check(fixture.Owned(Learned) == 1 && fixture.Owned(Talent) == 1,
                    "the first run must not wait until a later BotStart invocation to subscribe");
                Check(fixture.Refreshes == 1, "one initialization must cause one refresh, not zero or two");
            }),
            ("thirteen actual event dispatches perform exactly thirteen refreshes", fixture =>
            {
                // The production TreeRoot handler has unrelated game/client setup.
                // Isolate its documented Initialize call; dispatch the real BotEvents
                // event and execute real SpellManager methods and Lua registrations.
                StartHandlers.SetValue(null, new BotEvents.OnBotStartDelegate(_ => Initialize.Invoke(null, null)));
                var raise = typeof(BotEvents).GetMethod("RaiseBotStart", PrivateStatic)!;
                var counts = new List<int>();
                for (int i = 0; i < 13; i++)
                {
                    int before = fixture.Refreshes;
                    raise.Invoke(null, null);
                    counts.Add(fixture.Refreshes - before);
                }
                Console.WriteLine("Per-start refresh calls: " + string.Join(",", counts));
                Check(counts.All(count => count == 1) && fixture.Refreshes == 13,
                    "each start must refresh once without the historical 1..13 growth");
                Check(((Delegate?)StartHandlers.GetValue(null))?.GetInvocationList().Length == 1,
                    "the event must retain only the isolated start owner");
                Check(fixture.Owned(Learned) == 1 && fixture.Owned(Talent) == 1,
                    "repeated starts must retain exactly one owned handler per Lua event");
            }),
            ("shutdown removes owned Lua handlers and is repeatable", fixture =>
            {
                var callback = (LuaEventHandlerDelegate)typeof(SpellManager)
                    .GetMethod("OnSpellBookChanged", PrivateStatic)!
                    .CreateDelegate(typeof(LuaEventHandlerDelegate));
                fixture.Events.AttachEvent(Learned, callback);
                fixture.Events.Reset(); // Independent of the missing-globals regression.
                fixture.Events.AttachEvent(Talent, callback);
                Shutdown.Invoke(null, null);
                Shutdown.Invoke(null, null);
                Check(fixture.Owned(Learned) == 0 && fixture.Owned(Talent) == 0,
                    "engine teardown must not retain SpellManager's Lua callbacks");
                Check(SpellManager.KnownSpells.Count == 0, "shutdown must still clear the spellbook");
            }),
            ("other Lua subscribers survive initialization and teardown", fixture =>
            {
                LuaEventHandlerDelegate other = (_, _) => { };
                fixture.Events.AttachEvent(Learned, other);
                fixture.Events.Reset();
                fixture.Events.AttachEvent(Talent, other);
                Initialize.Invoke(null, null);
                Shutdown.Invoke(null, null);
                Check(fixture.Handlers(Learned).Count(handler => handler == other) == 1
                    && fixture.Handlers(Talent).Count(handler => handler == other) == 1,
                    "SpellManager must only remove its own delegates");
            }),
            ("Lua registration tolerates absent client globals", fixture =>
            {
                LuaEventHandlerDelegate other = (_, _) => { };
                fixture.Events.AttachEvent(Learned, other);
                fixture.Events.AttachEvent(Talent, other);
                Check(!fixture.Events.IsInitialized,
                    "managed subscription is not proof that a client-side Lua table exists");
                Check(fixture.Handlers(Learned).Length == 1 && fixture.Handlers(Talent).Length == 1,
                    "both registrations must remain available for a later client initialization");
            }),
            ("learned-spell notification invokes one refresh", fixture => VerifyNotification(fixture, Learned)),
            ("talent-change notification invokes one refresh", fixture => VerifyNotification(fixture, Talent)),
            ("reinitialization after shutdown restores single ownership", fixture =>
            {
                Initialize.Invoke(null, null);
                Shutdown.Invoke(null, null);
                Initialize.Invoke(null, null);
                Check(fixture.Refreshes == 2 && OwnedStartCount() == 0
                    && fixture.Owned(Learned) == 1 && fixture.Owned(Talent) == 1,
                    "restart after explicit teardown must rebuild without duplicating callbacks");
            }),
            ("Lua reset followed by initialization keeps single ownership", fixture =>
            {
                Initialize.Invoke(null, null);
                fixture.Events.Reset();
                Initialize.Invoke(null, null);
                Check(fixture.Owned(Learned) == 1 && fixture.Owned(Talent) == 1,
                    "disconnect/reset must not duplicate managed subscriptions");
            })
        };
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try
            {
                using var fixture = new Fixture();
                test.Test(fixture);
                Console.WriteLine("PASS: " + test.Name);
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException wrapped && wrapped.InnerException != null)
                    error = wrapped.InnerException;
                failures.Add(test.Name + ": " + error.GetType().Name + " — " + error.Message);
                Console.Error.WriteLine("FAIL: " + failures[failures.Count - 1]);
            }
        }
        Console.WriteLine($"Spell lifecycle scenarios: {cases.Length - failures.Count}/{cases.Length} passed. No game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static void VerifyNotification(Fixture fixture, string eventName)
    {
        Initialize.Invoke(null, null);
        Initialize.Invoke(null, null);
        int before = fixture.Refreshes;
        foreach (var handler in fixture.Handlers(eventName))
            handler(null!, new LuaEventArgs(eventName, 0, Array.Empty<object>()));
        Check(fixture.Refreshes - before == 1, "one notification must cause exactly one refresh after repeated initialization");
    }

    private static int OwnedStartCount() => ((Delegate?)StartHandlers.GetValue(null))?
        .GetInvocationList().Count(handler => handler.Method.DeclaringType == typeof(SpellManager)) ?? 0;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object? _start = StartHandlers.GetValue(null);
        private readonly object? _lua = LuaManager.GetValue(null);
        private readonly object? _lastCount = LastCount.GetValue(null);
        private readonly Dictionary<string, WoWSpell> _spells = new(SpellManager.KnownSpells);
        private readonly bool _fileLogging = Logging.FileLogging;
        private readonly Logging.LogMessageDelegate _logHandler;
        internal LuaEvents Events { get; }
        internal int Refreshes { get; private set; }

        internal Fixture()
        {
            if (ObjectManager.Wow != null || ObjectManager.Me != null || ObjectManager.Executor != null)
                throw new InvalidOperationException("Lifecycle fixtures require an unattached process.");
            Events = (LuaEvents)Activator.CreateInstance(typeof(LuaEvents), nonPublic: true)!;
            StartHandlers.SetValue(null, null);
            LuaManager.SetValue(null, Events);
            Logging.FileLogging = false;
            _logHandler = messages => Refreshes += messages.Count(message =>
                message.Message.StartsWith("Refresh() called.", StringComparison.Ordinal));
            Logging.OnLogMessage += _logHandler;
        }

        internal LuaEventHandlerDelegate[] Handlers(string eventName)
        {
            var handlers = (Dictionary<string, LuaEventHandlerDelegate>)typeof(LuaEvents)
                .GetField("_eventHandlers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Events)!;
            return handlers.TryGetValue(eventName, out var value) && value != null
                ? value.GetInvocationList().Cast<LuaEventHandlerDelegate>().ToArray()
                : Array.Empty<LuaEventHandlerDelegate>();
        }

        internal int Owned(string eventName) => Handlers(eventName)
            .Count(handler => handler.Method.DeclaringType == typeof(SpellManager));

        public void Dispose()
        {
            Logging.OnLogMessage -= _logHandler;
            Logging.FileLogging = _fileLogging;
            StartHandlers.SetValue(null, _start);
            LuaManager.SetValue(null, _lua);
            SpellManager.KnownSpells.Clear();
            foreach (var spell in _spells) SpellManager.KnownSpells.Add(spell.Key, spell.Value);
            LastCount.SetValue(null, _lastCount);
        }
    }
}
