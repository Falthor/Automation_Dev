namespace Game.UI
{
    /// <summary>
    /// UI-only grouping for the Building menu's category rail (matches the source project's
    /// Production/Logistic/Organisation tabs). Not gameplay data - purely a menu-presentation
    /// concern, so it lives in Game.UI rather than on BuildingDefinition itself.
    /// </summary>
    public enum BuildingCategory
    {
        Production,
        Power,
        Logistic,
        Organisation
    }
}
