using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
    /// <summary>
    /// Coordinates recovery requests across routines and plugins. Native entry,
    /// client cast observations, consumed stock and recovery effects are distinct.
    /// </summary>
    public static class RecoveryActions
    {
        // The existing direct-heal continuation waits ten seconds. This is an
        // acknowledgement budget, not a predicted heal amount or health threshold.
        private const long AcknowledgementBudget = 10000;
        private const long PulseInterval = 250;
        private static readonly object Operation = new object();
        private static readonly RecoveryActionLedger Ledger = new RecoveryActionLedger();
        private static volatile Context? _context;
        [ThreadStatic] private static bool _inside;
        [ThreadStatic] private static Binding? _dispatch;

        private sealed class Context
        {
            internal readonly SpellManager.SpellObservationContext Observation;
            internal readonly LocalPlayer Actor;
            internal readonly ulong ActorGuid;
            internal readonly uint Map;
            internal readonly object Routine, Profile, Run, Bot;
            internal readonly IntPtr ProcessHandle;
            internal readonly string Token = Guid.NewGuid().ToString("N");
            internal readonly List<Binding> Bindings = new List<Binding>();
            internal volatile Binding[] PublishedBindings = Array.Empty<Binding>();
            internal bool CollectorReady;
            internal long Cursor, ProducerSequence, Lost, LastPulse, LastBlockedLog;
            internal double ClientTime;

            internal Context(SpellManager.SpellObservationContext observation, LocalPlayer actor,
                object routine, object profile)
            {
                Observation = observation; Actor = actor; ActorGuid = actor.Guid; Map = actor.MapId;
                Routine = routine; Profile = profile; Run = TreeRoot.RunIdentity; Bot = TreeRoot.Current;
                ProcessHandle = ObjectManager.Wow.ProcessHandle;
            }
            internal void Require()
            {
                Observation.RequireCurrent();
                if (!ReferenceEquals(_context, this) || !ReferenceEquals(StyxWoW.Me, Actor)
                    || !Actor.IsValid || !Actor.IsAlive || Actor.Guid != ActorGuid || Actor.MapId != Map
                    || !ReferenceEquals(RoutineManager.Current, Routine)
                    || !ReferenceEquals(ProfileManager.CurrentProfile, Profile)
                    || ObjectManager.Wow.ProcessHandle != ProcessHandle || ProcessHandle == IntPtr.Zero)
                    throw Unavailable("The recovery action owner changed.");
            }
            internal bool HasEventOwners => Bindings.Any(b => IsPending(b.Ticket)
                && (b.Ticket.Kind == RecoveryActionKind.Heal
                    || b.Ticket.Kind == RecoveryActionKind.Consumable && (b.Ticket.Resources & RecoveryResource.Health) != 0));
        }

        private sealed class Binding
        {
            internal readonly Context Context;
            internal readonly RecoveryActionTicket Ticket;
            internal readonly WoWUnit Recipient;
            internal readonly ulong RecipientGuid, NativeTargetGuid;
            internal readonly uint RecipientBase;
            internal WoWSpell? Spell;
            internal string SpellKey = "";
            internal WoWItem? Item;
            internal uint ItemEntry, ItemBase;
            internal RecoveryItemObservation? ItemBefore;
            internal RecoverySpellEvidence? Cast;
            internal readonly HashSet<int> HealEffects = new HashSet<int>();
            internal long EventBaseline;
            internal double EarliestClientTime;
            internal bool ItemHealObserved, EventGap;
            internal string? ContainerScript;

            internal Binding(Context context, RecoveryActionTicket ticket, WoWUnit recipient, ulong nativeTarget)
            {
                Context = context; Ticket = ticket; Recipient = recipient; RecipientGuid = recipient.Guid;
                RecipientBase = recipient.BaseAddress; NativeTargetGuid = nativeTarget;
            }
            internal void RequireRecipient()
            {
                Context.Require();
                if (!Recipient.IsValid || !Recipient.IsAlive || Recipient.Guid != RecipientGuid
                    || Recipient.BaseAddress != RecipientBase)
                    throw Unavailable("The recovery recipient changed.");
            }
            internal void RequireNative()
            {
                RequireRecipient();
                if (Spell != null)
                {
                    if (!SpellManager.Spells.TryGetValue(SpellKey, out var learned)
                        || !ReferenceEquals(learned, Spell) || !learned.IsValid || learned.Id != Ticket.SpellId)
                        throw Unavailable("The learned spell changed before native entry.");
                    if ((Spell.AttributesEx & 0x50U) != 0)
                    {
                        var comboTarget = Context.Actor.CurrentTarget;
                        if (comboTarget == null || !comboTarget.IsValid || comboTarget.Guid != NativeTargetGuid)
                            throw Unavailable("The combo cast target changed before native entry.");
                    }
                }
                if (Item != null && (!Item.IsValid || Item.Guid != Ticket.ItemGuid
                    || Item.Entry != ItemEntry || Item.BaseAddress != ItemBase || Item.OwnerGuid != Context.ActorGuid))
                    throw Unavailable("The carried recovery item changed before native entry.");
            }
        }

        static RecoveryActions()
        {
            BotEvents.OnBotStopped += _ =>
            {
                // Managed revocation is synchronous. No native cleanup is issued
                // from shutdown; a later owned collector install retires its frame.
                var context = _context;
                if (context != null && !TreeRoot.IsRunning)
                    Ledger.Revoke(context, Environment.TickCount64);
            };
        }

        public static bool TryCast(string spellName, WoWUnit target, bool heal, bool aura, string owner)
            => TryCastCore(spellName, 0, target, heal, aura, owner);

        public static bool TryCast(int spellId, WoWUnit target, bool heal, bool aura, string owner)
            => spellId > 0 && TryCastCore(null, spellId, target, heal, aura, owner);

        private static bool TryCastCore(string? spellName, int requestedId, WoWUnit target, bool heal, bool aura, string owner)
        {
            return Run(context =>
            {
                if (requestedId > 0)
                {
                    spellName = SpellManager.Spells.FirstOrDefault(entry => entry.Value != null
                        && entry.Value.Id == requestedId).Key;
                    context.Require();
                }
                if (target == null || !target.IsValid || target.Guid == 0
                    || string.IsNullOrEmpty(spellName) || !SpellManager.Spells.TryGetValue(spellName, out var spell)
                    || spell == null || !spell.IsValid || requestedId > 0 && spell.Id != requestedId)
                    return false;
                var effects = spell.SpellEffects;
                if (effects == null || effects.Length != 3 || effects.Any(e => e == null))
                    throw Unavailable("The spell's complete effect metadata is unavailable.");
                heal |= effects.Any(e => IsDirectHeal(e.EffectType));
                if (!heal && !aura)
                {
                    context.Require();
                    return SpellManager.Cast(spell, target);
                }

                // Resurrection has its own host admission and may target a
                // corpse. Only an ordinary recovery reservation requires alive.
                if (!target.IsAlive) return false;

                Pump(context, false);
                if (!Ledger.CanPrepare(context, heal ? RecoveryActionKind.Heal : RecoveryActionKind.Aura,
                    spell.Id, target.Guid, heal ? RecoveryResource.Health : RecoveryResource.None, Environment.TickCount64))
                {
                    ReportBlocked(context, owner);
                    return false;
                }
                if (aura && !heal)
                {
                    var present = AuraPresent(context, target, spell.Id);
                    if (present == true) return false;
                    if (!present.HasValue) throw Unavailable("Complete pre-cast aura coverage is unavailable.");
                }
                if (heal && !PrepareEventBaseline(context)) return false;
                var identity = Lua.GetObservedReturnValues(RecoveryActionLua.SpellIdentity(spell.Id, context.ActorGuid));
                context.Require();
                if (identity.Count != 7 || identity[0] != "recovery-spell"
                    || identity[1] != spell.Id.ToString(CultureInfo.InvariantCulture)
                    || !RecoveryActionEvidence.GuidValue(identity[2], out ulong actor) || actor != context.ActorGuid
                    || string.IsNullOrEmpty(identity[3]) || identity[3] != spell.Name || identity[4] == null
                    || !RecoveryActionEvidence.Finite(identity[5], out double castTime)
                    || !RecoveryActionEvidence.Finite(identity[6], out double clientTime)
                    || castTime > RecoveryActionLedger.MaximumBudgetMilliseconds)
                    throw Unavailable("The spell identity or original-client cast time is incomplete.");
                ulong nativeTarget = target.Guid;
                if ((spell.AttributesEx & 0x50U) != 0)
                {
                    var comboTarget = context.Actor.CurrentTarget;
                    if (comboTarget == null || !comboTarget.IsValid || comboTarget.Guid == 0) return false;
                    nativeTarget = comboTarget.Guid;
                }
                context.Require();
                long now = Environment.TickCount64;
                if (!Ledger.TryPrepare(context, heal ? RecoveryActionKind.Heal : RecoveryActionKind.Aura,
                    spell.Id, target.Guid, 0, heal ? RecoveryResource.Health : RecoveryResource.None,
                    now, Math.Max(AcknowledgementBudget, (long)Math.Ceiling(castTime)), owner, out var ticket))
                {
                    ReportBlocked(context, owner);
                    return false;
                }
                var binding = new Binding(context, ticket!, target, nativeTarget)
                {
                    Spell = spell, SpellKey = spellName, EventBaseline = context.ProducerSequence,
                    EarliestClientTime = clientTime,
                    Cast = heal ? new RecoverySpellEvidence(spell.Id, identity[3], identity[4], context.ActorGuid,
                        target.Guid, context.ProducerSequence, clientTime, castTime == 0) : null
                };
                context.Bindings.Add(binding);
                context.PublishedBindings = context.Bindings.ToArray();
                var previous = _dispatch;
                _dispatch = binding;
                try
                {
                    binding.RequireNative();
                    if (aura && !heal)
                    {
                        var present = AuraPresent(context, target, spell.Id);
                        if (present == true) return false;
                        if (!present.HasValue) throw Unavailable("The final pre-cast aura observation is unavailable.");
                    }
                    return SpellManager.Cast(spell, target);
                }
                finally
                {
                    if (ReferenceEquals(_dispatch, binding)) _dispatch = previous;
                    Ledger.RejectUnsubmitted(ticket!, context, Environment.TickCount64);
                    Prune(context);
                }
            }, owner);
        }

        public static bool TryUseConsumable(WoWItem item, bool health, bool mana, string owner)
        {
            return Run(context =>
            {
                if (item == null || !item.IsValid || item.Guid == 0 || item.Entry == 0
                    || item.OwnerGuid != context.ActorGuid || (!health && !mana)) return false;
                Pump(context, false);
                var info = item.ItemInfo;
                if (info == null || info.SpellId == null || info.SpellId.Length != 5)
                    throw Unavailable("The carried item's complete effect slots are unavailable.");
                var healEffects = new HashSet<int>();
                foreach (int id in info.SpellId.Where(id => id != 0))
                {
                    var effectSpell = WoWSpell.FromId(id);
                    if (effectSpell == null || !effectSpell.IsValid)
                        throw Unavailable("A carried item effect could not be resolved. Spell=" + id);
                    var effects = effectSpell.SpellEffects;
                    if (effects == null || effects.Length != 3 || effects.Any(e => e == null))
                        throw Unavailable("A carried item effect is incomplete. Spell=" + id);
                    if (effects.Any(e => IsDirectHeal(e.EffectType))) { health = true; healEffects.Add(id); }
                    // Uninterpreted effects cannot establish that a mana candidate
                    // has no health consequence. Reserve health conservatively.
                    if (effects.Any(e => e.EffectType != WoWSpellEffectType.None
                        && e.EffectType != WoWSpellEffectType.Energize && e.EffectType != WoWSpellEffectType.EnergizePct
                        && !IsDirectHeal(e.EffectType))) health = true;
                }
                var resources = (health ? RecoveryResource.Health : RecoveryResource.None)
                    | (mana ? RecoveryResource.Mana : RecoveryResource.None);
                if (!Ledger.CanPrepare(context, RecoveryActionKind.Consumable, 0, context.ActorGuid,
                    resources, Environment.TickCount64))
                {
                    ReportBlocked(context, owner);
                    return false;
                }
                if (health && !PrepareEventBaseline(context)) return false;
                var values = Lua.GetObservedReturnValues(RecoveryActionLua.ItemSnapshot(item.Entry, context.ActorGuid));
                context.Require();
                if (!RecoveryActionEvidence.TryParseItem(values, item.Entry, context.ActorGuid, out var before))
                    throw Unavailable("The carried item quantity or cooldown is unavailable.");
                if (!before!.IsReady || before.Count <= 0) return false;
                if (!Ledger.TryPrepare(context, RecoveryActionKind.Consumable, 0, context.ActorGuid,
                    item.Guid, resources, Environment.TickCount64, AcknowledgementBudget, owner, out var ticket))
                {
                    ReportBlocked(context, owner);
                    return false;
                }
                var binding = new Binding(context, ticket!, context.Actor, context.ActorGuid)
                {
                    Item = item, ItemEntry = item.Entry, ItemBase = item.BaseAddress, ItemBefore = before,
                    EventBaseline = context.ProducerSequence, EarliestClientTime = before.ClientTime
                };
                binding.HealEffects.UnionWith(healEffects);
                context.Bindings.Add(binding);
                context.PublishedBindings = context.Bindings.ToArray();
                var previous = _dispatch;
                _dispatch = binding;
                try
                {
                    binding.RequireNative();
                    return item.TryUseContainerItem();
                }
                finally
                {
                    if (ReferenceEquals(_dispatch, binding)) _dispatch = previous;
                    Ledger.RejectUnsubmitted(ticket!, context, Environment.TickCount64);
                    Prune(context);
                }
            }, owner);
        }

        /// <summary>Maintain acknowledgements from the shared pulse even when a routine yields.</summary>
        public static void Pulse() { Run(context => { Pump(context, false); return true; }, "shared recovery pulse"); }

        /// <summary>Preserve explicit cancellation and actual process/executor ownership loss.</summary>
        public static void RethrowControlFlow(Exception error) { PreserveControl(error, _dispatch?.Context); }

        /// <summary>Share bounded optional-action diagnostics with runtime-compiled consumers.</summary>
        public static void ReportDeferral(Exception error, string owner)
        {
            RethrowControlFlow(error);
            Report(error, owner, _dispatch?.Context);
        }

        // These hooks run immediately before native entry. They do not issue Lua,
        // log, or rebuild assembly while an executor holds prepared instructions.
        internal static bool BeforeSpellSubmission(int spellId, ulong targetGuid)
        {
            var binding = _dispatch;
            if (binding == null)
            {
                var context = _context;
                if (context == null || !ReferenceEquals(context.Run, TreeRoot.RunIdentity)
                    || !ReferenceEquals(context.Actor, StyxWoW.Me) || context.Actor.MapId != context.Map
                    || !ReferenceEquals(context.Routine, RoutineManager.Current)
                    || !ReferenceEquals(context.Profile, ProfileManager.CurrentProfile)
                    || ObjectManager.Wow?.ProcessHandle != context.ProcessHandle) return true;
                return !context.PublishedBindings.Any(b => IsPending(b.Ticket) && b.Ticket.SpellId == spellId
                    && b.NativeTargetGuid == targetGuid && b.Ticket.WasSubmitted);
            }
            if (binding.Spell == null || binding.Ticket.SpellId != spellId || binding.NativeTargetGuid != targetGuid) return false;
            binding.RequireNative();
            return Ledger.BeginSubmission(binding.Ticket, binding.Context, Environment.TickCount64);
        }

        internal static bool BindContainerRequest(ulong itemGuid, uint entry, string script)
        {
            var binding = _dispatch;
            if (binding == null) return true;
            if (binding.Item == null || binding.Ticket.ItemGuid != itemGuid || binding.ItemEntry != entry
                || string.IsNullOrEmpty(script)) return false;
            binding.RequireNative();
            binding.ContainerScript = script;
            return true;
        }

        internal static bool BeforeLuaSubmission(string script)
        {
            var binding = _dispatch;
            if (binding?.Item == null || binding.ContainerScript != script) return true;
            binding.RequireNative();
            return Ledger.BeginSubmission(binding.Ticket, binding.Context, Environment.TickCount64);
        }

        internal static void ObserveContainerReply(string script, bool executed)
        {
            var binding = _dispatch;
            if (!executed && binding?.Item != null && binding.ContainerScript == script)
                Ledger.RejectKnownUnexecuted(binding.Ticket, binding.Context, Environment.TickCount64);
        }

        private static bool Run(Func<Context, bool> action, string owner)
        {
            if (_inside || !Monitor.TryEnter(Operation)) return false;
            _inside = true;
            Context? context = null;
            try
            {
                var actor = StyxWoW.Me;
                if (!TreeRoot.IsRunning || actor == null || !actor.IsAlive)
                {
                    RetireCurrent();
                    return false;
                }
                context = GetContext();
                bool result = action(context);
                EmitTransitions(context);
                context.Require();
                return result;
            }
            catch (Exception error)
            {
                PreserveControl(error, context);
                Report(error, owner, context);
                return false;
            }
            finally
            {
                _inside = false;
                Monitor.Exit(Operation);
            }
        }

        private static Context GetContext()
        {
            var observation = SpellManager.CaptureSpellObservation();
            var actor = StyxWoW.Me;
            var routine = RoutineManager.Current;
            var profile = ProfileManager.CurrentProfile;
            observation.RequireCurrent();
            var old = _context;
            if (old != null && old.Observation.SameOwner(observation) && ReferenceEquals(old.Actor, actor)
                && ReferenceEquals(old.Routine, routine) && ReferenceEquals(old.Profile, profile)
                && old.ProcessHandle == ObjectManager.Wow.ProcessHandle)
            {
                old.Require();
                Ledger.Advance(old, Environment.TickCount64);
                return old;
            }
            if (old != null) Ledger.Revoke(old, Environment.TickCount64);
            var context = new Context(observation, actor, routine, profile);
            _context = context;
            context.Require();
            Ledger.Advance(context, Environment.TickCount64);
            return context;
        }

        private static bool PrepareEventBaseline(Context context)
        {
            Prune(context);
            if (context.CollectorReady && context.HasEventOwners)
                return ReadEvents(context);
            var values = Lua.GetObservedReturnValues(RecoveryActionLua.Install(context.Token, context.ActorGuid));
            context.Require();
            if (!RecoveryActionEvidence.TryParseBatch(values, context.Token, out var baseline) || baseline!.Events.Count != 0)
                throw Unavailable("A complete recovery event baseline is unavailable.");
            context.CollectorReady = true;
            context.Cursor = context.ProducerSequence = baseline.LastSequence;
            context.Lost = baseline.LostEvents;
            context.ClientTime = baseline.ClientTime;
            return true;
        }

        private static void Pump(Context context, bool force)
        {
            long now = Environment.TickCount64;
            Ledger.Advance(context, now);
            Prune(context);
            if (!force && now >= context.LastPulse && now - context.LastPulse < PulseInterval) return;
            context.LastPulse = now;
            if (context.CollectorReady && context.HasEventOwners)
            {
                try { ReadEvents(context); }
                catch (Exception error) { PreserveControl(error, context); Report(error, "recovery event observation", context); }
            }
            foreach (var binding in context.Bindings.ToArray())
            {
                if (!IsPending(binding.Ticket) || !binding.Ticket.WasSubmitted) continue;
                try
                {
                    context.Require();
                    if (binding.Ticket.Kind == RecoveryActionKind.Heal)
                    {
                        if (binding.Cast!.Started) Ledger.Casting(binding.Ticket, context, now);
                        if (binding.Cast.Succeeded) Ledger.CastSucceeded(binding.Ticket, context, now);
                        Ledger.Observe(binding.Ticket, context, now, binding.Cast.Acknowledged,
                            binding.Cast.Interrupted && !binding.Cast.Ambiguous);
                    }
                    else if (binding.Ticket.Kind == RecoveryActionKind.Aura)
                    {
                        binding.RequireRecipient();
                        Ledger.Observe(binding.Ticket, context, now,
                            AuraPresent(context, binding.Recipient, binding.Ticket.SpellId, true), false);
                    }
                    else
                    {
                        var values = Lua.GetObservedReturnValues(RecoveryActionLua.ItemSnapshot(binding.ItemEntry, context.ActorGuid));
                        context.Require();
                        if (!RecoveryActionEvidence.TryParseItem(values, binding.ItemEntry, context.ActorGuid, out var after))
                            throw Unavailable("The submitted consumable outcome is unavailable.");
                        bool health = (binding.Ticket.Resources & RecoveryResource.Health) != 0;
                        bool acknowledged = after!.Acknowledges(binding.ItemBefore!)
                            && (!health || binding.ItemHealObserved && !binding.EventGap);
                        Ledger.Observe(binding.Ticket, context, Environment.TickCount64, acknowledged, false);
                    }
                }
                catch (Exception error) { PreserveControl(error, context); Report(error, binding.Ticket.Owner, context); }
            }
            Prune(context);
        }

        private static bool ReadEvents(Context context)
        {
            for (int round = 0; round < 4; round++)
            {
                var values = Lua.GetObservedReturnValues(RecoveryActionLua.Poll(context.Token, context.ActorGuid, context.Cursor));
                context.Require();
                if (!RecoveryActionEvidence.TryParseBatch(values, context.Token, out var batch)
                    || batch!.ClientTime < context.ClientTime || batch.LastSequence < context.ProducerSequence
                    || batch.LostEvents < context.Lost)
                    throw Unavailable("The recovery event reply is incomplete or its collector generation changed.");
                bool gap = batch.LostEvents != context.Lost
                    || batch.Events.Count > 0 && batch.Events[0].Sequence != context.Cursor + 1;
                if (gap)
                    foreach (var binding in context.Bindings)
                    {
                        binding.EventGap = true;
                        binding.Cast?.MarkUnavailable();
                    }
                context.ProducerSequence = batch.LastSequence;
                context.ClientTime = batch.ClientTime;
                context.Lost = batch.LostEvents;
                foreach (var value in batch.Events)
                {
                    foreach (var binding in context.Bindings)
                    {
                        if (!binding.Ticket.WasSubmitted || !IsPending(binding.Ticket)) continue;
                        binding.Cast?.Observe(value);
                        if (binding.Item != null && !binding.EventGap && value.Kind == "HEAL"
                            && value.Sequence > binding.EventBaseline && value.ClientTime >= binding.EarliestClientTime
                            && value.SourceGuid == context.ActorGuid && value.TargetGuid == context.ActorGuid
                            && binding.HealEffects.Contains(value.SpellId))
                            binding.ItemHealObserved = true;
                    }
                    context.Cursor = value.Sequence;
                }
                if (batch.Events.Count < RecoveryActionEvidence.MaximumBatchEvents) break;
            }
            return true;
        }

        private static bool? AuraPresent(Context context, WoWUnit target, int spellId, bool requireOwnCaster = false)
        {
            context.Require();
            if (!target.IsValid || target.Guid == 0) return null;
            ulong guid = target.Guid;
            uint address = target.BaseAddress;
            if (!target.TryGetAllAuras(out var auras, "recovery acknowledgement") || auras == null) return null;
            // Existing coverage blocks duplicate application regardless of its
            // caster. Only an owned caster can acknowledge our submitted action.
            bool found = auras.Any(a => a.SpellId == spellId && (!requireOwnCaster || a.CreatorGuid == context.ActorGuid));
            context.Require();
            return target.IsValid && target.Guid == guid && target.BaseAddress == address ? found : null;
        }

        private static bool IsDirectHeal(WoWSpellEffectType effect) => effect == WoWSpellEffectType.Heal
            || effect == WoWSpellEffectType.HealMaxHealth || effect == WoWSpellEffectType.HealPct
            || effect == WoWSpellEffectType.HealMechanical;
        private static bool IsPending(RecoveryActionTicket ticket) => ticket.State is RecoveryActionState.Prepared
            or RecoveryActionState.Submitted or RecoveryActionState.Casting or RecoveryActionState.AwaitingEffect;
        private static void Prune(Context context)
        {
            context.Bindings.RemoveAll(b => !IsPending(b.Ticket));
            context.PublishedBindings = context.Bindings.ToArray();
        }
        private static ObservationUnavailableException Unavailable(string reason) => new ObservationUnavailableException("recovery-action", reason);

        private static void RetireCurrent()
        {
            var old = _context;
            if (old == null) return;
            Ledger.Revoke(old, Environment.TickCount64);
            if (ReferenceEquals(_context, old)) _context = null;
        }
        private static void PreserveControl(Exception error, Context? context)
        {
            while (error is TargetInvocationException { InnerException: not null } wrapped) error = wrapped.InnerException;
            if (error is OperationCanceledException or ThreadInterruptedException or InvalidProcessException or InvalidExecutorException)
            {
                if (context != null) Ledger.Revoke(context, Environment.TickCount64);
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
        private static void Report(Exception error, string owner, Context? context)
        {
            var unavailable = ObservationUnavailableException.Find(error)
                ?? Unavailable("Optional recovery action deferred: " + error.GetType().Name);
            try { ObservationFailureDiagnostics.Report(unavailable, owner); }
            catch (Exception sink) { PreserveControl(sink, context); }
        }
        private static void EmitTransitions(Context context)
        {
            foreach (var transition in Ledger.DrainTransitions())
                SafeLog($"[RecoveryAction] generation={transition.Generation} kind={transition.Kind} state={transition.State} "
                    + $"spell={transition.SpellId} target={transition.TargetGuid:X16} item={transition.ItemGuid:X16} "
                    + $"owner={transition.Owner} created={transition.CreatedAt} submitted={transition.SubmittedAt} "
                    + $"deadline={transition.Deadline} blocked={transition.BlockedCount} firstBlocked={transition.FirstBlockedAt} "
                    + $"lastBlocked={transition.LastBlockedAt} reason={transition.Reason}", context);
        }
        private static void ReportBlocked(Context context, string owner)
        {
            long now = Environment.TickCount64;
            if (context.LastBlockedLog != 0 && now >= context.LastBlockedLog && now - context.LastBlockedLog < 30000) return;
            context.LastBlockedLog = now;
            SafeLog("[RecoveryAction] deferred owner=" + owner + " reason=action-in-flight; current recovery outcome remains pending", context);
        }
        private static void SafeLog(string message, Context? context)
        {
            try { Logging.WriteDiagnostic(message); }
            catch (Exception error) { PreserveControl(error, context); }
        }
    }
}
