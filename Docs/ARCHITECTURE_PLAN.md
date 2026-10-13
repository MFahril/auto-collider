# Auto Collider — Analysis, Architecture & Plan

> Status: **M0–M9 done — 1.2.0** (2D generation, editing, painting, tilemaps); 2D runtime planned as M10 (see §5.7, §8) — progress is tracked in [`ROADMAP.md`](ROADMAP.md).
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
│  └─ Doctor/        ColliderDoctor, DoctorIssue, DoctorCheck, DoctorSeverity
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
    public Transform Parent;         // M5: set → shape is in this transform's local space (bone colliders)
    public int RegionId = -1;        // M5: painted region that produced it
}

// Runtime — serialized on ColliderGroup
[Serializable] public sealed class ColliderEntry {      // private serialized fields, public getters
    Collider Collider; Collider2D Collider2D;            // exactly one is set (M8: 2D colliders)
    string Label; int PartId;                            // PartId: producing part (-1 = manual, ≥ 10000 = painted region)
    bool OwnsGameObject;                                 // GO was created by us (child of container / bone) → removal deletes it
    int Lod;                                             // M6: LOD level it was fitted for (-1 = every LOD) → ColliderLodFollower
    bool Generated;                                      // ownership → only these are ever deleted by the tool
    bool Locked;                                         // regenerate / remove skip locked entries
}

[Serializable] public sealed class GenerationRecipe {
    GenerationSettings Settings;                         // exact settings used → Regenerate
    List<PaintedRegion> Regions;                         // M5: Technie-style painted parts (see 5.5)
}

