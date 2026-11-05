using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Inteligencia artificial del enemigo basada en el patrón State (Máquina de Estados Finitos: Patrulla, Persecución y Ataque).
/// Se integra con el Object Pool y emite ondas visuales en el piso al caminar cerca del jugador para simular el sonido en la oscuridad.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyBlindAI : MonoBehaviour
{
    // --- Constantes para umbrales de navegación y evitar "magic numbers" ---
    private const float PATROL_ARRIVAL_THRESHOLD = 0.5f;
    private const float CHASE_ARRIVAL_THRESHOLD = 0.6f;
    private const float MOVING_VELOCITY_THRESHOLD_SQR = 0.05f;

    /// <summary>
    /// Estados posibles del enemigo dentro de su Máquina de Estados Finitos (Patrón State).
    /// </summary>
    public enum Estado
    {
        Patrulla,
        Persecucion,
        Ataque
    }

    [Header("Estado Actual (Patrón State)")]
    [Tooltip("Estado activo del enemigo en tiempo real.")]
    public Estado estadoActual = Estado.Patrulla;

    [Header("Configuración de Patrulla")]
    [Tooltip("Radio en metros para buscar puntos aleatorios de patrullaje.")]
    public float patrolRadius = 10f;
    [Tooltip("Segundos que el enemigo se detiene al llegar a un punto de patrulla.")]
    public float waitTimeAtPoint = 2f;
    [Tooltip("Velocidad del enemigo cuando camina patrullando (ajustada a 1 m/s).")]
    public float patrolSpeed = 1f;

    [Header("Configuración de Persecución y Ataque")]
    [Tooltip("Velocidad del enemigo al perseguir el origen de un ruido o destello (ajustada a 2 m/s).")]
    public float chaseSpeed = 2f;
    [Tooltip("Distancia en metros a la que alcanza y ataca al jugador cuerpo a cuerpo.")]
    public float attackDistance = 1.6f;

    [Header("Guía Visual de Ruido (Ondas de Pasos en el Piso)")]
    [Tooltip("Activa la emisión de aros tenues en el suelo cuando camina cerca del jugador.")]
    public bool emitVisualFootsteps = true;
    [Tooltip("Intervalo en segundos entre cada onda de paso cuando camina en Patrulla.")]
    public float patrolFootstepInterval = 0.75f;
    [Tooltip("Intervalo más rápido en segundos entre cada onda cuando corre en Persecución.")]
    public float chaseFootstepInterval = 0.42f;
    [Tooltip("Distancia máxima al jugador dentro de la cual se dibujan las ondas de pasos en el piso.")]
    public float audibleStepDistance = 12f;

    // --- Variables internas ---
    private NavMeshAgent agent;
    private float waitTimer = 0f;
    private float footstepTimer = 0f;
    private Vector3 ultimaPosicionDetectada;
    private Transform playerTransform;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        BuscarJugador();
    }

    /// <summary>
    /// Localiza la referencia al Transform del jugador mediante su Tag "Player".
    /// </summary>
    void BuscarJugador()
    {
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }
    }

    /// <summary>
    /// Inicializa o reinicia el enemigo al salir del Object Pool colocándolo de forma segura sobre el NavMesh.
    /// </summary>
    public void ActivarDesdePool(Vector3 spawnPosition)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        BuscarJugador();

        // Reiniciamos posición y agente para evitar estados sucios de la vida anterior
        agent.enabled = false;
        transform.position = spawnPosition;
        gameObject.SetActive(true);
        agent.enabled = true;

        if (agent.isOnNavMesh)
        {
            agent.Warp(spawnPosition);
            estadoActual = Estado.Patrulla;
            agent.speed = patrolSpeed;
            waitTimer = 0f;
            footstepTimer = 0f;
            ElegirNuevoPuntoPatrulla();
        }
    }

    /// <summary>
    /// Ejecuta la lógica del estado actual y actualiza la guía visual de pasos en cada cuadro.
    /// </summary>
    void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (Time.timeScale <= 0f) return;

        // Verificar transición al estado de Ataque Cuerpo a Cuerpo
        if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) <= attackDistance)
        {
            EjecutarAtaque();
            return;
        }

        // Ejecutar lógica según el estado actual (Patrulla o Persecución)
        switch (estadoActual)
        {
            case Estado.Patrulla:
                ActualizarEstadoPatrulla();
                break;

            case Estado.Persecucion:
                ActualizarEstadoPersecucion();
                break;
        }

        // Emitir onda visual en el suelo si se está moviendo cerca del jugador
        ActualizarOndasDePasos();
    }

    /// <summary>
    /// Lógica del estado Patrulla: camina hacia un punto aleatorio, espera y elige otro.
    /// </summary>
    void ActualizarEstadoPatrulla()
    {
        if (!agent.pathPending && agent.remainingDistance <= PATROL_ARRIVAL_THRESHOLD)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitTimeAtPoint)
            {
                ElegirNuevoPuntoPatrulla();
                waitTimer = 0f;
            }
        }
    }

    /// <summary>
    /// Lógica del estado Persecución: corre hacia el último estímulo detectado; si llega y no está el jugador, vuelve a Patrulla.
    /// </summary>
    void ActualizarEstadoPersecucion()
    {
        if (!agent.pathPending && agent.remainingDistance <= CHASE_ARRIVAL_THRESHOLD)
        {
            estadoActual = Estado.Patrulla;
            agent.speed = patrolSpeed;
            waitTimer = 0f;
            ElegirNuevoPuntoPatrulla();
        }
    }

    /// <summary>
    /// Lógica del estado Ataque: detiene la partida y notifica la derrota al Manager.
    /// </summary>
    void EjecutarAtaque()
    {
        estadoActual = Estado.Ataque;
        if (EnemyAndAmmoPoolManager.Instance != null)
        {
            EnemyAndAmmoPoolManager.Instance.TriggerGameOver();
        }
        else
        {
            Time.timeScale = 0f;
        }
    }

    /// <summary>
    /// Genera periódicamente un aro visual en el piso desde el Object Pool cuando el enemigo camina cerca del jugador.
    /// </summary>
    void ActualizarOndasDePasos()
    {
        if (!emitVisualFootsteps || playerTransform == null || EnemyAndAmmoPoolManager.Instance == null)
            return;

        // Solo emite ondas si realmente se está desplazando y está dentro de la distancia audible
        if (agent.velocity.sqrMagnitude < MOVING_VELOCITY_THRESHOLD_SQR)
            return;

        float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        if (distToPlayer > audibleStepDistance)
            return;

        float currentInterval = (estadoActual == Estado.Persecucion) ? chaseFootstepInterval : patrolFootstepInterval;
        footstepTimer += Time.deltaTime;

        if (footstepTimer >= currentInterval)
        {
            footstepTimer = 0f;
            bool isChasing = (estadoActual == Estado.Persecucion);
            EnemyAndAmmoPoolManager.Instance.SpawnFootstepRipple(transform.position, isChasing);
        }
    }

    /// <summary>
    /// Cambia al enemigo al estado Persecución al detectar pasos, el flash de la cámara o un disparo.
    /// </summary>
    public void EscucharEstimulo(Vector3 posicionOrigen)
    {
        if (!gameObject.activeInHierarchy || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            return;

        estadoActual = Estado.Persecucion;
        agent.speed = chaseSpeed;
        ultimaPosicionDetectada = posicionOrigen;
        agent.SetDestination(ultimaPosicionDetectada);
    }

    /// <summary>
    /// Sobrecarga con tilde para mantener compatibilidad con llamadas anteriores.
    /// </summary>
    public void EscucharEstímulo(Vector3 posicionOrigen)
    {
        EscucharEstimulo(posicionOrigen);
    }

    /// <summary>
    /// Busca un punto aleatorio caminable dentro del NavMesh y lo asigna como destino de patrulla.
    /// </summary>
    void ElegirNuevoPuntoPatrulla()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    /// <summary>
    /// Desactiva al enemigo al recibir un disparo y lo devuelve al Object Pool.
    /// </summary>
    public void RecibirDisparo()
    {
        if (!gameObject.activeInHierarchy) return;

        if (EnemyAndAmmoPoolManager.Instance != null)
        {
            EnemyAndAmmoPoolManager.Instance.ReturnEnemyToPool(this);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}