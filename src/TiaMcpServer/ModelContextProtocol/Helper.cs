using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class Helper
    {
        public static List<Attribute> GetAttributeList(IEngineeringObject obj)
        {
            var attributes = new List<Attribute>();

            if (obj != null)
            {
                foreach (var attr in obj.GetAttributeInfos())
                {
                    object value = obj.GetAttribute(attr.Name);
                    attributes.Add(new Attribute
                    {
                        Name = attr.Name,
                        Value = SanitizeAttributeValue(value),
                        AccessMode = Enum.GetName(typeof(EngineeringAttributeAccessMode), attr.AccessMode)
                    });
                }
            }

            return attributes;
        }

        // Some attribute values coming back from Openness aren't JSON-serializable as-is (e.g. a
        // COM-wrapped object reference rather than a plain value) - that throws deep inside the
        // MCP SDK's own response serialization, AFTER the tool method has already returned
        // normally, so it can't be caught by a try/catch around the tool body. Live-tested: this
        // broke GetSubnets on a real project (an attribute value no device's attributes happened
        // to hit). Only widen this allow-list for types verified to serialize safely - everything
        // else degrades to its string form rather than risking the same failure elsewhere.
        private static object? SanitizeAttributeValue(object? value)
        {
            switch (value)
            {
                case null:
                case string:
                case bool:
                case byte:
                case sbyte:
                case short:
                case ushort:
                case int:
                case uint:
                case long:
                case ulong:
                case float:
                case double:
                case decimal:
                case DateTime:
                case DateTimeOffset:
                case Guid:
                case Enum:
                    return value;
                default:
                    return value.ToString();
            }
        }

        /// <summary>
        /// First available translation of a MultilingualText (tag and constant comments), or
        /// null when the text has no items. Openness returns one item per project language.
        /// </summary>
        public static string? FirstText(MultilingualText? text)
        {
            if (text == null)
            {
                return null;
            }

            foreach (var item in text.Items)
            {
                return item.Text;
            }

            return null;
        }

        public static BlockGroupInfo BuildBlockHierarchy(PlcBlockGroup group, TiaMcpServer.Siemens.Portal? portal = null)
        {
            var groupInfo = new BlockGroupInfo
            {
                Name = group.Name
            };

            var blockList = new List<ResponseBlockInfo>();
            foreach (var block in group.Blocks)
            {
                var attributes = Helper.GetAttributeList(block);
                blockList.Add(new ResponseBlockInfo
                {
                    Path = portal == null ? null : portal.GetBlockPath(block),
                    Name = block.Name,
                    TypeName = block.GetType().Name,
                    Namespace = block.Namespace,
                    ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                    MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                    IsConsistent = block.IsConsistent,
                    HeaderName = block.HeaderName,
                    ModifiedDate = block.ModifiedDate,
                    IsKnowHowProtected = block.IsKnowHowProtected,
                    Attributes = attributes,
                    Description = block.ToString()
                });
            }
            groupInfo.Blocks = blockList;

            var groupList = new List<BlockGroupInfo>();
            foreach (var subGroup in group.Groups)
            {
                groupList.Add(BuildBlockHierarchy(subGroup, portal));
            }
            groupInfo.Groups = groupList;

            return groupInfo;
        }
    }
}
