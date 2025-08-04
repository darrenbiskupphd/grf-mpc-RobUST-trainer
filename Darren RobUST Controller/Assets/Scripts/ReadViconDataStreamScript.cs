using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

using ViconDataStreamSDK.CSharp;

namespace UnityVicon {

    public class ReadViconDataStreamScript : MonoBehaviour
    {
        public string subjectName = ""; //a string containing the first (and usually only) subject name. Can actually be retrieved from Nexus with the Datastream SDK.
        public ViconDataStreamClient Client; //the Vicon Data Stream prefab made available in the Vicon/Unity plugin. Like a wrapper for the DataStream SDK, it has many getter functions that call the DataStream SDK using mClient.someFunction()

        //Vicon data stream - startup variables (info about the subject skeleton, subject name, etc.
        bool subjectSetupComplete = false;
        bool markerDataEnabled = false; //if the data stream has been enabled to send marker data
        bool deviceDataEnabled = false; //if the data stream has been enabled to send device data
        uint numberOfSubjects; //the number of subjects in the frame (always one for us, if not zero).
        uint numberOfMarkersInSubjectSkeleton = 0; //how many labeled markers the subject skeleton contains (NOT how many are currently visible by the cameras)
        string[] subjectSkeletonMarkerNames; //the names of the markers in the subject skeleton

        //Vicon data stream fetched variables 
        uint currentFrameNumber; //the frame number of the most recent frame fetched from the Vicon data stream
        bool[] markersOcclusionStatus; //the most recent status for each marker, specifying whether it is occluded (true) or not (false)
        float[] markersPositionX; //the most recent x-axis coordinate of all of the subject's markers 
        float[] markersPositionY; //the most recent y-axis coordinate of all of the subject's markers 
        float[] markersPositionZ; //the most recent z-axis coordinate of all of the subject's markers 

        // Vicon marker data needed
        public bool usingViconMarkerDataFlag;

        // Stance model - needed to see if we're using Vive or Vicon for control
        public KinematicModelClass stanceModelScript;


        // Start is called before the first frame update
        void Start()
        {


        }



        // LateUpdate is called once per frame
        void Update()
        {
            //get the frame number
            uint newFrameNumber = Client.GetFrameNumber();

            //if the frame number is not the same as the current frame number, then respond by either continuing subject setup or updating the game by reading subject marker data
            if (newFrameNumber != currentFrameNumber)
            {
                if (subjectSetupComplete)
                {
                    // If we're using marker data
                    if(stanceModelScript.dataSourceSelector != ModelDataSourceSelector.ViveOnly)
                    {
                        // Update marker positions
                        updateMarkerPositionAndOcclusionStatus();
                    }     
                } else //finish getting all subject-specific info (skeleton marker number, marker names, etc.)
                {
                    subjectSetupComplete = attemptSubjectSetup(); //attempt to get all of the subject data we'll need before reading marker positions
                }
               
            }
        }



