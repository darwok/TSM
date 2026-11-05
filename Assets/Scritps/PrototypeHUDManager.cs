using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gestor de interfaz minimalista (Patrón Observer) que dibuja la mira central, el contador de balas,
/// las piezas de llave recolectadas (0/3), el estado del flash y los Tags / Popups dinámicos en pantalla
/// cuando el jugador está cerca de munición ("Ammo"), una pieza de llave ("Key Piece") o la puerta ("Exit").
/// </summary>
public class PrototypeHUDManager : MonoBehaviour
{
    // --- Constantes de diseño de interfaz para evitar "magic numbers" ---
    private const float CROSSHAIR_SIZE = 6f;
    private const float HUD_BOX_WIDTH = 365f;
    private const float HUD_BOX_HEIGHT = 140f;
    private const float HUD_MARGIN = 16f;
    private const float POPUP_WIDTH = 230f;
    private const float POPUP_HEIGHT = 34f;
    private const float POPUP_WORLD_Y_OFFSET = 0.65f;

    [Header("Configuración del HUD y Popups de Proximidad")]
    [Tooltip("Muestra el punto blanco central (mira), el contador de balas/llaves y los popups durante el juego.")]
    public bool showInGameHUD = true;
    [Tooltip("Distancia en metros a la que aparece en pantalla el tag/popup de Ammo, Key Piece o Exit.")]
    public float proximityPopupDistance = 3.2f;
    [Tooltip("Nombre de la escena principal del juego para el botón Jugar / Reiniciar.")]
    public string gameSceneName = "SampleScene";
    [Tooltip("Nombre de la escena del Menú Principal.")]
    public string mainMenuSceneName = "MainMenu";

    private int currentAmmo = 5;
    private int collectedKeys = 0;
    private int requiredKeys = 3;
    private int defeatedEnemies = 0;
    private int totalEnemies = 12;
    private float flashReadyTime = 0f;
    private bool gameEnded = false;
    private bool playerWon = false;

    private PlayerPrototypeController playerController;
    private Camera mainCam;
    private Texture2D whiteTexture;
    private GUIStyle hudTextStyle;
    private GUIStyle popupStyle;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;

    /// <summary>
    /// Se suscribe a los eventos del jugador y del Manager (Patrón Observer).
    /// </summary>
    void OnEnable()
    {
        PlayerPrototypeController.OnAmmoChanged += HandleAmmoChanged;
        PlayerPrototypeController.OnFlashCooldownStarted += HandleFlashCooldown;
        PlayerPrototypeController.OnKeyPiecesChanged += HandleKeyPiecesChanged;
        EnemyAndAmmoPoolManager.OnEnemyProgressChanged += HandleEnemyProgressChanged;
        EnemyAndAmmoPoolManager.OnGameEnded += HandleGameEnded;
    }

    /// <summary>
    /// Se desuscribe de todos los eventos al desactivarse para evitar fugas de memoria.
    /// </summary>
    void OnDisable()
    {
        PlayerPrototypeController.OnAmmoChanged -= HandleAmmoChanged;
        PlayerPrototypeController.OnFlashCooldownStarted -= HandleFlashCooldown;
        PlayerPrototypeController.OnKeyPiecesChanged -= HandleKeyPiecesChanged;
        EnemyAndAmmoPoolManager.OnEnemyProgressChanged -= HandleEnemyProgressChanged;
        EnemyAndAmmoPoolManager.OnGameEnded -= HandleGameEnded;
    }

    void Start()
    {
        whiteTexture = new Texture2D(1, 1);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();

        mainCam = Camera.main;
        playerController = FindFirstObjectByType<PlayerPrototypeController>();
    }

    private void HandleAmmoChanged(int newAmmo)
    {
        currentAmmo = newAmmo;
    }

    private void HandleFlashCooldown(float cooldownDuration)
    {
        flashReadyTime = Time.time + cooldownDuration;
    }

    private void HandleKeyPiecesChanged(int collected, int required)
    {
        collectedKeys = collected;
        requiredKeys = required;
    }

    private void HandleEnemyProgressChanged(int defeated, int total)
    {
        defeatedEnemies = defeated;
        totalEnemies = total;
    }

