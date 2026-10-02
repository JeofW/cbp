#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using GreenMagic;
using Styx.Helpers;
using Styx.Patchables;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
    public class WoWSpell : IEquatable<WoWSpell>
    {
        private readonly int _id;
        private readonly SpellEntry _spellEntry;
        private readonly WoWDb.Row _row;

        private static readonly Dictionary<int, SpellInfoCache> _spellInfoCache;

        static WoWSpell()
        {
            _spellInfoCache = new Dictionary<int, SpellInfoCache>();
        }

        private WoWSpell(int id, WoWDb.Row row)
        {
            _id = id;
            _row = row;
            _spellEntry = row.GetStruct<SpellEntry>();
        }

        // NOTE: ASM injection for cooldown removed - using Lua instead (see Cooldown property)
        // The old GetSpellCooldown() method caused crashes (InjectionFinishedEvent never fired)

        public bool IsValid
        {
            get { return _id != 0 && _row != null; }
        }

        public uint BaseLevel
        {
            get { return _spellEntry.baseLevel; }
        }

        public uint Level
        {
            get { return _spellEntry.spellLevel; }
        }

        private uint RangeIndex
        {
            get { return _spellEntry.rangeIndex; }
        }

        /// <summary>
        /// Gets the spell range ID for combat routine range checks.
        /// Uses Lua-based MinRange/MaxRange which are reliable and match what
        /// Singular expects: 1 = Self Only (max=0), 2 = Melee (max≤5), 3+ = Ranged.
        /// Note: DBC rangeIndex is available via RangeIndex property for DBC lookups.
        /// </summary>
        public uint SpellRangeId
        {
            get
            {
                float min = MinRange;
                float max = MaxRange;

                // Self-only spells (buffs, melee with max=0 from Lua)
                if (max == 0f && min == 0f)
                    return 1U;

                // Melee range spells (MaxRange <= 5 yards)
                if (max > 0f && max <= 5f)
                    return 2U;

                return 3U;
            }
        }

        /// <summary>
        /// Returns true if this is a melee-range spell (SpellRangeId == 2).
        /// Does NOT include self-only spells (RangeId == 1). Matches HB 4.3.4.
        /// </summary>
        public bool IsMeleeSpell => SpellRangeId == 2;

        /// <summary>
        /// Returns true if this spell can only target self.
        /// FEAT-08: Uses synthetic rangeId (Lua-based MaxRange == 0).
        /// </summary>
        public bool IsSelfOnlySpell => SpellRangeId == 1;

        public uint ManaCostPercent
        {
            get { return _spellEntry.ManaCostPercentage; }
        }

        /// <summary>
        /// Returns true if this spell is channeled.
        /// Checks AttributesEx for channeled flag (0x44).
        /// </summary>
        public bool IsChanneled
        {
            get
            {
                // BUG-01 fix: Was checking Attributes, must check AttributesEx
                return (_spellEntry.AttributesEx & 0x44) != 0;
            }
        }

        public uint AttributesEx
        {
            get { return _spellEntry.AttributesEx; }
        }

        public int Id
        {
            get { return _id; }
        }

        public uint Category
        {
            get { return _spellEntry.Category; }
        }

        public WoWDispelType DispelType
        {
            get { return (WoWDispelType)_spellEntry.Dispel; }
        }

        public WoWSpellMechanic Mechanic
        {
            get { return (WoWSpellMechanic)_spellEntry.Mechanic; }
        }

        public uint MaxTargets
        {
            get { return _spellEntry.MaxAffectedTargets; }
        }

        public WoWCreatureType TargetType
        {
            get { return (WoWCreatureType)_spellEntry.TargetCreatureType; }
        }

        public int CreatesItemId
        {
            get { return (int)_spellEntry.EffectItemType[0]; }
        }

        public SpellEffect SpellEffect1
        {
            get { return GetSpellEffect(0); }
        }

        public SpellEffect SpellEffect2
        {
            get { return GetSpellEffect(1); }
        }

        public SpellEffect SpellEffect3
        {
            get { return GetSpellEffect(2); }
        }

        public SpellEffect[] SpellEffects
        {
            get
            {
                SpellEffect[] effects = new SpellEffect[3];
                for (int i = 0; i < 3; i++)
                {
                    effects[i] = GetSpellEffect(i);
                }
                return effects;
            }
        }

        public WoWPowerType PowerType
        {
            get { return (WoWPowerType)_spellEntry.powerType; }
        }

        public SpellEntry InternalInfo
        {
            get { return _spellEntry; }
        }

        public int PowerCost
        {
            get { return GetCachedSpellInfo().PowerCost; }
        }

        public bool IsFunnel
        {
            get { return GetCachedSpellInfo().IsFunnel; }
        }

        public uint CastTime
        {
            get { return GetCachedSpellInfo().CastTime; }
        }

        public float MinRange
        {
            get { return GetCachedSpellInfo().MinRange; }
        }

        public float MaxRange
        {
            get { return GetCachedSpellInfo().MaxRange; }
        }

        public uint MaxStackCount
        {
            get { return _spellEntry.StackAmount; }
        }

        public string Name
        {
            get 
            { 
                // Use SpellDb to avoid Lua calls which can crash the game
                return SpellDb.GetSpellName(Id);
            }
        }

        public string Rank
        {
            get 
            { 
                // Use SpellDb to avoid Lua calls which can crash the game
                return SpellDb.GetSpellRank(Id);
            }
        }

        public string Tooltip
        {
            get { return ObjectManager.Wow.Read<string>(_spellEntry.ToolTip); }
        }

        public string Description
        {
            get { return ObjectManager.Wow.Read<string>(_spellEntry.Description); }
        }

        // HB 4.3.4 WoWSpell.cs line 426: delegates to CooldownTimeLeft
        public bool Cooldown
        {
            get
            {
                if (Id <= 0)
                    throw new ObservationUnavailableException("spell-cooldown", "Invalid spell identity for native cooldown observation.");
                var observation = SpellManager.CaptureSpellObservation();
                // Primary: ASM call to Spell_C__GetSpellCooldown — HB 3.3.5a method_0 pattern.
                // Language-independent, no Lua involved, no timing race.
                // ecx = SpellCooldownPtr (0xD3F5AC), args: push 0,0,0,0,spellId; call 0x807980
                try
                {
                    ExecutorRand? executor = ObjectManager.Executor;
                    if (executor != null)
                    {
                        lock (executor.AssemblyLock)
                        {
                            executor.Clear();
                            executor.AddLine("push 0");
                            executor.AddLine("push 0");
                            executor.AddLine("push 0");
                            executor.AddLine("push 0");
                            executor.AddLine("push {0}", _id);
                            executor.AddLine("mov ecx, {0}", 0xD3F5ACU); // SpellCooldownPtr
                            executor.AddLine("call {0}", 8419712U);       // Spell_C__GetSpellCooldown = 0x807980
                            executor.AddLine("retn");
                            observation.RequireCurrent();
                            executor.Execute();
                            observation.RequireCurrent();
                            int result = executor.Memory.Read<int>(executor.ReturnPointer);
                            observation.RequireCurrent();
                            if (result != 0) return true;
                            // Original build12340 also returns zero when its
                            // Spell-table lookup fails. A zero needs a complete
                            // current metadata/cooldown reply before it is ready.
                        }
                    }
                }
                catch (ObservationUnavailableException) { throw; }
                catch (Exception error)
                {
                    ObservationUnavailableException.RethrowCancellation(error);
                    // Fall through to Lua-based check
                }
                // A localized, marked reply separates zero from unavailable.
                observation.RequireCurrent();
                return CooldownTimeLeft.TotalMilliseconds > 0.0;
            }
        }

        /// <summary>
        /// Gets the remaining cooldown time for this spell.
        /// WotLK 3.3.5a: GetSpellCooldown() requires the spell name in the CLIENT'S LANGUAGE.
        /// Passing the English name fails on non-English clients (e.g. "Blood Fury" on a Russian
        /// client where the spell is "\208\175\209\128\208\190\209\129\209\130\209\140 \208\186\209\128\208\190\208\178\208\184").
        /// Fix: use GetSpellInfo(id) to get the localized name first — language-independent.
        /// </summary>
        public TimeSpan CooldownTimeLeft
        {
            get
            {
                return SpellManager.GetSpellCooldownTimeLeft(Id);
            }
        }

        public uint BaseCooldown
        {
            get { return _spellEntry.StartRecoveryTime; }
        }

        public bool HasRange
        {
            get
            {
                if (MinRange == 0f && MaxRange != 0f)
                    return true;
                if (MinRange == 0f)
                    return MaxRange == 0f;
                return false;
            }
        }

        public int BaseDuration
        {
            get { return StyxWoW.Db[ClientDb.SpellDuration].GetRow(_spellEntry.DurationIndex).GetField<int>(1U); }
        }

        public int DurationPerLevel
        {
            get { return StyxWoW.Db[ClientDb.SpellDuration].GetRow(_spellEntry.DurationIndex).GetField<int>(2U); }
        }

        public int MaxDuration
        {
            get { return StyxWoW.Db[ClientDb.SpellDuration].GetRow(_spellEntry.DurationIndex).GetField<int>(3U); }
        }

        public WoWSpellSchool School
        {
            get { return (WoWSpellSchool)_spellEntry.SchoolMask; }
        }

        public bool CanCast
        {
            get
            {
                return Lua.GetReturnVal<bool>("return IsUsableSpell(select(1, GetSpellInfo(" + Id + ")))", 0U);
            }
        }

        public string RangeDescription
        {
            get { return StyxWoW.Db[ClientDb.SpellRange].GetRow(RangeIndex).GetField<string>(6U); }
        }

        public SpellEffect GetSpellEffect(int index)
        {
            // HB 4.3.4 returns null for missing/empty effects.
            // WotLK 3.3.5a DBC has fixed 3-slot arrays; unused slots have Effect == 0.
            if (index < 0 || index > 2)
                return null;

            if (_spellEntry.Effect[index] == 0)
                return null;

            return new SpellEffect(
                (WoWSpellEffectType)_spellEntry.Effect[index],
                (WoWApplyAuraType)_spellEntry.EffectApplyAuraName[index],
                _spellEntry.EffectRealPointsPerLevel[index],
                _spellEntry.EffectBasePoints[index],
                _spellEntry.EffectMechanic[index],
                _spellEntry.EffectImplicitTargetA[index],
                _spellEntry.EffectImplicitTargetB[index],
                _spellEntry.EffectRadiusIndex[index],
                _spellEntry.EffectAmplitude[index],
                _spellEntry.EffectMultipleValue[index],
                _spellEntry.EffectChainTarget[index],
                _spellEntry.EffectItemType[index],
                _spellEntry.EffectMiscValueA[index],
                _spellEntry.EffectMiscValueB[index],
                _spellEntry.EffectTriggerSpell[index],
                _spellEntry.EffectPointsPerComboPoint[index],
                _spellEntry.EffectSpellClassMask[index]
            );
        }

        private SpellInfoCache GetCachedSpellInfo()
        {
            lock (_spellInfoCache)
            {
                if (_spellInfoCache.TryGetValue(Id, out var cached))
                    return cached;
            }

            var observed = GetSpellInfo();
            if (observed != null)
            {
                lock (_spellInfoCache)
                    _spellInfoCache[Id] = observed;
            }
            // Preserve legacy getter defaults without caching a failed read
            // as valid metadata. Admission uses the explicit current result.
            return observed ?? new SpellInfoCache();
        }

        internal bool TryGetCurrentSpellInfo(out uint castTime, out bool isFunnel,
            out float minRange, out float maxRange)
        {
            var observed = GetSpellInfo();
            lock (_spellInfoCache)
            {
                if (observed == null)
                    _spellInfoCache.Remove(Id);
                else
                    _spellInfoCache[Id] = observed;
            }
            castTime = observed?.CastTime ?? 0U;
            isFunnel = observed?.IsFunnel ?? false;
            minRange = observed?.MinRange ?? 0f;
            maxRange = observed?.MaxRange ?? 0f;
            return observed != null;
        }

        private SpellInfoCache GetSpellInfo()
        {
            // Build12340's fifth return is a Lua boolean. The managed bridge
            // uses lua_tolstring, which does not convert booleans itself.
            var result = Lua.GetReturnValues(
                "if type(GetSpellInfo) ~= 'function' then return end; " +
                "local name,rank,icon,cost,funnel,power,castTime,minRange,maxRange=GetSpellInfo(" +
                Id.ToString(CultureInfo.InvariantCulture) + "); " +
                "if type(funnel) ~= 'boolean' then return end; " +
                "return name,rank,icon,cost,tostring(funnel),power,castTime,minRange,maxRange", "hax.lua");
            if (result == null || result.Count < 9 || string.IsNullOrEmpty(result[0]) || result[0] == "nil" ||
                !int.TryParse(result[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var powerCost) || powerCost < 0 ||
                !bool.TryParse(result[4], out var isFunnel) ||
                !uint.TryParse(result[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var castTime) ||
                !float.TryParse(result[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var minRange) ||
                !float.TryParse(result[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxRange) ||
                float.IsNaN(minRange) || float.IsInfinity(minRange) || minRange < 0f ||
                float.IsNaN(maxRange) || float.IsInfinity(maxRange) || maxRange < minRange)
                return null;

            return new SpellInfoCache
            {
                PowerCost = powerCost,
                IsFunnel = isFunnel,
                CastTime = castTime,
                MinRange = minRange,
                MaxRange = maxRange
            };
        }

        public void Cast()
        {
            SpellManager.CastSpellById(Id);
        }

        public static WoWSpell FromId(int id) => ObserveFromId(id, out _);

        internal static WoWSpell? ObserveFromId(int id, out string failure)
        {
            // A spell ID is not a lifetime for its native row address. Resolve
            // the currently published table and row each time; Memory retains
            // its existing read cache. Localized rows own a decoded snapshot.
            // Missing rows must remain retryable, and
            // removed/replaced rows must not inherit an old ID-only entry.
            var table = StyxWoW.Db[ClientDb.Spell];
            if (table == null) { failure = $"table-unavailable id={id}"; return null; }
            var row = table.ObserveLocalizedRow(id, out failure);
            return row == null ? null : new WoWSpell(id, row);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1}), Range: {2}-{3}, CastTime: {4}, Cost: {5} ({6}%), Mechanic: {7}, Dispel: {8}, TargetType: {9}, Power: {10}",
                Name,
                Rank,
                MinRange,
                MaxRange,
                CastTime,
                PowerCost,
                ManaCostPercent,
                Mechanic,
                DispelType,
                TargetType,
                (int)PowerType
            );
        }

        public bool Equals(WoWSpell other)
        {
            return Id == other.Id;
        }

        private class SpellInfoCache
        {
            public int PowerCost;
            public bool IsFunnel;
            public uint CastTime;
            public float MinRange;
            public float MaxRange;
        }
    }
}
