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
        [SerializeField, Min(1)] int actionRadiusCells = 50;
        [SerializeField, Min(0f)] float cuOutput = 3000f;
        [SerializeField, Min(0.01f)] float cuOutputIntervalSeconds = 5f;
        [SerializeField, Min(0f)] float powerOutputKw = 20f;

        public int ActionRadiusCells => actionRadiusCells;

        /// <summary>CU granted into the global reserve in one go, every CuOutputIntervalSeconds - no cable/network needed.</summary>
        public float CuOutput => cuOutput;

        /// <summary>How often the CuOutput grant lands, in seconds.</summary>
        public float CuOutputIntervalSeconds => cuOutputIntervalSeconds;

        /// <summary>Permanent Power supply, reported unconditionally every tick.</summary>
        public float PowerOutputKw => powerOutputKw;
    }
}
