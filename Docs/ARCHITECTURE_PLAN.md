# Auto Collider — Analysis, Architecture & Plan

> Status: **M0–M1 done** — progress is tracked in [`ROADMAP.md`](ROADMAP.md).
> Rules for contributors / Claude Code: see [`CLAUDE.md`](../CLAUDE.md).

---

## 0. Goals

Build a Unity 6 editor plugin (UPM package) that makes colliders **fast to generate, cheap at runtime, and easy to fix by hand** — for one object or a whole selection.

| # | User criterion | Where it's solved |
|---|---|---|
| 1 | Clean UI in Inspector + Unity window | §5.6 UI layer (UI Toolkit inspector, `AutoColliderWindow`, Scene Overlay) |
| 2 | Optimize colliders (like Mesh Collider Optimizer) | §5.2 Optimization layer, §6 algorithms |
| 3 | Multiple object support (generate for selection) | §5.3 `BatchProcessor` |
| 4 | Edit colliders in scene (overhaul) | §5.5 Editing layer (`ColliderEditTool`) |
| 5 | Multi-collider system ("one bridge" for many colliders) | §5.4 `ColliderGroup` runtime bridge |
| + | Extra convenience (my recommendations) | §7 |

Non-goals (for v1): 2D colliders, terrain colliders, wheel colliders, native VHACD.

---

## 1. Analysis

### 1.1 Reference plugin — Mystery Studio "Easy Collider" (in this repo)

Files: `Assets/Mystery Studio/Easy Collider/Scripts/AddCollidersToMeshes.cs`, `Editor/AddCollidersToMeshesEditor.cs`.

**What it does:** a component you add to a root; picks one collider type (Mesh/Box/Capsule/Sphere/Wheel/Terrain), optionally recurses children, sets tag/layer, draws gizmo preview, removes colliders.

**Problems (what we must do better):**

| Problem | Detail | Our fix |
|---|---|---|
| Breaks builds | Runtime `MonoBehaviour` has `using UnityEditor;` | Strict Runtime/Editor asmdef split |
| Crude fitting | Uses `mesh.bounds` only → AABB box, Y-only capsule, oversized sphere (`max(size)/2`) | OBB / PCA / Welzl / min-volume fitters, auto axis |
| Destructive | `DestroyImmediate` on **all** existing colliders, no Undo | Ownership tracking + Undo groups |
| Memory leak | `GetMesh()` does `new Mesh()` + `BakeMesh` every `OnDrawGizmos` for skinned meshes, never destroyed | Cached `MeshSource`, explicit disposal |
| Side effects | Overwrites tag + layer of every visited child | Opt-in, generated objects only |
| One object at a time | Component per root, no multi-select | `BatchProcessor` over selection/prefabs |
| No editing | Can only regenerate | Full scene edit tool |
| No optimization | MeshCollider uses full render mesh | Simplifier + decomposition + primitive replacement |
| Nonsense options | Wheel/Terrain collider on arbitrary meshes | Removed |
| Inspector | Plain IMGUI fields + 3 buttons | UI Toolkit with summary, list, presets, stats |

### 1.2 Easy Collider Editor (pmurph0305) — $30, v6.20

Core idea: **select vertices/points on the mesh → create a fitted collider from them.**

- Collider types: box, rotated box, sphere, capsule, rotated capsule, cylinder (as convex mesh), convex mesh.
- Vertex selection tools (click, box select, add/remove), live preview while selecting.
- Auto skinned mesh: one click → box/capsule/sphere/convex per bone.
- VHACD integration for automatic convex decomposition; can convert VHACD output to boxes/spheres/capsules; collider **merge** tools.
- Colliders can be placed on the object or on child "holder" objects (needed for rotated primitives).

**Take:** vertex-selection-to-collider workflow, rotated primitives via child GOs, per-bone skinned generation, merge/convert operations.

### 1.3 Technie Collider Creator 2 (Triangular Pixels) — $70, v1.3

Core idea: **paint triangles into named "hulls"**, each hull has a type; generation is reproducible from the painted data.

- Hull painter: paint faces, as many hulls as needed; hull types incl. box, sphere, capsule, convex hull, face, auto (decomposed).
- "Select all + auto hull", or mix auto with manual painting.
- Auto character setup for skinned meshes in one click.
- Handles awkward shapes (hollow, spindly, arches) via multiple hulls.
- Painting data persisted → regenerate anytime.

