using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Controlador principal del jugador en primera persona para la mecánica de visión intermitente.
/// Administra el movimiento, rotación de cámara, destellos de luz (flash y disparo), recolección por contacto
/// de munición ("Ammo") y piezas de llave ("KeyPiece"), e interacción con la puerta de salida ("Exit")
/// usando una tecla configurable en el Inspector (por defecto E).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerPrototypeController : MonoBehaviour
{
    // --- Constantes de configuración para evitar "magic numbers" ---
    private const float MIN_VERTICAL_LOOK_ANGLE = -85f;
    private const float MAX_VERTICAL_LOOK_ANGLE = 85f;
    private const float MOVEMENT_NOISE_THRESHOLD_SQR = 0.1f;
    private const float OFF_LIGHT_INTENSITY = 0f;
    private const float AUTO_PICKUP_TOUCH_RADIUS = 1.15f;

    // --- Eventos del Patrón Observer para actualizar el HUD y la Puerta de Salida ---
    public static event Action<int> OnAmmoChanged;
    public static event Action<float> OnFlashCooldownStarted;
    public static event Action<int, int> OnKeyPiecesChanged;

    [Header("Controles (New Input System)")]
    [Tooltip("Acción Vector2 para desplazar al personaje (WASD / Joystick izquierdo).")]
    public InputActionReference moveAction;
    [Tooltip("Acción Vector2 para rotar la cámara (Mouse Delta / Joystick derecho).")]
    public InputActionReference lookAction;
    [Tooltip("Acción Button para activar el flash fotográfico (Clic derecho / Tecla F / Botón UI).")]
    public InputActionReference flashAction;
    [Tooltip("Acción Button para disparar la pistola (Clic izquierdo / Botón UI).")]
    public InputActionReference shootAction;
    [Tooltip("Acción opcional del Input System para interactuar con la puerta (para botones en Android).")]
    public InputActionReference interactAction;

    [Header("Interacción y Llaves de Salida")]
    [Tooltip("Tecla configurable desde el Inspector para abrir la puerta cuando tienes las 3 piezas (por defecto E).")]
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Piezas de llave requeridas para desbloquear la puerta de salida.")]
    public int requiredKeyPieces = 3;
    [Tooltip("Piezas de llave recolectadas actualmente por el jugador.")]
    public int collectedKeyPieces = 0;

    [Header("Movimiento y Cámara")]
    [Tooltip("Velocidad de desplazamiento del jugador en metros por segundo.")]
    public float moveSpeed = 5f;
    [Tooltip("Sensibilidad de giro de la cámara en primera persona.")]
    public float lookSensitivity = 0.15f;
    [Tooltip("Transform de la cámara hija ubicada a la altura de los ojos.")]
    public Transform playerCamera;
    [Tooltip("Bloquea y oculta el cursor al jugar en PC.")]
    public bool lockCursorOnPC = true;

    [Header("Mecánica Principal: Destello de Cámara")]
    [Tooltip("Luz puntual (Point Light) hija de la cámara que ilumina el entorno.")]
    public Light flashLight;
    [Tooltip("Duración en segundos del destello del flash antes de volver a oscuridad total.")]
    public float cameraFlashDuration = 1f;
    [Tooltip("Intensidad máxima de la luz al activar el flash fotográfico.")]
    public float cameraFlashIntensity = 5f;
    [Tooltip("Tiempo de espera en segundos para volver a usar el flash.")]
    public float flashCooldown = 1f;

    [Header("Combate y Munición")]
    [Tooltip("Cantidad de balas con las que inicia el jugador.")]
    public int ammo = 5;
    [Tooltip("Balas que suma cada caja de munición recolectada.")]
    public int ammoPerPickup = 3;
    [Tooltip("Duración muy breve en segundos del fogonazo al disparar.")]
    public float gunFlashDuration = 0.08f;
    [Tooltip("Intensidad máxima del fogonazo al disparar.")]
    public float gunFlashIntensity = 7f;
    [Tooltip("Alcance máximo del disparo mediante Raycast al centro de la pantalla.")]
    public float shootRange = 20f;

    [Header("Rangos de Alerta al Enemigo")]
    [Tooltip("Radio en metros dentro del cual los enemigos escuchan los pasos del jugador.")]
    public float walkNoiseRadius = 4f;
    [Tooltip("Radio en metros dentro del cual los enemigos detectan el uso del flash.")]
    public float flashAlertRadius = 14f;
    [Tooltip("Radio en metros dentro del cual los enemigos escuchan un disparo.")]
    public float gunshotAlertRadius = 25f;

    // --- Variables de estado interno ---
    private CharacterController controller;
    private float verticalRotation = 0f;
    private float nextFlashTime = 0f;
    private Coroutine lightCoroutine;

    /// <summary>
    /// Habilita las acciones del Input System y suscribe los eventos de botones.
    /// </summary>
    void OnEnable()
    {
        if (moveAction != null) moveAction.action.Enable();
        if (lookAction != null) lookAction.action.Enable();
        if (flashAction != null)
        {
            flashAction.action.Enable();
            flashAction.action.performed += OnFlashPerformed;
        }
        if (shootAction != null)
        {
            shootAction.action.Enable();
            shootAction.action.performed += OnShootPerformed;
        }
        if (interactAction != null)
        {
            interactAction.action.Enable();
            interactAction.action.performed += OnInteractPerformed;
        }
    }

    /// <summary>
    /// Desuscribe los eventos al desactivar el objeto para evitar referencias colgadas.
    /// </summary>
    void OnDisable()
    {
        if (flashAction != null) flashAction.action.performed -= OnFlashPerformed;
        if (shootAction != null) shootAction.action.performed -= OnShootPerformed;
        if (interactAction != null) interactAction.action.performed -= OnInteractPerformed;
    }

    /// <summary>
    /// Inicializa referencias, apaga la luz del destello y notifica la munición y llaves iniciales al HUD.
    /// </summary>
    void Start()
    {
        controller = GetComponent<CharacterController>();

#if UNITY_STANDALONE || UNITY_EDITOR
        if (lockCursorOnPC)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
#endif

        if (flashLight != null)
        {
            flashLight.intensity = OFF_LIGHT_INTENSITY;
        }

        if (EnemyAndAmmoPoolManager.Instance != null)
        {
            requiredKeyPieces = EnemyAndAmmoPoolManager.Instance.totalKeyPiecesRequired;
        }

        OnAmmoChanged?.Invoke(ammo);
        OnKeyPiecesChanged?.Invoke(collectedKeyPieces, requiredKeyPieces);
    }

    /// <summary>
    /// Procesa el movimiento, la rotación de cámara, la recolección por contacto y la tecla de interacción.
    /// </summary>
    void Update()
    {
        if (Time.timeScale <= 0f) return;

        MoverJugador();
        RotarCamara();
        VerificarRecoleccionPorContacto();
        VerificarTeclaDeInteraccion();
    }

    /// <summary>
    /// Lee el vector de movimiento y desplaza al jugador emitiendo ruido de pasos si camina.
    /// </summary>
    void MoverJugador()
    {
        if (moveAction == null) return;

        Vector2 inputMove = moveAction.action.ReadValue<Vector2>();
        Vector3 moveDirection = transform.right * inputMove.x + transform.forward * inputMove.y;
        controller.SimpleMove(moveDirection * moveSpeed);

        if (inputMove.sqrMagnitude > MOVEMENT_NOISE_THRESHOLD_SQR)
        {
            AlertarEnemigos(walkNoiseRadius);
        }
    }

    /// <summary>
    /// Rota horizontalmente la cápsula del jugador y verticalmente la cámara en primera persona.
    /// </summary>
    void RotarCamara()
    {
        if (lookAction == null || playerCamera == null) return;

        Vector2 inputLook = lookAction.action.ReadValue<Vector2>() * lookSensitivity;

        verticalRotation -= inputLook.y;
        verticalRotation = Mathf.Clamp(verticalRotation, MIN_VERTICAL_LOOK_ANGLE, MAX_VERTICAL_LOOK_ANGLE);

        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        transform.Rotate(Vector3.up * inputLook.x);
    }

    /// <summary>
    /// Garantiza que tanto las piezas de llave como la munición se recolecten al tocarlas (incluso si su Collider es Trigger).
    /// </summary>
    void VerificarRecoleccionPorContacto()
    {
        Collider[] nearbyHits = Physics.OverlapSphere(transform.position, AUTO_PICKUP_TOUCH_RADIUS, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider col in nearbyHits)
        {
            ProcesarObjetoRecolectable(col.gameObject);
        }
    }

    /// <summary>
    /// Detecta colisiones directas del CharacterController con cajas de munición o piezas de llave.
    /// </summary>
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        ProcesarObjetoRecolectable(hit.gameObject);
    }

    /// <summary>
    /// Evalúa si el objeto tocado es munición ("Ammo") o una pieza de llave ("KeyPiece") y lo recolecta sin presionar teclas.
    /// </summary>
    void ProcesarObjetoRecolectable(GameObject obj)
    {
        if (obj == null || !obj.activeInHierarchy) return;

        if (obj.CompareTag("Ammo"))
        {
            ammo += ammoPerPickup;
            OnAmmoChanged?.Invoke(ammo);

            if (EnemyAndAmmoPoolManager.Instance != null)
            {
                EnemyAndAmmoPoolManager.Instance.ReturnAmmoToPool(obj);
            }
            else
            {
                obj.SetActive(false);
            }
        }
        else if (obj.CompareTag("KeyPiece"))
        {
            if (EnemyAndAmmoPoolManager.Instance != null)
            {
                EnemyAndAmmoPoolManager.Instance.ReturnKeyPieceToPool(obj);
            }
            else
            {
                obj.SetActive(false);
            }

            collectedKeyPieces++;
            OnKeyPiecesChanged?.Invoke(collectedKeyPieces, requiredKeyPieces);
            Debug.Log($"¡Pieza de llave recolectada! ({collectedKeyPieces}/{requiredKeyPieces})");
        }
    }

    /// <summary>
    /// Revisa si el jugador presionó la tecla configurable en el Inspector (por defecto E) usando el New Input System.
    /// </summary>
    void VerificarTeclaDeInteraccion()
    {
        if (FuePresionadaTeclaConfigurable(interactKey))
        {
            IntentarInteractuarConPuerta();
        }
    }

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        if (Time.timeScale <= 0f) return;
        IntentarInteractuarConPuerta();
    }

    /// <summary>
    /// Intenta abrir la puerta de salida si el jugador está dentro del rango y ya tiene las 3 piezas de llave.
    /// </summary>
    public void IntentarInteractuarConPuerta()
    {
        if (EnemyAndAmmoPoolManager.Instance == null) return;

        ExitDoorController exitDoor = EnemyAndAmmoPoolManager.Instance.exitDoor;
        if (exitDoor != null)
        {
            exitDoor.TryInteract(transform.position, collectedKeyPieces, requiredKeyPieces);
        }
    }

    /// <summary>
    /// Traduce cualquier KeyCode elegido en el Inspector al control correspondiente de Keyboard.current del New Input System.
    /// </summary>
    bool FuePresionadaTeclaConfigurable(KeyCode keyCode)
    {
        if (Keyboard.current == null) return false;

        string keyName = keyCode.ToString().ToLowerInvariant();
        if (keyName.StartsWith("alpha"))
        {
            keyName = keyName.Replace("alpha", "");
        }

        InputControl control = Keyboard.current.TryGetChildControl(keyName);
        if (control is ButtonControl button)
        {
            return button.wasPressedThisFrame;
        }

        // Respaldo directo para la tecla E por defecto
        if (keyCode == KeyCode.E)
        {
            return Keyboard.current.eKey.wasPressedThisFrame;
        }

        return false;
    }

    private void OnFlashPerformed(InputAction.CallbackContext context)
    {
        if (Time.timeScale <= 0f) return;

        if (Time.time >= nextFlashTime)
        {
            UsarFlashCamara();
        }
    }

    private void OnShootPerformed(InputAction.CallbackContext context)
    {
        if (Time.timeScale <= 0f) return;

        if (ammo > 0)
        {
            Disparar();
        }
    }

    /// <summary>
    /// Activa el destello de la cámara fotográfica, inicia su enfriamiento y alerta a los enemigos en rango medio.
    /// </summary>
    public void UsarFlashCamara()
    {
        nextFlashTime = Time.time + flashCooldown;
        ActivarDestello(cameraFlashIntensity, cameraFlashDuration);
        AlertarEnemigos(flashAlertRadius);
        OnFlashCooldownStarted?.Invoke(flashCooldown);
    }

    /// <summary>
    /// Consume una bala, genera el fogonazo corto del arma, verifica impacto por Raycast y alerta a los enemigos.
    /// </summary>
    public void Disparar()
    {
        ammo--;
        OnAmmoChanged?.Invoke(ammo);

        ActivarDestello(gunFlashIntensity, gunFlashDuration);

        Ray aimRay = new Ray(playerCamera.position, playerCamera.forward);
        if (Physics.Raycast(aimRay, out RaycastHit hit, shootRange))
        {
            EnemyBlindAI enemy = hit.collider.GetComponent<EnemyBlindAI>();
            if (enemy != null && enemy.gameObject.activeInHierarchy)
            {
                enemy.RecibirDisparo();
            }
        }

        AlertarEnemigos(gunshotAlertRadius);
    }

    /// <summary>
    /// Función reutilizable para iniciar o reiniciar la corrutina de destello con la intensidad y duración indicadas.
    /// </summary>
    void ActivarDestello(float intensity, float duration)
    {
        if (flashLight == null) return;

        if (lightCoroutine != null)
        {
            StopCoroutine(lightCoroutine);
        }
        lightCoroutine = StartCoroutine(RutinaDestello(intensity, duration));
    }

    /// <summary>
    /// Corrutina que enciende la luz puntual y la desvanece suavemente hasta regresar a oscuridad total.
    /// </summary>
    IEnumerator RutinaDestello(float maxIntensity, float duration)
    {
        flashLight.intensity = maxIntensity;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            flashLight.intensity = Mathf.Lerp(maxIntensity, OFF_LIGHT_INTENSITY, elapsed / duration);
            yield return null;
        }

        flashLight.intensity = OFF_LIGHT_INTENSITY;
    }

    /// <summary>
    /// Notifica la posición actual del jugador a todos los enemigos activos dentro del radio especificado.
    /// </summary>
    void AlertarEnemigos(float radius)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (Collider col in hits)
        {
            EnemyBlindAI enemy = col.GetComponent<EnemyBlindAI>();
            if (enemy != null && enemy.gameObject.activeInHierarchy)
            {
                enemy.EscucharEstimulo(transform.position);
            }
        }
    }
}