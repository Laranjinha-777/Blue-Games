using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimation : MonoBehaviour
{
    private Animator animator;
    private PlayerMovement movement;
    private Transform bodyTransform;
    private Vector3 baseBodyPos;
    [Tooltip("Height offset for feet (0 = ground level). Adjust if feet sink or float.")]
    public float feetHeightOffset = 0f;
    [Tooltip("Forward/back offset for feet (negative = behind player, positive = in front).")]
    public float feetForwardOffset = -0.2f; // Slightly behind to keep feet visible when looking down

    void Awake()
    {
        animator = GetComponent<Animator>();
        movement = GetComponentInParent<PlayerMovement>();
        bodyTransform = transform;
        baseBodyPos = bodyTransform.localPosition;
        animator.applyRootMotion = false; // keep animation in place relative to character
        if (!movement)
            Debug.LogError("PlayerAnimation: Não foi possível encontrar PlayerMovement no pai");
    }

    void Update()
    {
        if (!movement) return;

        // Obter velocidade horizontal (ignorando movimento vertical)
        float hSpeed = new Vector2(movement.Velocity.x, movement.Velocity.z).magnitude;

        // Normalizar velocidade para animação (0 = idle, 1 = walk, >1 = run)
        float walkSpeed = movement.moveSpeed;    // 7f
        float runSpeed = movement.sprintSpeed;   // 10.5f

        float normalizedSpeed = Mathf.InverseLerp(0f, runSpeed, hSpeed);
        animator.SetFloat("Speed", normalizedSpeed);
    }

    void LateUpdate()
    {
        if (!animator || !movement) return;

        // Get foot bone transforms
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        if (!leftFoot || !rightFoot)
        {
            // Fallback to hips if feet not found
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!hips) return;

            // Desired hips height relative to the Player (constant)
            Vector3 desiredHipRelative = new Vector3(0, movement.StandEyeHeight - 0.7f, 0);

            // Current hips height relative to the Player
            Vector3 currentHipRelative = hips.position - movement.transform.position;

            // Error to correct
            Vector3 hipError = desiredHipRelative - currentHipRelative;

            // Apply correction to body's local position (preserving base offset)
            Vector3 hipAdjustment = baseBodyPos + hipError;
            bodyTransform.localPosition = hipAdjustment;
            return;
        }

        // Desired feet position relative to the Player (constant)
        Vector3 desiredFeetRelative = new Vector3(0, feetHeightOffset, feetForwardOffset);

        // Current feet position relative to the Player (average of both feet)
        Vector3 currentFeetRelative = (leftFoot.position + rightFoot.position) * 0.5f - movement.transform.position;

        // Error to correct
        Vector3 feetError = desiredFeetRelative - currentFeetRelative;

        // Apply correction to body's local position (preserving base offset)
        Vector3 feetAdjustment = baseBodyPos + feetError;
        bodyTransform.localPosition = feetAdjustment;
    }
}