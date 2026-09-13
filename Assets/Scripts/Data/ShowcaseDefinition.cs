using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// A building that only shows its art: no behaviour, no panel, nothing it accepts or produces.
    /// Its runtime is a plain BuildingRuntime - placed, drawn, animated when it has frames, saved and
    /// demolished like any other, and otherwise inert.
    ///
    /// For looking at a building's art in the game before its behaviour exists. Temporary by
    /// nature: when one gains real behaviour it gets its own definition and runtime, and its asset
    /// moves over.
    ///
    /// PowerDemandKw is a preview figure only, for the Building menu's consumption pill - a plain
    /// BuildingRuntime never registers a draw with PowerSystem, so it never actually consumes.
    /// </summary>
    [CreateAssetMenu(fileName = "ShowcaseDefinition", menuName = "Game/Buildings/Showcase Definition")]
    public sealed class ShowcaseDefinition : BuildingDefinition
    {
        [SerializeField, Min(0f)] float powerDemandKw;

        /// <summary>False for art that lies flat on the ground rather than standing on it (a cable) - see BuildingDefinition.DrawsShadow.</summary>
        [SerializeField] bool drawsShadow = true;

        /// <summary>True for art with a facing to show (a straight run laid in a direction) - see BuildingDefinition.RotatesSpriteWithFacing. False for omnidirectional art (a junction, a hub).</summary>
        [SerializeField] bool rotatesSpriteWithFacing;

        /// <summary>Which way this art was drawn facing at rest - see BuildingDefinition.ArtNativeDirection.</summary>
        [SerializeField] Direction artNativeDirection = Direction.North;

        /// <summary>Only there to be looked at - it takes no slot against the building cap.</summary>
        public override bool CountsAgainstBuildingCap => false;

        public override float PowerDemandKw => powerDemandKw;

        /// <summary>False regardless of PowerDemandKw: it never actually draws, so it earns no row on the Énergie panel's Priorité tab - that would show a group forever stuck at "alimenté" for a demand nothing ever asks the network for.</summary>
        public override bool DrawsPower => false;

        public override bool DrawsShadow => drawsShadow;

        public override bool RotatesSpriteWithFacing => rotatesSpriteWithFacing;

        public override Direction ArtNativeDirection => artNativeDirection;
    }
}
