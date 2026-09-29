using System;
using System.ComponentModel;

using Singular.ClassSpecific.Paladin;

using Styx.Helpers;

using DefaultValue = Styx.Helpers.DefaultValueAttribute;

namespace Singular.Settings
{

    internal class PaladinSettings : Styx.Helpers.Settings
    {
        public PaladinSettings()
            : base(SingularSettings.SettingsPath + "_Paladin.xml")
        {
        }
        
        #region Common
        [Setting]
        [DefaultValue(true)]
        [Category("Common")]
        [DisplayName("Dispel Debuffs")]
        [Description("Use learned Purify/Cleanse for safe removable effects. Disable for encounter-specific assignments.")]
        public bool DispelDebuffs { get; set; }

        [Setting]
        [DefaultValue(true)]
        [Category("Common")]
        [DisplayName("Dispel Party and Raid")]
        [Description("Include visible friendly group members in automatic cleansing. Self cleansing remains independent.")]
        public bool DispelParty { get; set; }

        [Setting]
        [DefaultValue(PaladinSeal.Auto)]
        [Category("Common")]
        [DisplayName("Seal")]
        [Description("The seal to be used for combat. Added by xyFaded")]
        public PaladinSeal Seal { get; set; }
        
        [Setting]
        [DefaultValue(PaladinAura.Auto)]
        [Category("Common")]
        [DisplayName("Aura")]
        [Description("The aura to be used while not mounted. Set this to Auto to allow the CC to automatically pick the aura depending on spec.")]
        public PaladinAura Aura { get; set; }

        [Setting]
        [DefaultValue(90)]
        [Category("Common")]
        [DisplayName("Holy Light Health")]
        [Description("Holy Light will be used at this value")]
        public int HolyLightHealth { get; set; }

        [Setting]
        [DefaultValue(PaladinBlessings.Auto)]
        [Category("Common")]
        [DisplayName("Blessings")]
        [Description("Which Blessing to cast")]
        public PaladinBlessings Blessings { get; set; }

        [Setting]
        [DefaultValue(false)]
        [Category("Common")]
        [DisplayName("Use Greater Blessings")]
        [Description("Prefer a learned Greater blessing out of combat only with known reagent availability and compatible same-class group coverage. Otherwise use the normal blessing.")]
        public bool UseGreaterBlessings { get; set; }

        [Setting]
        [DefaultValue(false)]
        [Category("Common")]
        [DisplayName("Use PallyPower Assignments")]
        [Description("When PallyPower is loaded in verified Wrath mode, honor its read-only local blessing and aura assignments while Singular is set to Auto. Unknown or incompatible addon state defers rather than guessing; this never writes PallyPower data.")]
        public bool UsePallyPowerAssignments { get; set; }

        [Setting]
        [DefaultValue(30)]
        [Category("Common")]
        [DisplayName("Lay on Hand Health")]
        [Description("Lay on Hands will be used at this value")]
        public int LayOnHandsHealth { get; set; }

        [Setting]
        [DefaultValue(50)]
        [Category("Common")]
        [DisplayName("Flash of Light Health")]
        [Description("Flash of Light will be used at this value")]
        public int FlashOfLightHealth { get; set; }

        #endregion

        #region Holy

        [Setting]
        [DefaultValue(90)]
        [Category("Holy")]
        [DisplayName("Holy Shock Health")]
        [Description("Holy Shock will be used at this value")]
        public int HolyShockHealth { get; set; }

        [Setting]
        [DefaultValue(50)]
        [Category("Holy")]
        [DisplayName("Divine Plea Mana")]
        [Description("Divine Plea will be used at this value")]
        public double DivinePleaMana { get; set; } 
        #endregion

        #region Protection

        [Setting]
        [DefaultValue(80)]
        [Category("Protection")]
        [DisplayName("Divine Protection Health")]
        [Description("Divine Protection will be used at this value")]
        public int DivineProtectionHealthProt { get; set; }

        [Setting]
        [DefaultValue(false)]
        [Category("Protection")]
        [DisplayName("Avengers On Pull Only")]
        [Description("Only use Avenger's Shield to pull")]
        public bool AvengersPullOnly { get; set; }

        [Setting]
        [DefaultValue(3)]
        [Category("Protection")]
        [DisplayName("Consecration Count")]
        [Description("Consecration will be used when you have more then that many mobs attacking you")]
        public int ProtConsecrationCount { get; set; }
        #endregion

        #region Retribution
        [Setting]
        [DefaultValue(70)]
        [Category("Retribution")]
        [DisplayName("Divine Protection Health")]
        [Description("Divine Protection will be used at this value")]
        public int DivineProtectionHealthRet { get; set; }

        [Setting]
        [DefaultValue(3)]
        [Category("Retribution")]
        [DisplayName("Consecration Count")]
        [Description("Consecration will be used when you have more then that many mobs attacking you")]
        public int ConsecrationCount { get; set; }

        [Setting]
        [DefaultValue(30)]
        [Category("Retribution")]
        [DisplayName("Heal Health")]
        [Description("Healing will be done at this percentage")]
        public int RetributionHealHealth { get; set; }

        [Setting]
        [DefaultValue(false)]
        [Category("Retribution")]
        [DisplayName("Use Solo Seal of Light")]
        [Description("Opt-in sustain while Seal is Auto, solo in Normal-context combat against a non-player, non-elite, non-boss target. Does not replace emergency healing, group damage or PvP seal choices.")]
        public bool UseSoloSealOfLight { get; set; }

        [Setting]
        [DefaultValue(50)]
        [Category("Retribution")]
        [DisplayName("Solo Seal of Light Entry Health")]
        [Description("Start Light at or below this health percentage, above the existing emergency-heal threshold, only in melee with sufficient mana. Policy threshold, not an optimal-DPS guarantee.")]
        public int SoloSealOfLightHealth { get; set; }

        [Setting]
        [DefaultValue(75)]
        [Category("Retribution")]
        [DisplayName("Solo Seal of Light Recovery Health")]
        [Description("Keep an observed Light below this percentage; resume the normal damage seal at or above it. Must be greater than Entry Health and at most 100.")]
        public int SoloSealOfLightRecoveryHealth { get; set; }

        [Setting]
        [DefaultValue(30)]
        [Category("Retribution")]
        [DisplayName("Solo Seal of Light Minimum Mana")]
        [Description("Minimum observed mana percentage to start Light. The actual spell layer still checks cost, usability and cooldown. Does not repeatedly replace an existing Light as mana drops.")]
        public int SoloSealOfLightMinimumMana { get; set; }

        #endregion
    }
}