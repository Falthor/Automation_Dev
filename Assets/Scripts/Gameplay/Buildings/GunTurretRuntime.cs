using Game.Core;
using Game.Data;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Holds ammo, up to GunTurretDefinition.MaxAmmoStack - the same intake shape as
    /// PowerplantGazRuntime's fuel.
    ///
    /// <b>Combat is not implemented.</b> Nothing here yet consumes the ammo held, targets anything or
    /// fires - this project has no threat/enemy system for a turret to answer to. Placing one and
    /// filling it with ammo is the whole of what it does today; a real firing/targeting pass is a
    /// separate, unscoped system.
    /// </summary>
    public sealed class GunTurretRuntime : BuildingRuntime
    {
        readonly GunTurretDefinition _definition;
        int _ammoAmount;

        public int AmmoAmount => _ammoAmount;

        public GunTurretRuntime(GunTurretDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
        }

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (_definition.AmmoItem == null || itemId != _definition.AmmoItem.Id) return false;
            return _ammoAmount + amount <= _definition.MaxAmmoStack;
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _ammoAmount += amount;

        public override int GetInputAmount(string itemId)
            => _definition.AmmoItem != null && itemId == _definition.AmmoItem.Id ? _ammoAmount : 0;

        public override JObject CaptureState() => new JObject { ["ammoAmount"] = _ammoAmount };

        public override void RestoreState(JObject state)
        {
            _ammoAmount = state.Value<int?>("ammoAmount") ?? 0;
        }
    }
}
