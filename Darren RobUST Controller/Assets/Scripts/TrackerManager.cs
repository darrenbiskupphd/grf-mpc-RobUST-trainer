using UnityEngine;
using System.Threading;
using Valve.VR;

/// <summary>
/// Manages Vive trackers using direct OpenVR API with dedicated threading.
/// Provides high-frequency (90Hz) tracker data independent of Unity's framerate.
/// </summary>
public class TrackerManager : MonoBehaviour
{
    [Header("Tracker Serial Numbers")]
    [Tooltip("Serial number of the Vive tracker on the human's body (for CoM estimation).")]
    public string comTrackerSerial = "LHR-12345678";

    [Tooltip("Serial number of the Vive tracker on the end-effector chest belt.")]
    public string endEffectorTrackerSerial = "LHR-87654321";

    // OpenVR tracking
    private CVRSystem vrSystem;
    private uint comTrackerIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private uint endEffectorTrackerIndex = OpenVR.k_unTrackedDeviceIndexInvalid;

    // Threading
    private Thread trackingThread;
    private volatile bool isTracking = false;
    private readonly object dataLock = new object();

    // Current tracker data - thread-safe access
    private TrackerData comTrackerData;
    private TrackerData endEffectorTrackerData;

    /// <summary>
    /// Initializes OpenVR and starts the tracking thread.
    /// Called by RobotController in the correct dependency order.
    /// </summary>
    /// <returns>True if initialization succeeded, false otherwise</returns>
    public bool Initialize()
    {
        // Initialize OpenVR
        var eError = EVRInitError.None;
        vrSystem = OpenVR.Init(ref eError, EVRApplicationType.VRApplication_Background);
        
        if (eError != EVRInitError.None)
        {
            Debug.LogError($"Failed to initialize OpenVR: {eError}", this);
            return false;
        }

        // Find tracker device indices by serial number
        if (!FindTrackerIndices())
        {
            Debug.LogError("Failed to find required tracker devices.", this);
            OpenVR.Shutdown();
            return false;
        }

        // Initialize data structures
        comTrackerData = new TrackerData(Vector3.zero, Quaternion.identity);
        endEffectorTrackerData = new TrackerData(Vector3.zero, Quaternion.identity);

        // Start tracking thread
        isTracking = true;
        trackingThread = new Thread(TrackingLoop) { IsBackground = true };
        trackingThread.Start();

        Debug.Log($"TrackerManager initialized with CoM tracker {comTrackerIndex} and EndEffector tracker {endEffectorTrackerIndex}");
        return true;
    }

    /// <summary>
    /// Finds the device indices for our specific tracker serial numbers.
    /// </summary>
    private bool FindTrackerIndices()
    {
        bool foundCom = false, foundEndEffector = false;

        for (uint deviceId = 0; deviceId < OpenVR.k_unMaxTrackedDeviceCount; deviceId++)
        {
            if (vrSystem.GetTrackedDeviceClass(deviceId) == ETrackedDeviceClass.GenericTracker)
            {
                var serialNumber = GetDeviceSerialNumber(deviceId);
                
                if (serialNumber == comTrackerSerial)
                {
                    comTrackerIndex = deviceId;
                    foundCom = true;
                }
                else if (serialNumber == endEffectorTrackerSerial)
                {
                    endEffectorTrackerIndex = deviceId;
                    foundEndEffector = true;
                }
            }
        }

        return foundCom && foundEndEffector;
    }

    /// <summary>
    /// Gets the serial number of a tracked device.
    /// </summary>
    private string GetDeviceSerialNumber(uint deviceId)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var capacity = vrSystem.GetStringTrackedDeviceProperty(deviceId, ETrackedDeviceProperty.Prop_SerialNumber_String, null, 0, ref error);
        
        if (capacity > 1)
        {
            var result = new System.Text.StringBuilder((int)capacity);
            vrSystem.GetStringTrackedDeviceProperty(deviceId, ETrackedDeviceProperty.Prop_SerialNumber_String, result, capacity, ref error);
            return result.ToString();
        }
        
        return "";
    }

    /// <summary>
    /// Background thread that continuously polls tracker data at 90Hz.
    /// </summary>
    private void TrackingLoop()
    {
        long targetIntervalTicks = (long)Math.Round((double)System.Diagnostics.Stopwatch.Frequency / 90.0); // 90Hz intervals
        long nextTargetTime = System.Diagnostics.Stopwatch.GetTimestamp() + targetIntervalTicks;
        
        var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        while (isTracking)
        {
            try
            {
                // Get latest poses from OpenVR
                vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, poses, (uint)poses.Length);

                // Extract our specific tracker data
                var newComData = ExtractTrackerData(poses[comTrackerIndex]);
                var newEndEffectorData = ExtractTrackerData(poses[endEffectorTrackerIndex]);

                // Thread-safe update
                lock (dataLock)
                {
                    comTrackerData = newComData;
                    endEffectorTrackerData = newEndEffectorData;
                }

                // Precise timing: wait until next target time (same pattern as TCP communicator)
                long timeUntilNext = nextTargetTime - System.Diagnostics.Stopwatch.GetTimestamp();
                double sleepMs = (double)timeUntilNext * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                
                if (sleepMs > 0.1)
                {
                    System.Threading.SpinWait.SpinUntil(() => System.Diagnostics.Stopwatch.GetTimestamp() >= nextTargetTime);
                }

                // Advance to next target time
                nextTargetTime += targetIntervalTicks;
                
                // Drift compensation
                long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
                if (nextTargetTime <= currentTime)
                {
                    nextTargetTime = currentTime + targetIntervalTicks;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Tracking error: {e.Message}");
                // Continue tracking even if there's an error
            }
        }
    }

    /// <summary>
    /// Converts OpenVR pose data to Unity TrackerData.
    /// </summary>
    private TrackerData ExtractTrackerData(TrackedDevicePose_t pose)
    {
        if (!pose.bPoseIsValid)
        {
            return new TrackerData(Vector3.zero, Quaternion.identity);
        }

        // Convert OpenVR matrix to Unity position/rotation
        var matrix = pose.mDeviceToAbsoluteTracking;
        
        // Extract position (convert from OpenVR coordinates to Unity)
        Vector3 position = new Vector3(matrix.m[0, 3], matrix.m[1, 3], -matrix.m[2, 3]);
        
        // Extract rotation (convert from OpenVR coordinates to Unity)
        Quaternion rotation = new Quaternion(
            matrix.m[0, 1],
            matrix.m[1, 1], 
            -matrix.m[2, 1],
            matrix.m[3, 1]
        );

        return new TrackerData(position, rotation);
    }

    /// <summary>
    /// Retrieves the current data for the CoM tracker (zero-check runtime).
    /// </summary>
    public TrackerData GetCoMTrackerData()
    {
        lock (dataLock)
        {
            return comTrackerData;
        }
    }

    /// <summary>
    /// Retrieves the current data for the end-effector tracker (zero-check runtime).
    /// </summary>
    public TrackerData GetEndEffectorTrackerData()
    {
        lock (dataLock)
        {
            return endEffectorTrackerData;
        }
    }

    /// <summary>
    /// Stops tracking and cleans up OpenVR resources.
    /// </summary>
    public void Shutdown()
    {
        if (!isTracking) return;
        
        isTracking = false;
        trackingThread?.Join(500);
        OpenVR.Shutdown();
        
        Debug.Log("TrackerManager shutdown complete.");
    }

    void OnApplicationQuit()
    {
        Shutdown();
    }
}
