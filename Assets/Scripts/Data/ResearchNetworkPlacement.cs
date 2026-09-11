using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Where a research sits on the network, from what its asset stores (ResearchDefinition.Tier and
    /// Angle) and back. The one place the convention is written, so the panel that draws the network
    /// and the editor that places it cannot disagree: the tier counted in rings of ringStep from the
    /// centre - between two rings as well as on one - and the angle in degrees counter-clockwise from
    /// the right, y up. A screen with y down flips y.
    ///
    /// Polar rather than a position, so the network can be drawn at any size and around any centre.
    /// </summary>
    public static class ResearchNetworkPlacement
    {
        public static Vector2 Offset(float tier, float angleDegrees, float ringStep)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (tier * ringStep);
        }

        /// <summary>The distance in rings - never inside the first, the centre is the Datacenter's - and the angle, in [0, 360). Neither is rounded: how a dropped node settles is the editor's choice, not the convention's.</summary>
        public static void FromOffset(Vector2 offset, float ringStep, out float tier, out float angleDegrees)
        {
            tier = Mathf.Max(1f, offset.magnitude / ringStep);
            angleDegrees = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg, 360f);
        }
    }
}
