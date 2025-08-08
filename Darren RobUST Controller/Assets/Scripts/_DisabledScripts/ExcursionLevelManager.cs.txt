using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Diagnostics;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class ExcursionLevelManager : LevelManagerScriptAbstractClass
{
    // Saving the excursion limits
    public bool overwriteExcursionLimits; // whether or not to overwrite the excursion limits already recorded for this subject on this day, if they exist.
    private bool excursionLimitsAlreadyExist = false; // whether or not the excursion limit file already exists. We assume false and check when creating file names.
    public uint excursionsPerAxis = 3; //how many excursions along each axis will be conducted
    private uint numberOfExcursionDirections = 8;
    public uint[] excursionOrderList; //the list of excursion orders that will be carried out. Filled with integers ranging from 0 to numberOfExcursionDirections - 1.
    private uint currentExcursionDirection = 0; //ranges from 0 to numberOfExcursionDirections - 1.
    private string[] excursionDirectionNames;
    private float[] excursionDirectionAnglesFromXAxisViconFrame;// = new float[] { 0.0f, 45.0f, 90.0f, 135.0f, 180.0f, 225.0f, 270.0f, 315.0f };
    public float maximumAngularDeviationFromAxisInDegrees = 10; //plus or minus, in both directions (so, a value of ten degrees would give a total range of 20 degrees)
    public int currentTrialIndex = 0; //the index of the current trial
    private float maximumExcursionAlongAxisThisTrial = 0; //max distance along the current axis that the average player position has moved from center this trial
    private float maximumComExcursionAlongAxisThisTrial = 0; // max distance the window-averaged COM has moved along axis this trial
    private float maximumChestExcursionAlongAxisThisTrial = 0; // max distance the window-averaged chest has moved along axis this trial
    public float distanceFromCenterToStartTrial; // how far the window-averaged (!) player position must be from the center to start a new trial (in the correct region, as well)
    public float distanceFromCenterToEndTrial; // how close the window-averaged player position must be to the center to end an ongoing trial
    public GameObject player; //the player game object
    private PlayerControllerComDrivenBasic playerScript; // the script on the player object
    public GameObject excursionIndicator; //the indicator of how far the player has gone along an axis. Averaged over a window.
    public GameObject axesRenderer; //the object containing the line renderers of the axes and the rendering script
    private RenderExcursionAxes axesRendererScript; //the script that renders the axes
    private Vector3 axesCenter; //the center position of the axes
    public float indicatorStartDistanceAlongAxis; //how far from the center the indicator starts, which helps indicate which axis is the goal axis.
    public Vector3[] playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame; //an array that stores the player's position over a certain period, which we'll average to update the indicator
    public bool[] isPlayerInProperRegion; //an array recording if the player was in a valid region for moving along the specified axis, at a particular time sample.
    public float playerPositionAveragingPeriod; //over how long we average the player's position to get a sense of their excursion.
    private float fixedUpdateFrequency;  //how often fixed update is called. Based on the value of Time.fixedDeltaTime (which is mutable, by the way).
    private int numberPlayerPositionsToAverage; //how many samples of the player position we average to get indicator max excursion. Based on fixed update frequency and desired averaging period.

    // Position averaging for the chest and COM to compute max excursion along each axis
    public Vector3[] comPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod; //an array that stores the chest position over a certain period
    public Vector3[] chestPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod; //an array that stores the chest position over a certain period

    // Task name
    private const string thisTaskNameString = "Excursion"; 


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
    private float[] subjectPerformanceExcursionDistanceViconUnitsMm; //how far the "player" (mapped from control point) went on each trial
    private float[] subjectComExcursionDistanceViconUnitsMm; // how far the COM went on each trial, projected onto excursion axis
    private float[] subjectChestExcursionDistanceViconUnitsMm; // how far the chest went on each trial, projected onto excursion axis

    //stimulation status
    private string currentStimulationStatus; //the current stimulation status for this block
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // Mapping Vicon coordinates to Unity coordinates
    private float leftEdgeOfBaseOfSupportInViewportCoords; //Sets how much of the screen the base of support takes up. Initialized in Start()
    private float rightEdgeOfBaseOfSupportInViewportCoords; //Sets how much of the screen the base of support takes up. Initialized in Start()
    private float backEdgeOfBaseOfSupportInViewportCoords; //Sets how much of the screen the base of support takes up. Initialized in Start()
    private float frontEdgeOfBaseOfSupportInViewportCoords; //Sets how much of the screen the base of support takes up. Initialized in Start()

    // The edges of the base of support
    private float leftEdgeBaseOfSupportViconXPos;
    private float rightEdgeBaseOfSupportViconXPos;
    private float frontEdgeBaseOfSupportViconYPos;
    private float backEdgeBaseOfSupportViconYPos;
    private Vector3 centerOfBaseOfSupportViconPos;

    // The distance from the center of base of support to be "at home position", defined 
    // as a percentage of the minimum dimension of the base of support
    private float atHomeMaxDistanceFromCenterOfBaseOfSupportAsFractionOfMinimumDimensionOfBaseOfSupport = 0.06f;
    private float atHomeRadiusUnityCoords;

    // The minimum distance from the center of base of support to start an excursion , defined 
    // as a percentage of the minimum dimension of the base of support
    private float minDistanceFromCenterOfBaseOfSupportToStartExcursionAsFractionOfMinimumDimensionOfBaseOfSupport = 0.07f;
    private float startExcursionRadiusUnityCoords;

    // Tracking the at-home status
    private uint millisecondsRequiredAtHomeToStartATrial = 3000;
    private bool isPlayerAtHome = false; // keep track of whether or not the player is at home,
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

    // The "universal experiment settings" - one key function is setting the control point used (chest or COM)
    public UniversalExperimentSettings experimentSettingsScript;

    //testing only - should be deletable!
    uint testStoreFrameDataCounter = 0;

    // Excursion performance summary file naming
    private string defaultExcursionPerformanceSummaryFileName; // the default file name we load from and save to




    // Start is called before the first frame update
    void Start()
    {
        fixedUpdateFrequency = 1.0f / Time.fixedDeltaTime;
        Debug.Log("fixed update frequency " + fixedUpdateFrequency);
        numberPlayerPositionsToAverage = (int)Mathf.Ceil(playerPositionAveragingPeriod * fixedUpdateFrequency);
        Debug.Log("numberPlayerPositionsToAverage is " + numberPlayerPositionsToAverage);

        playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame = new Vector3[numberPlayerPositionsToAverage]; //we will need to store as many samples as are taken in the desired averaging period
        comPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod = new Vector3[numberPlayerPositionsToAverage]; //we will need to store as many samples as are taken in the desired averaging period
        chestPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod = new Vector3[numberPlayerPositionsToAverage]; //we will need to store as many samples as are taken in the desired averaging period

        isPlayerInProperRegion = new bool[numberPlayerPositionsToAverage];
        axesRendererScript = axesRenderer.GetComponent<RenderExcursionAxes>();
        excursionDirectionNames = new string[numberOfExcursionDirections];
        excursionDirectionNames[0] = "Horizontal Right";
        excursionDirectionNames[1] = "Vertical Forward";
        excursionDirectionNames[2] = "Horizontal Left";
        excursionDirectionNames[3] = "Vertical Backward";

        //marker data and center of mass manager
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // Player script
        playerScript = player.GetComponent<PlayerControllerComDrivenBasic>();

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

        // Set key parameters for the mapping from Vicon frame to "on-screen" Unity frame. 
        // The mapping can depend on the control point used, i.e. if the COM is used, the base of support fills most of the screen, 
        // but if the chest is used, we see more of the ground plane outside of the base of support.
        // Get the current control point settings object
        controlPointEnum controlPointCurrentSetting = experimentSettingsScript.GetControlPointSettingsEnumObject();
        // Depending on the control point desired
        switch (controlPointCurrentSetting)
        {
            // if the COM is the control point
            case controlPointEnum.COM:
                // Set the edges of baser support to be close to the edges of the screen/viewport
                leftEdgeOfBaseOfSupportInViewportCoords = 0.1f;
                backEdgeOfBaseOfSupportInViewportCoords = 0.1f;
                break;
            // if the chest is the control point
            case controlPointEnum.Chest:
                // Set the edges of baser support to be further from the edges of the screen/viewport
                leftEdgeOfBaseOfSupportInViewportCoords = 0.3f;
                backEdgeOfBaseOfSupportInViewportCoords = 0.3f;
                break;
        }

        // Determine how/where the edges of the base of support will map onto the screen
        rightEdgeOfBaseOfSupportInViewportCoords = 1.0f - leftEdgeOfBaseOfSupportInViewportCoords;
        frontEdgeOfBaseOfSupportInViewportCoords = 1.0f - backEdgeOfBaseOfSupportInViewportCoords;


        // Initialize the arrays that will store the actual subject performance (excursion distance and direction) for each trial
        subjectPerformanceExcursionDirections = new uint[excursionOrderList.Length];
        subjectPerformanceExcursionDistanceUnityUnits = new float[excursionOrderList.Length];
        subjectPerformanceExcursionDistanceViconUnitsMm = new float[excursionOrderList.Length];
        subjectComExcursionDistanceViconUnitsMm = new float[excursionOrderList.Length];
        subjectChestExcursionDistanceViconUnitsMm = new float[excursionOrderList.Length];

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
                // Get the edges of the base of support in Vicon frame
                (leftEdgeBaseOfSupportViconXPos, rightEdgeBaseOfSupportViconXPos, frontEdgeBaseOfSupportViconYPos,
                 backEdgeBaseOfSupportViconYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();
                
                // Get the center of the base of support in Vicon frame
                centerOfBaseOfSupportViconPos = new Vector3((leftEdgeBaseOfSupportViconXPos + rightEdgeBaseOfSupportViconXPos)/2.0f,
                (frontEdgeBaseOfSupportViconYPos + backEdgeBaseOfSupportViconYPos)/2.0f,
                0.0f);

                // Set the radius defining the "at-home" area and the radius defining the minimum distance to start an excursion trial
                defineAtHomeRadiusAndExcursionStartRadius();

                // Send a command to Labview with the task-specific info string (subject number, date, time). 
                forceFieldRobotTcpServerScript.SendCommandWithCurrentTaskInfoString(mostRecentFileNameStub);

                // Get the on-screen excursion axis angles, defined CCW from the rightwards direction
                excursionDirectionAnglesFromXAxisViconFrame = axesRendererScript.GetExcursionDirectionAnglesCcwFromRightwardsViconFrame();

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
                communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

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

            //Regardless of state, update the stored player position and proper region arrays
            float playerAngleInDegreesViconFrame = updatePlayerPositionsArray();

            switch (currentState)
            {
                case waitingForPlayerToMoveHomeStateString:
                    // monitor whether or not the player is at home
                    monitorIfPlayerIsInHomePosition();

                    // If the player has been at home long enough
                    if (timeAtHomeStopwatch.IsRunning && (timeAtHomeStopwatch.ElapsedMilliseconds > millisecondsRequiredAtHomeToStartATrial))
                    {
                        // Then we've been at home long enough. The subject can now start an excursion when ready. 
                        // Move to the Waiting for Excursion Start state
                        changeActiveState(waitingForExcursionStartStateString);

                    }
                    break;
                case waitingForExcursionStartStateString:
                    // Monitor for the start of an excursion. This occurs if the 1-second window average player position
                    // is far enough from the center of base of support AND the player is currently in the correct sector
                    // for the excursion direction.
                    monitorForStartOfExcursionTrialInProperSector();
                    break;
                case excursionActiveStateString:
                    // Update the moving average indicator and monitor for the end of the excursion trial. 
                    // The trial ends when the COM re-enters the "at home" position
                    monitorForEndOfOngoingExcursionTrial(playerAngleInDegreesViconFrame);
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
        // Convert the position from Vicon coordinates to Viewport coordinates
        float pointInViewportCoordsX = leftEdgeOfBaseOfSupportInViewportCoords + ((pointInViconFrame.x - leftEdgeBaseOfSupportViconXPos) / (rightEdgeBaseOfSupportViconXPos - leftEdgeBaseOfSupportViconXPos)) * (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords);
        float pointInViewportCoordsY = backEdgeOfBaseOfSupportInViewportCoords + ((pointInViconFrame.y - backEdgeBaseOfSupportViconYPos) / (frontEdgeBaseOfSupportViconYPos - backEdgeBaseOfSupportViconYPos)) * (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords);

        //then convert viewport coordinates to Unity world coordinates
        Vector3 pointInUnityWorldCoords = sceneCamera.ViewportToWorldPoint(new Vector3(pointInViewportCoordsX, pointInViewportCoordsY, 5.0f));

        return pointInUnityWorldCoords;
        // return centerOfMassManagerScript.convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(pointInViconFrame);
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // convert Unity world coordinates to Viewport coordinates
        Vector3 pointInViewportCoordinates = sceneCamera.WorldToViewportPoint(pointInUnityFrame);

        // convert Viewport coordinates to Vicon coordinates (defined relative to the base of support)
        float comXPositionInViconCoords = leftEdgeBaseOfSupportViconXPos + ((pointInViewportCoordinates.x - leftEdgeOfBaseOfSupportInViewportCoords) / (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords)) * (rightEdgeBaseOfSupportViconXPos - leftEdgeBaseOfSupportViconXPos);
        float comYPositionInViconCoords = backEdgeBaseOfSupportViconYPos + ((pointInViewportCoordinates.y - backEdgeOfBaseOfSupportInViewportCoords) / (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords)) * (frontEdgeBaseOfSupportViconYPos - backEdgeBaseOfSupportViconYPos);

        // return the point in Vicon coordinates
        return new Vector3(comXPositionInViconCoords, comYPositionInViconCoords, 0.0f);


        //return centerOfMassManagerScript.convertUnityWorldCoordinatesToViconCoordinates(pointInUnityFrame);
    }

    public override Vector3 GetControlPointForRobustForceField()
    {
        if(currentState != setupStateString)
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
        isPlayerAtHome = false;
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
        maximumExcursionAlongAxisThisTrial = 0.0f; //reset the maximum excursion along the axis to zero
        currentExcursionDirection = excursionOrderList[currentTrialIndex]; //update the current excursion direction
        maximumComExcursionAlongAxisThisTrial = 0.0f; 
        maximumChestExcursionAlongAxisThisTrial = 0.0f;

        // Ensure that the excursion indicator is visible

        resetIndicatorForCurrentExcursionDirection(); //put the indicator in the right position and orientation

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
        excursionDirectionAnglesFromXAxisViconFrame = subjectSpecificOnScreenExcursionAnglesCounterclockwiseFromXAxisInDegrees;
        
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



    private void resetIndicatorForCurrentExcursionDirection()
    {
        //  Ensure that the indicator GameObject is active
        excursionIndicator.SetActive(true);

        //get the current excursion direction angle, in radians
        float excursionAngleInRadians = excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection] * convertDegreesToRadians;

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
        float desiredRotationAngle = (90.0f + excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection]) % 360.0f;
        excursionIndicator.transform.Rotate(0.0f, 0.0f, desiredRotationAngle, Space.World);
    }



    private float updatePlayerPositionsArray()
    {

        //get player position information, relative to center of axes
        axesCenter = axesRendererScript.getAxesCenterPosition();
        float playerYRelativeToCenter = (player.transform.position.y - axesCenter.y);
        float playerXRelativeToCenter = (player.transform.position.x - axesCenter.x);
        Vector3 playerPosViconFrame = mapPointFromUnityFrameToViconFrame(player.transform.position);
        float playerYViconFrame = playerPosViconFrame.y;
        float playerXViconFrame = playerPosViconFrame.x;
        float playerYViconFrameRelativeToCenterOfBos = playerYViconFrame - centerOfBaseOfSupportViconPos.y;
        float playerXViconFrameRelativeToCenterOfBos = playerXViconFrame - centerOfBaseOfSupportViconPos.x;
        // Compute the current player angle. We add 180 since the Vicon frame and Unity frame are rotated 180 about the z-axis,
        // and we define our excursion axes angles in Unity frame.
        float currentPlayerAngle = 180.0f + Mathf.Atan2(playerYViconFrameRelativeToCenterOfBos, playerXViconFrameRelativeToCenterOfBos) * (180.0f / Mathf.PI);

        //float currentPlayerAngle = Mathf.Atan2(playerYRelativeToCenter, playerXRelativeToCenter) * (180.0f / Mathf.PI);
        Vector3 playerPositionRelativeToAxesCenterViconFrame = new Vector3(playerXViconFrameRelativeToCenterOfBos, playerYViconFrameRelativeToCenterOfBos, player.transform.position.z);

        //first, just put the player position into the array of player positions
        Vector3[] tempPlayerPositionsArray = new Vector3[numberPlayerPositionsToAverage];
        Array.Copy(playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame, 0, tempPlayerPositionsArray, 1, numberPlayerPositionsToAverage - 1);
        tempPlayerPositionsArray[0] = playerPositionRelativeToAxesCenterViconFrame; //the most recent player position is in element 0 of the array
        playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame = tempPlayerPositionsArray;

        // Store the current Vicon center of mass (COM) position in the array of COM positions
        Vector3 currentComPositionInViconFrame = centerOfMassManagerScript.getSubjectCenterOfMassInViconCoordinates();
        Vector3[] tempComPositionsArray = new Vector3[numberPlayerPositionsToAverage];
        Array.Copy(comPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod, 0, tempComPositionsArray, 1, numberPlayerPositionsToAverage - 1);
        tempComPositionsArray[0] = currentComPositionInViconFrame - centerOfBaseOfSupportViconPos; //the most recent player position is in element 0 of the array
        comPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod = tempComPositionsArray;

        // Store the current Vicon chest position in the array of chest positions
        Vector3 currentChestPositionInViconFrame = centerOfMassManagerScript.GetCenterOfTrunkBeltPositionInViconFrame();
        Vector3[] tempChestPositionsArray = new Vector3[numberPlayerPositionsToAverage];
        Array.Copy(chestPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod, 0, tempChestPositionsArray, 1, numberPlayerPositionsToAverage - 1);
        tempChestPositionsArray[0] = currentChestPositionInViconFrame - centerOfBaseOfSupportViconPos; //the most recent player position is in element 0 of the array
        chestPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod = tempChestPositionsArray;

        //convert the player angle to range from 0 to 359.999 degrees.
        float degreesInCircle = 360.0f;
        if (currentPlayerAngle < 0)
        {
            currentPlayerAngle = degreesInCircle + currentPlayerAngle; //360 is degrees in a circle
        }
        if (currentPlayerAngle >= 360.0f)
        {
            currentPlayerAngle = currentPlayerAngle - degreesInCircle; //360 is degrees in a circle
        }

        //see if the player is in the right angle "slice", close enough to the target axis
        bool playerCloseToAxis = false;
        float currentExcursionDirectionAngle = excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection];
        
        // If the player is within a maximum angular error from the target axis
        // Note, the first conditional statement is important for all excursion angles, while the second is needed for the
        // rightwards direction, which is both zero and 360.0f.
        if ((Mathf.Abs(currentExcursionDirectionAngle - currentPlayerAngle) <= maximumAngularDeviationFromAxisInDegrees)
            || (Mathf.Abs((currentExcursionDirectionAngle + 360.0f) - currentPlayerAngle) <= maximumAngularDeviationFromAxisInDegrees) )
        {
            // Then the player is close enough to the axis to move the indicator
            playerCloseToAxis = true;
        }
        else
        {
            playerCloseToAxis = false;
        }

        //put the boolean value, whether or not player is near axis, into the array
        bool[] tempPlayerInProperRegionArray = new bool[numberPlayerPositionsToAverage];
        Array.Copy(isPlayerInProperRegion, 0, tempPlayerInProperRegionArray, 1, numberPlayerPositionsToAverage - 1);
        tempPlayerInProperRegionArray[0] = playerCloseToAxis; //the most recent boolean indicating whether the player position is close enough to the axis
        isPlayerInProperRegion = tempPlayerInProperRegionArray;

        //Debug.Log("Current control point angle increasing CCW from right: " + currentPlayerAngle);

        return currentPlayerAngle;
    }



    private void monitorIfPlayerIsInHomePosition()
    {
        (bool isPlayerAtHomeUpdated, _) = isPlayerInHomeCircle();

        // If the state of the player being at home has changed (entered home, left home), 
        // change the stopwatch status as needed
        if (isPlayerAtHomeUpdated != isPlayerAtHome)
        {
            //if the player just entered home, i.e. is now within the central circle/"home area"
            if (isPlayerAtHomeUpdated)
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
        isPlayerAtHome = isPlayerAtHomeUpdated;
    }



    private (bool, bool) isPlayerInHomeCircle()
    {
        // Get distance of player from center in Unity frame
        float playerDistanceFromCircleCenter = Vector3.Distance(player.transform.position,
            new Vector3(axesCenter.x, axesCenter.y, player.transform.position.z));

        // See if the player is at home based on distance from the center in the Unity frame (have they left the home circle?)
        bool isPlayerCurrentlyAtHome = (playerDistanceFromCircleCenter < (atHomeRadiusUnityCoords));

        //Debug.Log("Player is " + playerDistanceFromCircleCenter + " Unity units from center. Home is at most " + atHomeRadiusUnityCoords.ToString("F3") + " units from center.");

        // If the player is no longer at home but just was, then they have just left home
        bool hasPlayerJustLeftHome = ((isPlayerCurrentlyAtHome == false) && (isPlayerAtHome == true));

        return (isPlayerCurrentlyAtHome, hasPlayerJustLeftHome);
    }


    private void monitorForStartOfExcursionTrialInProperSector()
    {
        Vector3 currentPlayerPositionRelativeToCenter = playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame[0];
        Vector3 averagePlayerPosition = getAveragePositionOfPointInWindow(playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame);
        float playerDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(player.transform.position.y, 2.0f) + Mathf.Pow(player.transform.position.x, 2.0f));
        bool inProperRegionForCurrentExcursionAxis = isPlayerInProperRegion[0];

/*        Debug.Log("In Waiting For Excursion Start state, avg distance from center is " + averagePlayerDistanceFromCenter +
            "and player is in proper region?" + inProperRegionForCurrentExcursionAxis 
            + "and minimum avg distance to start excursion is " + startExcursionRadiusUnityCoords);*/
        if (inProperRegionForCurrentExcursionAxis && (playerDistanceFromCenter >= startExcursionRadiusUnityCoords)) //if the player is in the right region and far enough from the center
        {
            //then start the trial
            inTrialFlag = true;

            //mark the time at which this trial started
            currentTrialStartTime = Time.time;

            // Switch to the Active Excursion Trial state
            changeActiveState(excursionActiveStateString);
        }
    }


    private void monitorForEndOfOngoingExcursionTrial(float playerAngleInDegreesViconFrame)
    {
        Vector3 currentPlayerPositionRelativeToCenterViconFrame = playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame[0];
        Vector3 averagePlayerPositionRelativeToCenterViconFrame = getAveragePositionOfPointInWindow(playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame);

        float averagePlayerDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(averagePlayerPositionRelativeToCenterViconFrame.y, 2.0f) + Mathf.Pow(averagePlayerPositionRelativeToCenterViconFrame.x, 2.0f));
        bool inProperRegionForCurrentExcursionAxis = isPlayerInProperRegion[0];

        // Update the maximum excursion distance and moving average indicator, if needed
        computeMaxExcursionAlongAxisAndUpdateIndicator(axesCenter, playerAngleInDegreesViconFrame, averagePlayerPositionRelativeToCenterViconFrame);

        // Convert the average player position relative to center to Unity frame
        Vector3 currentPlayerPositionUnityFrame = mapPointFromViconFrameToUnityFrame(playerPositionsInLastAveragingPeriodRelativeToCenterBosViconFrame[0]
            + centerOfBaseOfSupportViconPos);
        float currentPlayerDistanceFromCenterUnityFrame = Mathf.Sqrt(Mathf.Pow(player.transform.position.y, 2.0f) +
            Mathf.Pow(player.transform.position.x, 2.0f));
        if (currentPlayerDistanceFromCenterUnityFrame <= atHomeRadiusUnityCoords) //if the player has returned close enough to the center
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




    private void computeMaxExcursionAlongAxisAndUpdateIndicator(Vector3 axesCenterPosition, float currentPlayerAngleInDegrees, 
        Vector3 averagePlayerPositionRelativeToBosCenterViconFrame)
    {
        //Would want to store/save this information!
        if (isPlayerInProperRegion.All(inRegionBool => (inRegionBool == true))) //if the player has been in the proper region over the entire last averaging period
        {
            // 1.) Use the player position, averaged over a short window, to control the indicator.
            float currentExcursionDirectionAngleViconFrame = excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection]; // Vicon frame
            float currentExcursionDirectionAngleRadiansFromRightCcw = currentExcursionDirectionAngleViconFrame * (Mathf.PI / 180.0f);
            // Compute and clamp player angle. 
            float averagePlayerAngle = 180.0f + Mathf.Atan2(averagePlayerPositionRelativeToBosCenterViconFrame.y, averagePlayerPositionRelativeToBosCenterViconFrame.x) * (180.0f / Mathf.PI);
            //convert the player angle to range from 0 to 359.999 degrees.
            float degreesInCircle = 360.0f;
            if (averagePlayerAngle < 0)
            {
                averagePlayerAngle = degreesInCircle + averagePlayerAngle; //360 is degrees in a circle
            }
            if (averagePlayerAngle >= 360.0f)
            {
                averagePlayerAngle = averagePlayerAngle - degreesInCircle; //360 is degrees in a circle
            }

            float averagePlayerDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(averagePlayerPositionRelativeToBosCenterViconFrame.y, 2.0f) + Mathf.Pow(averagePlayerPositionRelativeToBosCenterViconFrame.x, 2.0f));
            //the average distance along the axis is the average distance from the center projected onto the axis
            float averagePlayerDistanceAlongAxis = Mathf.Cos(Mathf.Abs((currentExcursionDirectionAngleViconFrame - averagePlayerAngle) * (Mathf.PI / 180.0f))) * averagePlayerDistanceFromCenter;
            // Store if the largest distance seen this trial
            if (averagePlayerDistanceAlongAxis > maximumExcursionAlongAxisThisTrial)
            {
                maximumExcursionAlongAxisThisTrial = averagePlayerDistanceAlongAxis;
            }

            //compute and update the actual indicator position
            // Convert the excursion direction back to the Vicon angles
            float currentExcursionDirectionAngleDegreesFromLeftCcw = currentExcursionDirectionAngleViconFrame + 180.0f;
            if (currentExcursionDirectionAngleDegreesFromLeftCcw < 0)
            {
                currentExcursionDirectionAngleDegreesFromLeftCcw = degreesInCircle + currentExcursionDirectionAngleDegreesFromLeftCcw; //360 is degrees in a circle
            }
            if (currentExcursionDirectionAngleDegreesFromLeftCcw >= 360.0f)
            {
                currentExcursionDirectionAngleDegreesFromLeftCcw = currentExcursionDirectionAngleDegreesFromLeftCcw - degreesInCircle; //360 is degrees in a circle
            }

            float currentExcursionDirectionAngleRadiansFromLeftCcw = currentExcursionDirectionAngleDegreesFromLeftCcw * (Mathf.PI / 180.0f);
            Vector3 indicatorPositionRelativeToBosCenterViconFrame = new Vector3(maximumExcursionAlongAxisThisTrial * Mathf.Cos(currentExcursionDirectionAngleRadiansFromLeftCcw), maximumExcursionAlongAxisThisTrial * Mathf.Sin(currentExcursionDirectionAngleRadiansFromLeftCcw), player.transform.position.z);
          /*  Debug.Log("Current excursion dir. in Vicon frame is: " + currentExcursionDirectionAngleDegreesFromLeftCcw + 
                " and Max excursion is: " + maximumExcursionAlongAxisThisTrial + " and indicator position relative to center BoS in Vicon frame: (" +
                indicatorPositionRelativeToBosCenterViconFrame.x + ", " + indicatorPositionRelativeToBosCenterViconFrame.y + ", " + indicatorPositionRelativeToBosCenterViconFrame.z + ")");*/

            // Convert to Unity frame
            // NOTE: we must add the center of BoS coordinate, since the current Vicon frame position is reported relative to center
            Vector3 indicatorPositionUnityFrame = mapPointFromViconFrameToUnityFrame(indicatorPositionRelativeToBosCenterViconFrame + centerOfBaseOfSupportViconPos);
            //Debug.Log("Indicator position unity frame: (" + indicatorPositionUnityFrame.x + ", " + indicatorPositionUnityFrame.y + ", " + indicatorPositionUnityFrame.z + ")");

            excursionIndicator.transform.position = new Vector3(indicatorPositionUnityFrame.x, indicatorPositionUnityFrame.y, excursionIndicator.transform.position.z);

            // 2.) Use the COM position, averaged over a short window, to compute a max COM excursion along the direction
            // Com position is in Vicon frame relative to center of BoS
            Vector3 averageComPositionInWindow = getAveragePositionOfPointInWindow(comPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod);
            // Compute and clamp player angle. 
            float averageComAngle = 180.0f + Mathf.Atan2(averageComPositionInWindow.y, averageComPositionInWindow.x) * (180.0f / Mathf.PI);
            //convert the player angle to range from 0 to 359.999 degrees.
            if (averageComAngle < 0)
            {
                averageComAngle = degreesInCircle + averageComAngle; //360 is degrees in a circle
            }
            if (averageComAngle >= 360.0f)
            {
                averageComAngle = averageComAngle - degreesInCircle; //360 is degrees in a circle
            }
            float averageComDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(averageComPositionInWindow.y, 2.0f) + Mathf.Pow(averageComPositionInWindow.x, 2.0f));
            //the average distance along the axis is the average distance from the center projected onto the axis
            float averageComDistanceAlongAxis = Mathf.Cos(Mathf.Abs((currentExcursionDirectionAngleViconFrame - averageComAngle) * (Mathf.PI / 180.0f))) * averageComDistanceFromCenter;
            // Store if the largest distance seen this trial
            if (averageComDistanceAlongAxis > maximumComExcursionAlongAxisThisTrial)
            {
                maximumComExcursionAlongAxisThisTrial = averageComDistanceAlongAxis;
            }

            // 3.) Use the chest position, averaged over a short window, to compute a max chest excursion along the direction
            // Chest position is in Vicon frame relative to center of BoS
            Vector3 averageChestPositionInWindow = getAveragePositionOfPointInWindow(chestPositionsRelativeToCenterBosViconFrameInLastAveragingPeriod);
            // Compute and clamp player angle. 
            float averageChestAngle = 180.0f + Mathf.Atan2(averageChestPositionInWindow.y, averageChestPositionInWindow.x) * (180.0f / Mathf.PI);
            //convert the player angle to range from 0 to 359.999 degrees.
            if (averageChestAngle < 0)
            {
                averageChestAngle = degreesInCircle + averageChestAngle; //360 is degrees in a circle
            }
            if (averageChestAngle >= 360.0f)
            {
                averageChestAngle = averageChestAngle - degreesInCircle; //360 is degrees in a circle
            }
            float averageChestDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(averageChestPositionInWindow.y, 2.0f) + Mathf.Pow(averageChestPositionInWindow.x, 2.0f));
            //the average distance along the axis is the average distance from the center projected onto the axis
            float averageChestDistanceAlongAxis = Mathf.Cos(Mathf.Abs((currentExcursionDirectionAngleViconFrame - averageChestAngle) * (Mathf.PI / 180.0f))) * averageChestDistanceFromCenter;
            // Store if the largest distance seen this trial
            if (averageChestDistanceAlongAxis > maximumChestExcursionAlongAxisThisTrial)
            {
                maximumChestExcursionAlongAxisThisTrial = averageChestDistanceAlongAxis;
            }
