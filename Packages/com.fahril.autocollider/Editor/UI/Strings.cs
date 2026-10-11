namespace AutoCollider.Editor
{
    /// <summary>All user-facing text. Keeping it in one place makes wording consistent and localization possible.</summary>
    internal static class Strings
    {
        public const string ProductName = "Auto Collider";

        // Menus & shortcuts
        public const string MenuRoot = "Tools/" + ProductName + "/";
        public const string MenuProjectSettings = MenuRoot + "Project Settings…";
        public const string ShortcutCategory = ProductName;

        // Project Settings page
        public const string SettingsPath = "Project/" + ProductName;
        public const string SettingsSubtitle = "Defaults for collider generation, scene editing and visuals.";
        public const string SectionGeneration = "Generation";
        public const string SectionSceneView = "Scene View";
        public const string SectionColors = "Colors";
        public const string Browse = "Browse…";
        public const string BrowseTooltip = "Pick a folder inside Assets/ for generated collider meshes.";
        public const string BrowseDialogTitle = "Generated Collider Mesh Folder";
        public const string ResetToDefaults = "Reset to Defaults";
        public const string ResetToDefaultsTooltip = "Restore every Auto Collider project setting to its default value.";
        public const string ResetConfirmTitle = "Reset Auto Collider Settings";
        public const string ResetConfirmMessage = "Restore all Auto Collider project settings to their defaults?";
        public const string Reset = "Reset";
        public const string Cancel = "Cancel";
        public const string InvalidMeshFolder = "The generated mesh folder must be inside \"Assets/\".";

        // Generation results
        public const string SkipMissingTarget = "Object no longer exists.";
        public const string SkipNoMeshes = "No meshes found (check filters).";
        public const string SkipNoGroup = "No generated colliders.";
        public const string ErrorZeroScale = "Scale has a zero component.";
        public const string WarningNonUniformScale = "Non-uniform scale: rotated shapes disabled for this object.";
        public const string WarningInvalidPattern = "Invalid exclude pattern \"{0}\" was ignored.";
        public const string WarningMeshReadFailed = "Could not read mesh on \"{0}\": {1}";

        // Undo & progress
        public const string UndoGenerate = "Auto Collider: Generate";
        public const string UndoRemove = "Auto Collider: Remove Colliders";
        public const string UndoGenerateCount = "Auto Collider: Generate ({0})";
        public const string UndoRemoveCount = "Auto Collider: Remove Colliders ({0})";
        public const string UndoEditCollider = "Auto Collider: Edit Collider";
        public const string ProgressGenerateTitle = "Auto Collider — Generating";

        // Window
        public const string MenuOpenWindow = MenuRoot + "Open Window";
        public const string TabGenerate = "Generate";
        public const string TabOptimize = "Optimize";
        public const string TabEdit = "Edit";
        public const string TabDoctor = "Doctor";
        public const string ComingOptimize = "Mesh collider optimization arrives in milestone M2.";
        public const string ComingEdit = "In-scene collider editing arrives in milestone M4.";
        public const string ComingDoctor = "The scene validator arrives in milestone M7.";

        // Generate tab
        public const string TargetsHeader = "Targets ({0})";
        public const string TargetsTooltip = "Follows the Hierarchy selection. Children of a selected object are included automatically.";
        public const string TargetsEmpty = "Select objects in the Hierarchy to generate colliders for them.";
        public const string TargetRowTooltip = "Double-click to ping the object.";
        public const string TargetInfo = "{0} mesh(es)";
        public const string TargetInfoWithColliders = "{0} mesh(es) · {1} collider(s)";
        public const string StatusCreated = "+{0}";
        public const string StatusWarning = "Warning";
        public const string StatusSkipped = "Skipped";
        public const string StatusError = "Error";
        public const string StatusNoMesh = "No mesh";
        public const string SectionShape = "Shape";
        public const string SectionFilters = "Filters";
        public const string SectionOutput = "Output";
        public const string ResetGenerationTooltip = "Restore the generation settings to their defaults.";
        public const string LivePreview = "Live Preview";
        public const string LivePreviewTooltip = "Draw the colliders that would be generated in the Scene view, without changing anything.";
        public const string PreviewSummary = "Previewing {0} collider(s)";
        public const string PreviewOverBudget = "Preview paused: {0:N0} triangles exceeds the {1:N0} budget (Project Settings).";
        public const string GenerateButton = "Generate ({0})";
        public const string GenerateTooltip = "Generate colliders for every target. One Undo step reverts the whole batch.";
        public const string RemoveButton = "Remove Generated ({0})";
        public const string RemoveTooltip = "Remove colliders created by Auto Collider (locked colliders and your own colliders are kept).";
        public const string ResultDetails = "Details";

        public static string StrategyHint(ColliderStrategy strategy) => strategy switch
        {
            ColliderStrategy.BestPrimitive => "Picks the tightest of box, sphere and capsule for each mesh. Cheapest physics.",
            ColliderStrategy.Box => "One box per mesh, rotated to fit when rotation is allowed.",
            ColliderStrategy.Sphere => "The smallest sphere around each mesh.",
            ColliderStrategy.Capsule => "A capsule along each mesh's longest direction.",
            ColliderStrategy.ConvexHull => "A convex mesh (≤ 255 triangles) wrapped around each mesh. Works with moving Rigidbodies.",
            _ => string.Empty,
        };

        // Reports & notifications
        public const string NotifyNothingSelected = "Auto Collider: select objects in the Hierarchy first.";
        public const string NotifyRemoved = "Removed {0} collider(s) from {1} object(s)";
        public const string ReportCreated = "{0} collider(s) on {1} object(s)";
        public const string ReportSkipped = " · {0} skipped";
        public const string ReportWarnings = " · {0} with warnings";
        public const string ReportCanceled = " · canceled";
        public const string ReportTime = " · {0:0} ms";

        // Collider Group inspector
        public const string SummaryOne = "{0} collider";
        public const string SummaryMany = "{0} colliders";
        public const string SummaryEmpty = "No colliders yet — click Regenerate or use the Auto Collider window.";
        public const string CostLabel = "Physics cost {0}  {1}";
        public const string CostTooltip = "Rough relative cost: spheres and capsules are cheapest, then boxes, convex meshes, and concave meshes.";
        public const string CostLow = "Low";
        public const string CostMedium = "Medium";
        public const string CostHigh = "High";
        public const string Regenerate = "Regenerate";
        public const string RegenerateTooltip = "Rebuild generated colliders with the settings that created them (the recipe). Locked colliders are kept.";
        public const string EditInScene = "Edit in Scene";
        public const string EditInSceneTooltip = "In-scene collider editing arrives in milestone M4.";
        public const string OpenWindow = "Open Window";
        public const string OpenWindowTooltip = "Open the Auto Collider window.";
        public const string MultiGroupNote = "{0} groups selected: actions apply to all; the list shows the first.";
        public const string ColumnCollider = "Collider";
        public const string ColumnOn = "On";
        public const string ColumnTrigger = "Trig";
        public const string ColumnLock = "Lock";
        public const string ToggleEnabledTooltip = "Enable or disable this collider.";
        public const string ToggleTriggerTooltip = "Make this collider a trigger (not available for concave mesh colliders).";
        public const string ToggleLockTooltip = "Locked colliders survive Regenerate and Remove.";
        public const string Select = "Select";
        public const string SelectTooltip = "Select the GameObject that holds this collider.";
        public const string EntryGeneratedTooltip = "Generated by Auto Collider.";
        public const string EntryManualTooltip = "Added manually.";
        public const string MissingEntries = "{0} tracked collider(s) were deleted outside Auto Collider.";
        public const string CleanUp = "Clean Up";
        public const string CleanUpTooltip = "Forget colliders that no longer exist.";
        public const string SectionBulk = "Bulk Edit";
        public const string SectionRecipe = "Recipe";
        public const string RecipeSettings = "Settings";
        public const string BulkTriggerOn = "Make All Triggers";
        public const string BulkTriggerOff = "Make All Solid";
        public const string BulkTriggerTooltip = "Set Is Trigger on every collider in the group (concave mesh colliders are skipped).";
        public const string BulkLayer = "Layer";
        public const string BulkLayerTooltip = "Move the collider objects (not your meshes) to this layer.";
        public const string BulkMaterial = "Physics Material";
        public const string BulkMaterialTooltip = "Assign this physics material to every collider in the group.";
        public const string Apply = "Apply";

        // Collider kinds
        public const string KindBox = "Box";
        public const string KindSphere = "Sphere";
        public const string KindCapsule = "Capsule";
        public const string KindConvex = "Convex";
        public const string KindMesh = "Mesh";
    }
}
