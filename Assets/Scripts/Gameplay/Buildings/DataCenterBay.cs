namespace Game.Gameplay.Buildings
{
    /// <summary>What a Data Center bay is configured to take. Unassigned installs nothing - the
    /// player must choose before it does anything (DATACENTER.md).</summary>
    public enum DataCenterBayType
    {
        Unassigned,
        Cpu,
        Memory
    }

    /// <summary>
    /// One physically universal Data Center bay: a type the player chose and, once one is
    /// installed, the component itself. Plain mutable instance, same family as
    /// <see cref="ComponentInstance"/> - never a shared ScriptableObject (DATACENTER.md).
    /// </summary>
    public sealed class DataCenterBay
    {
        public DataCenterBayType Assignment;
        public ComponentInstance Component;

        /// <summary>Non-null only while reconfiguring toward a different type - the target it will
        /// become once Component's replacement timer (reused for this) completes.</summary>
        public DataCenterBayType? ReconfigureTarget;
    }
}
