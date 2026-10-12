# Auto Collider — Roadmap & Milestone Tracker

Live progress tracker. Design details live in [`ARCHITECTURE_PLAN.md`](ARCHITECTURE_PLAN.md); rules in [`../CLAUDE.md`](../CLAUDE.md).

**How to use this file**
- Tick a task `[x]` only when it compiles clean (zero console errors) **and** its tests pass.
- When a milestone's tasks and *Done-when* check are all green, set its status to ✅ and add a line to the **Log**.
- New work discovered mid-milestone → add it as a task here (or to *Backlog*) instead of silently expanding scope.

Status legend: ⬜ not started · 🟦 in progress · ✅ done · ⏸ blocked

---

## Overview

| M | Milestone | Criteria | Status |
|---|---|---|---|
| M0 | Foundation | – | ✅ |
| M1 | Core generation | 1, 3, 5 | ✅ |
| M2 | Optimization | 2 | ✅ |
| M3 | Convex decomposition | 2 | ✅ |
| M4 | Scene editing | 4 | ✅ |
| M5 | Paint & skinned | 4 | ✅ |
| M6 | Runtime bridge features | 5 | ✅ |
| M7 | Polish & release | 1 | ⬜ |

Criteria: 1 clean UI · 2 optimize · 3 multi-object · 4 scene editing · 5 multi-collider bridge.

---

## M0 — Foundation ✅

- [x] Embedded UPM package `Packages/com.fahril.autocollider/` (`package.json`, README, CHANGELOG, `Documentation~`)
- [x] Dependencies: `com.unity.burst`, `com.unity.collections`, `com.unity.modules.physics` (Mathematics is an engine module in 6000.6 — no package needed)
- [x] Assemblies: `AutoCollider.Runtime`, `AutoCollider.Editor` (Editor-only), `AutoCollider.Editor.Tests` (EditMode)
- [x] `InternalsVisibleTo` Runtime → Editor/Tests, Editor → Tests
- [x] `PackagePaths` (all hard-coded paths), `Strings` (all UI text)
- [x] `PhysicsLimits` runtime constants (255 convex polygons, min hull verts, min extent)
- [x] `AutoColliderSettings` (`ScriptableSingleton` → `ProjectSettings/AutoColliderSettings.asset`) with `AutoColliderSettingsData` defaults
- [x] Project Settings page `Project ▸ Auto Collider` (UI Toolkit, sections, folder browse + validation, reset) + menu `Tools ▸ Auto Collider ▸ Project Settings…`
- [x] Shared USS theme `AutoCollider.uss` + `AutoColliderStyles.Apply` (dark/light skin tokens)
- [x] Removed empty `Assets/Auto Collider Generator/`
- [x] Tests: `PackageSetupTests` (16 cases)

**Done when:** package compiles with zero errors; Project Settings page visible; tests green. → *Verified 2026-10-12: 16/16 EditMode tests passed, console clean, settings page builds 3 sections / 11 fields.*

---

## M1 — Core generation ✅

Core math (`Editor/Core`)
- [x] `MeshGeometry` + `MeshSource` (MeshFilter / SkinnedMeshRenderer bake, `AcquireReadOnlyMeshData`, combine children, filters)
- [x] `TargetSpace` — fitting happens in the target's *scaled space* (target-oriented, world-sized) so non-uniform scale stays correct
- [x] `BoxFitter` — AABB, PCA OBB, minimum-volume OBB (hull face normals + min-area rectangle), canonical axes
- [x] `SphereFitter` — Ritter, Welzl (iterative, deterministic shuffle)
- [x] `CapsuleFitter` — best local axis or PCA axis; minimal-circle radius + tight cap segment
- [x] `QuickHull3D` (double precision, deterministic) + `TryBuildCapped` ≤ 255 triangles
- [x] `GeometryUtils` (Jacobi eigen, covariance, 2D hull, min-area rect, mesh volume, axis canonicalization)

