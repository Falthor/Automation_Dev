using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Where a research sits on the network, from what its asset stores (ResearchDefinition.Tier and
    /// Angle) and back. The one place the convention is written, so the panel that draws the network
    /// and the editor that places it cannot disagree: a ring every ringStep from the centre, the angle
    /// in degrees counter-clockwise from the right, y up. A screen with y down flips y.
    ///
    /// Polar rather than a position, so the network can be drawn at any size and around any centre.
    /// </summary>
    public static class ResearchNetworkPlacement
    {
        public static Vector2 Offset(int tier, float angleDegrees, float ringStep)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (tier * ringStep);
        }

        /// <summary>The nearest ring - never inside the first, the centre is the Datacenter's - and the free angle, in [0, 360).</summary>
        public static void FromOffset(Vector2 offset, float ringStep, out int tier, out float angleDegrees)
        {
            tier = Mathf.Max(1, Mathf.RoundToInt(offset.magnitude / ringStep));
            angleDegrees = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg, 360f);
        }
    }
}
