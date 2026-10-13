# Auto Collider — Project Rules & Domain Skill

Unity editor plugin that **generates, optimizes, and edits colliders** for one or many objects.
Goal: beat the reference plugin (`Assets/Mystery Studio/Easy Collider`) and match the key features of
Easy Collider Editor, Technie Collider Creator 2, and Mesh Collider Optimizer.

- Architecture, competitor analysis, algorithms: **[Docs/ARCHITECTURE_PLAN.md](Docs/ARCHITECTURE_PLAN.md)**
- Milestone tracker (tasks, status, log): **[Docs/ROADMAP.md](Docs/ROADMAP.md)**

Before starting work, check the current milestone in `ROADMAP.md`. When finishing a task, tick it there
(only after compile + tests are green) and add a Log line when a milestone completes. Update
`ARCHITECTURE_PLAN.md` when the design changes.

---

## 1. Project facts

| Item | Value |
|---|---|
| Unity | **6000.6.3f1**, URP 17.6 |
| Distribution | **UPM package** at `Packages/com.fahril.autocollider/` |
| Runtime namespace / asmdef | `AutoCollider` / `AutoCollider.Runtime` |
| Editor namespace / asmdef | `AutoCollider.Editor` / `AutoCollider.Editor` |
| Tests asmdefs | `AutoCollider.Editor.Tests` (EditMode), `AutoCollider.Tests` (PlayMode, runtime components) |
| Math / jobs | `Unity.Mathematics` (an **engine module** in 6000.6 — no package or asmdef reference needed), `Unity.Collections` 6.6.0 + `Unity.Burst` 2.0.0 (built-in packages, declared in `package.json`, referenced by name in the Editor asmdef) |
| Settings | `AutoColliderSettings` (`ScriptableSingleton`) → `ProjectSettings/AutoColliderSettings.asset`; defaults = field initializers in `AutoColliderSettingsData` |
| UI | **UI Toolkit** (UXML + USS) for inspectors and windows, **Overlays** for scene toolbars, IMGUI `Handles` **only** for in-scene gizmos |
| Convex decomposition | **Own Burst C# implementation** — no native VHACD / DLLs |

### Directory map

