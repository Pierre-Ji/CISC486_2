using UnityEngine;
using UnityEngine.InputSystem;

// Local adaptation of SingleShotMechanism and HitscanShooting. No network lifecycle is required.
[RequireComponent(typeof(PlayerInput))]
public class LocalPlayerShooting : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float shotInterval = 0.5f;
    [SerializeField, Min(0f)] private float damage = 25f;
    [SerializeField, Min(0.1f)] private float range = 40f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private bool friendlyFire;
    [SerializeField] private GameObject handgunVisual;
    [SerializeField] private AudioClip shotSound;
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField] private GameObject impactEffect;
    [SerializeField] private Material tracerMaterial;

    private readonly LocalShotTrigger trigger = new LocalShotTrigger();
    private bool shotPending;
    private Transform muzzle;
    private AudioSource audioSource;

    private void Awake()
    {
        var mount = new GameObject("Handgun").transform;
        mount.SetParent(transform, false);
        mount.localPosition = new Vector3(0.35f, 1f, 0.4f);
        if (handgunVisual != null)
            Instantiate(handgunVisual, mount, false);

        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(mount, false);
        muzzle.localPosition = new Vector3(0f, 0f, 0.3f);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    public void OnFire(InputValue value) => SetFirePressed(value.isPressed);

    public void SetFirePressed(bool pressed)
    {
        // Preserve quick press/release events until LateUpdate, after movement has updated facing.
        shotPending |= trigger.SetPressed(pressed, Time.timeAsDouble, shotInterval);
    }

    private void LateUpdate()
    {
        if (!shotPending)
            return;
        shotPending = false;
        Shoot();
    }

    private void Shoot()
    {
        Vector3 chest = transform.position + Vector3.up;
        Vector3 toMuzzle = muzzle.position - chest;
        // Check the barrel's path first so a muzzle poking through a wall cannot shoot through it.
        bool hitSomething = TryGetHit(chest, toMuzzle.normalized, toMuzzle.magnitude, out RaycastHit hit);
        if (!hitSomething)
            hitSomething = TryGetHit(muzzle.position, transform.forward, range, out hit);

        Vector3 end = hitSomething ? hit.point : muzzle.position + transform.forward * range;
        DrawTracer(muzzle.position, end);
        if (shotSound != null)
            audioSource.PlayOneShot(shotSound);
        if (muzzleFlash != null)
            Destroy(Instantiate(muzzleFlash, muzzle.position, muzzle.rotation), 2f);

        if (!hitSomething)
            return;

        var health = hit.collider.GetComponentInParent<LocalHealth>();
        bool isPlayer = hit.collider.GetComponentInParent<PlayerInput>() != null;
        if (health != null && (friendlyFire || !isPlayer))
            health.TakeDamage(damage);

        if (impactEffect != null)
            Destroy(Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
    }

    private bool TryGetHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default;
        float closestDistance = float.PositiveInfinity;
        // RaycastAll lets us ignore every collider on the shooter without hiding targets behind it.
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, hitMask, QueryTriggerInteraction.Ignore))
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
    }
}
