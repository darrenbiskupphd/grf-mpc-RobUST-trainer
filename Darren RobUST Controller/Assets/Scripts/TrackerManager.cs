using UnityEngine;
using System.Threading;
using System;
using Valve.VR;

/// <summary>
/// Manages Vive trackers using direct OpenVR API with dedicated threading.
/// Provides high-frequency (90Hz) tracker data independent of Unity's framerate.
/// Bypasses Unity transforms for maximum performance and background thread safety.
/// </summary>
public class TrackerManager : MonoBehaviour
{
    [Header("Tracker Configuration")]
    [Tooltip("Serial number of the CoM tracker (found in SteamVR settings)")]
    public string comTrackerSerial = "LHR-FFFFFFFF"; // Replace with your tracker's serial
    
    [Tooltip("Serial number of the end-effector tracker")]
    public string endEffectorTrackerSerial = "LHR-FFFFFFFF"; // Replace with your tracker's serial

    // OpenVR system
    private CVRSystem vrSystem;
    private TrackedDevicePose_t[] trackedDevicePoses;

    // Device tracking
    private uint comTrackerDeviceId = OpenVR.k_unTrackedDeviceIndexInvalid;
    private uint endEffectorTrackerDeviceId = OpenVR.k_unTrackedDeviceIndexInvalid;

    // Threading
    private Thread trackingThread;
    private volatile bool isTracking = false;
    private readonly object dataLock = new object();

    // Latest tracker data (thread-safe)
    private readonly TrackerData comTrackerData = new TrackerData();
    private readonly TrackerData endEffectorTrackerData = new TrackerData();

