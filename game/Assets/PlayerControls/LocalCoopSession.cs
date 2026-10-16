using UnityEngine;
using UnityEngine.InputSystem;

public class LocalCoopSession : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField, Range(1, 4)] private int maxPlayers = 4;
    [SerializeField] private Vector3 spawnOrigin = new Vector3(-1f, 0.1f, 0f);
    [SerializeField, Min(1f)] private float spawnSpacing = 2f;

    private PlayerInputManager manager;
    private InputAction joinAction;

    private void Awake()
    {
        if (playerPrefab == null || Camera.main == null)
        {
            Debug.LogError("LocalCoopSession requires a player prefab and a camera tagged MainCamera.", this);
            enabled = false;
            return;
        }

        // Configure before enabling, so joining cannot run against a partially initialized manager.
        var managerObject = new GameObject("Local player input manager");
        managerObject.SetActive(false);
        managerObject.transform.SetParent(transform, false);
        manager = managerObject.AddComponent<PlayerInputManager>();
        manager.DisableJoining();
        manager.playerPrefab = playerPrefab;
        manager.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        manager.joinBehavior = PlayerJoinBehavior.JoinPlayersWhenJoinActionIsTriggered;
        joinAction = new InputAction("Join", InputActionType.Button);
        joinAction.AddBinding("<Keyboard>/enter");
        joinAction.AddBinding("<Gamepad>/start");
        manager.joinAction = new InputActionProperty(joinAction);
        manager.onPlayerJoined += HandlePlayerJoined;
        manager.onPlayerLeft += HandlePlayerLeft;
        managerObject.SetActive(true);
        manager.EnableJoining();
    }

    private void OnEnable()
    {
        if (manager != null && manager.playerCount < maxPlayers)
            manager.EnableJoining();
    }

    private void OnDisable()
    {
        if (manager != null)
            manager.DisableJoining();
    }

    private void HandlePlayerJoined(PlayerInput player)
    {
        player.name = $"Player {player.playerIndex + 1}";
        player.neverAutoSwitchControlSchemes = true;
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = transform.position + spawnOrigin +
            new Vector3(player.playerIndex % 2, 0f, -(player.playerIndex / 2)) * spawnSpacing;
        controller.enabled = true;

        Color[] colors = { Color.cyan, Color.yellow, Color.magenta, Color.green };
        var visual = player.transform.Find("Visual").GetComponent<Renderer>();
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", colors[player.playerIndex % colors.Length]);
        visual.SetPropertyBlock(properties);

        if (manager.playerCount >= maxPlayers)
            manager.DisableJoining();
    }

    private void HandlePlayerLeft(PlayerInput player)
    {
        if (isActiveAndEnabled && manager.playerCount < maxPlayers)
            manager.EnableJoining();
    }

    private void OnGUI()
    {
        if (manager == null)
            return;
        GUI.Label(new Rect(15, 15, 850, 25), $"Players: {manager.playerCount}/{maxPlayers} | Enter: join with keyboard & mouse | Start: join with gamepad");
        GUI.Label(new Rect(15, 40, 850, 25), "Move: WASD / left stick | Aim: mouse / right stick | Shoot: left click / right trigger");
    }

    private void OnDestroy()
    {
        if (manager != null)
        {
            manager.DisableJoining();
            manager.onPlayerJoined -= HandlePlayerJoined;
            manager.onPlayerLeft -= HandlePlayerLeft;
        }
        joinAction?.Dispose();
    }
}
