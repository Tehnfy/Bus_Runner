using UnityEngine;

public class ConstantRotation : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Set the speed for the X, Y, and Z axes.")]
    [SerializeField] private Vector3 rotationSpeed = new Vector3(0f, 15f, 0f);

    private void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}