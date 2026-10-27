using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class LocalGameplayTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly List<InputDevice> devices = new List<InputDevice>();
    private readonly List<Object> assets = new List<Object>();
    private PlayerInput mousePlayer;
    private Mouse aimingMouse;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = PlayerInput.all.Count - 1; i >= 0; i--)
            Object.Destroy(PlayerInput.all[i].gameObject);
        foreach (var obj in objects)
            if (obj != null)
                Object.Destroy(obj);
        foreach (var asset in assets)
            if (asset != null)
                Object.Destroy(asset);
        yield return null;
        foreach (var device in devices)
            if (device.added)
                InputSystem.RemoveDevice(device);
        objects.Clear();
        devices.Clear();
        assets.Clear();
    }

    [UnityTest]
    public IEnumerator MissingAndGenericAvatarsUseStaticHandAttachmentWithoutThrowing()
    {
        foreach (bool useGenericAvatar in new[] { false, true })
        {
            var source = new GameObject("Test character model");
            objects.Add(source);
            var hand = new GameObject("Right_Hand_Attach").transform;
            hand.SetParent(source.transform, false);
            hand.localPosition = new Vector3(0.3f, 1.2f, 0.5f);
            var sourceAnimator = source.AddComponent<Animator>();
            if (useGenericAvatar)
            {
                var avatar = AvatarBuilder.BuildGenericAvatar(source, "");
                assets.Add(avatar);
                Assert.That(avatar.isHuman, Is.False);
                sourceAnimator.avatar = avatar;
            }
            var sourceMaterial = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Body" };
            var paletteMaterial = new Material(sourceMaterial) { name = "Player colour" };
            assets.Add(sourceMaterial);
            assets.Add(paletteMaterial);
            source.AddComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { sourceMaterial };
            var animation = new AnimatorOverrideController();
            assets.Add(animation);

            var player = new GameObject("Test player with invalid avatar");
            objects.Add(player);
            player.SetActive(false);
            var visuals = player.AddComponent<LocalPlayerVisuals>();
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(LocalPlayerVisuals).GetField("characterModel", fields).SetValue(visuals, source);
            typeof(LocalPlayerVisuals).GetField("animationController", fields).SetValue(visuals, animation);
            typeof(LocalPlayerVisuals).GetField("bodyMaterials", fields).SetValue(visuals, new[] { paletteMaterial });
            var mount = new GameObject("Test handgun").transform;
            mount.SetParent(player.transform, false);
            visuals.SetWeaponMount(mount);

            LogAssert.Expect(LogType.Warning, new Regex("^LocalPlayerVisuals: Character has no valid humanoid Avatar;"));
            player.SetActive(true);
            yield return null;
            yield return null;
            Assert.That(visuals.Animator.enabled, Is.False);
            Assert.That(visuals.Animator.runtimeAnimatorController, Is.Null);
            Assert.That(visuals.Hand, Is.SameAs(player.transform.Find("Character/Right_Hand_Attach")));
            Assert.That(Vector3.Distance(mount.position, visuals.Hand.position), Is.LessThan(0.001f));
            Assert.That(player.transform.Find("Character").GetComponent<SkinnedMeshRenderer>().sharedMaterials[0], Is.SameAs(paletteMaterial));
        }
    }

    [UnityTest]
    public IEnumerator GamepadsJoinAndControlOnlyTheirOwnPlayer()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        var first = InputSystem.AddDevice<Gamepad>();
        var second = InputSystem.AddDevice<Gamepad>();
        devices.Add(first);
        devices.Add(second);
        yield return PressStart(first);
        yield return PressStart(second);

        Assert.That(PlayerInput.all.Count, Is.EqualTo(2));
        var one = PlayerInput.FindFirstPairedToDevice(first);
        var two = PlayerInput.FindFirstPairedToDevice(second);
        Assert.That(one, Is.Not.Null);
        Assert.That(two, Is.Not.Null);
        Assert.That(one, Is.Not.SameAs(two));
        Assert.That(one.currentControlScheme, Is.EqualTo("Gamepad"));
        Assert.That(two.devices.Count, Is.EqualTo(1));
        Assert.That(two.devices[0], Is.SameAs(second));

        Vector3 oneBefore = one.transform.position;
        Vector3 twoBefore = two.transform.position;
        InputSystem.QueueStateEvent(first, new GamepadState { leftStick = Vector2.right });
        yield return new WaitForSeconds(0.1f);
        Assert.That(one.transform.position.x, Is.GreaterThan(oneBefore.x + 0.05f));
        Assert.That(two.transform.position.x, Is.EqualTo(twoBefore.x).Within(0.01f));
        Assert.That(two.transform.position.z, Is.EqualTo(twoBefore.z).Within(0.01f));

        var oneVisual = one.GetComponent<LocalPlayerVisuals>();
        var twoVisual = two.GetComponent<LocalPlayerVisuals>();
        Assert.That(oneVisual.Animator.isHuman, Is.True);
        Assert.That(oneVisual.Animator.applyRootMotion, Is.False);
        Assert.That(oneVisual.Hand, Is.Not.Null);
        var skinOne = one.GetComponentInChildren<SkinnedMeshRenderer>();
        var skinTwo = two.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.That(skinOne, Is.Not.Null);
        Assert.That(skinTwo, Is.Not.Null);
        Assert.That(skinOne.sharedMaterials[0], Is.Not.SameAs(skinTwo.sharedMaterials[0]));
        Assert.That(Vector3.Distance(one.transform.Find("Handgun").position, oneVisual.Hand.position), Is.LessThan(0.01f));

        // Rejoining with an already paired controller must not duplicate its player.
        yield return PressStart(first);
        Assert.That(PlayerInput.all.Count, Is.EqualTo(2));

        InputSystem.QueueStateEvent(first, new GamepadState { leftTrigger = 1f });
        yield return null;
        yield return null;
        Assert.That(one.transform.Find("Aim guide").GetComponent<LineRenderer>().enabled, Is.True);
        Assert.That(two.transform.Find("Aim guide").GetComponent<LineRenderer>().enabled, Is.False);
        InputSystem.QueueStateEvent(first, new GamepadState());
        yield return null;
        yield return null;
        Assert.That(one.transform.Find("Aim guide").GetComponent<LineRenderer>().enabled, Is.False);
    }

    [UnityTest]
    public IEnumerator ArenaHasFittedKitCollidersAndThreeDamageableTargets()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        yield return null;
        var arena = Object.FindFirstObjectByType<LocalArena>();
        Assert.That(arena, Is.Not.Null);
        var geometry = arena.transform.Find("Arena Geometry");
        Assert.That(geometry, Is.Not.Null);
        Assert.That(geometry.GetComponentsInChildren<LocalHealth>().Length, Is.EqualTo(3));
        foreach (Transform piece in geometry)
        {
            var box = piece.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null);
            var renderers = piece.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            Assert.That(Vector3.Distance(bounds.center, box.bounds.center), Is.LessThan(0.01f), piece.name);
            Assert.That(Vector3.Distance(bounds.size, box.bounds.size), Is.LessThan(0.01f), piece.name);
        }
        Assert.That(Camera.main.orthographic, Is.True);
        foreach (var corner in new[] { new Vector3(-7f, 0f, -6f), new Vector3(7f, 0f, 6f) })
        {
            var viewport = Camera.main.WorldToViewportPoint(corner);
            Assert.That(viewport.x, Is.InRange(0f, 1f));
            Assert.That(viewport.y, Is.InRange(0f, 1f));
        }
        // Rebuilding a preview must replace the old colliders, rather than doubling the level.
        arena.RebuildArena();
        yield return null;
        Assert.That(arena.transform.childCount, Is.EqualTo(2)); // Geometry plus the input manager.
        Assert.That(arena.GetComponentsInChildren<LocalHealth>().Length, Is.EqualTo(3));
    }

    [UnityTest]
    public IEnumerator PerimeterKeepsPlayersOnTheArenaFloor()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        var gamepad = InputSystem.AddDevice<Gamepad>();
        devices.Add(gamepad);
        yield return PressStart(gamepad);
        var player = PlayerInput.FindFirstPairedToDevice(gamepad);
        Assert.That(player, Is.Not.Null);
        InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.down });
        yield return new WaitForSeconds(1f);
        Assert.That(player.transform.position.z, Is.LessThan(-4f));
        Assert.That(player.transform.position.z, Is.GreaterThan(-5.7f));
        Assert.That(player.transform.position.y, Is.InRange(-0.2f, 0.2f));
    }

    [UnityTest]
    public IEnumerator JoinLimitStopsAtFourPlayers()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        for (int i = 0; i < 5; i++)
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            devices.Add(gamepad);
            yield return PressStart(gamepad);
        }
        Assert.That(PlayerInput.all.Count, Is.EqualTo(4));
    }

    [UnityTest]
    public IEnumerator KeyboardAndMouseShareOnePlayer()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        devices.Add(keyboard);
        devices.Add(mouse);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        var player = PlayerInput.FindFirstPairedToDevice(keyboard);
        Assert.That(player, Is.Not.Null);
        Assert.That(player.currentControlScheme, Is.EqualTo("KeyboardMouse"));
        bool hasMouse = false;
        foreach (var device in player.devices)
            hasMouse |= device is Mouse;
        Assert.That(hasMouse, Is.True);
        Assert.That(PlayerInput.all.Count, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator GamepadCanFireAgainAfterReleasingTrigger()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        var gamepad = InputSystem.AddDevice<Gamepad>();
        devices.Add(gamepad);
        yield return PressStart(gamepad);
        var player = PlayerInput.FindFirstPairedToDevice(gamepad);
        Assert.That(player, Is.Not.Null);

        player.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
        var mount = player.transform.Find("Handgun");
        Assert.That(Vector3.Dot(mount.GetChild(0).right, player.transform.forward), Is.EqualTo(1f).Within(0.001f));
        Assert.That(Vector3.Dot(mount.Find("Muzzle").forward, player.transform.forward), Is.EqualTo(1f).Within(0.001f));
        yield return CheckRepeatedFire(player, gamepad);
    }

    [UnityTest]
    public IEnumerator MouseCanFireAgainAfterReleasingButton()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        devices.Add(keyboard);
        devices.Add(mouse);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        var player = PlayerInput.FindFirstPairedToDevice(keyboard);
        Assert.That(player, Is.Not.Null);
        Mouse pairedMouse = null;
        foreach (var device in player.devices)
            if (device is Mouse paired)
                pairedMouse = paired;
        Assert.That(pairedMouse, Is.Not.Null);
        yield return CheckRepeatedFire(player, pairedMouse);
    }

    private IEnumerator CheckRepeatedFire(PlayerInput player, InputDevice device)
    {
        // Exercise the real Input System -> SendMessages -> OnFire path, including release.
        player.GetComponent<PlayerMovement>().enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(100f, 0f, 100f), Quaternion.identity);
        yield return null; // Let the animated hand and weapon catch up to the teleported player.
        var muzzle = player.transform.Find("Handgun/Muzzle");
        var target = CreateTarget(muzzle.position + player.transform.forward * 4f - new Vector3(100f, 0f, 100f));
        Physics.SyncTransforms();
        QueueFire(device, true);
        yield return null;
        yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(75f));
        yield return new WaitForSeconds(0.6f);
        Assert.That(target.CurrentHealth, Is.EqualTo(75f), "Holding must not fire repeatedly.");
        QueueFire(device, false);
        yield return null;
        yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(75f), "Releasing must not fire a shot.");
        QueueFire(device, true);
        yield return null;
        yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(50f), "A second press must fire another shot.");
        QueueFire(device, false);
        yield return null;
    }

    private static void QueueFire(InputDevice device, bool pressed)
    {
        if (device is Gamepad gamepad)
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightTrigger = pressed ? 1f : 0f });
        else if (device is Mouse mouse)
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = (ushort)(pressed ? 1 : 0) });
    }

    [UnityTest]
    public IEnumerator MouseShotConvergesOnCursorAndGuideOnlyShowsWhileHeld()
    {
        yield return SetupMouseAimScene();
        var target = CreateAimTarget(new Vector3(2f, 1.3f, 5f));
        Vector3 screen = Camera.main.WorldToScreenPoint(target.transform.position);
        Vector2 cursor = new Vector2(screen.x, screen.y);
        QueueMouse(cursor, 2); // Right button: preview only.
        yield return null;
        yield return null;

        var shooting = mousePlayer.GetComponent<LocalPlayerShooting>();
        var guide = mousePlayer.transform.Find("Aim guide").GetComponent<LineRenderer>();
        var path = shooting.GetShotPath();
        Assert.That(guide.enabled, Is.True);
        Assert.That(path.HasHit, Is.True);
        Assert.That(path.Collider.GetComponent<LocalHealth>(), Is.SameAs(target));
        Vector3 projected = Camera.main.WorldToScreenPoint(path.End);
        Assert.That(Vector2.Distance(cursor, new Vector2(projected.x, projected.y)), Is.LessThan(1f));
        Assert.That(Vector3.Distance(guide.GetPosition(1), path.End), Is.LessThan(0.01f));
        Assert.That(guide.startWidth, Is.LessThanOrEqualTo(0.03f));
        Assert.That(guide.sharedMaterial.GetColor("_BaseColor").a, Is.InRange(0.4f, 0.7f));
        Assert.That(target.CurrentHealth, Is.EqualTo(100f), "Preview must not deal damage.");

        QueueMouse(cursor, 3); // Hold right button and press left to fire.
        yield return null;
        yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(75f));
        QueueMouse(cursor, 0);
        yield return null;
        yield return null;
        Assert.That(guide.enabled, Is.False, "Releasing right-click must hide the guide.");
    }

    [UnityTest]
    public IEnumerator CursorSelectsTargetButCoverBlocksBothGuideAndDamage()
    {
        yield return SetupMouseAimScene();
        var target = CreateAimTarget(new Vector3(0f, 1.3f, 5f));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(wall);
        wall.transform.position = new Vector3(0f, 0.8f, 2.5f);
        wall.transform.localScale = new Vector3(2f, 1.6f, 0.2f);
        Physics.SyncTransforms();
        Vector3 screen = Camera.main.WorldToScreenPoint(target.transform.position);
        Vector2 cursor = new Vector2(screen.x, screen.y);
        QueueMouse(cursor, 2);
        yield return null;
        yield return null;

        var shooting = mousePlayer.GetComponent<LocalPlayerShooting>();
        Assert.That(shooting.TryGetMouseAimPoint(shooting.AimOrigin, out Vector3 point), Is.True);
        Assert.That(point.z, Is.GreaterThan(4f), "The camera sees the target above the cover.");
        var path = shooting.GetShotPath();
        Assert.That(path.Collider, Is.SameAs(wall.GetComponent<Collider>()));
        var guide = mousePlayer.transform.Find("Aim guide").GetComponent<LineRenderer>();
        Assert.That(Vector3.Distance(guide.GetPosition(1), path.End), Is.LessThan(0.01f));
        QueueMouse(cursor, 3);
        yield return null;
        yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
    }

    [UnityTest]
    public IEnumerator ArenaCoverBlocksShotsWhenCursorIsBeyondTheCube()
    {
        yield return JoinMousePlayer();
        var arena = Object.FindFirstObjectByType<LocalArena>();
        var cover = arena.transform.Find("Arena Geometry/Cover").GetComponent<BoxCollider>();
        Assert.That(cover.bounds.max.y, Is.EqualTo(2.2f).Within(0.01f));
        Assert.That(cover.bounds.min.y, Is.EqualTo(0f).Within(0.01f));
        var shooting = mousePlayer.GetComponent<LocalPlayerShooting>();
        var guide = mousePlayer.transform.Find("Aim guide").GetComponent<LineRenderer>();
        var controller = mousePlayer.GetComponent<CharacterController>();
        var positions = new[]
        {
            new Vector3(-3f, 0.1f, -5f),
            new Vector3(-6f, 0.1f, -0.5f),
            new Vector3(-0.2f, 0.1f, -0.5f)
        };
        var targets = new[]
        {
            new Vector3(-3f, 1.75f, 3.3f),
            new Vector3(-0.3f, 1.75f, -0.5f),
            new Vector3(-5.3f, 1.75f, -0.5f)
        };
        for (int i = 0; i < positions.Length; i++)
        {
            controller.enabled = false;
            mousePlayer.transform.SetPositionAndRotation(positions[i], Quaternion.identity);
            controller.enabled = true;
            yield return new WaitForSeconds(0.1f); // Use the real animated hand/muzzle, not the test gun.
            var target = CreateAimTarget(targets[i]);

            // Check both behaviors reported: cursor on the cube and cursor on a higher target beyond it.
            foreach (Vector3 aimPoint in new[] { cover.bounds.center, target.transform.position })
            {
                Vector3 screen = Camera.main.WorldToScreenPoint(aimPoint);
                QueueMouse(new Vector2(screen.x, screen.y), 2);
                yield return null;
                yield return null;
                var path = shooting.GetShotPath();
                Assert.That(path.Collider, Is.SameAs(cover), $"Player at {positions[i]}, cursor toward {aimPoint}");
                Assert.That(guide.enabled, Is.True);
                Assert.That(Vector3.Distance(guide.GetPosition(1), path.End), Is.LessThan(0.01f));
            }
            Assert.That(shooting.TryGetMouseAimPoint(shooting.AimOrigin, out Vector3 selected), Is.True);
            Assert.That(selected.y, Is.GreaterThan(1.7f), "Aim beyond the old 1.4 m cover, at a higher target surface.");
            Assert.That(Vector3.Distance(selected, target.transform.position), Is.LessThan(0.1f));
            Vector3 targetScreen = Camera.main.WorldToScreenPoint(target.transform.position);
            QueueMouse(new Vector2(targetScreen.x, targetScreen.y), 3);
            yield return null;
            yield return null;
            Assert.That(target.CurrentHealth, Is.EqualTo(100f), "Cover must block damage when the cursor is beyond it.");
            QueueMouse(new Vector2(targetScreen.x, targetScreen.y), 0);
            target.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.6f);
        }
    }

    [UnityTest]
    public IEnumerator EmptyFloorAimUsesMuzzleHeightAndGuideClearsOnDisable()
    {
        yield return SetupMouseAimScene();
        Vector3 screen = Camera.main.WorldToScreenPoint(new Vector3(3f, 1.3f, 4f));
        QueueMouse(new Vector2(screen.x, screen.y), 2);
        yield return null;
        yield return null;
        var shooting = mousePlayer.GetComponent<LocalPlayerShooting>();
        Assert.That(shooting.TryGetMouseAimPoint(shooting.AimOrigin, out Vector3 point), Is.True);
        Assert.That(point.y, Is.EqualTo(shooting.AimOrigin.y).Within(0.001f));
        var path = shooting.GetShotPath();
        Assert.That(path.HasHit, Is.False);
        Assert.That(path.End.y, Is.EqualTo(path.Start.y).Within(0.001f));
        Assert.That(Vector3.Distance(path.Start, path.End), Is.EqualTo(40f).Within(0.01f));
        var guide = mousePlayer.transform.Find("Aim guide").GetComponent<LineRenderer>();
        Assert.That(guide.enabled, Is.True);
        shooting.enabled = false;
        Assert.That(guide.enabled, Is.False);
    }

    private IEnumerator JoinMousePlayer()
    {
        yield return SceneManager.LoadSceneAsync("testScene");
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        devices.Add(keyboard);
        devices.Add(mouse);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        mousePlayer = PlayerInput.FindFirstPairedToDevice(keyboard);
        Assert.That(mousePlayer, Is.Not.Null);
        aimingMouse = null;
        foreach (var device in mousePlayer.devices)
            if (device is Mouse paired)
                aimingMouse = paired;
        Assert.That(aimingMouse, Is.Not.Null);
    }

    private IEnumerator SetupMouseAimScene()
    {
        yield return JoinMousePlayer();
        yield return ClearSession();

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(floor);
        floor.layer = 2;
        floor.transform.position = new Vector3(0f, -0.25f, 0f);
        floor.transform.localScale = new Vector3(20f, 0.5f, 20f);
        var controller = mousePlayer.GetComponent<CharacterController>();
        controller.enabled = false;
        mousePlayer.transform.SetPositionAndRotation(new Vector3(0f, 0.1f, 0f), Quaternion.identity);
        controller.enabled = true;
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.1f);
    }

    private LocalHealth CreateAimTarget(Vector3 position)
    {
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(target);
        target.transform.position = position;
        target.transform.localScale = new Vector3(0.8f, 1.6f, 0.08f);
        Physics.SyncTransforms();
        return target.AddComponent<LocalHealth>();
    }

    private void QueueMouse(Vector2 cursor, ushort buttons)
    {
        InputSystem.QueueStateEvent(aimingMouse, new MouseState { position = cursor, buttons = buttons });
    }

    [UnityTest]
    public IEnumerator HitscanDamagesNearestTargetAndIgnoresShooterColliders()
    {
        yield return ClearSession();
        var shooter = CreateShooter();
        var selfCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(selfCollider);
        selfCollider.transform.SetParent(shooter.transform, false);
        selfCollider.transform.localPosition = new Vector3(0.35f, 1f, 2f);
        var selfOverlap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(selfOverlap);
        selfOverlap.transform.SetParent(shooter.transform, false);
        selfOverlap.transform.localPosition = Vector3.up;
        selfOverlap.transform.localScale = Vector3.one * 2f;
        var triggerVolume = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(triggerVolume);
        triggerVolume.transform.position = shooter.AimOrigin;
        triggerVolume.GetComponent<Collider>().isTrigger = true;
        var near = CreateTarget(new Vector3(0.35f, 1f, 4f));
        var far = CreateTarget(new Vector3(0.35f, 1f, 7f));
        Physics.SyncTransforms();
        shooter.SetFirePressed(true);
        yield return null;
        yield return null;
        Assert.That(near.CurrentHealth, Is.EqualTo(75f));
        Assert.That(far.CurrentHealth, Is.EqualTo(100f));
        shooter.SetFirePressed(true);
        yield return new WaitForSeconds(0.6f);
        Assert.That(near.CurrentHealth, Is.EqualTo(75f), "Holding must not repeat a semi-auto shot.");
    }

    [UnityTest]
    public IEnumerator WallBetweenChestAndMuzzleBlocksDamage()
    {
        yield return ClearSession();
        var shooter = CreateShooter();
        var target = CreateTarget(new Vector3(0.35f, 1f, 4f));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(wall);
        wall.transform.position = new Vector3(100.175f, 1f, 100.35f);
        wall.transform.localScale = new Vector3(2f, 2f, 0.1f);
        Physics.SyncTransforms();
        shooter.SetFirePressed(true);
        yield return null;
        yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
    }

    [UnityTest]
    public IEnumerator GrazingCoverBlocksShotsAtDifferentAnglesButClearShotsStillPass()
    {
        yield return ClearSession();
        foreach (float angle in new[] { 0f, 30f, 60f, 90f, 135f, 225f, 315f })
        {
            var shooter = CreateShooter();
            shooter.transform.position += Vector3.right * angle;
            shooter.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            Vector3 start = shooter.AimOrigin;
            Vector3 forward = shooter.transform.forward;
            Vector3 right = shooter.transform.right;
            var target = CreateTarget(start + forward * 4f - new Vector3(100f, 0f, 100f));
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(cover);
            cover.transform.SetPositionAndRotation(start + forward * 2f + right * 0.505f, shooter.transform.rotation);
            cover.transform.localScale = new Vector3(1f, 2f, 0.5f);
            Physics.SyncTransforms();

            // The tracer overlaps this edge by 5 mm, while the old zero-width ray misses it.
            Assert.That(Physics.Raycast(start, forward, out RaycastHit thinHit, 10f), Is.True);
            Assert.That(thinHit.collider, Is.SameAs(target.GetComponent<Collider>()));
            var path = shooter.GetShotPath();
            Assert.That(path.Collider, Is.SameAs(cover.GetComponent<Collider>()), $"Grazing shot at {angle} degrees");
            Assert.That(Vector3.Distance(path.Start, start), Is.LessThan(0.001f));
            shooter.SetFirePressed(true);
            yield return null;
            yield return null;
            Assert.That(target.CurrentHealth, Is.EqualTo(100f));

            // A real gap beside or above the cube must remain shootable.
            cover.transform.position = start + forward * 2f + right * 0.55f;
            Physics.SyncTransforms();
            Assert.That(shooter.GetShotPath().Collider, Is.SameAs(target.GetComponent<Collider>()));
            cover.transform.localScale = Vector3.one;
            cover.transform.position = start + forward * 2f - Vector3.up * 0.56f;
            Physics.SyncTransforms();
            Assert.That(shooter.GetShotPath().Collider, Is.SameAs(target.GetComponent<Collider>()));
        }
    }

    [UnityTest]
    public IEnumerator EmbeddedShotOriginsCannotShootOutThroughCover()
    {
        yield return ClearSession();
        foreach (float angle in new[] { 0f, 45f, 90f })
        {
            var shooter = CreateShooter();
            shooter.transform.position += Vector3.right * angle;
            shooter.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            Vector3 chest = shooter.transform.position + Vector3.up;
            var target = CreateTarget(shooter.AimOrigin + shooter.transform.forward * 4f - new Vector3(100f, 0f, 100f));
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(cover);
            cover.transform.position = chest;
            cover.transform.localScale = Vector3.one * 2f;
            Physics.SyncTransforms();

            var collider = cover.GetComponent<Collider>();
            Assert.That(Vector3.Distance(collider.ClosestPoint(chest), chest), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(collider.ClosestPoint(shooter.AimOrigin), shooter.AimOrigin), Is.LessThan(0.001f));
            var path = shooter.GetShotPath();
            Assert.That(path.Collider, Is.SameAs(collider));
            Assert.That(Vector3.Distance(path.Start, path.End), Is.LessThan(0.001f), "An embedded shot must stop immediately.");
            Assert.That(path.Normal.sqrMagnitude, Is.GreaterThan(0.99f));
            shooter.SetFirePressed(true);
            yield return null;
            yield return null;
            Assert.That(target.CurrentHealth, Is.EqualTo(100f));
        }
    }

    [UnityTest]
    public IEnumerator FriendlyFireIsDisabledAndTargetsDeactivateAtZeroHealth()
    {
        yield return ClearSession();
        var shooter = CreateShooter();
        var ally = CreateTarget(new Vector3(0.35f, 1f, 4f));
        ally.gameObject.AddComponent<PlayerInput>();
        Physics.SyncTransforms();
        shooter.SetFirePressed(true);
        yield return null;
        yield return null;
        Assert.That(ally.CurrentHealth, Is.EqualTo(100f));
        ally.TakeDamage(100f);
        Assert.That(ally.CurrentHealth, Is.Zero);
        Assert.That(ally.gameObject.activeSelf, Is.False);
    }

    private static IEnumerator PressStart(Gamepad gamepad)
    {
        InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.Start));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(gamepad, new GamepadState());
        yield return null;
    }

    private static IEnumerator ClearSession()
    {
        foreach (var session in Object.FindObjectsByType<LocalCoopSession>())
            Object.Destroy(session.gameObject);
        yield return null;
    }

    private LocalPlayerShooting CreateShooter()
    {
        // Keep these isolated from the practice targets and players in the loaded scene.
        var obj = new GameObject("Test shooter");
        objects.Add(obj);
        obj.transform.position = new Vector3(100f, 0f, 100f);
        return obj.AddComponent<LocalPlayerShooting>();
    }

    private LocalHealth CreateTarget(Vector3 position)
    {
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(target);
        target.transform.position = position + new Vector3(100f, 0f, 100f);
        return target.AddComponent<LocalHealth>();
    }
}
