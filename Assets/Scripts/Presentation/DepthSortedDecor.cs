using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Marks a scene sprite as belonging to the sorted band, so its rank is computed at runtime
    /// instead of being written into the scene.
    ///
    /// It exists because a sorted-band rank cannot be baked any more: ranks are measured against a
    /// depth window that follows the camera (<see cref="DepthSortLadder"/>), so a number frozen in a
    /// scene would be correct only while the camera sat where the generator imagined it. What the
    /// scene stores is the sprite and its position; the rank follows from those.
    ///
    /// Only for decor that <b>rises above its base</b> - a rock, a mast. Anything lying flat belongs
    /// in the ground band at a fixed order and does not need this.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DepthSortedDecor : MonoBehaviour
    {
        [SerializeField] int subLayer = SortingBands.SubSprite;

        /// <summary>
        /// Puts this sprite on the ladder, keyed on the bottom of what it actually draws - it owns no
        /// footprint to measure, unlike a building.
        /// </summary>
        public void RegisterWith(DepthSortLadder ladder)
        {
            if (ladder == null) return;

            var renderer = GetComponent<SpriteRenderer>();
            if (renderer == null) return;

            // Dropped first: a second sweep over the same scene must re-rank this sprite, not add a
            // duplicate entry that the ladder would then keep re-applying forever.
            ladder.Unregister(renderer);
            ladder.Register(renderer, renderer.bounds.min.y, subLayer);
        }
    }
}