    /// <summary>
    /// Initializes the tracker manager with direct OpenVR access.
    /// </summary>
    public bool Initialize()
    {
        try
        {
            // Initialize OpenVR in a way that doesn't require a headset
            EVRInitError eVRInitError = EVRInitError.None;
            vrSystem = OpenVR.Init(ref eVRInitError, EVRApplicationType.VRApplication_Background);
            
            if (eVRInitError != EVRInitError.None)
            {
                Debug.LogError($"OpenVR initialization failed: {eVRInitError}. Ensure SteamVR is running.");
                return false;
            }

            // Pre-allocate pose arrays
            trackedDevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

            // Find tracker devices by serial number
            if (!FindTrackerDevices())
            {
                Debug.LogError("Failed to find required Vive trackers. Check serial numbers and ensure trackers are on.");
                return false;
            }

            // Start high-frequency tracking thread
            isTracking = true;
            trackingThread = new Thread(TrackingLoop) { IsBackground = true };
            trackingThread.Start();

            Debug.Log($"TrackerManager initialized - Direct OpenVR tracking at 90Hz");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"TrackerManager initialization failed: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds tracker devices by serial number and assigns device IDs.
    /// </summary>
    private bool FindTrackerDevices()
    {
        var serialNumberBuffer = new System.Text.StringBuilder((int)OpenVR.k_unMaxPropertyStringSize);
        
        for (uint deviceId = 0; deviceId < OpenVR.k_unMaxTrackedDeviceCount; deviceId++)
        {
            if (vrSystem.GetTrackedDeviceClass(deviceId) != ETrackedDeviceClass.GenericTracker)
                continue;

            // Get device serial number
            var error = ETrackedPropertyError.TrackedProp_Success;
            vrSystem.GetStringTrackedDeviceProperty(deviceId, 
                ETrackedDeviceProperty.Prop_SerialNumber_String, 
                serialNumberBuffer, OpenVR.k_unMaxPropertyStringSize, ref error);

            if (error != ETrackedPropertyError.TrackedProp_Success)
                continue;

            string serialNumber = serialNumberBuffer.ToString();
            
            // Log every tracker found for easier debugging and setup
            Debug.Log($"Discovered tracker - Device: {deviceId}, Serial: {serialNumber}");

            // Match serial numbers to assign device IDs (case-insensitive)
            if (string.Equals(serialNumber, comTrackerSerial, StringComparison.OrdinalIgnoreCase))
            {
                comTrackerDeviceId = deviceId;
                Debug.Log($"--> Matched CoM tracker: {serialNumber}");
            }
            else if (string.Equals(serialNumber, endEffectorTrackerSerial, StringComparison.OrdinalIgnoreCase))
            {
                endEffectorTrackerDeviceId = deviceId;
                Debug.Log($"--> Matched End-Effector tracker: {serialNumber}");
            }
        }

        bool foundBoth = (comTrackerDeviceId != OpenVR.k_unTrackedDeviceIndexInvalid) && 
                        (endEffectorTrackerDeviceId != OpenVR.k_unTrackedDeviceIndexInvalid);
        
        if (!foundBoth)
        {
            Debug.LogError($"Missing trackers - CoM found: {comTrackerDeviceId != OpenVR.k_unTrackedDeviceIndexInvalid}, EndEffector found: {endEffectorTrackerDeviceId != OpenVR.k_unTrackedDeviceIndexInvalid}");
        }

        return foundBoth;
    }

    /// <summary>
    /// High-frequency tracking loop running in dedicated background thread.
    /// </summary>
    private void TrackingLoop()
    {
        // Use double precision for accuracy to avoid truncation errors over time
        double exactIntervalTicks = (double)System.Diagnostics.Stopwatch.Frequency / 90.0; // 90Hz
        long targetIntervalTicks = (long)Math.Round(exactIntervalTicks);
        long nextTargetTime = System.Diagnostics.Stopwatch.GetTimestamp() + targetIntervalTicks;

        while (isTracking)
        {
            // Get latest poses from OpenVR - this is thread-safe
            vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, trackedDevicePoses);

            var comPose = trackedDevicePoses[comTrackerDeviceId];
            var endEffectorPose = trackedDevicePoses[endEffectorTrackerDeviceId];

            // This check is essential to avoid errors when a tracker temporarily loses signal.
            // We only update data when both trackers are actively tracking.
            if (comPose.bPoseIsValid && endEffectorPose.bPoseIsValid)
            {
                lock (dataLock)
                {
                    UpdateTrackerDataFromPose(comTrackerData, comPose);
                    UpdateTrackerDataFromPose(endEffectorTrackerData, endEffectorPose);
                }
            }

            // Precise timing: wait until the next target time using a high-resolution wait.
            // This ensures we maintain a consistent 90Hz sampling rate.
            SpinWait.SpinUntil(() => System.Diagnostics.Stopwatch.GetTimestamp() >= nextTargetTime);

            // Advance to the next target time
            nextTargetTime += targetIntervalTicks;
            
            // Drift compensation: if we've fallen behind, reset to the current time 
            // to prevent the loop from trying to play catch-up indefinitely.
            long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
            if (nextTargetTime <= currentTime)
            {
                nextTargetTime = currentTime + targetIntervalTicks;
            }
        }
    }

    /// <summary>
    /// Updates a TrackerData object with a new OpenVR pose.
    /// </summary>
    private void UpdateTrackerDataFromPose(TrackerData trackerData, TrackedDevicePose_t pose)
    {
        var m = pose.mDeviceToAbsoluteTracking;

        // Position is directly from the matrix (right-handed)
        trackerData.Position.x = m.m3;
        trackerData.Position.y = m.m7;
        trackerData.Position.z = m.m11;

        // Rotation conversion from 3x4 matrix to Quaternion (right-handed)
        float w = Mathf.Sqrt(Mathf.Max(0, 1 + m.m0 + m.m5 + m.m10)) / 2;
        float x = Mathf.Sqrt(Mathf.Max(0, 1 + m.m0 - m.m5 - m.m10)) / 2;
        float y = Mathf.Sqrt(Mathf.Max(0, 1 - m.m0 + m.m5 - m.m10)) / 2;
        float z = Mathf.Sqrt(Mathf.Max(0, 1 - m.m0 - m.m5 + m.m10)) / 2;
        x *= Mathf.Sign(m.m9 - m.m6);
        y *= Mathf.Sign(m.m2 - m.m8);
        z *= Mathf.Sign(m.m4 - m.m1);
        
        trackerData.Rotation.x = x;
        trackerData.Rotation.y = y;
        trackerData.Rotation.z = z;
        trackerData.Rotation.w = w;
    }

    /// <summary>
    /// Gets the latest data for the CoM tracker.
    /// </summary>
    public TrackerData GetCoMTrackerData()
    {
        lock (dataLock)
        {
            return comTrackerData;
        }
    }

    /// <summary>
    /// Gets the latest data for the end-effector tracker.
    /// </summary>
    public TrackerData GetEndEffectorTrackerData()
    {
        lock (dataLock)
        {
            return endEffectorTrackerData;
        }
    }

    /// <summary>
    /// Stops the tracking thread when the application quits.
    /// </summary>
    void OnDestroy()
    {
        isTracking = false;
        if (trackingThread != null && trackingThread.IsAlive)
        {
            trackingThread.Join();
        }
        OpenVR.Shutdown();
    }
}

