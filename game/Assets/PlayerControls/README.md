# Local co-op prototype

Open `Assets/Scenes/testScene.unity` and enter Play mode. This is also the first scene in Build Settings.
The camera is shared; the shooter template's networked player and managers are not used.

| Action | Keyboard and mouse | Gamepad |
|---|---|---|
| Join | Enter | Start / Menu |
| Move | WASD | Left stick |
| Aim | Mouse cursor | Right stick |
| Shoot | Left mouse button | Right trigger |
| Show aim line (hold) | Right mouse button | Left trigger |

Up to four players can join: one keyboard/mouse player and up to three gamepad players, or four gamepad players.
Each join pairs its devices to one `PlayerInput`; automatic scheme switching is disabled so controllers cannot take over another player.
Players spawn in different positions and use blue, red, orange, and white character materials. Aiming is independent of movement and keeps its last direction when a stick is released.

The handgun fires one shot per press, with a 0.5-second cooldown. Release and press again to fire another shot.
The Fire action uses **Press > Press And Release** so `PlayerInput` Send Messages delivers both edges and resets the trigger.
The handgun visual has a -90-degree Y rotation to align its +X barrel with the aim direction; the muzzle is positioned independently.
Shots deal 25 damage; each target board has 100 health and disappears after four hits. Walls and other players block shots.
Friendly fire is off by default. This first version has unlimited ammunition, with no reload, player death/respawn, enemies, or weapon switching.
Restart Play mode to restore the practice targets. The shared camera frames the enclosed 14-by-12-metre arena and adjusts its view for the window aspect ratio. Perimeter walls keep players inside.

## Mouse targeting and aim line

The camera ray through the cursor selects the nearest target or obstacle, ignoring the shooter. When it hits no surface,
the aim point falls on a horizontal plane at the current muzzle height. The character faces that point horizontally,
and the barrel and shot converge toward it from the offset, animated muzzle. Gamepad aiming retains the right-stick direction.
Nearby cover can still block the shot even when the camera can see the target.

Hold right mouse / left trigger to show a thin, muted blue aim line. It has no glow, uses 60% opacity, respects scene depth,
and disappears on release, disabling the player, or losing application focus. It does not cause damage.
The line and actual shot use the same `GetShotPath` calculation: first check chest-to-muzzle obstruction,
then sweep from the muzzle toward the aim point up to weapon range. Both checks use a 1 cm radius, matching the
2 cm tracer diameter, so glancing shots stop at cube edges. Each sweep first checks overlaps because Unity casts
skip colliders containing their origin. Shooter colliders and trigger volumes are ignored.
If the barrel clips through cover, the line starts at the chest and ends at that obstruction. Otherwise it starts
at the muzzle. A shot starting inside cover stops immediately. Clear shots beside or above low cover still pass;
the cubes are physical 3D obstacles, not infinitely tall walls.

`AimGuide` uses **Press > Press And Release** so both hold and release reach the Send Messages callback.
Arena floor colliders use **Ignore Raycast** (layer 2), excluded by the cursor-target mask so empty-floor aiming stays at gun height.
They still collide with players and bullets: the physical layer matrix and the weapon hit mask continue to include layer 2.
For another scene, put ground colliders on that layer or exclude a dedicated ground layer from `cursorTargetMask`.
The aim-line width and material can be tuned on `LocalPlayerShooting` in `LocalPlayer.prefab`.

## Reused template content

`LocalPlayerShooting` adapts the template's `SingleShotMechanism` trigger/cooldown and `HitscanShooting` raycast concepts.
It uses the existing handgun model, pistol audio clip, muzzle flash, impact effect, and tracer material.
The local scripts live in the `LocalGameplay` assembly, which references only Unity's Input System.
They do not use `NetworkBehaviour`, network ownership, RPCs, `ShooterAddon`, or the template's networked damage interface.
The original Core/Shooter templates and their package dependencies remain available for reference.

## Character and arena assets

`LocalPlayerVisuals` instantiates `Core/Art/Models/Armature.fbx` and drives the existing `ShooterAnimator.controller` with local movement velocity.
It blends idle/movement/strafe animations with the handgun upper-body pose, keeps root motion off, and uses the animated `Right_Hand_Attach` bone for the handgun grip.
The gun receives a small recoil offset on each accepted shot. Template animation events are disabled because they expect the template's networking and sound components.
If the imported character has no valid humanoid Avatar, the script warns once and uses a static pose with the
named hand attachment, while still initializing player colours and allowing movement/shooting. It only calls
Unity's humanoid bone API after validating the Avatar.

To restore animation after an Avatar import error, stop Play mode, select `Core/Art/Models/Armature.fbx`, and check
**Rig > Animation Type: Humanoid > Avatar Definition: Create From This Model**, then **Apply** and **Reimport**.
These settings are already checked in; if the error persists, inspect the model's Avatar configuration/import errors.
A blank Game view can have another cause: confirm the Main Camera is enabled and its **Culling Mask** includes
the arena/player layers. An empty **Camera** field on `PlayerInput` is expected with this shared-camera setup.

`LocalArena` assembles the floor and perimeter from `WallFloorKit`, cover from `BlockKit`, and boards from `Target/Pfb_Target.prefab` when Play starts.
The cover blocks are 2.2 m tall so they block standing handgun shots aimed at targets beyond them. The previous
1.4 m blocks could be cleared by shots aimed at higher points behind them, even when the cursor appeared beyond a cube.
`Cover Height` on the arena component changes both the visible block height and its collider, keeping the base on the floor.
Visual bounds are fitted to each piece's local box collider to account for the source prefabs' FBX axes and off-center pivots. Template target timelines are disabled; `LocalHealth` controls damage.
Select **Local Co-op Arena**, then use the **LocalArena > Rebuild Arena** component context menu for an editable preview outside Play mode.
The arena is rebuilt from its serialized layout on entering Play mode. Edit the component's cover/target positions, tile counts, and source prefabs to change the layout; direct edits to generated pieces are replaced on rebuild.

## Extending the prototype

- `LocalCoopSession` configures `PlayerInputManager`, the join buttons, spawn positions, and four-player limit.
- `LocalPlayer.prefab` holds movement, `PlayerInput`, local shooting, and the animated character driver. New instances use the camera tagged `MainCamera`; an explicit movement camera reference can override it.
- `LocalPlayerShooting` exposes damage, cooldown, range, hit layers, friendly fire, and cosmetic references in the Inspector.
- Add `LocalHealth` to an enemy/destructible root and a collider to it or a child to receive damage. Health is initialized once when instantiated.
- If adding player health, put `LocalHealth` on the player root; friendly fire remains disabled unless enabled on the weapon.

## Validation

Run `just test-local` from the repository root for the .NET shot-trigger regression checks.
In Unity, open **Window > General > Test Runner > PlayMode** and run `LocalGameplayTests` for joining, device isolation,
the player limit, damage, cursor aiming beyond arena cover from multiple sides, grazing edges, embedded shot origins, and friendly-fire checks.
The Unity PlayMode tests require the pinned Unity Editor and a valid license; they cannot be replaced by the .NET checks.
