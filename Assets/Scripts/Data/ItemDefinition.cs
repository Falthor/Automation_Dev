using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static metadata for one item id (CONTRACTS.md §3's "itemId"). Mirrors the source
    /// project's single Items registry entry (type/icon/color/label) - Id is the fixed string
    /// key every building/inventory/recipe contract reads by, never a duplicated per-building copy.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDefinition", menuName = "Game/Items/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] ItemType type;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] Color fallbackColor = Color.magenta;
        [SerializeField, Min(0f)] float cuOutput;
        [SerializeField, Min(0f)] float powerKw;
        [SerializeField, Min(0f)] float nominalLifetimeSeconds;

        public string Id => id;
        public ItemType Type => type;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public Color FallbackColor => fallbackColor;

        /// <summary>Nominal CU/s once installed as a Data Center component. 0 for every non-installable item.</summary>
        public float CuOutput => cuOutput;

        /// <summary>kW drawn while installed and active as a Data Center component. 0 for every non-installable item.</summary>
        public float PowerKw => powerKw;

        /// <summary>Nominal lifetime (seconds) once installed as a Data Center component, before the ±25% per-instance dispersion (ComponentInstance). 0 for every non-installable item.</summary>
        public float NominalLifetimeSeconds => nominalLifetimeSeconds;
    }
}
