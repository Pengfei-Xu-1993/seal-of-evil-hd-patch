# Extreme equipment bonuses

**Ctrl+Alt+A** applies this strong 29-effect preset to the selected item.
**Ctrl+Alt+T** keeps the separate original-table preset. In **Items & equipment
> Shortcuts > View preset values...**, the read-only comparison shows both
values, units and the reason for each choice.

The difference is substantial: **+4,096 maximum health vs 600**,
**+4,096 attack vs 120**, and **+32,768 to each of five defenses vs 120**.
These numbers are per-item bonuses. Character totals also depend on the
character, other equipment, skills and active effects.

The final profile contains **6 effective limits and 23 operational choices**.
An effective limit can mean a literal consumer clamp or a useful saturation
point in a particular calculation. An operational choice is a strong finite
value chosen around the native arithmetic and transport limits; it is **not a
claim of the game's global absolute maximum**. No entry falls back to the
original-table preset in this profile.

## Values

| Effect ID | Bonus | Extreme | Original-table reference | Unit | Basis |
| --- | --- | ---: | ---: | --- | --- |
| 22 | Maximum health | 4,096 | 600 | Points | Operational |
| 24 | Maximum energy | 4,096 | 360 | Points | Operational |
| 57 | Evasion | 2,048 | 432 | Rating | Operational |
| 58 | Accuracy | 2,048 | 1,920 | Rating | Operational |
| 80 | Fury gain speed | 4,096 | 100 | Percent bonus | Operational |
| 100 | Critical hit chance | 100 | 20 | Percent | Effective |
| 101 | Back attack | 1,024 | 84 | Attack points | Operational |
| 102 | Deadly attack | 4,096 | 20 | Chance coefficient | Operational |
| 103 | Reflected damage | 100 | 100 | Percent | Operational |
| 104 | Attack after taking damage | 1,024 | 42 | Attack points | Operational |
| 120 | Health regeneration speed | 512 | 50 | Percent bonus | Operational |
| 122 | Health restored on kill | 100 | 10 | Percent of own maximum | Effective |
| 123 | Poison attack | 4,096 | 20 | Chance coefficient | Operational |
| 140 | Energy regeneration speed | 512 | 50 | Percent bonus | Operational |
| 142 | Energy restored on kill | 100 | 10 | Percent of own maximum | Effective |
| 143 | Freeze attack | 4,096 | 20 | Chance coefficient | Operational |
| 144 | Abnormal status duration | 2,048 | 50 | Percent modifier | Operational |
| 160 | Movement speed | 4,096 | 10 | Percent bonus | Operational |
| 161 | Attack speed | 4,096 | 20 | Percent bonus | Operational |
| 163 | Burn attack | 4,096 | 20 | Chance coefficient | Operational |
| 164 | Absolute evasion | 50 | 20 | Percent | Effective |
| 180 | Exorcism attack | 100 | 100 | Percent | Effective |
| 181 | Stun attack | 4,096 | 20 | Chance coefficient | Operational |
| 182 | Impact resistance | 2,048 | 50 | Percent modifier | Operational |
| 183 | Abnormal status resistance | 2,048 | 50 | Percent modifier | Operational |
| 184 | Flat damage reduction | 32,768 | 30 | Points | Operational |
| 185 | Percentage damage reduction | 50 | 15 | Percent | Effective |
| 200 | Attack power | 4,096 | 120 | Points | Operational |
| 201 | Each of five elemental defenses | 32,768 | 120 | Points | Operational |

The reference column summarizes the generation annotations used by the separate
table preset. It is not a list of engine hard caps. The actual original game
tables, templates and resources are not included in this document or release.

## What the effective limits mean

- **Critical 100:** saturates the checked random-chance comparison for eligible
  attack types. It does not make every skill or event a critical hit.
- **Absolute evasion 50:** the client clamps this threshold to 50%; it is
  separate from the ordinary hit-rating calculation. Writing 100 does not give
  100% absolute evasion.
- **Kill restoration 100, for health and energy:** one qualifying kill can
  restore the full maximum. The actor's aggregate percentage can still stack
  above 100, but the current resource cannot be restored beyond full.
- **Exorcism 100:** saturates its chance comparison when the additional skill
  chance is nonnegative. Target and status eligibility conditions still apply.
- **Percentage reduction 50:** combat clamps the aggregate percentage to 50.
  High defense and flat reduction also retain a minimum-damage path; the preset
  does not provide complete damage or five-element immunity.

A chance coefficient is not the final probability. The deadly/poison/freeze/
burn/stun paths combine the coefficient with attack, defense and target
resistance. **4,096 does not mean 4,096% or a universal guaranteed trigger.**
Likewise, regeneration bonuses change recovery speed, not the percentage of
maximum health or energy recovered each second. Back attack and attack after
taking damage add attack points rather than multiplying attack by a percentage.

## Seven-equipment arithmetic scope

The operating choices account for up to **seven equipped copies** of this
profile, with one matching neutral preset record per effect on each item.
Repeated application does not append another matching preset record. Existing
higher values, duplicates and special/conditional/elemental/growing/disabled
records are preserved; they can place a save outside the stated scope.

The inspected native code performs several signed 32-bit multiplications
*before* division. Making the final divided result fit is insufficient. The
profile uses these finite input budgets:

