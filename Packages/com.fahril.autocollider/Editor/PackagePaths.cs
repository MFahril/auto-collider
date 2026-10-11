namespace AutoCollider.Editor
{
    /// <summary>Asset paths inside this package. Keep every hard-coded path here.</summary>
    internal static class PackagePaths
    {
        public const string PackageName = "com.fahril.autocollider";
        public const string Root = "Packages/" + PackageName;
        public const string UssFolder = Root + "/Editor/UI/Uss";
        public const string MainStyleSheet = UssFolder + "/AutoCollider.uss";
        public const string SettingsAsset = "ProjectSettings/AutoColliderSettings.asset";
        public const string UserGenerationSettings = "UserSettings/AutoColliderGeneration.asset";
    }
}
