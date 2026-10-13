# Auto Collider

Unity editor tool to **generate, optimize and edit colliders** for one or many objects.

> **1.3.0** — full **2D support**: colliders for sprites, flat meshes, tilemaps and skinned sprites, Scene-view editing, 2D collision events, sprite-swap colliders and 2D Doctor checks. Full user manual in [`Documentation~/index.md`](Documentation~/index.md); **Demo** and **Demo 2D** samples are available in the Package Manager (*Samples*).

## Requirements
- Unity 6000.6 or newer
- Burst and Collections (resolved automatically)

## Install
- **Embedded:** copy this folder into your project's `Packages/` folder.
- **Git URL:** Package Manager ▸ *Add package from git URL…* ▸
  `https://github.com/MFahril/auto-collider.git?path=/Packages/com.fahril.autocollider`

## Quick start
1. Select one or more objects in the Hierarchy.
2. Press **Shift+Alt+C** — or right-click ▸ **Auto Collider ▸ Generate Colliders**, or open **Tools ▸ Auto Collider ▸ Open Window** for options and a live preview.
3. Each object gets a **Collider Group** component listing its colliders. Use it to toggle, lock, regenerate or remove them. One **Ctrl+Z** undoes a whole batch.

## Strategies
| Strategy | Result |
|---|---|
| **Auto** (default) | Per mesh: a primitive if one fits, else a convex hull, else convex parts (moving objects) or an optimized mesh (static); skinned meshes get per-bone colliders |
| Best Primitive | Tightest of box, sphere and capsule per mesh (cheapest physics) |
| Box | Box per mesh, rotated to fit when allowed |
| Sphere | Smallest enclosing sphere |
| Capsule | Capsule along the best axis |
| Convex Hull | Convex mesh ≤ 255 triangles (works with moving Rigidbodies) |
| Optimized Mesh | Simplified copy of the mesh, keeps holes and concave shapes (static / kinematic objects) |
| Convex Decomposition | Several convex parts (boxes / spheres / capsules where they fit) — concave shapes on moving Rigidbodies |
| Per Bone | Skinned meshes: one collider per bone, parented to the bone so it follows the animation |

## 2D: sprites and flat meshes
Select sprites and press **Shift+Alt+C** — they get **2D colliders** (Box / Circle / Capsule / Polygon / Edge Collider 2D) in the same Collider Group, on a `[Colliders 2D]` child. With **Auto**, each sprite gets the cheapest shape that fits its opaque pixels: a box, circle or capsule when one fills it, else one convex polygon, else a simplified outline polygon (holes included).

- Outlines are traced from the sprite's pixels (no Read/Write needed). Pick *Physics Shape* to use a custom physics shape drawn in the Sprite Editor.
- Meshes get 2D colliders too when a **Rigidbody2D** moves them, or when **Dimension** is *2D* — their silhouette seen along the object's Z axis (2.5D games with 3D art).
- *Generate ▸ 2D* sets Dimension, the 2D strategy, outline source, alpha threshold, outline resolution, a Physics Material 2D and how tilemaps get colliders. Quality controls outline detail.
- **Tilemaps**: *Composite* sets up Unity's live TilemapCollider2D + CompositeCollider2D (follows tile edits); *Baked* traces the tiles into one simplified polygon (holes included) — far fewer points.
- **Edit in Scene** works on 2D colliders too: resize / move / rotate (around Z), drag polygon points (click **+** on an edge to add one, **Ctrl+click** to delete, **Simplify** in the overlay), and every operation (duplicate, mirror, merge, convert, refit, inflate, lock). **Paint** works on sprites as well.
- **Skinned sprites** (2D Animation package): one collider per bone, parented to the bones (Strategy 2D *Auto* / *Per Bone*).
- **Sprite Collider Switcher** (`GameObject ▸ Auto Collider ▸ Add Sprite Collider Switcher`): for frame-by-frame animation, bakes one polygon per sprite from the Animator clips and swaps the collider's paths when the sprite changes (no runtime tracing).
- **2D events**: `CollisionEnter2D` / `TriggerEnter2D` (+ Stay / Exit) on the Collider Group report the 2D part that was hit.
- The **Collider Doctor** checks 2D setups too (over-detailed polygons, tilemaps without a composite, 2D / 3D physics mixed under one Rigidbody, tilted 2D objects, duplicates, sprites without colliders).

