using System.Collections.Generic;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// One tool type shown as a single wheel sector. The wheel merges every eligible GameItem
    /// sharing the same identifier into one sector and shows how many are usable, e.g.
    /// "水质检测试剂×5" means five usable items of that type.
    ///
    /// Distinct instances are NOT collapsed into one GameItem: all of them are kept so that at
    /// confirm time we can re-validate and equip an instance that is still valid. Only the
    /// display is grouped; ownership, admission and equipment execution stay per-instance.
    /// </summary>
    internal sealed class ToolGroup
    {
        private readonly string identifier;
        private readonly List<GameItem> instances = new List<GameItem>();
        private int count;

        internal ToolGroup(string id)
        {
            identifier = id;
        }

        internal string Identifier { get { return identifier; } }

        /// <summary>All eligible instances of this tool type (never merged into one object).</summary>
        internal List<GameItem> Instances { get { return instances; } }

        internal int Count { get { return count; } }

        /// <summary>Representative used for the icon and name; the first instance found.</summary>
        internal GameItem Representative
        {
            get { return instances.Count > 0 ? instances[0] : null; }
        }

        internal void Add(GameItem item)
        {
            if (item == null)
            {
                return;
            }
            instances.Add(item);
            // Stacked items report unitCount; each unit is one usable tool.
            int units = item.unitCount > 0 ? item.unitCount : 1;
            count += units;
        }

        /// <summary>Display name with the usable count, e.g. "水质检测试剂×5".</summary>
        internal string DisplayName()
        {
            GameItem rep = Representative;
            string name = rep == null ? identifier : rep.GetDisplayName(true);
            if (string.IsNullOrEmpty(name))
            {
                name = identifier;
            }
            return name + "×" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
