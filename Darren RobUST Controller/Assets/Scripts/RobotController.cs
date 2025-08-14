using UnityEngine;

/// <summary>
/// Main controller for the cable-driven robot.
/// This class centralizes the robot's state management, coordinating trackers,
/// force plates, physics calculations, and communication with LabVIEW.
/// </summary>
public class RobotController : MonoBehaviour
{
    [Header("System Modules")]
    [Tooltip("Manages Vive Trackers for human and end-effector positions.")]
    public TrackerManager trackerManager;

    [Tooltip("Manages force plate data from the Vicon system.")]
    public ForcePlateManager forcePlateManager;

    [Tooltip("Handles TCP communication with the LabVIEW server.")]
    public LabviewTcpCommunicator tcpCommunicator;

    [Tooltip("Performs physics calculations for cable tension planning.")]
    public CableTensionPlanner tensionPlanner;

    [Header("Control Settings")]
    [Tooltip("Flag to enable or disable sending data to LabVIEW.")]
    public bool isLabviewControlEnabled = true;

    [Header("Tracker Visualization")]
    [Tooltip("Visual representation of the CoM tracker.")]
    public Transform comTrackerVisual;

    [Tooltip("Visual representation of the end-effector tracker.")]
    public Transform endEffectorVisual;

    // Transformation matrices calculated at startup to align Vive tracker data
    // with the Vicon coordinate system.
    private Matrix4x4 comViveToVicon;
    private Matrix4x4 endEffectorViveToVicon;

    private void Start()
    {
        if (!ValidateModules())
        {
            enabled = false; // Disable this script if modules are missing
            return;
        }

        // Initialize modules that don't depend on calibration data first
        if (!trackerManager.Initialize())
        {
            Debug.LogError("Failed to initialize TrackerManager.", this);
            enabled = false;
            return;
        }
        if (!tensionPlanner.Initialize())
        {
            Debug.LogError("Failed to initialize CableTensionPlanner.", this);
            enabled = false;
            return;
        }

        // Initialize remaining modules
        if (!forcePlateManager.Initialize())
        {
            Debug.LogError("Failed to initialize ForcePlateManager.", this);
            enabled = false;
            return;
        }
        if (!tcpCommunicator.Initialize(tensionPlanner.motorNumbers))
        {
            Debug.LogError("Failed to initialize LabviewTcpCommunicator.", this);
            enabled = false;
            return;
        }

        Debug.Log("All robot modules initialized successfully.");

        // This method will poll a static frame from Vicon and compute transformations to vicon origin. 
        // subsequent robot physics calculations are done in vicon origin. This is for easier frame pulley position calculation and
        // force plate cop localization.
        CalibrateTrackers();

        // Start the TCP connection if enabled
        if (isLabviewControlEnabled)
        {
            tcpCommunicator.ConnectToServer();
        }
    }

    private void Update()
    {
        // 1. Get the latest raw tracker data (in the RIGHT-HANDED OpenVR/Vive coordinate system).
        TrackerData comTrackerData = trackerManager.GetCoMTrackerData();
        TrackerData endEffectorTrackerData = trackerManager.GetEndEffectorTrackerData();
        
        // 2. Transform tracker data from Vive space to the Vicon coordinate space.
        Matrix4x4 comPoseVicon = comViveToVicon * comTrackerData.PoseMatrix;
        Matrix4x4 endEffectorPoseVicon = endEffectorViveToVicon * endEffectorTrackerData.PoseMatrix;

        // 3. Update the visual representation in Unity's left-handed system.
        Vector3 comPosition = comPoseVicon.GetColumn(3);
        Quaternion comRotation = comPoseVicon.rotation;
        comTrackerVisual.SetPositionAndRotation(
            new Vector3(comPosition.x, comPosition.y, -comPosition.z),
            new Quaternion(-comRotation.x, -comRotation.y, comRotation.z, comRotation.w)
        );

        Vector3 endEffectorPosition = endEffectorPoseVicon.GetColumn(3);
        Quaternion endEffectorRotation = endEffectorPoseVicon.rotation;
        endEffectorVisual.SetPositionAndRotation(
            new Vector3(endEffectorPosition.x, endEffectorPosition.y, -endEffectorPosition.z),
            new Quaternion(-endEffectorRotation.x, -endEffectorRotation.y, endEffectorRotation.z, endEffectorRotation.w)
        );

        // 4. Determine the desired force and torque to be applied by the robot.
        (Vector3 desiredForce, Vector3 desiredTorque) = CalculateDesiredWrench(comPoseVicon, endEffectorPoseVicon);

        // 5. Calculate desired cable tensions to achieve the wrench.
        float[] desiredTensions = tensionPlanner.CalculateTensions(endEffectorPoseVicon, desiredForce, desiredTorque);

        // 6. Send the calculated tensions to LabVIEW.
        if (isLabviewControlEnabled && tcpCommunicator.IsConnected)
        {
            tcpCommunicator.UpdateTensionSetpoint(desiredTensions);
        }
    }

