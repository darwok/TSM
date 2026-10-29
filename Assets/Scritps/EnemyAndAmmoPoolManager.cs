using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAndAmmoPoolManager : MonoBehaviour
{
    public static EnemyAndAmmoPoolManager Instance { get; private set; }

    [Header("Referencias Principales")]
    public Transform playerTransform;
    public EnemyBlindAI enemyPrefab;
    public GameObject ammoPrefab;

    [Header("Configuración de Oleadas de Enemigos")]
    [Tooltip("Suma total máxima de enemigos que aparecerán en toda la partida (iniciales + oleadas).")]
    public int maxTotalEnemies = 12;
    [Tooltip("Enemigos que aparecen en cuanto inicia el juego (1ra oleada).")]
    public int initialEnemies = 3;
    [Tooltip("Enemigos que aparecen en cada oleada subsecuente (por timer o trigger).")]
    public int enemiesPerWave = 3;
    [Tooltip("Segundos entre cada oleada automática.")]
    public float timeBetweenWaves = 10f;
    public bool useWaveTimer = true;

    [Header("Distancias de Spawn (Enemigos y Munición)")]
    [Tooltip("Distancia mínima respecto al jugador para que nunca aparezcan en su cara.")]
    public float minSpawnDistanceFromPlayer = 6f;
    [Tooltip("Distancia máxima respecto al jugador o centro de zona para buscar un punto caminable.")]
    public float maxSpawnDistanceFromPlayer = 20f;
    [Tooltip("Puntos opcionales de referencia para spawnear (si se deja vacío, usa alrededor del jugador).")]
    public Transform[] spawnZoneCenters;

    [Header("Pool de Munición (Cajas de Balas)")]
    public int maxAmmoPickupsInPool = 6;
    public int initialAmmoPickups = 4;
    public float ammoRespawnDelay = 12f;

    private List<EnemyBlindAI> enemyPool = new List<EnemyBlindAI>();
    private List<GameObject> ammoPool = new List<GameObject>();

    private int totalEnemiesSpawnedSoFar = 0;
    private float waveTimer = 0f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        InicializarPools();

        // 1ra Oleada (Inicial) al arrancar el juego
        int firstWaveCount = Mathf.Min(initialEnemies, maxTotalEnemies);
        SpawnearGrupoDeEnemigos(firstWaveCount);

        // Spawnear munición inicial desde el pool
        int firstAmmoCount = Mathf.Min(initialAmmoPickups, maxAmmoPickupsInPool);
        for (int i = 0; i < firstAmmoCount; i++)
        {
            SpawnearMunicionDesdePool();
        }
    }

    void Update()
    {
        // Si ya se alcanzó el tope máximo de enemigos de la partida, detenemos el timer
        if (!useWaveTimer || totalEnemiesSpawnedSoFar >= maxTotalEnemies) return;

        waveTimer += Time.deltaTime;
        if (waveTimer >= timeBetweenWaves)
        {
            TriggerNextWave();
        }
    }

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
    }

    // Llamado tanto por el Timer como por un Trigger en el mapa; reinicia el reloj de oleadas
    public void TriggerNextWave(Transform customZoneCenter = null)
    {
        if (totalEnemiesSpawnedSoFar >= maxTotalEnemies)
        {
            Debug.Log("Ya se alcanzó el máximo total de enemigos (" + maxTotalEnemies + "). No spawnean más.");
            return;
        }

        waveTimer = 0f; // Reinicia el timer para que la siguiente oleada cuente desde este momento
        int remainingQuota = maxTotalEnemies - totalEnemiesSpawnedSoFar;
        int countToSpawn = Mathf.Min(enemiesPerWave, remainingQuota);

        Debug.Log($"¡Nueva oleada! Spawneando {countToSpawn} enemigos (Total acumulado: {totalEnemiesSpawnedSoFar + countToSpawn}/{maxTotalEnemies}).");
        SpawnearGrupoDeEnemigos(countToSpawn, customZoneCenter);
    }

    void SpawnearGrupoDeEnemigos(int count, Transform customZoneCenter = null)
    {
        for (int i = 0; i < count; i++)
        {
            if (totalEnemiesSpawnedSoFar >= maxTotalEnemies) break;

            EnemyBlindAI enemy = ObtenerEnemigoInactivoDelPool();
            if (enemy != null)
            {
                if (EncontrarPuntoCaminableValido(out Vector3 validPos, customZoneCenter))
                {
                    enemy.ActivarDesdePool(validPos);
                    totalEnemiesSpawnedSoFar++;
                }
                else
                {
                    Debug.LogWarning("No se encontró un punto caminable válido lejos del jugador tras varios intentos.");
                }
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

    public void ReturnEnemyToPool(EnemyBlindAI enemy)
    {
        enemy.gameObject.SetActive(false);
    }

    // --- POOL DE MUNICIÓN ---
    public void SpawnearMunicionDesdePool()
    {
        foreach (GameObject ammoObj in ammoPool)
        {
            if (!ammoObj.activeInHierarchy)
            {
                if (EncontrarPuntoCaminableValido(out Vector3 validPos, null))
                {
                    ammoObj.transform.position = validPos + Vector3.up * 0.25f;
                    ammoObj.SetActive(true);
                }
                return;
            }
        }
    }

    public void ReturnAmmoToPool(GameObject ammoObj)
    {
        ammoObj.SetActive(false);
        if (ammoRespawnDelay > 0f)
        {
            StartCoroutine(RutinaReaparicionMunicion());
        }
    }

    IEnumerator RutinaReaparicionMunicion()
    {
        yield return new WaitForSeconds(ammoRespawnDelay);
        SpawnearMunicionDesdePool();
    }

    // --- BÚSQUEDA DE PUNTO CAMINABLE EN NAVMESH RESPETANDO DISTANCIA MÍNIMA AL JUGADOR ---
    bool EncontrarPuntoCaminableValido(out Vector3 result, Transform preferredCenter)
    {
        Vector3 playerPos = playerTransform != null ? playerTransform.position : Vector3.zero;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 searchCenter = playerPos;

            if (preferredCenter != null)
            {
                searchCenter = preferredCenter.position;
            }
            else if (spawnZoneCenters != null && spawnZoneCenters.Length > 0)
            {
                Transform randomZone = spawnZoneCenters[Random.Range(0, spawnZoneCenters.Length)];
                if (randomZone != null) searchCenter = randomZone.position;
            }

            // Dirección horizontal aleatoria a una distancia entre el mínimo y el máximo
            Vector2 randomCircle = Random.insideUnitCircle.normalized;
            if (randomCircle == Vector2.zero) randomCircle = Vector2.right;

            float randomDist = Random.Range(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);
            Vector3 candidatePoint = searchCenter + new Vector3(randomCircle.x * randomDist, 0f, randomCircle.y * randomDist);

            // Verificamos que caiga dentro de una superficie caminable del NavMesh
            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                // Confirmamos que el punto en el NavMesh no esté más cerca del jugador que la distancia mínima permitida
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