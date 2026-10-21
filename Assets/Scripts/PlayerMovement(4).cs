using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Movimentação estilo "Strafe": aceleração estilo Quake/CPM, air strafe, controle de ar com W,
/// bunny hop suave, sprint (Shift), slide, WALL JUMP com dash, coyote time e jump buffer.
/// Funciona com o Input System novo (padrão do Unity 6) e com o Input Manager antigo.
///
/// Controles: WASD = mover | Shift = correr | Mouse = olhar | Espaço = pular (segure para bhop automático)
///            Ctrl esquerdo ou C = agachar / slide | Esc = soltar o mouse
///            No AR, encostado numa parede: Espaço de novo = wall jump + dash na direção em que você olha
///            Não dá para pular duas vezes seguidas na MESMA parede: alterne entre paredes (ganhando altura).
///
/// Colisão: usa CharacterController (cápsula). Empurra objetos com Rigidbody (caixas, bolas).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Referências (o gerador de mapa preenche)")]
    public Transform cameraHolder;
    public Camera playerCamera;

    [Header("Mouse")]
    public float sensitivity = 0.1f;

    [Header("Corpo")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;
    public float radius = 0.35f;
    public float eyeOffset = 0.166f;     // distância do olho até o topo da cabeça (no Personagem.fbx os olhos ficam a 1,634 m)

    [Header("Chão")]
    public float moveSpeed = 7f;          // andar normal
    public float sprintSpeed = 10.5f;     // segurando Shift + W
    public float crouchSpeed = 4f;
    public float groundAccel = 14f;
    public float groundFriction = 9f;
    public float stopSpeed = 3f;         // abaixo disso a fricção para o player rápido
    public float groundStick = 3f;       // empurrãozinho pra baixo p/ colar em rampas
    public float slopeLimit = 55f;
    public LayerMask groundMask = ~(1 << 2);   // tudo, menos "Ignore Raycast" (layer do player)

    [Header("Ar (estilo Quake / CPM)")]
    public float gravity = 28f;
    public float airAccel = 10f;          // aceleração no ar com W / S / diagonais
    public float maxSpeed = 40f;
    [Tooltip("Aceleração do air strafe puro (só A ou D, girando o mouse).")]
    public float airStrafeAccel = 18f;
    [Tooltip("Quanto de velocidade o air strafe pode adicionar por frame na direção desejada. Maior = ganha velocidade mais fácil.")]
    public float airStrafeCap = 1.8f;
    [Tooltip("Acima desta velocidade o ganho do air strafe começa a diminuir (evita velocidades absurdas).")]
    public float softCapStart = 15f;
    [Tooltip("Nesta velocidade o air strafe para de ganhar velocidade.")]
    public float softCapEnd = 28f;
    [Tooltip("Controle de ar com W (estilo CPM): graus/s que a velocidade 'curva' para onde você olha, sem perder velocidade. 0 = desliga.")]
    public float airSteerRate = 140f;

    [Header("Pulo")]
    public float jumpHeight = 1.6f;
    public float jumpBufferTime = 0.12f;
    public float coyoteTime = 0.1f;
    public bool autoBhop = true;         // segurar espaço = pular assim que tocar no chão
    public int extraAirJumps = 0;        // 1 = pulo duplo

    [Header("Bhop suave (menos CS, mais Quake)")]
    [Tooltip("Tempo depois de pousar em que o pulo ainda conta como hop encadeado.")]
    public float bhopWindow = 0.2f;
    [Tooltip("Fricção durante essa janela (0 = nenhuma, 1 = normal). Pousar não 'trava' mais você.")]
    public float landingFrictionScale = 0.1f;
    [Tooltip("Velocidade extra (m/s) ganha a cada hop encadeado.")]
    public float hopBoost = 0.5f;
    [Tooltip("Acima desta velocidade o hop não dá mais boost.")]
    public float hopBoostMaxSpeed = 14f;
    [Tooltip("Velocidade extra (m/s) ao pular de dentro de um slide.")]
    public float slideJumpBoost = 2f;

    [Header("Wall jump: regras")]
    public bool enableWallJump = true;
    [Tooltip("Máximo de wall jumps encadeados no mesmo salto (recarrega ao tocar o chão). Não dá para repetir a mesma parede, então isso é o teto de quantas paredes você encadeia.")]
    public int maxWallChain = 8;
    [Tooltip("Alcance (além do raio do corpo) para detectar a parede.")]
    public float wallCheckDistance = 0.3f;
    public int wallProbeCount = 12;
    [Tooltip("Normal.y máximo para contar como parede (0 = vertical perfeita).")]
    public float wallMaxNormalY = 0.3f;
    public float wallJumpCooldown = 0.2f;
    [Tooltip("Apertar Espaço um pouco ANTES de encostar na parede também vale.")]
    public float wallJumpBuffer = 0.12f;
    [Tooltip("Apertar Espaço um pouco DEPOIS de sair da parede também vale.")]
    public float wallCoyote = 0.1f;
    [Tooltip("Duas superfícies contam como 'a mesma parede' se as normais forem parecidas (produto escalar acima deste valor)...")]
    public float wallSameWallDot = 0.85f;
    [Tooltip("...e estiverem no mesmo plano (distância máxima entre elas, em metros).")]
    public float wallSamePlaneDist = 0.6f;

    [Header("Wall jump: altura")]
    public float wallJumpUpSpeed = 8.5f;
    [Tooltip("Quanto da subida que você já tinha é mantida (1 = soma tudo). Cair não é mantido.")]
    public float wallJumpKeepVertical = 0.4f;
    [Tooltip("Subida extra (m/s) a cada wall jump encadeado (até o 4º). É o que faz você GANHAR altura alternando paredes.")]
    public float wallJumpChainUpBonus = 0.5f;

    [Header("Wall jump: momentum e dash")]
    [Tooltip("O momentum que você tinha ao chegar na parede é conservado (a colisão não zera mais ele). Este é o mínimo usado se você chegar quase parado.")]
    public float wallJumpMinCarry = 5f;
    [Tooltip("Tempo (s) em que a velocidade de antes da batida na parede ainda é lembrada.")]
    public float wallMomentumWindow = 0.15f;
    [Tooltip("0 = o momentum 'quica' na parede (reflexo). 1 = vai exatamente para onde você olha.")]
    [Range(0f, 1f)] public float wallJumpAimWeight = 0.6f;
    [Tooltip("O quanto o pulo é empurrado para longe da parede.")]
    public float wallJumpAwayRatio = 0.6f;
    [Tooltip("Impulso do dash (m/s). É uma velocidade À PARTE do seu momentum: não entra na conta do strafe e vai sumindo.")]
    public float wallDashSpeed = 9f;
    [Tooltip("Quão rápido o dash some no ar (1/s). Maior = dash mais curto.")]
    public float wallDashDecayAir = 2.2f;
    [Tooltip("Quão rápido o dash some no chão (1/s). Pousar corta o dash.")]
    public float wallDashDecayGround = 9f;
    public float wallJumpFovKick = 14f;
    public float wallJumpRoll = 5f;

    [Header("Slide")]
    public float slideMinSpeed = 7.5f;    // acima do andar (7) e abaixo do sprint (10.5): precisa correr
    public float slideEndSpeed = 4f;
    public float slideBoost = 4f;
    public float slideBoostMaxSpeed = 15f;
    public float slideFriction = 0.9f;
    public float slideSteerAccel = 8f;
    public float slideSteerCap = 1.5f;
    public float slideCooldownTime = 0.12f;

    [Header("Câmera")]
    public float baseFov = 90f;          // campo de visão (vertical). Quanto maior, mais você enxerga os pés
    public float nearClip = 0.03f;       // evita "atravessar" paredes/corpo com a câmera
    public float fovPerSpeed = 0.6f;
    public float maxRoll = 2.5f;

    [Header("Impacto suave (pouso)")]
    [Tooltip("Rigidez da mola da câmera. Menor = mais mole e lento.")]
    public float landSpring = 160f;
    [Tooltip("Amortecimento da mola. Maior = volta mais rápido, sem balançar.")]
    public float landDamping = 20f;
    [Tooltip("Abaixo desta velocidade de queda (m/s) o pouso não mexe a câmera.")]
    public float landKickMin = 5f;
    public float landKickGain = 0.18f;
    public float landKickMax = 2.6f;
    [Tooltip("Quanto a câmera pode descer (m) no pouso mais forte.")]
    public float landMaxDip = 0.12f;

    [Header("Colisão com objetos físicos")]
    [Tooltip("Força aplicada em objetos com Rigidbody ao empurrá-los. 0 = não empurra.")]
    public float pushForce = 3f;

    // ---- estado interno ----
    CharacterController cc;
    Vector3 vel;                 // momentum "de verdade" (é o que o strafe/fricção/slide enxergam)
    Vector3 dashVel;             // impulso do wall jump: somado ao movimento, mas FORA da conta do strafe
    float yaw, pitch, roll;
    bool grounded, wasGrounded;
    Vector3 groundNormal = Vector3.up;
    float groundMoveY, lastGroundVy;
    bool sliding;
    bool sprinting;
    bool crouchedNow;
    float slideCooldown;
    float jumpBuffer, coyote;
    int airJumpsLeft;
    float camY;
    float jumpSpeed;

    // bhop / pouso
    float lastLandTime = -10f;
    float dip, dipVel;           // mola da câmera no pouso (dip > 0 = câmera abaixo do normal)

    // wall jump
    int wallChain;
    float wallJumpCd, wallBuffer;
    float lastWallSeen = -10f;
    Vector3 lastWallNormal, lastWallPoint;
    bool hasLastJumpWall;
    Vector3 lastJumpWallNormal, lastJumpWallPoint;
    Vector3 wallHitVel;
    float wallHitTime = -10f;
    float lastWallJumpTime = -10f;
    float fovKick, fovKickGoal, rollKick, rollKickGoal;

    float MinGroundNormalY => Mathf.Cos(slopeLimit * Mathf.Deg2Rad);

    // ---- estado público (usado pelo FirstPersonBody e por efeitos) ----
    public Vector3 Velocity => new Vector3(vel.x + dashVel.x, vel.y, vel.z + dashVel.z);
    public float CurrentSpeed => new Vector2(vel.x + dashVel.x, vel.z + dashVel.z).magnitude;
    public bool IsGrounded => grounded;
    public bool IsSliding => sliding;
    public bool IsSprinting => sprinting;
    public bool IsCrouched => crouchedNow;
    public float EyeHeight => camY;                       // altura atual da câmera (suavizada)
    public float StandEyeHeight => standHeight - eyeOffset;
    public Vector3 GroundNormal => groundNormal;
    public int WallJumpsLeft => Mathf.Max(0, maxWallChain - wallChain);
    public float TimeSinceWallJump => Time.time - lastWallJumpTime;

    /// <summary>Disparado no instante do wall jump (use para som, partículas, etc.). Parâmetro: normal da parede.</summary>
    public event System.Action<Vector3> OnWallJump;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        gameObject.layer = 2; // Ignore Raycast: os testes de chão/teto não "enxergam" o próprio player
        cc.radius = radius;
        cc.height = standHeight;
        cc.center = new Vector3(0f, standHeight / 2f, 0f);
        cc.slopeLimit = slopeLimit;
        cc.stepOffset = 0.35f;
        cc.skinWidth = 0.03f;
        cc.minMoveDistance = 0f;

        jumpSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
        yaw = transform.eulerAngles.y;
        camY = standHeight - eyeOffset;
        if (playerCamera != null)
        {
            playerCamera.fieldOfView = baseFov;
            playerCamera.nearClipPlane = nearClip;
        }

        // Warn if cameraHolder is not assigned (needed for proper FPS camera control)
        if (cameraHolder == null)
        {
            Debug.LogWarning("PlayerMovement: cameraHolder is not assigned. For proper FPS camera control, assign a child GameObject named 'cameraHolder' to the cameraHolder field.");
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        ReadInput(out Vector2 moveInput, out bool jumpPressed, out bool jumpHeld, out bool crouchHeld, out bool sprintHeld);
        HandleCursor();
        if (Cursor.lockState == CursorLockMode.Locked) Look();

        CheckGround();

        // ---------- Pouso ----------
        if (grounded && !wasGrounded)
        {
            lastLandTime = Time.time;
            float impact = -vel.y;                                   // vel.y ainda é a velocidade de queda
            // Em vez de "afundar" a câmera de uma vez, damos um empurrão de velocidade na mola (começa suave).
            dipVel += Mathf.Clamp((impact - landKickMin) * landKickGain, 0f, landKickMax);
        }
        if (grounded)
        {
            wallChain = 0;                                           // tocar o chão recarrega a corrente de wall jumps
            hasLastJumpWall = false;                                 // e libera todas as paredes de novo
        }

        // Saiu do chão por uma rampa (sem pular): mantém a velocidade de subida
        if (!grounded && wasGrounded && lastGroundVy > 0f) vel.y = lastGroundVy;

        // ---------- Buffers de pulo ----------
        if (grounded) { coyote = coyoteTime; airJumpsLeft = extraAirJumps; }
        else coyote -= dt;

        if (jumpPressed || (autoBhop && jumpHeld)) jumpBuffer = jumpBufferTime;
        else jumpBuffer -= dt;

        wallBuffer -= dt;
        wallJumpCd -= dt;
        if (jumpPressed) wallBuffer = wallJumpBuffer;     // só um aperto NOVO conta para wall jump (segurar não)

        // ---------- Detecção de parede (só no ar) ----------
        bool wallNear = false;
        if (enableWallJump && !grounded)
        {
            if (ProbeWall(out Vector3 wn, out Vector3 wp))
            {
                lastWallNormal = wn;
                lastWallPoint = wp;
                lastWallSeen = Time.time;
            }
            wallNear = Time.time - lastWallSeen <= wallCoyote;
        }

        // ---------- Pulo / Wall jump ----------
        bool wallJumpReady = enableWallJump && !grounded && wallBuffer > 0f && wallNear
                             && wallChain < maxWallChain && wallJumpCd <= 0f;

        if (jumpBuffer > 0f && (grounded || coyote > 0f))
        {
            DoGroundJump();
            jumpBuffer = 0f;
            coyote = 0f;
            wallBuffer = 0f;     // o mesmo aperto não pode virar wall jump logo em seguida
            grounded = false;    // este frame já conta como no ar => sem fricção => mantém velocidade (bhop)
        }
        else if (wallJumpReady)
        {
            DoWallJump();
            jumpBuffer = 0f;
            wallBuffer = 0f;
        }
        else if (jumpPressed && !grounded && airJumpsLeft > 0)
        {
            vel.y = jumpSpeed;
            airJumpsLeft--;
        }

        // ---------- Agachar / Slide ----------
        bool canStand = CanStand();
        bool crouched = crouchHeld || !canStand;
        crouchedNow = crouched;
        float hSpeed = HorizontalSpeed();
        slideCooldown -= dt;

        if (!sliding && grounded && crouchHeld && hSpeed >= slideMinSpeed && slideCooldown <= 0f)
            StartSlide(hSpeed);
        if (sliding && (!grounded || (!crouchHeld && canStand) || hSpeed < slideEndSpeed))
            EndSlide();

        SetHeight(crouched ? crouchHeight : standHeight);

        // ---------- Sprint (Shift + para frente). No ar, mantém o estado de quando saiu do chão ----------
        if (grounded) sprinting = sprintHeld && moveInput.y > 0.1f && !crouched;

        // ---------- Direção desejada ----------
        Vector3 wish = transform.right * moveInput.x + transform.forward * moveInput.y;
        wish = Vector3.ClampMagnitude(wish, 1f);
        float baseSpeed = sprinting ? sprintSpeed : moveSpeed;
        float wishSpeed = wish.magnitude * ((crouched && grounded && !sliding) ? crouchSpeed : baseSpeed);
        wish = wish.normalized;

        // ---------- Aceleração (só mexe em "vel": o dash não entra aqui) ----------
        if (grounded)
        {
            if (sliding)
            {
                ApplyFriction(slideFriction, dt);
                Accelerate(wish, wishSpeed, slideSteerAccel, dt, slideSteerCap);
                // Descer rampa durante o slide acelera
                Vector3 slopeAccel = Vector3.ProjectOnPlane(Vector3.down * gravity, groundNormal);
                vel.x += slopeAccel.x * dt;
                vel.z += slopeAccel.z * dt;
            }
            else
            {
                // Logo depois de pousar a fricção quase some enquanto você está se movendo:
                // encadear pulos não "trava" mais a velocidade (estilo Quake, não CS).
                float fric = groundFriction;
                if (wish.sqrMagnitude > 0.01f && Time.time - lastLandTime < bhopWindow)
                    fric *= landingFrictionScale;

                ApplyFriction(fric, dt);
                Accelerate(wish, wishSpeed, groundAccel, dt, float.MaxValue);
            }
        }
        else
        {
            bool strafeOnly = Mathf.Abs(moveInput.x) > 0.1f && Mathf.Abs(moveInput.y) < 0.1f;
            bool forwardOnly = moveInput.y > 0.1f && Mathf.Abs(moveInput.x) < 0.1f;

            if (strafeOnly)
            {
                // Air strafe puro (A ou D + girar o mouse): ganha velocidade, com retorno decrescente em velocidades altas
                float gain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(softCapStart, softCapEnd, HorizontalSpeed()));
                Accelerate(wish, wishSpeed, airStrafeAccel, dt, airStrafeCap * gain);
            }
            else
            {
                Accelerate(wish, wishSpeed, airAccel, dt, float.MaxValue);
                if (forwardOnly) AirSteer(wish, dt);   // W + mouse = curva no ar sem perder velocidade
            }

            vel.y -= gravity * dt;
            if (vel.y < -60f) vel.y = -60f;
        }

        // Limite de velocidade horizontal
        float h = HorizontalSpeed();
        if (h > maxSpeed)
        {
            float k = maxSpeed / h;
            vel.x *= k; vel.z *= k;
        }

        // O dash do wall jump vai sumindo sozinho (bem mais rápido no chão)
        if (dashVel.sqrMagnitude > 0f)
        {
            dashVel *= Mathf.Exp(-(grounded ? wallDashDecayGround : wallDashDecayAir) * dt);
            if (dashVel.sqrMagnitude < 0.04f) dashVel = Vector3.zero;
        }

        // ---------- Mover ----------
        Vector3 move;
        if (grounded && vel.y <= 0.01f)
        {
            // Segue o plano do chão mantendo a velocidade horizontal (rampas ficam suaves)
            float tx = vel.x + dashVel.x;
            float tz = vel.z + dashVel.z;
            float y = -(groundNormal.x * tx + groundNormal.z * tz) / groundNormal.y;
            groundMoveY = y;
            move = new Vector3(tx, y - groundStick, tz);
            vel.y = 0f;
        }
        else
        {
            groundMoveY = 0f;
            move = new Vector3(vel.x + dashVel.x, vel.y, vel.z + dashVel.z);
        }

        CollisionFlags flags = cc.Move(move * dt);
        if ((flags & CollisionFlags.Above) != 0 && vel.y > 0f) vel.y = 0f;

        lastGroundVy = grounded ? groundMoveY : 0f;
        wasGrounded = grounded;

        CameraEffects(dt, crouched);
    }

    // =====================================================================
    //  Pulos
    // =====================================================================

    void DoGroundJump()
    {
        vel.y = jumpSpeed;

        float hs = HorizontalSpeed();
        if (hs > 0.5f)
        {
            float add = 0f;
            if (sliding)
                add = slideJumpBoost;                                              // slide jump: sai do slide ganhando velocidade
            else if (Time.time - lastLandTime <= bhopWindow && hs < hopBoostMaxSpeed)
                add = Mathf.Min(hopBoost, hopBoostMaxSpeed - hs);                  // hop encadeado: pequeno boost

            if (add > 0f)
            {
                vel.x += vel.x / hs * add;
                vel.z += vel.z / hs * add;
            }
        }
    }

    void DoWallJump()
    {
        Vector3 n = lastWallNormal;                               // horizontal, aponta da parede para você
        Vector3 aim = transform.forward;                          // para onde você olha (horizontal)

        // Direção pedida pelo olhar: segue a parede na direção em que você olha, sempre com um empurrão para longe dela.
        Vector3 aimDir = aim - n * Vector3.Dot(aim, n);
        aimDir += n * (Mathf.Max(Vector3.Dot(aim, n), 0f) + wallJumpAwayRatio);
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = n;
        aimDir.Normalize();

        // Momentum de antes da batida: a colisão com a parede já cortou "vel", então usamos o que foi guardado.
        Vector3 incoming = new Vector3(vel.x, 0f, vel.z);
        if (Time.time - wallHitTime <= wallMomentumWindow && wallHitVel.sqrMagnitude > incoming.sqrMagnitude)
            incoming = wallHitVel;
        float inSpeed = incoming.magnitude;
        float carried = Mathf.Max(inSpeed, wallJumpMinCarry);

        // O momentum "quica" na parede e é puxado em direção ao seu olhar.
        Vector3 dir = aimDir;
        if (inSpeed > 1f)
        {
            Vector3 inDir = incoming / inSpeed;
            float into = Vector3.Dot(inDir, n);
            Vector3 bounce = into < 0f ? inDir - 2f * into * n : inDir;
            dir = Vector3.Slerp(bounce, aimDir, wallJumpAimWeight);
            float outward = Vector3.Dot(dir, n);
            if (outward < 0.3f) dir += n * (0.3f - outward);   // garante que sai da parede
            dir.y = 0f;
            dir.Normalize();
        }

        // Momentum conservado (não inflado) + dash como impulso separado que vai sumindo.
        vel.x = dir.x * carried;
        vel.z = dir.z * carried;
        dashVel = dir * wallDashSpeed;

        // Altura: cada wall jump encadeado sobe um pouco mais, então alternar paredes faz você ganhar altura.
        int chainIndex = wallChain;
        float up = wallJumpUpSpeed + Mathf.Min(chainIndex, 3) * wallJumpChainUpBonus;
        vel.y = Mathf.Max(vel.y, 0f) * wallJumpKeepVertical + up;

        wallChain++;
        wallJumpCd = wallJumpCooldown;
        lastWallJumpTime = Time.time;
        lastWallSeen = -10f;

        // Esta parede fica bloqueada até você pular de OUTRA parede ou tocar o chão.
        hasLastJumpWall = true;
        lastJumpWallNormal = n;
        lastJumpWallPoint = lastWallPoint;

        // Feedback de câmera (suavizado em CameraEffects)
        fovKickGoal = wallJumpFovKick;
        float side = Vector3.Dot(dir, transform.right);
        rollKickGoal = -Mathf.Sign(side) * wallJumpRoll;

        OnWallJump?.Invoke(n);
    }

    // true se (normal, ponto) é a mesma parede da qual acabamos de pular (mesma direção e mesmo plano)
    bool IsLastJumpWall(Vector3 n, Vector3 point)
    {
        if (!hasLastJumpWall) return false;
        if (Vector3.Dot(n, lastJumpWallNormal) < wallSameWallDot) return false;
        return Mathf.Abs(Vector3.Dot(point - lastJumpWallPoint, lastJumpWallNormal)) < wallSamePlaneDist;
    }

    // Procura uma parede (superfície quase vertical) ao redor, na altura do centro do corpo.
    // Ignora a parede da qual acabamos de pular.
    bool ProbeWall(out Vector3 normal, out Vector3 point)
    {
        normal = Vector3.zero;
        point = Vector3.zero;
        Vector3 center = transform.TransformPoint(cc.center);
        float reach = cc.radius + wallCheckDistance;
        float best = float.MaxValue;
        bool found = false;
        int count = Mathf.Max(4, wallProbeCount);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * (360f / count), 0f) * Vector3.forward;
            if (!Physics.Raycast(center, dir, out RaycastHit hit, reach, groundMask, QueryTriggerInteraction.Ignore))
                continue;
            if (Mathf.Abs(hit.normal.y) > wallMaxNormalY) continue;

            Vector3 flat = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (flat.sqrMagnitude < 0.0001f) continue;
            flat.Normalize();

            if (IsLastJumpWall(flat, hit.point)) continue;

            if (hit.distance < best)
            {
                best = hit.distance;
                normal = flat;
                point = hit.point;
                found = true;
            }
        }
        return found;
    }

    // =====================================================================
    //  Física
    // =====================================================================

    void CheckGround()
    {
        Vector3 origin = transform.position + Vector3.up * (cc.radius + 0.05f);
        bool hitGround = Physics.SphereCast(origin, cc.radius * 0.95f, Vector3.down, out RaycastHit hit,
                                            0.25f, groundMask, QueryTriggerInteraction.Ignore);

        grounded = hitGround && hit.normal.y >= MinGroundNormalY && vel.y <= 0.1f;
        groundNormal = grounded ? hit.normal : Vector3.up;
    }

    bool CanStand()
    {
        Vector3 head = transform.position + Vector3.up * (standHeight - radius);
        return !Physics.CheckSphere(head, radius * 0.9f, groundMask, QueryTriggerInteraction.Ignore);
    }

    void SetHeight(float h)
    {
        if (Mathf.Approximately(cc.height, h)) return;
        cc.height = h;
        cc.center = new Vector3(0f, h / 2f, 0f);
    }

    float HorizontalSpeed() => new Vector2(vel.x, vel.z).magnitude;

    // Aceleração clássica (Quake/Source). "cap" limita a velocidade-alvo na direção desejada.
    void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt, float cap)
    {
        if (wishDir.sqrMagnitude < 0.0001f) return;

        float cappedWish = Mathf.Min(wishSpeed, cap);
        float current = vel.x * wishDir.x + vel.z * wishDir.z;
        float add = cappedWish - current;
        if (add <= 0f) return;

        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, add);
        vel.x += wishDir.x * accelSpeed;
        vel.z += wishDir.z * accelSpeed;
    }

    // Controle de ar estilo CPM: gira a direção da velocidade em direção ao que você quer (W + mouse),
    // preservando a velocidade. É o que faz as curvas no ar ficarem fluidas.
    void AirSteer(Vector3 wish, float dt)
    {
        if (airSteerRate <= 0f) return;

        float speed = HorizontalSpeed();
        if (speed < 0.5f) return;

        Vector3 flat = new Vector3(vel.x, 0f, vel.z) / speed;
        Vector3 target = new Vector3(wish.x, 0f, wish.z);
        if (target.sqrMagnitude < 0.0001f) return;
        target.Normalize();

        float dot = Vector3.Dot(flat, target);
        if (dot <= 0f) return;                                    // só ajusta se você já está indo mais ou menos para onde olha

        float maxRad = airSteerRate * Mathf.Deg2Rad * dt * dot;
        Vector3 nd = Vector3.RotateTowards(flat, target, maxRad, 0f);
        vel.x = nd.x * speed;
        vel.z = nd.z * speed;
    }

    void ApplyFriction(float friction, float dt)
    {
        float speed = HorizontalSpeed();
        if (speed < 0.01f) { vel.x = 0f; vel.z = 0f; return; }

        float control = Mathf.Max(speed, stopSpeed);
        float drop = control * friction * dt;
        float newSpeed = Mathf.Max(speed - drop, 0f) / speed;
        vel.x *= newSpeed;
        vel.z *= newSpeed;
    }

    void StartSlide(float hSpeed)
    {
        sliding = true;
        if (hSpeed < slideBoostMaxSpeed && hSpeed > 0.01f)
        {
            float add = Mathf.Min(slideBoost, slideBoostMaxSpeed - hSpeed);
            vel.x += vel.x / hSpeed * add;
            vel.z += vel.z / hSpeed * add;
        }
    }

    void EndSlide()
    {
        sliding = false;
        slideCooldown = slideCooldownTime;
    }

    // Colisões do CharacterController: teto, paredes e objetos físicos
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Empurrar objetos com Rigidbody (caixas, bolas...). Não empurra para baixo se você estiver em cima deles.
        Rigidbody rb = hit.collider.attachedRigidbody;
        if (pushForce > 0f && rb != null && !rb.isKinematic && hit.moveDirection.y > -0.3f)
        {
            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (dir.sqrMagnitude > 0.0001f)
            {
                dir.Normalize();
                float mySpeed = Mathf.Max(0f, vel.x * dir.x + vel.z * dir.z);
                float theirSpeed = Vector3.Dot(rb.GetPointVelocity(hit.point), dir);
                float relative = mySpeed - theirSpeed;
                if (relative > 0f)
                    rb.AddForceAtPosition(dir * relative * pushForce, hit.point, ForceMode.Impulse);
            }
        }

        Vector3 n = hit.normal;
        if (n.y < -0.5f && vel.y > 0f) { vel.y = 0f; return; }   // teto

        if (n.y < 0.3f)                                           // parede: remove só a parte da velocidade que vai "para dentro" dela
        {
            n.y = 0f;
            if (n.sqrMagnitude < 0.0001f) return;
            n.Normalize();

            // Guarda a parede encostada (ajuda o wall jump quando o raio de detecção perde um canto)
            if (!grounded && Mathf.Abs(hit.normal.y) <= wallMaxNormalY && !IsLastJumpWall(n, hit.point))
            {
                lastWallNormal = n;
                lastWallPoint = hit.point;
                lastWallSeen = Time.time;
            }

            float d = vel.x * n.x + vel.z * n.z;
            if (d < 0f)
            {
                // Lembra o momentum de ANTES da batida: o wall jump usa isso para conservar a velocidade.
                Vector3 pre = new Vector3(vel.x, 0f, vel.z);
                if (Time.time - wallHitTime > wallMomentumWindow || pre.sqrMagnitude >= wallHitVel.sqrMagnitude)
                {
                    wallHitVel = pre;
                    wallHitTime = Time.time;
                }

                vel.x -= n.x * d;
                vel.z -= n.z * d;
            }

            // O dash também não atravessa a parede
            float dd = dashVel.x * n.x + dashVel.z * n.z;
            if (dd < 0f)
            {
                dashVel.x -= n.x * dd;
                dashVel.z -= n.z * dd;
            }
        }
    }

    // =====================================================================
    //  Câmera
    // =====================================================================

    void Look()
    {
        Vector2 d = ReadMouseDelta() * sensitivity;
        yaw += d.x;
        pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cameraHolder != null) cameraHolder.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void CameraEffects(float dt, bool crouched)
    {
        // Mola do pouso: a câmera desce e volta de forma suave (sem "tranco"). Passo limitado para ficar estável com FPS baixo.
        float sdt = Mathf.Min(dt, 0.033f);
        dipVel += (-landSpring * dip - landDamping * dipVel) * sdt;
        dip = Mathf.Clamp(dip + dipVel * sdt, -0.02f, landMaxDip);

        // Kicks de FOV/inclinação do wall jump entram e saem suavemente
        fovKickGoal = Mathf.Lerp(fovKickGoal, 0f, dt * 5f);
        rollKickGoal = Mathf.Lerp(rollKickGoal, 0f, dt * 5f);
        fovKick = Mathf.Lerp(fovKick, fovKickGoal, dt * 14f);
        rollKick = Mathf.Lerp(rollKick, rollKickGoal, dt * 14f);

        if (cameraHolder != null)
        {
            float targetY = (crouched ? crouchHeight : standHeight) - eyeOffset;
            camY = Mathf.Lerp(camY, targetY, dt * 22f);
            cameraHolder.localPosition = new Vector3(0f, camY - dip, 0f);
        }

        if (playerCamera == null) return;

        // FOV aumenta com a velocidade (sensação de velocidade) + pulso suave no wall jump
        float extra = Mathf.Clamp(HorizontalSpeed() - moveSpeed, 0f, 25f) * fovPerSpeed + fovKick;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, baseFov + extra, dt * 10f);

        // Inclinação lateral ao andar de lado (+ inclinação do wall jump)
        float side = Vector3.Dot(vel, transform.right) / Mathf.Max(moveSpeed, 1f);
        float targetRoll = -Mathf.Clamp(side, -1.5f, 1.5f) * maxRoll;
        roll = Mathf.Lerp(roll, targetRoll, dt * 10f);
        playerCamera.transform.localRotation = Quaternion.Euler(0f, 0f, roll + rollKick);
    }

    // =====================================================================
    //  Input (Input System novo OU Input Manager antigo)
    // =====================================================================

