using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RetrieveForcePlateDataScript : MonoBehaviour
{

    // State management
    private string currentState;
    private string setupStateString = "SETUP";
    private string activelyRetrievingDataStateString = "RETRIEVING_DATA";

    // Public game objects dragged into place by the user
    public GameObject markerDataDistributor; //the GameObject that reads marker data each frame and has getter functions to get marker data by name. Accessses data via the dataStream prefab from the Unity/Vicon plugin.


    // Vicon SDK objects
    private UnityVicon.ReadViconDataStreamScript markerDataDistributorScript; //the callable script of the
                                                                              //GameObject that makes marker data
                                                                              //available.

    // Devices currently streaming data over the Vicon data stream SDK
    private string[] devicesCurrentlyStreamingDataNames;
    private ViconDataStreamSDK.CSharp.DeviceType[] devicesCurrentlyStreamingDataTypes;
    private List<string[]> outputComponentNamesForEachDevice; // each string[] contains all the output names for the corresponding device
    private List<ViconDataStreamSDK.CSharp.Unit[]> outputComponentUnitsForEachDevice; // each list contains all the output units for the corresponding device
    private uint numberOfForcePlatesStreamingData; // the number of force plates streaming data over the data stream

    // Force plate data (sample at same frequency as Vicon, for now (get the first subsample for the frame)
    private Vector3[] forcePlatesCenterOfPressure; // Each list entry stores a Vector3 containing the X,Y,Z positions of that plate's COP
    private Vector3[] forcePlatesForcesViconGlobalCoords; // Each list entry stores a Vector3 containing the X,Y,Z force components for that plate
    private Vector3[] forcePlatesMomentsViconGlobalCoords; // Each list entry stores a Vector3 containing the X,Y,Z moment components for that plate

    // Global center of pressure
    private Vector3 globalCenterOfPressureInViconFrameMm; // the global center of pressure, computed from a weighted average of the two force plate COPs

    // Sync pin (analog input on Vicon Lock box)
    private bool viconAnalogSyncPinAvailableFlag = false;
    private string nameOfViconLockAnalogInput; // will store the name of the analog input "Device"
    private string syncPinComponentName; // Will store the name of the sync pin component of the analog input "device"
    private string substringInAnalogInputDevice = "ANALOG_SLOT"; // The ID substring in the analog input device
    private string substringInSyncPinComponentName = "SYNC"; // The ID substring in SYNC pin component name
    private string[] analogInputComponentNames;
    private float mostRecentSyncPinVoltageValue;

    // Whether or not this script can provide any data (i.e. analog inputs) if there are no devices/force plates
    private bool devicesRequired = true;

    // Start is called before the first frame update
    void Start()
    {
        // Get a reference to the script accessing the Vicon SDK data stream
        markerDataDistributorScript = markerDataDistributor.GetComponent<UnityVicon.ReadViconDataStreamScript>();

        // We initialize in the setup state
        currentState = setupStateString;
    }

    // Update is called once per frame
    void Update()
    {
        if (currentState == setupStateString) //if we're still setting up
        {
            //Debug.Log("COP retrieve script: in setup state");
            //check if the data stream server is ready yet
            bool viconDataStreamDistributorReadyStatus = markerDataDistributorScript.getReadyStatusOfViconDataStreamDistributor();
            //Debug.Log("COP retrieve script: Vicon data stream ready state = " + viconDataStreamDistributorReadyStatus);
            //if the Vicon data stream is ready to be accessed
            if (viconDataStreamDistributorReadyStatus)
            {
                // Get the number of devices streaming data, their names,
                // their output names, and the output units
                (devicesCurrentlyStreamingDataNames, devicesCurrentlyStreamingDataTypes,
                    outputComponentNamesForEachDevice, outputComponentUnitsForEachDevice) =
                    markerDataDistributorScript.GetAllDeviceNamesAndTypes();
                int numberOfDevices = devicesCurrentlyStreamingDataNames.Length;
                Debug.Log("Vicon data stream ready. Looked for connected devices (force plates) and found: " + numberOfDevices);
                if (numberOfDevices > 0 || devicesRequired == false) // Only proceed if the number of devices is greater than zero OR we do not require the force plates
                {
                    Debug.Log("Number of devices is " + devicesCurrentlyStreamingDataNames.Length);

                    //Get number of force plates specifically 
                    numberOfForcePlatesStreamingData = markerDataDistributorScript.GetForcePlateCount();
                    Debug.Log("Number of force plates is " + numberOfForcePlatesStreamingData);

                    // Initialize the force plate data vectors based on the number of force plates
                    forcePlatesCenterOfPressure = new Vector3[numberOfForcePlatesStreamingData];
                    forcePlatesForcesViconGlobalCoords = new Vector3[numberOfForcePlatesStreamingData];
                    forcePlatesMomentsViconGlobalCoords = new Vector3[numberOfForcePlatesStreamingData];

                    // Store the analog inputs device name
                    string[] analogInputComponentNames;
                    for (int deviceIndex = 0; deviceIndex < devicesCurrentlyStreamingDataNames.Length; deviceIndex++)
                    {
                        if (devicesCurrentlyStreamingDataNames[deviceIndex].Contains(substringInAnalogInputDevice))
                        {
                            nameOfViconLockAnalogInput = devicesCurrentlyStreamingDataNames[deviceIndex];
                            viconAnalogSyncPinAvailableFlag = true;
                            Debug.Log("Analog input device has name: " + nameOfViconLockAnalogInput);

                            analogInputComponentNames = outputComponentNamesForEachDevice[deviceIndex];

                            for (int componentIndex = 0; componentIndex < analogInputComponentNames.Length; componentIndex++)
                            {
                                if (analogInputComponentNames[componentIndex].Contains(substringInSyncPinComponentName))
                                {
                                    syncPinComponentName = analogInputComponentNames[componentIndex];
                                    Debug.Log("Analog input sync pin component has name: " + syncPinComponentName);
                                }
                            }
                        }
                    }

                    // print the device names, types (and optionally, component names and units)
                    for (int deviceIndex = 0; deviceIndex < devicesCurrentlyStreamingDataNames.Length; deviceIndex++)
                    {
                        Debug.Log("Device at index " + deviceIndex + " has name " + devicesCurrentlyStreamingDataNames[deviceIndex] + " and type " + devicesCurrentlyStreamingDataTypes[deviceIndex]);

                        /*                    // Get the device's output component names
                                            string[] outputComponentNames = outputComponentNamesForEachDevice[deviceIndex];
                                            ViconDataStreamSDK.CSharp.Unit[] outputComponentUnits = outputComponentUnitsForEachDevice[deviceIndex];

                                            // Print the component names and units for the current device
                                            for (uint deviceComponentOutputIndex = 0; deviceComponentOutputIndex < outputComponentNames.Length; deviceComponentOutputIndex++)
                                            {
                                                Debug.Log("Device at index " + deviceIndex + 
                                                    " has component index" + deviceComponentOutputIndex + 
                                                    " , name: " + outputComponentNames[deviceComponentOutputIndex] + 
                                                    " and output unit: " + outputComponentUnits[deviceComponentOutputIndex]);
                                            }*/
                    }

                    // Finished setup, so change the active state to the Retrieving Data state
                    changeActiveState(activelyRetrievingDataStateString);
                }
            }

        }
        else if (currentState == activelyRetrievingDataStateString) // If we're actively retrieving force plate data
        {
            // Get the force, moment, and center of pressure data for each force plate individually this frame
            // For each force plate
            for (uint forcePlateIndex = 0; forcePlateIndex < numberOfForcePlatesStreamingData; forcePlateIndex++)
            {
                // Get the center of pressure in Vicon global coords
                forcePlatesCenterOfPressure[forcePlateIndex] = markerDataDistributorScript.GetForcePlateCenterOfPressureInViconGlobalCoords(forcePlateIndex);

                // Get the force components
                forcePlatesForcesViconGlobalCoords[forcePlateIndex] = markerDataDistributorScript.GetForcePlateForceInViconGlobalCoords(forcePlateIndex);

                // Get the moment components
                forcePlatesMomentsViconGlobalCoords[forcePlateIndex] = markerDataDistributorScript.GetForcePlateMomentInViconGlobalCoords(forcePlateIndex);
            }

            // Now, compute the global center of pressure
            if (numberOfForcePlatesStreamingData == 1) // if there's only one force plate
            {
                globalCenterOfPressureInViconFrameMm = forcePlatesCenterOfPressure[0];

                // Must convert from the Vicon data stream unit (m) to mm
                globalCenterOfPressureInViconFrameMm = globalCenterOfPressureInViconFrameMm * 1000.0f;

            }
            else if (numberOfForcePlatesStreamingData == 2) // if there're two force plates
            {
                globalCenterOfPressureInViconFrameMm = ComputeGlobalCenterOfPressureFromBothForcePlates();

                // Must convert from the Vicon data stream unit (m) to mm
                globalCenterOfPressureInViconFrameMm = globalCenterOfPressureInViconFrameMm * 1000.0f;

                /*Debug.Log("Global COP (x,y,z): (" + globalCenterOfPressureInViconFrameMm.x + ", " + globalCenterOfPressureInViconFrameMm.y + ", " +
                    globalCenterOfPressureInViconFrameMm.z + ")");*/
            }
            else
            {
                //error - too many or too few force plates
            }

            // Now get the sync pin value
            if (viconAnalogSyncPinAvailableFlag == true)
            {
                mostRecentSyncPinVoltageValue =
                    markerDataDistributorScript.GetAnalogPinVoltageGivenDeviceAndComponentNames(nameOfViconLockAnalogInput, syncPinComponentName);
                //Debug.Log("Analog SYNC pin value: " + mostRecentSyncPinVoltageValue);            }
            }
        }
    }




    // BEGIN: Getter functions *********************************************************************************

    public bool getForcePlateDataAvailableViaDataStreamStatus()
    {
        if(currentState == setupStateString)
        {
            return false;
        }
        else
        {
            return true;
        }
    }


    public Vector3 getMostRecentCenterOfPressureInViconFrame()
    {
        return globalCenterOfPressureInViconFrameMm;
    }

    public Vector3[] getAllForcePlateForces()
    {
        return forcePlatesForcesViconGlobalCoords;

    }

    public Vector3[] GetAllForcePlateTorques()
    {
        return forcePlatesMomentsViconGlobalCoords;
    }


    public float GetMostRecentSyncPinVoltageValue()
    {
            if (viconAnalogSyncPinAvailableFlag == true)
            {
                return mostRecentSyncPinVoltageValue;
            }
            else // if there's no data about the pin state
            {
                return -1.0f;
            }
    }






    // END: Getter functions *********************************************************************************





    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if (newState != currentState)
        {
            Debug.Log("Transitioning states from " + currentState + " to " + newState);
            // call the exit function for the current state. 
            if (currentState == setupStateString)
            {
                exitSetupState();
            }

            //then call the entry function for the new state
            if (newState == activelyRetrievingDataStateString)
            {
                enterRetrievingDataState();
            }
        }

    }


    private void exitSetupState()
    {
        //nothing needs to happen for now
    }

    private void enterRetrievingDataState()
    {
        // change the current state to the RetrievingData state
        currentState = activelyRetrievingDataStateString;
    }


    // END: State machine state-transitioning functions *********************************************************************************





    private Vector3 ComputeGlobalCenterOfPressureFromBothForcePlates()
    {
        // The global center of pressure is just the weighted average of the x-y locations of each plate COP

        // First get the weighting based on the magnitudes of the vertical force components of each plate
        float weightingForcePlateZero = forcePlatesForcesViconGlobalCoords[0].z / (forcePlatesForcesViconGlobalCoords[0].z + forcePlatesForcesViconGlobalCoords[1].z);
        float weightingForcePlateOne = forcePlatesForcesViconGlobalCoords[1].z / (forcePlatesForcesViconGlobalCoords[0].z + forcePlatesForcesViconGlobalCoords[1].z);

        // Next compute the global COP X and Y positions
        float globalCOPXCoordinate = weightingForcePlateZero * forcePlatesCenterOfPressure[0].x +
            weightingForcePlateOne * forcePlatesCenterOfPressure[1].x;
        float globalCOPYCoordinate = weightingForcePlateZero * forcePlatesCenterOfPressure[0].y +
            weightingForcePlateOne * forcePlatesCenterOfPressure[1].y;

        // Finally, since the z-coordinate should be about the same for both, we'll just take the 
        // average
        float globalCOPZCoordinate = (forcePlatesCenterOfPressure[0].z + forcePlatesCenterOfPressure[1].z) / (2.0f);

        // Package the global COP component positions into a Vector3 and return it
        return new Vector3(globalCOPXCoordinate, globalCOPYCoordinate, globalCOPZCoordinate);
    }


}