## Editing colliders in the Scene view
Select an object with a **Collider Group** and click **Edit in Scene** (or pick the tool in the Tools overlay). Click colliders to select them (click again to cycle through overlaps, drag to box-select), then:
- **Resize** (R) with the face handles — Alt = symmetric, hold **V** to snap a face to a mesh vertex.
- **Move** (W) / **Rotate** (E).
- **Duplicate** (Ctrl+D), **Delete** (Del), **Mirror** X / Y / Z (M), **Merge** into a convex hull or box (G), **Convert** to another shape, **Refit** (shrink-wrap to the mesh), **Inflate / Deflate**, **Lock** (L), **Solo** (H), **X-ray** (X).

Every operation is one Ctrl+Z. Colliders you edit are locked so *Regenerate* keeps them.

## Painting colliders
Right-click an object ▸ **Auto Collider ▸ Paint Colliders** (or press **3** in the edit tool). Drag over the mesh to paint triangles — **Ctrl+drag** erases, **[** / **]** change the brush, **Esc** clears; *All*, *Connected* and *Grow* extend the selection. Then click **Auto / Box / Sphere / Capsule / Convex / Parts** to turn the painted area into a collider.

Each painted area is saved as a **region** on the Collider Group, so *Regenerate* rebuilds exactly the same colliders (and keeps any you locked). Regions are listed in the paint panel, where you can re-paint or delete them; *Clear Regions* in the inspector returns the object to automatic generation.

## Skinned meshes (characters)
Generate on a character with **Auto** or **Per Bone**: every bone gets its own collider (a capsule by default), placed on a child of the bone so it follows the animation. The **Skinned Meshes** foldout sets the bone shape, the weight threshold, the minimum vertices per bone and **Symmetric Bones** (left / right pairs such as `Arm_L` / `Arm_R` get the same size).

## Collision events in game code
A **Collider Group** is one place to listen to all of an object's colliders — the event tells you which part was hit:

```csharp
var group = GetComponent<ColliderGroup>();
group.CollisionEnter += (entry, collision) => Debug.Log($"{entry.Label} hit {collision.collider.name}");
group.TriggerEnter  += (entry, other)     => Debug.Log($"{other.name} entered {entry.Label}");
```

It works with or without a Rigidbody (small relay components are added automatically the first time you subscribe, in play mode). There are also Stay / Exit events and bulk controls: `SetEnabled`, `SetTrigger`, `SetLayer`, `SetPhysicsMaterial`, `SetLayerOverrides`, `SetProvidesContacts`, `Where(...)`, `WorldBounds`.

## Animated and LOD colliders
- **Animated Mesh Collider** (`GameObject ▸ Auto Collider ▸ Add Animated Mesh Collider`): keeps a mesh collider in sync with an animated skinned mesh. Refreshes are throttled (every frame / every N frames / N times per second) and can skip frames where the mesh isn't visible. Per-bone colliders are much cheaper for characters — use this when the exact animated surface matters.
- **Per LOD Colliders** (Generate ▸ Filters): fits colliders for every LOD level and adds a **Collider LOD Follower**, which enables only the active level's colliders at runtime — distant objects use their cheaper colliders.

## Optimizing existing mesh colliders
**Window ▸ Optimize tab:** select objects (or choose *Whole Scene*), click **Analyze** to see triangles before → after, savings, accuracy and memory, turn on the **Error Heatmap** to see where the collider deviates, then **Optimize**. Optimized meshes are saved as assets and remember their original — **Restore Originals** puts it back.

Tune with **Quality** (fewer ↔ more triangles), **Shape Fidelity** (keeps corners and edges), **Max Triangles**, **Inflate** and **Min Island Size** — or pick a preset (*High Quality, Balanced, Performance, Mobile*) and save your own as a `Collider Preset` asset to share with your team.

## Collider Doctor
**Tools ▸ Auto Collider ▸ Run Doctor** checks the selection or the open scenes for collider mistakes that break or slow down physics — concave colliders on moving Rigidbodies, over-detailed mesh colliders, missing meshes, duplicates, broken Collider Group entries and more — and fixes them in one click (each fix can be undone; *Fix All* is a single step).

## Settings
`Project Settings ▸ Auto Collider` — generated mesh folder, container name, default preset, preview budget, handle size, Hierarchy badge, Doctor budget and colors.
