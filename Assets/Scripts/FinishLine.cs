using UnityEngine;

/// <summary>
/// Trigger volume at the end of the lane. Crossing it ends the run and starts
/// the outro. Deliberately tall so it still catches a player who arrives along
/// a rooftop rather than the road.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FinishLine : MonoBehaviour
{
    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (RunManager.Instance != null) RunManager.Instance.Finish();
    }
}