/*            Debug.Log("Player max (player, COM, chest) excursion distance along axis this trial: (" + maximumExcursionAlongAxisThisTrial + ", "
                + maximumComExcursionAlongAxisThisTrial + ", " + maximumChestExcursionAlongAxisThisTrial);*/

        }

    }



    private Vector3 getAveragePositionOfPointInWindow(Vector3[] positionsInWindow)
    {
        float positionXPosAverage = 0.0f;
        float positionYPosAverage = 0.0f;
        float positionZPosAverage = 0.0f;

        for (int index = 0; index < numberPlayerPositionsToAverage; index++)
        {
            positionXPosAverage += positionsInWindow[index].x;
            positionYPosAverage += positionsInWindow[index].y;
            positionZPosAverage += positionsInWindow[index].z;

        }

        //take average by dividing by number of samples
        positionXPosAverage = positionXPosAverage / (float)numberPlayerPositionsToAverage;
        positionYPosAverage = positionYPosAverage / (float)numberPlayerPositionsToAverage;
        positionZPosAverage = positionZPosAverage / (float)numberPlayerPositionsToAverage;


        Vector3 averagePosition = new Vector3(positionXPosAverage, positionYPosAverage, positionZPosAverage);
        return averagePosition;
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
        string[] csvExcursionPerformanceSummaryHeaderNames = new string[] {"DIR_0_MAX_COM_EXCURSION_VICON_UNITS_MM", "DIR_1_MAX_COM_EXCURSION_VICON_UNITS_MM",
            "DIR_2_MAX_COM_EXCURSION_VICON_UNITS_MM", "DIR_3_MAX_COM_EXCURSION_VICON_UNITS_MM", "DIR_4_MAX_COM_EXCURSION_VICON_UNITS_MM",
            "DIR_5_MAX_COM_EXCURSION_VICON_UNITS_MM", "DIR_6_MAX_COM_EXCURSION_VICON_UNITS_MM", "DIR_7_MAX_COM_EXCURSION_VICON_UNITS_MM",
            "DIR_0_MAX_CHEST_EXCURSION_VICON_UNITS_MM", "DIR_1_MAX_CHEST_EXCURSION_VICON_UNITS_MM",
            "DIR_2_MAX_CHEST_EXCURSION_VICON_UNITS_MM", "DIR_3_MAX_CHEST_EXCURSION_VICON_UNITS_MM", "DIR_4_MAX_CHEST_EXCURSION_VICON_UNITS_MM",
            "DIR_5_MAX_CHEST_EXCURSION_VICON_UNITS_MM", "DIR_6_MAX_CHEST_EXCURSION_VICON_UNITS_MM", "DIR_7_MAX_CHEST_EXCURSION_VICON_UNITS_MM"};

        //tell the data recorder what the excursion performance summary header names are
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryRowHeaderNames(csvExcursionPerformanceSummaryHeaderNames);


        //also set the default and run-specific excursion performance summary  file name
        defaultExcursionPerformanceSummaryFileName = "Excursion_Performance_Summary" + delimiter + currentStimulationStatus + ".csv";

        // We'd like to always save out a run-specific excursion performance summary, which we name here.
        string fileNameExcursionPerformanceSummary = fileNameStub + "_Excursion_Performance_Summary" + delimiter + currentStimulationStatus + ".csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(fileNameExcursionPerformanceSummary);
        
        // Set the run-specific name as the in-use file name for now.
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(fileNameExcursionPerformanceSummary);

        // If the default excursion performance summary file already exists
        bool excursionPerformanceFileAlreadyExists = generalDataRecorderScript.DoesFileAlreadyExist(subdirectoryString, defaultExcursionPerformanceSummaryFileName);
        if (excursionPerformanceFileAlreadyExists)
        {
            // We set the flag indicating the excursion limits file already exists.
            excursionLimitsAlreadyExist = true;
            Debug.Log("Excursion performance summary already existed for this date.");
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
        if(lastComPositionViconCoords != comPositionViconCoords)
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
        float trialNumber = (float) currentTrialIndex; //we only have trials for now. Should we implement a block format for excursion?
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
        frameDataToStore.Add(excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection]);
        frameDataToStore.Add(excursionIndicator.transform.position.x);
        frameDataToStore.Add(excursionIndicator.transform.position.y);
        //convert the indicator position to Vicon coordinates, then store
        Vector3 excursionIndicatorInViconCoords = centerOfMassManagerScript.convertUnityWorldCoordinatesToViconCoordinates(excursionIndicator.transform.position);
        frameDataToStore.Add(excursionIndicatorInViconCoords.x);
        frameDataToStore.Add(excursionIndicatorInViconCoords.y);

        // Store the logical flags relevant to this task
        frameDataToStore.Add(Convert.ToSingle(isPlayerInProperRegion[0])); //most recent flag value is in index 0
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
        float trialNumber = (float) currentTrialIndex; //we only have trials for now. Should we implement a block format for excursion?
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
        trialDataToStore.Add(excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection]);

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
        Vector3 centerOfBaseOfSupportViconCoords = new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos)/2.0f, (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f, indicatorPositionInViconCoords.z);
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

        // Record the excursion direction and max distance for this trial,
        // so we can build the functional boundaries of stability and/or workspace. 
        // We put this code here for convenience, because needed quantities are computed.
        subjectPerformanceExcursionDirections[currentTrialIndex] = currentExcursionDirection;
        subjectPerformanceExcursionDistanceUnityUnits[currentTrialIndex] = maximumExcursionAlongAxisThisTrial;
        subjectPerformanceExcursionDistanceViconUnitsMm[currentTrialIndex] = Vector3.Distance(centerOfBaseOfSupportViconCoords, indicatorPositionInViconCoords); ;
        subjectComExcursionDistanceViconUnitsMm[currentTrialIndex] = maximumComExcursionAlongAxisThisTrial; // vicon units of mm
        subjectChestExcursionDistanceViconUnitsMm[currentTrialIndex] = maximumChestExcursionAlongAxisThisTrial; // vicon units of mm

        Debug.Log("Max indicator/COM excursion this trial: (" + maximumComExcursionAlongAxisThisTrial + ", " + maximumChestExcursionAlongAxisThisTrial + " )");

        //send all of this trial's summary data to the general data recorder
        generalDataRecorderScript.storeRowOfTrialData(trialDataToStore.ToArray());


    }




    private void buildAndStoreExcursionPerformanceSummary()
    {
        float[] maxControlPointExcursionAlongEachDirection = new float[numberOfExcursionDirections]; //stores the max control point excursion for each direction. Element i corresponds to excursion direction i. Note: default value of float is zero.
        float[] maxComExcursionAlongEachDirection = new float[numberOfExcursionDirections]; //stores the max COM excursion for each direction. Element i corresponds to excursion direction i. Note: default value of float is zero.
        float[] maxChestExcursionAlongEachDirection = new float[numberOfExcursionDirections]; // stores the max chest excursion for each direction.
        for(uint excursionTrialIndex = 0; excursionTrialIndex < subjectPerformanceExcursionDirections.Length; excursionTrialIndex++)
        {
            //get the excursion direction of the current trial
            uint excursionDirection = subjectPerformanceExcursionDirections[excursionTrialIndex];
            // Get the excursion distance of each point along the direction
            float controlPointExcursionDistanceViconUnitsOfMillimeters = subjectPerformanceExcursionDistanceViconUnitsMm[excursionTrialIndex];
            float comExcursionDistanceViconUnitsOfMillimeters = subjectComExcursionDistanceViconUnitsMm[excursionTrialIndex];
            float chestExcursionDistanceViconUnitsOfMillimeters = subjectChestExcursionDistanceViconUnitsMm[excursionTrialIndex];

            Debug.Log("At end of block, excursion number " + excursionTrialIndex + " had excursion distance " + comExcursionDistanceViconUnitsOfMillimeters);
            //see if the COM corresponding excursion distance is the greatest we have seen thus far. 
            if (controlPointExcursionDistanceViconUnitsOfMillimeters > maxControlPointExcursionAlongEachDirection[excursionDirection]) //if it is the greatest excursion distance
            {
                //store it
                maxControlPointExcursionAlongEachDirection[excursionDirection] = controlPointExcursionDistanceViconUnitsOfMillimeters;
            }

            //see if the COM corresponding excursion distance is the greatest we have seen thus far. 
            if (comExcursionDistanceViconUnitsOfMillimeters > maxComExcursionAlongEachDirection[excursionDirection]) //if it is the greatest excursion distance
            {
                //store it
                maxComExcursionAlongEachDirection[excursionDirection] = comExcursionDistanceViconUnitsOfMillimeters;
            }

            //see if the chest corresponding excursion distance is the greatest we have seen thus far. 
            if (chestExcursionDistanceViconUnitsOfMillimeters > maxChestExcursionAlongEachDirection[excursionDirection]) //if it is the greatest excursion distance
            {
                //store it
                maxChestExcursionAlongEachDirection[excursionDirection] = chestExcursionDistanceViconUnitsOfMillimeters;
            }
        }

        float[] excursionDataRowToStore = new float[maxComExcursionAlongEachDirection.Length + maxChestExcursionAlongEachDirection.Length]; // Initialize data row to store
        // If we're collecting data with the keyboard
        if (playerScript.getPlayerBeingControlledByKeyboardStatus() == true)
        {
            // Store the control point excursion limits as both COM and chest excursion limits, 
            // since we don't have real chest or COM excursion data.
            maxControlPointExcursionAlongEachDirection.CopyTo(excursionDataRowToStore, 0);
            maxControlPointExcursionAlongEachDirection.CopyTo(excursionDataRowToStore, maxControlPointExcursionAlongEachDirection.Length);
        }
        // Else if we're using marker data
        else
        {
            // Store the data as a single row with order [COM data by excursion direction, chest data by excursion direction]
            maxComExcursionAlongEachDirection.CopyTo(excursionDataRowToStore, 0);
            maxChestExcursionAlongEachDirection.CopyTo(excursionDataRowToStore, maxComExcursionAlongEachDirection.Length);

        }

        //the array with the max excursion along each direction is the performance summary data, so send it to the general data recorder
        generalDataRecorderScript.storeRowOfExcursionPerformanceSummaryData(excursionDataRowToStore);
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

        // Save out the run-specific excursion limits file
        generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();

        // Tell the general data recorder to write the excursion distances data to the default file name for saving/loading limits for all tasks,
        // if the limit file does NOT already exist or we are willing to overwrite them.
        if (!excursionLimitsAlreadyExist || (excursionLimitsAlreadyExist && overwriteExcursionLimits))
        {
            // Change the excursion limits file name to the default name 
            generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(defaultExcursionPerformanceSummaryFileName);

            // Write the current limits to the default file name.
            generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();
        }

        // If we're using EMG data (if the size of the EMG data is not zero)
        int numberEmgSamplesStored = generalDataRecorderScript.GetNumberOfEmgDataRowsStored();
        Debug.Log("About to write EMG data to file. EMG data has num. samples: " + numberEmgSamplesStored);
        if (numberEmgSamplesStored != 0)
        {
            Debug.Log("Writing EMG data to file");
            // Tell the general data recorder to write the EMG data to file
            generalDataRecorderScript.writeEmgDataToFile();
        }
        Debug.Log(subdirectoryName + " " + mostRecentFileNameStub);
    }

    void OnApplicationQuit()
    {
        //tellDataRecorderToWriteStoredDataToFile();
        Debug.Log("Running quit routine");

        // In case of early app termination, the tellDataRecorderToWriteStoredDataToFile() function will not be called, but we 
        // still should pulse the stop pin. Note, pulsing the stop pin twice is not harmful. 
        if (syncingWithExternalHardwareFlag)
        {
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();
        }
    }


    //private void saveTrialData()
    //{
    //    frameDataToStore.Add((float)currentExcursionDirection);
    //    frameDataToStore.Add(excursionDirectionAnglesFromXAxisViconFrame[currentExcursionDirection]);
    //}
}