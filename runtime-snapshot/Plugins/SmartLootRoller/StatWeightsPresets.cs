using System.Collections.Generic;

namespace SmartLootRoller
{
    public static class StatWeightsPresets
    {
        public static Dictionary<string, string> GetPresets()
        {
            var presets = new Dictionary<string, string>();

            // Death Knight
            presets["Death Knight - Blood (Tank)"] = "Stamina=1.5\r\nArmor=0.1\r\nDefenseRating=1.0\r\nDodgeRating=0.8\r\nParryRating=0.8\r\nStrength=0.5\r\nAgility=0.4\r\nExpertiseRating=0.4\r\nHitRating=0.2";
            presets["Death Knight - Frost (DPS)"] = "Strength=2.8\r\nArmorPenetrationRating=2.2\r\nHasteRating=1.8\r\nCritRating=1.7\r\nExpertiseRating=2.1\r\nHitRating=2.1\r\nAgility=1.5\r\nAttackPower=1.0\r\nWeaponDps=5.0";
            presets["Death Knight - Unholy (DPS)"] = "Strength=3.1\r\nHasteRating=2.0\r\nCritRating=1.8\r\nArmorPenetrationRating=1.5\r\nExpertiseRating=2.1\r\nHitRating=2.1\r\nAgility=1.4\r\nAttackPower=1.0\r\nWeaponDps=4.5";

            // Druid
            presets["Druid - Feral (Bear)"] = "Stamina=7.3\r\nAgility=4.5\r\nArmor=3.6\r\nHitRating=2.9\r\nExpertiseRating=2.7\r\nStrength=2.379\r\nHasteRating=2.1\r\nDodgeRating=2.0\r\nDefenseRating=1.8\r\nArmorPenetrationRating=1.6\r\nCritRating=1.5\r\nFeralAttackPower=1.2\r\nAttackPower=1.0";
            presets["Druid - Feral (Cat)"] = "Strength=2.379\r\nHitRating=2.2\r\nAgility=2.1\r\nExpertiseRating=2.0\r\nCritRating=1.8\r\nArmorPenetrationRating=1.4\r\nHasteRating=1.2\r\nFeralAttackPower=1.2\r\nAttackPower=1.0";
            presets["Druid - Balance"] = "HitRating=2.5\r\nSpellPower=1.0\r\nHasteRating=0.9\r\nCritRating=0.7\r\nIntellect=0.6\r\nSpirit=0.4";
            presets["Druid - Restoration"] = "SpellPower=1.0\r\nHasteRating=0.8\r\nIntellect=0.7\r\nSpirit=0.6\r\nCritRating=0.4";

            // Hunter
            presets["Hunter - Marksmanship"] = "WeaponDps=7.0\r\nAgility=2.5\r\nArmorPenetrationRating=2.4\r\nHitRating=2.0\r\nCritRating=1.8\r\nIntellect=1.1\r\nAttackPower=1.0\r\nHasteRating=0.9";
            presets["Hunter - Survival"] = "WeaponDps=6.0\r\nAgility=2.7\r\nHitRating=2.0\r\nCritRating=1.9\r\nAttackPower=1.0\r\nIntellect=1.1\r\nArmorPenetrationRating=1.4\r\nHasteRating=1.1";
            presets["Hunter - Beast Mastery"] = "WeaponDps=6.0\r\nAgility=2.4\r\nHitRating=2.0\r\nAttackPower=1.0\r\nCritRating=1.7\r\nArmorPenetrationRating=1.5\r\nIntellect=1.1\r\nHasteRating=1.2";

            // Mage
            presets["Mage - Arcane"] = "HitRating=2.8\r\nSpellPower=1.0\r\nHasteRating=0.9\r\nIntellect=0.8\r\nCritRating=0.7\r\nSpirit=0.3";
            presets["Mage - Fire"] = "HitRating=2.8\r\nSpellPower=1.0\r\nCritRating=0.9\r\nHasteRating=0.8\r\nIntellect=0.7\r\nSpirit=0.4";
            presets["Mage - Frost"] = "HitRating=2.8\r\nSpellPower=1.0\r\nHasteRating=0.8\r\nCritRating=0.7\r\nIntellect=0.6\r\nSpirit=0.3";

            // Paladin
            presets["Paladin - Protection (Tank)"] = "Stamina=1.5\r\nArmor=0.1\r\nDefenseRating=1.0\r\nDodgeRating=0.8\r\nParryRating=0.8\r\nBlockRating=0.6\r\nBlockValue=0.5\r\nStrength=0.5\r\nAgility=0.4\r\nHitRating=0.3\r\nExpertiseRating=0.3";
            presets["Paladin - Retribution"] = "WeaponDps=7.5\r\nStrength=2.7\r\nHitRating=2.2\r\nExpertiseRating=2.2\r\nAgility=1.8\r\nCritRating=1.7\r\nHasteRating=1.4\r\nArmorPenetrationRating=1.2\r\nAttackPower=1.0";
            presets["Paladin - Holy"] = "Intellect=1.6\r\nHasteRating=1.4\r\nMp5=1.2\r\nSpellPower=1.0\r\nCritRating=0.8";

            // Priest
            presets["Priest - Shadow"] = "HitRating=2.5\r\nSpellPower=1.0\r\nHasteRating=0.9\r\nCritRating=0.8\r\nIntellect=0.6\r\nSpirit=0.5";
            presets["Priest - Discipline"] = "SpellPower=1.0\r\nIntellect=0.9\r\nCritRating=0.7\r\nHasteRating=0.6\r\nSpirit=0.4";
            presets["Priest - Holy"] = "SpellPower=1.0\r\nSpirit=0.9\r\nIntellect=0.8\r\nHasteRating=0.7\r\nCritRating=0.5";

            // Rogue
            presets["Rogue - Assassination"] = "WeaponDps=5.0\r\nAgility=2.2\r\nHitRating=1.9\r\nExpertiseRating=1.9\r\nHasteRating=1.6\r\nCritRating=1.5\r\nAttackPower=1.0\r\nArmorPenetrationRating=1.2";
            presets["Rogue - Combat"] = "WeaponDps=6.5\r\nAgility=2.1\r\nArmorPenetrationRating=2.0\r\nHitRating=1.8\r\nExpertiseRating=1.8\r\nHasteRating=1.5\r\nCritRating=1.4\r\nAttackPower=1.0";
            presets["Rogue - Subtlety"] = "WeaponDps=6.0\r\nAgility=2.3\r\nHitRating=1.8\r\nExpertiseRating=1.8\r\nArmorPenetrationRating=1.7\r\nCritRating=1.5\r\nAttackPower=1.0\r\nHasteRating=1.2";

            // Shaman
            presets["Shaman - Elemental"] = "HitRating=2.5\r\nSpellPower=1.0\r\nHasteRating=0.9\r\nCritRating=0.7\r\nIntellect=0.6";
            presets["Shaman - Enhancement"] = "WeaponDps=5.5\r\nAgility=2.1\r\nHitRating=2.0\r\nExpertiseRating=2.0\r\nAttackPower=1.0\r\nArmorPenetrationRating=1.4\r\nCritRating=1.3\r\nHasteRating=1.3\r\nIntellect=1.2\r\nSpellPower=0.9";
            presets["Shaman - Restoration"] = "SpellPower=1.0\r\nHasteRating=0.9\r\nIntellect=0.8\r\nCritRating=0.6";

            // Warlock
            presets["Warlock - Affliction"] = "HitRating=2.8\r\nSpellPower=1.0\r\nHasteRating=0.9\r\nCritRating=0.7\r\nSpirit=0.6\r\nIntellect=0.5";
            presets["Warlock - Demonology"] = "HitRating=2.8\r\nSpellPower=1.0\r\nHasteRating=0.8\r\nCritRating=0.7\r\nSpirit=0.6\r\nIntellect=0.5";
            presets["Warlock - Destruction"] = "HitRating=2.8\r\nSpellPower=1.0\r\nHasteRating=0.8\r\nCritRating=0.8\r\nSpirit=0.5\r\nIntellect=0.5";

            // Warrior
            presets["Warrior - Protection (Tank)"] = "Stamina=1.5\r\nArmor=0.1\r\nDefenseRating=1.0\r\nDodgeRating=0.8\r\nParryRating=0.8\r\nBlockRating=0.6\r\nBlockValue=0.5\r\nStrength=0.5\r\nAgility=0.4\r\nHitRating=0.3\r\nExpertiseRating=0.3";
            presets["Warrior - Arms"] = "WeaponDps=7.5\r\nArmorPenetrationRating=2.5\r\nStrength=2.4\r\nHitRating=2.0\r\nExpertiseRating=2.0\r\nCritRating=1.8\r\nAgility=1.5\r\nAttackPower=1.0\r\nHasteRating=0.9";
            presets["Warrior - Fury"] = "WeaponDps=8.0\r\nArmorPenetrationRating=2.6\r\nStrength=2.5\r\nHitRating=2.0\r\nExpertiseRating=2.0\r\nCritRating=1.9\r\nAgility=1.6\r\nAttackPower=1.0\r\nHasteRating=1.1";

            return presets;
        }

