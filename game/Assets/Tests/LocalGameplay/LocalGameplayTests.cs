using System.Collections;
using System.Collections.Generic;
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

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = PlayerInput.all.Count - 1; i >= 0; i--)
            Object.Destroy(PlayerInput.all[i].gameObject);
        foreach (var obj in objects)
            if (obj != null)
                Object.Destroy(obj);
        yield return null;
        foreach (var device in devices)
            if (device.added)
                InputSystem.RemoveDevice(device);
        objects.Clear();
        devices.Clear();
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
        foreach (var health in geometry.GetComponentsInChildren<LocalHealth>())
        {
            var box = health.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null);
            var renderers = health.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            Assert.That(Vector3.Distance(bounds.center, box.bounds.center), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(bounds.size, box.bounds.size), Is.LessThan(0.01f));
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
    public IEnumerator HitscanDamagesNearestTargetAndIgnoresShooterColliders()
    {
        yield return ClearSession();
        var shooter = CreateShooter();
        var selfCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(selfCollider);
        selfCollider.transform.SetParent(shooter.transform, false);
        selfCollider.transform.localPosition = new Vector3(0.35f, 1f, 2f);
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
