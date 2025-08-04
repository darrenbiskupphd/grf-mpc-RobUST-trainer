using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class ManageViveTrackedObjects : MonoBehaviour
{

    // Create a custom class to store tracked device properties
    public class TrackedDeviceProperties
    {

        // Tracked parameters
        private Vector3 devicePosition;
        private Quaternion deviceOrientation;
        private Vector3 deviceVelocity;
        private Vector3 deviceAngularVelocity;

        private string deviceName;

        public TrackedDeviceProperties()
        {

        }

        public void SetDeviceName(string nameOfDevice)
        {
            deviceName = nameOfDevice;
        }

        public void SetLatestDevicePosition(Vector3 currentPosition)
        {
            devicePosition = currentPosition;
        }

        public void SetLatestDeviceOrientation(Quaternion currentOrientation)
        {
            deviceOrientation = currentOrientation;
        }

        public void SetLatestDeviceVelocity(Vector3 currentVelocity)
        {
            deviceVelocity = currentVelocity;
        }

        public void SetLatestDeviceAngularVelocity(Vector3 currentAngularVelocity)
        {
            deviceAngularVelocity = currentAngularVelocity;
        }

        public string GetDeviceName()
        {
            return deviceName;
        }

        public Vector3 GetLatestDevicePosition()
        {
            return devicePosition;
        }

        public Vector3 GetLatestDeviceVelocity()
        {
            return deviceVelocity;
        }
    }

    // Manager states
    private string currentState;
    private string setupStateString = "SETUP";
    private string readyToServeDataStateString = "READY_TO_SERVE_DATA";
    private string gameOverStateString = "GAME_OVER";

    // VR controllers and headset references
    private InputDevice leftHandController;
    private InputDevice rightHandController;
    private InputDevice headsetDevice;
    List<InputDevice> allTrackedDevices;
    private string[] allTrackedDevicesSerialNumbers; // store all device serial numbers so that we can assign devices to roles by serial number
                                                     // Note that the index in this array is the actual device index used by the Tracked_Object script!

    // VR controller and headset position, velocity, orientation names
    private string devicePositionNameString = "DevicePosition";
    private string deviceOrientationNameString = "DeviceRotation";
    private string deviceVelocityNameString = "DeviceVelocity";
    private string deviceAngularVelocityNameString = "DeviceAngularVelocity";

    // VR controller and headset - corresponding feature usages (used to actually request a value/"feature usage")
    private InputFeatureUsage devicePositionFeatureUsage;
    private InputFeatureUsage deviceOrientationFeatureUsage;
    private InputFeatureUsage deviceVelocityFeatureUsage;
    private InputFeatureUsage deviceAngularVelocityFeatureUsage;


    private TrackedDeviceProperties leftHandControllerState = new TrackedDeviceProperties();
    private TrackedDeviceProperties rightHandControllerState = new TrackedDeviceProperties();
    private TrackedDeviceProperties headsetState = new TrackedDeviceProperties();
    List<TrackedDeviceProperties> allTrackedDeviceStates;

    // Tags for the left hand and right hand 
    private string leftHandObjectTag = "LeftHand";
    private string rightHandObjectTag = "RightHand";

    // The number of tracked devices! This must be the number of base stations + number of trackers + 1
    // or else this script won't work!!!!!!!!!!!!!!!!!!!!!!!!
    private const int expectedNumberOfTrackedDevices = 13;





    // Start is called before the first frame update
    void Start()
    {
        // start in setup mode
        currentState = setupStateString;
    }

    // Update is called once per frame
    void Update()
    {
        if(currentState == setupStateString)
        {
            // Get tracked devices in a list
            InputDeviceCharacteristics trackedDevicesFilter = InputDeviceCharacteristics.TrackedDevice;

            // InputDeviceCharacteristics trackedDevicesFilter = InputDeviceCharacteristics.HeadMounted;
            List<InputDevice> trackedDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(trackedDevicesFilter, trackedDevices);
            Debug.Log("Tracked Devices:");
            foreach (var device in trackedDevices)
            {
                Debug.Log($"Device name: {device.name}, characteristics: {device.characteristics}");
            }
            
            if (trackedDevices.Count == 0)
            {
                Debug.Log("No tracked devices found.");
            }
            
            // If there are any tracked devices available
            if(trackedDevices.Count > 0)
            {
                
                // retrieve XR input devices
                RetrieveXrInputDeviceReferences();

                // Put tracked device references and their state storage objects in lists
                allTrackedDevices = new List<InputDevice>() { leftHandController, rightHandController, headsetDevice };
                allTrackedDeviceStates = new List<TrackedDeviceProperties>() { leftHandControllerState, rightHandControllerState, headsetState };

                // Update the device tracked information
                //GetCurrentDeviceStatesAllDevices();

                // transition to the ready to serve data state string
                changeActiveState(readyToServeDataStateString);


            }
        }
        else if (currentState == readyToServeDataStateString)
        {
            // Update the device tracked information
            // GetCurrentDeviceStatesAllDevices();
        }
        else
        {

        }
    }

    

    // BEGIN: public functions *********************************************************************************

    public bool IfAllViveTrackerDataIsAvailableFlag()
    {
        InputDeviceCharacteristics trackedDevicesFilter = InputDeviceCharacteristics.TrackedDevice;
        List<InputDevice> trackedDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(trackedDevicesFilter, trackedDevices);
        //Debug.Log("trackedDevicesLength" + trackedDevices.Count);
        if (trackedDevices.Count == expectedNumberOfTrackedDevices) // checks to see if each track is active or not, 7 trackers + 3 stations + 1 headset
        {
            //Debug.Log("All trackers are connected successfully");
            return true;
        }
        else
        {
            Debug.Log("One of the tracker is connected unsuccessfully");
            return false;
        }
    }
    
    // Return if this script is ready to served data yet or not
    public bool IsReadyToServeData()
    {
        if (currentState == readyToServeDataStateString)
        {
            return true; // if we're ready to serve data, return true
        }
        else
        {
            return false; // otherwise, return false
        }
    }


    public (bool, int) GetDeviceIndexBySerialNumber(string desiredSerialNumber)
    {
        bool retrievedDeviceIndexSuccessFlag = false;
        int deviceIndex = -1;
        if (currentState == readyToServeDataStateString)
        {
            for (int trackedDeviceIndex = 0; trackedDeviceIndex < allTrackedDevicesSerialNumbers.Length; trackedDeviceIndex++)
            {
                // if the serial number matches the desired serial number
                if (String.Equals(allTrackedDevicesSerialNumbers[trackedDeviceIndex],desiredSerialNumber))
                {
                    // Set the device index
                    deviceIndex = trackedDeviceIndex;

                    // Set the success flag to true
                    retrievedDeviceIndexSuccessFlag = true;

                    Debug.Log("Found device index for device with serial number: " + desiredSerialNumber);

                    // return
                    return (retrievedDeviceIndexSuccessFlag, deviceIndex);
                }
            }
        }

        Debug.Log("Failed to find device index for device with serial number: " + desiredSerialNumber);
        // This return statement is used if the search is unsuccessful
        return (retrievedDeviceIndexSuccessFlag, deviceIndex);
    }

    // End: public functions *********************************************************************************




    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if (newState != currentState)
        {
            Debug.Log("Transitioning states from " + currentState + " to " + newState);
            // call the exit function for the current state.
            // Note that we never exit the EndGame state.
            if (currentState == setupStateString)
            {
                exitWaitingForSetupState();
            }
            else if (currentState == readyToServeDataStateString)
            {
                exitReadyToServeDataStateString();
            }
            else
            {
                // invalid state

            }

            //then call the entry function for the new state
            if (newState == readyToServeDataStateString)
            {
                enterReadyToServeDataStateString();
            }
            else if (newState == gameOverStateString)
            {
                enterGameOverState();
            }
            else
            {
                Debug.Log("Punching bag level manager cannot enter a non-existent state");
            }
        }

    }


    private void exitWaitingForSetupState()
    {
        //nothing needs to happen
    }

    private void enterReadyToServeDataStateString()
    {
        currentState = readyToServeDataStateString;
    }

    private void exitReadyToServeDataStateString()
    {
        // nothing needs to happen
    }

    private void enterGameOverState()
    {
        currentState = gameOverStateString;
    }

    // END: State machine state-transitioning functions *********************************************************************************




    private void RetrieveXrInputDeviceReferences()
    {

        // Get tracked devices in a list
        InputDeviceCharacteristics trackedDevicesFilter = InputDeviceCharacteristics.TrackedDevice;
        List<InputDevice> trackedDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(trackedDevicesFilter, trackedDevices);

        // Initialize the string[] tracking device serial numbers
        allTrackedDevicesSerialNumbers = new string[trackedDevices.Count];

        // Loop through device list and assign to Headset, Right, or Left based on the 
        // presence of keywords in the name

        for (int deviceIndex = 0; deviceIndex < trackedDevices.Count; deviceIndex++)
        {
            // Just print all device names so we can see all devices
            Debug.Log("Device at trackedDevices index: " + deviceIndex + " has name: " + trackedDevices[deviceIndex].name + " and serial number: " + trackedDevices[deviceIndex].serialNumber);

            // Store the serial number for the device.
            allTrackedDevicesSerialNumbers[deviceIndex] = trackedDevices[deviceIndex].serialNumber;
            Debug.Log("At device index " + deviceIndex + "stored serial number:" + allTrackedDevicesSerialNumbers[deviceIndex]);
            // Assign roles to HMD and controllers
            if (trackedDevices[deviceIndex].name.Contains("Right") && trackedDevices[deviceIndex].name.Contains("Controller"))
            {
                rightHandController = trackedDevices[deviceIndex];
                rightHandControllerState.SetDeviceName(rightHandController.name);

                Debug.Log("Right controller has name: " + rightHandController.name);
            }
            else if (trackedDevices[deviceIndex].name.Contains("Left") && trackedDevices[deviceIndex].name.Contains("Controller"))
            {
                leftHandController = trackedDevices[deviceIndex];
                leftHandControllerState.SetDeviceName(leftHandController.name);
                Debug.Log("Left controller has name: " + leftHandController.name);
            }
            else if (trackedDevices[deviceIndex].name.Contains("Headset"))
            {
                headsetDevice = trackedDevices[deviceIndex];
                headsetState.SetDeviceName(headsetDevice.name);
                Debug.Log("Headset has name: " + headsetDevice.name);
            }
            else
            {

            }
        }

        // Get a list of feature usages for the left controller
        List<InputFeatureUsage> leftControllerFeatureUsages = new List<InputFeatureUsage>();
        bool couldQueryControllerFeatureList = leftHandController.TryGetFeatureUsages(leftControllerFeatureUsages);
        if (couldQueryControllerFeatureList)
        {
            for (int featureUsageIndex = 0; featureUsageIndex < leftControllerFeatureUsages.Count; featureUsageIndex++)
            {
                Debug.Log("Left hand controller feature available: " + leftControllerFeatureUsages[featureUsageIndex].name);
            }
        }

        // Save the required feature usages
        for (int featureUsageIndex = 0; featureUsageIndex < leftControllerFeatureUsages.Count; featureUsageIndex++)
        {
            if (leftControllerFeatureUsages[featureUsageIndex].name == devicePositionNameString)
            {
                devicePositionFeatureUsage = leftControllerFeatureUsages[featureUsageIndex];
                Debug.Log("Found the device position feature usage. Name is: " + devicePositionFeatureUsage.name);
            }
            else if (leftControllerFeatureUsages[featureUsageIndex].name == deviceOrientationNameString)
            {
                deviceOrientationFeatureUsage = leftControllerFeatureUsages[featureUsageIndex];
                Debug.Log("Found the device orientation feature usage. Name is: " + deviceOrientationFeatureUsage.name);
            }
            else if (leftControllerFeatureUsages[featureUsageIndex].name == deviceVelocityNameString)
            {
                deviceVelocityFeatureUsage = leftControllerFeatureUsages[featureUsageIndex];
                Debug.Log("Found the device velocity feature usage. Name is: " + deviceVelocityFeatureUsage.name);
            }
            else if (leftControllerFeatureUsages[featureUsageIndex].name == deviceAngularVelocityNameString)
            {
                deviceAngularVelocityFeatureUsage = leftControllerFeatureUsages[featureUsageIndex];
                Debug.Log("Found the device angular velocity feature usage. Name is: " + deviceAngularVelocityFeatureUsage.name);
            }
        }

    }



    public void GetCurrentDeviceStatesAllDevices()
    {
        for (int trackedDeviceIndex = 0; trackedDeviceIndex < allTrackedDevices.Count; trackedDeviceIndex++)
        {
            UpdateCurrentDeviceStates(allTrackedDevices[trackedDeviceIndex], allTrackedDeviceStates[trackedDeviceIndex]);
        }
    }



    private void UpdateCurrentDeviceStates(InputDevice trackedDevice, TrackedDeviceProperties trackedDeviceStateStorageObject)
    {
        // Device position
        Vector3 devicePosition = new Vector3();
        bool retrievedPos = trackedDevice.TryGetFeatureValue(devicePositionFeatureUsage.As<Vector3>(), out devicePosition);
        trackedDeviceStateStorageObject.SetLatestDevicePosition(devicePosition);

        // Device orientation
        Quaternion deviceOrientation = new Quaternion();
        bool retrievedOrientation = trackedDevice.TryGetFeatureValue(deviceOrientationFeatureUsage.As<Quaternion>(), out deviceOrientation);
        trackedDeviceStateStorageObject.SetLatestDeviceOrientation(deviceOrientation);

        // Device velocity
        Vector3 deviceVelocity = new Vector3();
        bool retrievedVel = trackedDevice.TryGetFeatureValue(deviceVelocityFeatureUsage.As<Vector3>(), out deviceVelocity);
        trackedDeviceStateStorageObject.SetLatestDeviceVelocity(deviceVelocity);

        // Device angular velocity
        Vector3 deviceAngularVelocity = new Vector3();
        bool retrievedAngularVel = trackedDevice.TryGetFeatureValue(deviceAngularVelocityFeatureUsage.As<Vector3>(), out deviceAngularVelocity);
        trackedDeviceStateStorageObject.SetLatestDeviceAngularVelocity(deviceAngularVelocity);

    }
    // private void UpdateCurrentDeviceStates(InputDevice trackedDevice, TrackedDeviceProperties trackedDeviceStateStorageObject)
    // {
    //     // Device position
    //     Vector3 devicePosition = new Vector3();
    //     bool retrievedPos = trackedDevice.TryGetFeatureValue(CommonUsages.devicePosition, out devicePosition);
    //     trackedDeviceStateStorageObject.SetLatestDevicePosition(devicePosition);
    //
    //     // Device orientation
    //     Quaternion deviceOrientation = new Quaternion();
    //     bool retrievedOrientation = trackedDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out deviceOrientation);
    //     trackedDeviceStateStorageObject.SetLatestDeviceOrientation(deviceOrientation);
    //
    //     // Device velocity
    //     Vector3 deviceVelocity = new Vector3();
    //     bool retrievedVel = trackedDevice.TryGetFeatureValue(CommonUsages.deviceVelocity, out deviceVelocity);
    //     trackedDeviceStateStorageObject.SetLatestDeviceVelocity(deviceVelocity);
    //
    //     // Device angular velocity
    //     Vector3 deviceAngularVelocity = new Vector3();
    //     bool retrievedAngularVel = trackedDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out deviceAngularVelocity);
    //     trackedDeviceStateStorageObject.SetLatestDeviceAngularVelocity(deviceAngularVelocity);
    // }






}


