using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlador de la Puerta de Salida ("Exit") que implementa los patrones State, Observer y Component.
/// Permanece completamente a oscuras hasta que el jugador reúne las 3 piezas de llave; en ese momento
/// genera e ilumina únicamente su contorno (4 barras de marco) con un parpadeo lento sin iluminar el resto del mapa.
/// </summary>
public class ExitDoorController : MonoBehaviour
{
    // --- Constantes para el grosor del marco luminoso y evitar "magic numbers" ---
    private const float OUTLINE_BAR_THICKNESS = 0.06f;
    private const float OUTLINE_DEPTH_EXTRA = 0.04f;

    /// <summary>
    /// Estados de la puerta dentro del patrón State.
    /// </summary>
    public enum EstadoPuerta
    {
        Bloqueada,
        DesbloqueadaParpadeando,
        Abierta
    }

    [Header("Estado e Interacción de la Puerta")]
    [Tooltip("Estado actual de la puerta de salida.")]
    public EstadoPuerta estadoActual = EstadoPuerta.Bloqueada;
    [Tooltip("Distancia máxima en metros a la que el jugador puede abrir la puerta presionando la tecla de interacción.")]
    public float interactionDistance = 2.6f;

    [Header("Configuración del Contorno Parpadeante (Al tener las 3 llaves)")]
    [Tooltip("Color del contorno luminoso de la puerta cuando ya se tienen las 3 piezas.")]
    public Color outlineGlowColor = new Color(1f, 0.82f, 0.28f, 1f);
    [Tooltip("Velocidad del parpadeo lento del contorno (ciclos por segundo).")]
    public float outlinePulseSpeed = 1.2f;
    [Tooltip("Intensidad mínima del contorno durante el parpadeo (0 = se apaga por completo).")]
    public float minOutlineBrightness = 0.05f;
    [Tooltip("Intensidad máxima del contorno durante el parpadeo.")]
    public float maxOutlineBrightness = 0.85f;

    private readonly List<Renderer> outlineRenderers = new List<Renderer>();
    private Material outlineUnlitMaterial;

    /// <summary>
    /// Se suscribe al evento de recolección de piezas de llave (Patrón Observer).
    /// </summary>
    void OnEnable()
    {
        PlayerPrototypeController.OnKeyPiecesChanged += HandleKeyPiecesChanged;
    }

    void OnDisable()
    {
        PlayerPrototypeController.OnKeyPiecesChanged -= HandleKeyPiecesChanged;
    }

    void Start()
    {
        CrearMarcoDeContorno();
        AplicarBrilloAlContorno(0f);
    }

    /// <summary>
    /// Si la puerta ya está desbloqueada, hace parpadear lentamente solo las barras del contorno.
    /// </summary>
    void Update()
    {
        if (estadoActual != EstadoPuerta.DesbloqueadaParpadeando) return;

        // Onda senoidal suave entre 0 y 1 para un parpadeo lento y orgánico
        float wave = (Mathf.Sin(Time.time * outlinePulseSpeed * Mathf.PI) + 1f) * 0.5f;
        float currentBrightness = Mathf.Lerp(minOutlineBrightness, maxOutlineBrightness, wave);
        AplicarBrilloAlContorno(currentBrightness);
    }

    /// <summary>
    /// Escucha cuántas piezas de llave lleva el jugador; al juntar las 3 requeridas, activa el contorno parpadeante.
    /// </summary>
    private void HandleKeyPiecesChanged(int collected, int required)
    {
        if (estadoActual == EstadoPuerta.Abierta) return;

        if (collected >= required && required > 0)
        {
            estadoActual = EstadoPuerta.DesbloqueadaParpadeando;
            SetOutlineVisible(true);
            Debug.Log("¡Las 3 piezas de la llave fueron recolectadas! El contorno de la puerta de salida comenzó a parpadear.");
        }
        else
        {
            estadoActual = EstadoPuerta.Bloqueada;
            SetOutlineVisible(false);
        }
    }

    /// <summary>
    /// Verifica si el jugador está lo suficientemente cerca y tiene las 3 piezas cuando presiona la tecla de interacción (E).
    /// </summary>
    public void TryInteract(Vector3 playerPosition, int collectedKeys, int requiredKeys)
    {
        float distance = Vector3.Distance(transform.position, playerPosition);
        if (distance > interactionDistance) return;

        if (collectedKeys >= requiredKeys && estadoActual == EstadoPuerta.DesbloqueadaParpadeando)
        {
            estadoActual = EstadoPuerta.Abierta;
            Debug.Log("¡Puerta de salida abierta! ¡VICTORIA!");

            if (EnemyAndAmmoPoolManager.Instance != null)
            {
                EnemyAndAmmoPoolManager.Instance.TriggerVictory();
            }
        }
        else
        {
            Debug.Log($"La puerta está cerrada con llave. Piezas recolectadas: {collectedKeys}/{requiredKeys}.");
        }
    }

    /// <summary>
    /// Construye automáticamente 4 barras delgadas alrededor de los bordes del cubo de la puerta
    /// con un Shader Unlit para que solo brille el contorno sin iluminar el piso ni las paredes.
    /// </summary>
    void CrearMarcoDeContorno()
    {
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Standard");
        }

        outlineUnlitMaterial = new Material(unlitShader);
        outlineUnlitMaterial.color = Color.black;

        // Posiciones y escalas locales relativas a un cubo estándar (1x1x1 escalado en la escena)
        CrearBarraDeContorno("Outline_Top", new Vector3(0f, 0.5f, 0f), new Vector3(1.04f, OUTLINE_BAR_THICKNESS, 1f + OUTLINE_DEPTH_EXTRA));
        CrearBarraDeContorno("Outline_Bottom", new Vector3(0f, -0.5f, 0f), new Vector3(1.04f, OUTLINE_BAR_THICKNESS, 1f + OUTLINE_DEPTH_EXTRA));
        CrearBarraDeContorno("Outline_Left", new Vector3(-0.5f, 0f, 0f), new Vector3(OUTLINE_BAR_THICKNESS, 1.04f, 1f + OUTLINE_DEPTH_EXTRA));
        CrearBarraDeContorno("Outline_Right", new Vector3(0.5f, 0f, 0f), new Vector3(OUTLINE_BAR_THICKNESS, 1.04f, 1f + OUTLINE_DEPTH_EXTRA));

        SetOutlineVisible(false);
    }

    void CrearBarraDeContorno(string barName, Vector3 localPos, Vector3 localScale)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = barName;
        bar.transform.SetParent(transform, false);
        bar.transform.localPosition = localPos;
        bar.transform.localScale = localScale;

        Collider col = bar.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rend = bar.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = outlineUnlitMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            outlineRenderers.Add(rend);
        }
    }

    void SetOutlineVisible(bool visible)
    {
        foreach (Renderer rend in outlineRenderers)
        {
            if (rend != null)
            {
                rend.enabled = visible;
            }
        }
    }

    void AplicarBrilloAlContorno(float brightness)
    {
        if (outlineUnlitMaterial == null) return;

        Color currentColor = Color.Lerp(Color.black, outlineGlowColor, brightness);
        outlineUnlitMaterial.color = currentColor;
        if (outlineUnlitMaterial.HasProperty("_BaseColor"))
        {
            outlineUnlitMaterial.SetColor("_BaseColor", currentColor);
        }
    }
}