    private void HandleGameEnded(bool won)
    {
        gameEnded = true;
        playerWon = won;
    }

    /// <summary>
    /// Dibuja la mira discreta, el panel de estado (balas y llaves), los popups de proximidad y la pantalla final.
    /// </summary>
    void OnGUI()
    {
        if (!showInGameHUD) return;

        InicializarEstilosGUI();

        // 1. Mira central discreta y Popups de proximidad ("Ammo", "Key Piece", "Exit")
        if (!gameEnded)
        {
            float centerX = (Screen.width - CROSSHAIR_SIZE) * 0.5f;
            float centerY = (Screen.height - CROSSHAIR_SIZE) * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.DrawTexture(new Rect(centerX, centerY, CROSSHAIR_SIZE, CROSSHAIR_SIZE), whiteTexture);
            GUI.color = Color.white;

            DibujarPopupsDeProximidad();
        }

        // 2. Panel superior izquierdo: Balas, Piezas de Llave (0/3), Estado del Flash y Enemigos
        Rect hudRect = new Rect(HUD_MARGIN, HUD_MARGIN, HUD_BOX_WIDTH, HUD_BOX_HEIGHT);
        GUI.Box(hudRect, GUIContent.none);

        string flashStatus = (Time.time >= flashReadyTime)
            ? "LISTO [Clic Der / F]"
            : $"RECARGANDO ({(flashReadyTime - Time.time):F1}s)";

        string keyStatus = (collectedKeys >= requiredKeys)
            ? $"{collectedKeys} / {requiredKeys} (¡SALIDA ILUMINADA!)"
            : $"{collectedKeys} / {requiredKeys}";

        GUI.Label(new Rect(HUD_MARGIN + 12f, HUD_MARGIN + 10f, HUD_BOX_WIDTH - 20f, 26f),
            $"Balas disponibles: {currentAmmo}", hudTextStyle);
        GUI.Label(new Rect(HUD_MARGIN + 12f, HUD_MARGIN + 38f, HUD_BOX_WIDTH - 20f, 26f),
            $"Piezas de Llave: {keyStatus}", hudTextStyle);
        GUI.Label(new Rect(HUD_MARGIN + 12f, HUD_MARGIN + 66f, HUD_BOX_WIDTH - 20f, 26f),
            $"Flash de Cámara: {flashStatus}", hudTextStyle);
        GUI.Label(new Rect(HUD_MARGIN + 12f, HUD_MARGIN + 94f, HUD_BOX_WIDTH - 20f, 26f),
            $"Enemigos eliminados: {defeatedEnemies} / {totalEnemies}", hudTextStyle);

        // 3. Pantalla superpuesta de Victoria o Derrota
        if (gameEnded)
        {
            DibujarPantallaFinDePartida();
        }
    }

    /// <summary>
    /// Muestra en pantalla un tag/popup flotante cuando el jugador está cerca de munición, piezas de llave o la puerta.
    /// </summary>
    void DibujarPopupsDeProximidad()
    {
        if (EnemyAndAmmoPoolManager.Instance == null) return;
        if (mainCam == null) mainCam = Camera.main;
        if (playerController == null) playerController = FindFirstObjectByType<PlayerPrototypeController>();
        if (mainCam == null || playerController == null) return;

        Vector3 playerPos = playerController.transform.position;

        // 1. Popups para cajas de munición cercanas ("Ammo")
        foreach (GameObject ammoObj in EnemyAndAmmoPoolManager.Instance.ActiveAmmoPickups)
        {
            if (ammoObj != null && ammoObj.activeInHierarchy)
            {
                float dist = Vector3.Distance(playerPos, ammoObj.transform.position);
                if (dist <= proximityPopupDistance)
                {
                    DibujarEtiquetaEnPantalla(ammoObj.transform.position + Vector3.up * POPUP_WORLD_Y_OFFSET, "Ammo");
                }
            }
        }

        // 2. Popups para piezas de llave cercanas ("Key Piece")
        foreach (GameObject keyObj in EnemyAndAmmoPoolManager.Instance.ActiveKeyPieces)
        {
            if (keyObj != null && keyObj.activeInHierarchy)
            {
                float dist = Vector3.Distance(playerPos, keyObj.transform.position);
                if (dist <= proximityPopupDistance)
                {
                    DibujarEtiquetaEnPantalla(keyObj.transform.position + Vector3.up * POPUP_WORLD_Y_OFFSET, "Key Piece");
                }
            }
        }

        // 3. Popup para la puerta de salida cercana ("Exit") indicando la tecla configurada en el Inspector
        ExitDoorController exitDoor = EnemyAndAmmoPoolManager.Instance.exitDoor;
        if (exitDoor != null && exitDoor.gameObject.activeInHierarchy)
        {
            float distToDoor = Vector3.Distance(playerPos, exitDoor.transform.position);
            float maxDoorPopupDist = Mathf.Max(proximityPopupDistance, exitDoor.interactionDistance + 0.5f);

            if (distToDoor <= maxDoorPopupDist)
            {
                string keyName = playerController.interactKey.ToString();
                string exitLabel = (collectedKeys >= requiredKeys)
                    ? $"Exit [{keyName}]"
                    : $"Exit (Llaves: {collectedKeys}/{requiredKeys})";

                DibujarEtiquetaEnPantalla(exitDoor.transform.position + Vector3.up * 0.5f, exitLabel);
            }
        }
    }

