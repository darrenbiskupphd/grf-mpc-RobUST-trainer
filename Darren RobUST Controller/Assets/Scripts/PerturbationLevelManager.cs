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


// NEXT UP: We seem to be commanding perturbation forces that look roughly correct (grow, plateau, fall). 
// Minor changes are next:
// - base plateau force on % subject body weight
// - add a safety check for initiating a perturbation. This check should be placed in the FF high-level controller script. 
// - Store relevant data to file
// - Create the most basic task - quiet stance with fixation dot (different Scene).
// - Update Vicon data stream publishing rate to around 40 Hz (higher with new PC?)
// - TEST them live. 


public class PerturbationLevelManager : LevelManagerScriptAbstractClass
{

    // BEGIN: Public variables needed during experiment, for experimental control*********************
    public uint[] numberOfBlocks; // How many blocks to use AND the type for each block. The element values are important. 
                                // Let: 1 = mixed A/P perturbations, 2 = mixed perturbations A/P/L/R
    public bool usingBeltsToApplyForce;
    public bool storeForcePlateData; // Whether or not we are using (and thus should store) force plate data. Expects two force plates if using.
    public uint numberOfTrialsPerDirectionPerBlock; // How many perturbation trials PER DIRECTION USED per block.
    // END: Public variables needed during experiment, for experimental control*********************

    // BEGIN: Public GameObject variables and attached scripts*********************

    public GameObject player; //  the player game object
    public GameObject circleCenter; //the circle center game object
    public Material moveToHomeMaterial; //the material (color) of the circle center indicating the player should move there.
    private Vector3 circleCenterPosition; //the position of the center of the circle in world coordinates. Call the circle center object to get this value in Start()
    private GameObject boundaryOfStabilityRenderer; //the renderer that draws the functional boundary of stability
    private RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript; // the script in the functional boundary of stability renderer
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

    // END: Public GameObject variables and attached scripts*********************



    // Which directions perturbations will be in.
    private int[] numberOfPertDirectionsUsedThisBlock; // the length is how many pert directions,
                                                       // the element values are the direction specifiers.
                                                       // Let 1.0 = forward, 2.0 = right, 3.0 = backwards, 4.0 = left
    // The perturbation directions and their integer values. 
    // Note, these are all defined from the subject's perspective, i.e. left = subject's left.
    private int perturbationForwardValue = 1;
    private int perturbationRightValue = 2;
    private int perturbationBackwardsValue = 3;
    private int perturbationLeftValue = 4;


    // Trial info
    private uint numTrialsPerBlock;
    private List<int> perturbationDirectionsPerTrial; // A List storing ints representing the directions of perturbation for each 
                                                      // trial in this block. 
                                                      // Let 1.0 = forward, 2.0 = right, 3.0 = backwards, 4.0 = left
    private bool trialCompleted = false; // whether or not the current trial has been completed. This is set to true when the perturbation ends.

    // The task name (for data saving/loading)
    private string thisTaskNameString = "Perturbations";

    // Random number generator to pseudorandomize trial order
    private System.Random randomNumberGenerator = new System.Random();

    // The percent of the screen (along the dimension taking up more of the screen, given the BoS aspect ratio)
    // we want the base of support (BoS) to fill up
    private float baseOfSupportaPercentOfScreenToFill = 0.85f;
    private float shortestDimensionofBaseOfSupportViconUnits;
    private float circleCenterRadiusAsPercentOfBaseOfSupportShortDimension = 0.10f;

    // Block type specifier strings
    private const uint mixedForwardBackwardBlockSpecifier = 1; // mixed anterior/posterior perturbations
    private const uint mixedAllFourDirectionsBlockSpecifier = 2; // mixed anterior/posterior/left/right perturbations

    // states used to control program flow
    private string currentState;
    private const string waitingForSetupStateString = "WAITING_FOR_SETUP"; // Waiting for COM manager and other services. As float, let it be state 0.0f.
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string waitingForHomePositionStateString = "WAITING_FOR_HOME_POSITION"; // Wait for player to move to home position for some time. state 1.0f.
    private const string readyForPerturbationStateString = "READY_FOR_PERTURBATION"; // Player has been at home long enough to be "ready" for perturbation. State 2.0f
    private const string ongoingPerturbationStateString = "ONGOING_PERTURBATION"; // A perturbation is ongoing. State 3.0f
    private const string gameOverStateString = "GAME_OVER"; // Game over. State 4.0f.

    // Other program flow flags
    private bool waitingForHomeAfterPerturbationFlag = false; // A program flow flag that is set to true after a perturbation. Let's us know that the 
                                                 // subject experienced a perturbation and we're waiting for them to come to home position
                                                 // before incrementing the trial number.

    // the circle center aka the "home" area properties
    private float circleCenterRadiusAsPercentOfLargerCircularTrajectoryRadius = 0.25f;
    private uint millisecondsRequiredAtHomeToBePertReady = 3000;
    private bool isPlayerAtHome = false; // keep track of whether or not the player is at home,
                                         // when we're in the waitingForHomePosition state. An initial state of
                                         // false is desirable.

    private float circleInCenterOfTrajectoryDiameterViconUnits; //the actual central "home" circle diameter in Vicon units [mm]
    private float circleInCenterOfTrajectoryDiameterUnityUnits; //the actual central "home" circle diameter in Unity units

    // the player properties, which can change from frame to frame
    private float currentPlayerAngleInDegreesViconFrame; //the most recent value of the angle to the player, relative to the circle center. 
                                                         // Measured counterclockwise from +x-axis (right), in degrees.

    // a stopwatch to monitor time spent in the "home area"
    private Stopwatch timeAtHomeStopwatch = new Stopwatch();

    // monitoring block and trial number
    private int currentTrialNumber = 0;
    private int currentBlockNumber = 0;

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

    // boundary of stability loading
    private string subdirectoryWithBoundaryOfStabilityData; // the string specifying the subdirectory (name) we'll load the 
                                                            // boundary of stability data from

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

    // The mapping function from Vicon frame to Unity frame variables.
    private float trajectoryAspectScalingFactor = 0.0f; // We scale the trajectory based on the ratio of base of support to screen aspect
    private float fitToScreenScalingFactor = 0.0f; // We also scale the trajectory to fill a certain percentage of the screen width or height
    private float baseOfSupportDimensionScaler; // We look at excursion from center of base of support in Vicon frame, but 
                                                // normalize it by the SAME dimension of base of support (widht or height) regardless 
                                                // of if we are looking at the x- or y-coordinate. It is a bit unusual, but it
                                                // makes the mapping a uniform mapping.
    private float rightwardsSign; // +1 if x-axes of Vicon and Unity are aligned, -1 if they are inverted
    private float forwardsSign; // +1 if y-axes of Vicon and Unity are aligned, -1 if they are inverted

