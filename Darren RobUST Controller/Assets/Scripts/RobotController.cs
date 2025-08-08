using UnityEngine;
using System.Threading.Tasks;

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

    private void Start()
    {
        if (!ValidateModules())
        {
            enabled = false; // Disable this script if modules are missing
            return;
        }

        // Initialize modules in dependency order
        // 1. Initialize TrackerManager (no dependencies)
        if (!trackerManager.Initialize())
        {
            Debug.LogError("Failed to initialize TrackerManager.", this);
            enabled = false;
            return;
        }

        // 2. Initialize ForcePlateManager (no dependencies)
        if (!forcePlateManager.Initialize())
        {
            Debug.LogError("Failed to initialize ForcePlateManager.", this);
            enabled = false;
            return;
        }

        // 3. Initialize CableTensionPlanner (no dependencies, but provides motor config)
        if (!tensionPlanner.Initialize())
        {
            Debug.LogError("Failed to initialize CableTensionPlanner.", this);
            enabled = false;
            return;
        }

        // 4. Initialize TCP Communicator (depends on CableTensionPlanner for motor configuration)
        if (!tcpCommunicator.Initialize(tensionPlanner.motorNumbers))
        {
            Debug.LogError("Failed to initialize LabviewTcpCommunicator.", this);
            enabled = false;
            return;
        }

        Debug.Log("All robot modules initialized successfully.");

        // Start the TCP connection if enabled (after all modules are initialized)
        if (isLabviewControlEnabled)
        {
            tcpCommunicator.ConnectToServer();
        }
    }

    private void Update()
    {
        // In a real scenario, tracker and force plate data would be updated here.
        // For now, we assume TrackerManager and ForcePlateManager are updated by their own mechanisms
        // or a dedicated input manager script.

        // 1. Get fresh data from trackers and force plates
        TrackerData comTrackerData = trackerManager.GetCoMTrackerData();
        TrackerData endEffectorTrackerData = trackerManager.GetEndEffectorTrackerData();
        ForcePlateData forcePlateData = forcePlateManager.GetForcePlateData();

        // 2. Calculate the desired cable tensions
        float[] desiredTensions = tensionPlanner.CalculateTensions(endEffectorTrackerData, forcePlateData);

        // 3. Send data to LabVIEW
        if (isLabviewControlEnabled && tcpCommunicator.IsConnected)
        {
            // Update only the tension values (motor configuration is fixed)
            tcpCommunicator.UpdateTensionSetpoint(desiredTensions);
        }
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
        // Clean shutdown of threaded components
        trackerManager?.Shutdown();
        tcpCommunicator?.Disconnect();
    }
}
