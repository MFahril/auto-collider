# Auto Collider — Demo

Open **Auto Collider Demo.unity**. The props have no generated colliders yet, and three of them carry the kind of mistakes
real projects have, so you can see each part of the tool in action.

> The materials use the Universal Render Pipeline (URP). In another pipeline they show pink: convert them, or ignore it,
> the colliders are what matter here.

## 1. Find the problems: Collider Doctor
**Tools ▸ Auto Collider ▸ Run Doctor** lists:
- **Bracket (Rigidbody)**: a concave Mesh Collider on a moving Rigidbody. PhysX can't simulate that. Fix: *Make Convex*,
  or better, select it and generate **Convex Decomposition**.
- **Rock (heavy mesh collider)**: a 24,000-triangle concave collider. Fix: *Optimize* (or use the window's **Optimize** tab
  to compare before / after and see the error heatmap).
- **Crate (duplicate colliders)**: two identical Box Colliders. Fix: *Remove Duplicate*.

## 2. Generate colliders
Select **Table, Arch, Robot Arm, Pillar (LOD)** and press **Shift+Alt+C** (or open **Tools ▸ Auto Collider ▸ Open Window**
to preview first). With the default **Auto** strategy:
- *Table*: a box for the top and for each leg
- *Arch*: an optimized concave mesh (static object)
- *Robot Arm*: one capsule per bone, parented to the bones (it waves in Play mode and the colliders follow)
- *Pillar (LOD)*: turn on **Per LOD Colliders** in the Generate tab to get LOD-switched colliders

## 3. Edit them
Select a generated object and click **Edit in Scene**: resize faces, move / rotate, mirror, merge, convert, or switch to
**Paint** (key 3) and paint triangles into a collider of your own.

## 4. Play
Press Play. The **Ball Dropper** drops balls on the props, and every object with a **Hit Reporter** shows which part was
hit (top-left of the Game view and in the Console). It uses a single subscription per object:

```csharp
GetComponent<ColliderGroup>().CollisionEnter += (entry, collision) => Debug.Log($"{entry.Label} was hit");
```