    /// <summary>
    /// This is the placeholder for your high-level control logic.
    /// It determines the force and torque to be applied by the cables.
    /// </summary>
    private (Vector3 force, Vector3 torque) CalculateDesiredWrench(Matrix4x4 comPose, Matrix4x4 endEffectorPose)
    {
        // --- FUTURE IMPLEMENTATION ---
        // This is where you would implement logic like force fields, perturbations, etc.

        // For now, return a zero wrench (the robot will do nothing).
        return (Vector3.zero, Vector3.zero);
    }

    /// <summary>
    /// Runs the entire calibration sequence at startup.
    /// </summary>
    private void CalibrateTrackers()
    {
        Debug.Log("Starting tracker calibration...");

        // --- In a real implementation, you would wait for a static capture here ---

        // 1. Get the poses of the trackers from the Vicon system.
        // --- FUTURE IMPLEMENTATION ---
        // a. Call Vicon SDK to get the 3 marker positions for each tracker.
        // b. Construct a 4x4 pose matrix from these 3 points.
        Debug.Log("Getting Vicon poses for trackers (using identity placeholders).");
        Matrix4x4 comPoseInVicon = Matrix4x4.identity;
        Matrix4x4 endEffectorPoseInVicon = Matrix4x4.identity;

        // 2. Get the poses of the trackers from the Vive system.
        Matrix4x4 comPoseInVive = trackerManager.GetCoMTrackerData().PoseMatrix;
        Matrix4x4 endEffectorPoseInVive = trackerManager.GetEndEffectorTrackerData().PoseMatrix;

        // 3. Calculate and store the transformation matrices.
        comViveToVicon = comPoseInVicon * comPoseInVive.inverse;
        endEffectorViveToVicon = endEffectorPoseInVicon * endEffectorPoseInVive.inverse;

        Debug.Log("Vive-to-Vicon transforms calculated.");

        // 4. Get the pulley positions from the Vicon system (placeholder).
        Vector3[] pulleyPositions = GetViconPulleyPositions();

        // 5. Configure the CableTensionPlanner with the calibrated pulley positions.
        tensionPlanner.SetPulleyPositions(pulleyPositions);

        Debug.Log("Tracker calibration complete.");
    }

    /// <summary>
    /// Placeholder for getting the pulley positions from the Vicon system.
    /// </summary>
    private Vector3[] GetViconPulleyPositions()
    {
        // --- FUTURE IMPLEMENTATION ---
        // 1. Call Vicon SDK to get the world positions of the 4 pulley markers.
        // 2. Return them in the correct order.

        Debug.Log("Getting Vicon pulley positions (using hardcoded placeholders).");
        var positions = new Vector3[4];
        // These hardcoded values are now only for placeholder purposes.
        // The real values will come from your Vicon system.
        positions[0] = new Vector3(0.4826f, 0, 0.4826f);
        positions[1] = new Vector3(-0.4826f, 0, 0.4826f);
        positions[2] = new Vector3(-0.4826f, 0, -0.4826f);
        positions[3] = new Vector3(0.4826f, 0, -0.4826f);
        return positions;
    }

    // Validates that all required module references are assigned in the Inspector.
    // returns True if all modules are assigned, false otherwise.
    private bool ValidateModules()
    {
        if (trackerManager == null)
        {
            Debug.LogError("TrackerManager is not assigned in the RobotController inspector.", this);
            return false;
        }
        if (forcePlateManager == null)
        {
            Debug.LogError("ForcePlateManager is not assigned in the RobotController inspector.", this);
            return false;
        }
        if (tcpCommunicator == null)
        {
            Debug.LogError("LabviewTcpCommunicator is not assigned in the RobotController inspector.", this);
            return false;
        }
        if (tensionPlanner == null)
        {
            Debug.LogError("CableTensionPlanner is not assigned in the RobotController inspector.", this);
            return false;
        }
        return true;
    }

    void OnApplicationQuit()
    {
        // Clean shutdown of threaded components.
        // TrackerManager handles its own shutdown via its OnDestroy method.
        tcpCommunicator?.Disconnect();
    }
}