    // Timing the trial
    private float currentTrialStartTime; // When the current trial started, measured as when the indicator first moves that trial
    private float currentTrialEndTime; // When the current trial ends, measured as when the full lap is completed

    // Tracking maximum excursion from center of BoS from the moment of perturbation onset until the player has
    // successfully returned home
    private float maxExcursionFromHomeDuringPerturbationViconFrameInMm;

    // Tracking if the FF was activated during the perturbation period (pert onset until returned home)
    private bool forceFieldActivatedDuringPerturbationFlag = false;

    // Assigning points to each trial
    private float maximumPointsEarnedPerTrial = 100.0f; // The maximum number of points the player can earn each trial.
    private float pointsEarnedByPlayerThisTrial; // The points earned by the player for this trial. 
    private float totalPointsEarnedByPlayerThisBlock = 0.0f; // The total points earned by the player this block.

    // Feedback, displaying the points earned to the user
    public GameObject FeedbackManagerGameObject;
    private ProvideOnscreenTextFeedbackScript feedbackManagerScript;

    // Communication with force field robot (Robust)
    public GameObject forceFieldRobotTcpServerObject;
    private CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;

    // RobUST force field type specifier
    private const string forceFieldIdleModeSpecifier = "I";
    private const string forceFieldAtExcursionBoundary = "B";
    private string currentDesiredForceFieldTypeSpecifier;

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

    // Plotting perturbed distance at end of task
    public GameObject windowGraphCanvas; // The canvas containing the window graph for plotting error vs trial at the end.
    public WindowGraph windowGraphPlottingScript;
    private List<float> normalizedPerturbedDistanceAllTrials = new List<float>();

    // Start is called before the first frame update
    void Start()
    {

        // Set the initial state. We start in the waiting for home state 
        enterWaitingForSetupState();

        // Get the script inside the functional boundary of stability renderer
        GameObject[] boundaryOfStabilityRenderers = GameObject.FindGameObjectsWithTag("BoundaryOfStability");
        if (boundaryOfStabilityRenderers.Length > 0) //if there are any boundary of stability renderers
        {
            boundaryOfStabilityRenderer = boundaryOfStabilityRenderers[0];
        }
        boundaryOfStabilityRendererScript = boundaryOfStabilityRenderer.GetComponent<RenderBoundaryOfStabilityScript>();

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

        // Get the force field high-level controller (used to see if FF was activated during perturbation only).
        forceFieldHighLevelControllerScript = forceFieldHighLevelControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();

        // Get the communication with force field robot (e.g. RobUST) script
        forceFieldRobotTcpServerScript = forceFieldRobotTcpServerObject.GetComponent<CommunicateWithRobustLabviewTcpServer>();

        // get the feedback manager's script 
        feedbackManagerScript = FeedbackManagerGameObject.GetComponent<ProvideOnscreenTextFeedbackScript>();

        // Get the force plate data manager script
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();
        

        // If we're syncing with external hardware (typically the EMGs are included)
        if (syncingWithExternalHardwareFlag == true)
        {
            // Get a reference to the EMGs
            emgDataStreamerScript = emgDataStreamerObject.GetComponent<StreamAndRecordEmgData>();
        }

        // Get reference to Photon-based hardware sync object
        communicateWithPhotonScript = communicateWithPhotonViaSerialObject.GetComponent<CommunicateWithPhotonViaSerial>();

        // Set the stimulation status.
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();
        Debug.Log("Before setting file naming, set the current stimulation status string to: " + currentStimulationStatus);

        //set the header names for the saved-out data CSV headers
        setFrameAndTrialDataNaming();

        // Set the default force field mode as Idle
        currentDesiredForceFieldTypeSpecifier = forceFieldIdleModeSpecifier;

        // Set up for the first block 
        setUpForCurrentBlockCondition();

        // Initialize the array that sets the perturbation direction for each block 
        List<int> perturbationDirectionsListThisBlock = GeneratePseudorandomBlockPerturbationDirections(numberOfPertDirectionsUsedThisBlock);

        // Hide the window graph
        windowGraphCanvas.SetActive(false);
    }

    // FixedUpdate is called at a fixed frequency and in synchrony with the physics engine execution
    void FixedUpdate()
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

                // Compute the mapping parameters/constants from Vicon frame to Unity frame (and vice versa)
                defineMappingFromViconFrameToUnityFrameAndBack();

                // Debug mapping
                Vector3 testPointInViconFrame = new Vector3(200.0f, 200.0f, 600.0f);
                Debug.Log("Mapping test: point in Vicon frame is: (" +
                    testPointInViconFrame.x + ", " +
                    testPointInViconFrame.y + ", " +
                    testPointInViconFrame.z + ")");
                Vector3 testPointInUnityFrame = mapPointFromViconFrameToUnityFrame(testPointInViconFrame);
                Debug.Log("Mapping test: point in Unity frame is: (" +
                    testPointInUnityFrame.x + ", " +
                    testPointInUnityFrame.y + ", " +
                    testPointInUnityFrame.z + ")");
                Vector3 testPointRemappedToViconFrame = mapPointFromUnityFrameToViconFrame(testPointInUnityFrame);
                Debug.Log("Mapping test: point remapped to Vicon frame is: (" +
                    testPointRemappedToViconFrame.x + ", " +
                    testPointRemappedToViconFrame.y + ", " +
                    testPointRemappedToViconFrame.z + ")");

                // Do setup that requires the Vicon to Unity mapping 
                setupAfterFrameMappingEstablished();

                // Tell the boundary of stability renderer to draw the boundary of stability.
                // Based on the current working directory (by subject and date), load the functional boundary of 
                // stability based on the stored file within the Excursion folder. 
                // Note that the subject would have to have completed an Excursion test that day, or else the file must be 
                // manually copied into the proper folder location. 
                drawFunctionalBoundaryOfStability();

                /*                // Send the excursion limits and excursion center point to the force field robot (if using)
                                if (communicateWithForceFieldRobot)
                                {
                                    // Send the data needed to specify the force field set at the boundaries of excursion
                                    forceFieldRobotTcpServerScript.SendExcursionLimitCenterPositionInViconFrameToRobot();
                                    forceFieldRobotTcpServerScript.SendExcursionLimitsInViconFrameToRobot();

                                    // Set the robot force field mode to Boundary (boundary at excursion limits)
                                    currentDesiredForceFieldTypeSpecifier = forceFieldAtExcursionBoundary;
                                    forceFieldRobotTcpServerScript.SendForceFieldModeSpecifierToRobot();
                                }*/

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
                    // then proceed by moving to the Waiting For Home state
                    changeActiveState(waitingForHomePositionStateString);

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
                // Send the sync start pulse via photon to the Delsys EMG base station (and other external hardware)
                communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

                // Store the hardware sync signal sent time
                unityFrameTimeAtWhichHardwareSyncSent = Time.time;