**Take:** paint mode, persistent authoring data (our `GenerationRecipe`), mixing auto + manual.

### 1.4 Mesh Collider Optimizer (Occlusionn) — $15

Core idea: **component that rebuilds a cheaper MeshCollider (or convex parts) automatically.** This is our model for criterion 2.

Pipeline (from their docs):
1. **Grid-based simplification (Burst job):** vertices bucketed into spatial cells → merged by averaging → triangles remapped → degenerate triangles dropped.
2. **Inflate/Deflate:** offset vertices along normals (+ out / − in) to fix penetration/clearance.
3. **Convex mode:** spatial decomposition with *Decomposition Grid* (2–6 per axis) + *Strict 256 Guard* (simplify parts aggressively to stay under PhysX's 256-polygon limit). Trigger mode auto-falls back to compound convex parts.
4. **Animated collider (runtime):** refresh from `BakeMesh` (accurate) or `SharedMesh` (cheap); `EveryFrame` / `EveryNFrames` / `TargetHz`; root-only and only-when-visible options. Not compatible with convex.
5. **BlendShape tracking / pose snapshot** in editor.
6. **LOD:** colliders follow active LOD; lower quality for lower LODs.
7. **UX:** Auto (re-optimize on setting change, with safety limits) vs Rebuild; Quality (1→0) + Shape Fidelity sliders; quality hint label; presets (High Quality / Balanced / Performance / Mobile) + saveable `.asset` presets; status line (mode, parts, verts, savings %, bake time); stats (verts/tris before→after, memory, bake time); **error heatmap** (per-vertex distance, threshold, sample density, error lines); save generated mesh as asset; cooking options; layer overrides; batch via `Tools` menu.

**Take:** almost all of it. **Improve on:** plain averaging (we use quadric-error representatives), fixed grid decomposition (we merge adjacent parts greedily and replace parts with primitives when they fit).

### 1.5 Feature matrix

| Feature | Ref | ECE | Technie | MCO | **Ours** |
|---|:-:|:-:|:-:|:-:|:-:|
| Primitive auto-fit (AABB) | ✓ | ✓ | ✓ | – | ✓ |
| Oriented / rotated primitives | – | ✓ | ✓ | – | ✓ |
| Convex hull | ✓¹ | ✓ | ✓ | ✓ | ✓ |
| Convex decomposition | – | ✓ (VHACD) | ✓ | ✓ (grid) | ✓ (grid + merge, Burst) |
| Mesh simplification | – | – | – | ✓ | ✓ (QEM clustering) |
| Decomposed parts → primitives | – | ✓ | – | – | ✓ (automatic) |
| Inflate / deflate | – | – | – | ✓ | ✓ |
| Vertex selection → collider | – | ✓ | – | – | ✓ |
| Triangle painting → collider | – | – | ✓ | – | ✓ |
| Persistent / regenerable data | – | – | ✓ | ✓ | ✓ |
| Skinned per-bone | – | ✓ | ✓ | – | ✓ |
| Runtime animated collider | – | – | – | ✓ | ✓ |
| LOD follow | – | – | – | ✓ | ✓ |
| Multi-object batch | – | partial | – | ✓ | ✓ (+ prefab assets) |
| Scene handles editing | – | partial | – | – | ✓ (full tool) |
| Stats + heatmap | – | – | – | ✓ | ✓ |
| Presets (assets) | – | – | – | ✓ | ✓ |
| Multi-collider bridge / events | – | – | – | tracked list | ✓ (`ColliderGroup`) |
| Scene validator (Doctor) | – | – | – | partial | ✓ |
| Undo-safe | – | ✓ | ✓ | ✓ | ✓ |

¹ full render mesh, no hull computation of its own (PhysX convex flag).

---

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Distribution | UPM package `Packages/com.fahril.autocollider/` | Clean versioning, asmdefs, git-installable |
| Convex decomposition | Own Burst C# (no VHACD) | No native binaries, all platforms, full control |
| UI tech | UI Toolkit + Overlays; IMGUI Handles only in scene | Modern, themable, clean look |
| Where colliders live | `[Colliders]` child container under the target | Doesn't clutter the source object; rotated primitives need child GOs anyway; easy to remove |
| Source of truth | `ColliderGroup` + `GenerationRecipe` | Non-destructive regenerate; ownership tracking |
| Mesh storage | Sub-assets in `AutoCollider/Generated/<Model>_Colliders.asset` | No inline mesh bloat in scenes, shared across prefab instances |

---

## 3. Package layout

```
Packages/com.fahril.autocollider/
├─ package.json                  deps: com.unity.burst 2.0.0, com.unity.collections 6.6.0, com.unity.modules.physics (Mathematics is an engine module in 6000.6)
├─ Runtime/   (AutoCollider.Runtime.asmdef)
│  ├─ ColliderGroup.cs
│  ├─ ColliderEntry.cs
│  ├─ GenerationSettings.cs      serializable settings (+ ColliderStrategy, MeshGranularity, ReplaceMode enums)
│  ├─ GenerationRecipe.cs        settings + painted regions (for regenerate)
│  ├─ PhysicsLimits.cs
│  ├─ ColliderRelay.cs
│  ├─ AnimatedMeshCollider.cs
│  └─ ColliderLodFollower.cs
├─ Editor/    (AutoCollider.Editor.asmdef → Runtime, Burst, Collections)
│  ├─ Core/          MeshSource, MeshGeometry, TargetSpace, Fitting/*, QuickHull3D, ConvexHull, GeometryUtils
│  ├─ Optimization/  ClusterSimplifier, MeshCleaner, InflateDeflate, ConvexDecomposer,
│  │                 PrimitiveReplacer, ErrorMetric, ColliderStats
│  ├─ Generation/    GenerationSettingsState, ColliderPreset, Strategies/*, GenerationPipeline,
│  │                 TargetResult, TargetResolver, BatchProcessor, ColliderWriter, GeneratedMeshStore
│  ├─ Editing/       ColliderEditTool, ColliderOverlay, ShapeHandles, ColliderPicker,
│  │                 PaintMode, SelectionOps (merge/mirror/convert/refit), Shortcuts
│  ├─ UI/            AutoColliderWindow, GenerateTab, ColliderGroupEditor, GenerateCommands, ColliderCost,
│  │                 StatsView, HeatmapDrawer, AutoColliderSettingsProvider, Strings, AutoColliderStyles, Uss/
│  └─ Doctor/        ColliderDoctor, Checks/*
├─ Tests/Editor/ (AutoCollider.Editor.Tests.asmdef)
├─ Samples~/Demo/
└─ Documentation~/
```

---

## 4. Data model

```csharp
// Editor — computed, not yet applied (pure data; drives preview and ColliderWriter)
internal enum ShapeKind { Box, Sphere, Capsule, ConvexMesh }      // ConcaveMesh added in M2

internal sealed class FittedShape {
    public ShapeKind Kind; public string Label;
    public float3 Center;            // target *scaled space* (see TargetSpace below)
    public quaternion Rotation;      // identity → lives on the container; else its own child GO
    public float3 Size;              // box
    public float Radius, Height;     // sphere / capsule
    public int Direction;            // capsule axis 0/1/2
    public ConvexHull Hull;          // convex mesh data (≤ 255 triangles)
    public float Volume;             // used to compare fits
}

// Runtime — serialized on ColliderGroup
[Serializable] public sealed class ColliderEntry {      // private serialized fields, public getters
    Collider Collider; string Label; int PartId;         // PartId: producing part (-1 = manual)
    bool Generated;                                      // ownership → only these are ever deleted by the tool
    bool Locked;                                         // regenerate / remove skip locked entries
}

[Serializable] public sealed class GenerationRecipe {
    GenerationSettings Settings;                         // exact settings used → Regenerate
    // M5: List<PaintedRegion> Regions (triangle indices + ShapeKind per region, Technie-style)
}
```

**Scaled space (`TargetSpace`).** All fitting happens in the target's frame with scale removed (position + rotation only, world-sized units).
Spheres and capsules are then correct under non-uniform scale: `ColliderWriter` divides by the lossy scale (sphere radius by the largest axis, capsule radius by the larger radial axis). Rotated primitives are only produced for uniformly scaled targets, because a rotated child under non-uniform scale would be sheared.

```
GameObject "Chair"                (source; MeshFilter/Renderer untouched)
├─ ColliderGroup                   ← the bridge (entries, recipe, events)
└─ [Colliders]                     container (same layer as root unless overridden)
   ├─ BoxCollider ×n               axis-aligned primitives + mesh colliders live here
   ├─ Seat (rotated) → BoxCollider rotated primitives get their own child GO
   └─ Leg_FL (rotated) → CapsuleCollider
```

---

## 5. Architecture layers

Dependencies flow downward only: UI/Editing → Generation → Optimization → Core. Runtime is referenced by all editor layers but references nothing editor.

### 5.1 Core (pure math)

- **`MeshSource`** — collects renderers (filters: children, inactive, LOD0, min size, name regex; skips containers and nested groups) and reads them into `MeshGeometry` (`float3[]` vertices + `int[]` triangles) in the target's scaled space. Uses `Mesh.AcquireReadOnlyMeshData` (works on Read/Write-disabled meshes in the editor); skinned meshes are baked (`BakeMesh(useScale: true)`) and the temporary mesh destroyed. `MeshGeometry.Combine` merges parts.
- **Fitters** (static classes in `Core/Fitting`; all enclosing, run on hull vertices):
  - `BoxFitter`: `Aabb`, `Pca` (covariance eigenvectors), `MinVolume` (each hull face normal + 2D min-area rectangle, vs PCA/AABB). Axes canonicalized to the rotation closest to identity.
  - `SphereFitter`: `Ritter` (fast), `Welzl` (minimal; iterative, fixed-seed shuffle → deterministic).
  - `CapsuleFitter`: `Along(axis)` = minimal enclosing circle of the projection for the radius + the shortest segment keeping every point inside the caps; `BestAxisAligned`, `BestOriented` (PCA axis).
  - Cylinder (N-sided convex mesh): later, with the convert operations in M4.
- **`QuickHull3D`** — double precision, deterministic; `TryBuildCapped` enforces **≤ 255 triangles** by clustering hull vertices on a growing grid (keeping the vertex furthest from the centroid per cell) and rebuilding. Burst port when M2 needs it.
- **`GeometryUtils`** — Jacobi eigen decomposition, covariance, 2D hull, min-area rectangle, mesh volume, axis canonicalization. Closest-point / ray-vs-shape helpers arrive with picking (M4).

### 5.2 Optimization (criterion 2)

See §6 for algorithm detail. Components:

| Class | Purpose |
|---|---|
| `ClusterSimplifier` | Burst job: grid vertex clustering with **quadric error** representative per cell |
| `MeshCleaner` | Weld by epsilon, drop degenerate/duplicate tris, optional remove islands < X% of bounds |
| `InflateDeflate` | Offset along angle-weighted normals |
| `ConvexDecomposer` | Voxel/grid split → hull per region → greedy merge of adjacent hulls → 256 guard |
| `PrimitiveReplacer` | Part → box/sphere/capsule when fill ratio ≥ threshold |
| `ErrorMetric` | Sampled source↔collider distance (max, mean); per-vertex for heatmap |
| `ColliderStats` | verts/tris before→after, part count, estimated memory, bake ms, savings % |

### 5.3 Generation (criterion 3)

- **`GenerationSettings`** (+ `ColliderPreset : ScriptableObject`): strategy, quality, shape fidelity, inflate, convex/decomposition grid, merge tolerance, primitive-replace threshold, include children, merge children, filters, placement, trigger, physic material, layer override, cooking options, LOD mode.
  Built-in presets: **High Quality, Balanced (default), Performance, Mobile**. Users can save their own.
- **Strategies** (`IColliderStrategy.Fit(FitInput, FitContext) → List<FittedShape>`; `FitInput` = deduped points + full hull computed once):
  | Strategy | Output |
  |---|---|
  | `BestPrimitive` ✅ | tightest of sphere / capsule / box by enclosed volume; cheaper shape wins within 2 % |
  | `Box` ✅ | AABB, or min-volume OBB when rotation is allowed and ≥ 3 % tighter |
  | `Sphere` ✅ | minimal enclosing sphere |
  | `Capsule` ✅ | best local axis, or PCA axis when rotation is allowed and ≥ 1 % tighter |
  | `ConvexHull` ✅ | one convex mesh (≤ 255 triangles); flat geometry falls back to a box |
  | `OptimizedMesh` | simplified concave MeshCollider (static/kinematic only) |
  | `ConvexDecomposition` | N convex parts (+ primitive replacement) |
  | `PerBone` | skinned: one shape per bone, parented to the bone |
  | **`Auto`** | picks per object (see §6.5) |
- **`GenerationPipeline`**: `Plan` (collect targets, read meshes) → `Compute` (strategies, pure data) → `Preview` (draw ghosts, no scene change) → `Apply` (`ColliderWriter`).
- **`BatchProcessor`**: targets = Hierarchy selection (scene objects; prefab instances become overrides). Project-window prefab/model assets: backlog. Options: include children, filters (name regex, layer mask, min bounds size, renderer enabled, LOD0 only, skip objects already having user colliders). Cancelable progress; **single Undo group**; per-object status (OK / Skipped(reason) / Warning) shown in the window.
- **`ColliderWriter`**: creates `[Colliders]` container, adds components via Undo, rotated primitives → child GO, registers `ColliderEntry`s, saves meshes through `GeneratedMeshStore`, handles prefab instances/assets per CLAUDE.md rule 7. "Replace mode": *Replace generated* (default) / *Append* / *Replace all (incl. user colliders, with confirm)*.
- **`GeneratedMeshStore`**: one `.asset` per source model with meshes as sub-assets, keyed by content hash → identical meshes reused across instances.

### 5.4 Runtime bridge — `ColliderGroup` (criterion 5)

One component that represents *all* colliders of an object as one unit, no matter how many boxes/hulls it has.

```csharp
public class ColliderGroup : MonoBehaviour {
    public IReadOnlyList<ColliderEntry> Entries { get; }
    public Bounds WorldBounds { get; }

    // bulk control — applies to every entry (or a filtered subset)
    public void SetEnabled(bool on);
    public void SetTrigger(bool on);
    public void SetLayer(int layer);
    public void SetPhysicsMaterial(PhysicsMaterial mat);
    public void SetLayerOverrides(LayerMask include, LayerMask exclude, int priority);
    public void SetProvidesContacts(bool on);
    public IEnumerable<ColliderEntry> Where(Func<ColliderEntry,bool> filter);

    // one event hub — the "bridge" for game code
    public event Action<ColliderEntry, Collision> CollisionEnter, CollisionStay, CollisionExit;
    public event Action<ColliderEntry, Collider>  TriggerEnter,  TriggerStay,  TriggerExit;

    public bool TryGetEntry(Collider c, out ColliderEntry entry);
}
```

- **Event routing:** if a `Rigidbody` exists on/above the group, callbacks arrive on the Rigidbody GO → resolve part via `collision.GetContact(i).thisCollider`. Without Rigidbody, each collider GO gets a lightweight `ColliderRelay` (added only when someone subscribes / option enabled) that forwards to the group.
- **Editor-facing:** inspector list, solo/hide, lock, regenerate from `GenerationRecipe`, optimize, remove generated.
- **Optional runtime components:**
  - `AnimatedMeshCollider` — `Source { BakeMesh, SharedMesh }`, `Update { EveryFrame, EveryNFrames, TargetHz }`, `OnlyWhenVisible`, `RootOnly`. Concave only (warn if convex).
  - `ColliderLodFollower` — with `LODGroup`, enables only entries tagged for the active LOD.

### 5.5 Scene editing (criterion 4)

**`ColliderEditTool : EditorTool`** (shows in the Tools overlay; activates for objects with `ColliderGroup` or any Collider). Paired **`ColliderOverlay`** (UI Toolkit Overlay) with 3 modes:

| Mode | What you do |
|---|---|
| **Select** | Click colliders in the viewport (ray vs shape math, cycles through overlaps on repeated click), box-select, Shift/Ctrl add/remove. Hover highlight. |
| **Edit** | Handles on selected collider(s): face handles resize (Alt = symmetric, Ctrl = snap), radius/height handles for sphere/capsule, move/rotate gizmo per collider, **V = snap to mesh vertex**. Multi-edit when several selected. |
| **Paint** | Brush/box-select triangles (or vertices) on the source mesh → "Create Box / Rotated Box / Sphere / Capsule / Hull from selection" or "Auto-decompose selection". Selection saved as a `PaintedRegion` in the recipe. |

**Operations** (overlay buttons + shortcuts + context menu): Duplicate, Delete, Mirror X/Y/Z (object-local), Merge selected → hull or box, Convert type (box ↔ capsule ↔ sphere ↔ hull, refit), Refit to vertices inside, Inflate/Deflate selected, Lock/Unlock, Solo/Hide, Rename.

**Visuals:** per-type color, selected = highlight color, X-ray (draw through geometry, faded), filled + wire mode, label overlay (name/type). All colors/handle size configurable in Project Settings.

**Shortcuts** (`[Shortcut]`, rebindable, context = tool active): `1/2/3` mode, `V` vertex snap, `Ctrl+D` duplicate, `Del` delete, `M` mirror, `G` merge, `H` solo, `Esc` exit tool.

Every operation is Undo-able as one step.

### 5.6 UI (criterion 1)

**`AutoColliderWindow`** — `Tools ▸ Auto Collider ▸ Open` (dockable):

```
┌ Auto Collider ─────────────────────────────────────────────┐
│ [Generate] [Optimize] [Edit] [Doctor]          ⚙ Settings  │
├────────────────────────────────────────────────────────────┤
│ Targets (5 selected)                    [+ Selection] [✕]  │
│  ✓ Chair         Box ×3        OK                          │
│  ✓ Table         Hull ×6       OK                          │
│  ⚠ Robot (skin)  PerBone       Mesh not readable [Fix]     │
│  … filters ▾                                               │
│ Preset  [ Balanced           ▾]  [Save as…]                │
│ Strategy [ Auto ▾ ]   Quality ━━━━●━━  Fidelity ━━●━━━━    │
│ ▸ Advanced (placement, layer, trigger, material, cooking)  │
├────────────────────────────────────────────────────────────┤
│ Est. result: 14 colliders · 1.2k tris (−87%)              │
│ [ Preview ]   [ Generate (5) ]   [ Remove generated (5) ]  │
└────────────────────────────────────────────────────────────┘
```

- **Optimize tab:** table of existing MeshColliders in selection/scene: name, tris before→after, parts, savings bar; batch "Optimize", heatmap toggle, "Save mesh as asset".
- **Edit tab:** collider list of the active `ColliderGroup` (mirrors inspector) + "Enter Edit Tool".
- **Doctor tab:** see §7.

**`ColliderGroupEditor`** (inspector):

```
▼ Collider Group
  12 colliders · 8 Box · 3 Capsule · 1 Hull   Cost ●●○○○
  [✎ Edit in Scene] [⟳ Regenerate] [⚡ Optimize] [🗑 Remove 12]
  ┌──────────────────────────────────────────────┐
  │ ■ Seat        Box      ✓on  ☐trig  👁  🔒     │
  │ ■ Leg_FL      Capsule  ✓on  ☐trig  👁  🔓     │
  │ …                                            │
  └──────────────────────────────────────────────┘
  ▸ Bulk: Layer [Default▾] Trigger ☐ Material [None] Contacts ☐
  ▸ Recipe (preset Balanced, Auto)    ▸ Stats    ▸ Runtime options
```

- **Project Settings ▸ Auto Collider** (`SettingsProvider`): default preset, generated-mesh folder, container name, colors, handle size, X-ray default, max auto-preview triangle budget.
- **Entry points:** `Tools/Auto Collider/…`, `GameObject/Auto Collider/Generate (Auto)` (hierarchy right-click, works on multi-selection), `Assets/Auto Collider/Generate for Prefabs`, shortcut `Shift+Alt+C` = generate Auto for selection.
- Styling: single `AutoCollider.uss` with theme variables; icons for each shape type; consistent spacing; everything tooltipped.

---

## 6. Key algorithms

### 6.1 Optimized concave mesh (static)

```
MeshData → Weld(ε) → ClusterSimplify(cell = f(Quality, bounds)) → Clean(degenerate, dup, islands)
         → InflateDeflate(d) → (stats, error metric) → MeshCollider(sharedMesh = stored asset)
```

**Cluster simplification with quadrics** (improves on MCO's averaging):
1. Grid over bounds; cell size from `Quality` (1 = tiny cells/no reduction, 0 = coarse).
2. For every triangle, compute its plane quadric `Q = p pᵀ` (area-weighted) and add it to the cells of its 3 vertices.
3. Per cell, representative = argmin `vᵀ Q v` (solve 3×3; if ill-conditioned, fall back to the mean). `Fidelity` blends mean ↔ QEM point and adds boundary/feature-edge weight so sharp edges survive.
4. Remap indices, drop triangles whose 3 vertices fall into ≤ 2 cells, drop duplicate triangles.
5. All steps as `IJobParallelFor`/`IJob` with Burst; hashing cells via `NativeParallelMultiHashMap<int3,int>`.

### 6.2 Convex hull (dynamic, single part)

QuickHull3D on (optionally pre-simplified) vertices → if faces > 255: reduce input via cluster simplification with growing cell size until ≤ 255 (the "256 guard") → convex `MeshCollider`.

### 6.3 Convex decomposition (dynamic, multi-part)

1. **Voxelize** surface + flood-fill interior at resolution from `DecompositionGrid` (2–6 → coarse cells; internally voxels per cell for accuracy).
2. **Initial split:** regions = occupied grid cells (like MCO); triangles clipped/assigned to cells.
3. **Hull per region** (QuickHull3D).
4. **Greedy merge:** for adjacent part pairs, compute merged hull; *concavity cost* = (hull volume − covered voxel volume) / hull volume. Merge cheapest pair while cost < `MergeTolerance` and part count > `MinParts`. → fewer, bigger parts than a fixed grid.
5. **256 guard** per part (as 6.2).
6. **`PrimitiveReplacer`:** for each part, fit OBB/sphere/capsule; if fill ratio ≥ `PrimitiveThreshold` (default 0.85) replace with the primitive (rotated → child GO).
7. Optional `MaxParts` cap (merge until satisfied).

### 6.4 Per-bone (skinned)

Assign each vertex to its highest-weight bone (threshold), skip bones with < N verts, fit chosen shape in **bone-local** space (capsule default along the bone direction), parent collider GO to the bone. Optional: mirror left/right by name pattern (`_L/_R`, `Left/Right`).

### 6.5 Auto strategy heuristic

```
if skinned → PerBone (capsules)
else:
  fit box(OBB), sphere, capsule, hull
  best primitive = min error; if fill ratio ≥ 0.85 → that primitive
  else if object (or parent) has non-kinematic Rigidbody →
       hull if hull fill ≥ 0.75 else ConvexDecomposition
  else (static) →
       hull if hull fill ≥ 0.9 else OptimizedMesh
```
Thresholds live in the preset so users can tune them.

### 6.6 Error metric / heatmap

Sample source vertices (stride = `SampleDensity`), distance to collider surface (primitive analytic distance or closest point on mesh triangles via BVH). Report max/mean; heatmap colors vertices green → red up to `ErrorThreshold`, optional error lines. Drawn with `Handles` in Scene view (cached mesh, not recomputed per repaint).

---

## 7. Extra recommendations (convenience)

1. **Auto strategy** — most users press one button; experts open "Advanced".
2. **Live ghost preview** before applying (pipeline is pure data until Apply) with estimated stats in the button area.
3. **Non-destructive + regenerable** — recipe stored on `ColliderGroup`; locked entries survive regenerate.
4. **Collider Doctor** (validator, Doctor tab + `Tools/Auto Collider/Run Doctor`), each issue has a one-click fix:
   - concave MeshCollider on non-kinematic Rigidbody → make hull / decompose
   - MeshCollider over triangle budget → optimize
   - convex > 255 polys risk → apply guard
   - negative / heavily non-uniform scale with sphere/capsule
   - visible renderers without any collider (optional check)
   - duplicate/overlapping identical colliders
   - meshes not readable but needed at runtime
5. **Mirror symmetry** for characters/vehicles (edit one side, mirror to the other).
6. **Prefab-asset batch** — select prefabs/models in Project window, generate without opening them.
7. **Team presets** as assets, default preset in Project Settings.
8. **Hierarchy badge** (optional) — small icon on objects that have a `ColliderGroup`, with collider count.
9. **Undo-safe everything**, one Ctrl+Z per action.

---

## 8. Roadmap

Each milestone ends compiling clean (zero console errors) with its tests green — see `CLAUDE.md` §5.

| M | Scope | Done when |
|---|---|---|
| **M0** Foundation | Package skeleton, `package.json` (+ burst/collections/physics deps), 3 asmdefs, `Strings`, settings provider stub, USS theme file, remove empty `Assets/Auto Collider Generator/` | Package compiles; Project Settings page visible |
| **M1** Core generation ✅ | `MeshSource`, AABB/PCA-OBB/min-OBB, Ritter/Welzl, capsule fitter, `QuickHull3D` (255 cap), strategies BestPrimitive/Box/Sphere/Capsule/ConvexHull, pipeline, `ColliderWriter`, `GeneratedMeshStore`, `ColliderGroup` (entries + ownership), `BatchProcessor` (selection, single Undo), window Generate tab, basic inspector | Generate for 10 selected objects, one Ctrl+Z reverts all; fitter tests pass |
| **M2** Optimization | `ClusterSimplifier` (Burst, QEM), `MeshCleaner`, `InflateDeflate`, `OptimizedMesh` strategy, stats, presets (4 built-in + save), Optimize tab, heatmap | Sample mesh tris −80% at Balanced with max error under threshold; deterministic |
| **M3** Decomposition | Voxelizer, `ConvexDecomposer` + greedy merge, 256 guard, `PrimitiveReplacer`, `Auto` strategy | Dynamic sample objects get convex parts all ≤255 faces; Auto picks sensible types on sample scene |
| **M4** Scene editing | `ColliderEditTool`, `ColliderOverlay`, picking, shape handles, move/rotate, vertex snap, ops (dup/delete/mirror/merge/convert/refit/lock/solo), shortcuts, inspector list sync | All ops Undo-able; edit a box face by drag; mirror works on rotated children |
| **M5** Paint & skinned | Paint mode (brush/box triangle select), create-from-selection, `PaintedRegion` in recipe + regenerate, `PerBone` strategy + mirror L/R | Paint 3 regions → regenerate reproduces same colliders; skinned sample gets per-bone capsules that follow animation |
| **M6** Runtime | `ColliderGroup` events + `ColliderRelay`, bulk API, `AnimatedMeshCollider`, `ColliderLodFollower` | Play-mode test: events fire with correct entry; animated collider throttles per settings |
| **M7** Polish | Collider Doctor + fixes, hierarchy badge, `Documentation~` manual, `Samples~/Demo`, perf pass (1k-object batch), UI polish both skins | Doctor finds & fixes seeded issues; docs + sample importable via Package Manager |

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Own decomposition quality below VHACD | Voxel-based merge cost + primitive replacement; expose resolution; keep strategy interface so a VHACD adapter can be added later |
| Burst compile time / editor stalls on huge meshes | Pre-simplify before hull/decomposition; preview triangle budget; cancelable progress; jobs complete off the main loop where possible |
| Prefab complexity (nested/variants) | Centralize in `ColliderWriter`; tests on prefab instance + asset; never edit through instance unintentionally |
| Non-uniform scale makes primitives wrong | Fit in owner-local space with lossy-scale compensation; Doctor warning |
| Inspector perf with hundreds of entries | UI Toolkit `ListView` virtualization; stats cached, recomputed on change only |
| PhysX limits change between Unity versions | Limits centralized in one `PhysicsLimits` class |

---

## 10. Sources

- Reference plugin: `Assets/Mystery Studio/Easy Collider/` (+ `Documentation.pdf`)
- [Easy Collider Editor — Unity Asset Store](https://assetstore.unity.com/packages/tools/physics/easy-collider-editor-67880)
- [Technie Collider Creator 2 — Unity Asset Store](https://assetstore.unity.com/packages/tools/physics/technie-collider-creator-2-217070)
- [Mesh Collider Optimizer — Unity Asset Store](https://assetstore.unity.com/packages/tools/physics/mesh-collider-optimizer-advanced-animation-collider-blendshape-c-354322)
- [Mesh Collider Optimizer docs — How it works / Modes / Inspector reference / Best practices / API](https://occlusionn.gitbook.io/docs)
- Lindstrom, P. (2000). *Out-of-Core Simplification of Large Polygonal Models* (vertex clustering with quadric error metrics).
