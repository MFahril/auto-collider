# Changelog

All notable changes to this package are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows [SemVer](https://semver.org/).

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