        //this function attempts subject setup in a cascading order. If the bottom level is reached and returns a valid result, setup is complete.
        private bool attemptSubjectSetup()
        {
            bool localSubjectSetupFlag = false; //keeps track of whether or not subject setup is complete.

            //First ensure that the client is enabled to get marker data 
            if (!markerDataEnabled || !deviceDataEnabled)
            {
                // There may not be a need to use client pull pre-fetch mode - achieving 50 Hz in client pull mode.
                //bool modeChangedToClientPullPreFetchFlag = Client.SetDataStreamInClientPullPreFetchMode();
                //Debug.Log("Vicon SDK mode: changed to client pull pre-fetch? " + modeChangedToClientPullPreFetchFlag);

                markerDataEnabled = Client.EnableMarkerData();
                deviceDataEnabled = Client.EnableDeviceData();
                Debug.Log("Is marker data enabled in the Vicon data stream (true/false): " + markerDataEnabled);
                Debug.Log("Is device data enabled in the Vicon data stream (true/false): " + deviceDataEnabled);

            }
            else //if marker data is enabled
            {
                // If we're just using Vive data for control, then we're not using Vicon markers in Unity
                if (stanceModelScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
                {
                    // Then simply enabling Device Data means setup is complete.
                    localSubjectSetupFlag = true;
                } else // else if we are using Vicon markers for robot control/in Unity
                {
                    if (numberOfSubjects == 0)
                    { //if no subjects are showing up, then just keep checking if one has appeared
                        numberOfSubjects = Client.GetSubjectCount();
                    }
                    else //if there are valid subjects
                    {
                        if (subjectName == "") //if there are no valid subject names yet, then load them
                        {
                            subjectName = Client.GetFirstSubjectName();
                            Debug.Log("Vicon Data Stream's first subject has subject name: " + subjectName);
                        }
                        else //if we have a valid subject name, then get the number of markers in the subject skeleton and the markers' names
                        {
                            getAvailableMarkerNumberAndNames(subjectName);

                            //check if we got a valid number of subject markers (non-zero). Use this as the "setup complete" criterion.
                            if (numberOfMarkersInSubjectSkeleton != 0)
                            {
                                // Call the function to fill the marker position and occlusion status data 
                                updateMarkerPositionAndOcclusionStatus();

                                // Note that subject setup is complete
                                localSubjectSetupFlag = true; //we've completed our ordered/hierarchical setup phase
                            }
                        }
                    }
                }
            }
           
            //return the setup status boolean flag
            return localSubjectSetupFlag;
        }


        //This function is called in setup only! Gets the number and name of all markers in the subject's skeleton.
        private void getAvailableMarkerNumberAndNames(string subjectName)
        {
            //first get the number of markers available for the subject
            numberOfMarkersInSubjectSkeleton = Client.GetSkeletonMarkerCount(subjectName);
            //then get the names of all the available markers 
            subjectSkeletonMarkerNames = Client.GetNamesOfAllSubjectSkeletonMarkers(subjectName, numberOfMarkersInSubjectSkeleton);
        }


        //This function retrieves the most recent marker data, specifically position and occlusion status.
        private void updateMarkerPositionAndOcclusionStatus()
        {
            var result = Client.getAllMarkerPositionsAndOcclusionStatus(subjectName, subjectSkeletonMarkerNames);
            markersOcclusionStatus = result.Item1; //get the boolean flags for all markers indicating whether or not they are occluded (occluded = true, visible = false)
            markersPositionX = result.Item2; //get the X-coordinate of all of the markers
            markersPositionY = result.Item3; //get the Y-coordinate of all of the markers
            markersPositionZ = result.Item4; //get the Z-coordinate of all of the markers

            uint testIndex = 1;
            //Debug.Log("The marker at index " + testIndex + " has X-axis position: " + markersPositionX[testIndex]);
        }



        //Begin getter functions that can be used in any scene to access marker positions***********************************************************************


        //This function returns a boolean, indicating whether this script has finished setup and is ready to distribute Vicon data stream data
        // (true) or not (false)
        public bool getReadyStatusOfViconDataStreamDistributor()
        {
            return subjectSetupComplete;
        }



        //This function returns a uint equal to the frame number of the frame last retrieved from the Vicon DataStream. 
        public uint getLastRetrievedViconFrameNumber()
        {
            uint frameNumber = Client.getFrameNumber();
            return frameNumber;
        }




        //This very general function can be used by any object that needs marker data from the most recent frame. 
        //Just ensure that the calling object is called after this object is called by specifying call order. 
        public (bool, float, float, float) getMarkerOcclusionStatusAndPositionByName(string markerName)
        {
            //find the index of the requested marker, using its name
            int markerIndexFromName = Array.IndexOf(subjectSkeletonMarkerNames, markerName);
            //instantiate return values
            bool occlusionStatus;
            float markerXPos;
            float markerYPos;
            float markerZPos;

            if (markerIndexFromName >= 0)//if the marker name is a valid marker name
            {
                //get the needed information about the marker, specifically occlusion status and position
                occlusionStatus = markersOcclusionStatus[markerIndexFromName];
                markerXPos = markersPositionX[markerIndexFromName];
                markerYPos = markersPositionY[markerIndexFromName];
                markerZPos = markersPositionZ[markerIndexFromName];
            }
            else //if the marker name is not valid, return default values and indicate that the marker is occluded
            {
                occlusionStatus = true;
                markerXPos = 0;
                markerYPos = 0;
                markerZPos = 0;
            }

            //Debug.Log("Requested marker with index " + markerIndexFromName + " has position (x,y,z): (" + markerXPos + "," + markerYPos + "," + markerZPos + ")");

            //return the marker information as a tuple
            return (occlusionStatus, markerXPos, markerYPos, markerZPos);
        }



        //End getter functions that can be used in any scene to access marker positions***********************************************************************



        //START: getter functions that can be used in any scene to access device (force plate, EMG) data***********************************************************************

        public (string[], ViconDataStreamSDK.CSharp.DeviceType[], 
            List<string[]>, List<ViconDataStreamSDK.CSharp.Unit[]>) GetAllDeviceNamesAndTypes()
        {
            // Get the device count 
            (ViconDataStreamSDK.CSharp.Result retrieveSuccessFlag, uint deviceCount) = Client.getDeviceCount();

            Debug.Log("Vicon data stream device count retrieve success flag is : " + retrieveSuccessFlag);
            Debug.Log("Vicon data stream device count is: " + deviceCount);

            // Initialize lists to store the device names and types 
            string[] AllDeviceNames = new string[deviceCount];
            ViconDataStreamSDK.CSharp.DeviceType[] AllDeviceTypes = new ViconDataStreamSDK.CSharp.DeviceType[deviceCount];
            List<string[]> OutputComponentNamesForEachDevice = new List<string[]>();
            List<ViconDataStreamSDK.CSharp.Unit[]> OutputComponentUnitsForEachDevice = new List<ViconDataStreamSDK.CSharp.Unit[]>();

            // For each available device 
            for (uint deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                // Get the device name and type by index 
                (string deviceName, ViconDataStreamSDK.CSharp.DeviceType deviceType) = Client.getDeviceName(deviceIndex);

                // Store the device name and device type in their respective arrays 
                AllDeviceNames[deviceIndex] = deviceName;
                AllDeviceTypes[deviceIndex] = deviceType;

                // Get the device output count 
                uint deviceOutputCount = Client.GetDeviceOutputCount(deviceName);

                // Get the device component output names for the current device 
                Debug.Log("Current device name: " + deviceName);
                string[] deviceComponentOutputNames = new string[deviceOutputCount];
                ViconDataStreamSDK.CSharp.Unit[] deviceComponentUnitsSI = new ViconDataStreamSDK.CSharp.Unit[deviceOutputCount];
                for (uint deviceComponentOutputIndex = 0; deviceComponentOutputIndex < deviceOutputCount; deviceComponentOutputIndex++)
                {
                    (string outputName, string componentName, ViconDataStreamSDK.CSharp.Unit outputUnitSI) = 
                        Client.GetDeviceOutputNameWithComponentName(deviceName, deviceComponentOutputIndex);
                    deviceComponentOutputNames[deviceComponentOutputIndex] = componentName;
                    deviceComponentUnitsSI[deviceComponentOutputIndex] = outputUnitSI;

                    Debug.Log("Device output component name: " + componentName + " and output unit: " + outputUnitSI);
                }
                OutputComponentNamesForEachDevice.Add(deviceComponentOutputNames);
                OutputComponentUnitsForEachDevice.Add(deviceComponentUnitsSI);
            }

            // Return the device arrays 
            return (AllDeviceNames, AllDeviceTypes, OutputComponentNamesForEachDevice, 
                OutputComponentUnitsForEachDevice);
        }


        public uint GetForcePlateCount()
        {
            (ViconDataStreamSDK.CSharp.Result retrieveSuccessFlag, uint forcePlateCount) = Client.GetForcePlateCount();

            return forcePlateCount;
        }


        public Vector3 GetForcePlateCenterOfPressureInViconGlobalCoords(uint forcePlateIndex)
        {
            (ViconDataStreamSDK.CSharp.Result retrieveSuccessFlag,
                Vector3 globalCenterOfPressureThisPlate) = Client.GetForcePlateGlobalCentreOfPressure(forcePlateIndex);

            return globalCenterOfPressureThisPlate;
        }

        public Vector3 GetForcePlateForceInViconGlobalCoords(uint forcePlateIndex)
        {
            (ViconDataStreamSDK.CSharp.Result retrieveSuccessFlag,
                Vector3 forcePlateForceInViconGlobalCoords) = Client.GetForcePlateForceInViconGlobalCoords(forcePlateIndex);

            return forcePlateForceInViconGlobalCoords;
        }

        public Vector3 GetForcePlateMomentInViconGlobalCoords(uint forcePlateIndex)
        {
            (ViconDataStreamSDK.CSharp.Result retrieveSuccessFlag,
                Vector3 forcePlateMomentInViconGlobalCoords) = Client.GetForcePlateMomentInViconGlobalCoords(forcePlateIndex);

            return forcePlateMomentInViconGlobalCoords;
        }

        public float GetAnalogPinVoltageGivenDeviceAndComponentNames(string deviceName, string pinComponentName)
        {

            (ViconDataStreamSDK.CSharp.Result retrieveSuccessFlag,
                float pinAnalogVoltageValue) = Client.GetAnalogPinVoltageGivenDeviceAndComponentNames(deviceName, pinComponentName);

            return pinAnalogVoltageValue;
        }



        //END: getter functions that can be used in any scene to access device (force plate, EMG) data***********************************************************************


    } //end class
} //end namespace
