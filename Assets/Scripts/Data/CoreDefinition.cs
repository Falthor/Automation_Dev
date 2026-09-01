using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Core building: automatically placed once by world generation
    /// at the start of a game, never by the player. Its action radius is gameplay data (drives
    /// where world generation may place resource deposits), not just a visual.
    /// </summary>
    [CreateAssetMenu(fileName = "CoreDefinition", menuName = "Game/World/Core Definition")]
    public sealed class CoreDefinition : BuildingDefinition
    {
        [SerializeField] Sprite sprite;
        [SerializeField, Min(1)] int actionRadiusCells = 50;

        public Sprite Sprite => sprite;
        public int ActionRadiusCells => actionRadiusCells;
    }
}
