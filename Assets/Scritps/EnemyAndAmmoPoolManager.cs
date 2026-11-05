using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gestor central que implementa los patrones Singleton y Object Pool para administrar enemigos,
/// cajas de munición ("Ammo"), las 3 piezas de llave ("KeyPiece"), la puerta de salida ("Exit")
/// y las ondas visuales de ruido en el suelo.
/// </summary>
public class EnemyAndAmmoPoolManager : MonoBehaviour
{
    // --- Constantes para evitar "magic numbers" en posicionamiento y muestreo de NavMesh ---
    private const int MAX_NAVMESH_SEARCH_ATTEMPTS = 35;
    private const float NAVMESH_SAMPLE_RADIUS = 4f;
    private const float AMMO_GROUND_OFFSET_Y = 0.25f;
    private const float KEY_PIECE_GROUND_OFFSET_Y = 0.35f;
    private const float RIPPLE_GROUND_OFFSET_Y = 0.04f;
    private const float MIN_SEPARATION_BETWEEN_KEYS = 4f;

    /// <summary>
    /// Instancia única del Manager accesible desde cualquier script de la escena (Patrón Singleton).
    /// </summary>
    public static EnemyAndAmmoPoolManager Instance { get; private set; }

    // --- Eventos del Patrón Observer para notificar al HUD ---
    public static event Action<int, int> OnEnemyProgressChanged;
    public static event Action<bool> OnGameEnded; // true = Victoria, false = Derrota

    [Header("Referencias Principales")]
    [Tooltip("Transform del jugador para calcular la distancia mínima de aparición.")]
    public Transform playerTransform;
    [Tooltip("Prefab del enemigo con el script EnemyBlindAI.")]
    public EnemyBlindAI enemyPrefab;
    [Tooltip("Prefab del cubo de munición con el Tag 'Ammo'.")]
    public GameObject ammoPrefab;
    [Tooltip("Prefab de la pieza de llave con el Tag 'KeyPiece' (si se deja vacío, genera esferas amarillas automáticamente).")]
    public GameObject keyPiecePrefab;
    [Tooltip("Referencia a la puerta de salida en la escena con el script ExitDoorController.")]
    public ExitDoorController exitDoor;

    [Header("Configuración de Piezas de Llave (Condición de Victoria)")]
    [Tooltip("Cantidad de piezas de llave que spawnean de forma aleatoria en el mapa al iniciar (por defecto 3).")]
    public int totalKeyPiecesRequired = 3;

    [Header("Configuración de Oleadas de Enemigos")]
    [Tooltip("Suma total máxima de enemigos que aparecerán en toda la partida (iniciales + oleadas).")]
    public int maxTotalEnemies = 12;
    [Tooltip("Enemigos que aparecen en cuanto inicia el juego (1ra oleada).")]
    public int initialEnemies = 3;
    [Tooltip("Enemigos que aparecen en cada oleada subsecuente (por timer o trigger).")]
    public int enemiesPerWave = 3;
    [Tooltip("Segundos entre cada oleada automática.")]
    public float timeBetweenWaves = 10f;
    [Tooltip("Activa o desactiva las oleadas por tiempo.")]
    public bool useWaveTimer = true;

    [Header("Distancias de Spawn (Ajustado a 8.5m tras Playtest Entrega 2)")]
    [Tooltip("Distancia mínima respecto al jugador para que nunca aparezcan demasiado cerca.")]
    public float minSpawnDistanceFromPlayer = 8.5f;
    [Tooltip("Distancia máxima respecto al jugador o centro de zona para buscar un punto caminable.")]
    public float maxSpawnDistanceFromPlayer = 20f;
    [Tooltip("Puntos opcionales de referencia para spawnear (si se deja vacío, usa alrededor del jugador).")]
    public Transform[] spawnZoneCenters;

    [Header("Pool de Munición (Cajas de Balas)")]
    [Tooltip("Cantidad máxima de cajas de munición pre-instanciadas en el Pool.")]
    public int maxAmmoPickupsInPool = 6;
    [Tooltip("Cajas de munición activas al iniciar la partida.")]
    public int initialAmmoPickups = 4;
    [Tooltip("Segundos que tarda una caja recolectada en reaparecer en otro punto caminable.")]
    public float ammoRespawnDelay = 12f;

