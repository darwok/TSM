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
    public float patrolSpeed = 2f;

    [Header("Configuración de Persecución y Ataque")]
    public float chaseSpeed = 4.5f;
    public float attackDistance = 1.6f;

    private NavMeshAgent agent;
    private float waitTimer;
    private Vector3 ultimaPosicionDetectada;
    private Transform playerTransform;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = patrolSpeed;
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;
        ElegirNuevoPuntoPatrulla();
    }

    void Update()
    {
        // Revisar si alcanzó al jugador cuerpo a cuerpo
        if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) <= attackDistance)
        {
            Debug.Log("¡GAME OVER! El enemigo te alcanzó en la oscuridad.");
            Time.timeScale = 0f; // Pausa el prototipo al perder
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
            // Si llega al último lugar donde escuchó ruido o vio el flash y no encuentra al jugador, vuelve a patrullar
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
        estadoActual = Estado.Persecucion;
        agent.speed = chaseSpeed;
        ultimaPosicionDetectada = posicionOrigen;
        agent.SetDestination(ultimaPosicionDetectada);
    }

    void ElegirNuevoPuntoPatrulla()
    {
        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, patrolRadius, 1))
        {
            agent.SetDestination(hit.position);
        }
    }

    public void RecibirDisparo()
    {
        Debug.Log("¡Enemigo eliminado!");
        Destroy(gameObject);
    }
}