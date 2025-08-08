using UnityEngine;

/// <summary>
/// Manages data from the Vicon force plates.
/// In a real implementation, this class would interface with the Vicon SDK
/// or another data streaming solution to get live data.
/// </summary>
public class ForcePlateManager : MonoBehaviour
{
    [Header("Live Force Plate Data")]
    [Tooltip("Current force data from the plate. This is for visualization and debugging.")]
    [SerializeField] private Vector3 currentForce;

    [Tooltip("Current moment data from the plate. This is for visualization and debugging.")]
    [SerializeField] private Vector3 currentMoment;

    // In a real application, you would have a mechanism to update these values
    // from the Vicon data stream (e.g., via a network listener or SDK callback).

    /// <summary>
    /// Initializes the force plate manager.
    /// Called by RobotController in the correct dependency order.
    /// </summary>
    /// <returns>True if initialization succeeded, false otherwise</returns>
    public bool Initialize()
    {
        // For now, just initialize to zero. In a real implementation,
        // you would set up the Vicon connection here.
        currentForce = Vector3.zero;
        currentMoment = Vector3.zero;
        
        Debug.Log("ForcePlateManager initialized successfully.");
        return true;
    }

    private void Update()
    {
        // --- SIMULATION PLACEHOLDER ---
        // Replace this with your actual Vicon data integration logic.
        // For example, you might call a function here that polls the Vicon system.
        currentForce = Vector3.zero;
        currentMoment = Vector3.zero;
    }

    /// <summary>
    /// Retrieves the latest force plate data (zero-allocation runtime).
    /// </summary>
    /// <returns>A ForcePlateData object with the current force and moment.</returns>
    public ForcePlateData GetForcePlateData()
    {
        return new ForcePlateData(currentForce, currentMoment);
    }

    /// <summary>
    /// Public method to allow external scripts (like a Vicon data receiver)
    /// to update the force plate data.
    /// </summary>
    /// <param name="force">The new force vector.</param>
    /// <param name="moment">The new moment vector.</param>
    public void UpdateData(Vector3 force, Vector3 moment)
    {
        currentForce = force;
        currentMoment = moment;
    }
}
