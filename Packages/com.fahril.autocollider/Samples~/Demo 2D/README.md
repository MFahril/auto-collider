# Auto Collider — Demo 2D

Open **Auto Collider Demo 2D.unity**. The sprites, the flat gear mesh and the ground tilemap have no colliders yet, a
running character already has sprite-swap colliders, and three objects carry common 2D mistakes.

> Sprites use the default sprite material. The gear's material uses the Universal Render Pipeline (URP). In another
> pipeline it shows pink: convert it, or ignore it, the colliders are what matter here.

## 1. Find the problems: Collider Doctor
**Tools ▸ Auto Collider ▸ Run Doctor** lists:
- **Coin (duplicate colliders)**: two identical Circle Collider 2Ds. Fix: *Remove Duplicate*.
- **Ledge (no composite)**: a Tilemap Collider 2D where every tile is its own shape (objects catch on the seams). Fix:
  *Set Up Composite*.
- **Sign (tilted)**: a Box Collider 2D on an object rotated around X. 2D physics ignores that rotation, so the collider
  doesn't match the sprite you see. Rotate it around Z only.
- With **Report meshes without colliders** on, the props without colliders are listed too, each with a *Generate* fix.

## 2. Generate colliders
Select **Crate, Ring, Bracket, Star, Pill, Plank (rotated), Gear (flat mesh)** and **Grid ▸ Ground**, then press
**Shift+Alt+C** (or open **Tools ▸ Auto Collider ▸ Open Window** to preview first). With the default **Auto** settings:
- *Crate*: a Box Collider 2D; *Pill*: a Capsule Collider 2D; *Plank (rotated)*: a box that follows the rotation
- *Ring*: one polygon with a hole; *Bracket*, *Star*: polygons traced from the sprite's pixels
- *Gear (flat mesh)*: a 3D mesh moved by a Rigidbody2D, so it gets a 2D polygon of its silhouette
- *Ground*: a live Tilemap Collider 2D + Composite Collider 2D (follows tile edits). Pick **Tilemaps ▸ Baked** in the
  Generate tab's 2D section for one simplified polygon instead.

## 3. Edit them
Select a generated object and click **Edit in Scene**: resize, move, rotate (around Z), drag polygon points (**+** on an
edge adds one, **Ctrl+click** deletes), **Simplify**, mirror, merge or convert, or **Paint** (key 3) on a sprite.

## 4. Sprite-swap colliders
**Runner (sprite swap)** plays a three-frame run cycle from its Animator. Its **Sprite Collider Switcher** holds one
polygon per frame, baked from the Animator's clips, and swaps the Polygon Collider 2D's paths whenever the sprite changes
(flip X included). Change a frame and press **Bake Sprite Colliders** in its inspector to update them. Add one to your
own sprites with **GameObject ▸ Auto Collider ▸ Add Sprite Collider Switcher**.

## 5. Play
Press Play. The **Ball Dropper** drops balls on the props, and every object with a **Hit Reporter 2D** shows which part
was hit (top-left of the Game view and in the Console). It uses a single subscription per object:

```csharp
GetComponent<ColliderGroup>().CollisionEnter2D += (entry, collision) => Debug.Log($"{entry.Label} was hit");
```

## Skinned sprites
With Unity's **2D Animation** package installed, a sprite deformed by a Sprite Skin gets one collider per bone (Strategy
2D **Auto** or **Per Bone**), parented to the bones so the colliders follow the animation. The package isn't required by
Auto Collider, so this sample doesn't include a rigged character.
