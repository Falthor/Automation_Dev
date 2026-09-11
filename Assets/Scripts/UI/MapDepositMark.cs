using UnityEngine;

namespace Game.UI
{
    /// <summary>Which ore a deposit holds, as far as the map is concerned. A colour, not an item.</summary>
    public enum MapOreKind
    {
        /// <summary>An ore the map has no colour for. Drawn in the fallback rather than skipped: a deposit the player can see on the ground and not on the map is worse than one drawn in the wrong grey.</summary>
        Unknown = 0,

        Iron = 1,
        Copper = 2,
        Coal = 3
    }

    /// <summary>
    /// One deposit cell, as the map needs it: where, and which ore.
    ///
    /// <b>One mark per cell, not per cluster.</b> A cluster is six to fifteen touching cells and the
    /// map draws them as touching squares, so the patch reads as a patch - which is the shape a
    /// player is looking for when deciding where to put an Extractor. Grouping them into one mark
    /// would have thrown away exactly the information worth showing.
    /// </summary>
    public readonly struct MapDepositMark
    {
        public readonly int CellX;
        public readonly int CellY;
        public readonly MapOreKind Ore;

        public MapDepositMark(int cellX, int cellY, MapOreKind ore)
        {
            CellX = cellX;
            CellY = cellY;
            Ore = ore;
        }

        public bool SameAs(MapDepositMark other)
            => CellX == other.CellX && CellY == other.CellY && Ore == other.Ore;

        /// <summary>
        /// Which ore an item id names. Matched on the id rather than on a definition reference, so the
        /// map needs nothing from the item database - and an ore added later draws in the fallback
        /// instead of failing to compile somewhere unrelated.
        /// </summary>
        public static MapOreKind OreFor(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return MapOreKind.Unknown;

            if (itemId.Equals("iron_ore", System.StringComparison.OrdinalIgnoreCase)) return MapOreKind.Iron;
            if (itemId.Equals("copper_ore", System.StringComparison.OrdinalIgnoreCase)) return MapOreKind.Copper;
            if (itemId.Equals("Coal_ore", System.StringComparison.OrdinalIgnoreCase)) return MapOreKind.Coal;

            return MapOreKind.Unknown;
        }
    }
}