#if ENABLE_INPUT_SYSTEM
    static void ReadInput(out Vector2 move, out bool jumpPressed, out bool jumpHeld, out bool crouchHeld, out bool sprintHeld)
    {
        move = Vector2.zero; jumpPressed = false; jumpHeld = false; crouchHeld = false; sprintHeld = false;
        Keyboard k = Keyboard.current;
        if (k == null) return;

        move.x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        move.y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        jumpPressed = k.spaceKey.wasPressedThisFrame;
        jumpHeld = k.spaceKey.isPressed;
        crouchHeld = k.leftCtrlKey.isPressed || k.cKey.isPressed;
        sprintHeld = k.leftShiftKey.isPressed;
    }

    static Vector2 ReadMouseDelta() => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

    void HandleCursor()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                 && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
#else
    static void ReadInput(out Vector2 move, out bool jumpPressed, out bool jumpHeld, out bool crouchHeld, out bool sprintHeld)
    {
        sprintHeld = Input.GetKey(KeyCode.LeftShift);
        move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        jumpPressed = Input.GetKeyDown(KeyCode.Space);
        jumpHeld = Input.GetKey(KeyCode.Space);
        crouchHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
    }

    static Vector2 ReadMouseDelta() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;

    void HandleCursor()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
#endif
}
