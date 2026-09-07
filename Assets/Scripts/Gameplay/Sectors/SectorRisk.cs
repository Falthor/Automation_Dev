namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// How dangerous a sector is announced to be. Derived, never stored - see
    /// <see cref="SectorCatalog.RiskOf"/>.
    /// </summary>
    public enum SectorRisk
    {
        Low = 0,
        Moderate = 1,
        High = 2,
        Critical = 3
    }
}