    [Header("Pool de Guía Visual de Ruido (Ondas de Pasos en el Suelo)")]
    [Tooltip("Cantidad de aros visuales en el pool para mostrar los pasos de los enemigos en la oscuridad.")]
    public int maxNoiseRipplesInPool = 16;
    [Tooltip("Duración en segundos de cada onda visual en el piso.")]
    public float rippleDuration = 0.55f;
    [Tooltip("Diámetro final al que se expande la onda en el suelo.")]
    public float rippleMaxScale = 2.2f;

    // --- Listas del Patrón Object Pool ---
    private readonly List<EnemyBlindAI> enemyPool = new List<EnemyBlindAI>();
    private readonly List<GameObject> ammoPool = new List<GameObject>();
    private readonly List<GameObject> keyPiecePool = new List<GameObject>();
    private readonly List<NoiseRippleEffect> ripplePool = new List<NoiseRippleEffect>();

    private int totalEnemiesSpawnedSoFar = 0;
    private int totalEnemiesDefeated = 0;
    private float waveTimer = 0f;
    private bool isGameEnded = false;

    // Propiedades de solo lectura para que el HUD consulte posiciones y dibuje los popups de proximidad
    public IReadOnlyList<GameObject> ActiveAmmoPickups => ammoPool;
    public IReadOnlyList<GameObject> ActiveKeyPieces => keyPiecePool;

    /// <summary>
    /// Inicializa el Singleton verificando que no existan duplicados en la escena.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Crea las reservas de los pools, coloca las 3 piezas de llave aleatorias y lanza la primera oleada.
    /// </summary>
    void Start()
    {
        Time.timeScale = 1f;

        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        if (exitDoor == null)
        {
            exitDoor = FindFirstObjectByType<ExitDoorController>();
        }

        InicializarPools();

        // 1. Spawnear las 3 piezas de llave en posiciones caminables aleatorias del mapa
        SpawnearPiezasDeLlaveAleatorias();

        // 2. Spawnear 1ra Oleada (Inicial) de enemigos al arrancar el juego
        int firstWaveCount = Mathf.Min(initialEnemies, maxTotalEnemies);
        SpawnearGrupoDeEnemigos(firstWaveCount);

        // 3. Spawnear munición inicial desde el pool
        int firstAmmoCount = Mathf.Min(initialAmmoPickups, maxAmmoPickupsInPool);
        for (int i = 0; i < firstAmmoCount; i++)
        {
            SpawnearMunicionDesdePool();
        }

        OnEnemyProgressChanged?.Invoke(totalEnemiesDefeated, maxTotalEnemies);
    }

    /// <summary>
    /// Avanza el reloj de oleadas hasta alcanzar el tope máximo de enemigos configurados.
    /// </summary>
    void Update()
    {
        if (isGameEnded || !useWaveTimer || totalEnemiesSpawnedSoFar >= maxTotalEnemies) return;

        waveTimer += Time.deltaTime;
        if (waveTimer >= timeBetweenWaves)
        {
            TriggerNextWave();
        }
    }

    /// <summary>
    /// Pre-instancia en memoria los enemigos, cajas de munición, piezas de llave y ondas de pasos.
    /// </summary>
    void InicializarPools()
    {
        if (enemyPrefab != null)
        {
            for (int i = 0; i < maxTotalEnemies; i++)
            {
                EnemyBlindAI newEnemy = Instantiate(enemyPrefab, transform);
                newEnemy.gameObject.SetActive(false);
                enemyPool.Add(newEnemy);
            }
        }

        if (ammoPrefab != null)
        {
            for (int i = 0; i < maxAmmoPickupsInPool; i++)
            {
                GameObject newAmmo = Instantiate(ammoPrefab, transform);
                newAmmo.SetActive(false);
                ammoPool.Add(newAmmo);
            }
        }

        // Pre-instancia las 3 piezas de llave en el pool (si no se asignó Prefab, crea una esfera dorada básica)
        for (int i = 0; i < totalKeyPiecesRequired; i++)
        {
            GameObject keyObj;
            if (keyPiecePrefab != null)
            {
                keyObj = Instantiate(keyPiecePrefab, transform);
            }
            else
            {
                keyObj = CrearPiezaDeLlavePrimitiva(i + 1);
            }
            keyObj.SetActive(false);
            keyPiecePool.Add(keyObj);
        }

        // Genera automáticamente los aros planos para las ondas de pasos
        for (int i = 0; i < maxNoiseRipplesInPool; i++)
        {
            NoiseRippleEffect ripple = NoiseRippleEffect.CreateRippleInstance(transform);
            ripple.gameObject.SetActive(false);
            ripplePool.Add(ripple);
        }
    }

