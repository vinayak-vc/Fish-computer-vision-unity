namespace ViitorCloud.FishAquarium.Fish {
    /// <summary> Stages a fish passes through, from a PNG appearing on disk to its GameObject being destroyed. </summary>
    public enum FishLifecycleState {
        Discovered = 0,
        Loading = 1,
        Validated = 2,
        Spawning = 3,
        Entering = 4,
        Swimming = 5,
        Removing = 6,
        Destroyed = 7
    }
}