                // We've accomplished a minimum delay for sending the sync signal, so 
                // switch to the Waiting for Home state. 
                changeActiveState(waitingForHomePositionStateString);

                Debug.Log("Level manager setup complete");
            }
        }
        else if (currentState == readyForPerturbationStateString)
        {
            // Call the main function for this state, if there is one.
            monitorIfPlayerIsInHomePosition();

            // Compute the player angle in Vicon frame, which we won't use in this state but would like to write to file for later use
            (Vector3 playerPositionInViconFrame, float playerXRelativeToCenterViconFrame, float playerYRelativeToCenterViconFrame,
                float currentPlayerAngleViconFrame) = computePlayerAngleInViconFrame();
            currentPlayerAngleInDegreesViconFrame = currentPlayerAngleViconFrame; // Store in instance variable

            // If the player is no longer at home
            if (isPlayerAtHome == false)
            {
                // Switch back to the waiting for home state. 
                changeActiveState(waitingForHomePositionStateString);
            }

            // store the new Unity frame rate data (e.g. trace trajectory position, on-screen player position + COM position for good measure)
            // for this frame
            storeFrameData();
        }
        else if (currentState == waitingForHomePositionStateString)
        {
            // Test to see if the player is currently in the "Home" area (near center of feet)
            monitorIfPlayerIsInHomePosition();

            // Compute the player angle in Vicon frame, which we won't use in this state but would like to write to file for later use
            (Vector3 playerPositionInViconFrame, float playerXRelativeToCenterViconFrame, float playerYRelativeToCenterViconFrame,
                float currentPlayerAngleViconFrame) = computePlayerAngleInViconFrame();
            currentPlayerAngleInDegreesViconFrame = currentPlayerAngleViconFrame; // Store in instance variable

            // If we're currently waiting for the player to get to the home position after a perturbation
            if (waitingForHomeAfterPerturbationFlag)
            {
                // Continue to update the value tracking maximum COM/player excursion during the perturbation. 
                // This may occur after the perturbation has ended (e.g. the player hasn't reversed their velocity from the pert yet)
                UpdatePlayerMaxExcursionFromHomeViconFrame();

                // See if the force field was active on the last FF call.
                bool forceFieldWasActiveLastCall = forceFieldHighLevelControllerScript.GetAssistiveForceFieldActiveLastCallFlag();
                if (forceFieldWasActiveLastCall == true)
                {
                    forceFieldActivatedDuringPerturbationFlag = true; // then note the FF was activated during the perturbation period
                }
            }

            // store the new Unity frame rate data (e.g. trace trajectory position, on-screen player position + COM position for good measure)
            // for this frame
            storeFrameData();

            // If the player has been at home long enough
            if (timeAtHomeStopwatch.IsRunning && (timeAtHomeStopwatch.ElapsedMilliseconds > millisecondsRequiredAtHomeToBePertReady))
            {
                // If a perturbation was just experienced by the subject and they have just returned home
                string desiredNextState = "";
                if (waitingForHomeAfterPerturbationFlag) // If the player is returning home after a perturbation
                {
                    // Then do end-of-trial tasks
                    // (feedback, store trial data, increment trial number and block number, choose next state)
                    desiredNextState = doTrialCompletedActions();

                    // Set the waiting for home after perturbation flag to false, since the player has arrived at home position.
                    waitingForHomeAfterPerturbationFlag = false;
                }
                else // If we are just starting the task
                {
                    desiredNextState = readyForPerturbationStateString;
                }

                // Then transition to the desired next state 
                // (either Ready for Perturbation if we'll do another trial 
                // or Game Over if we've completed all blocks)
                changeActiveState(desiredNextState);
            }
        }
        else if(currentState == ongoingPerturbationStateString)
        {
            // Test to see if the player is currently in the "Home" area (near center of feet)
            monitorIfPlayerIsInHomePosition();

            // Compute the player angle in Vicon frame, which we won't use in this state but would like to write to file for later use
            (Vector3 playerPositionInViconFrame, float playerXRelativeToCenterViconFrame, float playerYRelativeToCenterViconFrame,
                float currentPlayerAngleViconFrame) = computePlayerAngleInViconFrame();
            currentPlayerAngleInDegreesViconFrame = currentPlayerAngleViconFrame; // Store in instance variable

            // Compute the peak excursion distance from home while the perturbation is ongoing. 
            // Note: this quantity continues to be updated in the Waiting For Home state, IF the
            // waiting for home after perturbation flag is true. This quantity is mainly used to assign points. 
            UpdatePlayerMaxExcursionFromHomeViconFrame();

            // See if the force field was active on the last FF call.
            // Note: this quantity continues to be updated in the Waiting For Home state, IF the
            // waiting for home after perturbation flag is true. Used to assign points.
            bool forceFieldWasActiveLastCall = forceFieldHighLevelControllerScript.GetAssistiveForceFieldActiveLastCallFlag();
            if (forceFieldWasActiveLastCall == true)
            {
                forceFieldActivatedDuringPerturbationFlag = true; // then note the FF was activated during the perturbation period
            }

            // store the new Unity frame rate data (e.g. trace trajectory position, on-screen player position + COM position for good measure)
            // for this frame
            storeFrameData();
        }
        else if (currentState == gameOverStateString)
        {
            //do nothing in the game over state
        }
    }


    private void UpdatePlayerMaxExcursionFromHomeViconFrame()
    {
        // Get the player's current position relative to center of BoS in Vicon frame
        Vector3 vectorToPlayerFromCenterOfBosInViconFrame = mapPointFromUnityFrameToViconFrame(player.transform.position) -
            new Vector3(centerOfBaseOfSupportXPosViconFrame, centerOfBaseOfSupportYPosViconFrame, 0.0f);

        // Set the z-axis difference equal to 0, since the z-axis value is meaningless
        vectorToPlayerFromCenterOfBosInViconFrame.z = 0.0f;

        // Compute and store player distance from center of BoS ("home") in Vicon frame [mm]
        float distancePlayerToCenterOfBos = vectorToPlayerFromCenterOfBosInViconFrame.magnitude;

        // If current distance from home is greater than any previous value
        if(distancePlayerToCenterOfBos > maxExcursionFromHomeDuringPerturbationViconFrameInMm)
        {
            // Update the maximum excursion value
            maxExcursionFromHomeDuringPerturbationViconFrameInMm = distancePlayerToCenterOfBos;
        }
    }


    private string doTrialCompletedActions()
    {
        // If the trial was completed, increment the trial number and update the state accordingly

        // Assign a score to the trial, provide visual feedback
        assignTrialScoreAndProvideFeedback();

        //If the trial is completed, mark the end-time for the trial 
        currentTrialEndTime = Time.time;

        // Store the trial excursion distance in a List for plotting
        float normalizedPerturbedDistance = maxExcursionFromHomeDuringPerturbationViconFrameInMm / 
            GetBaselineExcursionDistanceInRelevantDirectionThisTrial();
        normalizedPerturbedDistanceAllTrials.Add(normalizedPerturbedDistance);

        // Store the trial data for the completed trial
        storeTrialData();

        // Reset variables for the next trial 
        pointsEarnedByPlayerThisTrial = 0.0f;
        maxExcursionFromHomeDuringPerturbationViconFrameInMm = 0.0f;
        forceFieldActivatedDuringPerturbationFlag = false;

        //increment the trial. This function changes the state if appropriate.
        string nextStateString = incrementTrialNumberAndManageState();

        // Return the next state 
        return nextStateString;

    }




    // BEGIN: Mapping from Vicon to Unity frame and back functions*********************************************************************************

    private void defineMappingFromViconFrameToUnityFrameAndBack()
    {
        // Choose the first part of the scaling factor, based on the base of support aspect ratio
        float baseOfSupportWidthViconFrame = Mathf.Abs(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        float baseOfSupportHeightViconFrame = Mathf.Abs(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);
        float baseOfSupportAspectRatio = (baseOfSupportWidthViconFrame / baseOfSupportHeightViconFrame);

        // Get the screen height and width in Unity frame
        float screenWidthInUnityCoords = (mainCamera.ViewportToWorldPoint(new Vector3(1, 0, 0)) - mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0))).x;
        float screenHeightInUnityCoords = (mainCamera.ViewportToWorldPoint(new Vector3(0, 1, 0)) - mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0))).y;
        float screenAspectRatioInUnityCoords = screenWidthInUnityCoords / screenHeightInUnityCoords;
        float ratioOfBaseOfSupportToScreenAspectRatios = baseOfSupportAspectRatio / screenAspectRatioInUnityCoords;

        // Also, compute and store the rightwardSign and forwardSign values, which indicate how Vicon frame and Unity frame are related. 
        rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        forwardsSign = rightwardsSign;




        // the scaling will depend on this ratio of aspect ratios
        if (ratioOfBaseOfSupportToScreenAspectRatios >= 1.0f) //if the ratio is greater than 1, the base of support
                                                           //is relatively wide, so we normalize by that dimension
        {

            Debug.Log("Mapping from Vicon to Unity based on width of base of support (to fit on screen)");

            // Store the "shortest dimension" of the base of support, i.e. the dimension that can certainly fit on-screen if the other one does.
            // This will allow us to scale the "center dot"/home area diameter
            shortestDimensionofBaseOfSupportViconUnits = baseOfSupportHeightViconFrame;

            // We want to scale the shape to take up a certain percent of the screen
            fitToScreenScalingFactor = baseOfSupportaPercentOfScreenToFill * screenWidthInUnityCoords;

            // We always normalize the distance excursion from center of BoS in Vicon frame by the 
            // same dimension (either width or height). This means that we can create a uniform mapping function. 
            // In this case, use base of support width.
            baseOfSupportDimensionScaler = 2.0f / baseOfSupportWidthViconFrame;
        }
        else
        {
            Debug.Log("Mapping from Vicon to Unity based on height of base of support (to fit on screen)");

            // Store the "shortest dimension" of the base of support, i.e. the dimension that can certainly fit on-screen if the other one does.
            // This will allow us to scale the "center dot"/home area diameter
            shortestDimensionofBaseOfSupportViconUnits = baseOfSupportWidthViconFrame;

            // We also want to scale the shape to take up a certain percent of the screen
            fitToScreenScalingFactor = baseOfSupportaPercentOfScreenToFill * screenHeightInUnityCoords;

            // We always normalize the distance excursion from center of BoS in Vicon frame by the 
            // same dimension (either width or height). This means that we can create a uniform mapping function. 
            // In this case, use base of support height.
            baseOfSupportDimensionScaler = 2.0f / baseOfSupportHeightViconFrame;

        }

        Debug.Log("Fit-to-screen scaling factor is: " + fitToScreenScalingFactor);

    }


    public override String GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    // THE MAPPING FUNCTION from Vicon frame to Unity frame for the trajectory-tracing task (this task). 
    // Key point: the center of the base of support maps to (0,0) in Unity for this task.
    // Key point 2: the x- and y-axis coordinates are multiplied by -1 in the mapping, 
    // because the Unity frame is defined to be rotated 180 degrees relative to the Vicon frame.
    // Why the flip? +x-axis is to the subject's left in Vicon frame (based on wand placement), but
    // we want +x in Unity to be right. 

    // Really, we should multiply by a rotation vector to account for flipping of axes (or misalignment, although we assume perfect alignment), 
    // then multiply by a scaling matrix (identity matrix times some constant). 
    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        // Carry out the mapping from Vicon frame to Unity frame
        float baseOfSupportWidthViconFrame = Mathf.Abs(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        float baseOfSupportHeightViconFrame = Mathf.Abs(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);

        float pointInUnityFrameX = rightwardsSign * 0.5f * fitToScreenScalingFactor * baseOfSupportDimensionScaler
            * (pointInViconFrame.x - centerOfBaseOfSupportXPosViconFrame);

        // Note: we also divide the forward/backward excursion by half the base of support WIDTH, not height. 
        // This gives us a uniform scaling along both dimensions.
        float pointInUnityFrameY = forwardsSign * 0.5f * fitToScreenScalingFactor * baseOfSupportDimensionScaler
            * (pointInViconFrame.y - centerOfBaseOfSupportYPosViconFrame);

        //return the point in Unity frame
        return new Vector3(pointInUnityFrameX, pointInUnityFrameY, player.transform.position.z);
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // Carry out the mapping from Unity frame to Vicon frame
        float baseOfSupportWidthViconFrame = Mathf.Abs(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        float baseOfSupportHeightViconFrame = Mathf.Abs(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);

        float pointInViconFrameX = ((1.0f) / (rightwardsSign * 0.5f * fitToScreenScalingFactor * baseOfSupportDimensionScaler)) * 
            pointInUnityFrame.x + centerOfBaseOfSupportXPosViconFrame;

        float pointInViconFrameY = ((1.0f) / (forwardsSign * 0.5f * fitToScreenScalingFactor * baseOfSupportDimensionScaler)) *
            pointInUnityFrame.y + centerOfBaseOfSupportYPosViconFrame;

        //return the point in Unity frame
        return new Vector3(pointInViconFrameX, pointInViconFrameY, player.transform.position.z);
    }


    // Return the current desired force field type (only applicable if coordinating Unity with RobUST). 
    // This will be sent to the RobUST Labview script via TCP. 
    // Default value: let the default value be the Idle mode specifier.
    public override string GetCurrentDesiredForceFieldTypeSpecifier()
    {
        return currentDesiredForceFieldTypeSpecifier;
    }

    private (float, float) convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(float widthXAxisViconFrame, float heightYAxisViconFrame)
    {
        float baseOfSupportWidthViconFrame = Mathf.Abs(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        float baseOfSupportHeightViconFrame = Mathf.Abs(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);

        float widthInUnityFrameX =  0.5f * fitToScreenScalingFactor * baseOfSupportDimensionScaler * widthXAxisViconFrame;

        float heightInUnityFrameY =  0.5f * fitToScreenScalingFactor * baseOfSupportDimensionScaler * heightYAxisViconFrame;

        return (widthInUnityFrameX, heightInUnityFrameY);
    }

    // Converts an angle, measured CCW from the +x-axis, in Unity frame to Vicon frame, or vice versa. 
    // Note: Vicon has a frame, and we construct these 2D Unity games such that the Unity +x-axis is 
    // aligned with the Vicon frame or 180 degrees off (whichever makes a rightward motion appear to go right on the screen)
    private float convertAngleFromViconFrameToUnityFrameOrViceVersa(float angleInUnityOrViconFrame)
    {
        float angleInTheOtherFrame = angleInUnityOrViconFrame + 180;
        if (angleInTheOtherFrame > 360.0f)
        {
            angleInTheOtherFrame = angleInTheOtherFrame - 360.0f;
        }

        return angleInTheOtherFrame;
    }


    // END: Mapping from Vicon to Unity frame and back functions*********************************************************************************




    // BEGIN: Other public functions*********************************************************************************

    public override bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }

    public bool IsLevelManagerInReadyForPerturbationState()
    {
        // Return if the level manager is in the Ready for Perturbation state (true) or not (false).
        // This function is called by the perturbation controller to see if a perturbation can begin.
        return (currentState == readyForPerturbationStateString);
    }

    public Vector3 GetCurrentDesiredPerturbationDirectionAsUnitVectorInViconFrame()
    {
        // Return the current desired perturbation direction for this trial 
        // as a unit vector. 
        // We return it in Vicon frame, so we must take sign between Vicon and Unity frame into consideration.
        Vector3 perturbationDirectionThisTrialAsUnitVectorViconFrame = new Vector3(0.0f, 0.0f, 0.0f);

        // Let 1.0 = forward, 2.0 = right, 3.0 = backwards, 4.0 = left
        if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationForwardValue)
        {
            perturbationDirectionThisTrialAsUnitVectorViconFrame = new Vector3(0.0f, forwardsSign * 1.0f, 0.0f);
        }else if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationRightValue)
        {
            perturbationDirectionThisTrialAsUnitVectorViconFrame = new Vector3(rightwardsSign * 1.0f, 0.0f, 0.0f);
        }
        else if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationBackwardsValue)
        {
            perturbationDirectionThisTrialAsUnitVectorViconFrame = new Vector3(0.0f, -forwardsSign * 1.0f, 0.0f);
        }
        else if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationLeftValue)
        {
            perturbationDirectionThisTrialAsUnitVectorViconFrame = new Vector3(-rightwardsSign * 1.0f, 0.0f, 0.0f);
        }

        // Return the desired perturbation direction unit vector in Vicon frame
        return perturbationDirectionThisTrialAsUnitVectorViconFrame;
    }

    public void PerturbationHasStartedEvent()
    {
        // This function should only be called when a perturbation has started (so, 
        // we know the level manager should be in the Ready for Perturbation state). 
        // We transition to the ongoing perturbation state.
        changeActiveState(ongoingPerturbationStateString);
    }

    public void PerturbationHasEndedEvent()
    {
        // This function should only be called when a perturbation has ended. 
        // Transition from the ongoingPerturbation state (presumably our current state)
        // to waitingForHome state.
        changeActiveState(waitingForHomePositionStateString);
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

    // END: Other public functions*********************************************************************************




    // BEGIN: Set-up only functions*********************************************************************************




    /************************

    Function: setUpForCurrentBlockCondition()

    Parameters: none

    Returns: none

    Description: Creates a list of travel directions per trial, either clockwise or counterclockwise. 
                 Set up so that the direction alternates from trial to trial. 

    Notes: Called only once, a setup function

    **************************/
    private void setUpForCurrentBlockCondition()
    {

        if (numberOfBlocks[currentBlockNumber] == mixedForwardBackwardBlockSpecifier)
        {
            numberOfPertDirectionsUsedThisBlock = new int[] { 1, 3};
        }
        else if (numberOfBlocks[currentBlockNumber] == mixedAllFourDirectionsBlockSpecifier)
        {
            numberOfPertDirectionsUsedThisBlock = new int[] { 1, 2, 3, 4};
        }
        else
        {
            numberOfPertDirectionsUsedThisBlock = new int[] { 1, 3 };
        }

        // Compute the number of trials this block, which is = # of trials per pert. direction * # of directions
        numTrialsPerBlock = numberOfTrialsPerDirectionPerBlock * (uint) numberOfPertDirectionsUsedThisBlock.Length;

        Debug.Log("Creating pert directions per trial List.");
        perturbationDirectionsPerTrial = GeneratePseudorandomBlockPerturbationDirections(numberOfPertDirectionsUsedThisBlock);
        Debug.Log("Size of perturbation direction list = " + perturbationDirectionsPerTrial.Count);
        //dataCollectorScript.setFileNamesForCurrentBlockTrialAndFrameData();

    }



    /************************

    Function: GeneratePseudorandomBlockPerturbationDirections()

    Parameters: none

    Returns: none

    Description: Creates a list of perturbation directions per trial, choosing an equal number of trials 
                 per direction used in this block, then pseudorandomizing the order.

    Notes: Called at the start of each block by setUpForCurrentBlockCondition(). A setup function

    **************************/
    private List<int> GeneratePseudorandomBlockPerturbationDirections(int[] pertDirectionsToUseThisBlockType)
    {
        // Trial position indexes
        List<int> listOfPerturbationDirectionsPerTrial = new List<int>();

        // Instantiate the trial positions
        // For the number of trials we need in each bag position/direction
        for (int perDirectionTrialIndex = 0; perDirectionTrialIndex < numberOfTrialsPerDirectionPerBlock; perDirectionTrialIndex++)
        {
            // for each perturbation direction being used
            for (int pertDirectionsToUseThisBlockIndex = 0; pertDirectionsToUseThisBlockIndex < pertDirectionsToUseThisBlockType.Length; pertDirectionsToUseThisBlockIndex++)
            {
                // Add a direction specifier to the list (we do this operation n = # of trials times)
                listOfPerturbationDirectionsPerTrial.Add(pertDirectionsToUseThisBlockType[pertDirectionsToUseThisBlockIndex]);
            }
        }

        // Now we shuffle the list of perturbation directions 
        // This was from a forum, based on the Fisher-Yates shuffle
        int n = listOfPerturbationDirectionsPerTrial.Count;
        while (n > 0)
        {
            n--;
            int k = randomNumberGenerator.Next(n + 1);
            int temp = listOfPerturbationDirectionsPerTrial[k];
            listOfPerturbationDirectionsPerTrial[k] = listOfPerturbationDirectionsPerTrial[n];
            listOfPerturbationDirectionsPerTrial[n] = temp;
            Debug.Log("Trial in index " + n + " has value " + temp);
        }
        // Return the list 
        return listOfPerturbationDirectionsPerTrial;
    }


    private void setupAfterFrameMappingEstablished()
    {
        // set the central circle radius in Vicon units
        circleInCenterOfTrajectoryDiameterViconUnits = shortestDimensionofBaseOfSupportViconUnits *
            circleCenterRadiusAsPercentOfBaseOfSupportShortDimension;
        Debug.Log("Circle center: diameter in Vicon units is " + circleInCenterOfTrajectoryDiameterViconUnits);

        //convert the radius in Vicon units to Unity units
        (circleInCenterOfTrajectoryDiameterUnityUnits, _) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(circleInCenterOfTrajectoryDiameterViconUnits, 0.0f);
        Debug.Log("Circle center: diameter in Unity units " + circleInCenterOfTrajectoryDiameterUnityUnits);

        // Save the circle center position into its own variable for readability.
        circleCenterPosition = circleCenter.transform.position;

        //Set the radius of the circle center / "home" area.
        circleCenter.transform.localScale = new Vector3(circleInCenterOfTrajectoryDiameterUnityUnits,
            circleInCenterOfTrajectoryDiameterUnityUnits,
            circleCenter.transform.localScale.z);
    }





    private void setFrameAndTrialDataNaming()
    {
        // 1.) Frame data naming
        // A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[] { };
        csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE","COM_POS_X","COM_POS_Y", "COM_POS_Z", "IS_COM_POS_FRESH_FLAG",
           "MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER", "BLOCK_NUMBER", "TRIAL_NUMBER",
           "PLAYER_ANGLE_RADIANS", "PLAYER_POS_X", "PLAYER_POS_Y", "PLAYER_ANGLE_RADIANS_VICON_FRAME", "PLAYER_POS_VICON_FRAME_X",
           "PLAYER_POS_VICON_FRAME_Y", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT", "IS_PLAYER_AT_HOME_FLAG"};



        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // 2.) Trial data naming
        // A string array with all of the header names
        string[] csvTrialDataHeaderNames = new string[]{"BLOCK_NUMBER", "TRIAL_NUMBER", "CIRCLE_CENTER_POS_X_UNITY_FRAME",
            "CIRCLE_CENTER_POS_Y_UNITY_FRAME", "CIRCLE_CENTER_DIAMETER_VICON_MM", "CIRCLE_CENTER_DIAMETER_UNITY",
            "LEFT_BASEOFSUPPORT_VICON_POS_X", "RIGHT_BASE_OF_SUPPORT_VICON_POS_X", "FRONT_BASEOFSUPPORT_VICON_POS_Y",
            "BACK_BASEOFSUPPORT_VICON_POS_Y", "BASE_OF_SUPPORT_CENTER_X",
            "BASE_OF_SUPPORT_CENTER_Y", "EXCURSION_DISTANCE_DIR_0_VICON_MM", "EXCURSION_DISTANCE_DIR_1_VICON_MM",
            "EXCURSION_DISTANCE_DIR_2_VICON_MM", "EXCURSION_DISTANCE_DIR_3_VICON_MM", "EXCURSION_DISTANCE_DIR_4_VICON_MM",
            "EXCURSION_DISTANCE_DIR_5_VICON_MM", "EXCURSION_DISTANCE_DIR_6_VICON_MM", "EXCURSION_DISTANCE_DIR_7_VICON_MM",
            "UNITY_VICON_MAPPING_FCN_BOS_SCREEN_ASPECT_RATIO_SCALER","UNITY_VICON_MAPPING_FCN_FILL_SCREEN_PERCENTAGE_SCALER",
            "DIMENSION_BOS_USED_TO_SCALE_MAPPING_VICON_UNITS_MM", "UNITY_VICON_MAPPING_FCN_RIGHTWARD_SIGN_AXIS_FLIP",
            "UNITY_VICON_MAPPING_FCN_FORWARD_SIGN_AXIS_FLIP", "STIMULATION_STATUS", "TRIAL_START_TIME_SECONDS","TRIAL_END_TIME_SECONDS",
            "TRIAL_DURATION_SECONDS", "PERT_DIRECTION", "MAX_EXCURSION_DIST_DUE_TO_PERT_MM", 
            "BASELINE_EXCURSION_DIST_TO_EARN_POINTS_THIS_TRIAL_MM",
            "FORCE_FIELD_ACTIVATED_DURING_PERT_FLAG","POINTS_EARNED_THIS_TRIAL"};

        //tell the data recorder what the trial data CSV headers will be
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvTrialDataHeaderNames);

        // 3.) Data subdirectory naming for trajectory tracing data
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



    private void drawFunctionalBoundaryOfStability()
    {

        //tell the functional boundary of stability drawing object to draw the BoS, if the object is active.
        boundaryOfStabilityRendererScript.renderBoundaryOfStability();
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
            if (currentState == waitingForHomePositionStateString)
            {
                exitWaitingForHomeState();
            }
            else if (currentState == readyForPerturbationStateString)
            {
                exitReadyForPerturbationState();
            }
            else if (currentState == waitingForSetupStateString)
            {
                exitWaitingForSetupState();
            }
            else if (currentState == ongoingPerturbationStateString)
            {
                exitOngoingPerturbationState();
            }

            //then call the entry function for the new state
            if (newState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            if (newState == waitingForHomePositionStateString)
            {
                enterWaitingForHomeState();
            }
            else if (newState == readyForPerturbationStateString)
            {
                enterReadyForPerturbationState();
            }
            else if (newState == ongoingPerturbationStateString)
            {
                enterOngoingPerturbationState();
            }
            else if (newState == gameOverStateString)
            {
                enterGameOverState();
            }
        }
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

    private void enterWaitingForEmgReadyState()
    {
        //set the current state
        currentState = waitingForEmgReadyStateString;
    }

    private void exitWaitingForEmgReadyState()
    {
        // do nothing for now
    }

    private void enterWaitingForHomeState()
    {

        Debug.Log("Changing to Waiting for Home state");
        //change the current state to the Waiting For Home state
        currentState = waitingForHomePositionStateString;

        //set the center circle color to the color indicating the player should move there
        circleCenter.GetComponent<Renderer>().material.color = Color.red;
    }


    private void exitWaitingForHomeState()
    {
        //set the center circle color to its default
        circleCenter.GetComponent<Renderer>().material.color = Color.blue;
    }

    private void enterReadyForPerturbationState()
    {
        Debug.Log("Changing to Ready For Perturbation state.");

        //change the current state to the Ready For Perturbation state
        currentState = readyForPerturbationStateString;
    }

    private void exitReadyForPerturbationState()
    {
        // do nothing (for now)
    }

    private void enterOngoingPerturbationState()
    {
        Debug.Log("Changing to Ongoing Perturbation state.");

        // Set the trial start time. 
        // Each trial can be denoted as starting when the perturbation begins
        currentTrialStartTime = Time.time;

        //change the current state to the Ongoing Perturbation state
        currentState = ongoingPerturbationStateString;
    }

    private void exitOngoingPerturbationState()
    {
        // Set the "waiting for home after perturbation" flag, indicating that the 
        // subject has experienced a perturbation and we're waiting for them to get to the
        // home position before ending the trial
        waitingForHomeAfterPerturbationFlag = true;
    }

    private void enterGameOverState()
    {
        Debug.Log("Changing to Game Over state");

        // the block and task are over, so send a stop signal to the external hardware 
        // (only if using external hardware)
        if (syncingWithExternalHardwareFlag)
        {
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();
        }

        // Write the stored data to file
        tellDataRecorderToWriteStoredDataToFile();

        // Plot the trial error data vs. trial number in the plotting window
        float plottingWindowTimeStep = 1.0f; // 1 because it is trial number
        windowGraphCanvas.SetActive(true); // show the window graph
        windowGraphPlottingScript.PlotDataPointsOnWindowGraph(normalizedPerturbedDistanceAllTrials, plottingWindowTimeStep,
            perturbationDirectionsPerTrial.ToList());

        //change the current state to the Game Over state
        currentState = gameOverStateString;

    }

    // END: State machine state-transitioning functions *********************************************************************************






    // BEGIN: Main loop functions ( e.g. called from Update() )*********************************************************************************

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





    private (Vector3, float, float, float) computePlayerAngleInViconFrame()
    {
        //convert to Vicon frame (really, could just use (COM position = player position), but we want the game to work with the keyboard for testing)
        Vector3 playerPositionInViconFrame = mapPointFromUnityFrameToViconFrame(player.transform.position);
        float playerYRelativeToCenterViconFrame = playerPositionInViconFrame.y - centerOfBaseOfSupportYPosViconFrame;
        float playerXRelativeToCenterViconFrame = playerPositionInViconFrame.x - centerOfBaseOfSupportXPosViconFrame;
        float currentPlayerAngleViconFrame = Mathf.Atan2(playerYRelativeToCenterViconFrame, playerXRelativeToCenterViconFrame) * (180.0f / Mathf.PI);

        // Vector3 playerPositionInUnityFrame = getUnityCoordinateForEllipseTrajectoryGivenAngleFromXAxis(currentPlayerAngleViconFrame);

        //convert the player angle to range from 0 to 359.999 degrees.
        float degreesInCircle = 360.0f;
        if (currentPlayerAngleViconFrame < 0)
        {
            currentPlayerAngleViconFrame = degreesInCircle + currentPlayerAngleViconFrame; //360 is degrees in a circle
        }

        return (playerPositionInViconFrame, playerXRelativeToCenterViconFrame, playerYRelativeToCenterViconFrame, currentPlayerAngleViconFrame);
    }





    private (bool, bool) isPlayerInHomeCircle()
    {
        // Get distance of player from center in Unity frame
        float playerDistanceFromCircleCenter = Vector3.Distance(player.transform.position,
            circleCenter.transform.position);

        // See if the player is at home based on distance from the center in the Unity frame (have they left the home circle?)
        bool isPlayerCurrentlyAtHome = (playerDistanceFromCircleCenter < (0.5f * circleInCenterOfTrajectoryDiameterUnityUnits));

        // If the player is no longer at home but just was, then they have just left home
        bool hasPlayerJustLeftHome = ((isPlayerCurrentlyAtHome == false) && (isPlayerAtHome == true));

        return (isPlayerCurrentlyAtHome, hasPlayerJustLeftHome);
    }


    private void assignTrialScoreAndProvideFeedback()
    {
        // Ensure that the points earned by the player this trial is reset to zero before computing it.
        pointsEarnedByPlayerThisTrial = 0.0f;

        // If the player earned points this trial, compute how many
        float forceFieldNotActivatedPoints = 0.0f;
        // Award half the maximum points if the FF was NOT activated
        if(forceFieldActivatedDuringPerturbationFlag == false)
        {
            forceFieldNotActivatedPoints = maximumPointsEarnedPerTrial / 2.0f;
        }
        // Award the other half of points based on distance moved
        float distanceCenterOfBosToLimitInRelevantDirectionThisTrialInMm = GetBaselineExcursionDistanceInRelevantDirectionThisTrial();
        float distancePointsEarned = 0.5f * maximumPointsEarnedPerTrial * 
            (1.0f - (maxExcursionFromHomeDuringPerturbationViconFrameInMm/ distanceCenterOfBosToLimitInRelevantDirectionThisTrialInMm));
        // If distance points earned were less than zero, assign zero
        if(distancePointsEarned < 0.0f)
        {
            distancePointsEarned = 0.0f;
        }
        // Sum two types of points earned
        pointsEarnedByPlayerThisTrial = forceFieldNotActivatedPoints + distancePointsEarned;

        Debug.Log("Player max excursion this trial: " + maxExcursionFromHomeDuringPerturbationViconFrameInMm);
        Debug.Log("Player relevant baseline excursion this trial: " + distanceCenterOfBosToLimitInRelevantDirectionThisTrialInMm);

        //Add the points earned this trial to the total for the block
        totalPointsEarnedByPlayerThisBlock += pointsEarnedByPlayerThisTrial;

        // Update the total points displayed to the user 
        feedbackManagerScript.SetTotalPointsEarned(totalPointsEarnedByPlayerThisBlock);

        // Send a trial-specific feedback string to be temporarily displayed to the user
        if (pointsEarnedByPlayerThisTrial > 0.0f)
        {
            feedbackManagerScript.DisplayTrialSpecificTextFeedback("Great! Points earned: +" + pointsEarnedByPlayerThisTrial.ToString("0.0"));
        }
        else
        {
            feedbackManagerScript.DisplayTrialSpecificTextFeedback("No points earned.");
        }
    }

    private float GetBaselineExcursionDistanceInRelevantDirectionThisTrial()
    {
        // Recall, the excursion distances are stored in the order: right, front right, 
        // forwards, front left, left, back left, back, back right.
        float baselineExcursionDistanceThisTrial = -1.0f;
        if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationForwardValue)
        {
            baselineExcursionDistanceThisTrial = excursionDistancesPerDirectionViconFrame[2]; // Store the forward baseline excursion distance
        }
        else if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationRightValue)
        {
            baselineExcursionDistanceThisTrial = excursionDistancesPerDirectionViconFrame[0]; // Store the forward baseline excursion distance
        }
        else if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationBackwardsValue)
        {
            baselineExcursionDistanceThisTrial = excursionDistancesPerDirectionViconFrame[6]; // Store the forward baseline excursion distance
        }
        else if (perturbationDirectionsPerTrial[currentTrialNumber] == perturbationLeftValue)
        {
            baselineExcursionDistanceThisTrial = excursionDistancesPerDirectionViconFrame[4]; // Store the forward baseline excursion distance
        }
        return baselineExcursionDistanceThisTrial;
    }


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
                // Then the next state should be the Ready for Perturbation state, 
                // since the subject is back in the home position and we're starting a new block
                desiredNextStateString = readyForPerturbationStateString;
            }
            else // If we've already completed all desired blocks
            {
                // Then the task is complete. Enter Game Over state.
                desiredNextStateString = gameOverStateString;
            }
        }
        else // If we still have more trials in this block
        {
            // Then the next state should be the Ready for Perturbation state, 
            // since the subject is back in the home position and we're starting a new trial
            desiredNextStateString = readyForPerturbationStateString;
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

        // Get the time the hardware sync was sent to the EMGs
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

        // Retrieve the player angle
        float currentPlayerAngleUnityFrame = Mathf.Atan2(player.transform.position.y, player.transform.position.x) * (180.0f / Mathf.PI);
        frameDataToStore.Add(currentPlayerAngleUnityFrame);

        // Retrieve the player position
        frameDataToStore.Add(player.transform.position.x);
        frameDataToStore.Add(player.transform.position.y);

        // Get the player position in Vicon coordinates, as the player position could be lagging the most recent COM data
        // Also store the player angle in Vicon frame (again, measured from that frame's +x-axis CCW).
        Vector3 playerPositionInViconFrame = mapPointFromUnityFrameToViconFrame(player.transform.position);
        frameDataToStore.Add(currentPlayerAngleInDegreesViconFrame);
        frameDataToStore.Add(playerPositionInViconFrame.x);
        frameDataToStore.Add(playerPositionInViconFrame.y);

        // Store the logical flags relevant to this task (likely, state is the only one, but think on it)
        float currentStateFloat = -1.0f;
        if (currentState == waitingForSetupStateString)
        {
            currentStateFloat = 0.0f;
        }
        else if (currentState == waitingForHomePositionStateString)
        {
            currentStateFloat = 1.0f;
        }
        else if (currentState == readyForPerturbationStateString)
        {
            currentStateFloat = 2.0f;
        }
        else if (currentState == ongoingPerturbationStateString)
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

        // Also, store the "Player at Home" flag when we're in the Waiting for Home state
        frameDataToStore.Add(Convert.ToSingle(isPlayerAtHome));

        //Send the data to the general data recorder. It will be stored in memory until it is written to a CSV file.
        generalDataRecorderScript.storeRowOfFrameData(frameDataToStore);

    }


    // This function stores a single trial's summary data by sending a "row" of data to the general data recorder. 
    private void storeTrialData()
    {

        // the list that will store the data
        List<float> trialDataToStore = new List<float>();

        // Get the trial # and block #
        float blockNumber = (float)currentBlockNumber;
        trialDataToStore.Add(blockNumber);
        float trialNumber = (float)currentTrialNumber; //we only have trials for now. Should we implement a block format for excursion?
        trialDataToStore.Add(trialNumber);

        // Store the trajectory center position. (0,0) in Unity frame, but could theoretically move, shifting the 
        // center of the on-screen circular trajectory in the process. 
        trialDataToStore.Add(circleCenterPosition.x);
        trialDataToStore.Add(circleCenterPosition.y);

        // Get the size of the center circle, which determines when the player is "At Home" and cannot push the trace indicator. 
        // If the player is outside of this circle radius, they can push the trace indicator.
        trialDataToStore.Add(circleInCenterOfTrajectoryDiameterViconUnits);
        trialDataToStore.Add(circleInCenterOfTrajectoryDiameterUnityUnits);

        // Get edges of base of support
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
            float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();
        Vector3 centerOfBaseOfSupportViconCoords = new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos) / 2.0f, (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f, player.transform.position.z);
        trialDataToStore.Add(leftEdgeBaseOfSupportXPos);
        trialDataToStore.Add(rightEdgeBaseOfSupportXPos);
        trialDataToStore.Add(frontEdgeBaseOfSupportYPos);
        trialDataToStore.Add(backEdgeBaseOfSupportYPos);
        trialDataToStore.Add(centerOfBaseOfSupportViconCoords.x); // Also stored in trial data (so redundant), but now can analyze frame data on its own
        trialDataToStore.Add(centerOfBaseOfSupportViconCoords.y); // Also stored in trial data (so redundant), but now can analyze frame data on its own

        // Store the read-in excursion distances along each axis. This gives us confidence about which file was read during analysis of the data.
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[0]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[1]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[2]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[3]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[4]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[5]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[6]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[7]);

        //store Vicon to Unity (and back) mapping function variables
        trialDataToStore.Add(baseOfSupportDimensionScaler);
        trialDataToStore.Add(fitToScreenScalingFactor);
        trialDataToStore.Add(shortestDimensionofBaseOfSupportViconUnits);
        trialDataToStore.Add(rightwardsSign);
        trialDataToStore.Add(forwardsSign);

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

        // Add trial start time, stop time, and duration
        trialDataToStore.Add(currentTrialStartTime);
        trialDataToStore.Add(currentTrialEndTime);
        trialDataToStore.Add(currentTrialEndTime - currentTrialStartTime);

        // Add the perturbation direction specifier
        int currentTrialPertDirectionSpecifier = perturbationDirectionsPerTrial[currentTrialNumber];
        trialDataToStore.Add((float) currentTrialPertDirectionSpecifier);

        // Store distance of max excursion this trial during/after the perturbation
        trialDataToStore.Add(maxExcursionFromHomeDuringPerturbationViconFrameInMm);

        // Store perturbation COM distance traveled
        trialDataToStore.Add(GetBaselineExcursionDistanceInRelevantDirectionThisTrial());

        // Store the flag indicating if the force field was activated this trial (true) or not (false)
        trialDataToStore.Add(Convert.ToSingle(forceFieldActivatedDuringPerturbationFlag));

        // Store points earned, if using
        trialDataToStore.Add(pointsEarnedByPlayerThisTrial);

        //send all of this trial's summary data to the general data recorder
        generalDataRecorderScript.storeRowOfTrialData(trialDataToStore.ToArray());
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

        // If we're using EMG data (if the size of the EMG data is not zero)
        int numberEmgSamplesStored = generalDataRecorderScript.GetNumberOfEmgDataRowsStored();
        if (numberEmgSamplesStored != 0)
        {
            Debug.Log("Writing EMG data to file. EMG data has num. samples: " + numberEmgSamplesStored);
            // Tell the general data recorder to write the EMG data to file
            generalDataRecorderScript.writeEmgDataToFile();
        }
    }

    // END: Data storage functions **************************************************************************************

}
