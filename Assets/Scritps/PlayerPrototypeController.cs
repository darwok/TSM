using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerPrototypeController : MonoBehaviour
{
    [Header("Controles (New Input System)")]
    public InputActionReference moveAction;
    public InputActionReference lookAction;
    public InputActionReference flashAction;
    public InputActionReference shootAction;

    [Header("Movimiento y Cámara")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 0.15f;
    public Transform playerCamera;
    public bool lockCursorOnPC = true;

    [Header("Mecánica de Destello (Flash / Disparo)")]
    public Light flashLight;
    public float cameraFlashDuration = 0.25f;
    public float cameraFlashIntensity = 4f;
    public float flashCooldown = 1.5f;

    [Header("Combate y Munición")]
    public int ammo = 5;
    public float gunFlashDuration = 0.08f;
    public float gunFlashIntensity = 7f;
    public float shootRange = 20f;

    [Header("Rangos de Alerta al Enemigo")]
    public float walkNoiseRadius = 4f;
    public float flashAlertRadius = 14f;
    public float gunshotAlertRadius = 25f;

    private CharacterController controller;
    private float verticalRotation = 0f;
    private float nextFlashTime = 0f;
    private Coroutine lightCoroutine;

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
    }

    void OnDisable()
    {
        if (flashAction != null) flashAction.action.performed -= OnFlashPerformed;
        if (shootAction != null) shootAction.action.performed -= OnShootPerformed;
    }

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

        if (flashLight != null) flashLight.intensity = 0f;
    }

    void Update()
    {
        MoverJugador();
        RotarCamara();
    }

    void MoverJugador()
    {
        if (moveAction == null) return;

        Vector2 inputMove = moveAction.action.ReadValue<Vector2>();
        Vector3 move = transform.right * inputMove.x + transform.forward * inputMove.y;
        controller.SimpleMove(move * moveSpeed);

        // Si el jugador se mueve, emite ruido cercano
        if (inputMove.sqrMagnitude > 0.1f)
        {
            AlertarEnemigos(walkNoiseRadius);
        }
    }

    void RotarCamara()
    {
        if (lookAction == null || playerCamera == null) return;

        Vector2 inputLook = lookAction.action.ReadValue<Vector2>() * lookSensitivity;

        verticalRotation -= inputLook.y;
        verticalRotation = Mathf.Clamp(verticalRotation, -85f, 85f);

        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        transform.Rotate(Vector3.up * inputLook.x);
    }

    private void OnFlashPerformed(InputAction.CallbackContext context)
    {
        if (Time.time >= nextFlashTime)
        {
            UsarFlashCamara();
        }
    }

    private void OnShootPerformed(InputAction.CallbackContext context)
    {
        if (ammo > 0)
        {
            Disparar();
        }
    }

    public void UsarFlashCamara()
    {
        nextFlashTime = Time.time + flashCooldown;
        ActivarDestello(cameraFlashIntensity, cameraFlashDuration);
        AlertarEnemigos(flashAlertRadius);
        Debug.Log("¡Flash activado!");
    }

    public void Disparar()
    {
        ammo--;
        ActivarDestello(gunFlashIntensity, gunFlashDuration);
        AlertarEnemigos(gunshotAlertRadius);
        Debug.Log("¡Disparo! Munición restante: " + ammo);

        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, shootRange))
        {
            EnemyBlindAI enemy = hit.collider.GetComponent<EnemyBlindAI>();
            if (enemy != null)
            {
                enemy.RecibirDisparo();
            }
        }
    }

    void ActivarDestello(float intensity, float duration)
    {
        if (lightCoroutine != null) StopCoroutine(lightCoroutine);
        lightCoroutine = StartCoroutine(RutinaDestello(intensity, duration));
    }

    IEnumerator RutinaDestello(float maxIntensity, float duration)
    {
        flashLight.intensity = maxIntensity;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            flashLight.intensity = Mathf.Lerp(maxIntensity, 0f, elapsed / duration);
            yield return null;
        }

        flashLight.intensity = 0f;
    }

    void AlertarEnemigos(float radius)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (Collider col in hits)
        {
            EnemyBlindAI enemy = col.GetComponent<EnemyBlindAI>();
            if (enemy != null)
            {
                enemy.EscucharEstímulo(transform.position);
            }
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.gameObject.CompareTag("Ammo"))
        {
            ammo += 3;
            Destroy(hit.gameObject);
            Debug.Log("Munición recolectada. Total: " + ammo);
        }
    }
}