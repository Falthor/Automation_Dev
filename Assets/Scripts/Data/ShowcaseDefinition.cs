using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// A building that only shows its art: no behaviour, no power, no panel, nothing it accepts or
    /// produces. Its runtime is a plain BuildingRuntime - placed, drawn, animated when it has frames,
    /// saved and demolished like any other, and otherwise inert.
    ///
    /// For looking at a building's art in the game before its behaviour exists. Temporary by
    /// nature: when one gains real behaviour it gets its own definition and runtime, and its asset
    /// moves over.
    /// </summary>
    [CreateAssetMenu(fileName = "ShowcaseDefinition", menuName = "Game/Buildings/Showcase Definition")]
    public sealed class ShowcaseDefinition : BuildingDefinition
    {
        /// <summary>Only there to be looked at - it takes no slot against the building cap.</summary>
        public override bool CountsAgainstBuildingCap => false;
    }
}
