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
| M2 | Optimization | 2 | ⬜ |
| M3 | Convex decomposition | 2 | ⬜ |
| M4 | Scene editing | 4 | ⬜ |
| M5 | Paint & skinned | 4 | ⬜ |
| M6 | Runtime bridge features | 5 | ⬜ |
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

## M2 — Optimization ⬜

- [ ] `ClusterSimplifier` (Burst; grid clustering + quadric-error representatives; Quality → cell size, Fidelity → QEM blend)
- [ ] `MeshCleaner` (weld ε, degenerate/duplicate tris, small islands)
- [ ] `InflateDeflate` (angle-weighted normals)
- [ ] `OptimizedMesh` strategy (concave, static/kinematic)
- [ ] `ErrorMetric` (sampled max/mean) + `ColliderStats` (verts/tris/memory/bake ms/savings)
- [ ] `ColliderPreset` ScriptableObject + built-ins (High Quality / Balanced / Performance / Mobile) + "Save as preset"; default preset in Project Settings
- [ ] Optimize tab (existing MeshColliders table, before → after, batch optimize, save mesh)
- [ ] Heatmap drawer (cached, Scene view)
- [ ] Tests: bounds preserved, no degenerates, deterministic, error under threshold

**Done when:** sample mesh −80% tris at Balanced with max error under threshold; output deterministic.

---

## M3 — Convex decomposition ⬜

- [ ] Voxelizer (surface + interior flood fill)
- [ ] `ConvexDecomposer` (grid regions → hull per region → greedy merge by concavity cost; `MaxParts`)
- [ ] 256 guard per part
- [ ] `PrimitiveReplacer` (fill ratio ≥ threshold → box/sphere/capsule)
- [ ] `ConvexDecomposition` strategy + `Auto` strategy heuristic
- [ ] Tests: every part convex & ≤ 255 faces, coverage within tolerance

**Done when:** dynamic sample objects get convex parts ≤ 255 faces; Auto picks sensible types on the sample scene.

---

## M4 — Scene editing ⬜

- [ ] `ColliderEditTool : EditorTool` + `ColliderOverlay` (Select / Edit / Paint modes)
- [ ] `ColliderPicker` (ray vs shape, overlap cycling, box select)
- [ ] `ShapeHandles` (face resize, Alt symmetric, Ctrl snap, radius/height, move/rotate)
- [ ] Vertex snap (V)
- [ ] Ops: duplicate, delete, mirror X/Y/Z, merge → hull/box, convert type, refit, inflate, lock, solo/hide, rename
- [ ] Visuals: type colors, selection highlight, X-ray, fill + wire, labels
- [ ] `[Shortcut]` bindings
- [ ] Inspector list ↔ scene selection sync
- [ ] Tests: each op is a single Undo step; mirror of rotated child correct

**Done when:** all ops Undo-able; edit a box face by drag; mirror works on rotated children.

---

## M5 — Paint & skinned ⬜

- [ ] Paint mode (brush + box triangle/vertex selection on source mesh)
- [ ] Create box / rotated box / sphere / capsule / hull / auto-decompose from selection
- [ ] `PaintedRegion` stored in recipe; Regenerate reproduces result; locked entries kept
- [ ] `PerBone` strategy (weight assignment, bone-local fit, parent to bone)
- [ ] Mirror L/R by bone name pattern
- [ ] Tests: paint 3 regions → regenerate identical; per-bone capsules on skinned sample

**Done when:** painted regions regenerate identically; skinned sample gets per-bone capsules that follow animation.

---

## M6 — Runtime bridge features ⬜

- [ ] `ColliderGroup` events (Collision/Trigger Enter/Stay/Exit with entry) + `ColliderRelay`
- [ ] Bulk API: layer overrides (include/exclude/priority), provides contacts, `Where` filter, world bounds
- [ ] `AnimatedMeshCollider` (BakeMesh/SharedMesh, EveryFrame/EveryNFrames/TargetHz, only-when-visible)
- [ ] `ColliderLodFollower`
- [ ] PlayMode tests: events report correct entry; throttling respected

**Done when:** play-mode tests green; events fire with correct entry; animated collider throttles per settings.

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
- Lower minimum Unity version (currently 6000.6 because of built-in Burst 2.0 / Collections 6.6 versions)

---

## Log

| Date | Milestone | Note |
|---|---|---|
| 2026-10-12 | Docs | `CLAUDE.md`, `ARCHITECTURE_PLAN.md`, `ROADMAP.md` created |
| 2026-10-12 | M0 ✅ | Package skeleton, assemblies, settings page, USS theme, 16 tests green |
| 2026-10-12 | M1 ✅ | Fitters, QuickHull, pipeline, writer, ColliderGroup, batch + single Undo, window Generate tab, inspector, 60 tests green; package 0.2.0 |
