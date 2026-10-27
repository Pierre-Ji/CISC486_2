using UnityEngine;
using UnityEngine.InputSystem;

// Local adaptation of SingleShotMechanism and HitscanShooting. No network lifecycle is required.
[RequireComponent(typeof(PlayerInput))]
[DefaultExecutionOrder(50)]
public class LocalPlayerShooting : MonoBehaviour
{
    // Match the visible tracer's 2 cm diameter, including grazing hits at cover edges.
    private const float ShotRadius = 0.01f;
    [SerializeField, Min(0.01f)] private float shotInterval = 0.5f;
    [SerializeField, Min(0f)] private float damage = 25f;
    [SerializeField, Min(0.1f)] private float range = 40f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private bool friendlyFire;
    [SerializeField] private GameObject handgunVisual;
    [SerializeField] private Vector3 handgunLocalEulerAngles = new Vector3(0f, -90f, 0f);
    [SerializeField] private Vector3 muzzleLocalPosition = new Vector3(0f, 0.16021f, 0.249f);
    [SerializeField] private AudioClip shotSound;
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField] private GameObject impactEffect;
    [SerializeField] private Material tracerMaterial;

    [Header("Cursor targeting and aim guide")]
    [SerializeField] private LayerMask cursorTargetMask = Physics.DefaultRaycastLayers;
    [SerializeField] private Material aimGuideMaterial;
    [SerializeField, Range(0.005f, 0.04f)] private float aimGuideWidth = 0.025f;

    private readonly LocalShotTrigger trigger = new LocalShotTrigger();
    private bool shotPending;
    private Transform muzzle;
    private AudioSource audioSource;
    private Transform weaponMount;
    private PlayerMovement movement;
    private LineRenderer aimGuide;
    private bool aimGuideHeld;
    public event System.Action ShotFired;
    public Vector3 AimOrigin => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.2f;

    public readonly struct ShotPath
    {
        public readonly Vector3 Start;
        public readonly Vector3 End;
        public readonly Collider Collider;
        public readonly Vector3 Normal;
        public bool HasHit => Collider != null;

        public ShotPath(Vector3 start, Vector3 end, Collider collider, Vector3 normal)
        {
            Start = start;
            End = end;
            Collider = collider;
            Normal = normal;
        }
    }

    private void Awake()
    {
        var mount = new GameObject("Handgun").transform;
        weaponMount = mount;
        movement = GetComponent<PlayerMovement>();
        mount.SetParent(transform, false);
        mount.localPosition = new Vector3(0.35f, 1f, 0.4f);
        if (handgunVisual != null)
        {
            var visual = Instantiate(handgunVisual, mount, false).transform;
            // The template handgun's barrel points along +X; gameplay aiming uses +Z.
            visual.localRotation = Quaternion.Euler(handgunLocalEulerAngles);
        }

        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(mount, false);
        muzzle.localPosition = muzzleLocalPosition;
        var visuals = GetComponent<LocalPlayerVisuals>();
        if (visuals != null)
            visuals.SetWeaponMount(mount);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        aimGuide = new GameObject("Aim guide").AddComponent<LineRenderer>();
        aimGuide.transform.SetParent(transform, false);
        aimGuide.sharedMaterial = aimGuideMaterial;
        aimGuide.useWorldSpace = true;
        aimGuide.positionCount = 2;
        aimGuide.startWidth = aimGuide.endWidth = aimGuideWidth;
        aimGuide.numCapVertices = 2;
        aimGuide.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimGuide.receiveShadows = false;
        aimGuide.enabled = false;
    }

    // Fire uses Press And Release so PlayerInput.SendMessages forwards both button edges.
    public void OnFire(InputValue value) => SetFirePressed(value.isPressed);
    public void OnAimGuide(InputValue value) => SetAimGuideHeld(value.isPressed);
    public void SetAimGuideHeld(bool held) => aimGuideHeld = held;

    public bool TryGetMouseAimPoint(Vector3 origin, out Vector3 point)
    {
        point = default;
        if (movement == null || !movement.TryGetMouseAimRay(out Ray ray))
            return false;
        if (TryGetHit(ray.origin, ray.direction, 1000f, cursorTargetMask, out RaycastHit hit))
        {
            point = hit.point;
            return true;
        }
        // Floors are excluded from cursor picking; empty space aims horizontally at muzzle height.
        var plane = new Plane(Vector3.up, origin);
        if (!plane.Raycast(ray, out float distance))
            return false;
        point = ray.GetPoint(distance);
        return true;
    }

    public void SetFirePressed(bool pressed)
    {
        // Preserve quick press/release events until LateUpdate, after movement has updated facing.
        shotPending |= trigger.SetPressed(pressed, Time.timeAsDouble, shotInterval);
    }

    private void LateUpdate()
    {
        if (TryGetMouseAimPoint(AimOrigin, out Vector3 target))
        {
            // Aim the visual barrel as well as the ray. Re-evaluate after rotating the offset muzzle.
            for (int i = 0; i < 2; i++)
            {
                Vector3 direction = target - muzzle.position;
                if (direction.sqrMagnitude > 0.0001f)
                    weaponMount.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
        else
            weaponMount.rotation = transform.rotation;

        if (!shotPending && !aimGuideHeld)
        {
            aimGuide.enabled = false;
            return;
        }
        ShotPath path = GetShotPath();
        aimGuide.enabled = aimGuideHeld && aimGuideMaterial != null && (path.End - path.Start).sqrMagnitude > 0.0004f;
        if (aimGuide.enabled)
        {
            aimGuide.SetPosition(0, path.Start);
            aimGuide.SetPosition(1, path.End);
        }
        if (!shotPending)
            return;
        shotPending = false;
        Shoot(path);
    }

    public ShotPath GetShotPath()
    {
        Vector3 start = muzzle.position;
        Vector3 direction = transform.forward;
        if (TryGetMouseAimPoint(start, out Vector3 target) && (target - start).sqrMagnitude > 0.0001f)
            direction = (target - start).normalized;
        Vector3 chest = transform.position + Vector3.up;
        Vector3 toMuzzle = muzzle.position - chest;
        // Check the barrel's path first so a muzzle poking through a wall cannot shoot through it.
        ShotPath barrelPath = TraceShot(chest, toMuzzle.normalized, toMuzzle.magnitude);
        return barrelPath.HasHit ? barrelPath : TraceShot(start, direction, range);
    }

    private ShotPath TraceShot(Vector3 origin, Vector3 direction, float distance)
    {
        // Unity casts skip colliders containing their origin. Check overlaps before each cast,
        // both at the chest and at the muzzle, so an embedded barrel cannot bypass cover.
        foreach (var collider in Physics.OverlapSphere(origin, ShotRadius, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(transform))
                continue;
            Vector3 point = collider.ClosestPoint(origin);
            Vector3 normal = origin - point;
            normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : -direction;
            if (normal.sqrMagnitude < 0.000001f)
                normal = -transform.forward;
            return new ShotPath(origin, point, collider, normal);
        }

        RaycastHit closest = default;
        float closestDistance = float.PositiveInfinity;
        foreach (var hit in Physics.SphereCastAll(origin, ShotRadius, direction, distance, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance)
                continue;
            closest = hit;
            closestDistance = hit.distance;
        }
        return closestDistance < float.PositiveInfinity
            ? new ShotPath(origin, closest.point, closest.collider, closest.normal)
            : new ShotPath(origin, origin + direction * distance, null, Vector3.zero);
    }

    private void Shoot(ShotPath path)
    {
        ShotFired?.Invoke();
        DrawTracer(path.Start, path.End);
        if (shotSound != null)
            audioSource.PlayOneShot(shotSound);
        if (muzzleFlash != null)
            Destroy(Instantiate(muzzleFlash, muzzle.position, muzzle.rotation), 2f);

        if (!path.HasHit)
            return;

        var health = path.Collider.GetComponentInParent<LocalHealth>();
        bool isPlayer = path.Collider.GetComponentInParent<PlayerInput>() != null;
        if (health != null && (friendlyFire || !isPlayer))
            health.TakeDamage(damage);

        if (impactEffect != null)
            Destroy(Instantiate(impactEffect, path.End, Quaternion.LookRotation(path.Normal)), 2f);
    }

    private bool TryGetHit(Vector3 origin, Vector3 direction, float distance, int layers, out RaycastHit closest)
    {
        closest = default;
        float closestDistance = float.PositiveInfinity;
        // RaycastAll lets us ignore every collider on the shooter without hiding targets behind it.
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, layers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance)
                continue;
            closest = hit;
            closestDistance = hit.distance;
        }
        return closestDistance < float.PositiveInfinity;
    }

    private void DrawTracer(Vector3 start, Vector3 end)
    {
        if (tracerMaterial == null)
            return;
        var tracer = new GameObject("Shot tracer").AddComponent<LineRenderer>();
        tracer.sharedMaterial = tracerMaterial;
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = tracer.endWidth = ShotRadius * 2f;
        tracer.SetPosition(0, start);
        tracer.SetPosition(1, end);
        Destroy(tracer.gameObject, 0.06f);
    }

    private void OnDisable()
    {
        shotPending = false;
        trigger.Release();
        aimGuideHeld = false;
        if (aimGuide != null)
            aimGuide.enabled = false;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
            OnDisable();
    }
}