Generation (`Editor/Generation`, data in `Runtime/`)
- [x] Runtime data: `ColliderEntry`, `GenerationRecipe`, `GenerationSettings`, enums `ColliderStrategy` / `MeshGranularity` / `ReplaceMode`
- [x] `FittedShape` — editor-side shape data (replaces the planned runtime `ColliderShape`)
- [x] Strategies `BestPrimitive`, `Box`, `Sphere`, `Capsule`, `ConvexHull` behind `IColliderStrategy` + `StrategyRegistry`
- [x] `GenerationPipeline.Compute` → `TargetResult` (pure; also drives live preview)
- [x] `GeneratedMeshStore` + `GeneratedMeshLibrary` (sub-assets per target name, FNV-1a content-hash dedupe)
- [x] `ColliderWriter` (`[Colliders]` container, rotated primitives on child GOs, replace modes, prefab-instance overrides, Undo)
- [x] `ColliderGroup` (entries, ownership, bulk enable / trigger / layer / material, world bounds)
- [x] `BatchProcessor` (cancelable progress, single Undo group, per-object status) + `TargetResolver` (scene objects only, nested targets and containers dropped, hierarchy order)
- [x] `GenerationSettingsState` (per-user settings in `UserSettings/`)

UI
- [x] `AutoColliderWindow` (TabView: Generate + placeholders) — `Tools ▸ Auto Collider ▸ Open Window`
- [x] Generate tab: selection-following target list with status chips, strategy hint, Filters / Output foldouts, Live Preview (triangle budget), Generate / Remove with counts, result summary + details
- [x] `ColliderGroupEditor`: summary, cost meter, Regenerate (from recipe), Remove, entry list (on / trigger / lock / select), missing-entry clean-up, bulk trigger / layer / material, read-only recipe
- [x] `PreviewDrawer` + `ShapeDrawing` (ghost wireframes in the Scene view)
- [x] Menus `GameObject ▸ Auto Collider ▸ Generate / Remove` (safe with multi-selection) + shortcut `Shift+Alt+C`

Tests (60 EditMode, all green)
- [x] Fitters: exact AABB; rotated box → OBB volume ±1 % and rotation; axis-aligned → identity; flat quad; UV sphere ±1 %; cube corners; Ritter ≥ Welzl; determinism; capsule along X; diagonal capsule; short capsule
- [x] QuickHull: cube 8 v / 12 t; random cloud containment + outward winding; closed manifold (Euler = 2); coplanar / too few fail; capped ≤ 255 keeps > 90 % volume; determinism
- [x] Batch: 10 objects one Undo; user colliders untouched; regenerate replaces / locked survives; ReplaceAll; RemoveGenerated; convex mesh asset dedupe; rotated child; non-uniform scale sphere; per-renderer vs combined; filters; resolver; prefab-instance overrides; no-mesh skip

**Done when:** generate for 10 selected objects; one Ctrl+Z reverts all; fitter tests pass. → *Verified 2026-10-12: 60/60 tests. Manual run on `Assets/AutoCollider Dev/M1 Test Scene.unity`: 15 colliders on 7 objects in ~330 ms; one Undo / Redo round-trips every group; user colliders untouched; convex hulls ≤ 255 triangles saved as sub-assets.*

---

## M2 — Optimization ✅

Optimization core (`Editor/Optimization`)
- [x] `ClusterSimplifier` — Burst job: grid clustering; per-cell representative from area-weighted **quadrics** solved around the cell mean with a pseudo-inverse (corner → corner, edge → edge line, flat → plane), clamped to the cell; Quality → log-scaled cell size, Fidelity → mean ↔ quadric blend; `MaxTriangles` grows cells until it fits; deterministic
- [x] `MeshCleaner` — weld ε, collapsed / duplicate (either winding) triangles, small-island removal (union-find)
- [x] `InflateDeflate` — angle-weighted vertex normals for meshes; analytic grow / shrink for primitives
- [x] `ErrorMetric` — Burst parallel closest-point-on-triangle over sampled source vertices (max / mean / per-sample)
- [x] `ColliderStats` — triangles / vertices before → after, savings %, memory estimate, bake time
- [x] `MeshOptimizer` pipeline (weld → islands → simplify → inflate → measure) + `OptimizeConvex` (capped hull)
- [x] `ColliderOptimizer` — optimize **existing** MeshColliders: analyze (no changes) → apply (one Undo step, mesh saved as asset) → restore original; re-optimizing always starts from the original mesh (source tracked in `GeneratedMeshLibrary`)

Generation
- [x] `OptimizedMesh` strategy (concave); dynamic Rigidbody → automatic convex fallback + warning
- [x] Inflate applied to every strategy; mesh stats + error per shape, aggregated per target and batch
- [x] Performance: lazy hull in `FitInput`; dense input (> 4,096 points) thinned before hulls, primitives re-fit extents over all points so they still enclose; QuickHull allocations removed (test suite 22 s → 5 s)

