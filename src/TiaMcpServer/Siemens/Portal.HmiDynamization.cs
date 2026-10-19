using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // WinCC Unified screen item dynamizations in full (BACKLOG C6): which tag, value range or bit
        // drives a property (the tag dynamization's mapping table), the script and its trigger, and
        // flashing. GetHmiScreenItems already lists property, type and tag; this reads the rest.
        //
        // Callers: GetHmiDynamizations in McpServer.HmiDynamization.cs. Affected API: additive.
        // Reads no data files.
        //
        // Openness constraints (V20, read from the installed Siemens.Engineering.dll):
        // - Every dynamization is a DynamizationBase on UIBase.Dynamizations, keyed by property name;
        //   the concrete type (TagDynamization, ScriptDynamization, FlashingDynamization,
        //   ResourceListDynamization, ExpressionDynamization, TagParameterDynamization) carries the rest.
        // - A tag or expression dynamization maps the value through a ValueConverter: either a
        //   Formula, or a MappingTable whose ConditionType says how its entries are read (None:
        //   direct, Range, Singlebit, Bitmask). Entry values are typed by the property
        //   (System.Drawing.Color for colors, bool, numbers) but exposed as object.
        // - Bit entries carry 'Relevant' (the bit mask) and 'Condition' (the bit values that must
        //   match), e.g. bit 3 set: Relevant 8, Condition 8.

        #region read

        public sealed class HmiDynamizationsResult
        {
            public List<HmiItemDynamizations> Items { get; } = new List<HmiItemDynamizations>();
            public int ItemsLookedAt { get; set; }
            public string? StoppedReason { get; set; }
        }

        public HmiDynamizationsResult GetHmiDynamizations(string softwarePath, string screenName, string regexName, string propertyName, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetHmiDynamizations), PortalErrorCode.NotFound,
                () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var result = new HmiDynamizationsResult();

                    foreach (var item in GetHmiScreenItems(softwarePath, screenName, regexName))
                    {
                        if (stopwatch.Elapsed > budget)
                        {
                            result.StoppedReason = $"time budget of {budget.TotalSeconds:0} s used up after {result.ItemsLookedAt} item(s); narrow 'regexName' to read the rest";
                            break;
                        }

                        result.ItemsLookedAt++;

                        var dynamizations = item.Dynamizations
                            .Where(d => d != null && (string.IsNullOrEmpty(propertyName) || string.Equals(d.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase)))
                            .Select(DescribeDynamization)
                            .ToList();

                        if (dynamizations.Count > 0)
                        {
                            result.Items.Add(new HmiItemDynamizations
                            {
                                ItemName = item.Name,
                                Kind = KindOf(item),
                                Dynamizations = dynamizations
                            });
                        }
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        /// <summary>
        /// Full description of one dynamization. Each part is read on its own, so a property TIA
        /// Portal refuses to give out leaves an 'error' note instead of failing the whole screen.
        /// </summary>
        public static HmiDynamizationDetail DescribeDynamization(DynamizationBase dynamization)
        {
            var detail = new HmiDynamizationDetail
            {
                PropertyName = dynamization.PropertyName,
                Type = dynamization.DynamizationType.ToString()
            };

            try
            {
                switch (dynamization)
                {
                    case TagDynamization tag:
                        detail.Tag = tag.Tag;
                        detail.PlcTag = NullIfEmpty(tag.PlcTag);
                        detail.DataType = NullIfEmpty(tag.DataType);
                        detail.ReadOnly = tag.ReadOnly;
                        detail.UseIndirectAddressing = tag.UseIndirectAddressing ? true : null;
                        DescribeConverter(tag.ValueConverter, detail);
                        break;

                    case ExpressionDynamization expression:
                        DescribeConverter(expression.ValueConverter, detail);
                        break;

                    case ScriptDynamization script:
                        detail.ScriptCode = script.ScriptCode;
                        detail.GlobalDefinitionAreaScriptCode = NullIfEmpty(script.GlobalDefinitionAreaScriptCode?.Trim());
                        detail.Async = script.Async ? true : null;
                        DescribeTrigger(script.Trigger, detail);
                        break;

                    case FlashingDynamization flashing:
                        detail.FlashingCondition = flashing.FlashingCondition.ToString();
                        detail.FlashingRate = flashing.FlashingRate.ToString();
                        detail.Color = ColorText(flashing.Color);
                        detail.AlternateColor = ColorText(flashing.AlternateColor);
                        break;

                    case ResourceListDynamization resourceList:
                        detail.Tag = NullIfEmpty(resourceList.Tag);
                        detail.ResourceList = resourceList.ResourceList;
                        break;

                    case TagParameterDynamization tagParameter:
                        detail.Tag = tagParameter.DynamicTagName;
                        break;
                }
            }
            catch (Exception ex)
            {
                detail.Error = Operation.DescribeChain(ex);
            }

            return detail;
        }

        private static void DescribeConverter(ValueConverter? converter, HmiDynamizationDetail detail)
        {
            if (converter == null)
            {
                return;
            }

            if (converter.IsFormulaSelected)
            {
                detail.Formula = converter.Formula;
                return;
            }

            var table = converter.MappingTable;
            if (table == null || table.Entries.Count == 0)
            {
                return;
            }

            detail.ConditionType = table.ConditionType.ToString();
            detail.Mapping = table.Entries.Select(DescribeMappingEntry).ToList();
        }

        private static HmiMappingEntryDescription DescribeMappingEntry(MappingTableEntryBase entry)
        {
            var description = new HmiMappingEntryDescription
            {
                Value = ValueText(entry.Value),
                Flashing = entry.Flashing ? true : null
            };

            if (entry.Flashing)
            {
                description.AlternateValue = ValueText(entry.AlternateValue);
                description.FlashingRate = entry.FlashingRate.ToString();
            }

            switch (entry)
            {
                case MappingTableEntrySimple simple:
                    description.Condition = simple.Condition;
                    break;

                case MappingTableEntryRange range:
                    description.RangeType = range.RangeType.ToString();
                    description.From = range.RangeType == RangeType.To ? null : ValueText(range.From);
                    description.To = range.RangeType == RangeType.From ? null : ValueText(range.To);
                    break;

                case MappingTableEntryBitmask bits:
                    description.BitType = bits.BitDynamizationType.ToString();
                    description.Relevant = bits.Relevant;
                    description.BitCondition = bits.Condition;
                    description.Bits = BitNumbers(bits.Relevant);
                    break;
            }

            return description;
        }

        private static void DescribeTrigger(Trigger? trigger, HmiDynamizationDetail detail)
        {
            if (trigger == null)
            {
                return;
            }

            detail.TriggerType = trigger.Type.ToString();

            if (trigger.Type == TriggerType.CustomCycle)
            {
                detail.TriggerCycle = NullIfEmpty(trigger.CustomDuration);
            }

            // Trigger.Tags is typed object; it holds the tag names as a list.
            detail.TriggerTags = trigger.Tags switch
            {
                null => null,
                string single => string.IsNullOrEmpty(single) ? null : new List<string> { single },
                System.Collections.IEnumerable many => NullIfEmpty(many.Cast<object>().Select(t => t?.ToString() ?? string.Empty).Where(t => t.Length > 0).ToList()),
                var other => new List<string> { other.ToString() }
            };
        }

        /// <summary>Bit numbers set in a mask, e.g. 20 -> [2, 4].</summary>
        private static List<int> BitNumbers(ulong mask)
        {
            var bits = new List<int>();

            for (var bit = 0; bit < 64; bit++)
            {
                if ((mask & (1UL << bit)) != 0)
                {
                    bits.Add(bit);
                }
            }

            return bits;
        }

        /// <summary>Colors as '#RRGGBB' ('#AARRGGBB' when not opaque); other values as invariant text.</summary>
        private static string? ValueText(object? value) => value switch
        {
            null => null,
            System.Drawing.Color color => ColorText(color),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        private static string ColorText(System.Drawing.Color color) =>
            color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        private static List<T>? NullIfEmpty<T>(List<T> list) => list.Count == 0 ? null : list;

        #endregion
    }

    public class HmiItemDynamizations
    {
        public string ItemName { get; set; } = string.Empty;

        /// <summary>Button, IOField, Label, Text, Rectangle, or the Openness type name for other items.</summary>
        public string Kind { get; set; } = string.Empty;

        public List<HmiDynamizationDetail> Dynamizations { get; set; } = new List<HmiDynamizationDetail>();
    }

    public class HmiDynamizationDetail
    {
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>Tag, Script, Flashing, ResourceList, Expression or TagParameter.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The HMI tag: bound tag (Tag), index tag (ResourceList), dynamic tag name (TagParameter).</summary>
        public string? Tag { get; set; }

        public string? PlcTag { get; set; }
        public string? DataType { get; set; }
        public bool? ReadOnly { get; set; }
        public bool? UseIndirectAddressing { get; set; }

        /// <summary>Value converter formula, when a formula is selected instead of a mapping table.</summary>
        public string? Formula { get; set; }

        /// <summary>How the mapping entries are read: None (direct value), Range, Singlebit, Bitmask, Expression.</summary>
        public string? ConditionType { get; set; }

        /// <summary>Mapping table: which tag value gives which property value.</summary>
        public List<HmiMappingEntryDescription>? Mapping { get; set; }

        public string? ScriptCode { get; set; }
        public string? GlobalDefinitionAreaScriptCode { get; set; }
        public bool? Async { get; set; }

        /// <summary>Script trigger: Disabled, T100ms ... T10s, CustomCycle, Tags, AutomaticTags.</summary>
        public string? TriggerType { get; set; }

        public string? TriggerCycle { get; set; }
        public List<string>? TriggerTags { get; set; }

        /// <summary>Flashing: Never, Always or RangeViolation.</summary>
        public string? FlashingCondition { get; set; }

        /// <summary>Slow (0.5 Hz), Medium (1 Hz) or Fast (2 Hz).</summary>
        public string? FlashingRate { get; set; }

        public string? Color { get; set; }
        public string? AlternateColor { get; set; }

        public string? ResourceList { get; set; }

        /// <summary>Set when TIA Portal refused to give out part of this dynamization.</summary>
        public string? Error { get; set; }
    }

    public class HmiMappingEntryDescription
    {
        /// <summary>Simple entries: the tag value this entry matches.</summary>
        public string? Condition { get; set; }

        /// <summary>Range entries: Range, From (value and above) or To (value and below).</summary>
        public string? RangeType { get; set; }

        public string? From { get; set; }
        public string? To { get; set; }

        /// <summary>Bit entries: SingleBit or MultiBit.</summary>
        public string? BitType { get; set; }

        /// <summary>Bit entries: mask of the bits looked at, e.g. 8 for bit 3.</summary>
        public ulong? Relevant { get; set; }

        /// <summary>Bit entries: the value the relevant bits must have, e.g. 8 for bit 3 set, 0 for bit 3 clear.</summary>
        public ulong? BitCondition { get; set; }

        /// <summary>Bit entries: the bit numbers in 'Relevant'.</summary>
        public List<int>? Bits { get; set; }

        /// <summary>The property value for this entry, colors as '#RRGGBB'.</summary>
        public string? Value { get; set; }

        public bool? Flashing { get; set; }
        public string? AlternateValue { get; set; }
        public string? FlashingRate { get; set; }
    }
}
