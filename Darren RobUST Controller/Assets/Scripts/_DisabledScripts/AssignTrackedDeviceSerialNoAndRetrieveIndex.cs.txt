using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;


public class AssignTrackedDeviceSerialNoAndRetrieveIndex : MonoBehaviour
{
    public string desiredDeviceSerialNumber;
    public GameObject viveTrackedObjectsManager;
    private ManageViveTrackedObjects viveTrackedObjectsManagerScript; // the script that can be used to get device information and 
                                                                     // device index by serial number

    // The tracked object script (a Steam VR built-in) that we must pass the device index to once it is found.
    private SteamVR_TrackedObject trackedObjectScript; 

    // The device index we desire
    private int deviceIndexCorrespondingToSerialNumber = -1;

    // A flag to indicate if the Vive tracked object manager is ready to serve data
    private bool viveTrackerManagerReady = false;

    // A flag to indicate if we have found the device index by serial number already
    private bool haveFoundDeviceIndex = false;

    // Start is called before the first frame update
    void Start()
    {

        // Get a reference to the Vive Tracked Objects Manager script 
        viveTrackedObjectsManagerScript = viveTrackedObjectsManager.GetComponent<ManageViveTrackedObjects>();

        // Get a reference to the tracked object script that is 
        // attached to the SAME GameObject as this script.
        trackedObjectScript = transform.gameObject.GetComponent<SteamVR_TrackedObject>(); // we use transform.gameObject to get the parent GameObject
    }

    // Update is called once per frame
    void Update()
    {
        // if the vive tracker manager is not ready to serve data yet
        if (!viveTrackerManagerReady)
        {
            // See if it's read now
            viveTrackerManagerReady = viveTrackedObjectsManagerScript.IsReadyToServeData();

            if (viveTrackerManagerReady)
            {
                Debug.Log("The Vive Tracked Objects Manager is ready to serve data. Begin looking for serial number.");
            }
        }
        else // if the Vive Tracker Manager is ready to serve device data
        {
            // if we're still looking for the device index
            if (!haveFoundDeviceIndex)
            {
                // Try to retrieve the device index with the serial number
                (haveFoundDeviceIndex, deviceIndexCorrespondingToSerialNumber) = 
                    viveTrackedObjectsManagerScript.GetDeviceIndexBySerialNumber(desiredDeviceSerialNumber);

                // If we were successful and retrieved the device index
                if (haveFoundDeviceIndex)
                {

                    // Print that we succeeded
                    Debug.Log("Success! Device with serial number " + desiredDeviceSerialNumber + " has device index: " + deviceIndexCorrespondingToSerialNumber);
                    // Set the device index in the Steam VR_Tracked Object script attached to the parent GameObject
                    // of this script
                    trackedObjectScript.SetDeviceIndex(deviceIndexCorrespondingToSerialNumber);
                }
            }
        }
    }

    // If the device ID has been assigned, then the device position should be valid. 
    // If not, we should either wait or this device is not being used and should be ignored.
    public bool GetDeviceIdAssignedFlag()
    {
        return haveFoundDeviceIndex;
    }


}