Presets
- [x] `ColliderPreset` asset (`Create ▸ Auto Collider ▸ Collider Preset`, "Save Current as Preset…")
- [x] Built-ins High Quality / Balanced / Performance / Mobile (optimization values only; defaults = Balanced)
- [x] Project Settings ▸ Default Preset (used by Reset to Defaults)

UI
- [x] Generate tab: preset row, Inflate, Mesh Optimization foldout, stats in preview hint / result / Scene notification
- [x] Optimize tab: selection or whole-scene MeshCollider list, Analyze / Optimize / Restore Originals, per-row before → after + savings + error tooltip, totals with memory, error heatmap (threshold, lines)
- [x] `HeatmapDrawer` (green → yellow → red, colour-bucketed `Handles.DrawLines`)

Tests (84 EditMode, all green, ~5 s)
- [x] Dense sphere at Balanced: ≥ 80 % fewer triangles, max error < 1.5 % of size, no degenerates, bounds within one cell
- [x] Deterministic; quadric representatives ≥ 2× more accurate than averaging on a cube; MaxTriangles respected; quality monotonic
- [x] Weld / degenerate / island removal; inflate (mesh + primitives); error metric; closest-point regions; presets; stats; units
- [x] Scene: Optimized Mesh generation, dynamic fallback, inflate; optimize existing → apply / Undo / Redo / restore; convex existing; concave on dynamic body rejected; dense input still enclosed

**Done when:** sample mesh −80% tris at Balanced with max error under threshold; output deterministic. → *Verified 2026-10-12: tests above, plus manual run on the dev scene's 39,200-triangle `Rock`: Balanced → 6,042 triangles (−85 %), max error 6.5 mm (mean 1.2 mm) on a ~2.8 m object, memory ≈ 846 KB → 130 KB, 40–55 ms; Optimize / Undo / Redo / Restore round-trip; heatmap renders; Optimized Mesh generation 140 ms.*

---

## M3 — Convex decomposition ✅

- [x] `Voxelizer` (Burst): surface voxels by exact triangle/box overlap (SAT), interior by flood-filling the outside (2-voxel padding), surface samples (≤ ½ voxel spacing) for hull points; closed meshes → solid, open → shell
- [x] `ConvexDecomposer`: block regions (6 per axis, connected pieces per block) → hull per region (interior-voxel corners + surface samples) → greedy merge of adjacent regions by **added empty space** (merged waste − own wastes, ÷ object volume) with a deterministic min-heap; merges beyond `Concavity Tolerance` only to get under `Max Parts`; disconnected pieces always keep a part
- [x] 256 guard per part (`TryBuildCapped`), region hull points capped at 128 to keep merges fast
- [x] `PrimitiveReplacer`: part → box / sphere / capsule when hull volume ÷ primitive volume ≥ `Primitive Fit Threshold` (cheaper shape wins within 2 %)
- [x] `ConvexDecomposition` strategy (stats + error for the whole part set, parts count) and **`Auto` strategy** (now the default): primitive if it fills ≥ threshold → convex hull if convexity (exact volume for watertight meshes, voxel estimate otherwise) ≥ threshold → decomposition (moving Rigidbody) / optimized mesh (static); explains its choice per mesh
- [x] `ShapeMesher` (primitives → triangles, for measuring mixed results)
- [x] Settings: Decomposition Resolution, Max Parts, Concavity Tolerance, Primitive Fit Threshold, Convexity Threshold (+ Generate tab foldout, Auto notes in results)
- [x] Fix: `MeshSource.ReadMesh` drops vertices no triangle uses (they inflated fits and error numbers)
- [x] Tests: triangle/box overlap, solid vs shell voxelization, sphere → 1 part / sphere primitive, L → 2–4 tight parts covering every vertex (< 1.2× true volume), arms → boxes, torus keeps its hole, Max Parts, disconnected pieces, determinism, primitive replacer, exact convexity, Auto choices, Auto capsule, shape mesher, Auto on a moving L in the scene (one Undo), unreferenced vertices