```
Packages/com.fahril.autocollider/
  package.json
  Runtime/                     AutoCollider.Runtime  (NO UnityEditor)
    ColliderGroup.cs           multi-collider "bridge" (ownership, bulk API, events)
    ColliderEntry.cs           one tracked collider: a Collider OR a Collider2D (Is2D, Component, IsAlive, Enabled, IsTrigger)
    GenerationRecipe.cs, GenerationSettings.cs (+ strategy/granularity/replace/dimension/strategy2D/outline enums), PhysicsLimits.cs
    PaintedRegion.cs, PartShape.cs  painted parts stored in the recipe (ids ≥ 10000 = entry PartId)
    ColliderRelay.cs (+ ColliderStayRelay, TriggerPairs)  routes physics messages to the group (added at runtime on first event subscription)
    AnimatedMeshCollider.cs (+ RefreshSchedule)  runtime skinned/blendshape collider refresh, throttled
    ColliderLodFollower.cs (+ LodMath)  enables the active LOD level's colliders
  Editor/                      AutoCollider.Editor
    PackagePaths.cs            every hard-coded asset path (package root, USS, settings asset)
    AutoColliderSettings.cs    project settings singleton + AutoColliderSettingsData
    Core/                      MeshSource, MeshGeometry, TargetSpace, Fitting/ (box/sphere/capsule), QuickHull3D (pure math)
    Core2D/                    SpriteSource (sprite / flat-mesh outlines), OutlineTracer (Burst raster → loops), PolygonSimplifier,
                               Polygon2D, Outline2D, Box2DFitter, CircleFitter, Capsule2DFitter
    Optimization/              ClusterSimplifier (Burst, QEM), MeshCleaner, InflateDeflate, ErrorMetric (Burst), MeshOptimizer, ColliderOptimizer,
                               Voxelizer (Burst), ConvexDecomposer, PrimitiveReplacer, stats
    Generation/                settings, presets, strategies, pipeline, batch, writer, mesh store,
                               RegionFitter (painted region → shapes, shared by paint tool + pipeline), BoneFitter (per-bone)
    Generation2D/              FittedShape2D, Strategies2D (Auto 2D), GenerationPipeline2D (routing + fit), ColliderWriter2D,
                               RegionFitter2D (painted 2D regions), TilemapSetup (Composite / Baked tilemaps)
    Editing/                   ColliderEditTool (EditorTool), ColliderOverlay, ColliderOperations (one Undo step each),
                               ColliderShapes (collider ↔ shape), ColliderPicker, ColliderEditState, ColliderDrawing,
                               VertexSnapping, ColliderToolCommands (+ shortcut context), ShapeDrawing, PreviewDrawer, HeatmapDrawer,
                               PaintMesh (+ PaintMeshCache: world mesh, raycast, brush, grow), PaintDrawing (GL region tints),
                               ColliderShapes2D + ColliderOperations2D (2D counterparts; tool state is Component-based)
    UI/                        windows, inspectors, settings provider, Strings, AutoColliderStyles, Uss/
    Doctor/                    ColliderDoctor (scan + one-click fixes), DoctorIssue / DoctorCheck / DoctorSeverity
  Tests/Editor/                EditMode tests
  Tests/Runtime/               PlayMode tests (physics events, throttling, LOD)
  Samples~/Demo/               demo scene + meshes + sample scripts (registered in package.json; author it in a temp
                               Assets/ folder so Unity makes the .meta GUIDs, then move it here with every .meta)
  Documentation~/              user manual
Docs/ARCHITECTURE_PLAN.md      design (dev-facing)
Docs/ROADMAP.md                milestone tracker — tick tasks here
Assets/Mystery Studio/         THIRD-PARTY REFERENCE — READ ONLY
Assets/AutoCollider Dev/       dev test scenes for manual checks: M1 Test Scene (3D props), M8 2D Test Scene (+ 2D/ sprites)
Assets/Sample Model/           (empty) place real test models here
```

---

## 2. Hard rules (never break)

### Assemblies & builds
1. `Runtime/` **never** references `UnityEditor` (not even inside `#if UNITY_EDITOR` unless truly unavoidable). The reference plugin breaks player builds this way — we don't.
2. Everything heavy (fitting, simplification, decomposition) lives in **Editor**. Runtime only holds data + light components.
3. No new third-party or native dependencies without asking the user.

### Scene safety & Undo
4. **Every** scene/asset mutation goes through Undo:
   `Undo.RegisterCreatedObjectUndo`, `Undo.AddComponent`, `Undo.RecordObject`, `Undo.DestroyObjectImmediate`, `Undo.SetTransformParent`.
   One user action = **one Undo group** (`Undo.IncrementCurrentGroup` + `Undo.SetCurrentGroupName` + `Undo.CollapseUndoOperations`). Batch over 100 objects is still one Ctrl+Z.
   **Record right before you modify.** Any other Undo call (`AddComponent`, `RegisterCreatedObjectUndo`, `DestroyObjectImmediate`) flushes pending `RecordObject`s — a record taken *before* such a call captures "no change" and the later edit is silently lost on undo. Use `ColliderWriter.RecordGroup(group, …)` immediately before every `ColliderGroup` change, and change the group *before* destroying what it referenced.
   Tests that check undo must assert the restored state (entries, references) — after undoing a destroy, compare objects with Unity `==` (the C# wrapper is new), not `Is.SameAs`.
5. **Only delete colliders we own** (registered in `ColliderGroup` with `generated == true`). Never wipe user-authored colliders unless the user explicitly picks "Replace existing colliders". Delete a collider's **GameObject** only when `ColliderEntry.OwnsGameObject` is set (we created it — container children, bone children); remove through `ColliderWriter.DestroyEntryCollider`. Non-collider helpers we add (a tilemap composite's static `Rigidbody2D`) go in `ColliderGroup.OwnedComponents` and are removed with their colliders; a user's own components (e.g. their `TilemapCollider2D`) are never modified.
6. Never silently change a user's **tag, layer, static flags, or transform**. Layer/tag assignment is opt-in and applies to generated collider objects only.
7. Prefabs: modify instances with `PrefabUtility.RecordPrefabInstancePropertyModifications`; modify prefab assets with `PrefabUtility.LoadPrefabContents` → edit → `SaveAsPrefabAsset` → `UnloadPrefabContents`. Never edit a prefab asset through a scene instance by accident.
8. Mark scenes dirty only through Undo / `EditorSceneManager.MarkSceneDirty` — never leave changes unsaved-but-invisible.

