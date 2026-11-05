using System.Collections;
using UnityEngine;

/// <summary>
/// Efecto visual de onda en el piso que simula el rastro auditivo de los pasos de un enemigo en la oscuridad.
/// Utiliza un material emisivo propio (Unlit/Color) para ser sutilmente visible incluso con las luces apagadas.
/// </summary>
public class NoiseRippleEffect : MonoBehaviour
{
    // --- Constantes de grosor y color para evitar "magic numbers" ---
    private const float FLAT_HEIGHT_SCALE = 0.015f;
    private const float INITIAL_HORIZONTAL_SCALE = 0.25f;

    private static readonly Color PATROL_RIPPLE_COLOR = new Color(0.55f, 0.75f, 0.95f, 1f);
    private static readonly Color CHASE_RIPPLE_COLOR = new Color(0.95f, 0.25f, 0.25f, 1f);

    private Renderer meshRenderer;
    private Material runtimeMaterial;
    private Coroutine rippleCoroutine;

    /// <summary>
    /// Crea automáticamente una instancia de disco plano sin colisión lista para el Object Pool.
    /// </summary>
    public static NoiseRippleEffect CreateRippleInstance(Transform parent)
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "FootstepNoiseRipple_Pooled";
        cylinder.transform.SetParent(parent);

        // Quitamos el Collider para que no interfiera con el jugador ni con los Raycasts
        Collider col = cylinder.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        NoiseRippleEffect effect = cylinder.AddComponent<NoiseRippleEffect>();
        effect.ConfigurarMaterialUnlit();
        return effect;
    }

    /// <summary>
    /// Asigna un Shader Unlit para que la onda se distinga suavemente en el suelo aunque FlashLight esté en 0.
    /// </summary>
    void ConfigurarMaterialUnlit()
    {
        meshRenderer = GetComponent<Renderer>();
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Standard");
        }

        runtimeMaterial = new Material(unlitShader);
        runtimeMaterial.color = PATROL_RIPPLE_COLOR;
        meshRenderer.material = runtimeMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    /// <summary>
    /// Activa la onda desde el Object Pool y la expande en el suelo antes de volver a apagarse.
    /// </summary>
    public void PlayRipple(Vector3 position, float duration, float maxHorizontalScale, bool isChasing)
    {
        transform.position = position;
        transform.localScale = new Vector3(INITIAL_HORIZONTAL_SCALE, FLAT_HEIGHT_SCALE, INITIAL_HORIZONTAL_SCALE);
        gameObject.SetActive(true);

        if (rippleCoroutine != null)
        {
            StopCoroutine(rippleCoroutine);
        }
        rippleCoroutine = StartCoroutine(RutinaExpansion(duration, maxHorizontalScale, isChasing));
    }

    IEnumerator RutinaExpansion(float duration, float maxScale, bool isChasing)
    {
        Color startColor = isChasing ? CHASE_RIPPLE_COLOR : PATROL_RIPPLE_COLOR;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Se expande horizontalmente mientras oscurece su color hasta desaparecer en el fondo negro
            float currentScale = Mathf.Lerp(INITIAL_HORIZONTAL_SCALE, maxScale, t);
            transform.localScale = new Vector3(currentScale, FLAT_HEIGHT_SCALE, currentScale);

            if (runtimeMaterial != null)
            {
                Color fadedColor = Color.Lerp(startColor, Color.black, t);
                runtimeMaterial.color = fadedColor;
                if (runtimeMaterial.HasProperty("_BaseColor"))
                {
                    runtimeMaterial.SetColor("_BaseColor", fadedColor);
                }
            }

            yield return null;
        }

        gameObject.SetActive(false);
    }
}