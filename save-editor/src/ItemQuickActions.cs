using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
        public string Summary { get; internal set; }
    }

    /// <summary>Atomic item presets using verified persistent effects and installed table limits.</summary>
    public static class ItemQuickActions
    {
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
                    Evidence = "Original table annotation MaximumValue (column 14); " + rule.Alias + ", "
                        + rule.SourceFile + ":" + rule.SourceLine.ToString(CultureInfo.InvariantCulture)
                        + ". Applied as an enabled neutral absolute bonus; existing larger bonuses are retained."
                });
            }
            return result.AsReadOnly();
        }

        public static QuickActionResult ApplyMaxBonuses(SaveDocument document, ItemRecord item, ItemTemplateCatalog templates)
        {
            if (document == null) throw new ArgumentNullException("document");
            // CaptureItem also verifies ownership and that this is an active item.
            ItemSnapshot rollback = document.CaptureItem(item);
            IList<ItemBonusMaximum> maximums = GetMaximumBonuses(templates);
            var result = new QuickActionResult {
                Maximums = maximums,
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
                + result.UpdatedCount.ToString(CultureInfo.InvariantCulture) + " raised to original table limits, "
                + result.UnchangedCount.ToString(CultureInfo.InvariantCulture) + " already at or above those limits. "
                + "Conditional, elemental and existing duplicate bonuses were preserved. Consumable actions are excluded.";
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
