using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Runtime state of one installed CPU/Memory component in a Data Center slot. Each installed
    /// component needs independent state, so this is a plain mutable instance, never a shared
    /// ScriptableObject. Direct translation of the source project's component_instance.gd.
    /// </summary>
    public sealed class ComponentInstance
    {
        const float ReplacementThreshold = 5f;

        public string ItemId { get; }

        /// <summary>Nominal CU/s, snapshotted once at construction from ItemDefinition.CuOutput - independent of later registry balance changes.</summary>
        public float BaseCu { get; }

        /// <summary>kW drawn while installed and active, snapshotted once at construction from ItemDefinition.PowerKw.</summary>
        public float PowerKw { get; }

        /// <summary>Percent, 0..100. Fixed at 80 on install - the probability used by RecalculatePerformance(), not itself the output multiplier.</summary>
        public float Stability { get; } = 80f;

        /// <summary>Percent, 0..100. Starts fully unworn, decays via DecayWear().</summary>
        public float Wear { get; private set; } = 100f;

        /// <summary>0..1 multiplier actually applied to BaseCu. Starts at 1 until the first 5s recalculation.</summary>
        public float EffectivePerformance { get; private set; } = 1f;

        /// <summary>True from the moment Wear crosses the replacement threshold until swapped or hard-removed. While true, EffectiveCu() is forced to 0.</summary>
        public bool IsReplacing { get; set; }

        /// <summary>Seconds elapsed since IsReplacing became true.</summary>
        public float ReplacementElapsed { get; set; }

        public ComponentInstance(string itemId, ItemDatabase itemDatabase)
        {
            ItemId = itemId;
            ItemDefinition item = itemDatabase.Get(itemId);
            BaseCu = item != null ? item.CuOutput : 0f;
            PowerKw = item != null ? item.PowerKw : 0f;
        }

        /// <summary>One stability roll: Stability% chance of 100% performance, otherwise a fluctuation uniformly between 70% and 100%. Never touches BaseCu.</summary>
        public void RecalculatePerformance()
        {
            EffectivePerformance = UnityEngine.Random.value < Stability / 100f ? 1f : UnityEngine.Random.Range(0.70f, 1.0f);
        }

        /// <summary>1 percentage point per call, floored at 0. Never touches BaseCu or Stability.</summary>
        public void DecayWear()
        {
            Wear = Wear - 1f > 0f ? Wear - 1f : 0f;
        }

        public bool HasCrossedReplacementThreshold => Wear <= ReplacementThreshold;

        public float EffectiveCu() => IsReplacing ? 0f : BaseCu * EffectivePerformance;

        /// <summary>Flat draw (unlike EffectiveCu, not scaled by stability) - 0 while being replaced, same rule as EffectiveCu().</summary>
        public float ActivePowerKw() => IsReplacing ? 0f : PowerKw;

        /// <summary>Restores Wear/EffectivePerformance to a previously-captured value. Used only by the save/load system (CONTRACTS.md §14) - IsReplacing/ReplacementElapsed already have public setters and don't need this.</summary>
        public void RestoreWearAndPerformance(float wear, float effectivePerformance)
        {
            Wear = wear;
            EffectivePerformance = effectivePerformance;
        }
    }
}
