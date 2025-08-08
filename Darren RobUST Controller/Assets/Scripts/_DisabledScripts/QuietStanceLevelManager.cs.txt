using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class QuietStanceLevelManager : LevelManagerScriptAbstractClass
{

    // BEGIN: Public variables needed during experiment, for experimental control*********************
    public bool usingBeltsToApplyForce;
    public bool storeForcePlateData; // Whether or not we are using (and thus should store) force plate data. Expects two force plates if using.
                                     // END: Public variables needed during experiment, for experimental control*********************

    // BEGIN: Public GameObject variables and attached scripts*********************
    public GameObject player; //  the player game object
    public GameObject fixationPoint; //the fixation point (a circle) game object
    private Vector3 fixationPointPosition; //the position of the fixation point in Unity frame.
    public Camera mainCamera;
    private CameraSettingsController mainCameraSettingsControlScript;
    public GameObject forceFieldHighLevelControllerObject;
    private ForceFieldHighLevelControllerScript forceFieldHighLevelControllerScript; // The script with the FF high-level controller. 
                                                                                     // The level manager only needs a reference to see if 
                                                                                     // the FF was activated during the perturbation period.
                                                                                     // Coordinating with RobUST (or force field robot, generally)
    public GameObject computeStructureMatrixThisFrameServiceObject;
    private BuildStructureMatricesForBeltsThisFrameScript computeStructureMatrixScript;
    // Force plate data (if using)
    public GameObject forcePlateDataAccessObject;
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;
    // Boundary of stability renderer (needed to load the BoS and control FF limits!)
    private GameObject boundaryOfStabilityRenderer; //the renderer that draws the functional boundary of stability
    private RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript; // the script in the functional boundary of stability renderer

    // END: Public GameObject variables and attached scripts*********************

    

    // states used to control program flow
    private string currentState;
    private const string waitingForSetupStateString = "WAITING_FOR_SETUP"; // Waiting for COM manager and other services.
                                                                           // As float, let it be state 0.0f.
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string waitingForTaskStartButtonState = "WAITING_FOR_START_BUTTON"; // Waiting for the operator to start the stance time. 
                                                                                      // This gives the subject time to move to center of fBoS
                                                                                      // under FF support before starting.
    private const string activeTaskState = "ACTIVE_TASK"; // Wait for player to move to home position for some time. state 1.0f.
    private const string gameOverStateString = "GAME_OVER"; // Game over. State 2.0f.

    // Circle in center size
    private float circleInCenterOfTrajectoryDiameterViconUnits; //the actual central "home" circle diameter in Vicon units [mm]
    private float circleInCenterOfTrajectoryDiameterUnityUnits; //the actual central "home" circle diameter in Unity units

    // The task name (for data saving/loading)
    private const string thisTaskNameString = "QuietStance";

    // boundary of stability loading
    private string subdirectoryWithBoundaryOfStabilityData; // the string specifying the subdirectory (name) we'll load the 
                                                            // boundary of stability data from

    // Monitoring block and trial number
    // Note: the concept of trial still applies. 1 trial = 1 period of quiet stance. 
    // Typically, we'll have only one block and 1 trial per condition.
    public uint[] numberOfBlocks; // How many blocks to use. The element values in the array don't matter.
    public uint numTrialsPerBlock; // How many trials per block.
    private int currentTrialNumber = 0;
    private int currentBlockNumber = 0;

    // The desired length of the trial in milliseconds
    private float stanceTrialTimeInMs = 60000.0f; 

    // A stopwatch to set the length of the quiet stance trial
    private Stopwatch stanceTrialStopwatch = new Stopwatch();

    // subject-specific data
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data

    // data recording
    public GameObject generalDataRecorder; //the GameObject that stores data and ultimately writes it to file (CSV)
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    private string subdirectoryName; //the string specifying the subdirectory (name) we'll be saving to in this session
    private string mostRecentFileNameStub; //the string specifying the .csv file save name for the frame, without the suffix specifying whether it's marker, frame, or trial data.

    // COM manager (Vicon data)
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    private Vector3 lastComPositionViconCoords = new Vector3(-1.0f, -1.0f, -1.0f); //the last position of the COM retrieved from the center of mass manager. Used to see if the new COM position has been updated.

    // Structure matrix setup data loading
    private string subdirectoryWithSetupDataForStructureMatrixComputation; // the string specifying the subdirectory (name) we'll load the 
                                                                           // data needed for structure matrix computation from

    // stimulation status
    private string currentStimulationStatus; //the current stimulation status for this block
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // Base of support
    private float centerOfBaseOfSupportXPosViconFrame;
    private float centerOfBaseOfSupportYPosViconFrame;
    private float leftEdgeBaseOfSupportXPosInViconCoords;
    private float rightEdgeBaseOfSupportXPosInViconCoords;
    private float frontEdgeBaseOfSupportYPosInViconCoords;
    private float backEdgeBaseOfSupportYPosInViconCoords;

    // Excursion distances read from file (from THAT day's excursion trial)
    private float[] excursionDistancesPerDirectionViconFrame;

    // Tracking if the FF was activated during the perturbation period (pert onset until returned home)
    private bool forceFieldActivatedDuringPerturbationFlag = false;

    // Communication with force field robot (Robust)
    public GameObject forceFieldRobotTcpServerObject;
    private CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;
    public bool communicateWithForceFieldRobot;

    // Synchronize with external hardware (including EMGs)
    public GameObject communicateWithPhotonViaSerialObject;
    private CommunicateWithPhotonViaSerial communicateWithPhotonScript;
    public bool syncingWithExternalHardwareFlag; // Whether or not we're using the System Sync object to sync with EMGs and Vicon nexus
    private float unityFrameTimeAtWhichHardwareSyncSent = 0.0f; // The Time.time of the Update() frame when the hardware sync signal command was sent.

    // EMG data streaming
    public bool streamingEmgDataFlag; // Whether to run the EMG service (true) or not (false)
    public GameObject emgDataStreamerObject;
    private StreamAndRecordEmgData emgDataStreamerScript; // communicates with Delsys base station, reads and saves EMG data
    private bool emgBaseIsReadyForTriggerStatus = false; // whether the EMG base station is ready for the sync trigger (true) or not (false)
    private uint millisecondsDelayForStartEmgSyncSignal = 1000; // How long to wait between base station being armed for sync and actually
                                                                // sending the start sync signal (at minimum)
    private bool hasEmgSyncStartSignalBeenSentFlag = false; // A flag that is flipped to true when the EMG sync signal (and, thus, start data stream)
                                                            // was sent to the Delsys base station.
    private Stopwatch delayStartSyncSignalStopwatch = new Stopwatch(); // A stopwatch to add a small delay to sending our photon START sync signal
                                                                       // Seems necessary for Delsys base station.

    // The mapping function from Vicon frame to Unity frame variables.
    private float leftEdgeOfBaseOfSupportInViewportCoords = 0.1f;
    private float rightEdgeOfBaseOfSupportInViewportCoords; //initialized in Start()
    private float backEdgeOfBaseOfSupportInViewportCoords = 0.1f;
    private float frontEdgeOfBaseOfSupportInViewportCoords; //initialized in Start()

    // The text showing the state
    public Text textCurrentState;

    // Start is called before the first frame update
    void Start()
    {

        // Set the initial state. We start in the waiting for home state 
        enterWaitingForSetupState();

        // Get the script that computes the structure matrix for the current frame (for when using a cable-driven robot)
        computeStructureMatrixScript = computeStructureMatrixThisFrameServiceObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

        // marker data and center of mass manager
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // data saving
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // get the camera settings control script 
        mainCameraSettingsControlScript = mainCamera.GetComponent<CameraSettingsController>();

        // Get the script inside the functional boundary of stability renderer
        GameObject[] boundaryOfStabilityRenderers = GameObject.FindGameObjectsWithTag("BoundaryOfStability");
        if (boundaryOfStabilityRenderers.Length > 0) //if there are any boundary of stability renderers
        {
            boundaryOfStabilityRenderer = boundaryOfStabilityRenderers[0];
        }
        boundaryOfStabilityRendererScript = boundaryOfStabilityRenderer.GetComponent<RenderBoundaryOfStabilityScript>();

        // Get the force field high-level controller (used to see if FF was activated during perturbation only).
        forceFieldHighLevelControllerScript = forceFieldHighLevelControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();

        // Get the communication with force field robot (e.g. RobUST) script
        forceFieldRobotTcpServerScript = forceFieldRobotTcpServerObject.GetComponent<CommunicateWithRobustLabviewTcpServer>();

        // Get the force plate data manager script, if using force plate data
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();
        

        // Get reference to Photon-based hardware sync object
        communicateWithPhotonScript = communicateWithPhotonViaSerialObject.GetComponent<CommunicateWithPhotonViaSerial>();

        // If we're syncing with external hardware (typically the EMGs are included)
        if (syncingWithExternalHardwareFlag == true)
        {
            // Get a reference to the EMGs
            emgDataStreamerScript = emgDataStreamerObject.GetComponent<StreamAndRecordEmgData>();
        }

        // Set the stimulation status.
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();
        Debug.Log("Before setting file naming, set the current stimulation status string to: " + currentStimulationStatus);

        //set the header names for the saved-out data CSV headers
        setFrameAndTrialDataNaming();

        // Determine how/where the edges of the base of support will map onto the screen
        rightEdgeOfBaseOfSupportInViewportCoords = 1.0f - leftEdgeOfBaseOfSupportInViewportCoords;
        frontEdgeOfBaseOfSupportInViewportCoords = 1.0f - backEdgeOfBaseOfSupportInViewportCoords;

        // Move the fixation point circle to the center of the viewport (center of camera's field of view)
        fixationPoint.transform.position = mainCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, fixationPoint.transform.position.z));

    }

    // FixedUpdate is called at a fixed frequency and in synchrony with the physics engine execution
    void Update()
    {
        // Depending on the current state, take action at the start of each frame
        // and transition between states as needed.
        if (currentState == waitingForSetupStateString)
        {
            bool centerOfMassManagerReadyFlag = centerOfMassManagerScript.getCenterOfMassManagerReadyStatus();

            //if the center of mass manager has finished setup and is ready
            if (centerOfMassManagerReadyFlag)
            {
                // Then complete setup that must occur after the COM manager is ready

                // Now that the save folder name has been constructed and the COM manager has been set up, we can tell the functional 
                // boundary of stability renderer to load the saved excursion limits
                boundaryOfStabilityRendererScript.loadBoundaryOfStability(subdirectoryWithBoundaryOfStabilityData);

                // We can also tell the structure matrix computation service to load the setup data needed to 
                // compute the structure matrix (if we're using a force belt)
                if (usingBeltsToApplyForce)
                {
                    // Tell the structure matrix computation script to load daily setup data needed for structure matrix computation
                    computeStructureMatrixScript.loadDailySetupDataForStructureMatrixConstruction(subdirectoryWithSetupDataForStructureMatrixComputation);

                    // Flip the flag in the COM manager that tells it to ping the structure matrix service whenever fresh Vicon data is ready.
                    centerOfMassManagerScript.SetUsingCableDrivenRobotFlagToTrue();
                }

                // Next, get the excursion limits in Vicon coordinates
                excursionDistancesPerDirectionViconFrame = boundaryOfStabilityRendererScript.getExcursionDistancesInViconCoordinates();

                //Get the center of the base of support in Vicon coordinates
                (leftEdgeBaseOfSupportXPosInViconCoords, rightEdgeBaseOfSupportXPosInViconCoords,
                    frontEdgeBaseOfSupportYPosInViconCoords,
                    backEdgeBaseOfSupportYPosInViconCoords) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();
                centerOfBaseOfSupportXPosViconFrame = (leftEdgeBaseOfSupportXPosInViconCoords + rightEdgeBaseOfSupportXPosInViconCoords) / 2.0f;
                centerOfBaseOfSupportYPosViconFrame = (backEdgeBaseOfSupportYPosInViconCoords + frontEdgeBaseOfSupportYPosInViconCoords) / 2.0f;
                Debug.Log("Center of base of support, Vicon frame, in trajectory trace level manager is (x,y): (" +
                    centerOfBaseOfSupportXPosViconFrame + ", " + centerOfBaseOfSupportYPosViconFrame + ")");

                // Render the functional boundary of stability
                drawFunctionalBoundaryOfStability();

                // Send a command to Labview with the task-specific info string (subject number, date, time). 
                forceFieldRobotTcpServerScript.SendCommandWithCurrentTaskInfoString(mostRecentFileNameStub);

                // If we're syncing with external hardware (EMGs), we should move to a special state 
                // for EMG setup
                if (syncingWithExternalHardwareFlag == true)
                {
                    // then move to the waiting for EMG state
                    changeActiveState(waitingForEmgReadyStateString);
                }
                else // If not syncing with hardware, then set up is complete
                {
                    // then proceed by moving to the Active Task state
                    changeActiveState(waitingForTaskStartButtonState);

                    Debug.Log("Level manager setup complete");
                }
            }
        }
        else if (currentState == waitingForEmgReadyStateString)
        {
            // If EMG setup is complete and EMG hardware is ready for a start pulse
            emgBaseIsReadyForTriggerStatus = emgDataStreamerScript.IsBaseStationReadyForSyncSignal();
            if (emgBaseIsReadyForTriggerStatus == true && delayStartSyncSignalStopwatch.IsRunning == false)
            {

                // Call a stopwatch to delay the Photon start signal by 1 second (seems like 
                // that is needed by the Delsys base station).
                delayStartSyncSignalStopwatch.Start();
            }
            else if (emgBaseIsReadyForTriggerStatus == true && delayStartSyncSignalStopwatch.IsRunning == true &&
               delayStartSyncSignalStopwatch.ElapsedMilliseconds > millisecondsDelayForStartEmgSyncSignal)
            {
                // NOTE: we do NOT send the start data stream signal because 
                // we send that at the start of the block.

                // We've accomplished a minimum delay for sending the sync signal, so 
                // switch to the Waiting for Task Start Button state. 
                changeActiveState(waitingForTaskStartButtonState);

                Debug.Log("Level manager setup complete");
            }
        }
        else if (currentState == waitingForTaskStartButtonState) // if we're waiting for the operator to start the task clock
        {
            // Store the Unity frame rate data (mostly, COM position and level manager states)
            // for this frame
            storeFrameData();
        }
        else if (currentState == activeTaskState)
        {
            // Print the time left for the operator 

            // If the trial time has been reached 
            if(stanceTrialStopwatch.IsRunning && stanceTrialStopwatch.ElapsedMilliseconds >= stanceTrialTimeInMs)
            {
                // Increment the trial number and switch states (NOTE: this should typically end the task,
                // since we use 1 trial only per condition)
                string nextState = incrementTrialNumberAndManageState();

                // Change active states
                changeActiveState(nextState);
            }

            // Store the new Unity frame rate data (e.g. trace trajectory position, on-screen player position + COM position for good measure)
            // for this frame
            storeFrameData();
        }
        else if (currentState == gameOverStateString)
        {
            //do nothing in the game over state
        }
    }



    private void drawFunctionalBoundaryOfStability()
    {

        //tell the functional boundary of stability drawing object to draw the BoS, if the object is active.
        boundaryOfStabilityRendererScript.renderBoundaryOfStability();
    }


    // BEGIN: Other public functions*********************************************************************************

    public override string GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    public override Vector3 GetControlPointForRobustForceField()
    {
        if (currentState != waitingForSetupStateString)
        {
            // For now, we assume the control point is the body COM. Can add controls to return 
            // other control points (like pelvis center), if desired.
            return centerOfMassManagerScript.getSubjectCenterOfMassInViconCoordinates();
        }
        else
        {
            return new Vector3(0.0f, 0.0f, 0.0f);
        }
    }

    public override List<Vector3> GetExcursionLimitsFromExcursionCenterInViconUnits()
    {
        return boundaryOfStabilityRendererScript.getExcursionLimitsPerDirectionWithProperSign();
    }

    public override Vector3 GetCenterOfExcursionLimitsInViconFrame()
    {
        // Get edges of base of support
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
            float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();

        //compute center of base of support and return it
        return new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos) / 2.0f,
            (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f,
            0.0f);
    }

    // THE MAPPING FUNCTION from Vicon frame to Unity frame for the Quiet Stance (this task). 
    // We use the SAME code as from ExcursionLevelManager.
    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        // Convert the position from Vicon coordinates to Viewport coordinates
        float pointInViewportCoordsX = leftEdgeOfBaseOfSupportInViewportCoords + ((pointInViconFrame.x - leftEdgeBaseOfSupportXPosInViconCoords) / (rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords)) * (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords);
        float pointInViewportCoordsY = backEdgeOfBaseOfSupportInViewportCoords + ((pointInViconFrame.y - backEdgeBaseOfSupportYPosInViconCoords) / (frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords)) * (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords);

        //then convert viewport coordinates to Unity world coordinates
        Vector3 pointInUnityWorldCoords = mainCamera.ViewportToWorldPoint(new Vector3(pointInViewportCoordsX, pointInViewportCoordsY, 5.0f));

        return pointInUnityWorldCoords;
        // return centerOfMassManagerScript.convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(pointInViconFrame);
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // convert Unity world coordinates to Viewport coordinates
        Vector3 pointInViewportCoordinates = mainCamera.WorldToViewportPoint(pointInUnityFrame);

        // convert Viewport coordinates to Vicon coordinates (defined relative to the base of support)
        float comXPositionInViconCoords = leftEdgeBaseOfSupportXPosInViconCoords + ((pointInViewportCoordinates.x - leftEdgeOfBaseOfSupportInViewportCoords) / (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords)) * (rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        float comYPositionInViconCoords = backEdgeBaseOfSupportYPosInViconCoords + ((pointInViewportCoordinates.y - backEdgeOfBaseOfSupportInViewportCoords) / (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords)) * (frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);

        // return the point in Vicon coordinates
        return new Vector3(comXPositionInViconCoords, comYPositionInViconCoords, 0.0f);


        //return centerOfMassManagerScript.convertUnityWorldCoordinatesToViconCoordinates(pointInUnityFrame);
    }

    public override string GetCurrentDesiredForceFieldTypeSpecifier()
    {
        return "";
    }

    public override bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }



    public void TransitionToActiveStateAndStartQuietStanceTimer()
    {
        // Transition to the active task state
        changeActiveState(activeTaskState);
    }

    // END: Other public functions*********************************************************************************




    // BEGIN: Set-up only functions*********************************************************************************

    private void setFrameAndTrialDataNaming()
    {
        // 1.) Frame data naming
        // A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[] { };
        csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START","TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE","COM_POS_X","COM_POS_Y", "COM_POS_Z", "IS_COM_POS_FRESH_FLAG",
           "MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER", "BLOCK_NUMBER", "TRIAL_NUMBER", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT"};

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // 2.) Data subdirectory naming for trajectory tracing data
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).

        // Now that we've done this formatting, we should be able to reconstruct the directory containing the 
        // Excursion limits collected on the same day.
        subdirectoryWithBoundaryOfStabilityData = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "Excursion" + "/" + dateString + "/";

        // We should also be able to reconstruct the directory containing the setup data for computing the structure matrix
        subdirectoryWithSetupDataForStructureMatrixComputation = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "StructureMatrixData" + "/" + dateString + "/";

        // Build the name of the subdirectory that will contain all of the output files for trajectory trace this session
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + thisTaskNameString + "/" + dateString + "/";
        Debug.Log("Will store data for this task in subdirectory: " + subdirectoryString);
        //set the frame data and the task-specific trial subdirectory name (will go inside the CSV folder in Assets)
        subdirectoryName = subdirectoryString; //store as an instance variable so that it can be used for the marker and trial data
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvEmgDataSubdirectoryName(subdirectoryString);

        // 4.) Call the function to set the file names (within the subdirectory) for the current block
        setFileNamesForCurrentBlockTrialAndFrameData();
    }


    private void setFileNamesForCurrentBlockTrialAndFrameData()
    {
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).
        string timeString = localDate.ToString("t");
        timeString = timeString.Replace(" ", "_");
        timeString = timeString.Replace(":", "_");
        string delimiter = "_";
        string dateAndTimeString = dateString + delimiter + timeString; //concatenate date ("d") and time ("t")

        // Set the frame data file name
        string subjectSpecificInfoString = subjectSpecificDataScript.getSubjectSummaryStringForFileNaming();
        string blockNumberAsString = "Block" + currentBlockNumber; // We'll save each block into its own file
        string fileNameStub = thisTaskNameString + delimiter + subjectSpecificInfoString + delimiter + currentStimulationStatus + delimiter + dateAndTimeString + delimiter + blockNumberAsString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the task-specific trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);

        // Set the EMG data file name (even if not using EMG data)
        string fileNameEmgData = fileNameStub + "_Emg_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvEmgDataFileName(fileNameEmgData);
    }

    // END: Set-up only functions*********************************************************************************


    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if (newState != currentState)
        {
            Debug.Log("Transitioning states from " + currentState + " to " + newState);
            // call the exit function for the current state. 
            // Note that we never exit the EndGame state.
            if (currentState == waitingForEmgReadyStateString)
            {
                exitWaitingForEmgReadyState();
            }
            if(currentState == waitingForTaskStartButtonState)
            {
                exitWaitingForTaskStartButtonState();

            }
            if (currentState == activeTaskState)
            {
                exitActiveTaskState();
            }


            //then call the entry function for the new state
            if (newState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            if (newState == waitingForTaskStartButtonState)
            {
                enterWaitingForTaskStartButtonState();

            }
            if (newState == activeTaskState)
            {
                enterActiveTaskState();
            }
            else if (newState == gameOverStateString)
            {
                enterGameOverState();
            }
        }
    }

    private void enterWaitingForEmgReadyState()
    {
        //set the current state
        currentState = waitingForEmgReadyStateString;
    }

    private void exitWaitingForEmgReadyState()
    {
        // do nothing for now
    }

    private void enterWaitingForSetupState()
    {
        //set the current state
        currentState = waitingForSetupStateString;
    }

    private void exitWaitingForSetupState()
    {
        //nothing needs to happen 
    }

    private void enterWaitingForTaskStartButtonState()
    {
        //set the current state
        currentState = waitingForTaskStartButtonState;

        // Sync the external hardware at the start of a bout of quiet standing
        // (if we're syncing with external hardware (e.g. EMGs))
        if (syncingWithExternalHardwareFlag == true)
        {
            // Tell the Photon to pulse the start pin and raise the analog sync pin
            communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

            // Store the hardware sync signal sent time
            unityFrameTimeAtWhichHardwareSyncSent = Time.time;
        }
    }

    private void exitWaitingForTaskStartButtonState()
    {
        // do nothing for now
    }

    private void enterActiveTaskState()
    {

        Debug.Log("Changing to Active Task state");
        //change the current state to the Waiting For Home state
        currentState = activeTaskState;

        // Hide the player object
        player.GetComponent<Renderer>().enabled = false;

        // Start the stopwatch that tracks task time. The quiet stance trial will end
        // when the stopwatch has logged the desired time.
        stanceTrialStopwatch.Restart();

        // Change the countdown text to say "Active trial"
        textCurrentState.text = "Active trial: " + (stanceTrialTimeInMs/1000.0f) + " sec.";
    }


    private void exitActiveTaskState()
    {
        // Show the player object
        player.GetComponent<Renderer>().enabled = true;
    }

    private void enterGameOverState()
    {
        Debug.Log("Changing to Game Over state");

        // Update the current state text
        textCurrentState.text = "Task complete.";


        // Tell the external hardware that the recording is over, if we're using external hardware
        if (syncingWithExternalHardwareFlag)
        {
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();
        }

        // Write the stored frame data and marker data to file
        tellDataRecorderToWriteStoredDataToFile();

        //change the current state to the Game Over state
        currentState = gameOverStateString;

    }

    // END: State machine state-transitioning functions *********************************************************************************






    // BEGIN: Main loop functions ( e.g. called from Update() )********************************************************************************


    private string incrementTrialNumberAndManageState()
    {

        // Desired next state 
        string desiredNextStateString = "";

        //increment the trial number 
        currentTrialNumber = currentTrialNumber + 1;

        //if the block is over
        if (currentTrialNumber >= numTrialsPerBlock)
        {
            //increment the block number
            currentBlockNumber = currentBlockNumber + 1;

            // Reset the current trial number
            currentTrialNumber = 0;

            // If we have completed all desired blocks
            if (currentBlockNumber < numberOfBlocks.Length)
            {
                // Then the next state should be the Waiting for Task Start Button state, 
                // since we're starting a new block
                desiredNextStateString = waitingForTaskStartButtonState;
            }
            else // If we've already completed all desired blocks
            {
                // Then the task is complete. Enter Game Over state.
                desiredNextStateString = gameOverStateString;
            }
        }
        else // If we still have more trials in this block
        {
            // Then the next state should be the Waiting for Task Start Button state, 
            // since the subject is ready to continue in quiet stance
            desiredNextStateString = waitingForTaskStartButtonState;
        }

        // Return the next state
        return desiredNextStateString;
    }



    // END: Main loop functions ( e.g. called from Update() )*********************************************************************************






    // START: Data storage functions (called to store frame, trial, COM data) ********************************************




    //Note, this function is called from Update(), not FixedUpdate(), and thus will record at a higher frequency than
    //FixedUpdate() most of the time.
    private void storeFrameData()
    {
        // The list that will store the data
        List<float> frameDataToStore = new List<float>();
        // Note, the header names for all of the data we will store are specifed in Start()

        // Get the time called at the beginning of this frame (this call to Update())
        frameDataToStore.Add(Time.time); // Time.time does just that - gets the time at the start of the Update() call.

        // Store the frame start time of the frame where Unity sent the sync signal command to external hardware
        frameDataToStore.Add(unityFrameTimeAtWhichHardwareSyncSent);

        // The analog sync pin voltage (high = EMG streaming, low = EMG stopped)
        float analogSyncPinVoltage = scriptToRetrieveForcePlateData.GetMostRecentSyncPinVoltageValue();
        frameDataToStore.Add(analogSyncPinVoltage);

        // Get needed data from the COM position manager
        // Get COM position in Vicon coordinates
        Vector3 comPositionViconCoords = centerOfMassManagerScript.getSubjectCenterOfMassInViconCoordinates();
        frameDataToStore.Add(comPositionViconCoords.x);
        frameDataToStore.Add(comPositionViconCoords.y);
        frameDataToStore.Add(comPositionViconCoords.z);

        //Determine if the retrieved COM position is new ("fresh") or not, and create a boolean flag to indicate if it is fresh (true) or not (false)
        bool isComPositionViconCoordsFresh;
        if (lastComPositionViconCoords != comPositionViconCoords)
        {
            isComPositionViconCoordsFresh = true;
        }
        else
        {
            isComPositionViconCoordsFresh = false;
        }
        frameDataToStore.Add(Convert.ToSingle(isComPositionViconCoordsFresh));

        //update the stored COM position 
        lastComPositionViconCoords = comPositionViconCoords;


        // Get the time information (frame)
        uint mostRecentlyAccessedViconFrameNumber = centerOfMassManagerScript.getMostRecentlyAccessedViconFrameNumber();
        frameDataToStore.Add((float)mostRecentlyAccessedViconFrameNumber);

        // Get the trial # and block #
        float blockNumber = (float)currentBlockNumber;
        frameDataToStore.Add(blockNumber);
        float trialNumber = (float)currentTrialNumber; //we only have trials for now. Should we implement a block format for excursion?
        frameDataToStore.Add(trialNumber);

        // Store the logical flags relevant to this task (likely, state is the only one, but think on it)
        float currentStateFloat = -1.0f;
        if (currentState == waitingForSetupStateString)
        {
            currentStateFloat = 0.0f;
        }
        if (currentState == waitingForEmgReadyStateString)
        {
            currentStateFloat = 1.0f;
        }
        if (currentState == waitingForTaskStartButtonState)
        {
            currentStateFloat = 2.0f;
        }
        else if (currentState == activeTaskState)
        {
            currentStateFloat = 3.0f;
        }
        else if (currentState == gameOverStateString)
        {
            currentStateFloat = 4.0f;
        }
        else
        {
            //let the state remain as -1.0f, some error occurred
        }
        frameDataToStore.Add(currentStateFloat);

        //Send the data to the general data recorder. It will be stored in memory until it is written to a CSV file.
        generalDataRecorderScript.storeRowOfFrameData(frameDataToStore);

    }


    //This function is called when the frame data should be written to file (typically at the end of a block of data collection). 
    private void tellDataRecorderToWriteStoredDataToFile()
    {
        // Tell the general data recorder to write the frame data to file
        generalDataRecorderScript.writeFrameDataToFile();

        // If we're using EMG data (if the size of the EMG data is not zero)
        int numberEmgSamplesStored = generalDataRecorderScript.GetNumberOfEmgDataRowsStored();
        if (numberEmgSamplesStored != 0)
        {
            Debug.Log("Writing EMG data to file. EMG data has num. samples: " + numberEmgSamplesStored);
            // Tell the general data recorder to write the EMG data to file
            generalDataRecorderScript.writeEmgDataToFile();
        }

        //Also, tell the center of mass Manager object to tell the general data recorder to store the marker/COM data to file. 
        centerOfMassManagerScript.tellDataRecorderToSaveStoredDataToFile(subdirectoryName, mostRecentFileNameStub);
    }

    // END: Data storage functions **************************************************************************************

}

