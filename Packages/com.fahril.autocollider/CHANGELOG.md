# Changelog

All notable changes to this package are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows [SemVer](https://semver.org/).

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
