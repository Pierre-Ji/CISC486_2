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

        // Rejoining with an already paired controller must not duplicate its player.
        yield return PressStart(first);
        Assert.That(PlayerInput.all.Count, Is.EqualTo(2));
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