    /// <summary>
    /// Convierte la posición 3D del objeto a coordenadas de pantalla 2D (o lo muestra abajo al centro si está a los pies).
    /// </summary>
    void DibujarEtiquetaEnPantalla(Vector3 worldPos, string labelText)
    {
        Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);

        float rectX;
        float rectY;

        if (screenPos.z > 0f)
        {
            rectX = Mathf.Clamp(screenPos.x - (POPUP_WIDTH * 0.5f), 10f, Screen.width - POPUP_WIDTH - 10f);
            rectY = Mathf.Clamp(Screen.height - screenPos.y - (POPUP_HEIGHT * 0.5f), 10f, Screen.height - POPUP_HEIGHT - 10f);
        }
        else
        {
            // Si el objeto está justo a las espaldas/pies dentro del radio corto, se muestra en la parte inferior central
            rectX = (Screen.width - POPUP_WIDTH) * 0.5f;
            rectY = Screen.height - 95f;
        }

        Rect popupRect = new Rect(rectX, rectY, POPUP_WIDTH, POPUP_HEIGHT);
        GUI.Box(popupRect, $"[ {labelText} ]", popupStyle);
    }

    void InicializarEstilosGUI()
    {
        if (hudTextStyle == null)
        {
            hudTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold
            };
            hudTextStyle.normal.textColor = new Color(0.9f, 0.94f, 1f, 1f);
        }

        if (popupStyle == null)
        {
            popupStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            popupStyle.normal.textColor = new Color(1f, 0.92f, 0.45f, 1f);
        }

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
        }
    }

    void DibujarPantallaFinDePartida()
    {
        float panelW = 500f;
        float panelH = 240f;
        float panelX = (Screen.width - panelW) * 0.5f;
        float panelY = (Screen.height - panelH) * 0.5f;

        GUI.Box(new Rect(panelX, panelY, panelW, panelH), GUIContent.none);

        titleStyle.normal.textColor = playerWon ? new Color(0.4f, 0.95f, 0.5f) : new Color(0.95f, 0.3f, 0.3f);
        string titulo = playerWon ? "¡ABRISTE LA PUERTA Y ESCAPASTE!" : "¡TE ALCANZARON EN LA OSCURIDAD!";
        GUI.Label(new Rect(panelX + 20f, panelY + 25f, panelW - 40f, 50f), titulo, titleStyle);

        if (GUI.Button(new Rect(panelX + 80f, panelY + 100f, panelW - 160f, 48f), "Reiniciar Partida", buttonStyle))
        {
            ReiniciarNivelActual();
        }

        if (GUI.Button(new Rect(panelX + 80f, panelY + 162f, panelW - 160f, 48f), "Salir del Juego", buttonStyle))
        {
            SalirDelJuego();
        }
    }

    // --- Funciones públicas para conectar también a botones UI de las escenas MainMenu, Victory y GameOver ---

    public void ReiniciarNivelActual()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void CargarEscenaDeJuego()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameSceneName);
    }

    public void CargarMenuPrincipal()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void SalirDelJuego()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}