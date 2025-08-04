using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class ReachingExcursionLevelManager : LevelManagerScriptAbstractClass
{
    public uint excursionsPerAxis = 3; //how many excursions along each axis will be conducted
    private uint numberOfExcursionDirections = 8;
    public uint[] excursionOrderList; //the list of excursion orders that will be carried out. Filled with integers ranging from 0 to numberOfExcursionDirections - 1.
    private uint currentExcursionDirection = 0; //ranges from 0 to numberOfExcursionDirections - 1.
    private string[] excursionDirectionNames;
    private float[] excursionDirectionAnglesFromRightwardsCcw;// = new float[] { 0.0f, 45.0f, 90.0f, 135.0f, 180.0f, 225.0f, 270.0f, 315.0f };
    public float maximumAngularDeviationFromAxisInDegrees = 10; //plus or minus, in both directions (so, a value of ten degrees would give a total range of 20 degrees)
    public int currentTrialIndex = 0; //the index of the current trial
    private float maximumExcursionAlongAxisThisTrial = 0; //max distance along the current axis that the average player position has moved from center this trial
    public float distanceFromCenterToStartTrial; // how far the window-averaged (!) player position must be from the center to start a new trial (in the correct region, as well)
    public float distanceFromCenterToEndTrial; // how close the window-averaged player position must be to the center to end an ongoing trial
    public GameObject player; //the player game object
    public GameObject leftHand; //the left hand game object
    public GameObject rightHand; //the left hand game object
    public GameObject chestObject; // the chest game object
    public GameObject excursionIndicator; //the indicator of how far the player has gone along an axis. Averaged over a window.
    public GameObject axesRenderer; //the object containing the line renderers of the axes and the rendering script
    private RenderExcursionAxes axesRendererScript; //the script that renders the axes
    private Vector3 axesCenter; //the center position of the axes
    public float indicatorStartDistanceAlongAxis; //how far from the center the indicator starts, which helps indicate which axis is the goal axis.

    private float fixedUpdateFrequency;  //how often fixed update is called. Based on the value of Time.fixedDeltaTime (which is mutable, by the way).
    private int numberChestPositionsToAverage; //how many samples of the player position we average to get indicator max excursion. Based on fixed update frequency and desired averaging period.

    // Task name
    private const string thisTaskNameString = "Excursion";

    // Where the chest home position is defined in Unity frame
    private Vector3 homeChestPositionUnityFrame;
    private float nearHomeRadiusInMeters = 0.1f; // meters 

    // The chest averaging period/window
    private bool[] isChestInProperRegion; //an array recording if the player was in a valid region for moving along the specified axis, at a particular time sample.
    public float chestPositionAveragingPeriod; //over how long we average the player's position to get a sense of their excursion.
    public Vector3[] chestPositionsInLastAveragingPeriod; //an array that stores the player's position over a certain period, which we'll average to update the indicator

    // The hand averaging period/window
    private bool[] isLeftHandInProperRegion; //an array recording if the left hand was in a valid region for moving along the specified axis, at a particular time sample.
    private bool[] isRightHandInProperRegion; //an array recording if the right hand was in a valid region for moving along the specified axis, at a particular time sample.
    private float handPositionAveragingPeriod; // How long the hand must be in the proper sector to push the target. Set equal to chest position averaging period
    public Vector3[] leftHandPositionsInLastAveragingPeriod; //an array that stores the player's position over a certain period, which we'll average to update the indicator
    public Vector3[] rightHandPositionsInLastAveragingPeriod; //an array that stores the player's position over a certain period, which we'll average to update the indicator


    // program flow control flags
    private bool centerOfMassManagerReadyStatus = false; //whether or not the center of mass manager is ready to distribute data
    private bool retrievedSubjectSpecificOnScreenExcursionAnglesFlag = false;
    private bool emgBaseIsReadyForTriggerStatus = false; // whether the EMG base station is ready for the sync trigger (true) or not (false)

    // The scene camera
    public Camera sceneCamera; //the camera that visulizes the scene. Used for converting viewport coordinates to world coordinates.


    // block and trial control flags
    private bool inTrialFlag; //indicates whether an excursion has begun (true) or if we're waiting for an excursion trial to start (false)
    private bool activeBlock; //indicates whether or not we're in a block. If we are, run the script core functions, else do nothing.

    //subject-specific data
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data

    // Excursion distances read from file (from THAT day's excursion trial)
    private float[] excursionDistancesPerDirectionViconFrame;

    //marker and center of mass data management
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    Vector3 lastComPositionViconCoords = new Vector3(-1.0f, -1.0f, -1.0f); //the last position of the COM retrieved from the center of mass manager. Used to see if the new COM position has been updated.

    //trial-monitoring
    float currentTrialStartTime = -1.0f; //the current trial's start time (gathered with a call to Time.time when the player enters the proper region, so reported in time relative to application start. Units = seconds).
    float currentTrialEndTime = -1.0f; //the current trial's end time (gathered with a call to Time.time, so reported in time relative to application start. Units = seconds).

    //data recording
    public GameObject generalDataRecorder; //the GameObject that stores data and ultimately writes it to file (CSV)
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    private string subdirectoryName; //the string specifying the subdirectory (name) we'll be saving to in this session
    private string mostRecentFileNameStub; //the string specifying the .csv file save name for the frame, without the suffix specifying whether it's marker, frame, or trial data.
    private bool dataWrittenToFileFlag = false; // a flag that says whether or not data has already been written to file (for that block or run)

    // Structure matrix setup data loading
    private string subdirectoryWithSetupDataForStructureMatrixComputation; // the string specifying the subdirectory (name) we'll load the 
                                                                           // data needed for structure matrix computation from

    // EMG data streaming
    public GameObject emgDataStreamerObject;
    private StreamAndRecordEmgData emgDataStreamerScript; // communicates with Delsys base station, reads and saves EMG data

    //building functional boundary of stability from the subject's performance
    private uint[] subjectPerformanceExcursionDirections; //which directions the subject went on each trial (the number, not the angle). (see above variables for number to angle mapping).
    private float[] subjectPerformanceExcursionDistanceUnityUnits; //how far the subject went on each trial
    private float[] subjectPerformanceExcursionDistanceViconUnitsMm; //how far the subject went on each trial

    //stimulation status
    private string currentStimulationStatus; //the current stimulation status for this block
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // Mapping Vicon coordinates to Unity coordinates
    private float leftEdgeOfBaseOfSupportInViewportCoords = 0.1f;
    private float rightEdgeOfBaseOfSupportInViewportCoords; //initialized in Start()
    private float backEdgeOfBaseOfSupportInViewportCoords = 0.1f;
    private float frontEdgeOfBaseOfSupportInViewportCoords; //initialized in Start()

    // The edges of the base of support
    private float leftEdgeBaseOfSupportViconXPos;
    private float rightEdgeBaseOfSupportViconXPos;
    private float frontEdgeBaseOfSupportViconYPos;
    private float backEdgeBaseOfSupportViconYPos;
    private float centerOfBaseOfSupportXPosViconFrame;
    private float centerOfBaseOfSupportYPosViconFrame;

    // Axis alignment between Vicon frame +x-axis and "rightwards" on screen. 
    private float rightwardsSign; // +1 if x-axes of Vicon and Unity are aligned, -1 if they are inverted
    private float forwardsSign; // +1 if y-axes of Vicon and Unity are aligned, -1 if they are inverted

    // The distance from the center of base of support to be "at home position", defined 
    // as a percentage of the minimum dimension of the base of support
    private float atHomeMaxDistanceFromCenterOfBaseOfSupportAsFractionOfMinimumDimensionOfBaseOfSupport = 0.03f;
    private float atHomeRadiusUnityCoords;

    // The minimum distance from the center of base of support to start an excursion , defined 
    // as a percentage of the minimum dimension of the base of support
    private float minDistanceFromCenterOfBaseOfSupportToStartExcursionAsFractionOfMinimumDimensionOfBaseOfSupport = 0.06f;
    private float startExcursionRadiusUnityCoords;

    // Tracking the at-home status
    private uint millisecondsRequiredAtHomeToStartATrial = 3000;
    private bool isChestAtHome = false; // keep track of whether or not the player is at home,
                                         // when we're in the waitingForHomePosition state. An initial state of
                                         // false is desirable.
    private Stopwatch timeAtHomeStopwatch = new Stopwatch(); // A stopwatch to monitor time spent in the "home area"
    private Stopwatch delayStartSyncSignalStopwatch = new Stopwatch(); // A stopwatch to add a small delay to sending our photon START sync signal
                                                                       // Seems necessary for Delsys base station.
    private uint millisecondsDelayForStartEmgSyncSignal = 1000;
    private uint millisecondsDelayForStopEmgSyncSignal = 500;
    private bool hasEmgSyncStartSignalBeenSentFlag = false; // A flag that is flipped to true when the EMG sync signal (and, thus, start data stream)
                                                            // was sent to the Delsys base station.

    // States 
    private string currentState;
    private const string setupStateString = "SETUP";
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string waitingForPlayerToMoveHomeStateString = "WAITING_FOR_HOME";
    private const string waitingForExcursionStartStateString = "WAITING_FOR_EXCURSION_START";
    private const string excursionActiveStateString = "EXCURSION_ACTIVE";
    private const string gameOverStateString = "GAME_OVER";


    //constants
    private const float convertDegreesToRadians = (Mathf.PI / 180.0f);

    // Boundary of stability renderer
    public GameObject boundaryOfStabilityRenderer;
    private RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript;

    // Communication with force field robot (Robust)
    public GameObject forceFieldRobotTcpServerObject;
    private CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;
    public bool communicateWithForceFieldRobot;

    // RobUST force field type specifier
    private const string forceFieldIdleModeSpecifier = "I";
    private const string forceFieldAtExcursionBoundary = "B";
    private string currentDesiredForceFieldTypeSpecifier;

    // Coordinating with RobUST (or force field robot, generally)
    public GameObject computeStructureMatrixThisFrameServiceObject;
    private BuildStructureMatricesForBeltsThisFrameScript computeStructureMatrixScript;

    // Using EMGs flag 
    public bool streamingEmgDataFlag; // Whether to run the EMG service (true) or not (false)

    // Synchronize with external hardware (including EMGs)
    public GameObject communicateWithPhotonViaSerialObject;
    private CommunicateWithPhotonViaSerial communicateWithPhotonScript;
    public bool syncingWithExternalHardwareFlag; // Whether or not we're using the System Sync object to sync with EMGs and Vicon nexus
    private float unityFrameTimeAtWhichHardwareSyncSent = 0.0f; // The Time.time of the Update() frame when the hardware sync signal command was sent.

    // The Vicon device data access object (this includes force plates and the analog sync pin)
    public GameObject forcePlateDataAccessObject;
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;

    // Reaching target object
    public GameObject reachingTargetObject;

    //testing only - should be deletable!
    uint testStoreFrameDataCounter = 0;

    // Define the center-of-base-of-support position in Unity frame
    Vector3 unityCenterOfBaseOfSupport = new Vector3(0.0f, 0.0f, 0.0f);



    // Start is called before the first frame update
    void Start()
    {
        fixedUpdateFrequency = 1.0f / Time.fixedDeltaTime;
        Debug.Log("fixed update frequency " + fixedUpdateFrequency);
        numberChestPositionsToAverage = (int)Mathf.Ceil(chestPositionAveragingPeriod * fixedUpdateFrequency);
        Debug.Log("numberChestPositionsToAverage is " + numberChestPositionsToAverage);

        chestPositionsInLastAveragingPeriod = new Vector3[numberChestPositionsToAverage]; //we will need to store as many samples as are taken in the desired averaging period
        isChestInProperRegion = new bool[numberChestPositionsToAverage];
        axesRendererScript = axesRenderer.GetComponent<RenderExcursionAxes>();
        excursionDirectionNames = new string[numberOfExcursionDirections];
        excursionDirectionNames[0] = "Horizontal Right";
        excursionDirectionNames[1] = "Vertical Forward";
        excursionDirectionNames[2] = "Horizontal Left";
        excursionDirectionNames[3] = "Vertical Backward";

        // Initialize hand position averaging window
        leftHandPositionsInLastAveragingPeriod = new Vector3[numberChestPositionsToAverage]; //we will need to store as many samples as are taken in the desired averaging period
        rightHandPositionsInLastAveragingPeriod = new Vector3[numberChestPositionsToAverage]; //we will need to store as many samples as are taken in the desired averaging period
        isLeftHandInProperRegion = new bool[numberChestPositionsToAverage];
        isRightHandInProperRegion = new bool[numberChestPositionsToAverage];

        //marker data and center of mass manager
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        //data saving
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // Communication with force field robot (e.g. RobUST)
        forceFieldRobotTcpServerScript = forceFieldRobotTcpServerObject.GetComponent<CommunicateWithRobustLabviewTcpServer>();

        // Boundary of stability renderer
        boundaryOfStabilityRendererScript = boundaryOfStabilityRenderer.GetComponent<RenderBoundaryOfStabilityScript>();

        // Get reference to Photon-based hardware sync object
        communicateWithPhotonScript = communicateWithPhotonViaSerialObject.GetComponent<CommunicateWithPhotonViaSerial>();

        // Get the script that computes the structure matrix for the current frame (for when using a cable-driven robot)
        computeStructureMatrixScript =
            computeStructureMatrixThisFrameServiceObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

        // Get a reference to the force plate data access script 
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();

        // How long the hand must be in the proper sector to push the target.
        handPositionAveragingPeriod = chestPositionAveragingPeriod;


        // Initialize axes center
        axesCenter = axesRendererScript.getAxesCenterPosition();

        // Get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // If we're syncing with external hardware (typically the EMGs are included)
        if (streamingEmgDataFlag == true)
        {
            // Get a reference to the EMGs
            emgDataStreamerScript = emgDataStreamerObject.GetComponent<StreamAndRecordEmgData>();
        }

        // Set the stimulation status - note, for now we're just filling it in manually. Use a bool box in the subject data object!
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();
        Debug.Log("Before setting file naming, set the current stimulation status string to: " + currentStimulationStatus);

        // Set the header names for the saved-out data CSV headers
        setFrameTrialAndExcursionSummaryDataNaming();

        // We can also tell the structure matrix computation service to load the setup data needed to 
        // compute the structure matrix (if we're using a force belt / cable-driven robot)
        if (communicateWithForceFieldRobot)
        {

            // Tell the structure matrix computation script to load daily setup data needed for structure matrix computation
            computeStructureMatrixScript.loadDailySetupDataForStructureMatrixConstruction(subdirectoryWithSetupDataForStructureMatrixComputation);


            // Flip the flag in the COM manager that tells it to ping the structure matrix service whenever fresh Vicon data is ready.
            centerOfMassManagerScript.SetUsingCableDrivenRobotFlagToTrue();
        }

        // Generate the trial/excursion order and set up the trial
        generateTrialOrder();
        currentExcursionDirection = excursionOrderList[currentTrialIndex];

        // Determine how/where the edges of the base of support will map onto the screen
        rightEdgeOfBaseOfSupportInViewportCoords = 1.0f - leftEdgeOfBaseOfSupportInViewportCoords;
        frontEdgeOfBaseOfSupportInViewportCoords = 1.0f - backEdgeOfBaseOfSupportInViewportCoords;


        // Initialize the arrays that will store the actual subject performance (excursion distance and direction) for each trial
        subjectPerformanceExcursionDirections = new uint[excursionOrderList.Length];
        subjectPerformanceExcursionDistanceUnityUnits = new float[excursionOrderList.Length];
        subjectPerformanceExcursionDistanceViconUnitsMm = new float[excursionOrderList.Length];

        // Hide the excursion indicator until the first excursion is ready to begin
        excursionIndicator.SetActive(false);

        // Set the default force field mode as Idle
        currentDesiredForceFieldTypeSpecifier = forceFieldIdleModeSpecifier;

        // Current state - start in setup state
        currentState = setupStateString;

        // Start the block 
        activeBlock = true;

    }



    // Update is called once per frame
    void Update()
    {
        // If we need to set up the EMG base station (Delsys)
        if (currentState == setupStateString)
        {

            //if the center of mass manager is ready to distribute data, then store relevant frame-data here.
            if (centerOfMassManagerReadyStatus == false) //if the center of mass manager object is not currently ready to serve COM data
            {
                centerOfMassManagerReadyStatus = centerOfMassManagerScript.getCenterOfMassManagerReadyStatus(); // see if it is ready now
            }

            if (centerOfMassManagerReadyStatus)
            {
                // Get the edges of the base of support
                (leftEdgeBaseOfSupportViconXPos, rightEdgeBaseOfSupportViconXPos, frontEdgeBaseOfSupportViconYPos,
                 backEdgeBaseOfSupportViconYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();

                // Determine if there is axis-flipping in the Vicon to Unity mapping (equivalent to a rotation matrix with axes either aligned or flipped)
                rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportViconXPos - leftEdgeBaseOfSupportViconXPos);
                forwardsSign = rightwardsSign; // always equal to rightwards sign, since both frames are right-handed.

                // Compute the center of base of support position
                centerOfBaseOfSupportXPosViconFrame = (leftEdgeBaseOfSupportViconXPos + rightEdgeBaseOfSupportViconXPos) / 2.0f;
                centerOfBaseOfSupportYPosViconFrame = (backEdgeBaseOfSupportViconYPos + frontEdgeBaseOfSupportViconYPos) / 2.0f;

                // Initialize the chest home position
                InitializeHomeChestPosition();

                // Set the radius defining the "at-home" area and the radius defining the minimum distance to start an excursion trial
                defineAtHomeRadiusAndExcursionStartRadius();

                // Load the postural workspace, store the baseline excursion distances, and render the workspace
                boundaryOfStabilityRendererScript.loadBoundaryOfStability(subdirectoryName);
                excursionDistancesPerDirectionViconFrame = boundaryOfStabilityRendererScript.getExcursionDistancesInViconCoordinates();
                boundaryOfStabilityRendererScript.renderBoundaryOfStability();

                // Send a command to Labview with the task-specific info string (subject number, date, time). 
                forceFieldRobotTcpServerScript.SendCommandWithCurrentTaskInfoString(mostRecentFileNameStub);

                // Get the real-world excursion axis angles, defined CCW from the rightwards direction
                excursionDirectionAnglesFromRightwardsCcw = axesRendererScript.GetExcursionDirectionAnglesCcwFromRightwardsViconFrame();

                // If we're syncing with external hardware (EMGs), we should move to a special state 
                // for EMG setup
                if (streamingEmgDataFlag == true)
                {
                    // then move to the waiting for EMG state
                    changeActiveState(waitingForEmgReadyStateString);
                }
                else // If not using EMGs, then move on to normal setup
                {
                    // Send the start sync signal via the Photon
                    if (syncingWithExternalHardwareFlag)
                    {
                        communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

                        // Store the hardware sync signal sent time
                        unityFrameTimeAtWhichHardwareSyncSent = Time.time;
                    }

                    // then proceed by moving to the Waiting For Player Home state
                    changeActiveState(waitingForPlayerToMoveHomeStateString);
                }
            }
        }
        else if (currentState == waitingForEmgReadyStateString)
        {
            Debug.Log("In EMG setup state");
            // If EMG setup is complete and EMG hardware is ready for a start pulse
            emgBaseIsReadyForTriggerStatus = emgDataStreamerScript.IsBaseStationReadyForSyncSignal();
            if (emgBaseIsReadyForTriggerStatus == true && delayStartSyncSignalStopwatch.IsRunning == false)
            {

                // Call a stopwatch to delay the Photon start signal by 1 second (seems like 
                // that is needed by the Delsys base station).
                delayStartSyncSignalStopwatch.Start();
            }
            else if (emgBaseIsReadyForTriggerStatus == true && delayStartSyncSignalStopwatch.IsRunning == true &&
               delayStartSyncSignalStopwatch.ElapsedMilliseconds > millisecondsDelayForStartEmgSyncSignal) // If the EMG base station is ready 
                                                                                                           // AND we have waited a period for it to be ready
                                                                                                           // for the start sync pulse
            {
                // Send the start sync signal via the Photon
                if (syncingWithExternalHardwareFlag)
                {
                    communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();
                }

                // Store the hardware sync signal sent time
                unityFrameTimeAtWhichHardwareSyncSent = Time.time;

                // Stop the stopwatch and reset it to zero
                delayStartSyncSignalStopwatch.Reset();

                // We've accomplished a minimum delay for sending the sync signal, so 
                // switch to the Waiting For Home state
                changeActiveState(waitingForPlayerToMoveHomeStateString);
            }
        }
        // If we're still doing setup
        else if (currentState == waitingForPlayerToMoveHomeStateString ||
        currentState == waitingForExcursionStartStateString ||
        currentState == excursionActiveStateString) //only record frame data if we've left the setup state
        {
            // Record frame-relevant data(e.g.player position, COM position in Vicon space, the Vicon frame,
            // the time, trial and block #, etc.
            // MOVE THIS TO THE FIXEDUPDATE() FCN!
            storeFrameData();

            //Regardless of state, update the stored chest positions and proper region arrays
            float chestAngleInDegrees = updateChestPositionsArray();

            // Regardless of state, also update the stored hand position and proper region arrays
            updateHandPositionsArray();

            switch (currentState)
            {
                case waitingForPlayerToMoveHomeStateString:
                    // monitor whether or not the player is at home
                    monitorIfChestIsInHomeRegion();

                    // If the player has been at home long enough
                    if (timeAtHomeStopwatch.IsRunning && (timeAtHomeStopwatch.ElapsedMilliseconds > millisecondsRequiredAtHomeToStartATrial))
                    {
                        // Then we've been at home long enough. The subject can now start an excursion when ready. 
                        // Move to the Waiting for Excursion Start state
                        changeActiveState(excursionActiveStateString);

                    }
                    break;
                case waitingForExcursionStartStateString:
                    break;
                case excursionActiveStateString:
                    // Update the reaching target position, if needed
                    computeMaxReachExcursionAlongAxisAndUpdateReachTarget();

                    // Monitor for the end of the excursion trial. 
                    // The trial ends when the chest re-enters the "at home" position
                    monitorForEndOfOngoingExcursionTrial(chestAngleInDegrees);
                    break;
            }
        }
        else if (currentState == gameOverStateString)
        {
            // If the stop sync signal has been sent, but we want to wait briefly before writing data to file (using stopwatch)
            if (delayStartSyncSignalStopwatch.IsRunning &&
                delayStartSyncSignalStopwatch.ElapsedMilliseconds > millisecondsDelayForStopEmgSyncSignal)
            {
                // If the data has not been written to file
                if (dataWrittenToFileFlag == false)
                {
                    //build the excursion performance summary data row (it's only one row) and send it to the general data recorder
                    buildAndStoreExcursionPerformanceSummary();

                    //tell the general data recorder object to write all of the stored data to file (marker data, frame data, and task-specific trial data)
                    tellDataRecorderToWriteStoredDataToFile();

                    // Stop the stopwatch and reset to zero
                    delayStartSyncSignalStopwatch.Reset();

                    //now that we have excursion data, tell the functional boundary of stability drawing object to draw the BoS, if the object is active.
                    boundaryOfStabilityRendererScript.loadBoundaryOfStability(subdirectoryName);
                    boundaryOfStabilityRendererScript.renderBoundaryOfStability();

                    // Note that the data has been written to file
                    dataWrittenToFileFlag = true;
                }
            }
        }
    }

    IEnumerator ExampleCoroutine()
    {
        //Print the time of when the function is first called.
        Debug.Log("Started Coroutine at timestamp : " + Time.time);

        //yield on a new YieldInstruction that waits for 5 seconds.
        yield return new WaitForSeconds(5);

        //After we have waited 5 seconds print the time again.
        Debug.Log("Finished Coroutine at timestamp : " + Time.time);
    }



    /*    //keep track of the average position of the player over the last second in FixedUpdate, as this allows us to know how many
        //samples are taken per second (makes filtering easier, at a fixed 50 Hz)
        void FixedUpdate()
        {
            if (currentState == waitingForPlayerToMoveHomeStateString || 
                currentState == waitingForExcursionStartStateString ||
                currentState  == excursionActiveStateString) //only run the script core functions if we've retrieved
                                                  //the on-screen excursion axes angles and are no longer in the setup state
            {

            }
        }*/




    // BEGIN: Vicon <-> Unity mapping functions and other public functions*********************************************************************************

    public override string GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    // The mapping function from Vicon frame to Unity frame. This function is a member of the 
    // parent class of this script, so that other GameObjects can access it. 
    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        // Express the Vicon frame point relative to the base of support center
        Vector3 pointInViconFrameRelativeToBosCenter = pointInViconFrame - centerOfMassManagerScript.GetCenterOfBaseOfSupportPositionViconFrame();

        // Unity uses a left-handed coordinate system. Also, y is "up" in Unity, and z is "up" in Vicon. 
        // Apply these conversions. 
        Vector3 pointInUnityFrame = new Vector3(-pointInViconFrameRelativeToBosCenter.x, pointInViconFrameRelativeToBosCenter.z, -pointInViconFrameRelativeToBosCenter.y);

        // Convert from mm (Vicon) to meters (Unity)
        pointInUnityFrame = pointInUnityFrame / 1000.0f;

        // Shift the point in "Unity frame" to be centered about the unity center of base of support.
        // Note: this point is (0,0,0), but we add the step just in case we want to use a non-zero value.
        pointInUnityFrame = pointInUnityFrame - unityCenterOfBaseOfSupport;

        return pointInUnityFrame;
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // Add the unity center of base of support 
        Vector3 pointInUnityFrameTemp = pointInUnityFrame + unityCenterOfBaseOfSupport;

        // Unity uses a left-handed coordinate system. Also, y is "up" in Unity, and z is "up" in Vicon. 
        // Apply these conversions. 
        Vector3 pointInViconFrame = new Vector3(-pointInUnityFrameTemp.x, pointInUnityFrameTemp.z, -pointInUnityFrameTemp.y);

        // Convert from meters (Unity) to mm (Vicon)
        pointInViconFrame = pointInViconFrame * 1000.0f;

        // Add the center of the base of support in Vicon frame to the computed position
        pointInViconFrame = pointInViconFrame + centerOfMassManagerScript.GetCenterOfBaseOfSupportPositionViconFrame();

        // Return 
        return pointInViconFrame;
    }

    public override Vector3 GetControlPointForRobustForceField()
    {
        if (currentState != setupStateString)
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

    // Return the current desired force field type (only applicable if coordinating Unity with RobUST). 
    // This will be sent to the RobUST Labview script via TCP. 
    // Default value: let the default value be the Idle mode specifier.
    public override string GetCurrentDesiredForceFieldTypeSpecifier()
    {
        return currentDesiredForceFieldTypeSpecifier;
    }

    // END: Vicon <-> Unity mapping functions *********************************************************************************




    // START: other abstract level manager public functions *******************************************************************

    public override bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }

    // END: other abstract level manager public functions *******************************************************************





    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if (newState != currentState)
        {
            Debug.Log("Transitioning states from " + currentState + " to " + newState);
            // call the exit function for the current state. 
            // Note that we never exit the EndGame state.
            switch (currentState)
            {
                case waitingForEmgReadyStateString:
                    exitWaitingForEmgReadyState();
                    break;
                case setupStateString:
                    exitSetupState();
                    break;
                case waitingForPlayerToMoveHomeStateString:
                    exitWaitingForHomeState();
                    break;
                case waitingForExcursionStartStateString:
                    exitWaitingForExcursionStartState();
                    break;
                case excursionActiveStateString:
                    exitExcursionActiveState();
                    break;
                default:
                    Debug.LogWarning("Current state is invalid.");
                    break;
            }

            switch (newState)
            {
                case waitingForEmgReadyStateString:
                    enterWaitingForEmgReadyState();
                    break;
                case setupStateString:
                    enterSetupState();
                    break;
                case waitingForPlayerToMoveHomeStateString:
                    enterWaitingForHomeState();
                    break;
                case waitingForExcursionStartStateString:
                    enterWaitingForExcursionStartState();
                    break;
                case excursionActiveStateString:
                    enterExcursionActiveState();
                    break;
                case gameOverStateString:
                    enterGameOverState();
                    break;
                default:
                    Debug.LogWarning("New state is invalid.");
                    break;
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

    private void enterSetupState()
    {
        //change the current state to the Setup state
        currentState = setupStateString;
    }

    private void exitSetupState()
    {
        // do nothing for now
    }

    private void enterWaitingForHomeState()
    {

        //  Ensure that the indicator GameObject is hidden while the subject is just holding at the home position
        excursionIndicator.SetActive(false);

        Debug.Log("Changing to Waiting for Home state");

        // Reset the variables measuring at-home state and time
        isChestAtHome = false;
        timeAtHomeStopwatch.Reset();

        //change the current state to the Waiting For Home state
        currentState = waitingForPlayerToMoveHomeStateString;

        //set the center circle color to the color indicating the player should move there
        //circleCenter.GetComponent<Renderer>().material.color = Color.red;
    }


    private void exitWaitingForHomeState()
    {
        //set the center circle color to its default
        //circleCenter.GetComponent<Renderer>().material.color = Color.blue;

        //reset and stop the stop watch
        timeAtHomeStopwatch.Reset();
    }

    private void enterWaitingForExcursionStartState()
    {
        Debug.Log("Changing to Waiting For Excursion Start state");

        // Initialize or reset variables that track trial-specific quantities
        currentTrialStartTime = -1.0f; //reset the trial start time, since the new trial does not officially "start" until the player moves into the correct region.
        currentExcursionDirection = excursionOrderList[currentTrialIndex]; //update the current excursion direction

        // Ensure that the excursion indicator is visible

        resetIndicatorForCurrentExcursionDirection(); //put the indicator in the right position and orientation

        // Reset the reaching push target 
        ResetReachingPushTargetForThisTrial();


        //change the current state to the Waiting For Excursion state string
        currentState = waitingForExcursionStartStateString;


    }

    private void exitWaitingForExcursionStartState()
    {

    }


    private void enterExcursionActiveState()
    {
        Debug.Log("Changing to Waiting For Excursion Active state");

        //change the current state to the Waiting For Excursion state string
        currentState = excursionActiveStateString;


    }


    private void exitExcursionActiveState()
    {

    }


    private void enterGameOverState()
    {
        Debug.Log("Changing to Game Over state");

        // Tell the external hardware that the recording is over, if we're using external hardware
        if (syncingWithExternalHardwareFlag)
        {
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();
        }

        delayStartSyncSignalStopwatch.Restart();


        //change the current state to the Game Over state
        currentState = gameOverStateString;
    }

    // END: State machine state-transitioning functions *********************************************************************************



    // START:  Getter and Setter functions ***********************************************************************

    /*    // A setter function. Called by the Axes Renderer script once the axes have been drawn on-screen. 
        // Note: This means that this function is only called after the COM manager is "ready", as drawing the axes
        // on screen requires knowledge of the subject's base of support dimensions.
        public void setOnScreenExcursionAxesAnglesInDegrees(float[] subjectSpecificOnScreenExcursionAnglesCounterclockwiseFromXAxisInDegrees)
        {
            excursionDirectionAnglesFromRightwardsCcw = subjectSpecificOnScreenExcursionAnglesCounterclockwiseFromXAxisInDegrees;

            //indicate that we have the on-screen axis angles, so we can detect if the player is in the correct
            //region and animate the 1-second moving average indicator appropriately.
            retrievedSubjectSpecificOnScreenExcursionAnglesFlag = true;

            //now that the excursion angles on-screen are known,
            //reset the indicator to be in the appropriate position for the first trial 
            //resetIndicatorForCurrentExcursionDirection(); //put the indicator in the right position and orientation

        }*/

    // END:  Getter and Setter functions ***********************************************************************






    private void generateTrialOrder()
    {
        //note, let 0 indicate right, 1 indicate NE, 2 indicate forward, 3 indicate NW, 4 indicate left,
        // 5 indicate SW, 6 indicate backward, and 7 indicate SE.
        uint numberTrials = excursionsPerAxis * numberOfExcursionDirections;
        excursionOrderList = new uint[excursionsPerAxis * numberOfExcursionDirections];
        //fill using a for loop
        for (int trialIndex = 0; trialIndex < numberTrials; trialIndex++)
        {
            uint currentAxisValue = (uint)Mathf.Floor((float)trialIndex / (float)excursionsPerAxis);
            excursionOrderList[trialIndex] = currentAxisValue;
        }
        //shuffle the order
        System.Random random = new System.Random();
        excursionOrderList = excursionOrderList.OrderBy(x => random.Next()).ToArray();
    }



    private void defineAtHomeRadiusAndExcursionStartRadius()
    {
        // Get the width and height of the base of support in Unity coordinates. 
        // Use these values to determine the limits for "being at home position" and starting each excursion trial.
        float leftEdgeOfBaseOfSupportUnityCoords = sceneCamera.ViewportToWorldPoint(new Vector3(leftEdgeOfBaseOfSupportInViewportCoords,
            0.0f, 0.0f)).x;
        float rightEdgeOfBaseOfSupportUnityCoords = sceneCamera.ViewportToWorldPoint(new Vector3(rightEdgeOfBaseOfSupportInViewportCoords,
            0.0f, 0.0f)).x;
        float frontEdgeOfBaseOfSupportUnityCoords = sceneCamera.ViewportToWorldPoint(new Vector3(0.0f,
            frontEdgeOfBaseOfSupportInViewportCoords, 0.0f)).y;
        float backEdgeOfBaseOfSupportUnityCoords = sceneCamera.ViewportToWorldPoint(new Vector3(0.0f,
            backEdgeOfBaseOfSupportInViewportCoords, 0.0f)).y;

        // Width and height of base of support in Unity frame 
        float widthOfBaseOfSupportUnityCoords = Mathf.Abs(rightEdgeOfBaseOfSupportUnityCoords - leftEdgeOfBaseOfSupportUnityCoords);
        float heightOfBaseOfSupportUnityCoords = Mathf.Abs(frontEdgeOfBaseOfSupportUnityCoords - backEdgeOfBaseOfSupportUnityCoords);

        Debug.Log("Width of base of support in Unity units = " + widthOfBaseOfSupportUnityCoords);
        Debug.Log("Height of base of support in Unity units = " + heightOfBaseOfSupportUnityCoords);

        // Smallest dimension of base of support 
        float smallestDimensionOfBaseOfSupportInUnityCoordinates;
        if (widthOfBaseOfSupportUnityCoords < heightOfBaseOfSupportUnityCoords)
        {
            smallestDimensionOfBaseOfSupportInUnityCoordinates = widthOfBaseOfSupportUnityCoords;
        }
        else
        {
            smallestDimensionOfBaseOfSupportInUnityCoordinates = heightOfBaseOfSupportUnityCoords;
        }

        // At-home radius, i.e. max distance
        atHomeRadiusUnityCoords = atHomeMaxDistanceFromCenterOfBaseOfSupportAsFractionOfMinimumDimensionOfBaseOfSupport
            * smallestDimensionOfBaseOfSupportInUnityCoordinates;

        // Start-excursion radius
        startExcursionRadiusUnityCoords = minDistanceFromCenterOfBaseOfSupportToStartExcursionAsFractionOfMinimumDimensionOfBaseOfSupport
            * smallestDimensionOfBaseOfSupportInUnityCoordinates;
    }

    private void InitializeHomeChestPosition()
    {
        // Build the home chest position in Vicon frame
        Vector3 homeChestPositionViconFrame = new Vector3(centerOfBaseOfSupportXPosViconFrame, centerOfBaseOfSupportYPosViconFrame,
            centerOfMassManagerScript.GetCenterOfTrunkBeltPositionInViconFrame().z);

        // Transform the Vicon frame position to Unity frame and store
        homeChestPositionUnityFrame = mapPointFromViconFrameToUnityFrame(homeChestPositionViconFrame);
    }



    private void resetIndicatorForCurrentExcursionDirection()
    {
        //  Ensure that the indicator GameObject is active
        excursionIndicator.SetActive(true);

        //get the current excursion direction angle, in radians
        float excursionAngleInRadians = excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection] * convertDegreesToRadians;

        //set the indicator position to be at a fixed distance from the center, along the excursion direction
        excursionIndicator.transform.position = new Vector3(axesCenter.x + distanceFromCenterToStartTrial * Mathf.Cos(excursionAngleInRadians),
                                                axesCenter.y + distanceFromCenterToStartTrial * Mathf.Sin(excursionAngleInRadians),
                                                axesCenter.z);

        float indicatorCurrentRotationInDegrees = excursionIndicator.transform.eulerAngles.z;
        //rotate the indicator back to a neutral start position (rotation about z = 0).
        Debug.Log("Indicator z-rotation before reset: " + excursionIndicator.transform.eulerAngles.z);
        excursionIndicator.transform.Rotate(0.0f, 0.0f, -indicatorCurrentRotationInDegrees, Space.World);
        Debug.Log("Indicator z-rotation after reset: " + excursionIndicator.transform.eulerAngles.z);

        //Then rotate the indicator to the desired orientation
        float desiredRotationAngle = (90.0f + excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection]) % 360.0f;
        excursionIndicator.transform.Rotate(0.0f, 0.0f, desiredRotationAngle, Space.World);
    }


    // Based on the trial direction, compute the end-movement target region position in Vicon frame
    private void ResetReachingPushTargetForThisTrial()
    {
        //get the current excursion direction angle, in radians
        float excursionAngleInRadians = excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection] * convertDegreesToRadians;

        // Get the baseline excursion distance for this excursion direction (in Vicon units, i.e. real units)
        float baselineMaxExcursionThisTrialDirection = excursionDistancesPerDirectionViconFrame[currentExcursionDirection];

        // Compute the Vicon location of the end-movement target region
        float reachingTargetInitialPosX = centerOfBaseOfSupportXPosViconFrame + rightwardsSign * baselineMaxExcursionThisTrialDirection * Mathf.Cos(excursionAngleInRadians);
        float reachingTargetInitialPosY = centerOfBaseOfSupportYPosViconFrame + rightwardsSign * baselineMaxExcursionThisTrialDirection * Mathf.Sin(excursionAngleInRadians);
        Vector3 reachingTargetInitialPosViconFrame = new Vector3(reachingTargetInitialPosX, reachingTargetInitialPosY, 0.0f);

        // Convert the end-movement position frame from Vicon to Unity frame
        Vector3 reachingTargetInitialPosUnityFrame = mapPointFromViconFrameToUnityFrame(reachingTargetInitialPosViconFrame);

        // Set end-movement indicator position for this trial 
        reachingTargetObject.transform.position = reachingTargetInitialPosUnityFrame;

        // Set the maximum reaching excursion this trial in Unity frame equal to the distance from chest to reaching target in Unity frame
        maximumExcursionAlongAxisThisTrial = Vector3.Distance(reachingTargetObject.transform.position, homeChestPositionUnityFrame); //reset the maximum excursion along the axis to zero


    }



    private float updateChestPositionsArray()
    {

        //get chest position information, relative to center of axes
        float chestYRelativeToCenter = (chestObject.transform.position.y - homeChestPositionUnityFrame.y);
        float chestXRelativeToCenter = (chestObject.transform.position.x - homeChestPositionUnityFrame.x);
        float currentChestAngle = Mathf.Atan2(chestYRelativeToCenter, chestXRelativeToCenter) * (180.0f / Mathf.PI);
        Vector3 chestPositionRelativeToChestHome = new Vector3(chestXRelativeToCenter, chestYRelativeToCenter, chestObject.transform.position.z - homeChestPositionUnityFrame.z);

        //first, just put the chest position into the array of chest positions
        Vector3[] tempChestPositionsArray = new Vector3[numberChestPositionsToAverage];
        Array.Copy(chestPositionsInLastAveragingPeriod, 0, tempChestPositionsArray, 1, numberChestPositionsToAverage - 1);
        tempChestPositionsArray[0] = chestPositionRelativeToChestHome; //the most recent chest position is in element 0 of the array
        chestPositionsInLastAveragingPeriod = tempChestPositionsArray;

        //convert the chest angle to range from 0 to 359.999 degrees.
        float degreesInCircle = 360.0f;
        if (currentChestAngle < 0)
        {
            currentChestAngle = degreesInCircle + currentChestAngle; //360 is degrees in a circle
        }

        //see if the chest is in the right angle "slice", close enough to the target axis
        bool chestCloseToAxis = false;
        float currentExcursionDirectionAngle = excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection];

        // If the chest is within a maximum angular error from the target axis
        // Note, the first conditional statement is important for all excursion angles, while the second is needed for the
        // rightwards direction, which is both zero and 360.0f.
        if ((Mathf.Abs(currentExcursionDirectionAngle - currentChestAngle) <= maximumAngularDeviationFromAxisInDegrees)
            || (Mathf.Abs((currentExcursionDirectionAngle + 360.0f) - currentChestAngle) <= maximumAngularDeviationFromAxisInDegrees))
        {
            // Then the chest is close enough to the axis to move the indicator
            chestCloseToAxis = true;
        }
        else
        {
            chestCloseToAxis = false;
        }

        //put the boolean value, whether or not chest is near axis, into the array
        bool[] tempChestInProperRegionArray = new bool[numberChestPositionsToAverage];
        Array.Copy(isChestInProperRegion, 0, tempChestInProperRegionArray, 1, numberChestPositionsToAverage - 1);
        tempChestInProperRegionArray[0] = chestCloseToAxis; //the most recent boolean indicating whether the chest position is close enough to the axis
        isChestInProperRegion = tempChestInProperRegionArray;

        return currentChestAngle;
    }

    private void updateHandPositionsArray()
    {
        // LEFT HAND *****************************************************************************
        //get left hand position information, relative to center of axes
        float leftHandYRelativeToCenter = (leftHand.transform.position.y - homeChestPositionUnityFrame.y);
        float leftHandXRelativeToCenter = (leftHand.transform.position.x - homeChestPositionUnityFrame.x);
        float currentLeftHandAngle = Mathf.Atan2(leftHandYRelativeToCenter, leftHandXRelativeToCenter) * (180.0f / Mathf.PI);
        Vector3 leftHandPositionRelativeToChestHome = new Vector3(leftHandXRelativeToCenter, leftHandYRelativeToCenter, leftHand.transform.position.z);

        //first, just put the chest position into the array of chest positions
        Vector3[] tempLeftHandPositionsArray = new Vector3[numberChestPositionsToAverage];
        Array.Copy(leftHandPositionsInLastAveragingPeriod, 0, tempLeftHandPositionsArray, 1, numberChestPositionsToAverage - 1);
        tempLeftHandPositionsArray[0] = leftHandPositionRelativeToChestHome; //the most recent chest position is in element 0 of the array
        leftHandPositionsInLastAveragingPeriod = tempLeftHandPositionsArray;

        // Now pass the current left hand angle to the function that computes if the hands are in the correct region
        bool leftHandInProperReachingRegion = DetermineIfPointIsInCorrectRegionAndStoreFlag(currentLeftHandAngle);

        //put the boolean value, whether or not the left hand is near axis, into the array
        bool[] tempLeftHandInProperRegionArray = new bool[numberChestPositionsToAverage];
        Array.Copy(isLeftHandInProperRegion, 0, tempLeftHandInProperRegionArray, 1, numberChestPositionsToAverage - 1);
        tempLeftHandInProperRegionArray[0] = leftHandInProperReachingRegion; //the most recent boolean indicating whether the chest position is close enough to the axis
        isLeftHandInProperRegion = tempLeftHandInProperRegionArray;

        // RIGHT HAND *****************************************************************************
        //get right hand position information, relative to center of axes
        float rightHandYRelativeToCenter = (rightHand.transform.position.y - homeChestPositionUnityFrame.y);
        float rightHandXRelativeToCenter = (rightHand.transform.position.x - homeChestPositionUnityFrame.x);
        float currentRightHandAngle = Mathf.Atan2(rightHandYRelativeToCenter, rightHandXRelativeToCenter) * (180.0f / Mathf.PI);
        Vector3 rightHandPositionRelativeToChestHome = new Vector3(rightHandXRelativeToCenter, rightHandYRelativeToCenter, rightHand.transform.position.z);

        //first, just put the chest position into the array of chest positions
        Vector3[] tempRightHandPositionsArray = new Vector3[numberChestPositionsToAverage];
        Array.Copy(rightHandPositionsInLastAveragingPeriod, 0, tempRightHandPositionsArray, 1, numberChestPositionsToAverage - 1);
        tempRightHandPositionsArray[0] = rightHandPositionRelativeToChestHome; //the most recent chest position is in element 0 of the array
        rightHandPositionsInLastAveragingPeriod = tempRightHandPositionsArray;

        // Now pass the current right hand angle to the function that computes if the hands are in the correct region
        bool rightHandInProperReachingRegion = DetermineIfPointIsInCorrectRegionAndStoreFlag(currentRightHandAngle);

        //put the boolean value, whether or not the right hand is near axis, into the array
        bool[] tempRightHandInProperRegionArray = new bool[numberChestPositionsToAverage];
        Array.Copy(isRightHandInProperRegion, 0, tempRightHandInProperRegionArray, 1, numberChestPositionsToAverage - 1);
        tempRightHandInProperRegionArray[0] = rightHandInProperReachingRegion; //the most recent boolean indicating whether the chest position is close enough to the axis
        isRightHandInProperRegion = tempRightHandInProperRegionArray;
    }

    private bool DetermineIfPointIsInCorrectRegionAndStoreFlag(float currentPointAngleRelativeToChestCenter)
    {
        //convert the chest angle to range from 0 to 359.999 degrees.
        float degreesInCircle = 360.0f;
        if (currentPointAngleRelativeToChestCenter < 0)
        {
            currentPointAngleRelativeToChestCenter = degreesInCircle + currentPointAngleRelativeToChestCenter; //360 is degrees in a circle
        }

        //see if the point is in the right angle "slice", close enough to the target axis
        bool pointCloseToAxis = false;
        float currentExcursionDirectionAngle = excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection];

        // If the chest is within a maximum angular error from the target axis
        // Note, the first conditional statement is important for all excursion angles, while the second is needed for the
        // rightwards direction, which is both zero and 360.0f.
        if ((Mathf.Abs(currentExcursionDirectionAngle - currentPointAngleRelativeToChestCenter) <= maximumAngularDeviationFromAxisInDegrees)
            || (Mathf.Abs((currentExcursionDirectionAngle + 360.0f) - currentPointAngleRelativeToChestCenter) <= maximumAngularDeviationFromAxisInDegrees))
        {
            // Then the chest is close enough to the axis to move the indicator
            pointCloseToAxis = true;
        }
        else
        {
            pointCloseToAxis = false;
        }

        return pointCloseToAxis;
    }



    private void monitorIfChestIsInHomeRegion()
    {
        (bool isChestAtHomeUpdated, _) = IsChestInHomeRegion();

        // If the state of the player being at home has changed (entered home, left home), 
        // change the stopwatch status as needed
        if (isChestAtHomeUpdated != isChestAtHome)
        {
            //if the player just entered home, i.e. is now within the central circle/"home area"
            if (isChestAtHomeUpdated)
            {
                Debug.Log("Player has entered home, starting stopwatch");
                //then start the stopwatch keeping track of continuous time spent in the home area
                timeAtHomeStopwatch.Restart(); //reset elapsed time to zero and restart
            }
            else //if the player just left home, i.e. is now outside the central circle/"home area"
            {
                Debug.Log("Player has left home, resetting stopwatch");
                //then reset and stop the stopwatch keeping track of continous time spent in the home area
                timeAtHomeStopwatch.Reset();
            }
        }

        //update the instance variable keeping track of if the player is at home
        isChestAtHome = isChestAtHomeUpdated;
    }



    private (bool, bool) IsChestInHomeRegion()
    {
        // Get distance of chest from center in Unity frame
        float chestDistanceFromHomePositionUnityFrame = Vector3.Distance(chestObject.transform.position,
            homeChestPositionUnityFrame);

        // See if the player is at home based on distance from the center in the Unity frame (have they left the home circle?)
        bool isChestCurrentlyAtHome = (chestDistanceFromHomePositionUnityFrame < (atHomeRadiusUnityCoords));

        //Debug.Log("Player is " + playerDistanceFromCircleCenter + " Unity units from center. Home is at most " + atHomeRadiusUnityCoords.ToString("F3") + " units from center.");

        // If the player is no longer at home but just was, then they have just left home
        bool hasChestJustLeftHome = ((isChestCurrentlyAtHome == false) && (isChestAtHome == true));

        return (isChestCurrentlyAtHome, hasChestJustLeftHome);
    }


    private void monitorForEndOfOngoingExcursionTrial(float chestAngleInDegrees)
    {
        Vector3 currentChestPositionRelativeToCenter = chestPositionsInLastAveragingPeriod[0];
        Vector3 averageChestPosition = getAveragePositionOfPointInWindow(chestPositionsInLastAveragingPeriod);
        float currentChestDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(currentChestPositionRelativeToCenter.y, 2.0f) +
            Mathf.Pow(currentChestPositionRelativeToCenter.x, 2.0f));
        float averageChestDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(averageChestPosition.y, 2.0f) + Mathf.Pow(averageChestPosition.x, 2.0f) + Mathf.Pow(averageChestPosition.z, 2.0f));

        if (averageChestDistanceFromCenter <= atHomeRadiusUnityCoords) //if the player has returned close enough to the center
        {
            //end the current trial
            inTrialFlag = false;

            //mark the time at which this trial ended
            currentTrialEndTime = Time.time;

            //store the trial summary data before changing any variables
            storeTrialData();

            //if there are more trials in the block 
            if (currentTrialIndex < (excursionOrderList.Length - 1))
            {
                //then increment the trial index
                currentTrialIndex = currentTrialIndex + 1; //increment to the next trial

                // Since we're doing another trial, the next state is the Waiting for Home  state, 
                // since we'd like the subject to wait in the center position for some minimum time before starting 
                // the next excursion
                changeActiveState(waitingForPlayerToMoveHomeStateString);

            }
            else //if we have completed all trials 
            {

                // note that we're no longer in an active block (until and if we load another block)
                activeBlock = false;

                // For now, since we've only programmed the option for a single block, move to the game over state
                changeActiveState(gameOverStateString);

                // Other option: end the block, display an end-of-block message, reset for the next block.

            }
        }
    }




    private void computeMaxReachExcursionAlongAxisAndUpdateReachTarget()
    {
        //Would want to store/save this information!
        // LEFT HAND ********************************************
        // Has the left hand moved far enough to push the reaching target

        // Compute if the left hand is contacting the reaching target  (within the reaching target radius from the reaching target center)
        bool leftHandContactingReachingTarget = true;
        if (isLeftHandInProperRegion.All(inRegionBool => (inRegionBool == true)) && leftHandContactingReachingTarget) //if the player has been in the proper region over the entire last averaging period
        {
            Vector3 leftHandAveragePosInWindowUnityFrame = getAveragePositionOfPointInWindow(leftHandPositionsInLastAveragingPeriod);
            float currentExcursionDirectionAngle = excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection];
            float currentExcursionDirectionAngleRadians = currentExcursionDirectionAngle * (Mathf.PI / 180.0f);
            float averageLeftHandAngle = Mathf.Atan2(leftHandAveragePosInWindowUnityFrame.y, leftHandAveragePosInWindowUnityFrame.x) * (180.0f / Mathf.PI);
            float averageLeftHandDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(leftHandAveragePosInWindowUnityFrame.y, 2.0f) + Mathf.Pow(leftHandAveragePosInWindowUnityFrame.x, 2.0f));
            //the average distance along the axis is the average distance from the center projected onto the axis
            float averageLeftHandDistanceAlongAxis = Mathf.Cos(Mathf.Abs((currentExcursionDirectionAngle - averageLeftHandAngle) * (Mathf.PI / 180.0f))) * averageLeftHandDistanceFromCenter;
            //compute and update the actual indicator position
            if (averageLeftHandDistanceAlongAxis > maximumExcursionAlongAxisThisTrial)
            {
                maximumExcursionAlongAxisThisTrial = averageLeftHandDistanceAlongAxis;
            }
            Vector3 reachingTargetPosition = new Vector3(maximumExcursionAlongAxisThisTrial * Mathf.Cos(currentExcursionDirectionAngleRadians), maximumExcursionAlongAxisThisTrial * Mathf.Sin(currentExcursionDirectionAngleRadians), reachingTargetObject.transform.position.z);
            reachingTargetObject.transform.position = reachingTargetPosition;
        }

        // Compute if the right hand is contacting the reaching target  (within the reaching target radius from the reaching target center)
        bool rightHandContactingReachingTarget = true;
        if (isRightHandInProperRegion.All(inRegionBool => (inRegionBool == true)) && rightHandContactingReachingTarget) //if the player has been in the proper region over the entire last averaging period
        {
            Vector3 rightHandAveragePosInWindowUnityFrame = getAveragePositionOfPointInWindow(rightHandPositionsInLastAveragingPeriod);
            float currentExcursionDirectionAngle = excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection];
            float currentExcursionDirectionAngleRadians = currentExcursionDirectionAngle * (Mathf.PI / 180.0f);
            float averageRightHandAngle = Mathf.Atan2(rightHandAveragePosInWindowUnityFrame.y, rightHandAveragePosInWindowUnityFrame.x) * (180.0f / Mathf.PI);
            float averageRightHandDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(rightHandAveragePosInWindowUnityFrame.y, 2.0f) + Mathf.Pow(rightHandAveragePosInWindowUnityFrame.x, 2.0f));
            //the average distance along the axis is the average distance from the center projected onto the axis
            float averageRightHandDistanceAlongAxis = Mathf.Cos(Mathf.Abs((currentExcursionDirectionAngle - averageRightHandAngle) * (Mathf.PI / 180.0f))) * averageRightHandDistanceFromCenter;
            //compute and update the actual indicator position
            if (averageRightHandDistanceAlongAxis > maximumExcursionAlongAxisThisTrial)
            {
                maximumExcursionAlongAxisThisTrial = averageRightHandDistanceAlongAxis;
            }
            Vector3 reachingTargetPosition = new Vector3(homeChestPositionUnityFrame.x + maximumExcursionAlongAxisThisTrial * Mathf.Cos(currentExcursionDirectionAngleRadians),
                reachingTargetObject.transform.position.y,
                homeChestPositionUnityFrame.z + maximumExcursionAlongAxisThisTrial * Mathf.Sin(currentExcursionDirectionAngleRadians));
            reachingTargetObject.transform.position = reachingTargetPosition;
        }
    }



    private Vector3 GetAveragePositionOfChestInWindow()
    {
        float chestXPosAverage = 0.0f;
        float chestYPosAverage = 0.0f;
        float chestZPosAverage = 0.0f;
        for (int index = 0; index < numberChestPositionsToAverage; index++)
        {
            chestXPosAverage += chestPositionsInLastAveragingPeriod[index].x;
            chestYPosAverage += chestPositionsInLastAveragingPeriod[index].y;
            chestZPosAverage += chestPositionsInLastAveragingPeriod[index].z;
        }

        //take average by dividing by number of samples
        chestXPosAverage = chestXPosAverage / (float)numberChestPositionsToAverage;
        chestYPosAverage = chestYPosAverage / (float)numberChestPositionsToAverage;
        chestZPosAverage = chestZPosAverage / (float)numberChestPositionsToAverage;

        Vector3 chestAveragePosition = new Vector3(chestXPosAverage, chestYPosAverage, chestZPosAverage);
        return chestAveragePosition;

    }

    private Vector3 getAveragePositionOfPointInWindow(Vector3[] positionsInAveragingPeriod)
    {
        float chestXPosAverage = 0.0f;
        float chestYPosAverage = 0.0f;
        float chestZPosAverage = 0.0f;
        for (int index = 0; index < numberChestPositionsToAverage; index++)
        {
            chestXPosAverage += positionsInAveragingPeriod[index].x;
            chestYPosAverage += positionsInAveragingPeriod[index].y;
            chestZPosAverage += positionsInAveragingPeriod[index].z;
        }

        //take average by dividing by number of samples
        chestXPosAverage = chestXPosAverage / (float)numberChestPositionsToAverage;
        chestYPosAverage = chestYPosAverage / (float)numberChestPositionsToAverage;
        chestZPosAverage = chestZPosAverage / (float)numberChestPositionsToAverage;

        Vector3 chestAveragePosition = new Vector3(chestXPosAverage, chestYPosAverage, chestZPosAverage);
        return chestAveragePosition;

    }


    private void setFrameTrialAndExcursionSummaryDataNaming()
    {
        //1.) Frame data naming
        // A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE", "EMG_SYNC_SIGNAL_SENT_FLAG", "COM_POS_X","COM_POS_Y", "COM_POS_Z", "IS_COM_POS_FRESH_FLAG", "LEFT_BASEOFSUPPORT_VICON_POS_X",
            "RIGHT_BASEOFSUPPORT_VICON_POS_X", "FRONT_BASEOFSUPPORT_VICON_POS_Y", "BACK_BASEOFSUPPORT_VICON_POS_Y",
            "MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER", "TRIAL_NUMBER", "PLAYER_POS_X", "PLAYER_POS_Y", "PLAYER_POS_VICON_FRAME_X",
            "PLAYER_POS_VICON_FRAME_Y", "TIME_AT_HOME_STOPWATCH_TIME_MS", "CURRENT_STATE", "EXCURSION_DIRECTION",
            "EXCURSION_DIRECTION_ANGLE_FROM_X", "EXCURSION_INDICATOR_POS_X",
            "EXCURSION_INDICATOR_POS_Y", "EXCURSION_INDICATOR_VICON_POS_X", "EXCURSION_INDICATOR_VICON_POS_Y", "PLAYER_IN_PROPER_REGION_FLAG",
            "TRIAL_HAS_STARTED_FLAG"};

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).
        string timeString = localDate.ToString("t");
        timeString = timeString.Replace(" ", "_");
        timeString = timeString.Replace(":", "_");
        string delimiter = "_";
        string dateAndTimeString = dateString + delimiter + timeString; //concatenate date ("d") and time ("t")
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "Excursion" + "/" + dateString + "/";

        //set the subdirectory name for all desired data save-outs (will go inside the CSV folder in Assets)
        subdirectoryName = subdirectoryString; //store as an instance variable so that it can be used for the marker and trial data
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvExcursionPerformanceSummarySubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvEmgDataSubdirectoryName(subdirectoryString);

        //set the frame data file name
        string subjectSpecificInfoString = subjectSpecificDataScript.getSubjectSummaryStringForFileNaming();
        string fileNameStub = "Excursion" + delimiter + subjectSpecificInfoString + delimiter + currentStimulationStatus + delimiter + dateAndTimeString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);


        //2.) Trial data naming
        // A string array with all of the header names
        string[] csvTrialDataHeaderNames = new string[]{"TRIAL_NUMBER", "STIMULATION_STATUS", "EXCURSION_DIRECTION", "EXCURSION_DIRECTION_ANGLE_FROM_X",
            "EXCURSION_INDICATOR_POS_X", "EXCURSION_INDICATOR_POS_Y", "EXCURSION_INDICATOR_VICON_POS_X", "EXCURSION_INDICATOR_VICON_POS_Y",
        "MAX_EXCURSION_ALONG_DIRECTION_UNITY_UNITS", "MAX_EXCURSION_ALONG_DIRECTION_VICON_UNITS_MM", "LEFT_BASEOFSUPPORT_VICON_POS_X",
            "RIGHT_BASE_OF_SUPPORT_VICON_POS_X", "FRONT_BASEOFSUPPORT_VICON_POS_Y", "BACK_BACKOFSUPPORT_VICON_POS_Y",
        "CENTER_OF_BASEOFSUPPORT_X", "CENTER_OF_BASEOFSUPPORT_Y", "AT_HOME_RADIUS_UNITY_UNITS", "START_EXCURSION_RADIUS_UNITY_UNITS",
        "TRIAL_DURATION_FROM_FIRST_MOVING_INDICATOR_SECS"};

        //tell the data recorder what the trial data CSV headers will be
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvTrialDataHeaderNames);

        //also set the task-specific trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);


        //3.) Set the excursion performance summary header names
        string[] csvExcursionPerformanceSummaryHeaderNames = new string[] {"DIR_0_MAX_EXCURSION_VICON_UNITS_MM", "DIR_1_MAX_EXCURSION_VICON_UNITS_MM", "DIR_2_MAX_EXCURSION_VICON_UNITS_MM",
        "DIR_3_MAX_EXCURSION_VICON_UNITS_MM", "DIR_4_MAX_EXCURSION_VICON_UNITS_MM", "DIR_5_MAX_EXCURSION_VICON_UNITS_MM", "DIR_6_MAX_EXCURSION_VICON_UNITS_MM", "DIR_7_MAX_EXCURSION_VICON_UNITS_MM"};

        //tell the data recorder what the excursion performance summary header names are
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryRowHeaderNames(csvExcursionPerformanceSummaryHeaderNames);

        //also set the excursion performance summary  file name
        string fileNameExcursionPerformanceSummary = "Excursion_Performance_Summary" + delimiter + currentStimulationStatus + ".csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(fileNameExcursionPerformanceSummary);
        // If the excursion performance summary file already exists
        bool excursionPerformanceFileAlreadyExists = generalDataRecorderScript.DoesFileAlreadyExist(subdirectoryString, fileNameExcursionPerformanceSummary);
        Debug.Log("Excursion performance file already exists for today - flag status: " + excursionPerformanceFileAlreadyExists);
        if (excursionPerformanceFileAlreadyExists)
        {
            Debug.Log("Excursion performance summary already existed for this date. Writing to an alternate file name NOT used for limits.");
            // Then we do NOT overwrite it. We'd like to keep only one daily excursion performance summary file. 
            // Instead, use an alternate file name
            fileNameExcursionPerformanceSummary = fileNameStub + "_Excursion_Performance_Summary" + delimiter + currentStimulationStatus + ".csv"; //the final file name. Add any block-specific info!
            generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(fileNameExcursionPerformanceSummary);
        }

        // 4.) Set the EMG file name (even if we're not using it, a quick operation)
        string fileNameEmgData = fileNameStub + "_Emg_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvEmgDataFileName(fileNameEmgData);

        // 5.) We should also be able to reconstruct the directory containing the setup data for computing the structure matrix
        subdirectoryWithSetupDataForStructureMatrixComputation = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "StructureMatrixData" + "/" + dateString + "/";

    }


    //Note, this function is called from Update(), not FixedUpdate(), and thus will record at a higher frequency than
    //FixedUpdate() most of the time.
    private void storeFrameData()
    {
        //the list that will store the data
        List<float> frameDataToStore = new List<float>();

        //the header names for all of the data we will store are specifed in Start()

        //get the time called at the beginning of this frame (this call to Update())
        frameDataToStore.Add(Time.time);

        // Get the time the hardware sync was sent to the EMGs
        frameDataToStore.Add(unityFrameTimeAtWhichHardwareSyncSent);

        // The analog sync pin voltage (high = EMG streaming, low = EMG stopped)
        float analogSyncPinVoltage = scriptToRetrieveForcePlateData.GetMostRecentSyncPinVoltageValue();
        frameDataToStore.Add(analogSyncPinVoltage);

        // Get the boolean representing whether or not the EMG sync signal has been sent by the Photon
        frameDataToStore.Add(Convert.ToSingle(hasEmgSyncStartSignalBeenSentFlag));


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



        // Get edges of base of support
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
            float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();
        frameDataToStore.Add(leftEdgeBaseOfSupportXPos);
        frameDataToStore.Add(rightEdgeBaseOfSupportXPos);
        frameDataToStore.Add(frontEdgeBaseOfSupportYPos);
        frameDataToStore.Add(backEdgeBaseOfSupportYPos);

        // Get the time information (frame)
        uint mostRecentlyAccessedViconFrameNumber = centerOfMassManagerScript.getMostRecentlyAccessedViconFrameNumber();
        frameDataToStore.Add((float)mostRecentlyAccessedViconFrameNumber);

        // Get the trial # and block #
        float trialNumber = (float)currentTrialIndex; //we only have trials for now. Should we implement a block format for excursion?
        frameDataToStore.Add(trialNumber);

        // Retrieve the player position
        frameDataToStore.Add(player.transform.position.x);
        frameDataToStore.Add(player.transform.position.y);

        // Get the player position in Vicon coordinates, as the player position could be lagging the most recent COM data
        Vector3 playerPositionInViconCoords = centerOfMassManagerScript.convertUnityWorldCoordinatesToViconCoordinates(player.transform.position);
        frameDataToStore.Add(playerPositionInViconCoords.x);
        frameDataToStore.Add(playerPositionInViconCoords.y);

        // Record the time-at-home stopwatch time
        frameDataToStore.Add(timeAtHomeStopwatch.ElapsedMilliseconds);

        // Record the current state
        // Store the logical flags relevant to this task (likely, state is the only one, but think on it)
        float currentStateFloat = -1.0f;
        if (currentState == setupStateString)
        {
            currentStateFloat = 0.0f;
        }
        else if (currentState == waitingForPlayerToMoveHomeStateString)
        {
            currentStateFloat = 1.0f;
        }
        else if (currentState == waitingForExcursionStartStateString)
        {
            currentStateFloat = 2.0f;
        }
        else if (currentState == excursionActiveStateString)
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


        // Get task-specific parameters (excursion indicator coordinates, region logic flags)
        frameDataToStore.Add((float)currentExcursionDirection);
        frameDataToStore.Add(excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection]);
        frameDataToStore.Add(excursionIndicator.transform.position.x);
        frameDataToStore.Add(excursionIndicator.transform.position.y);
        //convert the indicator position to Vicon coordinates, then store
        Vector3 excursionIndicatorInViconCoords = centerOfMassManagerScript.convertUnityWorldCoordinatesToViconCoordinates(excursionIndicator.transform.position);
        frameDataToStore.Add(excursionIndicatorInViconCoords.x);
        frameDataToStore.Add(excursionIndicatorInViconCoords.y);

        // Store the logical flags relevant to this task
        frameDataToStore.Add(Convert.ToSingle(isChestInProperRegion[0])); //most recent flag value is in index 0
        frameDataToStore.Add(Convert.ToSingle(inTrialFlag)); //whether or not the 1-second average indicator has moved (current trial has started), or not.

        //Send the data to the general data recorder. It will be stored in memory until it is written to a CSV file.
        generalDataRecorderScript.storeRowOfFrameData(frameDataToStore);

    }




    // This function stores a single trial's summary data by sending a "row" of data to the general data recorder. 
    private void storeTrialData()
    {
        // the list that will store the data
        List<float> trialDataToStore = new List<float>();

        // Get the trial # and block #
        float trialNumber = (float)currentTrialIndex; //we only have trials for now. Should we implement a block format for excursion?
        trialDataToStore.Add(trialNumber);

        // The stimulation status string for this block 
        float stimulationStatusAsFloat;
        if (currentStimulationStatus == stimulationOffStatusName) // if stimulation is off
        {
            stimulationStatusAsFloat = 0.0f;
        }
        else if (currentStimulationStatus == stimulationOnStatusName) // if stimulation is on
        {
            stimulationStatusAsFloat = 1.0f;
        }
        else //else if the string is not formatted properly
        {
            stimulationStatusAsFloat = -100.0f; //store something unintuitive, indicating an error

        }
        trialDataToStore.Add(stimulationStatusAsFloat);

        //Get the excursion direction number and angle from the +x-axis
        trialDataToStore.Add((float)currentExcursionDirection);
        trialDataToStore.Add(excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection]);

        // store the indicator position in Unity coordinates (should be at it's position of furthest excursion at the end of the trial
        trialDataToStore.Add(excursionIndicator.transform.position.x);
        trialDataToStore.Add(excursionIndicator.transform.position.y);

        // store the indicator position converted into Vicon coordinates
        Vector3 indicatorPositionInViconCoords = centerOfMassManagerScript.convertUnityWorldCoordinatesToViconCoordinates(excursionIndicator.transform.position);
        trialDataToStore.Add(indicatorPositionInViconCoords.x);
        trialDataToStore.Add(indicatorPositionInViconCoords.y);

        // store the distance traveled out in Unity units
        trialDataToStore.Add(maximumExcursionAlongAxisThisTrial);

        // calculate the distance traveled out in Vicon (real-world, mm) units
        // get edges of base of support
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
            float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();
        //compute distance from center of base of support to the furthest excursion point (all in Vicon coords)
        Vector3 centerOfBaseOfSupportViconCoords = new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos) / 2.0f, (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f, indicatorPositionInViconCoords.z);
        float maxExcursionDistanceInViconUnitsOfMillimeters = Vector3.Distance(centerOfBaseOfSupportViconCoords, indicatorPositionInViconCoords);
        trialDataToStore.Add(maxExcursionDistanceInViconUnitsOfMillimeters);

        //store the edges of the base of support as well 
        trialDataToStore.Add(leftEdgeBaseOfSupportXPos);
        trialDataToStore.Add(rightEdgeBaseOfSupportXPos);
        trialDataToStore.Add(frontEdgeBaseOfSupportYPos);
        trialDataToStore.Add(backEdgeBaseOfSupportYPos);
        trialDataToStore.Add(centerOfBaseOfSupportViconCoords.x);
        trialDataToStore.Add(centerOfBaseOfSupportViconCoords.y);

        // Record the Unity unit radius for being at homoe
        trialDataToStore.Add(atHomeRadiusUnityCoords);

        // Record the Unity unit radius for starting an excursion
        trialDataToStore.Add(startExcursionRadiusUnityCoords);

        //store the trial time, from first entering the correct region until trial end.
        float trialDuration = currentTrialEndTime - currentTrialStartTime;
        trialDataToStore.Add(trialDuration);

        //could consider storing extra parameters such as path length...

        //ADDITIONAL: record the excursion direction and distance so we can build the functional boundaries of stability (fitting an ellipse)
        // QUESTION TO SELF: why is this code here?????
        subjectPerformanceExcursionDirections[currentTrialIndex] = currentExcursionDirection;
        subjectPerformanceExcursionDistanceUnityUnits[currentTrialIndex] = maximumExcursionAlongAxisThisTrial;
        subjectPerformanceExcursionDistanceViconUnitsMm[currentTrialIndex] = maxExcursionDistanceInViconUnitsOfMillimeters;

        //send all of this trial's summary data to the general data recorder
        generalDataRecorderScript.storeRowOfTrialData(trialDataToStore.ToArray());


    }




    private void buildAndStoreExcursionPerformanceSummary()
    {
        float[] maxExcursionAlongEachDirection = new float[numberOfExcursionDirections]; //stores the max excursion for each direction. Element i corresponds to excursion direction i. Note: default value of float is zero.
        for (uint excursionTrialIndex = 0; excursionTrialIndex < subjectPerformanceExcursionDirections.Length; excursionTrialIndex++)
        {
            //get the excursion direction of the current trial
            uint excursionDirection = subjectPerformanceExcursionDirections[excursionTrialIndex];
            float excursionDistanceViconUnitsOfMillimeters = subjectPerformanceExcursionDistanceViconUnitsMm[excursionTrialIndex];

            Debug.Log("At end of block, excursion number " + excursionTrialIndex + " had excursion distance " + excursionDistanceViconUnitsOfMillimeters);
            //see if the corresponding excursion distance is the greatest we have seen thus far. 
            if (excursionDistanceViconUnitsOfMillimeters > maxExcursionAlongEachDirection[excursionDirection]) //if it is the greatest excursion distance
            {
                //store it
                maxExcursionAlongEachDirection[excursionDirection] = excursionDistanceViconUnitsOfMillimeters;
            }
        }

        //the array with the max excursion along each direction is the performance summary data, so send it to the general data recorder
        generalDataRecorderScript.storeRowOfExcursionPerformanceSummaryData(maxExcursionAlongEachDirection);
    }


    //This function is called when the frame data should be written to file (typically at the end of a block of data collection). 
    private void tellDataRecorderToWriteStoredDataToFile()
    {
        // Tell the general data recorder to write the frame data to file
        generalDataRecorderScript.writeFrameDataToFile();

        //Also, tell the center of mass Manager object to tell the general data recorder to store the marker/COM data to file. 
        centerOfMassManagerScript.tellDataRecorderToSaveStoredDataToFile(subdirectoryName, mostRecentFileNameStub);

        //Also, tell the general data recorder to write the task-specific trial data to  file
        generalDataRecorderScript.writeTrialDataToFile();

        // Tell the general data recorder to write the excursion distances data to  file
        generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();

        // If we're using EMG data (if the size of the EMG data is not zero)
        int numberEmgSamplesStored = generalDataRecorderScript.GetNumberOfEmgDataRowsStored();
        Debug.Log("About to write EMG data to file. EMG data has num. samples: " + numberEmgSamplesStored);
        if (numberEmgSamplesStored != 0)
        {
            Debug.Log("Writing EMG data to file");
            // Tell the general data recorder to write the EMG data to file
            generalDataRecorderScript.writeEmgDataToFile();
        }
    }



    //private void saveTrialData()
    //{
    //    frameDataToStore.Add((float)currentExcursionDirection);
    //    frameDataToStore.Add(excursionDirectionAnglesFromRightwardsCcw[currentExcursionDirection]);
    //}
}