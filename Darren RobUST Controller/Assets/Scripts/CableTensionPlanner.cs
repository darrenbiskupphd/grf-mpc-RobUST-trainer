using UnityEngine;
using MathNet.Numerics.LinearAlgebra;

/// <summary>
/// Performs the physics calculations to determine the required cable tensions
/// based on the robot's state and external forces.
/// </summary>
public class CableTensionPlanner : MonoBehaviour
{
    [Tooltip("Number of rows in the structure matrix.")]
    public int matrixRows = 6;

    [Tooltip("Number of columns (cables) in the structure matrix.")]
    public int matrixCols = 4;

    [Header("Motor Mapping")]
    [Tooltip("The motor number corresponding to each column of the structure matrix. Size must match 'matrixCols'.")]
    public int[] motorNumbers;

    [Header("Chest Anterior-Posterior Distance")]
    [Tooltip("Measured Thickness of the chest in the anterior-posterior direction.")]
    public float chest_AP_distance = 0;

    [Header("Chest Medial-Lateral Distance")]
    [Tooltip("Measured width of the chest in the medial-lateral direction.")]
    public float chest_ML_distance = 0;

    public enum BeltSize { Small, Large }
    [Header("Belt Size")]
    [Tooltip("Select the belt size for the user.")]
    public BeltSize beltSize = BeltSize.Small;

    [Header("Pulley Heights")]
    [Tooltip("Height of the front left pulley (in meters).")]
    public float frontLeftPulleyHeight = 0f;

    [Tooltip("Height of the front right pulley (in meters).")]
    public float frontRightPulleyHeight = 0f;

    [Tooltip("Height of the back left pulley (in meters).")]
    public float backLeftPulleyHeight = 0f;

    [Tooltip("Height of the back right pulley (in meters).")]
    public float backRightPulleyHeight = 0f;

    // Pre-computed constants
    private Matrix<float> sMatrix;
    private float[] tensions;
    private Vector3[] localAttachmentPoints; // r_i vectors, local to the end-effector
    private Vector3[] pulleyWorldPositions;  // World positions of the pulleys, SET BY ROBOTCONTROLLER

    /// <summary>
    /// Initializes the tension planner by pre-calculating constant geometric properties.
    /// Called by RobotController in the correct dependency order.
    /// </summary>
    /// <returns>True if initialization succeeded, false otherwise</returns>
    public bool Initialize()
    {
        // Validate configuration
        if (motorNumbers == null || motorNumbers.Length != matrixCols)
        {
            Debug.LogError($"Motor numbers array must have {matrixCols} elements to match matrix columns.", this);
            return false;
        }

        if (chest_AP_distance <= 0 || chest_ML_distance <= 0)
        {
            Debug.LogError("Chest dimensions must be positive values.", this);
            return false;
        }

        // Pre-allocate all data structures
        tensions = new float[matrixCols];
        sMatrix = Matrix<float>.Build.Dense(matrixRows, matrixCols);
        localAttachmentPoints = new Vector3[matrixCols];
        pulleyWorldPositions = new Vector3[matrixCols]; // Array is created, but will be populated by SetPulleyPositions

        // Pre-compute local attachment points based on belt geometry in the RIGHT-HANDED coordinate system
        float halfAP = chest_AP_distance / 2.0f;
        float halfML = chest_ML_distance / 2.0f;
        float ap_factor = (beltSize == BeltSize.Small) ? 0.7f : 0.85f;
        float ml_factor = (beltSize == BeltSize.Small) ? 0.8f : 0.95f;

        // Note: Assuming Z is forward, X is right, Y is up (standard right-handed system)
        localAttachmentPoints[0] = new Vector3(halfML * ml_factor, 0, halfAP * ap_factor);  // Front-Right
        localAttachmentPoints[1] = new Vector3(-halfML * ml_factor, 0, halfAP * ap_factor); // Front-Left
        localAttachmentPoints[2] = new Vector3(-halfML * ml_factor, 0, -halfAP * ap_factor);// Back-Left
        localAttachmentPoints[3] = new Vector3(halfML * ml_factor, 0, -halfAP * ap_factor); // Back-Right

        Debug.Log($"CableTensionPlanner initialized for {matrixCols} cables with {beltSize} belt size. Waiting for pulley positions from RobotController.");
        return true;
    }

    /// <summary>
    /// Sets the world positions of the cable pulleys, as determined by the calibration routine.
    /// </summary>
    /// <param name="worldPositions">An array of Vector3 points representing the pulley locations in the Vicon coordinate system.</param>
    public void SetPulleyPositions(Vector3[] worldPositions)
    {
        if (worldPositions.Length != matrixCols)
        {
            Debug.LogError($"Cannot set pulley positions. Expected {matrixCols} positions, but received {worldPositions.Length}.", this);
            return;
        }
        
        pulleyWorldPositions = worldPositions;
        Debug.Log("Pulley positions have been set by RobotController.");
    }

    /// <summary>
    /// Calculates the desired cable tensions based on real-time tracker and force data.
    /// All calculations are performed in the RIGHT-HANDED coordinate system.
    /// </summary>
    /// <param name="endEffectorPose">The 4x4 pose matrix of the end-effector (assumed to be in the Vicon coordinate system).</param>
    /// <param name="desiredForce">The desired force to be applied by the cables.</param>
    /// <param name="desiredTorque">The desired torque to be applied by the cables.</param>
    public float[] CalculateTensions(Matrix4x4 endEffectorViconPose, Vector3 desiredForce, Vector3 desiredTorque)
    {
        // Placeholder for your physics implementation.
        // This method should contain the logic for:
        // 1. Building the structure matrix (S) based on the endEffectorViconPose.
        // 2. Setting up and solving the Quadratic Programming problem to find the
        //    tensions (t) that best satisfy S*t = [desiredForce; desiredTorque].
        
        // For now, returning a zeroed array to prevent errors.
        for (int i = 0; i < tensions.Length; i++)
        {
            tensions[i] = 0f;
        }
        return tensions;
    }
}
