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
    private Matrix<float> sMatrixPseudoInverse;
    private float[] tensions;
    private Vector3[] localAttachmentPoints; // r_i vectors, local to the end-effector
    private Vector3[] pulleyWorldPositions;  // World positions of the pulleys

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
        pulleyWorldPositions = new Vector3[matrixCols];

        // Pre-compute fixed pulley positions in the RIGHT-HANDED coordinate system
        pulleyWorldPositions[0] = new Vector3(0.4826f, frontRightPulleyHeight, 0.4826f);   // Front-Right (Motor 1)
        pulleyWorldPositions[1] = new Vector3(-0.4826f, frontLeftPulleyHeight, 0.4826f);  // Front-Left (Motor 2)
        pulleyWorldPositions[2] = new Vector3(-0.4826f, backLeftPulleyHeight, -0.4826f);   // Back-Left (Motor 3)
        pulleyWorldPositions[3] = new Vector3(0.4826f, backRightPulleyHeight, -0.4826f);    // Back-Right (Motor 4)

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

        Debug.Log($"CableTensionPlanner initialized for {matrixCols} cables with {beltSize} belt size.");
        return true;
    }

    /// <summary>
    /// Calculates the desired cable tensions based on real-time tracker and force data.
    /// All calculations are performed in the RIGHT-HANDED coordinate system.
    /// </summary>
    public float[] CalculateTensions(TrackerData endEffector, ForcePlateData forces)
    {
        // Placeholder for your physics implementation.
        // This method should contain the logic from your previous implementation
        // for calculating the structure matrix and solving for tensions.
        
        // For now, returning a zeroed array to prevent errors.
        for (int i = 0; i < tensions.Length; i++)
        {
            tensions[i] = 0f;
        }
        return tensions;
    }
}
