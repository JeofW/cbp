# W110 aura selection paths — documentation clarification

28 September 2026. Source examined: `a43c04a76b8446dacb819555c27b920a7f51fc24`. This note clarifies the phrase “three original resistance auras remain distinct explicit choices” in the frozen `2026-09-27/W110_RET_PALADIN_COMPATIBILITY.md`, section5. It does **not** mean that all three resistance auras are separate values in Singular's native `PaladinAura` setting.

## Actual current selection paths

`ClassSpecific/Paladin/Common.cs:28-39` exposes `Auto`, `Devotion`, `Retribution`, legacy `Resistance`, `Concentration` and `Crusader`. `PaladinSupport.cs:371-409` maps legacy `Resistance` to Shadow Resistance Aura. Native Auto without a verified assignment chooses among Devotion, Retribution and Concentration according to the existing role/contribution policy; it does not automatically select an encounter resistance aura.

| Aura | Native explicit Aura setting | Verified PallyPower Wrath aura slot |
|---|---|---|
| Devotion Aura | Devotion | 1 |
| Retribution Aura | Retribution | 2 |
| Concentration Aura | Concentration | 3 |
| Shadow Resistance Aura | Resistance, retained legacy mapping | 4 |
| Frost Resistance Aura | No dedicated native enum value | 5 |
| Fire Resistance Aura | No dedicated native enum value | 6 |
| Crusader Aura | Crusader | 7 |

The seven names exist in the actual support factory and `PallyPowerWrathAuras` table at `PaladinSupport.cs:90-95`. A verified PallyPower assignment is a separate input path: `UsePallyPowerAssignments` must be enabled and the native Aura setting must be Auto. The existing read-only query requires the reviewed `v3.2.21` metadata, `IsWrath`, explicit Wrath tables and bounded valid response slots before interpreting them. An explicit native Aura setting bypasses that assignment path. Learned-spell/readiness, current coverage, participant identity and support-maintenance guards still apply.

The retained `PallyPowerAssignmentRegressionTests` specifically includes the Frost slot5 mapping and native explicit-selection precedence. That controlled test is not verification of an installed addon or live server effect. This note reports the Fire slot6 mapping from the actual table; it does not invent an additional executed Fire-specific scenario or expand the test counts.

## Meaning for the W110 audit

All seven original names are represented in the supported factory/assignment paths, but the native settings menu does **not** offer seven individual manual aura values. Describing those two different capabilities as the same would overstate native-setting coverage. The legacy Resistance choice remains Shadow-only, not a later-expansion combined resistance spell. The unmounted-setting versus mounted Crusader travel distinction in the earlier matrix is unchanged.

This is a correction to the precision of the evidence report, not a newly demonstrated casting or assignment defect. No enum value, serialized configuration, Auto priority, PallyPower schema, aura effect or production/test code is changed. Adding separate manual Frost/Fire enum options would be an explicit configuration extension with its own compatibility and regression requirements; this audit does not silently implement that new behavior or claim it already exists.

Current tested production remains `a43c04a7`, with its previously verified17/17 integrated groups and exact source/artifact evidence. This documentation-only clarification does not justify another project build or unchanged CI rerun. The exact native-map, final-verifier and other historical provider-refused operations remain unexecuted and are not reconstructed. Current client/realm/strength/encounter and independent-review acceptance boundaries remain unchanged.
