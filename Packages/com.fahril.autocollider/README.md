# Auto Collider

Unity editor tool to **generate, optimize and edit colliders** for one or many objects.

> Status: 0.6.0 — milestones M1 (generation), M2 (optimization), M3 (convex decomposition + Auto), M4 (scene editing) and M5 (painted colliders + skinned meshes) done. Runtime collision events and animated colliders are next.

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

## Optimizing existing mesh colliders
**Window ▸ Optimize tab:** select objects (or choose *Whole Scene*), click **Analyze** to see triangles before → after, savings, accuracy and memory, turn on the **Error Heatmap** to see where the collider deviates, then **Optimize**. Optimized meshes are saved as assets and remember their original — **Restore Originals** puts it back.

Tune with **Quality** (fewer ↔ more triangles), **Shape Fidelity** (keeps corners and edges), **Max Triangles**, **Inflate** and **Min Island Size** — or pick a preset (*High Quality, Balanced, Performance, Mobile*) and save your own as a `Collider Preset` asset to share with your team.

## Settings
`Project Settings ▸ Auto Collider` — generated mesh folder, container name, default preset, preview budget, handle size and colors.
