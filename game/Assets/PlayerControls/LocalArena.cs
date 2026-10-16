using UnityEngine;
using UnityEngine.Playables;

// A small editable kit-built arena. Rebuild Arena also provides a preview outside Play mode.
[DefaultExecutionOrder(-100)]
public class LocalArena : MonoBehaviour
{
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject coverPrefab;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private Vector2Int floorTiles = new Vector2Int(7, 6);
    [SerializeField] private Vector3[] coverPositions = { new Vector3(-3f, 0f, -0.5f), new Vector3(3f, 0f, -0.5f) };
    [SerializeField] private Vector3[] targetPositions = { new Vector3(-4f, 0f, 3.8f), new Vector3(0f, 0f, 3.8f), new Vector3(4f, 0f, 3.8f) };

    private Camera sharedCamera;

    private void Awake()
    {
        RebuildArena();
        sharedCamera = Camera.main;
        if (sharedCamera != null)
        {
            sharedCamera.orthographic = true;
            sharedCamera.transform.SetPositionAndRotation(new Vector3(0f, 16f, -12f), Quaternion.LookRotation(new Vector3(0f, -16f, 12f)));
            FitCamera();
        }
    }

    private void LateUpdate() => FitCamera();

    private void FitCamera()
    {
        if (sharedCamera != null)
            sharedCamera.orthographicSize = Mathf.Max(floorTiles.y + 2f, (floorTiles.x + 1f) / Mathf.Max(sharedCamera.aspect, 0.1f));
    }

    [ContextMenu("Rebuild Arena")]
    public void RebuildArena()
    {
        if (floorPrefab == null || wallPrefab == null || coverPrefab == null || targetPrefab == null)
        {
            Debug.LogError("LocalArena requires floor, wall, cover and target prefabs.", this);
            return;
        }
        var previous = transform.Find("Arena Geometry");
        if (previous != null)
        {
            previous.gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(previous.gameObject);
            else
                DestroyImmediate(previous.gameObject);
        }
        var geometry = new GameObject("Arena Geometry").transform;
        geometry.SetParent(transform, false);
        int width = Mathf.Max(2, floorTiles.x);
        int depth = Mathf.Max(2, floorTiles.y);
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                var floor = AddPiece(geometry, floorPrefab, $"Floor {x},{z}", new Vector3(2f * x - width + 1f, -0.12f, 2f * z - depth + 1f), new Vector3(2f, 0.24f, 2f));
                floor.layer = 2; // Ignore Raycast: omit ground from cursor picking, but keep physical collision.
            }

        for (int x = 0; x < width; x++)
        {
            float position = 2f * x - width + 1f;
            AddPiece(geometry, wallPrefab, "North wall", new Vector3(position, 0.75f, depth + 0.15f), new Vector3(2f, 1.5f, 0.3f));
            AddPiece(geometry, wallPrefab, "South wall", new Vector3(position, 0.45f, -depth - 0.15f), new Vector3(2f, 0.9f, 0.3f));
        }
        for (int z = 0; z < depth; z++)
        {
            float position = 2f * z - depth + 1f;
            AddPiece(geometry, wallPrefab, "West wall", new Vector3(-width - 0.15f, 0.75f, position), new Vector3(0.3f, 1.5f, 2f), 90f);
            AddPiece(geometry, wallPrefab, "East wall", new Vector3(width + 0.15f, 0.75f, position), new Vector3(0.3f, 1.5f, 2f), 90f);
        }
        foreach (Vector3 position in coverPositions)
            AddPiece(geometry, coverPrefab, "Cover", position + Vector3.up * 0.7f, new Vector3(1.5f, 1.4f, 1.5f));
        foreach (Vector3 position in targetPositions)
        {
            var target = AddPiece(geometry, targetPrefab, "Practice Target", position + Vector3.up * 0.9f, new Vector3(1.1f, 1.8f, 0.35f), preserveRotation: false);
            target.AddComponent<LocalHealth>();
        }
    }

    private static GameObject AddPiece(Transform parent, GameObject prefab, string name, Vector3 center, Vector3 size, float yaw = 0f, bool preserveRotation = true)
    {
        var piece = new GameObject(name);
        piece.transform.SetParent(parent, false);
        piece.transform.localPosition = center;
        var visual = Instantiate(prefab, piece.transform, false).transform;
        visual.localPosition = Vector3.zero;
        // The target variant was authored upside down for a hanging mount; stand it upright here.
        visual.localRotation = Quaternion.Euler(0f, yaw, 0f) * (preserveRotation ? visual.localRotation : Quaternion.identity);
        // These are art-only instances. The target's template timeline is not part of local damage.
        foreach (var director in visual.GetComponentsInChildren<PlayableDirector>())
        {
            director.Stop();
            director.enabled = false;
        }
        foreach (var animator in visual.GetComponentsInChildren<Animator>())
            animator.enabled = false;
        foreach (var collider in visual.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        // Fit under an unrotated wrapper to account for the template's off-center pivots and FBX axes.
        var fitted = new GameObject("Visual").transform;
        fitted.SetParent(piece.transform, false);
        visual.SetParent(fitted, false);
        Bounds bounds = LocalBounds(piece.transform);
        fitted.localScale = new Vector3(size.x / Mathf.Max(bounds.size.x, 0.001f), size.y / Mathf.Max(bounds.size.y, 0.001f), size.z / Mathf.Max(bounds.size.z, 0.001f));
        bounds = LocalBounds(piece.transform);
        fitted.localPosition = -bounds.center;
        piece.AddComponent<BoxCollider>().size = size;
        return piece;
    }

    private static Bounds LocalBounds(Transform root)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            Bounds world = renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = world.center + Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                point = root.InverseTransformPoint(point);
                if (first)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    first = false;
                }
                else
                    bounds.Encapsulate(point);
            }
        }
        return bounds;
    }
}
