namespace Game.Gameplay.Sites
{
    /// <summary>A builder robot's state machine (TASK_05_ROBOT_CONSTRUCTEUR.md §7), driven entirely by ConstructionSiteSystem's central tick - never by an individual Update().</summary>
    public enum BuilderRobotState
    {
        Idle,
        MovingToSource,
        Loading,
        MovingToSite,
        Delivering,
        Repatriating,
        Blocked
    }
}
