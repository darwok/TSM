using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyBlindAI : MonoBehaviour
{
    public enum Estado { Patrulla, Persecucion }
    public Estado estadoActual = Estado.Patrulla;

    [Header("Configuración de Patrulla")]
    public float patrolRadius = 10f;
    public float waitTimeAtPoint = 2f;
    public float patrolSpeed = 1f;

    [Header("Configuración de Persecución y Ataque")]
    public float chaseSpeed = 2f;
    public float attackDistance = 1.6f;

    private NavMeshAgent agent;
    private float waitTimer;
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

    void BuscarJugador()
    {
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }
    }

    // Inicializa o reinicia el enemigo desde el Pool colocándolo de forma segura sobre el NavMesh
    public void ActivarDesdePool(Vector3 spawnPosition)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        BuscarJugador();

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
            ElegirNuevoPuntoPatrulla();
        }
    }

    void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) <= attackDistance)
        {
            Debug.Log("¡GAME OVER! El enemigo te alcanzó en la oscuridad.");
            Time.timeScale = 0f;
            return;
        }

        if (estadoActual == Estado.Patrulla)
        {
            if (!agent.pathPending && agent.remainingDistance <= 0.5f)
            {
                waitTimer += Time.deltaTime;
                if (waitTimer >= waitTimeAtPoint)
                {
                    ElegirNuevoPuntoPatrulla();
                    waitTimer = 0f;
                }
            }
        }
        else if (estadoActual == Estado.Persecucion)
        {
            if (!agent.pathPending && agent.remainingDistance <= 0.6f)
            {
                estadoActual = Estado.Patrulla;
                agent.speed = patrolSpeed;
                ElegirNuevoPuntoPatrulla();
            }
        }
    }

    public void EscucharEstímulo(Vector3 posicionOrigen)
    {
        if (!gameObject.activeInHierarchy || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            return;

        estadoActual = Estado.Persecucion;
        agent.speed = chaseSpeed;
        ultimaPosicionDetectada = posicionOrigen;
        agent.SetDestination(ultimaPosicionDetectada);
    }

    void ElegirNuevoPuntoPatrulla()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    public void RecibirDisparo()
    {
        if (!gameObject.activeInHierarchy) return;

        Debug.Log("¡Enemigo eliminado y devuelto al Pool!");

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