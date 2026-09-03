using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Single source of truth for item metadata (PROJECT_ARCHITECTURE.md §6: static content).
    /// One asset assigned on GameRuntime - not a Resources-loaded service locator
    /// (DEVELOPMENT_RULES.md §5).
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Game/Items/Item Database")]
    public sealed class ItemDatabase : ScriptableObject
    {
        [SerializeField] ItemDefinition[] items;

        Dictionary<string, ItemDefinition> _byId;

        /// <summary>Returns the definition for itemId, or null if it isn't registered.</summary>
        public ItemDefinition Get(string itemId)
        {
            if (_byId == null) BuildLookup();
            return itemId != null && _byId.TryGetValue(itemId, out var definition) ? definition : null;
        }

        void BuildLookup()
        {
            _byId = new Dictionary<string, ItemDefinition>();
            if (items == null) return;

            foreach (ItemDefinition item in items)
            {
                if (item != null && !string.IsNullOrEmpty(item.Id)) _byId[item.Id] = item;
            }
        }
    }
}
