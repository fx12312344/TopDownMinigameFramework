namespace RaidDocksExample
{
    /// <summary>
    /// Game-only entry point. The framework's ModLoader bridge owns content registration
    /// through Localization/en.csv, not a cross-mod assembly reference.
    /// </summary>
    public sealed class Entry : IMod
    {
        public void Init(ModManifest manifest) { }
        public void OnEnable() { }
        public void OnDisable() { }
    }
}
