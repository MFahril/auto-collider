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
        public const string SectionDoctorHierarchy = "Doctor & Hierarchy";
        public const string HierarchyBadgeTooltip = "{0} collider(s) in this Collider Group";
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
        public const string WarningDynamicConcave = "Moving Rigidbodies can't use concave mesh colliders — used a convex hull instead.";
        public const string ProblemHullFailed = "Mesh is flat or too small for a convex hull.";

        // Statistics
        public const string StatsLine = "Triangles {0:N0} → {1:N0} (−{2:0}%) · max error {3}";
        public const string MemoryLine = "Collision memory ≈ {0} → {1}";
        public const string StatsParts = " · {0} parts";

        // Convex decomposition & Auto
        public const string SectionDecomposition = "Convex Decomposition & Auto";
        public const string DecompositionHint = "Used by Convex Decomposition and Auto. Parts that fill a primitive well become boxes, spheres or capsules (cheaper).";
        public const string StrategyConvexHull = "Convex Hull";
        public const string StrategyConvexDecomposition = "Convex Decomposition";
        public const string StrategyOptimizedMesh = "Optimized Mesh";
        public const string NoteAutoChose = "Auto → {0}.";
        public const string NoteComponentsExceedMaxParts = "{0} separate pieces need at least {0} parts (Max Parts is {1}).";

        // Presets
        public const string PresetLabel = "Preset";
        public const string PresetButton = "Presets ▾";
        public const string PresetButtonTooltip = "Apply a built-in or project preset, or save the current settings as a preset asset.";
        public const string PresetCurrentTooltip = "Built-in presets set mesh quality, shape fidelity and the triangle limit.";
        public const string PresetCustom = "Custom";
        public const string PresetHighQuality = "High Quality";
        public const string PresetHighQualityInfo = "Detailed, accurate meshes";
        public const string PresetBalanced = "Balanced";
        public const string PresetBalancedInfo = "Good default";
        public const string PresetPerformance = "Performance";
        public const string PresetPerformanceInfo = "≤ 2,000 triangles";
        public const string PresetMobile = "Mobile";
        public const string PresetMobileInfo = "≤ 500 triangles";
        public const string PresetNoAssets = "Project Presets/(none yet)";
        public const string PresetProjectPrefix = "Project Presets/";
        public const string PresetSaveAs = "Save Current as Preset…";
        public const string PresetSaveTitle = "Save Collider Preset";
        public const string PresetSaveMessage = "Save the current Auto Collider settings as a preset asset.";

        // Mesh optimization
        public const string SectionMeshOptimization = "Mesh Optimization";
        public const string MeshOptimizationHint = "Used by Optimized Mesh (and the Optimize tab). Lower quality = fewer triangles; higher fidelity keeps edges.";

        // Optimize tab
        public const string OptimizeHeader = "Mesh Colliders ({0})";
        public const string ScopeSelection = "Selection";
        public const string ScopeScene = "Whole Scene";
        public const string ScopeTooltip = "Optimize mesh colliders under the selected objects, or in every open scene.";
        public const string OptimizeEmpty = "Select objects that have Mesh Colliders (children are included).";
        public const string OptimizeEmptyScene = "No Mesh Colliders in the open scenes.";
        public const string OptimizeHintAnalyze = "Click Analyze to preview savings and accuracy before changing anything.";
        public const string Analyze = "Analyze";
        public const string AnalyzeTooltip = "Compute optimized meshes and statistics without changing the scene.";
        public const string OptimizeButton = "Optimize ({0})";
        public const string OptimizeTooltip = "Replace each collider's mesh with its optimized version (saved as an asset). One Undo step; originals can be restored.";
        public const string RestoreButton = "Restore Originals ({0})";
        public const string RestoreTooltip = "Put the original meshes back on colliders optimized by Auto Collider.";
        public const string OptimizeDone = "Optimized {0} collider(s).";
        public const string RestoreDone = "Restored {0} original mesh(es).";
        public const string ProgressAnalyzeTitle = "Auto Collider — Analyzing";
        public const string RowTriangles = "{0} · {1:N0} tris";
        public const string RowBeforeAfter = "{0} · {1:N0} → {2:N0} · ±{3}";
        public const string RowSavings = "−{0:0}%";
        public const string RowAlreadyOptimal = "Optimal";
        public const string RowOptimized = "Optimized";
        public const string RowOptimizedTooltip = "Uses an optimized mesh made by Auto Collider (the original can be restored).";
        public const string RowStatsTooltip = "Max error {0} · mean {1}\nMemory ≈ {2} → {3}\nTime {4:0} ms";
        public const string Heatmap = "Error Heatmap";
        public const string HeatmapTooltip = "Show how far the optimized collider is from the original surface: green = close, red = at or above the threshold.";
        public const string HeatmapThreshold = "Red at (m)";
        public const string HeatmapThresholdTooltip = "Distance (meters) shown as full red in the heatmap.";
        public const string HeatmapLines = "Lines";
        public const string HeatmapLinesTooltip = "Draw lines from the original surface to the collider where the error is noticeable.";

        // Undo (optimize)
        public const string UndoOptimize = "Auto Collider: Optimize Collider";
        public const string UndoOptimizeCount = "Auto Collider: Optimize Colliders ({0})";
        public const string UndoRestore = "Auto Collider: Restore Original Meshes";

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
        public const string MenuRunDoctor = MenuRoot + "Run Doctor";

        // Doctor
        public const string DoctorIntro = "Finds collider setups that break or slow down physics — and fixes them in one click (each fix can be undone).";
        public const string DoctorScopeTooltip = "Check the selected objects (and their children), or every open scene.";
        public const string DoctorScan = "Scan";
        public const string DoctorScanTooltip = "Look for collider problems now.";
        public const string DoctorCheckMissing = "Report meshes without colliders";
        public const string DoctorCheckMissingTooltip = "Also list visible meshes that have no collider at all (decor often doesn't need one, so this is off by default).";
        public const string DoctorNotScanned = "Press Scan to check your colliders.";
        public const string DoctorHealthy = "✓ No problems found ({0} objects checked).";
        public const string DoctorSummary = "{0} errors · {1} warnings · {2} info";
        public const string DoctorFixAll = "Fix All ({0})";
        public const string DoctorFixAllTooltip = "Apply every available fix as one Undo step. Issues without a fix are left for you.";
        public const string DoctorSelectTooltip = "Select and ping this object.";
        public const string DoctorMissingObject = "(deleted)";
        public const string UndoDoctorFix = "Auto Collider: Doctor Fix";
        public const string UndoDoctorFixAll = "Auto Collider: Doctor Fix All";

        public const string DoctorMissingMesh = "Mesh Collider has no mesh — it collides with nothing.";
        public const string DoctorConcaveMoving = "Concave Mesh Collider on a moving Rigidbody — PhysX doesn't support this; the collider is ignored or errors at runtime.";
        public const string DoctorConcaveTrigger = "Concave Mesh Colliders can't be triggers — the trigger never fires.";
        public const string DoctorDetailedMesh = "Mesh collider with {0:N0} triangles (budget {1:N0}) — slow to cook and to collide.";
        public const string DoctorComplexConvex = "Convex mesh with {0:N0} triangles — PhysX reduces it to 255 polygons when cooking (slow, unpredictable shape).";
        public const string DoctorNegativeScale = "Negative scale: the mesh is re-cooked mirrored when loaded, which costs time. Prefer a mirrored mesh asset.";
        public const string DoctorScaledPrimitive = "Non-uniform scale ({0:0.0}×): Unity sizes this collider by the largest axis, so it is bigger than the mesh.";
        public const string DoctorDuplicate = "Identical collider on the same object — doubles the physics cost for nothing.";
        public const string DoctorMissingEntries = "{0} tracked collider(s) were deleted outside Auto Collider.";
        public const string DoctorNoCollider = "Visible mesh without any collider.";

        public const string DoctorFixConvex = "Make Convex";
        public const string DoctorFixConvexTooltip = "Set Convex on the Mesh Collider (works with moving Rigidbodies and triggers; loses concave detail — use Convex Decomposition for concave shapes).";
        public const string DoctorFixAssignMesh = "Assign Mesh";
        public const string DoctorFixAssignMeshTooltip = "Use the object's own (Mesh Filter) mesh.";
        public const string DoctorFixRemove = "Remove";
        public const string DoctorFixRemoveTooltip = "Delete this collider.";
        public const string DoctorFixOptimize = "Optimize";
        public const string DoctorFixOptimizeTooltip = "Replace the mesh with a simplified copy (Optimize tab settings; the original can be restored).";
        public const string DoctorFixHullTooltip = "Replace the mesh with a convex hull of at most 255 triangles (the original can be restored).";
        public const string DoctorFixRemoveDuplicate = "Remove Duplicate";
        public const string DoctorFixRemoveDuplicateTooltip = "Delete this second, identical collider.";
        public const string DoctorFixGenerate = "Generate";
        public const string DoctorFixGenerateTooltip = "Generate colliders for this object with the window's current settings.";

        public static string SeverityName(DoctorSeverity severity) => severity switch
        {
            DoctorSeverity.Error => "Error",
            DoctorSeverity.Warning => "Warning",
            _ => "Info",
        };

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
            ColliderStrategy.OptimizedMesh => "A simplified copy of the mesh (keeps holes and concave shapes). Static or kinematic objects only — see Mesh Optimization.",
            ColliderStrategy.ConvexDecomposition => "Splits concave meshes into several convex parts (primitives where they fit). For moving Rigidbodies — see Convex Decomposition.",
            ColliderStrategy.Auto => "Recommended. Per mesh: a primitive if one fits, else a convex hull, else convex parts (moving) or an optimized mesh (static). Skinned meshes get per-bone colliders.",
            ColliderStrategy.PerBone => "Skinned meshes: one collider per bone, parented to the bone so it follows the animation. Other meshes use Auto.",
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
        public const string EditInSceneTooltip = "Edit these colliders in the Scene view: select, resize, move, rotate, mirror, merge, convert…";
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
        public const string SelectTooltip = "Select this collider in the Scene view (opens the collider tool) and frame it.";
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

        // Collider edit tool
        public const string ToolName = "Edit Colliders (Auto Collider)";
        public const string ToolTooltip = "Edit Colliders — select, resize, move, rotate, mirror, merge and convert colliders in the Scene view.";
        public const string ToolActivatedHint = "Click a collider · Shift add · Ctrl toggle · drag to box-select";
        public const string ToolNothingSelected = "No collider selected";
        public const string ToolSelectionSummary = "{0} selected · {1}";
        public const string ToolPickHint = "Click a collider (click again to cycle through overlaps). Shift adds, Ctrl toggles, drag to box-select. Esc exits.";
        public const string ToolEditHint = "Drag the handles. Resize: Alt = symmetric. Hold V to snap to mesh vertices. Ctrl+D duplicate, Del delete, F frame.";
        public const string ToolMeshResizeHint = "Mesh colliders can't be resized — use Move / Rotate, Convert, or Refit.";
        public const string ToolExit = "Exit Tool";
        public const string ToolExitTooltip = "Leave the collider tool (Esc).";
        public const string ModeSelect = "Select";
        public const string ModeSelectTooltip = "Select colliders only, no handles (1).";
        public const string ModeEdit = "Edit";
        public const string ModeEditTooltip = "Select and edit with handles (2).";
        public const string ModePaint = "Paint";
        public const string ModePaintTooltip = "Paint triangles on the mesh, then create colliders from them (3).";
        public const string HandleResize = "Resize";
        public const string HandleResizeTooltip = "Drag faces / radius / height (R). Alt = symmetric, hold V = snap face to a vertex.";
        public const string HandleMove = "Move";
        public const string HandleMoveTooltip = "Move the selected collider (W). Hold V to snap to a vertex.";
        public const string HandleRotate = "Rotate";
        public const string HandleRotateTooltip = "Rotate the selected collider (E). Needs uniform object scale.";
        public const string RenameTooltip = "Rename the selected collider (press Enter).";
        public const string ActionDuplicate = "Duplicate";
        public const string ActionDuplicateTooltip = "Duplicate the selected colliders (Ctrl+D).";
        public const string ActionDelete = "Delete";
        public const string ActionDeleteTooltip = "Delete the selected colliders (Del).";
        public const string ActionLock = "Lock";
        public const string ActionUnlock = "Unlock";
        public const string ActionLockTooltip = "Locked colliders survive Regenerate and Remove (L).";
        public const string CaptionMirror = "Mirror";
        public const string ActionMirrorTooltip = "Mirrored copy across the object's own axis plane through its pivot (M = X). Left/Right names are swapped.";
        public const string CaptionConvert = "Convert";
        public const string ActionConvertTooltip = "Replace with this shape, fitted around the current collider.";
        public const string CaptionMerge = "Merge";
        public const string ActionMergeTooltip = "Replace the selected colliders with one shape around all of them (G = convex).";
        public const string ActionRefit = "Refit";
        public const string ActionRefitTooltip = "Shrink-wrap: refit to the mesh vertices inside each collider (same shape and orientation).";
        public const string InflateStepTooltip = "Inflate / deflate step in meters.";
        public const string ActionInflateTooltip = "Grow the selected colliders by the step.";
        public const string ActionDeflateTooltip = "Shrink the selected colliders by the step.";
        public const string ActionSolo = "Solo";
        public const string ActionUnsolo = "Unsolo";
        public const string ActionSoloTooltip = "Show only the selected colliders (H). Shift+H hides the selection.";
        public const string ActionShowAll = "Show All";
        public const string ActionShowAllTooltip = "Show every collider again (Alt+H).";
        public const string ToggleXray = "X-Ray";
        public const string ToggleXrayTooltip = "Draw colliders through geometry, faded (X).";
        public const string ToggleLabels = "Labels";
        public const string ToggleLabelsTooltip = "Show every collider's name (selected ones always show).";
        public const string MergedLabel = "Merged";
        public const string NotifyDuplicated = "Duplicated {0} collider(s)";
        public const string NotifyDeleted = "Deleted {0} collider(s)";
        public const string NotifyMirrored = "Mirrored {0} collider(s)";
        public const string NotifyMerged = "Merged into {0} collider(s)";
        public const string NotifyConverted = "Converted {0} collider(s)";
        public const string NotifyRefit = "Refit {0} of {1} collider(s)";
        public const string NotifyLocked = "Locked";
        public const string NotifyUnlocked = "Unlocked";
        public const string NotifyRotateNeedsUniformScale = "Rotating colliders needs uniform object scale";
        public const string UndoResize = "Auto Collider: Resize Collider";
        public const string UndoMove = "Auto Collider: Move Collider";
        public const string UndoRotate = "Auto Collider: Rotate Collider";
        public const string UndoDuplicate = "Auto Collider: Duplicate Colliders";
        public const string UndoDelete = "Auto Collider: Delete Colliders";
        public const string UndoMirror = "Auto Collider: Mirror Colliders";
        public const string UndoMerge = "Auto Collider: Merge Colliders";
        public const string UndoConvert = "Auto Collider: Convert Colliders";
        public const string UndoRefit = "Auto Collider: Refit Colliders";
        public const string UndoInflate = "Auto Collider: Inflate Colliders";
        public const string UndoLock = "Auto Collider: Lock Colliders";
        public const string UndoRename = "Auto Collider: Rename Collider";

        // Paint mode & regions
        public const string PaintSummary = "{0} triangles painted";
        public const string PaintHint = "Drag on the mesh to paint, Ctrl+drag to erase. [ / ] change the brush, Esc clears.";
        public const string PaintBrush = "Brush";
        public const string PaintBrushTooltip = "Brush radius in screen points ([ and ] while painting).";
        public const string PaintAll = "All";
        public const string PaintAllTooltip = "Paint every triangle of the selected objects' meshes.";
        public const string PaintConnected = "Connected";
        public const string PaintConnectedTooltip = "Extend the paint to the whole connected pieces it touches.";
        public const string PaintGrow = "Grow";
        public const string PaintGrowTooltip = "Extend the paint by one ring of neighbouring triangles.";
        public const string PaintClear = "Clear";
        public const string PaintClearTooltip = "Clear the current paint (Esc).";
        public const string CaptionCreate = "Create";
        public const string CreateRegionTooltip = "Make a collider from the painted triangles. It is saved as a painted region, so Regenerate rebuilds it exactly.";
        public const string RegionsHeader = "Painted regions";
        public const string RegionsNone = "None yet — paint triangles, then click a Create shape.";
        public const string RegionsRegenerateNote = "With painted regions, Generate / Regenerate rebuild only the regions (and keep locked colliders).";
        public const string RegionRow = "{0} · {1} · {2} tris";
        public const string RegionPaint = "Paint";
        public const string RegionPaintTooltip = "Load this region's triangles into the paint (to check it or make a new region from it).";
        public const string RegionDeleteTooltip = "Delete this region and its colliders.";
        public const string RegionLabel = "Region {0}";
        public const string NotifyNothingPainted = "Paint some triangles first";
        public const string NotifyRegionCreated = "Created {0} collider(s) from paint";
        public const string NoteRegions = "Built from {0} painted region(s).";
        public const string NoteBones = "{0}: {1} bone collider(s).";
        public const string WarningRegionRendererMissing = "Painted region \"{0}\": its renderer no longer exists.";
        public const string WarningRegionTrianglesChanged = "Painted region \"{0}\": {1} triangle(s) no longer exist (the mesh changed) — repaint it.";
        public const string UndoCreateRegion = "Auto Collider: Create Painted Region";
        public const string UndoDeleteRegion = "Auto Collider: Delete Painted Region";
        public const string UndoAddGroup = "Auto Collider: Add Collider Group";
        public const string ClearRegions = "Clear Regions ({0})";
        public const string ClearRegionsTooltip = "Delete every painted region of this group and the colliders built from them.";

        public static string PartShapeName(PartShape shape) => shape switch
        {
            PartShape.Box => KindBox,
            PartShape.Sphere => KindSphere,
            PartShape.Capsule => KindCapsule,
            PartShape.ConvexHull => KindConvex,
            PartShape.ConvexParts => "Parts",
            _ => "Auto",
        };

        // Skinned meshes
        public const string SectionSkinned = "Skinned Meshes";
        public const string SkinnedHint = "Used by Per Bone and Auto: one collider per bone, parented to the bone so it follows the animation.";

        // Edit tab
        public const string EditTabIntro = "Edit generated colliders directly in the Scene view. Select an object with a Collider Group, then enter the tool.";
        public const string EditTabEnter = "Enter Collider Tool";
        public const string EditTabEnterTooltip = "Activate the collider edit tool for the selected Collider Groups.";
        public const string EditTabPaint = "Paint Colliders";
        public const string EditTabPaintTooltip = "Paint triangles on the selected objects and turn them into colliders (adds a Collider Group if needed).";
        public const string EditTabNoGroup = "Select an object that has a Collider Group (generate colliders first).";
        public const string EditTabGroups = "{0} Collider Group(s) selected · {1} colliders";
        public const string EditTabShortcuts = "Shortcuts";
        public const string EditTabShortcutList =
            "1 / 2  Select / Edit mode\nW / E / R  Move / Rotate / Resize\nV (hold)  Snap to vertex\nCtrl+D  Duplicate   Del  Delete\nM  Mirror X   G  Merge (convex)\nL  Lock   H  Solo   Shift+H Hide   Alt+H Show all\nX  X-ray   F  Frame   Esc  Clear / exit\nRebind in Edit ▸ Shortcuts ▸ Auto Collider.";

        // Runtime bridge (M6)
        public const string SectionRuntime = "Runtime";
        public const string RuntimeEventsHint =
            "Game code can listen to every collider of this group at once:\n" +
            "group.CollisionEnter += (entry, collision) => …\n" +
            "group.TriggerEnter += (entry, other) => …\n" +
            "The entry tells you which part was hit (entry.Label). Works with or without a Rigidbody; relays are added automatically in play mode.";
        public const string AddLodFollower = "Add LOD Follower";
        public const string AddLodFollowerTooltip = "Add a Collider LOD Follower: at runtime only the colliders generated for the active LOD level are enabled (generate with Per LOD Colliders).";
        public const string UndoAddLodFollower = "Auto Collider: Add LOD Follower";
        public const string EntryKindLod = "{0} · LOD{1}";
        public const string BulkIncludeLayers = "Include Layers";
        public const string BulkIncludeLayersTooltip = "Extra layers these colliders collide with, on top of the Layer Collision Matrix.";
        public const string BulkExcludeLayers = "Exclude Layers";
        public const string BulkExcludeLayersTooltip = "Layers these colliders ignore, on top of the Layer Collision Matrix.";
        public const string BulkLayerPriority = "Override Priority";
        public const string BulkLayerPriorityTooltip = "When two colliders' include / exclude layers disagree, the higher priority wins.";
        public const string BulkLayerOverridesTooltip = "Apply include / exclude layers and priority to every collider in the group.";
        public const string BulkContactsOn = "Provide Contacts";
        public const string BulkContactsOff = "No Contacts";
        public const string BulkContactsTooltip = "Report (or stop reporting) contacts of every collider to Physics.ContactEvent.";

        // Animated mesh collider
        public const string AddAnimatedCollider = "Add Animated Mesh Collider";
        public const string UndoAddAnimatedCollider = "Auto Collider: Add Animated Mesh Collider";
        public const string NotifyAnimatedAdded = "Animated Mesh Collider added to {0} object(s).";
        public const string NotifyNoSkinned = "Select objects with a Skinned Mesh Renderer.";
        public const string AnimatedMissing = "Assign a Skinned Mesh Renderer (Source) and a Mesh Collider (Target).";
        public const string AnimatedConcaveDynamic = "The target is a concave Mesh Collider under a moving (non-kinematic) Rigidbody — PhysX doesn't allow that. Make it convex or the Rigidbody kinematic.";
        public const string AnimatedMakeConvex = "Make Convex";
        public const string AnimatedMakeConvexTooltip = "Set Convex on the target Mesh Collider (cheaper to cook, works with moving Rigidbodies, loses concave detail).";
        public const string AnimatedNotReadable = "Shared Mesh mode assigns the mesh at runtime, which needs Read/Write enabled on '{0}'.";
        public const string AnimatedEnableReadWrite = "Enable Read/Write";
        public const string AnimatedEnableReadWriteTooltip = "Turn on Read/Write in the model's import settings and reimport it.";
        public const string AnimatedCost = "≈ {0:N0} triangles re-cooked {1} — keep this low; per-bone primitive colliders are usually far cheaper.";
        public const string AnimatedCostPerFrame = "every frame";
        public const string AnimatedCostPerNFrames = "every {0} frames";
        public const string AnimatedCostHz = "{0:0.#} times per second";
        public const string AnimatedSharedHint = "Shared Mesh: the collider uses the bind pose and is only refreshed when the mesh is swapped (no per-frame cost).";

        // 2D (M8)
        public const string Section2D = "2D (Sprites & Flat Meshes)";
        public const string Hint2D = "Sprites get 2D colliders; meshes too when a Rigidbody2D moves them or Dimension is 2D. Granularity, Allow Rotation, Inflate, Quality (outline detail), Min Island Size and the Auto thresholds apply to 2D as well.";
        public const string WarningNoPhysicsShape = "Sprite '{0}' has no physics shape (enable Generate Physics Shape in its import settings) — its mesh outline was used.";
        public const string WarningAlphaUnavailable = "Sprite '{0}' is packed tightly in an atlas, so its pixels can't be traced — its mesh outline was used.";
        public const string WarningSpritesSkipped3D = "{0} sprite(s) skipped: Dimension is 3D (sprites need 2D colliders).";
        public const string BulkMaterial2D = "Physics Material 2D";
        public const string BulkMaterial2DTooltip = "Assign this 2D physics material to every 2D collider in the group.";
        public const string KindCircle = "Circle";
        public const string KindPolygon = "Polygon";
        public const string KindConvexPolygon = "Convex Polygon";
        public const string KindEdge = "Edge";
        public const string KindBox2D = "Box 2D";
        public const string KindCircle2D = "Circle 2D";
        public const string KindCapsule2D = "Capsule 2D";
        public const string KindPolygon2D = "Polygon 2D";
        public const string KindEdge2D = "Edge 2D";

        // Collider kinds
        public const string KindBox = "Box";
        public const string KindSphere = "Sphere";
        public const string KindCapsule = "Capsule";
        public const string KindConvex = "Convex";
        public const string KindMesh = "Mesh";
    }
}
