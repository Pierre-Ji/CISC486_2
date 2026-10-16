using UnityEngine;

// Uses the template's rig and animation asset, without its networked animator components.
[DefaultExecutionOrder(20)]
public class LocalPlayerVisuals : MonoBehaviour
{
    [SerializeField] private GameObject characterModel;
    [SerializeField] private RuntimeAnimatorController animationController;
    [SerializeField] private Material[] bodyMaterials;
    [SerializeField] private Material[] armMaterials;
    [SerializeField] private Material[] legMaterials;

    private Animator animator;
    private CharacterController controller;
    private LocalPlayerShooting shooting;
    private Transform hand;
    private Transform weaponMount;
    private SkinnedMeshRenderer[] renderers;
    private float recoil;
    private int playerIndex;

    public Animator Animator => animator;
    public Transform Hand => hand;

    private void Awake()
    {
        if (characterModel == null || animationController == null)
        {
            Debug.LogError("LocalPlayerVisuals requires a character model and animation controller.", this);
            enabled = false;
            return;
        }

        var model = Instantiate(characterModel, transform, false);
        model.name = "Character";
        model.transform.localPosition = Vector3.zero;
        controller = GetComponent<CharacterController>();
        animator = model.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = animationController;
        animator.applyRootMotion = false;
        animator.fireEvents = false; // Template events expect its networked animator and sound system.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.SetBool("IsAiming", true);
        animator.SetBool("Grounded", true);
        animator.SetInteger("WeaponTypeID", 1);
        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        foreach (var bone in model.GetComponentsInChildren<Transform>())
            if (bone.name == "Right_Hand_Attach")
                hand = bone;
        renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        SetPlayerIndex(playerIndex);
    }

    private void OnEnable()
    {
        shooting = GetComponent<LocalPlayerShooting>();
        if (shooting != null)
            shooting.ShotFired += HandleShot;
    }

    private void OnDisable()
    {
        if (shooting != null)
            shooting.ShotFired -= HandleShot;
    }

    public void SetPlayerIndex(int index)
    {
        playerIndex = index;
        if (renderers == null)
            return;
        foreach (var renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                string part = materials[i].name;
                Material[] palette = part.Contains("Body") ? bodyMaterials : part.Contains("Arms") ? armMaterials : legMaterials;
                if (palette != null && palette.Length > 0)
                    materials[i] = palette[index % palette.Length];
            }
            renderer.sharedMaterials = materials;
        }
    }

    public void SetWeaponMount(Transform mount) => weaponMount = mount;

    private void Update()
    {
        if (animator == null || controller == null)
            return;
        Vector3 velocity = controller.velocity;
        float verticalSpeed = velocity.y;
        velocity.y = 0f;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        float speed = velocity.magnitude;
        animator.SetFloat("Speed", speed);
        animator.SetFloat("MotionSpeed", Mathf.Clamp01(speed / 6f));
        animator.SetFloat("StrafeX", localVelocity.x / 6f, 0.1f, Time.deltaTime);
        animator.SetFloat("StrafeY", localVelocity.z / 6f, 0.1f, Time.deltaTime);
        animator.SetBool("Grounded", controller.isGrounded);
        animator.SetBool("FreeFall", !controller.isGrounded && verticalSpeed < -0.1f);
    }

    private void LateUpdate()
    {
        if (weaponMount == null || hand == null)
            return;
        recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 0.3f);
        // Follow the animated hand, but keep the barrel parallel to the independent aim direction.
        weaponMount.SetPositionAndRotation(hand.position - transform.forward * recoil, transform.rotation);
    }

    private void HandleShot() => recoil = 0.035f;
}