        public static string GetAllowedArmor(string presetName, bool isLowLevel)
        {
            if (presetName.Contains("Death Knight") || presetName.Contains("Paladin") || presetName.Contains("Warrior"))
                return "Plate,Mail,Leather,Cloth";
            if (presetName.Contains("Hunter") || presetName.Contains("Shaman"))
                return "Mail,Leather,Cloth";
            if (presetName.Contains("Druid") || presetName.Contains("Rogue"))
                return "Leather,Cloth";
            if (presetName.Contains("Mage") || presetName.Contains("Priest") || presetName.Contains("Warlock"))
                return "Cloth";
            return "Cloth,Leather,Mail,Plate"; // Default fallback
        }

        public static string GetAllowedWeapons(string presetName)
        {
            // Simplified weapon allowances. Feral can use Polearm, Staff, MaceTwoHand.
            if (presetName.Contains("Feral")) return "Polearm,Staff,MaceTwoHand";
            if (presetName.Contains("Druid - Balance") || presetName.Contains("Druid - Restoration")) return "MaceOneHand,MaceTwoHand,Dagger,Staff";
            if (presetName.Contains("Rogue")) return "SwordOneHand,AxeOneHand,MaceOneHand,Dagger,Fist,Bow,Gun,Crossbow";
            if (presetName.Contains("Mage") || presetName.Contains("Warlock")) return "SwordOneHand,Dagger,Staff,Wand";
            if (presetName.Contains("Priest")) return "MaceOneHand,Dagger,Staff,Wand";
            if (presetName.Contains("Hunter")) return "Bow,Gun,Crossbow,Polearm,Staff,SwordOneHand,SwordTwoHand,AxeOneHand,AxeTwoHand,Dagger,Fist";
            if (presetName.Contains("Shaman")) return "AxeOneHand,MaceOneHand,Dagger,Fist,Staff";
            if (presetName.Contains("Death Knight")) return "SwordOneHand,SwordTwoHand,AxeOneHand,AxeTwoHand,MaceOneHand,MaceTwoHand,Polearm";
            if (presetName.Contains("Paladin - Holy")) return "SwordOneHand,AxeOneHand,MaceOneHand";
            if (presetName.Contains("Paladin - Protection")) return "SwordOneHand,AxeOneHand,MaceOneHand";
            if (presetName.Contains("Paladin - Retribution")) return "SwordTwoHand,AxeTwoHand,MaceTwoHand,Polearm";
            if (presetName.Contains("Warrior")) return "SwordOneHand,SwordTwoHand,AxeOneHand,AxeTwoHand,MaceOneHand,MaceTwoHand,Dagger,Polearm,Fist,Bow,Gun,Crossbow";
            return "";
        }
    }
}