| Calculation | Assumptions | Checked intermediate |
| --- | --- | ---: |
| Maximum-resource snapshot | Seven +4,096 items and up to 4,095 other maximum-resource points | 32,767, the positive signed-16 maximum |
| Maximum resource and regeneration | That resource budget, seven +512 regeneration bonuses and up to 65,536 total flat regeneration | 127,267,228 |
| On-kill restoration | The same maximum-resource budget and seven +100 restoration bonuses | 22,936,900 |
| Fury gain | Seven +4,096 bonuses and victim fury at most 65,535 | 1,885,573,020 |
| Accuracy/evasion transport | Seven +2,048 ratings and at most 16,384 other rating | 30,720, below signed-16 maximum 32,767 |
| Speed transport | Seven +4,096 bonuses and at most 4,095 other speed bonus | 32,767 |
| Status duration/resistance | Seven +2,048 modifiers, at most 4,096 other contribution, and attack/base timer at most 100,000 | 1,853,200,000 |
| Movement multiplication | The speed budget and pre-percentage movement step at most 49,150 | 1,615,413,050 |

All listed multiplication results stay below signed-32 maximum 2,147,483,647.
The maximum-resource snapshot, speed and rating limits additionally respect
their **signed 16-bit** consumers; exceeding 32,767 can produce negative values
even when an earlier packet or destination uses a 32-bit field.

An earlier **65,536** resource candidate was rejected by actual game testing:
the character panel displayed maxima of 240 and 100 where the edited totals
were 65,776 and 65,636. Those values match 16-bit wraparound. Although one actor
update packet carries full DWORD HP values, the panel uses a separate packed
snapshot: it shifts and sign-extends the HP/energy maxima before assigning the
display properties. Original-binary checks confirmed those signed reads.
The final **4,096** bonuses account for this additional boundary. The earlier
candidate is a failed regression case, not evidence of supported 65,536-point
resource bonuses. Existing values above the final profile are still preserved.

For attack, seven sets of +4,096 AP, +1,024 back attack and +1,024 attack after
damage, plus up to 8,192 ordinary additional AP, give a combined input of 51,200.
The model allows a skill multiplier of 21, element factor 1.6, critical factor
2, frozen-target factor 1.5, impact factor 1.99 and world factor 1.5. Its
upward-rounded damage ceiling is 15,405,467; multiplication by an ordinary
opponent's reflection percentage up to 100 stays at or below 1,540,546,700.

The player's own seven +100 reflection bonuses total 700%. Their separate
incoming-raw-hit boundary is 3,067,833, since the game multiplies raw damage by
the reflection percentage before division. Modified opponents, arbitrary skill
or timer edits, additional strong effects and two extreme PvP characters are
outside these arithmetic assumptions.

Movement and attack speed can become **very fast**. The native movement formula
scales the base step by `(100 + bonus) / 100`; attack timing uses
`100 / (100 + bonus)` and can reach zero after integer rounding. This is not
infinite attacks per second or a promise of comfortable movement control.
See the current [runtime verification status](validation.md).

## Native evidence references

The following are **relative virtual addresses (RVAs)** in the verified
original binaries. They identify the inspected consumers; no original machine
code, disassembly or complete game table is redistributed here.

| Behavior | Module | Relevant RVA |
| --- | --- | --- |
| Generic effect application and integer addition | ERRelive.dll | `0x053430`, `0x001CC0` |
| HP/energy regeneration multiplication | ERRelive.dll | `0x031317`, `0x0313A0` |
| Kill restoration | ERRelive.dll | `0x030F74`, `0x030FC3` |
| Fury gain | ERRelive.dll | `0x031012` |
| Full-DWORD combat AP and five defenses | ERRelive.dll | `0x024AA0`, `0x024AC0` |
| Additive special attack points | ERRelive.dll | `0x022E22`, `0x022E5B` |
| Percentage reduction clamp and 5% damage floor | ERRelive.dll | `0x022ED2`, `0x022EF7` |
| Skill/element/critical/frozen/world factors | ERRelive.dll | `0x022F1D`, `0x022F92`, `0x022FC6`, `0x022FDA`, `0x02313A` |
| Reflection multiplication | ERRelive.dll | `0x023637` |
| On-hit chance and duration/resistance math | ERRelive.dll | `0x023395`, `0x023404`, `0x023435` |
| Exorcism chance comparison | ERRelive.dll | `0x023866` |
| Impact resistance ratios | ERRelive.dll | `0x02300D`, `0x02304C`, `0x023088` |
| Status-object timer scaling | ERRelive.dll | `0x078147`, `0x078207` |
| Partial direct-skill status-resistance gate | ERRelive.dll | `0x0780B4` |
| Signed rating decoding; separate full-DWORD actor HP packet | SOE.EXE | `0x0989BD`, `0x0989C0`, `0x0989C4` |
| Character-info HP/energy maximum snapshot sign extension | SOE.EXE | `0x19D800`, `0x19D86A` |
| Signed speed decoding | SOE.EXE | `0x098B54` |
| Movement scaling and attack timing | SOE.EXE | `0x024925`, `0x0259F1` |
| Critical comparison | SOE.EXE | `0x025B04` |
| Absolute evasion clamp | SOE.EXE | `0x01FCAA` |

Original binary SHA256 values:

```text
ERRelive.dll
B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F

SOE.EXE
452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B
```

The editor checks both installed binaries before enabling this extreme preset.
The original-table preset remains independent of that extreme-specific check.
Static evidence, arithmetic modeling and actual game execution are separate
forms of validation; [validation.md](validation.md) records their status.

The published table was checked against the frozen `ItemQuickActions.cs`
implementation with SHA256:

```text
DA8EB578838D7A079EBCAFB0DDA7A40ACA471B43B83A71857120FE82EEE97444
```