**Done when:** dynamic sample objects get convex parts ≤ 255 faces; Auto picks sensible types on the sample scene. → *Verified 2026-10-12: 100/100 tests. Auto on the dev scene (9 objects, 2.0 s): table → box top + capsule legs, chair → boxes, ball → sphere, pillar → capsule, squashed ball and rock → convex hull (no primitive fits; rock hull deviates ≤ 11.6 cm on 2.8 m), crate → box, static arch → optimized mesh, L-bracket on a Rigidbody → 2 box parts.*

---

## M4 — Scene editing ✅

- [x] `ColliderEditTool : EditorTool` (component tool for `ColliderGroup`, in the Tools overlay) + `ColliderOverlay` (transient UI Toolkit overlay): Select / Edit modes (Paint button present, enabled in M5)
- [x] `ColliderPicker`: exact per-collider raycasts (any collider type, ignores layers), repeat-click cycles through overlaps, drag rectangle select; Shift add / Ctrl toggle; clicks never change Unity's selection
- [x] Handles: Unity bounds handles in the shape's own frame (face resize, Alt symmetric, Ctrl snap) for box / sphere / capsule radius & height; Move / Rotate handles (W / E / R); rotating a box on the shared container moves it onto its own child; mesh colliders detach to a child (same mesh asset) to move / rotate
- [x] Vertex snap (hold V): resize snaps the dragged face to the vertex under the mouse; move snaps the center
- [x] `ColliderOperations` (one Undo step each): duplicate, delete, mirror X / Y / Z (rotated children too, mirrored meshes re-wound, Left↔Right names), merge → convex / box, convert → box / sphere / capsule / convex, refit (shrink-wrap to the source vertices inside), inflate / deflate, lock, rename; plus hide / solo / show all (editor-only)
- [x] Edited colliders lock automatically (Project Settings ▸ Lock After Edit) so Regenerate keeps them
- [x] `ColliderShapes`: live collider ↔ `FittedShape` in the group's world-sized frame (exact round trip, non-uniform scale aware); `EnsureRepresentable` refits rotated primitives axis-aligned under non-uniform scale
- [x] Visuals (`ColliderDrawing`): type colors, selection color, translucent fill, X-ray (faded through geometry), labels (lock icon), disabled colliders dimmed
- [x] Shortcuts via an `IShortcutContext` (only while the tool is active, rebindable): 1/2, W/E/R, Ctrl+D, Del, M, G, L, H / Shift+H / Alt+H, X, V; Edit-menu commands (Duplicate, Delete, Frame Selected, Select All) act on colliders
- [x] Inspector ↔ scene sync: Edit in Scene button, row click selects (Shift / Ctrl), selected rows highlighted, Select button frames; window Edit tab (enter tool + shortcut sheet)
- [x] **Fix (also M1 Regenerate):** `RecordObject` must come right before each group change — other Undo calls flush pending records, so entry edits were lost on undo. `ColliderWriter.RecordGroup` + ordering fixed everywhere; regenerate-undo test added
- [x] Tests (20 new): shape round trips (non-uniform scale, rotated child), resize in place + auto-lock + undo, rotate → own child + undo, detach mesh, duplicate / delete, mirror rotated child (pose + every mirrored corner inside), merge, convert, refit, inflate, lock + rename, picker order + cycling, rect select, vertex snap, mirror names, selection / visibility state

**Done when:** all ops Undo-able; edit a box face by drag; mirror works on rotated children. → *Verified 2026-10-12: 121/121 tests (face resize via the same `Apply` path the bounds handle uses). Live in the editor on the dev-scene table: tool activates from the inspector / window, overlay shows modes, handles, summary and actions; Mirror Z, Convert → convex, Merge → box all applied, edited colliders auto-locked, three Ctrl+Z restored the original exactly.*

---

## M5 — Paint & skinned ✅

