# Auto Collider — User Manual

Auto Collider generates, optimizes and edits colliders for one or many objects. It never touches your meshes, keeps every
change undoable, and only ever removes colliders it created itself.

**Contents**
1. [Install](#1-install)
2. [Quick start](#2-quick-start)
3. [The Collider Group](#3-the-collider-group)
4. [Generating colliders](#4-generating-colliders)
5. [Optimizing existing mesh colliders](#5-optimizing-existing-mesh-colliders)
6. [Editing colliders in the Scene view](#6-editing-colliders-in-the-scene-view)
7. [Painting colliders](#7-painting-colliders)
8. [Characters and skinned meshes](#8-characters-and-skinned-meshes)
9. [Runtime: events, animated and LOD colliders](#9-runtime-events-animated-and-lod-colliders)
10. [Collider Doctor](#10-collider-doctor)
11. [Settings reference](#11-settings-reference)
12. [Shortcuts and menus](#12-shortcuts-and-menus)
13. [Tips, limits and troubleshooting](#13-tips-limits-and-troubleshooting)

---

## 1. Install

Requirements: Unity **6000.6** or newer. Burst and Collections are pulled in automatically.

- **Git URL:** *Window ▸ Package Manager ▸ + ▸ Add package from git URL…*
  `https://github.com/MFahril/auto-collider.git?path=/Packages/com.fahril.autocollider`
- **Embedded:** copy the `com.fahril.autocollider` folder into your project's `Packages/` folder.
- **Demo:** *Package Manager ▸ Auto Collider ▸ Samples ▸ Demo ▸ Import* (URP materials). Its README walks through every feature.

The runtime part (`AutoCollider.Runtime`) has no editor dependencies, so player builds are unaffected; everything heavy
(fitting, simplification, decomposition) runs only in the editor.

## 2. Quick start

1. Select one or more objects in the Hierarchy.
2. Press **Shift+Alt+C**, or right-click ▸ **Auto Collider ▸ Generate Colliders**.
   For options and a live preview open **Tools ▸ Auto Collider ▸ Open Window**.
3. Each object gets a **Collider Group** listing its colliders. **Ctrl+Z** undoes the whole batch in one step.

The default **Auto** strategy picks the cheapest collider that fits each mesh: a box, sphere or capsule when one fits,
a convex hull for convex shapes, convex parts for concave shapes on moving Rigidbodies, an optimized mesh for static ones,
and per-bone colliders for skinned meshes. The window explains each choice in the result details.

## 3. The Collider Group

The **Collider Group** component is the "bridge" between all of an object's colliders and your game code.

- **Ownership.** Every collider Auto Collider creates is registered as *generated*. Regenerate and Remove only ever touch
  generated, unlocked colliders. Your own colliders stay put unless you pick *Replace All*.
- **Container.** Axis-aligned primitives and mesh colliders live on a `[Colliders]` child. Rotated primitives get their own
  child object, because Box, Sphere and Capsule colliders can't rotate. Your object's layer and static flags are copied
  to these objects, and your own objects are never changed.
- **Recipe.** The group remembers the settings (and painted regions) that built it, so **Regenerate** reproduces the
  result after you change a mesh.
- **Lock** a collider (inspector row, or **L** in the edit tool) to keep it through Regenerate. Hand-edited colliders
  lock automatically.

**Inspector**
- Summary with a cost estimate, then **Regenerate / Remove (N) / Edit in Scene / Open Window**.
- Collider list: on, trigger, lock and select. Click a row to select that collider in the Scene tool.
- **Bulk Edit:** make all triggers or all solid, layer, physics material, include / exclude layers with override
  priority, and contact reporting.
- **Runtime:** how to use the events, and *Add LOD Follower* when the object has a LOD Group.
- **Recipe:** the stored settings (read-only) and *Clear Regions*.

The Hierarchy shows a small collider-count badge next to every object with a group. You can turn it off in Project Settings.

## 4. Generating colliders

**Tools ▸ Auto Collider ▸ Open Window ▸ Generate.** The target list follows your selection, and children are included
automatically. **Live Preview** draws the result in the Scene view before anything is created.

### Strategies
| Strategy | Result | Best for |
|---|---|---|
| **Auto** | Picks per mesh (see above) | Almost everything |
| Best Primitive | Tightest of box / sphere / capsule | Simple props, cheapest physics |
| Box / Sphere / Capsule | That primitive, rotated when it's tighter | When you know the shape |
| Convex Hull | One convex mesh, at most 255 triangles | Convex props on Rigidbodies |
| Optimized Mesh | Simplified concave copy of the mesh | Static level geometry (keeps holes and arches) |
| Convex Decomposition | Several convex parts, each replaced by a primitive where one fits | Concave props on Rigidbodies |
| Per Bone | One collider per bone, parented to the bone | Characters (see §8) |

### Presets
**High Quality, Balanced, Performance and Mobile** set the optimization values. *Save Current as Preset…* stores the full
settings as a `Collider Preset` asset your team can share. Project Settings ▸ *Default Preset* sets what
*Reset to Defaults* uses.

### Settings
- **Shape:** Strategy; Granularity (*Per Renderer*: every mesh gets its own colliders; *Combined*: the whole object is
  fitted as one); Allow Rotation (ignored under non-uniform scale); Inflate (grow + / shrink − in meters).
- **Mesh Optimization:** Quality (fewer ↔ more triangles), Shape Fidelity (keeps corners and edges at the same triangle
  count), Max Triangles, Min Island Size (drops bolts and decals).
- **Skinned Meshes:** see §8.
- **Convex Decomposition & Auto:**
  - *Decomposition Resolution*: voxels along the longest side.
  - *Max Parts*.
  - *Concavity Tolerance*: how much empty space a merge may add.
  - *Primitive Fit Threshold*: how full a box / sphere / capsule must be to replace a part.
  - *Convexity Threshold*: when Auto uses one hull instead of parts.
- **Filters:**
  - *Include Children*, *Include Inactive*.
  - *LOD 0 Only*, and *Per LOD Colliders* (§9).
  - *Min Size*.
  - *Exclude Name Pattern* (regex).
- **Output:**
  - *Replace*: Replace Generated (default), Append, or Replace All (removes your colliders too).
  - *Is Trigger*.
  - *Physics Material*.

Generated collider meshes are saved as assets: one `.asset` per source model, deduplicated, in the folder set in Project
Settings. Scenes never hold large meshes inline.

## 5. Optimizing existing mesh colliders

**Window ▸ Optimize:**
1. Choose *Selection* or *Whole Scene*.
2. **Analyze** shows triangles before → after, savings, memory and accuracy for every Mesh Collider. Turn on the
   **Error Heatmap** to see where the simplified surface deviates (green → red up to the threshold).
3. **Optimize** replaces the meshes in one Undo step. Each optimized mesh remembers its original, and
   **Restore Originals** puts it back. Re-optimizing always starts from the original.

Convex colliders get a capped hull (at most 255 triangles). Concave colliders on moving Rigidbodies are refused, because
PhysX doesn't support them.

## 6. Editing colliders in the Scene view

Select an object with a Collider Group and click **Edit in Scene** (or pick the tool in the Tools overlay). An overlay
panel shows the modes, handles, the selection and every action.

- **Select (1):** click a collider (click again to cycle through overlapping ones). Drag a rectangle to box-select.
  Shift adds and Ctrl toggles.
- **Edit (2):** handles on the active collider.
  - **Resize (R):** drag a face; Alt resizes symmetrically, Ctrl snaps. Spheres and capsules have radius and height handles.
  - **Move (W)** and **Rotate (E)**.
  - Hold **V** to snap the dragged face (or the moved center) to the mesh vertex under the mouse.
- **Operations** (each one Undo step):
  - Duplicate (Ctrl+D), Delete (Del).
  - Mirror X / Y / Z (M). It also swaps Left/Right names.
  - Merge → convex hull (G) or box.
  - Convert → box / sphere / capsule / convex.
  - Refit: shrink-wrap to the mesh inside the collider.
  - Inflate / Deflate, Lock (L), Rename.
  - Hide, Solo (H), Show All (Alt+H), X-ray (X).

Rotating a box that sits on the shared container moves it onto its own child, because primitives can't rotate.
Edited colliders lock automatically (Project Settings ▸ *Lock After Edit*).

## 7. Painting colliders

Right-click ▸ **Auto Collider ▸ Paint Colliders**, or press **3** in the edit tool. This works on objects without
colliders too.

1. Drag over the mesh to paint triangles. The brush only paints faces turned towards you, so it doesn't reach the far
   side. **Ctrl+drag** erases, **[ / ]** change the brush size, **Esc** clears. *All*, *Connected* and *Grow* extend the
   selection.
2. Click **Auto / Box / Sphere / Capsule / Convex / Parts** to turn the painted triangles into colliders.

Each painted area is stored as a **region** on the group. Once a group has regions, Generate and Regenerate rebuild
exactly those colliders, plus the locked ones. Re-paint or delete regions in the overlay. **Clear Regions** in the
inspector returns the object to automatic generation.

## 8. Characters and skinned meshes

Generate with **Auto** or **Per Bone** on an object with a Skinned Mesh Renderer:
- Each vertex belongs to its strongest bone if that weight is at least *Bone Weight Threshold*. Bones with fewer than
  *Min Bone Vertices* get nothing (fingers, twist bones).
- Each bone gets a *Bone Shape* (capsule by default) fitted in the bone's bind pose. It is placed on a child of the bone,
  so it follows the animation at no runtime cost.
- *Symmetric Bones* gives Left/Right pairs (`Arm_L` / `Arm_R`, `LeftLeg` / `RightLeg`, …) the same size.

Removing the colliders also removes the child objects created under the bones, and nothing else.

## 9. Runtime: events, animated and LOD colliders

### Collision events
```csharp
using AutoCollider;

var group = GetComponent<ColliderGroup>();
group.CollisionEnter += (entry, collision) => Debug.Log($"{entry.Label} hit {collision.collider.name}");
group.TriggerEnter  += (entry, other)     => Debug.Log($"{other.name} entered {entry.Label}");
```
- Also available: `CollisionStay / CollisionExit` and `TriggerStay / TriggerExit`.
- **It works with or without a Rigidbody.** On the first subscription (in Play mode) small relay components are added to
  the objects that receive physics messages. Stay relays are added only if you use a Stay event, because Unity calls them
  every physics step.
- Each contact is reported **once**, with the **entry** (part) that was involved.
- After adding colliders at runtime, call `group.RefreshRouting()`.

**Bulk API:** `SetEnabled`, `SetTrigger`, `SetLayer`, `SetPhysicsMaterial`, `SetLayerOverrides(include, exclude,
priority)`, `SetProvidesContacts`, `Where(predicate)`, `TryGetEntry(collider, out entry)`, `WorldBounds`, `Entries`.

### Animated Mesh Collider
Right-click ▸ **Auto Collider ▸ Add Animated Mesh Collider** on a skinned mesh. At runtime the Mesh Collider follows the
animated surface (bones and blend shapes):
- **Mode:** *Bake Mesh* follows the current pose. *Shared Mesh* uses the bind pose and is refreshed only when the mesh is
  swapped, with no per-frame cost.
- **Update:** *Every Frame*, *Every N Frames* or *Target Hz*. **Only When Visible** skips frames where nobody sees it.
- Its inspector estimates the cost and offers fixes: *Make Convex* for moving Rigidbodies, *Enable Read/Write* for
  Shared Mesh mode.

Each refresh re-cooks the collider. Per-bone primitives are far cheaper for characters.

### LOD colliders
Turn on **Per LOD Colliders** (Generate ▸ Filters) for an object with a LOD Group:
- Colliders are fitted for every LOD level and a **Collider LOD Follower** is added.
- At runtime the follower enables only the active level's colliders. It mirrors Unity's LOD rule, including LOD bias and
  maximum LOD level.
- Culled objects keep their lowest-detail colliders, because physics still runs off screen.
- In the editor, LOD 0's colliders are on.

## 10. Collider Doctor

**Tools ▸ Auto Collider ▸ Run Doctor** (or the window's *Doctor* tab) scans the selection or every open scene:

| Problem | Severity | Fix |
|---|---|---|
| Concave Mesh Collider on a moving Rigidbody | Error | Make Convex |
| Concave Mesh Collider marked as trigger | Error | Make Convex |
| Mesh Collider without a mesh | Warning | Assign the object's mesh / Remove |
| Concave mesh collider over the triangle budget | Warning | Optimize |
| Duplicate identical collider on one object | Warning | Remove Duplicate |
| Collider Group entries whose collider was deleted | Warning | Clean Up |
| Animated Mesh Collider not set up / mesh not readable | Warning | Enable Read/Write |
| Convex mesh with more than 255 triangles | Info | Optimize (capped hull) |
| Negative scale on a Mesh Collider | Info | — |
| Your sphere / capsule under strongly non-uniform scale | Info | — |
| Visible mesh without any collider (optional) | Info | Generate |

Each fix is one Undo step, and **Fix All (N)** is a single step for everything.

## 11. Settings reference

**Project Settings ▸ Auto Collider** (stored in `ProjectSettings/`, shared through version control):
- **Generation:**
  - Generated mesh folder.
  - Container name.
  - Default preset.
  - Preview triangle budget.
- **Scene View:**
  - Handle size.
  - X-ray by default.
  - Lock after edit.
  - Show labels.
  - Fill opacity.
- **Doctor & Hierarchy:**
  - Hierarchy badge.
  - Doctor triangle budget.
  - Report meshes without colliders.
- **Colors:** box, sphere, capsule, mesh and selection colors.

The window's generation settings are per user (`UserSettings/`). Presets are how you share them.

## 12. Shortcuts and menus

All shortcuts can be rebound in *Edit ▸ Shortcuts ▸ Auto Collider*. Tool shortcuts only apply while the collider tool is
active.

| Key | Action |
|---|---|
| Shift+Alt+C | Generate colliders for the selection |
| 1 / 2 / 3 | Select / Edit / Paint mode |
| W / E / R | Move / Rotate / Resize handles |
| V (hold) | Snap to mesh vertex |
| [ / ] | Brush size |
| Ctrl+D / Del | Duplicate / Delete |
| M / G | Mirror X / Merge to hull |
| L | Lock |
| H / Shift+H / Alt+H | Solo / Hide / Show all |
| X | X-ray |
| Esc | Clear the paint / selection, then exit |

**Menus:**
- *Tools ▸ Auto Collider ▸* Open Window, Run Doctor, Project Settings…
- *GameObject ▸ Auto Collider ▸* (also on right-click in the Hierarchy) Generate Colliders, Remove Generated Colliders,
  Paint Colliders, Add Animated Mesh Collider.

## 13. Tips, limits and troubleshooting

- **Cost ranking:** sphere < capsule < box < convex mesh < concave mesh. Prefer primitives (Auto does).
- **PhysX limits:**
  - Convex meshes have at most 255 polygons (enforced).
  - Moving Rigidbodies need convex colliders.
  - Concave mesh colliders can't be triggers.
- **Non-uniform scale:** spheres and capsules are sized by the largest axis. Auto Collider compensates while fitting.
  Rotated primitives are only generated under uniform scale, because a rotated child would be sheared.
- **Repeated props** (the same mesh at the same scale) are fitted once per batch. 1,000 simple props take a few seconds.
- **Convex decomposition** takes a few seconds per mesh (managed hull merging). Lower *Decomposition Resolution* or
  *Max Parts* for faster results.
- **Read/Write:** generation doesn't need it. Only the Animated Mesh Collider in Shared Mesh mode does (one-click fix).
- **Prefabs:** instances get overrides you can apply. Open the prefab to change the asset itself.
- **Undoing a generation** can leave unused mesh sub-assets in the generated-mesh folder. They are harmless and you can
  delete the folder content when nothing references it.
