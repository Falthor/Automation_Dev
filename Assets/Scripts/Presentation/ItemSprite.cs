using Game.Data;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The sprite that stands for an item, wherever one is drawn in the world.
    ///
    /// Real art when the item has some, and a solid square in its fallback colour when it does not -
    /// so an item added before its icon still reads as something rather than as nothing. Extracted
    /// rather than copied when a second view needed the same answer (<see cref="ItemVisualSync"/>
    /// for items in transit, <see cref="RecipeOverlayView"/> for what a machine is making): two
    /// copies of "icon, else placeholder" would drift the day either gains a case.
    /// </summary>
    public static class ItemSprite
    {
        public static Sprite For(ItemDatabase itemDatabase, ProceduralSpriteFactory spriteFactory, string itemId)
        {
            ItemDefinition item = itemDatabase != null ? itemDatabase.Get(itemId) : null;
            if (item != null && item.Icon != null) return item.Icon;

            Color fallback = item != null ? item.FallbackColor : Color.magenta;
            return spriteFactory.CreateSolidSquareSprite(fallback);
        }
    }
}