[Serializable] public sealed class PaintedRegion {     // id ≥ 10000 = PartId of its colliders
    int Id; string Label; Renderer Renderer; int[] Triangles; PartShape Shape;   // Auto/Box/Sphere/Capsule/ConvexHull/ConvexParts
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
  - `BoxFitter`: `Aabb`, `Pca` (covariance eigenvectors), `MinVolume` (hull face directions + 2D min-area rectangle, vs PCA/AABB; at most the 64 directions with the most hull area facing them — round hulls have hundreds of near-identical faces). Axes canonicalized to the rotation closest to identity.
  - `SphereFitter`: `Ritter` (fast), `Welzl` (minimal; iterative, fixed-seed shuffle → deterministic).
  - `CapsuleFitter`: `Along(axis)` = minimal enclosing circle of the projection for the radius + the shortest segment keeping every point inside the caps; `BestAxisAligned`, `BestOriented` (PCA axis).
  - Cylinder (N-sided convex mesh): later, with the convert operations in M4.
- **`QuickHull3D`** — double precision, deterministic; `TryBuildCapped` enforces **≤ 255 triangles** by clustering hull vertices on a growing grid (keeping the vertex furthest from the centroid per cell) and rebuilding — only once the cluster count can fit (a hull of V points has ≤ 2V − 4 triangles). Burst port when M2 needs it.
- **`GeometryUtils`** — Jacobi eigen decomposition, covariance, 2D hull, min-area rectangle, mesh volume, axis canonicalization. Closest-point / ray-vs-shape helpers arrive with picking (M4).

### 5.2 Optimization (criterion 2)

See §6 for algorithm detail. Components:

| Class | Purpose |
|---|---|
| `ClusterSimplifier` ✅ | Burst job: grid vertex clustering with a **quadric-error** representative per cell (pseudo-inverse solve around the cell mean) |
| `MeshCleaner` ✅ | Weld by epsilon, drop degenerate/duplicate tris, optional remove islands < X% of bounds |
| `InflateDeflate` ✅ | Offset meshes along angle-weighted normals; grow / shrink primitives analytically |
| `Voxelizer` ✅ | Burst: exact triangle/box surface voxels, flood-filled interior, surface samples |
| `ConvexDecomposer` ✅ | Block regions → hull per region → greedy merge by added empty space (min-heap) → 256 guard → primitive replacement |
| `PrimitiveReplacer` ✅ | Part → box / sphere / capsule when hull volume ÷ primitive volume ≥ threshold |
| `ErrorMetric` ✅ | Burst, parallel. Sampled source↔collider distance (max, mean); per-vertex for heatmap |
| `ColliderStats` ✅ | verts/tris before→after, part count, estimated memory, bake ms, savings % |
| `MeshOptimizer` ✅ | Pipeline: weld → islands → simplify → inflate → measure; `OptimizeConvex` = capped hull |
| `ColliderOptimizer` ✅ | Optimize **existing** MeshColliders: analyze → apply (one Undo step, mesh asset remembers its source) → restore original |

### 5.3 Generation (criterion 3)

- **`GenerationSettings`** (+ `ColliderPreset : ScriptableObject`): strategy, quality, shape fidelity, inflate, convex/decomposition grid, merge tolerance, primitive-replace threshold, include children, merge children, filters, placement, trigger, physic material, layer override, cooking options, LOD mode.
  Built-in presets: **High Quality, Balanced (default), Performance, Mobile** — they set only Quality / Shape Fidelity / Max Triangles. `ColliderPreset` assets store full settings (save from the window); Project Settings ▸ Default Preset drives "Reset to Defaults".
- **Strategies** (`IColliderStrategy.Fit(FitInput, FitContext) → List<FittedShape>`; `FitInput` = deduped points + full hull computed once):
  | Strategy | Output |
  |---|---|
  | `BestPrimitive` ✅ | tightest of sphere / capsule / box by enclosed volume; cheaper shape wins within 2 % |
  | `Box` ✅ | AABB, or min-volume OBB when rotation is allowed and ≥ 3 % tighter |
  | `Sphere` ✅ | minimal enclosing sphere |
  | `Capsule` ✅ | best local axis, or PCA axis when rotation is allowed and ≥ 1 % tighter |
  | `ConvexHull` ✅ | one convex mesh (≤ 255 triangles); flat geometry falls back to a box |
  | `OptimizedMesh` ✅ | simplified concave MeshCollider (static / kinematic only; dynamic Rigidbody → convex hull + warning) |
  | `ConvexDecomposition` ✅ | N convex parts (+ primitive replacement); stats for the whole set |
  | `PerBone` ✅ | skinned: one shape per bone, parented to the bone (§6.4); Auto uses it for skinned meshes |
  | **`Auto`** ✅ (default) | picks per mesh: primitive → hull → decomposition / optimized mesh (see §6.5) |
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

*Implemented in M6.*

- **Event routing (zero cost until used):** the first subscription to any event (play mode) adds a `ColliderRelay` to every object that receives physics messages for the tracked colliders — each collider's own object and its Rigidbody's object. `ColliderStayRelay` (Stay messages) is added only when `CollisionStay` / `TriggerStay` has a subscriber, because Unity calls Stay every step for every contact. `RefreshRouting()` covers colliders added at runtime.
  - *Collisions:* the part is exact — `Collision.thisCollider` (also on Exit, which has no contacts). Unity may deliver the message to the Rigidbody object and the collider object; only the canonical receiver (Rigidbody object, else the collider's object) is accepted, so nothing is reported twice.
  - *Triggers:* `OnTrigger*` only passes the other collider, and the shared container holds many colliders on one object, so `TriggerPairs` resolves the part: on Enter the receiver's unpaired candidate with the best overlap (`Physics.ComputePenetration` > bounds within contact offset), remembered per other collider; Exit takes the paired entry that no longer overlaps; Stay cycles through the pairs within a step. A Rigidbody object also receives its children's trigger messages — without an overlapping candidate of its own, the message is ignored there (the child's relay reports it).
  - `TryGetEntry` is a dictionary lookup (rebuilt after `OnAfterDeserialize` / when an entry was re-pointed).
- **Editor-facing:** inspector list, solo/hide, lock, regenerate from `GenerationRecipe`, optimize, remove generated; Bulk Edit (trigger, layer, material, layer overrides, contacts); *Runtime* foldout (events how-to, Add LOD Follower).
- **Optional runtime components:**
  - `AnimatedMeshCollider` — `AnimatedMeshSource { BakeMesh, SharedMesh }`, `ColliderUpdateMode { EveryFrame, EveryNFrames, TargetHz }` (pure `RefreshSchedule`), `OnlyWhenVisible`. Bakes with `useScale: true` into an owned mesh (destroyed with the component, original mesh restored); a target on another transform gets the vertices transformed. Re-assigns `null` first so the collider re-cooks. Shared Mesh refreshes only when the renderer's mesh is swapped. Added via `GameObject ▸ Auto Collider ▸ Add Animated Mesh Collider` (convex under moving Rigidbodies); its inspector warns about concave-on-dynamic and Read/Write with one-click fixes.
  - `ColliderLodFollower` — Unity has no API for the active LOD, so `LodMath` mirrors LODGroup's rule (world size ÷ distance ÷ tan(fov/2), × `QualitySettings.lodBias`, `maximumLODLevel`; orthographic uses the ortho size). Enables only entries whose `Lod` matches; a level without colliders uses the closest more detailed level; culled keeps the lowest-detail colliders (physics runs off screen). Fed by the **Per LOD Colliders** setting: the pipeline collects every level of the target's LOD Group, tags shapes / entries (`ColliderEntry.Lod`, serialized +1 so pre-M6 data means "every LOD"), generation leaves only LOD 0's colliders enabled and adds the follower.

### 5.5 Scene editing (criterion 4)

*Implemented in M4 (Paint mode in M5).*

**`ColliderEditTool : EditorTool`** — a component tool for `ColliderGroup` (in the Tools overlay when a group is selected; "Edit in Scene" in the inspector and the window's Edit tab activate it). Its targets are the selected groups, so the tool never changes Unity's `Selection`; collider selection lives in **`ColliderEditState`** (editor-only: selection, mode, handle mode, hide / solo, X-ray, labels). Paired **`ColliderOverlay`** (transient UI Toolkit overlay, visible only while the tool is active).

| Mode | What you do |
|---|---|
| **Select** (1) | Click colliders (exact `Collider.Raycast` per candidate; repeat-click cycles through overlaps), drag a rectangle to box-select (projected bounds centers), Shift add / Ctrl toggle, click empty to clear. A default control keeps clicks from reaching Unity's object picking. |
| **Edit** (2) | Same picking + handles on the active collider: **Resize** (R) — Unity bounds handles drawn in the shape's own frame (face drag, Alt symmetric, Ctrl snap; sphere radius; capsule radius + height); **Move** (W) / **Rotate** (E) — position / rotation handles. Hold **V** to snap the dragged face (or the moved center) to the source-mesh vertex under the mouse. |
| **Paint** (3) | Brush triangles on the source meshes → create a collider from them; saved as `PaintedRegion`s in the recipe (see *Painted regions* below). |

**Shapes ↔ colliders (`ColliderShapes`).** Every edit converts the live collider to a `FittedShape` in the group's world-sized frame (exact round trip incl. non-uniform scale), edits the shape, and writes it back with `ColliderOperations.Apply`: in place when the collider can express it (on the shared container when unrotated, or on its own child), otherwise rebuilt with the **same entry** re-pointed (e.g. rotating a box that lives on the container moves it onto its own child; mesh colliders `DetachToChild` — same mesh asset — before moving / rotating). Under non-uniform scale rotated primitives are refit axis-aligned (`EnsureRepresentable`) because a rotated child would shear.

**Operations (`ColliderOperations`, each exactly one Undo step):** Duplicate, Delete, Mirror X/Y/Z (plane through the group pivot; rotations S·R·S, meshes mirrored + re-wound, Left↔Right / _L↔_R names), Merge → convex / box, Convert → box / sphere / capsule / convex (fit around the current shape), Refit (shrink-wrap to the source vertices inside the collider + 10 %, same kind and orientation), Inflate / Deflate, Lock, Rename (+ dedicated child name). Editor-only: Hide, Solo, Show All. **Edited colliders are locked automatically** (Project Settings ▸ Lock After Edit) so Regenerate keeps hand edits.

**Undo discipline:** any Undo call (create / add / destroy) flushes pending `RecordObject`s, so the group is recorded **right before** each change (`ColliderWriter.RecordGroup`) and changed before the objects it referenced are destroyed.

**Visuals (`ColliderDrawing`):** per-type color, selection color + stronger fill, translucent fill (pushed out 0.2 % against z-fighting), X-ray (faded lines through geometry), labels (🔒 when locked), disabled colliders dimmed; mesh collider edges cached per mesh.

**Painted regions (M5, Technie-style).** `GameObject ▸ Auto Collider ▸ Paint Colliders` (or the window's Edit tab) adds a `ColliderGroup` if needed and opens Paint mode.
- *Brush:* `PaintMesh` holds each renderer's mesh in world space (read through `MeshSource` in the group's space, so painted indices are the same triangles the pipeline reads; rebuilt by `PaintMeshCache` when the renderer moves). The brush paints every **front-facing** triangle whose centroid lies within the screen radius and within a depth tolerance of the Möller–Trumbore hit, so it doesn't bleed through to the back. Ctrl+drag erases, `[` / `]` resize, Esc clears. *All / Connected / Grow* use welded-vertex adjacency.
- *Create:* Auto / Box / Sphere / Capsule / Convex / Parts → `ColliderOperations.CreateRegions` (one Undo step) stores a `PaintedRegion` per renderer and writes its colliders tagged with the region id. Fitting goes through **`RegionFitter`**, which the pipeline also uses — so creating and regenerating give identical colliders.
- *Regenerate:* a group with regions rebuilds **only** its regions (+ locked colliders); a locked collider of a region suppresses that region. Missing renderers / triangle indices beyond the current mesh produce warnings.
- *Drawing:* `PaintDrawing` tints painted triangles and each region (palette per region id) with GL triangles lifted slightly along the normal, plus the brush disc.

**Shortcuts:** an `IShortcutContext` registered while the tool is active (rebindable in Edit ▸ Shortcuts ▸ Auto Collider): `1/2/3` mode, `[` / `]` brush size, `W/E/R` handles, `V` (hold) vertex snap, `Ctrl+D` duplicate, `Del` delete, `M` mirror X, `G` merge → convex, `L` lock, `H` solo, `Shift+H` hide, `Alt+H` show all, `X` X-ray; `Esc` clears the selection, then exits. Edit-menu commands (Duplicate, Delete, Frame Selected, Select All) act on colliders while the tool is active.

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

### 5.7 2D (sprites, flat meshes, tilemaps, sprite skeletons)

*M8 (generation) and M9 (editing, painting, tilemaps) implemented; M10 (2D events, skeletons, sprite swaps, Doctor) planned.*

**One group for both kinds.** `ColliderEntry` holds a `Collider` **or** a `Collider2D` (generic `Component`, `IsAlive`, `Enabled`, `IsTrigger`, `Bounds`); the bulk API, lookup and inspector handle both. Unity refuses a `Collider2D` on a GameObject that has a 3D `Collider` (and vice versa), so 2D colliders live on their own container, `[Colliders 2D]` (`ColliderGroup.Container2D`); rotated 2D boxes / capsules get their own child rotated around Z.

**Routing** (`GenerationPipeline2D.Is2D`, per renderer): sprites → 2D unless `Dimension = 3D` (then skipped with a warning); meshes → 2D when `Dimension = 2D` or (Auto) a `Rigidbody2D` moves the target, else the normal 3D path. A target can produce both (`TargetResult.Shapes` + `Shapes2D`).

**Space.** The existing `TargetSpace` (target position + rotation, world-sized) with Z dropped: 2D colliders live in their object's XY plane. Shapes are fitted world-sized, the writer divides by `scale.xy` (circle by max |scale|, like Unity).

**Outlines** (`Editor/Core2D`) → `Outline2D` (loops: outer CCW, holes CW, even-odd fill):
- `SpriteSource`: Auto traces the opaque pixels — Unity's generated physics shapes / tight meshes are coarse enclosing polygons (a 64 px disc: 9 points, radius +16 %); falls back to the physics shape (custom shapes: pick *Physics Shape*), then the mesh boundary. Pixels are read through a temporary RenderTexture blit (no Read/Write). Sliced / tiled sprites use `renderer.size`. Per-batch cache by sprite + source settings.
- Flat meshes: `MeshSource.Read` triangles projected to XY, rasterized (Burst) and traced — the union silhouette.
- `OutlineTracer`: raster → directed boundary edges with the solid on the left; at a pinch vertex take the left turn (stays on the piece) so diagonal cells don't merge; collapse straight runs. `PolygonSimplifier`: closed Douglas–Peucker anchored at the lowest point + its farthest point; a loop that would self-intersect, cross an earlier loop or flip winding retries at half tolerance (5×), else stays unsimplified. Tolerance = diagonal × lerp(2 %, 0.15 %, Quality); islands < Min Island Size × diagonal are dropped.

**Editing (M9).** The edit tool works on `Component`s (a 3D `Collider` or a `Collider2D`); `ColliderOperations` has mixed-selection overloads that send the 3D part to the existing code and the 2D part to `ColliderOperations2D` inside one Undo group. `ColliderShapes2D` converts `Collider2D` ↔ `FittedShape2D` exactly (offset, size × |scale|, the host's angle around the group's Z, polygon paths through the host transform). `Apply` updates in place when the kind matches and the shape keeps the host's angle (polygons always), else rebuilds on `[Colliders 2D]` and re-points the same entry. Picking: ray ∩ the collider's plane, then `OverlapPoint`; edges are hollow and hit within a few pixels. Handles: Unity's bounds handles restricted to X / Y, `Handles.Disc` around Z, and point handles for polygons / edges (drag; "+" at edge midpoints inserts; Ctrl+click deletes; V snaps to the source outline). Tilemap / composite colliders are selectable but not shape-edited.

**Painting (M9).** Unity's sprite meshes are too coarse to paint (a disc is 8 vertices), so sprites paint on a grid of their opaque alpha cells (two triangles per cell, row-major — the indices regions store); flat meshes paint on their triangles. `RegionFitter2D` rasterizes the painted triangles, traces them and fits the region's shape (Box / Circle / Capsule / Convex Polygon / Polygon / Auto 2D); generation calls the same code, so regions regenerate identically.

**Tilemaps (M9, `TilemapSetup`).** *Composite* (default): `TilemapCollider2D` (composite operation Merge) + `CompositeCollider2D` (polygons) + a static `Rigidbody2D` when the tilemap has none — that body is a non-collider component the tool created, tracked in `ColliderGroup.OwnedComponents` and destroyed once no composite needs it. A user's own `TilemapCollider2D` is never touched (warning). *Baked*: the tiles are rasterized directly — grid tiles fill their cells exactly; sprite tiles use their physics shape through the tile's transform at 4 sub-cells — then traced and simplified (tolerance ≤ 0.2 % of the size, so corners survive) into one owned polygon with holes, or edge loops. Only rectangular grids bake; others fall back to Composite.

**Fitting** (`Strategies2D`): Box (AABB, or min-area rectangle when rotation is allowed and ≥ 3 % tighter), Circle (Welzl), Capsule (X / Y / principal axis; 4 % cap slack for pixel stair corners; near-circular capsules become circles), Convex Polygon (simplified hull), Polygon (simplified outline + holes), Edge (one closed `EdgeCollider2D` per loop). **Auto 2D:** best primitive (cheaper wins within 2 %) if it fills ≥ Primitive Fit Threshold of it → convex polygon if area ÷ hull area ≥ Convexity Threshold → simplified polygon (`PolygonCollider2D` decomposes it into convex pieces itself). Combined granularity unions the outlines on one raster. Inflate grows primitives analytically and offsets polygons (miter, clamped).

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
3. Per cell, representative = quadric minimizer **closest to the cell mean**: eigen-decompose the 3×3 part and pseudo-invert eigenvalues above 1e-3 × the largest (rank 3 → corner, rank 2 → onto the edge line, rank 1 → onto the plane), clamp to the cell. `Fidelity` blends mean ↔ this point. (A plain 3×3 inverse falls back to the mean on edges and rounds them off — measured 2× worse on a cube.)
4. Remap indices, drop triangles whose 3 vertices fall into ≤ 2 cells, drop duplicate triangles.
5. One Burst `IJob` (`CompileSynchronously`, single-threaded for deterministic cluster ids) with `NativeHashMap<int3,int>`; `Max Triangles` re-runs with 1.2× larger cells until the budget fits. Weld / island removal run before, inflate after; error is measured by a parallel Burst closest-point job.

### 6.2 Convex hull (dynamic, single part)

QuickHull3D on (optionally pre-simplified) vertices → if faces > 255: reduce input via cluster simplification with growing cell size until ≤ 255 (the "256 guard") → convex `MeshCollider`.

### 6.3 Convex decomposition (dynamic, multi-part)

*Implemented in M3 (`Voxelizer`, `ConvexDecomposer`, `PrimitiveReplacer`).*

1. **Voxelize** (Burst) at `Decomposition Resolution` voxels along the longest side (default 40): surface voxels by exact triangle/box overlap (SAT), interior by flood-filling the outside from a 2-voxel padding. Also emits surface samples (≤ ½ voxel apart) as hull points. Closed meshes become solid; open meshes a shell.
2. **Initial split:** 6 blocks per axis; each block's connected occupied voxels form a region (so separate pieces in one block never share a hull). Region points = corners of interior voxels (inside the mesh) + surface samples, reduced to their hull (≤ 128 points kept).
3. **Greedy merge** (deterministic min-heap on cost, ties by id): cost of merging adjacent A and B = *added empty space* = waste(hull(A∪B)) − waste(A) − waste(B), divided by the object's solid volume (waste = hull volume − estimated solid volume; solid = interior voxels + ½ surface voxels). Merge the cheapest pair while cost ≤ `Concavity Tolerance` (default 0.03); beyond that only while parts > `Max Parts`. Disconnected pieces never merge, so they always keep their own part.
4. **256 guard** per part (as 6.2).
5. **`PrimitiveReplacer`:** best of sphere / capsule / box by fill ratio (hull volume ÷ primitive volume; cheaper shape wins within 2 %); replace when ≥ `Primitive Fit Threshold` (default 0.85; 1 disables). Rotated primitives land on child GameObjects as usual.
6. Stats / error for the whole set are measured against the union of parts (primitives meshed by `ShapeMesher`) and attached to the first part.

Known limit: initial blocks don't align with the shape's features, so an inner corner can leave a small irregular hull between primitive parts (backlog: plane-cut refinement). Cost is dominated by managed QuickHull during merges (~1–2 s for ~150 regions; backlog: Burst QuickHull).

### 6.4 Per-bone (skinned)

*Implemented in M5 (`BoneFitter`, `GenerationPipeline.TryFitBones`).*

1. Read the shared mesh (bind pose — no `BakeMesh` needed) with `boneWeights` and `bindposes`.
2. `StrongestBones`: each vertex goes to its highest-weight bone if that weight ≥ `Bone Weight Threshold` (0.5); bones with < `Min Bone Vertices` (12) are skipped.
3. Transform the bone's vertices by `bindposes[bone]` → **bone-local** space and fit `Bone Shape` (capsule default, rotation allowed so it runs along the bone; Auto = best primitive by fill ratio, capsule fallback; convex shapes → hull).
4. The `FittedShape` carries `Parent = bone`; `ColliderWriter` puts the collider on a new child of the bone (`ColliderEntry.OwnsGameObject = true`) — the collider follows animation with no runtime cost, and removal deletes exactly those children.
5. `Symmetric Bones`: Left/Right pairs by name (`_L`/`_R`, `.L`/`.R`, `Left`/`Right` — same rules as Mirror) take the larger radius / height / size of the two.

Auto picks this for every `SkinnedMeshRenderer` with bone weights; meshes without weights fall back to the normal path.

### 6.5 Auto strategy heuristic

*Implemented in M3 (`AutoStrategy`, the default strategy), per mesh / part:*
```
hull = convex hull (null → flat → box)
best primitive (sphere / capsule / box, cheaper wins within 2 %);
if hull volume ÷ primitive volume ≥ PrimitiveFitThreshold (0.85) → that primitive
convexity = solid volume ÷ hull volume
          (exact signed volume when the welded mesh is watertight, else a 24-voxel estimate)
if convexity ≥ ConvexityThreshold (0.85)  → ConvexHull
else if moved by a non-kinematic Rigidbody → ConvexDecomposition
else                                       → OptimizedMesh
skinned meshes (SkinnedMeshRenderer) → PerBone, before any of the above (M5)
```
Both thresholds are `GenerationSettings` fields (so presets carry them). Auto adds a note per mesh ("Auto → Capsule.") shown in the window's result details and status tooltips.

### 6.6 Error metric / heatmap

Sample source vertices (stride = `SampleDensity`), distance to collider surface (primitive analytic distance or closest point on mesh triangles via BVH). Report max/mean; heatmap colors vertices green → red up to `ErrorThreshold`, optional error lines. Drawn with `Handles` in Scene view (cached mesh, not recomputed per repaint).

---

## 7. Extra recommendations (convenience)

1. **Auto strategy** — most users press one button; experts open "Advanced".
2. **Live ghost preview** before applying (pipeline is pure data until Apply) with estimated stats in the button area.
3. **Non-destructive + regenerable** — recipe stored on `ColliderGroup`; locked entries survive regenerate.
4. **Collider Doctor** ✅ (M7; `Editor/Doctor`, Doctor tab + `Tools/Auto Collider/Run Doctor`). `ColliderDoctor.Scan(roots | all loaded scenes, Options)` → `DoctorIssue`s (check, severity, target, message, optional fix), sorted by severity then hierarchy order; `FixOne` / `FixAll` wrap fixes in one Undo group.
   | Check | Severity | Fix |
   |---|---|---|
   | concave MeshCollider under a non-kinematic Rigidbody (looked up via `GetComponentInParent` — Unity never sets `attachedRigidbody` for that combination) | Error | Make Convex |
   | concave MeshCollider trigger (only reachable through serialized data; PhysX rejects it) | Error | Make Convex |
   | MeshCollider without mesh | Warning | Assign the MeshFilter mesh / Remove |
   | concave mesh over `DoctorTriangleBudget` (20k) | Warning | Optimize (`ColliderOptimizer`, restorable) |
   | convex mesh > 255 triangles | Info | Optimize (capped hull) |
   | duplicate identical collider on one object (type + parameters signature) | Warning | Remove Duplicate (also unregisters it) |
   | Collider Group entries with deleted colliders | Warning | Clean Up |
   | Animated Mesh Collider without refs / Shared Mesh on a non-readable mesh | Warning | Enable Read/Write |
   | negative scale on a MeshCollider | Info | — |
   | user sphere / capsule under ≥ 1.25× non-uniform scale (generated ones are compensated) | Info | — |
   | visible mesh without any collider (optional) | Info | Generate |
5. **Mirror symmetry** for characters/vehicles (edit one side, mirror to the other).
6. **Prefab-asset batch** — select prefabs/models in Project window, generate without opening them.
7. **Team presets** as assets, default preset in Project Settings.
8. **Hierarchy badge** ✅ (M7, `HierarchyBadge`, `hierarchyWindowItemByEntityIdOnGUI`) — collider icon + count on objects that have a `ColliderGroup`; toggle in Project Settings.
10. **Batch fit cache** ✅ (M7, `FitCache`) — inside a `BatchProcessor.Generate`, parts with identical geometry in target space (FNV-64 over vertex bits + indices), strategy, settings JSON and fit context are fitted once and cloned (`FittedShape.Clone`, shallow — later steps replace arrays, never write into them). Target position / rotation don't matter because fitting happens in the target's space; scale does.
9. **Undo-safe everything**, one Ctrl+Z per action.

---

## 8. Roadmap

Each milestone ends compiling clean (zero console errors) with its tests green — see `CLAUDE.md` §5.

| M | Scope | Done when |
|---|---|---|
| **M0** Foundation | Package skeleton, `package.json` (+ burst/collections/physics deps), 3 asmdefs, `Strings`, settings provider stub, USS theme file, remove empty `Assets/Auto Collider Generator/` | Package compiles; Project Settings page visible |
| **M1** Core generation ✅ | `MeshSource`, AABB/PCA-OBB/min-OBB, Ritter/Welzl, capsule fitter, `QuickHull3D` (255 cap), strategies BestPrimitive/Box/Sphere/Capsule/ConvexHull, pipeline, `ColliderWriter`, `GeneratedMeshStore`, `ColliderGroup` (entries + ownership), `BatchProcessor` (selection, single Undo), window Generate tab, basic inspector | Generate for 10 selected objects, one Ctrl+Z reverts all; fitter tests pass |
| **M2** Optimization ✅ | `ClusterSimplifier` (Burst, QEM), `MeshCleaner`, `InflateDeflate`, `OptimizedMesh` strategy, stats, presets (4 built-in + save), Optimize tab, heatmap | Sample mesh tris −80% at Balanced with max error under threshold; deterministic |
| **M3** Decomposition ✅ | Voxelizer, `ConvexDecomposer` + greedy merge, 256 guard, `PrimitiveReplacer`, `Auto` strategy | Dynamic sample objects get convex parts all ≤255 faces; Auto picks sensible types on sample scene |
| **M4** Scene editing ✅ | `ColliderEditTool`, `ColliderOverlay`, picking, shape handles, move/rotate, vertex snap, ops (dup/delete/mirror/merge/convert/refit/lock/solo), shortcuts, inspector list sync | All ops Undo-able; edit a box face by drag; mirror works on rotated children |
| **M5** Paint & skinned ✅ | Paint mode (brush triangle select, grow / connected), create-from-selection, `PaintedRegion` in recipe + regenerate, `PerBone` strategy + mirror L/R | Paint 3 regions → regenerate reproduces same colliders; skinned sample gets per-bone capsules that follow animation |
| **M6** Runtime ✅ | `ColliderGroup` events + `ColliderRelay`, bulk API, `AnimatedMeshCollider`, `ColliderLodFollower` | Play-mode test: events fire with correct entry; animated collider throttles per settings |
| **M7** Polish ✅ | Collider Doctor + fixes, hierarchy badge, `Documentation~` manual, `Samples~/Demo`, perf pass (1k-object batch), UI polish both skins | Doctor finds & fixes seeded issues; docs + sample importable via Package Manager |
| **M8** 2D core ✅ | `ColliderEntry` holds `Collider` or `Collider2D`; `Dimension` / `Strategy2D` / outline settings; `Core2D` (sprite / flat-mesh outlines, Burst raster + marching squares, Douglas–Peucker, box / circle / capsule 2D fitters); `GenerationPipeline2D` + `ColliderWriter2D`; 2D preview, inspector rows | Sprites / flat meshes get fitting 2D colliders in one Undo step |
| **M9** 2D editing ✅ | `ColliderShapes2D`, 2D ops + handles + polygon point editing, 2D painting, tilemaps (Composite / Baked) | Every 2D collider editable, one Undo per action; tilemaps set up or baked in one click |
| **M10** 2D runtime | 2D group events + relays (`otherCollider`, `IsTouching`), sprite skeletons (`BoneFitter2D`, SpriteSkin via reflection), `SpriteColliderSwitcher`, Doctor 2D, Demo2D sample, docs | Events report the right part; Doctor fixes seeded 2D issues; sample plays |

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
