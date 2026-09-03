namespace Game.Data
{
    /// <summary>
    /// Coarse item classification. Ore is only the two raw ores that Foundry's input filter
    /// accepts (iron_ore/copper_ore) - Coal_ore is deliberately Component despite
    /// being a raw material, so Foundry (which filters strictly by Type == Ore) can never accept
    /// it; AdvancedFoundry filters by its own explicit accepted-item list instead, independent
    /// of this type. Ingot is Iron_Ingot/copper_Ingot/Steel. Everything else is Component.
    /// </summary>
    public enum ItemType
    {
        Ore,
        Ingot,
        Component
    }
}
