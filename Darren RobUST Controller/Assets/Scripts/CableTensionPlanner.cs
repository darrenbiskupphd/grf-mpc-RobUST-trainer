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

        // Pre-compute fixed pulley positions
        pulleyWorldPositions[0] = new Vector3(0.4826f, frontRightPulleyHeight, 0.4826f);   // Front-Right (Motor 1)
        pulleyWorldPositions[1] = new Vector3(-0.4826f, frontLeftPulleyHeight, 0.4826f);  // Front-Left (Motor 2)
        pulleyWorldPositions[2] = new Vector3(-0.4826f, backLeftPulleyHeight, -0.4826f);   // Back-Left (Motor 3)
        pulleyWorldPositions[3] = new Vector3(0.4826f, backRightPulleyHeight, -0.4826f);    // Back-Right (Motor 4)

        // Pre-compute local attachment points based on belt geometry
        float halfAP = chest_AP_distance / 2.0f;
        float halfML = chest_ML_distance / 2.0f;
        float ap_factor = (beltSize == BeltSize.Small) ? 0.7f : 0.85f;
        float ml_factor = (beltSize == BeltSize.Small) ? 0.8f : 0.95f;

        localAttachmentPoints[0] = new Vector3(halfML * ml_factor, 0, halfAP * ap_factor);  // Front-Right
        localAttachmentPoints[1] = new Vector3(-halfML * ml_factor, 0, halfAP * ap_factor); // Front-Left
        localAttachmentPoints[2] = new Vector3(-halfML * ml_factor, 0, -halfAP * ap_factor);// Back-Left
        localAttachmentPoints[3] = new Vector3(halfML * ml_factor, 0, -halfAP * ap_factor); // Back-Right

        Debug.Log($"CableTensionPlanner initialized for {matrixCols} cables with {beltSize} belt size.");
        return true;
    }

    /// <summary>
    /// Calculates the desired cable tensions based on real-time tracker and force data.
    /// </summary>
    public float[] CalculateTensions(TrackerData endEffector, ForcePlateData forces)
    {
        // --- ON-THE-FLY CALCULATION (every frame) ---

        // 1. Calculate current cable direction vectors (u_i)
        for (int i = 0; i < matrixCols; i++)
        {
            // Transform the local attachment point to its current world position
            Vector3 worldAttachmentPoint = endEffector.Position + (endEffector.Rotation * localAttachmentPoints[i]);

            // Find the vector from the attachment point to the pulley
            Vector3 cableVector = pulleyWorldPositions[i] - worldAttachmentPoint;

            // The direction vector u_i is the normalized cable vector
            Vector3 u_i = cableVector.normalized;

            // 2. Construct the structure matrix column for this cable
            // The local attachment vector r_i was pre-computed in Initialize()
            Vector3 r_i = localAttachmentPoints[i];
            Vector3 torque = Vector3.Cross(r_i, u_i);

            sMatrix[0, i] = u_i.x;
            sMatrix[1, i] = u_i.y;
            sMatrix[2, i] = u_i.z;
            sMatrix[3, i] = torque.x;
            sMatrix[4, i] = torque.y;
            sMatrix[5, i] = torque.z;
        }

        // 3. Calculate the pseudo-inverse of the now-complete S-Matrix
        sMatrixPseudoInverse = sMatrix.PseudoInverse();

        // 4. Construct the desired Wrench vector (W) from real-time force plate data.
        // This is where your control law is implemented. For now, we just counteract external forces.
        var wrenchVector = Vector<float>.Build.Dense(matrixRows);
        wrenchVector[0] = -forces.Force.x;
        wrenchVector[1] = -forces.Force.y;
        wrenchVector[2] = -forces.Force.z;
        wrenchVector[3] = -forces.Moment.x;
        wrenchVector[4] = -forces.Moment.y;
        wrenchVector[5] = -forces.Moment.z;

        // 5. Calculate tensions: T = S+ * W
        Vector<float> tensionsVector = sMatrixPseudoInverse * wrenchVector;

        // 6. Apply constraints (cables can only pull, not push)
        for (int i = 0; i < tensionsVector.Count; i++)
        {
            tensions[i] = Mathf.Max(0, tensionsVector[i]);
        }

        return tensions;
    }
}