### Memory & assets
9. Dispose every `NativeArray`/`NativeList` (`using` or `try/finally`). Jobs must be completed before disposal.
10. Temporary meshes (e.g. `SkinnedMeshRenderer.BakeMesh`) are **cached and destroyed** — never allocated per frame or per `OnDrawGizmos`/`OnSceneGUI` (the reference plugin leaks here).
11. Generated collider meshes are **saved as assets** via `GeneratedMeshStore` (sub-assets, one `.asset` per source model, deduped by content hash). Never leave large meshes serialized inline in scenes.
12. Do not require meshes to be Read/Write enabled in the editor; use `Mesh.AcquireReadOnlyMeshData` where possible. If runtime features need readability, warn in the UI with a one-click fix.

### Algorithms
13. Core/Optimization code is **pure**: input arrays in, result structs out. No `GameObject`, no `Selection`, no `EditorUtility` in that layer.
14. Deterministic: same input + settings → identical output (no unseeded randomness, stable sort orders).
15. Prefer `float3`/`float4x4` + Burst jobs for hot loops; keep a managed fallback path only if Burst can't express it.
16. Work in a well-defined space: fitters operate in the **collider-owner's local space**. Convert world↔local exactly once at the boundary (`MeshSource` / `ColliderWriter`).
17. Every new fitter / optimizer step ships with **EditMode tests** (see §5).

