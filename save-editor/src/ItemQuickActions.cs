using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SealSaveEditor
{
    public sealed class ItemBonusMaximum
    {
        public int AttributeId { get; internal set; }
        public string Name { get; internal set; }
        public float Value { get; internal set; }
        public string RuleAlias { get; internal set; }
        public string SourceFile { get; internal set; }
        public int SourceLine { get; internal set; }
        public string Evidence { get; internal set; }
        public float OriginalTableValue { get; internal set; }
        public string LimitKind { get; internal set; }
        public string Unit { get; internal set; }
    }

    public sealed class QuickActionResult
    {
        public int ChangedCount { get { return AddedCount + UpdatedCount; } }
        public int AddedCount { get; internal set; }
        public int UpdatedCount { get; internal set; }
        public int UnchangedCount { get; internal set; }
        public int SkippedCount { get { return SkippedEffects.Count; } }
        public IList<int> SkippedEffects { get; internal set; }
        public IList<ItemBonusMaximum> Maximums { get; internal set; }
        public string ProfileName { get; internal set; }
        public IList<string> Limitations { get; internal set; }
        public string Summary { get; internal set; }
    }

    /// <summary>Atomic item presets using verified persistent effects and installed table limits.</summary>
    public static class ItemQuickActions
    {
        private const string ExtremeEngineSha256 = "B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F";
        private const string ExtremeClientSha256 = "452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B";
        // These 29 IDs have persistent native actor-stat/AP/DP application paths.
        // 82/85/202/203 are consumable actions, not permanent equipment bonuses.
        private static readonly int[] PersistentEffects = {
            22, 24, 57, 58, 80, 100, 101, 102, 103, 104, 120, 122, 123,
            140, 142, 143, 144, 160, 161, 163, 164, 180, 181, 182, 183,
            184, 185, 200, 201
        };

        /// <summary>
        /// The largest declared MaximumValue per persistent effect in installed attribT.
        /// This is the original table's limit, not a claim of an engine-wide hard cap:
        /// some native generation paths do not enforce column 14 (for example damage reduction).
        /// </summary>
        public static IList<ItemBonusMaximum> GetMaximumBonuses(ItemTemplateCatalog templates)
        {
            if (templates == null) throw new ArgumentNullException("templates");
            var result = new List<ItemBonusMaximum>();
            foreach (int effect in PersistentEffects)
            {
                ItemAttributeRule rule = templates.AttributeRules.Values
                    .Where(r => r.AttributeId == effect && IsPositiveFinite(r.MaximumValue)
                        && r.MaximumValue <= Int32.MaxValue)
                    .OrderByDescending(r => r.MaximumValue).ThenBy(r => r.SourceLine).FirstOrDefault();
                if (rule == null)
                    throw new InvalidDataException("The installed tables contain no verified positive maximum for " + GameCatalog.AttributeName(effect) + ".");
                float amount = (float)rule.MaximumValue;
                if (Single.IsNaN(amount) || Single.IsInfinity(amount) || amount <= 0)
                    throw new InvalidDataException("The installed item bonus limit cannot be represented safely.");
                result.Add(new ItemBonusMaximum {
                    AttributeId = effect, Name = GameCatalog.AttributeName(effect), Value = amount,
                    RuleAlias = rule.Alias, SourceFile = rule.SourceFile, SourceLine = rule.SourceLine,
                    OriginalTableValue = amount, LimitKind = "OriginalTableMaximum", Unit = UnitForEffect(effect),
                    Evidence = "Original table annotation MaximumValue (column 14); " + rule.Alias + ", "
                        + rule.SourceFile + ":" + rule.SourceLine.ToString(CultureInfo.InvariantCulture)
                        + ". Applied as an enabled neutral absolute bonus; existing larger bonuses are retained."
                });
            }
            return result.AsReadOnly();
        }

        public static QuickActionResult ApplyMaxBonuses(SaveDocument document, ItemRecord item, ItemTemplateCatalog templates)
        { return ApplyBonuses(document, item, templates, false); }

        /// <summary>
        /// A separately evidenced extreme profile. Unproved engine ceilings are
        /// explicitly labeled as table fallbacks rather than invented hard caps.
        /// </summary>
        public static IList<ItemBonusMaximum> GetExtremeBonuses(ItemTemplateCatalog templates)
        {
            IList<ItemBonusMaximum> result = GetMaximumBonuses(templates);
            VerifyExtremeEngine(templates);
            foreach (ItemBonusMaximum limit in result)
            {
                limit.LimitKind = "OriginalTableFallback";
                limit.Evidence += " No stronger engine bound has yet been verified for this effect; this entry retains its declared table maximum.";
            }
            SetExtremeLimit(result, 185, 50, "EffectiveEngineLimit", "engine:B_SHJS_PER:0x10022ED2",
                "ERRelive 0x10022ED2..0x10022ED7 clamps total percentage damage reduction to 50. One neutral 50-point percentage bonus reaches that effective ceiling; it does not provide immunity.");
            SetExtremeLimit(result, 100, 100, "EffectiveEngineLimit", "client:critical:0x00425B04",
                "SOE 0x00425B04..0x00425B19 compares rand()%100 against the critical threshold. A total of 100 saturates that probability for the applicable attack types 5/6; larger values add no probability benefit. This does not make every kind of attack eligible.", "SOE.EXE");
            SetExtremeLimit(result, 164, 50, "EffectiveEngineLimit", "client:absolute-evasion:0x0041FCAA",
                "SOE 0x0041FCAA..0x0041FCC3 clamps the absolute-evasion threshold to 50 before comparing rand()%100. The effective maximum is 50%, not guaranteed evasion.", "SOE.EXE");
            foreach (int effect in new[] { 122, 142 })
                SetExtremeLimit(result, effect, 100, "EffectiveEngineLimit", "engine:resource-on-kill:0x10030F74",
                    "ERRelive 0x10030F74/0x10030FC3 computes maximum health/energy * total kill-restoration percentage / 100, then caps the restored resource at its current maximum. A single 100% bonus already restores the full resource on a qualifying kill; larger percentages cannot restore beyond full.");
            foreach (int effect in new[] { 22, 24 })
                SetExtremeLimit(result, effect, 4096, "ConservativeOperationalLimit", "client:resource-snapshot-signed16:0x0059D800",
                    "Although one resource message carries DWORDs, the character-panel snapshot sign-extends maximum health at SOE 0x0059D7F7..0x0059D800 and maximum energy at 0x0059D862..0x0059D86A before assigning HP_MAX/MP_MAX. Totals above 32767 can become negative or wrap to unrelated small values; +65536 was observed to wrap in the running game and is not a usable preset. Seven equipped items at +4096 plus <=4095 additional base maximum resource total 32767. This is a per-item operating budget, not a global engine maximum. The paired +512 regeneration profile remains within signed Int32 arithmetic. Existing above-profile, duplicate or extra bonuses can exceed the budget and are retained rather than silently repaired.", "SOE.EXE");
            foreach (int effect in new[] { 120, 140 })
                SetExtremeLimit(result, effect, 512, "ConservativeOperationalLimit", "engine:resource-regeneration-budget:0x10031317",
                    "ERRelive 0x10031317..0x10031323 and 0x100313A0..0x100313B1 multiply (100 + total regeneration bonus) by maximum health/energy as signed Int32 before dividing by 120/80. The +512% per-item operating value is paired with +4096 maximum resource, seven equipped items and <=4095 additional base maximum resource, keeping the resource total within the client's signed16 snapshot range. The regeneration product plus <=65536 total flat regeneration remains within Int32. It is a regeneration-speed modifier, not a percentage of maximum resource restored per second, and not a hard engine maximum. Existing above-profile, duplicate or extra bonuses can exceed the joint budget.");
            SetExtremeLimit(result, 200, 4096, "ConservativeOperationalLimit", "engine:attack-arithmetic-budget:0x10022E22",
                "The native attack effect applies points. This per-item operating value is paired with +1024 back-attack and +1024 attack-after-being-hit points. With seven equipped items and <=8192 extra normal attack, A=51200. ERRelive 0x10022E67..0x10022EA4 and 0x10022F1D..0x10023142 remain within signed Int32 under skill-500 bonus <=2000, elemental multiplier <=1.6, critical <=2, frozen target <=1.5, impact <=199% and world multiplier <=1.5. The resulting <=15405467 damage also leaves room for an ordinary opponent's <=100% reflection at 0x10023637. This is an operating envelope, not a hard engine maximum; existing stronger/duplicate bonuses, edited skills/NPCs or two extreme characters fighting each other are outside it.");
            foreach (int effect in new[] { 101, 104 })
                SetExtremeLimit(result, effect, 1024, "ConservativeOperationalLimit", "engine:conditional-attack-points:0x10022E22",
                    "ERRelive 0x10022E22 and 0x10022E5B add back-attack and attack-after-being-hit as attack points, not percentages. The +1024 point operating value is paired with +4096 attack per item and a seven-item arithmetic envelope: extra normal attack <=8192, skill-500 bonus <=2000, elemental <=1.6, critical <=2, frozen target <=1.5, impact <=199%, world <=1.5 and ordinary opponent reflection <=100%. This is not an engine hard cap; stronger existing/duplicate effects, modified skills/NPCs or dual-extreme combat can exceed the envelope.");
            foreach (int effect in new[] { 201, 184 })
                SetExtremeLimit(result, effect, 32768, "ConservativeOperationalLimit", "engine:damage-mitigation-floor:0x10022EF7",
                    "This is a strong per-item point value, not a proved hard engine cap. Seven equipped items contribute 229376 points. Defense 201 with neutral p2=0 applies to all five elemental defenses; 184 is flat damage reduction. ERRelive 0x10022EF7..0x10022F19 still enforces damage of at least 5% of the attack input, so this profile does not claim damage immunity. Extra or existing above-profile/duplicate bonuses are outside the stated arithmetic budget.");
            foreach (int effect in new[] { 57, 58 })
                SetExtremeLimit(result, effect, 2048, "ConservativeOperationalLimit", "client:signed-rating-transport:0x004989BD",
                    "Evasion and accuracy are ratings, not direct percentages. ERRelive 0x10036BAE..0x10036BBE transports only 16 bits; SOE 0x004989BD and 0x004989C0 sign-extend those values. Total actor rating must stay at or below 32767 to avoid negative wrap. Seven equipped items at +2048 plus at most 16384 additional base rating total 30720. This is an operating profile, not a global combat maximum. Existing higher, duplicate or extra bonuses can exceed the envelope.", "SOE.EXE");
            SetExtremeLimit(result, 80, 4096, "ConservativeOperationalLimit", "engine:fury-gain-budget:0x10031012",
                "ERRelive 0x10031012..0x10031017 computes victim fury * (100 + total fury-gain percentage) using signed Int32 before division. With seven equipped items at +4096% and victim fury <=65535, the multiplication remains below Int32.MaxValue. This is a conservative operating profile, not a hard engine cap; stronger existing/duplicate bonuses or targets beyond the stated fury bound can exceed that budget.");
            SetExtremeLimit(result, 103, 100, "ConservativeOperationalLimit", "engine:reflected-damage-budget:0x10023637",
                "ERRelive 0x10023637 multiplies incoming raw damage by the reflected-damage percentage as signed Int32 before dividing by 100. The +100% operating value is retained rather than arbitrarily increasing that multiplier. Seven equipped items total 700%; the arithmetic envelope requires incoming raw damage <=3067833. This is not a hard engine cap and does not guarantee safety for two extreme characters fighting each other or other above-profile combat values.");
            SetExtremeLimit(result, 180, 100, "EffectiveEngineLimit", "engine:exorcism-probability:0x10023866",
                "ERRelive 0x10023866..0x100238BD compares a rand(100) result with B_QMGJ plus skill effect 508. A 100% neutral bonus saturates that probability when additional skill chance is nonnegative; higher values do not improve that probability.");
            foreach (int effect in new[] { 160, 161 })
                SetExtremeLimit(result, effect, 4096, "ConservativeOperationalLimit", "client:signed-speed-transport:0x00498B54",
                    "ERRelive 0x10036F85..0x10036FAE transports speed bonuses in 16-bit halves; SOE 0x00498B54..0x00498B65 sign-extends them. Seven equipped items at +4096% plus <=4095 other speed/buff points total 32767. Movement at 0x00424925..0x00424944 multiplies base distance by (100+movement bonus)/100; attack timing at 0x004205CE..0x004205E2 and 0x004259F1..0x00425A09 scales by 100/(100+attack-speed bonus). This is an operational signed-transport bound, not a hard useful cap or infinite attack rate. Extreme movement can be difficult to control; stronger existing/duplicate bonuses or extra buffs can wrap the signed field and stop movement.", "SOE.EXE");
            foreach (int effect in new[] { 102, 123, 143, 163, 181 })
                SetExtremeLimit(result, effect, 4096, "ConservativeOperationalLimit", "engine:on-hit-proc-coefficient:0x10023395",
                    "ERRelive 0x10023395..0x1002345E scales the on-hit status/deadly-attack threshold using attack difference, the proc coefficient and a resistance denominator. This coefficient is not a standalone final probability and does not guarantee activation against every target. The operating envelope allows seven +4096 coefficients plus <=4095 other proc coefficient, total <=32767, pre-proc attacker AP <=100000 and status/impact-resistance totals <=18432. Stronger or duplicated effects, arbitrary skill edits or modified targets are outside that envelope.");
            SetExtremeLimit(result, 144, 2048, "ConservativeOperationalLimit", "engine:status-duration-budget:0x10023435",
                "ERRelive 0x10023435..0x1002343E and 0x10078147/0x10078207 scale duration as base timer * (100+duration bonus)/(100+target resistance), with signed Int32 multiplication before division. With seven +2048% items and <=4096 other duration bonus, total <=18432; base status timer <=100000 keeps the numerator <=1853200000. This increases a duration modifier, not a proc chance. It is an operating envelope, not a finite hard engine cap; existing stronger/duplicate modifiers or longer/custom timers can exceed it.");
            SetExtremeLimit(result, 182, 2048, "ConservativeOperationalLimit", "engine:impact-resistance-budget:0x1002300D",
                "ERRelive 0x1002300D/0x1002304C/0x10023088 uses diminishing resistance ratios with numerator constants 7500/5000/2500 divided by (100+resistance). No finite hard maximum is asserted. Seven +2048% items plus <=4096 other resistance give <=18432. The related on-hit arithmetic envelope requires pre-proc AP <=100000, so multiplication by (100+18432) remains <=1853200000. Existing stronger/duplicate bonuses, modified skills or targets are outside the envelope.");
            SetExtremeLimit(result, 183, 2048, "ConservativeOperationalLimit", "engine:status-resistance-budget:0x10023404",
                "ERRelive 0x10023404 uses (100+status resistance) in the on-hit probability/duration arithmetic; values above 100 can still help. The distinct direct-skill path at 0x100780B4..0x100780C9 rejects certain status classes at >=100, which is not universal immunity. Seven +2048% items plus <=4096 other resistance total <=18432; pre-proc AP and base status timers <=100000 keep related signed Int32 products <=1853200000. This is an operating envelope, not a hard global cap, and existing stronger/duplicate or custom effects can exceed it.");
            return result;
        }

        private static void SetExtremeLimit(IList<ItemBonusMaximum> limits, int effect, float value, string kind, string rule, string evidence, string sourceFile = "ERRelive.dll")
        {
            ItemBonusMaximum limit = limits.Single(m => m.AttributeId == effect);
            string tableRule = limit.RuleAlias;
            limit.Value = value; limit.LimitKind = kind; limit.RuleAlias = rule;
            limit.SourceFile = sourceFile; limit.SourceLine = 0;
            limit.Evidence = evidence + " Original table maximum: "
                + limit.OriginalTableValue.ToString("R", CultureInfo.InvariantCulture) + " (" + tableRule + ").";
        }

        private static void VerifyExtremeEngine(ItemTemplateCatalog templates)
        {
            VerifyExtremeBinary(Path.Combine(templates.GameDirectory, "ERRelive.dll"), ExtremeEngineSha256);
            VerifyExtremeBinary(Path.Combine(templates.GameDirectory, "SOE.EXE"), ExtremeClientSha256);
        }

        private static void VerifyExtremeBinary(string path, string expected)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Extreme reinforcement requires the verified installed Steam engine. Choose its game folder first.", path);
            string hash;
            using (SHA256 sha = SHA256.Create())
            using (Stream stream = File.OpenRead(path))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!String.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Extreme reinforcement is available only for the verified Steam ERRelive.dll and SOE.EXE build. Original-table presets remain independent of this native-engine check.");
        }

        public static QuickActionResult ApplyExtremeBonuses(SaveDocument document, ItemRecord item, ItemTemplateCatalog templates)
        { return ApplyBonuses(document, item, templates, true); }

        private static QuickActionResult ApplyBonuses(SaveDocument document, ItemRecord item, ItemTemplateCatalog templates, bool extreme)
        {
            if (document == null) throw new ArgumentNullException("document");
            // CaptureItem also verifies ownership and that this is an active item.
            ItemSnapshot rollback = document.CaptureItem(item);
            IList<ItemBonusMaximum> maximums = extreme ? GetExtremeBonuses(templates) : GetMaximumBonuses(templates);
            var result = new QuickActionResult {
                Maximums = maximums,
                ProfileName = extreme ? "Extreme reinforcement" : "Original table maxima",
                Limitations = maximums.Where(m => m.LimitKind == "OriginalTableFallback" || m.LimitKind == "ConservativeOperationalLimit")
                    .Select(m => m.Name + (m.LimitKind == "OriginalTableFallback"
                        ? ": original table fallback; a stronger effective engine bound is unverified."
                        : ": conservative operating profile, not a hard engine maximum. " + m.Evidence)).ToList().AsReadOnly(),
                SkippedEffects = templates.AttributeRules.Values.Select(r => r.AttributeId)
                    .Where(id => !PersistentEffects.Contains(id)).Distinct().OrderBy(id => id).ToList().AsReadOnly()
            };
            var changes = maximums.Select(m => new {
                Maximum = m,
                Existing = item.Modifiers.FirstOrDefault(x => x.AttributeId == m.AttributeId && IsNeutralAbsolute(x))
            }).ToList();
            bool hasChanges = changes.Any(c => c.Existing == null || c.Existing.Value < c.Maximum.Value);
            try
            {
                if (hasChanges) ClearGeneratedDescriptions(item);
                foreach (var change in changes)
                {
                    if (change.Existing == null)
                    {
                        document.AddModifier(item, change.Maximum.AttributeId, change.Maximum.Value);
                        result.AddedCount++;
                    }
                    else if (change.Existing.Value < change.Maximum.Value)
                    {
                        change.Existing.Value = change.Maximum.Value;
                        result.UpdatedCount++;
                    }
                    else result.UnchangedCount++;
                }
            }
            catch
            {
                document.RestoreItem(item, rollback);
                throw;
            }
            result.Summary = result.AddedCount.ToString(CultureInfo.InvariantCulture) + " bonuses added, "
                + result.UpdatedCount.ToString(CultureInfo.InvariantCulture) + (extreme ? " raised to extreme profile values, " : " raised to original table limits, ")
                + result.UnchangedCount.ToString(CultureInfo.InvariantCulture) + " already at or above those limits. "
                + "Conditional, elemental and existing duplicate bonuses were preserved. Consumable actions are excluded.";
            if (extreme)
            {
                int fallbacks = maximums.Count(m => m.LimitKind == "OriginalTableFallback");
                int operating = maximums.Count(m => m.LimitKind == "ConservativeOperationalLimit");
                if (fallbacks > 0) result.Summary += " " + fallbacks.ToString(CultureInfo.InvariantCulture)
                    + " effects retain original table values because stronger effective bounds are unverified.";
                if (operating > 0) result.Summary += " " + operating.ToString(CultureInfo.InvariantCulture)
                    + " effects use conservative operating values with stated arithmetic budgets, not engine-wide maxima.";
            }
            return result;
        }

        /// <summary>Set both current and base level/element requirements, keeping recalculation baselines consistent.</summary>
        public static int SetRequirementsToOne(SaveDocument document, ItemRecord item)
        {
            if (document == null) throw new ArgumentNullException("document");
            ItemSnapshot rollback = document.CaptureItem(item);
            string[] keys = { "attr.152", "attr.153", "attr.154", "attr.155" };
            var requirements = keys.Select(key => item.Fields.SingleOrDefault(f => f.Key == key)).ToList();
            if (requirements.Any(f => f == null || f.Kind != "Int32" || !f.Editable))
                throw new InvalidOperationException("This item does not expose all four verified current/base requirement fields.");
            int changed = requirements.Count(f => f.Value != 1);
            if (changed == 0) return 0;
            try
            {
                foreach (SaveField field in requirements) field.Value = 1;
                ClearGeneratedDescriptions(item);
            }
            catch
            {
                document.RestoreItem(item, rollback);
                throw;
            }
            return changed;
        }

        private static bool IsPositiveFinite(double value)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value > 0; }

        private static string UnitForEffect(int effect)
        {
            if (effect == 22 || effect == 24 || effect == 101 || effect == 104 || effect == 184 || effect == 200 || effect == 201) return "Points";
            if (effect == 57 || effect == 58) return "Rating";
            if (effect == 102 || effect == 123 || effect == 143 || effect == 163 || effect == 181) return "ChanceCoefficient";
            return "Percent";
        }

        private static bool IsNeutralAbsolute(ItemModifier modifier)
        {
            // Match the native 0x4340 / 0x52CE0 constructor exactly. A same-ID
            // hidden, disabled, conditional, growing, percent-mode or elemental
            // record must never be silently repurposed by a bulk action.
            return Word(modifier, 1) == 0x11 && Word(modifier, 2) == 0 && Word(modifier, 3) == -1
                && Word(modifier, 5) == 1 && Word(modifier, 6) == 0 && Word(modifier, 7) == 0
                && Word(modifier, 8) == 0 && Word(modifier, 10) == 0 && Word(modifier, 11) == 0 && Word(modifier, 12) == 0;
        }

        private static double Word(ItemModifier modifier, int word)
        {
            SaveField field = modifier.Fields.Single(f => f.ModifierWord == word);
            return Double.Parse(field.Text, CultureInfo.InvariantCulture);
        }

        private static void ClearGeneratedDescriptions(ItemRecord item)
        {
            foreach (SaveField field in item.Fields.Where(f => f.Key == "string.G_Info" || f.Key == "string.G_Info2" || f.Key == "string.G_Info3"))
                if (field.Text.Length > 0) field.Text = "";
        }
    }
}
