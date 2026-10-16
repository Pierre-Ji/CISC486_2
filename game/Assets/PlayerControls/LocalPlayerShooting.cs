using UnityEngine;
using UnityEngine.InputSystem;

// Local adaptation of SingleShotMechanism and HitscanShooting. No network lifecycle is required.
[RequireComponent(typeof(PlayerInput))]
[DefaultExecutionOrder(50)]
public class LocalPlayerShooting : MonoBehaviour
{
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
        public readonly RaycastHit Hit;
        public readonly bool HasHit;

        public ShotPath(Vector3 start, Vector3 end, RaycastHit hit, bool hasHit)
        {
            Start = start;
            End = end;
            Hit = hit;
            HasHit = hasHit;
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
        bool hasHit = TryGetHit(chest, toMuzzle.normalized, toMuzzle.magnitude, hitMask, out RaycastHit hit);
        if (hasHit)
            start = chest;
        else
            hasHit = TryGetHit(start, direction, range, hitMask, out hit);
        Vector3 end = hasHit ? hit.point : start + direction * range;
        return new ShotPath(start, end, hit, hasHit);
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

        RaycastHit hit = path.Hit;
        var health = hit.collider.GetComponentInParent<LocalHealth>();
        bool isPlayer = hit.collider.GetComponentInParent<PlayerInput>() != null;
        if (health != null && (friendlyFire || !isPlayer))
            health.TakeDamage(damage);

        if (impactEffect != null)
            Destroy(Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
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
        tracer.startWidth = tracer.endWidth = 0.02f;
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
