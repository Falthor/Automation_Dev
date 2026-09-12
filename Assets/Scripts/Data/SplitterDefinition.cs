using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Splitter: a single-cell logistics piece routing one conveyor-fed
    /// item across up to 3 outputs - every cardinal side except its one fixed entry side.
    /// </summary>
    [CreateAssetMenu(fileName = "SplitterDefinition", menuName = "Game/Buildings/Splitter Definition")]
    public sealed class SplitterDefinition : BuildingDefinition
    {
        /// <summary>Transport, not machinery - see BuildingDefinition.CountsAgainstBuildingCap.</summary>
        public override bool CountsAgainstBuildingCap => false;

        [SerializeField] Direction artNativeEntrySide = Direction.West;

        /// <summary>Which side the sprite's own chevron/entry marking visually shows at zero rotation.</summary>
        public Direction ArtNativeEntrySide => artNativeEntrySide;
    }
}
