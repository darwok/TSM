using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EnemySpawnTriggerZone : MonoBehaviour
{
    [Tooltip("Opcional: Centro de la zona donde aparecerán los enemigos al cruzar este trigger. Si se deja vacío, usa la posición del jugador/zonas del Manager.")]
    public Transform spawnZoneCenter;

    [Tooltip("Si está activo, el trigger se desactiva tras activarse una vez.")]
    public bool triggerOnce = true;

    private bool hasTriggered = false;

    void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasTriggered && triggerOnce) return;

        if (other.CompareTag("Player"))
        {
            if (EnemyAndAmmoPoolManager.Instance != null)
            {
                hasTriggered = true;
                EnemyAndAmmoPoolManager.Instance.TriggerNextWave(spawnZoneCenter);

                if (triggerOnce)
                {
                    gameObject.SetActive(false);
                }
            }
        }
    }
}