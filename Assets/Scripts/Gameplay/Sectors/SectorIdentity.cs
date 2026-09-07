using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// Everything the map needs to show about one sector, gathered in one value. Built on demand by
    /// <see cref="SectorCatalog.IdentityOf"/> and thrown away - no sector is ever an object that
    /// lives somewhere.
    ///
    /// The cells are not in here: they are arithmetic on the index
    /// (<see cref="SectorGrid.CellsOf"/>), and copying 144 coordinates into a value that mostly ends
    /// up in a tooltip would be the one expensive field.
    /// </summary>
    public readonly struct SectorIdentity
    {
        public readonly int Index;
        public readonly string Name;
        public readonly SectorRisk Risk;
        public readonly Vector2 CenterCells;
        public readonly SectorDiscovery Discovery;

        public SectorIdentity(int index, string name, SectorRisk risk, Vector2 centerCells, SectorDiscovery discovery)
        {
            Index = index;
            Name = name;
            Risk = risk;
            CenterCells = centerCells;
            Discovery = discovery;
        }
    }
}
