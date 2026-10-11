# Auto Collider

Unity editor tool to **generate, optimize and edit colliders** for one or many objects.

> Status: 0.2.0 — milestone M1 (core generation). Optimization, convex decomposition and in-scene editing are on the way.

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
| Best Primitive | Tightest of box, sphere and capsule per mesh (cheapest physics) |
| Box | Box per mesh, rotated to fit when allowed |
| Sphere | Smallest enclosing sphere |
| Capsule | Capsule along the best axis |
| Convex Hull | Convex mesh ≤ 255 triangles (works with moving Rigidbodies) |

## Settings
`Project Settings ▸ Auto Collider` — generated mesh folder, container name, preview budget, handle size and colors.