- [x] Paint mode in the edit tool (key 3, `GameObject ▸ Auto Collider ▸ Paint Colliders`, window Edit tab): screen-space triangle brush on the source meshes (front faces only, depth tolerance so it doesn't bleed through, `[` / `]` resize, Ctrl+drag erases, Esc clears), All / Connected / Grow (welded adjacency); painting works on objects without colliders (adds a Collider Group, one Undo step). `PaintMesh` (world-space mesh + Möller–Trumbore raycast), `PaintMeshCache` (rebuilt when the renderer moves), `PaintDrawing` (GL tint per region + brush disc). Box (rectangle) select moved to the Backlog
- [x] Create from the painted triangles: Auto / Box (rotated when it's tighter) / Sphere / Capsule / Convex / Parts (decomposition) — `RegionFitter`, shared with the pipeline so creating and regenerating give the same result
- [x] `PaintedRegion` (renderer + triangle indices + shape + label) stored in the recipe; region ids ≥ 10000 tag their colliders (`PartId`); with regions, Generate / Regenerate rebuild only the regions (Technie-style) and locked region colliders suppress their region; overlay region list (swatch, re-paint, delete) + inspector *Clear Regions*; missing renderer / changed triangles → warnings
- [x] `PerBone` strategy (also what Auto picks for skinned meshes): strongest-bone vertex assignment (weight threshold, min vertices), fit in bone-local bind pose (`bindposes`), collider objects parented to the bones so they follow animation; Bone Shape (Auto / box / sphere / capsule / convex); `ColliderEntry.OwnsGameObject` lets removal delete those bone children (and never a user object)
- [x] Symmetric bones: Left/Right pairs by name pattern (`_L`/`_R`, `Left`/`Right`, …) get the larger size of the two
- [x] Tests (13 new): strongest-bone threshold, per-bone capsules on a procedural 3-bone limb (radius, axis, parented, no container), colliders follow a bent bone, Auto on skinned → per bone + remove / undo, symmetrize (unit + through generation); 3 painted regions on an L regenerate identically, locked region kept, create / delete region single Undo, sub-mesh with stale triangles, brush front-face / radius, grow / connected, paint state

**Done when:** painted regions regenerate identically; skinned sample gets per-bone capsules that follow animation. → *Verified 2026-10-12: 134/134 tests (procedural skinned limb: capsules on bones follow a 90° elbow bend). Live on the dev-scene `Arch (static)`: Paint Colliders added a group and opened Paint mode; 3 regions (2 pillar feet → boxes, the arch → 12 convex parts) shown in the overlay list with colored tints; Regenerate reproduced all 14 colliders bit-identically.*

---

## M6 — Runtime bridge features ✅

- [x] `ColliderGroup` events `CollisionEnter/Stay/Exit (entry, Collision)` and `TriggerEnter/Stay/Exit (entry, other)`; routing starts on the first subscription (play mode): `ColliderRelay` on each object that receives messages (collider objects + their Rigidbody objects), `ColliderStayRelay` only when a Stay event is used (Unity calls Stay every step). Collisions resolve the part exactly via `Collision.thisCollider` and accept only the canonical receiver (Rigidbody object, else the collider's own object) → no double reports; triggers resolve the part by overlap (`TriggerPairs`: penetration test on Enter, remembered per other collider until Exit). `RefreshRouting()` for colliders added at runtime
- [x] Bulk API: `SetLayerOverrides(include, exclude, priority)`, `SetProvidesContacts`, plus existing enable / trigger / layer / material / `Where` / `WorldBounds`; `TryGetEntry` uses a dictionary (rebuilt after Undo / re-pointing). Inspector: Bulk Edit gets layer overrides + contacts, new *Runtime* foldout (events how-to, Add LOD Follower), LOD tag per row; every Apply button tooltipped
- [x] `AnimatedMeshCollider` (Bake Mesh / Shared Mesh; Every Frame / Every N Frames / Target Hz via pure `RefreshSchedule`; only when visible; target on another transform is handled; baked mesh owned + destroyed, original mesh restored). `GameObject ▸ Auto Collider ▸ Add Animated Mesh Collider` (convex under moving Rigidbodies, one Undo). UI Toolkit inspector: missing refs, concave-on-dynamic (Make Convex), Shared Mesh needs Read/Write (Enable Read/Write), cost estimate
- [x] `ColliderLodFollower` + `LodMath` (LODGroup's screen-relative height × LOD bias, maximum LOD level, culled → lowest-detail colliders); levels without colliders fall back to the closest more detailed level. Generation option **Per LOD Colliders**: fits every level of the LOD Group, tags entries (`ColliderEntry.Lod`, stored +1 so old data means "every LOD"), starts with LOD 0's colliders on, adds the follower
- [x] Tests: PlayMode assembly `AutoCollider.Tests` (8): collision part on a shared container without Rigidbody (+ exit), Rigidbody compound reports the lower foot first and never twice per step, trigger part by overlap (enter / switch / exit), Stay relay only when subscribed, Target Hz and Every N Frames throttling, collider follows the skinned pose + original mesh restored, only-when-visible, LOD follower near / far / culled. EditMode (11): schedule rules, LOD math + bias, per-LOD generation + undo, LOD 0 Only default, combined per level, fallback level, entry LOD default, lookup after re-point / undo, bulk overrides, Add Animated (convex when dynamic, one undo), bake follows the pose

**Done when:** play-mode tests green; events fire with correct entry; animated collider throttles per settings. → *Verified 2026-10-12: 145/145 EditMode + 8/8 PlayMode tests, console clean. Live on the dev scene: inspector shows Bulk Edit / Runtime / Recipe with every button tooltipped; Per LOD Colliders on a two-level LOD object → LOD 0 box (on) + LOD 1 sphere (off) + follower; Add Animated Mesh Collider + its inspector build without errors.*

---

## M7 — Polish & release ⬜

- [ ] Collider Doctor (checks + one-click fixes) — Doctor tab + menu
- [ ] Hierarchy badge (optional, toggle in settings)
- [ ] `Documentation~` user manual
- [ ] `Samples~/Demo` (scene + models) registered in `package.json`
- [ ] Perf pass: 1 000-object batch, large meshes, inspector with 500 entries
- [ ] UI polish in both skins; every control tooltipped
- [ ] Version bump 1.0.0 + CHANGELOG

**Done when:** Doctor finds & fixes seeded issues; docs + sample importable via Package Manager.

---

## Backlog / ideas

- Optional VHACD adapter behind `IColliderStrategy` (only if own decomposition quality is insufficient)
- BlendShape pose snapshot / re-optimize on weight change (MCO feature)
- LOD-specific quality per LOD level
- Localization of `Strings`
- Clean up orphaned generated meshes (undoing a convex generation leaves its mesh sub-asset behind) — `Tools ▸ Auto Collider ▸ Clean Unused Meshes`
- Batch generation for prefab / model assets selected in the Project window (the writer is already prefab-aware)
- Burst port of `QuickHull3D` (managed today; dense input is thinned first) — would also speed up decomposition merges (~1.5 ms per 256-point hull)
- Decomposition: split regions along the best cutting plane (better corners than fixed initial blocks)
- Error heatmap in the Generate tab live preview (Optimize tab has it)
- Edit tool: per-face drag preview of the error heatmap; multi-collider handles (edit several at once)
- Paint mode: rectangle / lasso triangle select, vertex pick → collider (ECE style)
- Per-bone: several capsules along long twist chains, ragdoll export (joints + rigidbodies)
- Lower minimum Unity version (currently 6000.6 because of built-in Burst 2.0 / Collections 6.6 versions)

---

## Log

| Date | Milestone | Note |
|---|---|---|
| 2026-10-12 | Docs | `CLAUDE.md`, `ARCHITECTURE_PLAN.md`, `ROADMAP.md` created |
| 2026-10-12 | M0 ✅ | Package skeleton, assemblies, settings page, USS theme, 16 tests green |
| 2026-10-12 | M1 ✅ | Fitters, QuickHull, pipeline, writer, ColliderGroup, batch + single Undo, window Generate tab, inspector, 60 tests green; package 0.2.0 |
| 2026-10-12 | M2 ✅ | Burst QEM simplifier, cleaner, inflate, error metric + heatmap, Optimized Mesh strategy, Optimize tab (apply / restore), presets, 84 tests green; package 0.3.0 |
| 2026-10-12 | M3 ✅ | Burst voxelizer, convex decomposition with greedy concavity merge, primitive replacement, Auto strategy (default), 100 tests green; package 0.4.0 |
| 2026-10-12 | M4 ✅ | Collider edit tool + overlay, picking, handles, vertex snap, 10 operations (one Undo each), shortcuts, inspector sync; fixed Undo record ordering; 121 tests green; package 0.5.0 |
| 2026-10-12 | M5 ✅ | Paint mode (brush, grow / connected, regions stored in the recipe, identical regenerate), per-bone colliders for skinned meshes (follow animation, L/R symmetry), Auto → per bone on skinned; 134 tests green; package 0.6.0 |
| 2026-10-12 | M6 ✅ | Group collision / trigger events with the hit part (relays, no double reports), bulk layer overrides + contacts, Animated Mesh Collider (throttled), per-LOD colliders + LOD follower; 145 EditMode + 8 PlayMode tests green; package 0.7.0 |
