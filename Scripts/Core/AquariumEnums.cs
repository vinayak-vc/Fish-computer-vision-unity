namespace ViitorCloud.FishAquarium.Core {
    /// <summary> Root the configured fish folder is resolved against. Keeps machine specific absolute paths out of the asset. </summary>
    public enum InputFolderRoot {
        StreamingAssets = 0,
        PersistentData = 1,
        Absolute = 2
    }

    /// <summary> What happens when a new fish arrives while the aquarium is already at MaxFishCount. </summary>
    public enum FishOverflowStrategy {
        RemoveOldest = 0,
        RejectNew = 1
    }

    /// <summary> Where a newly created fish is placed before it starts swimming. </summary>
    public enum FishSpawnStrategy {
        EdgeEntry = 0,
        RandomInsideBounds = 1
    }

    /// <summary> Development builds expose debug tooling; production builds run unattended and fullscreen. </summary>
    public enum AquariumRuntimeMode {
        Development = 0,
        Production = 1
    }
}
