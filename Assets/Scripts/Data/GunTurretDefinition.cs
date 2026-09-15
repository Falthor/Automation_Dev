using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Gun Turret: accepts one specific ammo item and holds it, up to
    /// MaxAmmoStack - the same intake shape as the Gas Powerplant's fuel (PowerplantGazDefinition),
    /// not the recipe-based production contract: no player choice, nothing crafted.
    ///
    /// Combat itself (targeting, firing, consuming the held ammo) is not implemented - this project
    /// has no threat/enemy system yet for a turret to answer to. See GunTurretRuntime.
    /// </summary>
    [CreateAssetMenu(fileName = "GunTurretDefinition", menuName = "Game/Buildings/Gun Turret Definition")]
    public sealed class GunTurretDefinition : BuildingDefinition
    {
        [SerializeField] ItemDefinition ammoItem;
        [SerializeField, Min(1)] int maxAmmoStack = 50;

        public ItemDefinition AmmoItem => ammoItem;
        public int MaxAmmoStack => maxAmmoStack;

        public override bool HasInputArrows => true;
    }
}
