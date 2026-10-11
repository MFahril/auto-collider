# Auto Collider

Unity editor tool to **generate, optimize and edit colliders** for one or many objects.

> Status: 0.4.0 — milestones M1 (generation), M2 (optimization) and M3 (convex decomposition + Auto) done. In-scene editing is next.

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
| **Auto** (default) | Per mesh: a primitive if one fits, else a convex hull, else convex parts (moving objects) or an optimized mesh (static) |
| Best Primitive | Tightest of box, sphere and capsule per mesh (cheapest physics) |
| Box | Box per mesh, rotated to fit when allowed |
| Sphere | Smallest enclosing sphere |
| Capsule | Capsule along the best axis |
| Convex Hull | Convex mesh ≤ 255 triangles (works with moving Rigidbodies) |
| Optimized Mesh | Simplified copy of the mesh, keeps holes and concave shapes (static / kinematic objects) |
| Convex Decomposition | Several convex parts (boxes / spheres / capsules where they fit) — concave shapes on moving Rigidbodies |

## Optimizing existing mesh colliders
**Window ▸ Optimize tab:** select objects (or choose *Whole Scene*), click **Analyze** to see triangles before → after, savings, accuracy and memory, turn on the **Error Heatmap** to see where the collider deviates, then **Optimize**. Optimized meshes are saved as assets and remember their original — **Restore Originals** puts it back.

Tune with **Quality** (fewer ↔ more triangles), **Shape Fidelity** (keeps corners and edges), **Max Triangles**, **Inflate** and **Min Island Size** — or pick a preset (*High Quality, Balanced, Performance, Mobile*) and save your own as a `Collider Preset` asset to share with your team.

## Settings
`Project Settings ▸ Auto Collider` — generated mesh folder, container name, default preset, preview budget, handle size and colors.
