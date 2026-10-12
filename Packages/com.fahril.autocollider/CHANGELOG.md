# Changelog

All notable changes to this package are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows [SemVer](https://semver.org/).

## [0.6.0] - 2026-10-12

### Added
- **Paint mode** (M5) in the collider edit tool (key **3**, `GameObject ▸ Auto Collider ▸ Paint Colliders`, window *Edit* tab — works on objects without colliders yet):
  - Screen-space triangle brush on the source meshes (front faces only, doesn't bleed through), Ctrl+drag erases, `[` / `]` brush size, Esc clears; *All*, *Connected* and *Grow* selection helpers.
  - Create a collider from the painted triangles: Auto, Box (rotated when tighter), Sphere, Capsule, Convex or Parts (convex decomposition) — one Undo step.
  - Painted **regions** are stored in the Collider Group's recipe; Generate / Regenerate rebuild exactly those colliders (locked ones are kept). Region list in the overlay (re-paint, delete) and *Clear Regions* in the inspector.
- **Per Bone** strategy for skinned meshes, also chosen by Auto: one collider per bone (strongest-weight vertices, fitted in the bone's bind pose), placed on a child of the bone so it follows animation. Settings: Bone Shape, Bone Weight Threshold, Min Bone Vertices, Symmetric Bones (Left/Right pairs get the same size) — "Skinned Meshes" foldout.

### Changed
- Removing generated colliders also deletes the GameObjects Auto Collider created for them (bone children, detached colliders) — and never any other object.

## [0.5.0] - 2026-10-12

### Added
- **Collider edit tool** (M4) — Scene-view tool for Collider Groups (Tools overlay, *Edit in Scene* in the inspector, window *Edit* tab):
  - Click to select (click again to cycle through overlapping colliders), drag to box-select, Shift add / Ctrl toggle.
  - Resize with face handles (Alt symmetric, Ctrl snap), sphere radius, capsule radius / height; Move and Rotate handles; hold **V** to snap to mesh vertices.
  - Operations, each one Undo step: Duplicate, Delete, Mirror X / Y / Z (swaps Left/Right names), Merge → convex / box, Convert → box / sphere / capsule / convex, Refit (shrink-wrap), Inflate / Deflate, Lock, Rename; Hide / Solo / Show All.
  - Overlay panel with modes, handles, selection summary and all actions; X-ray and labels.
  - Rebindable shortcuts that apply only while the tool is active (1/2, W/E/R, Ctrl+D, Del, M, G, L, H, X, V); Edit-menu Duplicate / Delete / Frame / Select All act on colliders.
  - Edited colliders are locked automatically so Regenerate keeps your changes (Project Settings ▸ Lock After Edit).
- Collider Group inspector ↔ scene sync: clicking a row selects the collider in the tool; selected rows are highlighted.
- Project Settings: Lock After Edit, Show Labels.

### Fixed
- Undo after regenerating an existing Collider Group now restores the previous colliders and entry list (Undo records were taken too early).

## [0.4.0] - 2026-10-12

### Added
- **Convex Decomposition** strategy (M3): splits concave meshes into convex parts for moving Rigidbodies — Burst voxelizer (solid for closed meshes), greedy merging by added empty space, PhysX 255-triangle cap per part, and parts that fit a box / sphere / capsule are replaced by that cheaper primitive.
- **Auto** strategy, now the default: per mesh it picks a primitive if one fits, else one convex hull if the mesh is convex enough, else convex parts (moving objects) or an optimized mesh (static); the window explains each choice.
- Settings: Decomposition Resolution, Max Parts, Concavity Tolerance, Primitive Fit Threshold, Convexity Threshold ("Convex Decomposition & Auto" foldout).
- Statistics show the number of parts.

### Fixed
- Vertices not used by any triangle no longer influence collider fits or error numbers.

## [0.3.0] - 2026-10-12

### Added
- Mesh collider optimization (M2):
  - **Optimized Mesh** strategy — a simplified concave MeshCollider for static / kinematic objects (dynamic Rigidbodies fall back to a convex hull with a warning).
  - Burst vertex-clustering simplifier with quadric-error representatives (keeps corners and edges), weld / degenerate clean-up and small-island removal.
  - Settings: Quality, Shape Fidelity, Max Triangles, Inflate / Deflate, Min Island Size.
- **Optimize tab**: optimize existing MeshColliders (selection or whole scene) — Analyze (no changes), Optimize (one Undo step, mesh saved as an asset), Restore Originals; before → after triangles, savings, max / mean error, memory estimate.
- **Error heatmap** in the Scene view (green → red, adjustable threshold, error lines).
- **Presets**: built-in High Quality / Balanced / Performance / Mobile, `Collider Preset` assets (save from the window or `Create ▸ Auto Collider ▸ Collider Preset`), Project Settings ▸ Default Preset.
- Inflate / Deflate for every strategy; triangle and error statistics in the window, preview and Scene notification.

### Changed
- Dense meshes generate much faster: the convex hull is built lazily and from thinned points above 4,096 vertices; primitives still enclose every vertex.

## [0.2.0] - 2026-10-12

### Added
- Collider generation for one or many selected objects (M1):
  - Strategies: **Best Primitive** (tightest of box / sphere / capsule), **Box** (minimum-volume oriented box), **Sphere** (minimal enclosing), **Capsule** (best axis), **Convex Hull** (≤ 255 triangles, PhysX limit).
  - Per-renderer or combined fitting; filters for children, inactive objects, LOD 0, minimum size and name pattern.
  - Correct results under non-uniform scale (fitting in world-sized target space).
  - Rotated primitives placed on their own child objects under a `[Colliders]` container.
- `ColliderGroup` component: tracks every generated collider (ownership, lock), bulk enable / trigger / layer / material, stores the recipe for Regenerate.
- Generated convex meshes saved as sub-assets (deduplicated by content hash) — never inline in scenes.
- Whole batches are a single Undo step; user-made colliders are never touched unless "Replace All" is chosen.
- **Auto Collider window** (`Tools ▸ Auto Collider ▸ Open Window`): Generate tab following the selection, per-object status, live Scene-view preview, result summary.
- Collider Group inspector: summary, physics-cost meter, collider list with on / trigger / lock toggles, bulk edit, Regenerate / Remove.
- `GameObject ▸ Auto Collider ▸ Generate / Remove Generated Colliders` and shortcut `Shift+Alt+C`.
- 60 EditMode tests (fitters, QuickHull, batch / Undo / prefab behaviour).

## [0.1.0] - 2026-10-12

### Added
- M0 foundation: package skeleton, Runtime / Editor / Tests assemblies.
- Project Settings page (`Project Settings ▸ Auto Collider`) backed by `ProjectSettings/AutoColliderSettings.asset`.
- Shared USS theme (`AutoCollider.uss`) with light/dark skin support.
- `PhysicsLimits` constants (PhysX convex polygon limit etc.).