    /// <summary>
    /// Crea una esfera pequeña de respaldo para la pieza de llave si el usuario no asignó un Prefab en el Inspector.
    /// </summary>
    GameObject CrearPiezaDeLlavePrimitiva(int index)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = $"KeyPiece_{index}";
        sphere.transform.SetParent(transform);
        sphere.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
        sphere.tag = "KeyPiece";

        Renderer rend = sphere.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(1f, 0.85f, 0.2f);
            rend.material = mat;
        }
        return sphere;
    }

    /// <summary>
    /// Coloca las 3 piezas de llave en puntos caminables aleatorios del NavMesh cuidando que no queden encimadas.
    /// </summary>
    void SpawnearPiezasDeLlaveAleatorias()
    {
        List<Vector3> chosenLocations = new List<Vector3>();

        foreach (GameObject keyObj in keyPiecePool)
        {
            Vector3 finalPos = Vector3.zero;
            bool foundSpot = false;

            for (int attempt = 0; attempt < MAX_NAVMESH_SEARCH_ATTEMPTS; attempt++)
            {
                if (EncontrarPuntoCaminableValido(out Vector3 candidatePos, null))
                {
                    bool farFromOtherKeys = true;
                    foreach (Vector3 prevPos in chosenLocations)
                    {
                        if (Vector3.Distance(candidatePos, prevPos) < MIN_SEPARATION_BETWEEN_KEYS)
                        {
                            farFromOtherKeys = false;
                            break;
                        }
                    }

                    if (farFromOtherKeys)
                    {
                        finalPos = candidatePos;
                        foundSpot = true;
                        break;
                    }
                }
            }

            if (!foundSpot && EncontrarPuntoCaminableValido(out Vector3 fallbackPos, null))
            {
                finalPos = fallbackPos;
                foundSpot = true;
            }

            if (foundSpot)
            {
                chosenLocations.Add(finalPos);
                keyObj.transform.position = finalPos + Vector3.up * KEY_PIECE_GROUND_OFFSET_Y;
                keyObj.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Desactiva una pieza de llave recolectada y la mantiene en el pool.
    /// </summary>
    public void ReturnKeyPieceToPool(GameObject keyObj)
    {
        if (keyObj != null)
        {
            keyObj.SetActive(false);
        }
    }

    /// <summary>
    /// Activa la siguiente oleada respetando el máximo total de enemigos y reiniciando el reloj.
    /// </summary>
    public void TriggerNextWave(Transform customZoneCenter = null)
    {
        if (isGameEnded || totalEnemiesSpawnedSoFar >= maxTotalEnemies)
        {
            return;
        }

        waveTimer = 0f;
        int remainingQuota = maxTotalEnemies - totalEnemiesSpawnedSoFar;
        int countToSpawn = Mathf.Min(enemiesPerWave, remainingQuota);

        SpawnearGrupoDeEnemigos(countToSpawn, customZoneCenter);
    }

    /// <summary>
    /// Extrae enemigos inactivos del pool y los coloca en puntos caminables válidos del NavMesh.
    /// </summary>
    void SpawnearGrupoDeEnemigos(int count, Transform customZoneCenter = null)
    {
        for (int i = 0; i < count; i++)
        {
            if (totalEnemiesSpawnedSoFar >= maxTotalEnemies) break;

            EnemyBlindAI enemy = ObtenerEnemigoInactivoDelPool();
            if (enemy != null && EncontrarPuntoCaminableValido(out Vector3 validPos, customZoneCenter))
            {
                enemy.ActivarDesdePool(validPos);
                totalEnemiesSpawnedSoFar++;
            }
        }
    }

    EnemyBlindAI ObtenerEnemigoInactivoDelPool()
    {
        foreach (EnemyBlindAI enemy in enemyPool)
        {
            if (!enemy.gameObject.activeInHierarchy)
            {
                return enemy;
            }
        }
        return null;
    }

    /// <summary>
    /// Devuelve un enemigo eliminado al pool y actualiza el progreso en el HUD.
    /// </summary>
    public void ReturnEnemyToPool(EnemyBlindAI enemy)
    {
        enemy.gameObject.SetActive(false);
        totalEnemiesDefeated++;
        OnEnemyProgressChanged?.Invoke(totalEnemiesDefeated, maxTotalEnemies);
    }

    /// <summary>
    /// Activa una caja de munición inactiva del pool en un punto caminable válido.
    /// </summary>
    public void SpawnearMunicionDesdePool()
    {
        foreach (GameObject ammoObj in ammoPool)
        {
            if (!ammoObj.activeInHierarchy)
            {
                if (EncontrarPuntoCaminableValido(out Vector3 validPos, null))
                {
                    ammoObj.transform.position = validPos + Vector3.up * AMMO_GROUND_OFFSET_Y;
                    ammoObj.SetActive(true);
                }
                return;
            }
        }
    }

    /// <summary>
    /// Devuelve una caja de munición recolectada al pool y programa su reaparición.
    /// </summary>
    public void ReturnAmmoToPool(GameObject ammoObj)
    {
        ammoObj.SetActive(false);
        if (ammoRespawnDelay > 0f && !isGameEnded)
        {
            StartCoroutine(RutinaReaparicionMunicion());
        }
    }

    IEnumerator RutinaReaparicionMunicion()
    {
        yield return new WaitForSeconds(ammoRespawnDelay);
        SpawnearMunicionDesdePool();
    }

    /// <summary>
    /// Extrae una onda visual de ruido del pool y la coloca al ras del piso donde pisó el enemigo.
    /// </summary>
    public void SpawnFootstepRipple(Vector3 footPosition, bool isChasing)
    {
        foreach (NoiseRippleEffect ripple in ripplePool)
        {
            if (!ripple.gameObject.activeInHierarchy)
            {
                Vector3 spawnPos = new Vector3(footPosition.x, footPosition.y + RIPPLE_GROUND_OFFSET_Y, footPosition.z);
                ripple.PlayRipple(spawnPos, rippleDuration, rippleMaxScale, isChasing);
                return;
            }
        }
    }

    /// <summary>
    /// Declara estado de Victoria cuando el jugador interactúa con la Puerta de Salida teniendo las 3 piezas de llave.
    /// </summary>
    public void TriggerVictory()
    {
        if (isGameEnded) return;
        isGameEnded = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        OnGameEnded?.Invoke(true);
    }

    /// <summary>
    /// Declara estado de Derrota (Game Over) cuando un enemigo alcanza al jugador.
    /// </summary>
    public void TriggerGameOver()
    {
        if (isGameEnded) return;
        isGameEnded = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        OnGameEnded?.Invoke(false);
    }

    /// <summary>
    /// Busca una coordenada sobre el NavMesh que respete la distancia mínima respecto al jugador.
    /// </summary>
    bool EncontrarPuntoCaminableValido(out Vector3 result, Transform preferredCenter)
    {
        Vector3 playerPos = playerTransform != null ? playerTransform.position : Vector3.zero;

        for (int attempt = 0; attempt < MAX_NAVMESH_SEARCH_ATTEMPTS; attempt++)
        {
            Vector3 searchCenter = playerPos;

            if (preferredCenter != null)
            {
                searchCenter = preferredCenter.position;
            }
            else if (spawnZoneCenters != null && spawnZoneCenters.Length > 0)
            {
                Transform randomZone = spawnZoneCenters[UnityEngine.Random.Range(0, spawnZoneCenters.Length)];
                if (randomZone != null) searchCenter = randomZone.position;
            }

            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized;
            if (randomCircle == Vector2.zero) randomCircle = Vector2.right;

            float randomDist = UnityEngine.Random.Range(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);
            Vector3 candidatePoint = searchCenter + new Vector3(randomCircle.x * randomDist, 0f, randomCircle.y * randomDist);

            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                if (Vector3.Distance(hit.position, playerPos) >= minSpawnDistanceFromPlayer)
                {
                    result = hit.position;
                    return true;
                }
            }
        }

        result = Vector3.zero;
        return false;
    }
}