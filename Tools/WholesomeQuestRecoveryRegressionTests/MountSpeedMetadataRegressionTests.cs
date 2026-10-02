using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Styx.Logic.Combat;

internal static class MountSpeedMetadataRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        int passed = 0;
        foreach (int die in new[] { 0, 1, 2, -1 })
        {
            object raw = new SpellEntry();
            foreach (var field in typeof(SpellEntry).GetFields().Where(f => f.FieldType.IsArray))
                field.SetValue(raw, Array.CreateInstance(field.FieldType.GetElementType()!, field.GetCustomAttribute<MarshalAsAttribute>()!.SizeConst));
            var entry = (SpellEntry)raw;
            entry.Effect[0] = 6; entry.EffectApplyAuraName[0] = 32;
            entry.EffectBasePoints[0] = 59; entry.EffectDieSides[0] = die;
            var spell = (WoWSpell)RuntimeHelpers.GetUninitializedObject(typeof(WoWSpell));
            typeof(WoWSpell).GetField("_spellEntry", hidden)!.SetValue(spell, entry);
            var effect = spell.GetSpellEffect(0);
            var property = typeof(SpellEffect).GetProperty("DieSides");
            if (property == null || !Equals(property.GetValue(effect), die) || effect.BasePoints != 59)
                throw new InvalidOperationException("Actual GetSpellEffect must retain original die-sides, rather than always assuming raw base+1; expected=" + die);
            if (spell.GetSpellEffect(1) != null || spell.GetSpellEffect(-1) != null)
                throw new InvalidOperationException("Unused original effect slots changed");
            passed++;
        }
        Console.WriteLine($"Mount speed metadata: {passed}/4; actual WoWSpell/SpellEffect, controlled original DBC rows; no client query.");
    }
}
