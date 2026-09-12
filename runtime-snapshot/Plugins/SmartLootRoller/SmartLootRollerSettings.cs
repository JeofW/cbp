using System.ComponentModel;
using System.IO;
using Styx.Helpers;
using Styx;
using DefaultValue = Styx.Helpers.DefaultValueAttribute;

namespace SmartLootRoller
{
    public enum MatchRollType
    {
        Need = 1,
        Greed = 2,
        Pass = 3
    }

    public enum NoMatchRollType
    {
        Greed = 2,
        Pass = 3
    }

    public class SmartLootRollerSettings : Settings
    {
        private static SmartLootRollerSettings _instance;

        public SmartLootRollerSettings()
            : base(Path.Combine(Path.Combine(Logging.ApplicationPath, "Settings"), string.Format("SmartLootRollerSettings_{0}.xml", StyxWoW.Me.Name)))
        {
        }

        public static SmartLootRollerSettings Instance => _instance ?? (_instance = new SmartLootRollerSettings());

        #region Category: General

        [Setting]
        [DefaultValue(true)]
        public bool RollForLoot { get; set; }

        [Setting]
        [DefaultValue(false)]
        public bool RollForLootDE { get; set; }

        [Setting]
        [DefaultValue(MatchRollType.Need)]
        public MatchRollType MatchRule { get; set; }

        [Setting]
        [DefaultValue(NoMatchRollType.Greed)]
        public NoMatchRollType NoMatchRule { get; set; }

        #endregion

        #region Category: Pawn Profile

        [Setting]
        [DefaultValue(false)]
        [Category("Pawn Profile")]
        [DisplayName("Low Level Armor (1-39)")]
        [Description("Check this if you are below level 40. It switches Plate to Mail and Mail to Leather.")]
        public bool IsLowLevel { get; set; }

        [Setting]
        [DefaultValue("Druid - Feral (Cat)")] // Safe default
        [Category("Pawn Profile")]
        public string ActiveProfileName { get; set; }

        #endregion

        #region Category: Stat Weights

        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Stamina { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Armor { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_DefenseRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_DodgeRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_ParryRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_BlockRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_BlockValue { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Strength { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Agility { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_HitRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_ExpertiseRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_ArmorPenetrationRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_HasteRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_CritRating { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_AttackPower { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_FeralAttackPower { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_WeaponDps { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_SpellPower { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Intellect { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Spirit { get; set; }
        [Setting] [DefaultValue(0f)] [Category("Stat Weights")] public float Weight_Mp5 { get; set; }

        public System.Collections.Generic.Dictionary<string, float> GetWeightsDictionary()
        {
            var dict = new System.Collections.Generic.Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
            if (Weight_Stamina > 0) dict["Stamina"] = Weight_Stamina;
            if (Weight_Armor > 0) dict["Armor"] = Weight_Armor;
            if (Weight_DefenseRating > 0) dict["DefenseRating"] = Weight_DefenseRating;
            if (Weight_DodgeRating > 0) dict["DodgeRating"] = Weight_DodgeRating;
            if (Weight_ParryRating > 0) dict["ParryRating"] = Weight_ParryRating;
            if (Weight_BlockRating > 0) dict["BlockRating"] = Weight_BlockRating;
            if (Weight_BlockValue > 0) dict["BlockValue"] = Weight_BlockValue;
            if (Weight_Strength > 0) dict["Strength"] = Weight_Strength;
            if (Weight_Agility > 0) dict["Agility"] = Weight_Agility;
            if (Weight_HitRating > 0) dict["HitRating"] = Weight_HitRating;
            if (Weight_ExpertiseRating > 0) dict["ExpertiseRating"] = Weight_ExpertiseRating;
            if (Weight_ArmorPenetrationRating > 0) dict["ArmorPenetrationRating"] = Weight_ArmorPenetrationRating;
            if (Weight_HasteRating > 0) dict["HasteRating"] = Weight_HasteRating;
            if (Weight_CritRating > 0) dict["CritRating"] = Weight_CritRating;
            if (Weight_AttackPower > 0) dict["AttackPower"] = Weight_AttackPower;
            if (Weight_FeralAttackPower > 0) dict["FeralAttackPower"] = Weight_FeralAttackPower;
            if (Weight_WeaponDps > 0) dict["WeaponDps"] = Weight_WeaponDps;
            if (Weight_SpellPower > 0) dict["SpellPower"] = Weight_SpellPower;
            if (Weight_Intellect > 0) dict["Intellect"] = Weight_Intellect;
            if (Weight_Spirit > 0) dict["Spirit"] = Weight_Spirit;
            if (Weight_Mp5 > 0) dict["ManaEvery5Seconds"] = Weight_Mp5;
            return dict;
        }

        public void ClearWeights()
        {
            Weight_Stamina = 0; Weight_Armor = 0; Weight_DefenseRating = 0; Weight_DodgeRating = 0;
            Weight_ParryRating = 0; Weight_BlockRating = 0; Weight_BlockValue = 0; Weight_Strength = 0;
            Weight_Agility = 0; Weight_HitRating = 0; Weight_ExpertiseRating = 0; Weight_ArmorPenetrationRating = 0;
            Weight_HasteRating = 0; Weight_CritRating = 0; Weight_AttackPower = 0; Weight_FeralAttackPower = 0;
            Weight_WeaponDps = 0; Weight_SpellPower = 0; Weight_Intellect = 0; Weight_Spirit = 0; Weight_Mp5 = 0;
        }

        [Setting]
        [DefaultValue("Leather")]
        [Category("Pawn Profile")]
        public string AllowedArmor { get; set; }

        [Setting]
        [DefaultValue("Polearm,Staff,MaceTwoHand")]
        [Category("Pawn Profile")]
        public string AllowedWeapons { get; set; }

        #endregion

        #region Category: Auto-Equip

        [Setting]
        [DefaultValue(false)]
        [Category("Auto-Equip")]
        [DisplayName("Auto Equip Upgrades")]
        [Description("Automatically scans bags every few seconds and equips upgrades based on your Pawn weights.")]
        public bool AutoEquipUpgrades { get; set; }

        [Setting]
        [DefaultValue(false)]
        [Category("Auto-Equip")]
        [DisplayName("Auto Equip BoE Greens")]
        [Description("If checked, the bot will auto-equip Bind-on-Equip Green (Uncommon) upgrades, making them Soulbound.")]
        public bool AutoEquipBoEGreens { get; set; }

        [Setting]
        [DefaultValue(false)]
        [Category("Auto-Equip")]
        [DisplayName("Auto Equip BoE Blues")]
        [Description("If checked, the bot will auto-equip Bind-on-Equip Blue (Rare) upgrades, making them Soulbound.")]
        public bool AutoEquipBoEBlues { get; set; }

        #endregion
    }
}
