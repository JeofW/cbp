using System;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;

namespace SmartLootRoller
{
    public partial class FormSettings : Form
    {
        private bool _isUpdating = false;

        public FormSettings()
        {
            InitializeComponent();
        }

        private void FormSettings_Load(object sender, EventArgs e)
        {
            pgSettings.SelectedObject = SmartLootRollerSettings.Instance;

            _isUpdating = true;
            var presets = StatWeightsPresets.GetPresets();
            foreach (var preset in presets.Keys.OrderBy(k => k))
            {
                cbPresets.Items.Add(preset);
            }

            string active = SmartLootRollerSettings.Instance.ActiveProfileName;
            if (cbPresets.Items.Contains(active))
            {
                cbPresets.SelectedItem = active;
            }
            _isUpdating = false;
        }

        private void cbPresets_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isUpdating) return;

            string selected = cbPresets.SelectedItem as string;
            if (!string.IsNullOrEmpty(selected))
            {
                var presets = StatWeightsPresets.GetPresets();
                if (presets.TryGetValue(selected, out string weights))
                {
                    SmartLootRollerSettings.Instance.ActiveProfileName = selected;
                    SmartLootRollerSettings.Instance.ClearWeights();
                    
                    var parsed = PawnScorer.ParseWeights(weights);
                    if (parsed.TryGetValue("Stamina", out float stam)) SmartLootRollerSettings.Instance.Weight_Stamina = stam;
                    if (parsed.TryGetValue("Armor", out float armor)) SmartLootRollerSettings.Instance.Weight_Armor = armor;
                    if (parsed.TryGetValue("DefenseRating", out float def)) SmartLootRollerSettings.Instance.Weight_DefenseRating = def;
                    if (parsed.TryGetValue("DodgeRating", out float dodge)) SmartLootRollerSettings.Instance.Weight_DodgeRating = dodge;
                    if (parsed.TryGetValue("ParryRating", out float parry)) SmartLootRollerSettings.Instance.Weight_ParryRating = parry;
                    if (parsed.TryGetValue("BlockRating", out float block)) SmartLootRollerSettings.Instance.Weight_BlockRating = block;
                    if (parsed.TryGetValue("BlockValue", out float bv)) SmartLootRollerSettings.Instance.Weight_BlockValue = bv;
                    if (parsed.TryGetValue("Strength", out float str)) SmartLootRollerSettings.Instance.Weight_Strength = str;
                    if (parsed.TryGetValue("Agility", out float agi)) SmartLootRollerSettings.Instance.Weight_Agility = agi;
                    if (parsed.TryGetValue("HitRating", out float hit)) SmartLootRollerSettings.Instance.Weight_HitRating = hit;
                    if (parsed.TryGetValue("ExpertiseRating", out float exp)) SmartLootRollerSettings.Instance.Weight_ExpertiseRating = exp;
                    if (parsed.TryGetValue("ArmorPenetrationRating", out float arp)) SmartLootRollerSettings.Instance.Weight_ArmorPenetrationRating = arp;
                    if (parsed.TryGetValue("HasteRating", out float haste)) SmartLootRollerSettings.Instance.Weight_HasteRating = haste;
                    if (parsed.TryGetValue("CritRating", out float crit)) SmartLootRollerSettings.Instance.Weight_CritRating = crit;
                    if (parsed.TryGetValue("AttackPower", out float ap)) SmartLootRollerSettings.Instance.Weight_AttackPower = ap;
                    if (parsed.TryGetValue("FeralAttackPower", out float fap)) SmartLootRollerSettings.Instance.Weight_FeralAttackPower = fap;
                    if (parsed.TryGetValue("WeaponDps", out float wdps)) SmartLootRollerSettings.Instance.Weight_WeaponDps = wdps;
                    if (parsed.TryGetValue("SpellPower", out float sp)) SmartLootRollerSettings.Instance.Weight_SpellPower = sp;
                    if (parsed.TryGetValue("Intellect", out float intel)) SmartLootRollerSettings.Instance.Weight_Intellect = intel;
                    if (parsed.TryGetValue("Spirit", out float spi)) SmartLootRollerSettings.Instance.Weight_Spirit = spi;
                    if (parsed.TryGetValue("Mp5", out float mp5)) SmartLootRollerSettings.Instance.Weight_Mp5 = mp5;

                    SmartLootRollerSettings.Instance.AllowedArmor = StatWeightsPresets.GetAllowedArmor(selected, SmartLootRollerSettings.Instance.IsLowLevel);
                    SmartLootRollerSettings.Instance.AllowedWeapons = StatWeightsPresets.GetAllowedWeapons(selected);
                    SmartLootRollerSettings.Instance.Save();
                    pgSettings.Refresh();
                }
            }
        }

        private void pgSettings_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            if (pgSettings.SelectedObject != null && pgSettings.SelectedObject is SmartLootRollerSettings)
            {
                var settings = (SmartLootRollerSettings)pgSettings.SelectedObject;
                
                // If the user toggled IsLowLevel, we need to update AllowedArmor!
                if (e.ChangedItem.PropertyDescriptor.Name == "IsLowLevel" && !string.IsNullOrEmpty(settings.ActiveProfileName))
                {
                    settings.AllowedArmor = StatWeightsPresets.GetAllowedArmor(settings.ActiveProfileName, settings.IsLowLevel);
                    pgSettings.Refresh();
                }

                settings.Save();
            }
        }
    }
}
