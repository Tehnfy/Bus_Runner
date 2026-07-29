using UnityEngine;

/// <summary>
/// Trigger volume dressed as a bus stop. Passing it moves the respawn point here.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Optional explicit respawn spot. Falls back to this transform, lifted clear of the ground.")]
    [SerializeField] Transform spawnAnchor;

    [Tooltip("Lift applied when falling back to this transform, so the player does not spawn inside the floor.")]
    [SerializeField] float spawnLift = 0.1f;

    public Vector3 SpawnPosition =>
        spawnAnchor != null
            ? spawnAnchor.position
            : new Vector3(transform.position.x, transform.position.y + spawnLift, transform.position.z);

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (RunManager.Instance != null) RunManager.Instance.ReachCheckpoint(this);
    }
}