### UI
18. UI Toolkit only for Inspector + EditorWindow (trees built in C# — UXML optional — styled by USS). No IMGUI `OnInspectorGUI` code. Bind settings with `SerializedObject` + `PropertyField` so `[Tooltip]`/`[Range]` attributes drive the UI.
19. Every UI root calls `AutoColliderStyles.Apply(root)` (adds `AutoCollider.uss` + `ac-root` + `ac-root--dark/--light`). Use `--ac-*` tokens for plugin colors/spacing (defined per skin in `AutoCollider.uss`) and Unity theme variables (`var(--unity-colors-...)`) for neutrals. Class names use the `ac-` prefix, BEM-style (`ac-section__header`, `ac-button--primary`).
20. Every control has a **tooltip**. Every destructive button shows its effect count (e.g. "Remove 12 colliders").
21. Long operations show `EditorUtility.DisplayCancelableProgressBar` (or the `Progress` API) and are cancelable.
22. All user-facing strings live in one `Strings`/`Labels` static class (makes later localization possible).
23. Menu paths: `Tools/Auto Collider/...`, `GameObject/Auto Collider/...`. Shortcuts via `[Shortcut]` (rebindable), category `Auto Collider`.

---

## 3. Domain skill — PhysX / Unity collider knowledge

Use this when designing fitters, writers, and validators.

**Cost ranking (cheapest → most expensive):** Sphere < Capsule < Box < Convex MeshCollider < Concave MeshCollider. Prefer primitives whenever the fit error is acceptable.

**MeshCollider limits**
- Convex hull: **max 255 polygons** (PhysX). Our `QuickHull3D` and decomposition must enforce `≤ 255` faces (the "256 guard").
- Non-kinematic `Rigidbody` **requires convex** MeshColliders. Concave mesh only on static or kinematic objects.
- Concave MeshCollider triggers are not supported → use compound convex parts for triggers.
- `cookingOptions`: `CookForFasterSimulation`, `EnableMeshCleaning`, `WeldColocatedVertices`, `UseFastMidphase` — expose in settings, default to all on.
- Meshes with < 4 non-coplanar vertices can't make a hull → fall back to a thin box.

**Primitive colliders**
- `BoxCollider`/`CapsuleCollider`/`SphereCollider` have `center` but **no rotation**. A rotated primitive needs its **own child GameObject** whose `localRotation` carries the orientation.
- `CapsuleCollider.direction`: 0 = X, 1 = Y, 2 = Z. `height` includes both caps (`height >= 2 * radius`).
- `SphereCollider` uses the **max** lossy scale axis; `CapsuleCollider` radius uses the max of the two non-direction axes → non-uniform scale inflates them. Compensate during fitting or warn.
- Negative scale: primitives handle it; MeshColliders get re-cooked (cost) — flag it in the Doctor.

**Compound colliders & callbacks**
- All colliders under one `Rigidbody` form a compound body. `OnCollision*` / `OnTrigger*` go to the **Rigidbody's GameObject**; identify the part via `Collision.thisCollider` (works on Exit too, which has no contacts). Child trigger messages also reach the Rigidbody's object, and `OnTrigger*` doesn't say which of several colliders on one object was involved (see `TriggerPairs`).
- Without a Rigidbody, callbacks go to the **collider's own GameObject** → `ColliderRelay` forwards them to `ColliderGroup`.
- Child GameObjects holding colliders must keep the root's layer unless the user overrides; `includeLayers`/`excludeLayers` and `layerOverridePriority` are per collider.

**Skinned meshes**
- `BakeMesh` gives the current pose (accurate, expensive). `sharedMesh` gives bind pose (cheap, wrong when animated).
- Per-bone colliders: assign vertices to the bone with the highest weight, fit in bone-local space, parent collider GO to the bone → follows animation for free.
  Bone-local bind pose = `mesh.bindposes[b].MultiplyPoint(sharedMesh vertex)` — no bake needed. Such shapes carry `FittedShape.Parent` (parent-local, not scaled space).
- Painted triangle indices refer to the triangles `MeshSource` reads (unreferenced vertices dropped, submeshes concatenated) — read through `MeshSource`, never `mesh.triangles`, wherever indices are stored or matched.
- Runtime MeshCollider refresh is expensive; throttle (`EveryNFrames` / `TargetHz`), only when visible, never with convex decomposition.

**2D physics (Box2D)**
- A GameObject can't hold both a 3D `Collider` and a `Collider2D` (Unity refuses the second kind) → 2D colliders go on `[Colliders 2D]` (`ColliderGroup.Container2D`), never on the 3D container.
- 2D colliders live in their object's **XY plane**; X/Y rotation of the object is ignored by 2D physics. Rotated 2D primitives need their own child with a **Z** rotation (only under equal, positive X/Y scale).
- `CircleCollider2D` scales by max(|sx|, |sy|); `CapsuleCollider2D` / `BoxCollider2D` by each axis; polygon / edge points scale exactly.
- `PolygonCollider2D` takes several paths (even-odd: inner paths are holes) and decomposes concave shapes itself; `EdgeCollider2D` is hollow (border only). Closed edge loops repeat the first point.
- Unity's **generated** sprite physics shape and tight sprite mesh are coarse enclosing polygons (64 px disc → 9 points, +16 % radius): trace the pixels instead (RenderTexture blit — works without Read/Write). Custom physics shapes are only used when chosen explicitly.
- Concave mesh-collider-style limits don't exist in 2D (any 2D collider works on a dynamic `Rigidbody2D`); a 2D collider under a 3D `Rigidbody` (or the reverse) never collides — different physics worlds.
- Pixel staircases: enclosing fits see stair corners ~1 px outside the true curve — allow a pixel of slack (capsule caps) or the shape stretches.
- Edit-tool state, picking and operations take `Component` (3D or 2D). Keep the typed 3D overloads (`IEnumerable<Collider>`) — tests and the 3D code call them — and route mixed selections through the `IEnumerable<Component>` overloads (one Undo group for both parts).
- Tilemaps: `CompositeCollider2D` requires a `Rigidbody2D` — add the (static) body first, explicitly, so it is Undo-tracked and can be owned. Unity's sprite meshes are too coarse to paint or trace accurately — paint on the alpha-cell grid.

**LODs**
- Generate from **LOD0** by default. Optional `ColliderLodFollower` enables only the colliders for the active LOD.

**Fit quality metrics**
- *Fill ratio* = mesh (or hull) volume / primitive volume. High fill ratio → primitive is a good fit.
- *Error* = sampled distance between source surface and collider surface (one-sided + symmetric). Drives heatmap and the Auto strategy.

---

## 4. Code style

- C# standard conventions: `PascalCase` types/methods/properties/public fields, `_camelCase` private fields, `camelCase` locals/params, `k_` not used — use `const` PascalCase.
- One public type per file; file name = type name.
- `[SerializeField] private` over public fields on components; public API through properties/methods.
- XML `///` docs on all public API in Runtime and on public entry points in Editor.
- Comments explain **why**, not what. No commented-out code.
- No LINQ in hot paths or per-frame code; fine in one-shot editor UI code.
- Keep methods short; strategies/fitters behind interfaces (`IColliderStrategy`, `IShapeFitter`).
- Nullable reference handling: check `UnityEngine.Object` with `== null` / `!obj`, never `?.` / `??` on Unity objects.
- Namespace gotcha: inside `namespace AutoCollider.Editor`, the bare name `Editor` resolves to our namespace — write `UnityEditor.Editor` for custom inspectors.
- Unity 6.6 gotchas: `Object.GetInstanceID()` is obsolete-as-error → use `GetEntityId()`; pick icons with `AssetPreview.GetMiniTypeThumbnail(type)` (no "missing icon" console warnings, unlike `EditorGUIUtility.IconContent`).
- Hierarchy (`GameObject/…`) context menu items run once **per selected object** — guard with the "first invocation only" check (see `GenerateCommands`).
- Component `EditorTool`s are found through the inspector's active editors, which can lag a selection change: `ToolManager.SetActiveTool` may throw — rebuild `ActiveEditorTracker.sharedTracker` and retry (see `ColliderToolCommands.ActivateTool`). Overlays default to hidden: use `defaultDisplay = true` and/or set `displayed`.
- Tool-only shortcuts use an `IShortcutContext` registered in `OnActivated` (they override global keys like Ctrl+D / Delete only while the tool is active). Never change Unity's `Selection` from inside the tool — the tool's targets are the selected groups.
- Editor-only internals are `internal`; tests reach them via `InternalsVisibleTo` (see each assembly's `AssemblyInfo.cs`).

---

## 5. Workflow & verification

1. Read the current milestone in `Docs/ROADMAP.md` before coding. Work one milestone slice at a time; tick tasks there only when compile + tests are green.
2. After code changes, verify through **Unity-MCP**:
   - `refresh_unity` (compile) → `read_console` → **zero errors, zero new warnings** (the MCP bridge's own "WebSocket" reconnect warnings during domain reload are not ours).
   - `run_tests` (EditMode, assembly `AutoCollider.Editor.Tests`) → all green; when `Runtime/` changed, also `run_tests` (PlayMode, assembly `AutoCollider.Tests`, `init_timeout` 120000).
   - Runtime components without `[ExecuteAlways]` get no `OnEnable` / `OnDestroy` / `LateUpdate` in edit mode — don't rely on them in EditMode tests (test lifecycle in PlayMode).
   - For UI/scene features, exercise them in `Assets/AutoCollider Dev/M1 Test Scene.unity` (primitive props: table, tilted chair, squashed / scaled objects; dense `Rock` mesh with and without a MeshCollider for optimization; concave `Arch (static)` and `Bracket (Rigidbody)` for Auto / decomposition) via `execute_code` / `manage_scene`, and screenshot with `manage_camera` (`capture_source: scene_view`). Leave the scene clean (no generated colliders, original meshes on colliders), delete generated test meshes (`Assets/AutoCollider/`), and reopen the user's scene afterwards.
   - `execute_code` compiles as **C# 6** (CodeDom): no local functions / pattern matching — use `System.Func` lambdas. Set `Selection` in one call and drive the UI in the next (selection events fire between calls). Find UI controls by label/class, never "first FloatField" — bound PropertyFields contain FloatFields too and you would edit the user's settings.
3. Required tests per area:
   - Fitters: cube → exact box; UV sphere → sphere radius within 1%; cylinder mesh → capsule axis correct; rotated cube → OBB recovers rotation.
   - QuickHull: face count ≤ 255, all input points inside/on hull.
   - Simplifier: bounds preserved within tolerance, no degenerate triangles, deterministic output.
   - Decomposition: every part convex & ≤ 255 faces, union covers source within tolerance.
   - Writer/Batch: one Undo group restores scene exactly; user colliders untouched.
4. Scene tests: EditMode tests run in **whatever scene is open**, and the runner reverts Undo afterwards — an Undo-destroyed component whose GameObject TearDown then `DestroyImmediate`d comes back (in memory, in the user's scene). Every scene test uses `TestScene` (`new TestScene()` in SetUp, `Cleanup(folder)` in TearDown): it reverts the test's own Undo groups first, then destroys what the test created and deletes its temp asset folder. Don't create additive scenes. After a run, check the open scene has no extra roots.
5. Every UI control needs a tooltip — `UiTooltipTests` walks all tabs, inspectors, the settings page and the overlay and fails otherwise. New UI surfaces must be added there.
6. Unity-MCP `execute_code` blocks `AssetDatabase.DeleteAsset` unless `safety_checks: false`; only disable it for paths you created.
7. Never mark work done with compile errors or failing tests — report them.
8. Don't modify `Assets/Mystery Studio/` (third-party reference, not committed) or `Library/`, `Temp/`, `*.csproj`.
9. Commits follow §6.

---

## 6. Commit rules

The user ships one commit per finished milestone and owns the history.

1. **When:** after a milestone is done (or when the user asks) — never mid-milestone, never with failing tests.
2. **Pre-commit checklist** (all must be true):
   - Compiles with zero errors / zero new warnings; all EditMode tests green (state the count in the message).
   - `Docs/ROADMAP.md`: milestone tasks ticked, status ✅, *Done when* verified, Log line added.
   - `Docs/ARCHITECTURE_PLAN.md` and this file updated if the design / rules changed.
   - Package version bumped — one **minor** per milestone (M1 = 0.2.0, M2 = 0.3.0, …) — and `CHANGELOG.md` + package `README.md` updated.
   - Dev scene saved clean; generated test assets (`Assets/AutoCollider/`, `Assets/__AutoCollider*`) deleted.
3. **Authorship:** the user's git identity only. **Never** add `Co-Authored-By`, "Generated with Claude Code", or any AI attribution — not in the subject, body or trailers.
4. **Stage explicitly by path — never `git add -A` / `git add .`:**
   - Include: `Packages/com.fahril.autocollider/`, `Docs/`, `CLAUDE.md`, `Assets/AutoCollider Dev/` (+ `.meta`).
   - Exclude: the user's own local edits (`Packages/manifest.json`, `Packages/packages-lock.json`, `Assets/Settings/*`, `ProjectSettings/QualitySettings.asset`), third-party `Assets/Mystery Studio/`, Unity-generated `ProjectSettings/SceneTemplateSettings.json`, `Temp/`, `Library/`, `UserSettings/`. Run `git diff --cached --stat` and check before committing.
5. **Message format:**
   ```
   <Imperative summary of the milestone> (package X.Y.Z)        ← ≤ 72 chars

   - What was added / changed, one bullet per feature area
   - Notable fixes or behavior changes
   - N EditMode tests
   ```
   No trailers. Pass the message with a heredoc (`git commit -F -`) so formatting survives.
6. **Push:** `git push origin main` right after the commit. Never force-push, never amend or rewrite a pushed commit — fix forward with a new commit.
7. **Verify:** `git log -1 --format='%an%n%B'` shows the user as author and no attribution lines.
