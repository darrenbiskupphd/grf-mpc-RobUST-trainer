using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below
using Debug = UnityEngine.Debug;

public class LevelManagerDifferentHeightsReaching : LevelManagerScriptAbstractClass
{
    // Start is called before the first frame update

    // Which side to conduct the task on
    public WhichSideSelectEnum whichSideToPerformTaskSelector;

    // Task name
    private const string thisTaskNameString = "ReachingDifferentHeights";

    // center of mass manager
    public ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    private bool centerOfMassManagerReadyStatus = false; // whether or not the COM manager is ready to dispense Vicon data (initialize as no = false).

    // Startup support script
    public LevelManagerStartupSupportScript levelManagerStartupSupportScript;
    private bool startupSupportScriptCompleteFlag = false;

    // Vive tracker data manager
    public ViveTrackerDataManager viveTrackerDataManagerScript;
    private bool viveTrackerDataInitializedFlag = false;

    // Boundary of stability renderer (in ground plane)
    public RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript;

    // Transformation matrix from Vicon frame to reference tracker frame. Critical for placing tracked objects in Unity.
    public LoadTransformationViconToReferenceTracker loadTransformationViconToReferenceTrackerScript;
    private Matrix4x4 transformationViconToReferenceTracker; // the transformation matrix from Vicon frame to reference tracker frame.
    private Matrix4x4 transformationReferenceTrackerToViconFrame; // the transformation matrix from Vicon frame to reference tracker frame.
    private string subdirectoryViconTrackerTransformString; // the path to the folder containing the transformation matrix from Vicon frame to reference tracker frame.

    // The transformation from the Vive reference tracker to Unity frame 
    private Matrix4x4 transformationReferenceTrackerToUnity; // converts from the right-handed coordinate frame of the tracker to the Unity frame

    // Whether or not we must wait for the experimenter to reposition the player camera at the home position before 
    // starting the Instructions (and experiment in general)
    public bool waitForToggleHomeToStartInstructions;
    // Set the last known value of the player to home toggle
    public bool playerCameraToHmdPosToggle;
    private bool lastValueCameraToHomeToggle;
    private bool hmdToggledToHomeByExperimenter = false; // whether or not the experimenter has toggled the player home this run
    private Stopwatch delayToggleCameraViewToHmdPosStopwatch = new Stopwatch();
    private float delayToggleHomeInMilliseconds = 1000.0f; // a delay between when the subject hits toggle home IN THIS SCRIPT and when the player camera is 
                                                           // repositioned. It seems this is necessary.

    // Save the HMD "home" or "neutral stance" position, which is the position when the toggle home occurs
    private Vector3 hmdHomePositionInViconFrame;

    // RobUST force field type specifier
    private const string forceFieldIdleModeSpecifier = "I";
    private const string forceFieldAtExcursionBoundary = "B";
    private string currentDesiredForceFieldTypeSpecifier;

    // Establishing how the ball angles will be defined, relative to the Vicon frame axes. 
    // DEFAULT CASE **************************************************************************
    // The default ball angles proceeding CCW from the +x-axis, with the assumption that the person is 
    // facing the +y-axis with the +x-axis rightwards. In this case,
    // our ball definitions are 0 = rightwards = +x-axis; 90 = forwards = +y-axis. 
    // CASE DEFAULT: set rotation = 0
    // MODIFYING THE DEFAULT CASE **************************************************************************
    // However, we can add an offset, such that we still proceed CCW (rotating about the Vicon +z-axis using right hand rule), 
    // but the person is now oriented differently w.r.t. the Vicon axes. 
    // CASE 1: person is facing along the negative x-axis: set rotation = pi/2
    // CASE 2: person is facing along the negative y-axis: set rotation = pi
    // CASE 1: person is facing along the positive x-axis: set rotation = (3/2) * pi 
    //float relativeRotationInRadians = Mathf.PI;
    float relativeRotationInRadians = (3.0f/2.0f) * Mathf.PI;

    // Establishing the forwards, rightwards, and upwards direction in Vicon frame, from the subject's perspective (relative to subject mid-foot)
    Vector3 midFootForwardDirectionViconFrame = new Vector3(1.0f, 0.0f, 0.0f);
    Vector3 midFootRightwardDirectionViconFrame = new Vector3(0.0f, -1.0f, 0.0f);
    Vector3 midFootUpwardsDirectionViconFrame = new Vector3(0.0f, 0.0f, 1.0f);

    // We'll also store the subject-relative frame in Unity coordinates
    Vector3 midFootForwardDirectionUnityFrame;
    Vector3 midFootRightwardDirectionUnityFrame;
    Vector3 midFootUpwardsDirectionUnityFrame;

    // Vive reference tracker
    public GameObject viveReferenceTracker;
    Matrix4x4 transformViconTrackerToUnityTrackerFrame = new Matrix4x4();
    Matrix4x4 transformUnityTrackerToViconTrackerFrame = new Matrix4x4();

    // Transformation from the subject-centric frame in Unity frame to the Unity global frame.
    // This will put the subject's center of BoS at (0,0,0) in Unity frame and orient the
    // markers to the desired gaze direction (along +zaxis) in Unity frame.
    Matrix4x4 transformationMidfootSubjectFrameToUnityGlobalFrame = new Matrix4x4();
    Matrix4x4 transformationUnityGlobalFrameToMidfootSubjectFrame = new Matrix4x4();


    private float StartTime;
    private float CurrentTime;
    public GameObject TheBall;

    // The ball touch detector
    private BallTouchDetector ballTouchDetectorScript;

    public GameObject LeftHand;
    public GameObject RightHand;
    public GameObject WaistTracker;
    public GameObject ChestTracker;
    public GameObject LeftAnkleTracker;
    public GameObject RightAnkleTracker;
    public GameObject LeftShankTracker;
    public GameObject RightShankTracker;
    public GameObject RefTracker;
    public GameObject headsetCameraGameObject;

    // Store the curernt reaching hand position
    private Vector3 currentReachingHandPositionUnityFrame;
    // Flags to store if either hand was touching the ball this unity frame
    private bool leftHandTouchingBallThisFrameFlag = false;
    private bool rightHandTouchingBallThisFrameFlag = false;

    // Reaching target (Ball) and hand radius (set in start)
    private float ballRadius;
    private float handSphereRadius;

    // Tracker in correct region for measurement flags
    private bool useRightControllerDistanceThisFrameFlag = false;
    private bool useLeftControllerDistanceThisFrameFlag = false;
    private bool useChestTrackerDistanceThisFrameFlag = false;


    // Player position and toggling to home position
    public Transform PlayerPosition;
    private MovePlayerToPositionOnStartup playerRepositioningScript;
    private bool playerToggledHomeFlag = false;

    private float timeForReadingInSetup = 10000.0f; // milliseconds
    private float timeAfterLastBallTouchToLeaveActiveStateInMs = 5000.0f; // milliseconds
    private float timeForFeedbackInMs = 100.0f; // milliseconds (we don't really provide feedback in this task, beyond the active feedback display).

    // State machine states
    private string currentState;
    private const string setupStateString = "SETUP";
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string instructionsStateString = "INSTRUCTIONS";
    private const string trialActiveState = "TRIAL_ACTIVE";
    private const string givingFeedbackStateString = "FEEDBACK";
    private const string gameOverStateString = "GAME_OVER";


    // Trial and block structure
    private int trialsPerBlock; // a trial is one reach
    public int trialsPerDirectionPerYValue;
    private int currentTrialNumber = 0; // the current trial number

    // Overall reaching direcitions in this game 
    public float[] reachingDirectionsInDegreesCcwFromRight; // the reaching angles specified CCW from the rightwards direction (player's right)
    private string[] reachingDirectionsAsStrings; // a string representation of the reaching directions, initialized in Start();
    private int[] reachingDirectionOrderListThisBlock; // this array stores the reaching direction for all trials this block.
    private int[] reachingYValueOrderListThisBlock; // the positional y-values
    public String[] reachingTargetZHeightsSpecifierStrings; // this array stores the 3 preset categories of reaching target height for all trials this block.
    private float[] reachingTargetUniytFrameYHeightsSpecifierValues; // stores the y-pos values corresponding to the string values in reachingTargetZHeightsSpecifierStrings.

    // Storing some trial-specific variables
    private float currentTrialStartTime;
    private float currentTrialEndTime;
    private Vector3 currentTrialBallStartPos;
    private Vector3 currentTrialBallEndPos;
    private bool rightHandCausedFurthestPushThisTrial = false;



    // Reaching direction order for the current trial
    // The integer value in each element corresponds to the index
    // of the reaching direction in the reachingDirectionsInDegreesCcwFromRight variable


    // Pseudorandom number generator for randomizing reach order
    private static System.Random randomNumberGenerator = new System.Random();

    // State transition timer
    private Stopwatch stateTransitionStopwatch = new Stopwatch();




    // feedback
    private string[] textPerHeight = new string[]
    {
        "at waist-level",
        "at shoulder-level",
        "at eye-level"
    };

    private string[] textPerDirection = new string[] { "right",
                                                        "forward-right",
                                                        "forward",
                                                        "forward-left",
                                                        "left"};

    private bool Right0Flag = false;
    private bool Right45Flag = false;
    private bool Left0Flag = false;
    private bool Left45Flag = false;
    private bool StraightForwardFlag = false;

    private bool WaitForEveryThingReadyFlag = true;
    private bool RuleFlag = false;

    // Ball spawning "radius" from center position (center of BoS, typically)
    [SerializeField]
    private float ballSpawnFractionUpperLimbLength = 0.5f; // Set in editor!!!!!
    private float ballSpawnRadius;

    private List<float[]> bestReachingDistancesPerDirectionAllTrials;
    private float[] bestReachingDistancesPerDirectionInTrial;
    private float[] bestChestExcursionDistancesPerDirection;
    private float BestReachingRangeForRight0 = 0f;
    private float BestReachingRangeForLeft0 = 0f;
    private float BestReachingRangeForRight45 = 0f;
    private float BestReachingRangeForLeft45 = 0f;
    private float ZoffsetDistance = 0f;

    private float rightDirectionAngle = 0.0f;
    private float right45DirectionAngle = 45.0f;

    // subject-specific distances
    private float upperArmLengthInMeters;
    private float forearmLengthInMeters;

    // Tracking ball interactions
    private bool lastFrameBallTouchedFlag = false; // whether or not the ball is in contact with EITHER hand.
    private bool leftHandCouldPushReachTarget = false; // if the ball is in contact with the left hand
    private bool rightHandCouldPushReachTarget = false;  // if the ball is in contact with the right hand

    private int TrialIndex;

    public GameObject generalDataRecorder;
    private GeneralDataRecorder generalDataRecorderScript;
    public GameObject subjectSpecificData;
    private SubjectInfoStorageScript subjectSpecificDataScript;

    public Text instructionsText;
    public Canvas bestReachDistanceCanvas;
    public Text bestReachDistanceText;

    // The edges of the base of support
    private float leftEdgeBaseOfSupportViconYPos;
    private float rightEdgeBaseOfSupportViconYPos;
    private float frontEdgeBaseOfSupportViconXPos;
    private float backEdgeBaseOfSupportViconXPos;
    private Vector3 centerOfBaseOfSupportViconFramePos;

    // Photon communication (for hardware sync)
    public CommunicateWithPhotonViaSerial communicateWithPhotonScript;
    public bool syncingWithExternalHardwareFlag; // Whether or not we're using the System Sync object to sync with EMGs and Vicon nexus
    private float unityFrameTimeAtWhichHardwareSyncSent = 0.0f; // The Time.time of the Update() frame when the hardware sync signal command was sent.

    // EMG stuff
    public bool streamingEmgDataFlag; // Whether to run the EMG service (true) or not (false)
    public StreamAndRecordEmgData emgDataStreamerScript; // communicates with Delsys base station, reads and saves EMG data
    private Stopwatch delayStartSyncSignalStopwatch = new Stopwatch(); // A stopwatch to add a small delay to sending our photon START sync signal
                                                                       // Seems necessary for Delsys base station.
    private bool emgBaseIsReadyForTriggerStatus = false; // whether the EMG base station is ready for the sync trigger (true) or not (false)
    private uint millisecondsDelayForStartEmgSyncSignal = 1000;
    private uint millisecondsDelayForStopEmgSyncSignal = 500;
    private bool hasEmgSyncStartSignalBeenSentFlag = false; // A flag that is flipped to true when the EMG sync signal (and, thus, start data stream)
                                                            // was sent to the Delsys base station.

    // Data saving
    private string subdirectoryName; // the folder name where all data will be saved.
    private string mostRecentFileNameStub; // the "stub" for the current run (e.g. Subject number, start time, task name - does not include type of data, e.g. trial)
    // Data saving - reach limits
    private const string defaultExcursionPerformanceSummaryFileName = "BestReachAndLeanDistances.csv";
    // Flags to specify whether or not we'll overwrite the current "best" reaching limits. 
    public bool overwriteExcursionLimits;

    // Neutral posture
    private string subdirectoryNeutralPoseDataString;
    private const string neutralPoseTaskName = "NeutralPose";
    private const string neutralPoseTaskFileName = "medianPostures.csv";
    private string medianPostureCsvPath;
    private float[] neutral5DofJointAngles = new float[5];
    private Vector3 neutralKneePosInFrame0;
    private Vector3 neutralPelvisPosInFrame0;
    private Vector3 neutralChestPosInFrame0;
    private Vector3 neutralHmdPosInFrame0;

    //stimulation status
    private string currentStimulationStatus; //the current stimulation status for this block
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // SOLO testing only
    private bool doingSoloTesting = false; // Set to true if solo testing the task
    private Stopwatch soloTestingToggleHeadsetToHomeStopwatch = new Stopwatch();
    private float timeToToggleCameraHomeInSoloTestingInMs = 10000.0f;

    // Keyboard testing
    public bool usingKeyboardControlForRightHand;



    void Start()
    {
        StartTime = Time.time;
        CurrentTime = Time.time;
        instructionsText.text = "Push the ball outwards \n as far as you can!";

        CreateReachingDirectionsThisTrial();

        // Start in setup state
        currentState = setupStateString;

        // Build a string[] representation of the reaching directions
        reachingDirectionsAsStrings = ConvertFloatArrayToStringArray(reachingDirectionsInDegreesCcwFromRight);

        // Get references
        playerRepositioningScript = PlayerPosition.gameObject.GetComponent<MovePlayerToPositionOnStartup>();
        subjectSpecificDataScript = subjectSpecificData.GetComponent<SubjectInfoStorageScript>();
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();
        // Number of trials in a block should be the number of reaching directions * number of y categories
        trialsPerBlock = trialsPerDirectionPerYValue * reachingTargetZHeightsSpecifierStrings.Length * reachingDirectionsInDegreesCcwFromRight.Length;

        // Initalize best distances per direction
        bestReachingDistancesPerDirectionInTrial = new float[trialsPerBlock];
        bestChestExcursionDistancesPerDirection = new float[trialsPerBlock];

        // Make the balls spawn at some percentage of the upper limb length
        ballSpawnRadius = ballSpawnFractionUpperLimbLength * (subjectSpecificDataScript.getUpperArmLengthInMeters() + subjectSpecificDataScript.getForearmLengthInMeters());

        // Get ball and hand object radius
        ballRadius = (TheBall.transform.localScale.x / 2.0f); // Default diameter is 1.0m, so the radius is 0.5 * the scaling factor (Assuming a sphere)
        handSphereRadius = LeftHand.transform.localScale.x / 2.0f;

        // Get the ball "touch detector" that detects if a hand is touching the ball
        ballTouchDetectorScript = TheBall.GetComponent<BallTouchDetector>();

        // Set the file paths, file names, and column headers for the frame and trial data
        setFrameAndTrialDataNaming();

        // Set the default force field mode as Idle
        currentDesiredForceFieldTypeSpecifier = forceFieldIdleModeSpecifier;

        Debug.Log("Atan2 test, expect a negative value in radians close to -pi: " + Mathf.Atan2(-0.01f, -.99f));

        // Get the stimulation status from the subject-specific info object
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();

        // Assign subject-specific distances
        upperArmLengthInMeters = subjectSpecificDataScript.getUpperArmLengthInMeters();
        forearmLengthInMeters = subjectSpecificDataScript.getForearmLengthInMeters();

        // Build the path to the neutral posture file and load the neutral posture
        medianPostureCsvPath = getDirectoryPath() + subdirectoryNeutralPoseDataString + neutralPoseTaskFileName;
        LoadNeutralPostureFromCsv(medianPostureCsvPath);

        // Set the last known value of the player to home toggle
        lastValueCameraToHomeToggle = playerCameraToHmdPosToggle;

        // If we're doing solo testing of this task and need to toggle the HMD to home without assistance
        if(doingSoloTesting == true)
        {
            soloTestingToggleHeadsetToHomeStopwatch.Start(); // start the stopwatch that will toggle the headset home some fixed time after Start().
        }

    //StartCoroutine(Sparwn());
}


    //FixedUpdate Should Always Record The Maximum of the Reaching Distance;
    void Update()
    {
        //float timestep = Time.time - CurrentTime;

        //ManageStateTransitionStopwatch();

        // Update the transformation from the reference tracker to the Unity frame
        UpdateTransformationFromRightHandedTrackerToUnityFrame();

        // Now depending on the current state
        if (currentState == setupStateString)
        {

            // See if the level manager startup support script has finished. 
            // Only do task-specific setup if it has been completed. 
            if(startupSupportScriptCompleteFlag == false)
            {
                startupSupportScriptCompleteFlag = levelManagerStartupSupportScript.GetServicesStartupCompleteStatusFlag();
            }

            // See if the Vive tracker data manager is ready to serve data
            if(viveTrackerDataInitializedFlag == false)
            {
                viveTrackerDataInitializedFlag = viveTrackerDataManagerScript.GetViveTrackerDataHasBeenInitializedFlag();
            }

            // See if the player has been toggled home
            if(playerToggledHomeFlag == false)
            {
                playerToggledHomeFlag = playerRepositioningScript.GetToggleHmdStatus();

                // If the player has just been toggled home
                if(playerToggledHomeFlag == true)
                {
                    // Lock the frame 0 (if allowing for frame 0 locking in the Vive data manager script)
                    viveTrackerDataManagerScript.FixFrame0OriginAndOrientation();
                }
            }

            bool systemsReadyFlag = startupSupportScriptCompleteFlag && viveTrackerDataInitializedFlag && playerToggledHomeFlag;
            

            // if the player has been reoriented already and the COM manager is ready to share Vicon data
            if (systemsReadyFlag == true)
            {

                // Tell the loader to load the transformation from Vicon to the reference tracker 
                //loadTransformationViconToReferenceTrackerScript.LoadViconToReferenceTrackerTransformation(subdirectoryViconTrackerTransformString, "");

                // Store a local copy of the Matrix4x4 transformation from Vicon frame to the reference tracker frame
                //transformationReferenceTrackerToViconFrame = loadTransformationViconToReferenceTrackerScript.GetTransformationReferenceTrackerToVicon();
                //Matrix4x4.Inverse3DAffine(transformationReferenceTrackerToViconFrame, ref transformationViconToReferenceTracker);

                // Get the edges of the base of support in Vicon frame
                //(leftEdgeBaseOfSupportViconYPos, rightEdgeBaseOfSupportViconYPos, frontEdgeBaseOfSupportViconXPos,
                 //backEdgeBaseOfSupportViconXPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportRotatedInViconCoordinates();

                // Send the start sync signal via the Photon, if using a sync signal
                if (syncingWithExternalHardwareFlag)
                {
                    communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

                    // Store the hardware sync signal sent time
                    unityFrameTimeAtWhichHardwareSyncSent = Time.time;
                }

                // Change to the instructions state
                changeActiveState(instructionsStateString);
                
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
                changeActiveState(instructionsStateString);
            }
        }
        // If we're currently just displaying instructions to the user
        else if (currentState == instructionsStateString)
        {
            storeFrameData();

            if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForReadingInSetup)
            {
                // Stop the stopwatch that delayed the Instructions timer until the player camera had been toggled home.
                stateTransitionStopwatch.Reset();

                // Change states to the active state
                changeActiveState(trialActiveState);
            }
        }
        // If we're currently measuring reaching distance along some direction
        else if (currentState == trialActiveState)
        {

            bool ballTouchedFlag = TrackMaxReachingDistanceAlongDirection();

            storeFrameData();

            // If the ball is not being touched now but has been touched
            if(ballTouchedFlag == false && lastFrameBallTouchedFlag == true)
            {
                // Start the timer that will terminate the active phase of the trial
                stateTransitionStopwatch.Restart();
            }
            // else if the subject has started to touch the ball
            else if(ballTouchedFlag == true && lastFrameBallTouchedFlag == false)
            {
                // Stop the timer and set it to zero.
                stateTransitionStopwatch.Reset();
            }

            // Update the lastBallPotentiallPushedFlag
            lastFrameBallTouchedFlag = ballTouchedFlag;

            // If enough time has elapsed since the person stopped touching the reach target/ball
            if (stateTransitionStopwatch.ElapsedMilliseconds >= timeAfterLastBallTouchToLeaveActiveStateInMs)
            {
                // COULD ADD A REQUIREMENT TO BE NEAR NEUTRAL POSE HERE!

                // Switch to the feedback state.
                changeActiveState(givingFeedbackStateString);
            }

        }
        else if (currentState == givingFeedbackStateString)
        {
            // Currently the feedback state just provides buffer time. No feedback is given.
            storeFrameData();

            if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForFeedbackInMs)
            {
                // if there are trials remaining
                if (currentTrialNumber < trialsPerBlock - 1)
                {
                    // switch states
                    changeActiveState(trialActiveState);
                }
                // if the block is over
                else
                {
                    // switch states to game over, since we only ever do 1 block.
                    changeActiveState(gameOverStateString);
                }
            }
        }
        else if (currentState == gameOverStateString)
        {
            // Do nothing in the game over state
        }
        else
        {

        }
    }



    /*    private void UpdatePositionOfViveHeadsetRepresentation()
        {
            // Convert the Vive headset location in Vive/Unity coordinates to Vicon frame
            //Vector3 viveHeadSetLocationViconFrame = mapPointFromUnityFrameToViconFrame(viveHeadset.transform.position);

            // Compute the transformation from Unity to reference tracker
            Matrix4x4 transformationUnityToReferenceTracker = new Matrix4x4();
            Matrix4x4.Inverse3DAffine(transformationReferenceTrackerToUnity, ref transformationUnityToReferenceTracker);

            // Compute the point in tracker frame
            Vector3 pointInUnityTrackerFrame = transformationUnityToReferenceTracker.MultiplyPoint3x4(viveHeadset.transform.position);

            // Convert from the left-handed (and just different) Unity tracker frame to the Vicon tracker frame
            Vector3 pointInViconTrackerFrame = transformUnityTrackerToViconTrackerFrame.MultiplyPoint3x4(pointInUnityTrackerFrame);

            // Convert from m to mm
            pointInViconTrackerFrame = pointInViconTrackerFrame * 1000.0f;

            // Transform the point from Vicon reference tracker frame to Vicon frame
            Vector3 viveHeadSetLocationViconFrame = transformationReferenceTrackerToViconFrame.MultiplyPoint3x4(pointInViconTrackerFrame);

          *//*  Debug.Log("HMD tracked pos. in Unity frame: (" + viveHeadset.transform.position.x + ", "
                + viveHeadset.transform.position.y + ", " + viveHeadset.transform.position.z + ") and " +
                "HMD tracked pos. in Unity tracker frame: (" + pointInUnityTrackerFrame.x + ", "
                + pointInUnityTrackerFrame.y + ", " + pointInUnityTrackerFrame.z +
                ") and HMD pos. transformed to Vicon tracker frame: (" +
                pointInViconTrackerFrame.x + ", " + pointInViconTrackerFrame.y + ", " + pointInViconTrackerFrame.z +
                ") and HMD pos. transformed to Vicon frame: (" +
                viveHeadSetLocationViconFrame.x + ", " + viveHeadSetLocationViconFrame.y + ", " + viveHeadSetLocationViconFrame.z + ")");*//*

            // Now map the Vive headset location from Vicon frame to the co-localized Unity frame. 
            // This SHOULD put the headset in the correct position relative to the subject's body.
            Vector3 viveHeadSetInColocalizedUnityFrame = mapPointFromViconFrameToUnityFrame(viveHeadSetLocationViconFrame);

            // Update the game object that represents the headset.
            viveHeadsetInColocalizedFrame.transform.position = viveHeadSetInColocalizedUnityFrame;
        }*/


    private void LoadNeutralPostureFromCsv(string medianPostureCsvPath)
    {
        if (!File.Exists(medianPostureCsvPath))
        {
            Debug.LogError("Median posture CSV file not found: " + medianPostureCsvPath);
            return;
        }

        string[] lines = File.ReadAllLines(medianPostureCsvPath);
        if (lines.Length < 2)
        {
            Debug.LogError("Median posture file is malformed or empty: " + medianPostureCsvPath);
            return;
        }

        // Read headers and values
        string[] headers = lines[0].Split(',');
        string[] values = lines[1].Split(',');

        Dictionary<string, float> data = new Dictionary<string, float>();

        for (int i = 0; i < headers.Length && i < values.Length; i++)
        {
            if (float.TryParse(values[i], out float parsedValue))
                data[headers[i]] = parsedValue;
        }

        // Assign joint variables
        neutral5DofJointAngles[0] = data["Median_Theta_1_In_Rads"];
        neutral5DofJointAngles[1] = data["Median_Theta_2_In_Rads"];
        neutral5DofJointAngles[2] = data["Median_Theta_3_In_Rads"];
        neutral5DofJointAngles[3] = data["Median_Theta_4_In_Rads"];
        neutral5DofJointAngles[4] = data["Median_Theta_5_In_Rads"];

        // Assign Vector3 positions of the key body points in frame 0
        neutralKneePosInFrame0 = new Vector3(
            data["Median_KNEE_CENTER_IN_FRAME_0_X"],
            data["Median_KNEE_CENTER_IN_FRAME_0_Y"],
            data["Median_KNEE_CENTER_IN_FRAME_0_Z"]
        );

        neutralPelvisPosInFrame0 = new Vector3(
            data["Median_PELVIC_CENTER_IN_FRAME_0_X"],
            data["Median_PELVIC_CENTER_IN_FRAME_0_Y"],
            data["Median_PELVIC_CENTER_IN_FRAME_0_Z"]
        );

        neutralChestPosInFrame0 = new Vector3(
            data["Median_CHEST_CENTER_IN_FRAME_0_X"],
            data["Median_CHEST_CENTER_IN_FRAME_0_Y"],
            data["Median_CHEST_CENTER_IN_FRAME_0_Z"]
        );

        neutralHmdPosInFrame0 = new Vector3(
            data["Median_HMD_POS_IN_FRAME_0_X"],
            data["Median_HMD_POS_IN_FRAME_0_Y"],
            data["Median_HMD_POS_IN_FRAME_0_Z"]
        );

        Debug.Log("Loaded neutral posture from: " + medianPostureCsvPath);
    }


    private bool TrackMaxReachingDistanceAlongDirection()
    {
        string debugTag = "ball push debug: ";

        // Get current reaching angle in Vicon frame
        float currentReachAngle =
            reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]];
        //Debug.Log($"{debugTag}currentReachAngle: {currentReachAngle:F3} degrees");

        // Get the frame 0 origin = mid-ankle point in Unity frame
        Vector3 frame0OriginInUnityFrame = viveTrackerDataManagerScript.GetAnkleCenterInUnityFrame();
        //Debug.Log($"{debugTag}frame0OriginInUnityFrame: {frame0OriginInUnityFrame.ToString("F3")}");

        // Get the adjusted Vive ref. tracker unit vectors along which the ball will be displaced
        Vector3 unitVectorReachingDirectionFrame0 =
            new Vector3(0.0f, Mathf.Sin(currentReachAngle * Mathf.Deg2Rad), -Mathf.Cos(currentReachAngle * Mathf.Deg2Rad));
        //Debug.Log($"{debugTag}unitVectorReachingDirectionFrame0: {unitVectorReachingDirectionFrame0.ToString("F3")}");

        // Get the transformation matrix from Vive ref (software, left-handed) frame to Unity frame
        Matrix4x4 transformationFrame0ToUnityFrame = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
        string matrixString =
            $"[{transformationFrame0ToUnityFrame.m00:F3}, {transformationFrame0ToUnityFrame.m01:F3}, {transformationFrame0ToUnityFrame.m02:F3}, {transformationFrame0ToUnityFrame.m03:F3}]\n" +
            $"[{transformationFrame0ToUnityFrame.m10:F3}, {transformationFrame0ToUnityFrame.m11:F3}, {transformationFrame0ToUnityFrame.m12:F3}, {transformationFrame0ToUnityFrame.m13:F3}]\n" +
            $"[{transformationFrame0ToUnityFrame.m20:F3}, {transformationFrame0ToUnityFrame.m21:F3}, {transformationFrame0ToUnityFrame.m22:F3}, {transformationFrame0ToUnityFrame.m23:F3}]\n" +
            $"[{transformationFrame0ToUnityFrame.m30:F3}, {transformationFrame0ToUnityFrame.m31:F3}, {transformationFrame0ToUnityFrame.m32:F3}, {transformationFrame0ToUnityFrame.m33:F3}]";
       // Debug.Log($"{debugTag}transformationFrame0ToUnityFrame:\n{matrixString}");

        // Get the vertical direction as defined by the Vive Reference tracker
        Vector3 frame0VerticalInUnityFrame = new Vector3(transformationFrame0ToUnityFrame.GetColumn(0).x,
            transformationFrame0ToUnityFrame.GetColumn(0).y, transformationFrame0ToUnityFrame.GetColumn(0).z);
        //Debug.Log($"{debugTag}frame0VerticalInUnityFrame: {frame0VerticalInUnityFrame.ToString("F3")}");

        // The reach direction unit vector in Unity frame
        Vector3 unitVectorReachingDirectionUnityFrame = transformationFrame0ToUnityFrame.MultiplyVector(unitVectorReachingDirectionFrame0);
        //Debug.Log($"{debugTag}unitVectorReachingDirectionUnityFrame: {unitVectorReachingDirectionUnityFrame.ToString("F3")}");

        // Ground plane orthogonal to the reaching direction
        Vector3 unitVectorReachingDirectionOrthogonal = Vector3.Cross(frame0VerticalInUnityFrame, unitVectorReachingDirectionUnityFrame);
        //Debug.Log($"{debugTag}unitVectorReachingDirectionOrthogonal: {unitVectorReachingDirectionOrthogonal.ToString("F3")}");

        // Determine if the relevant hand is touching the reach target (ball)
        (bool ballTouchedFlag, GameObject handTouchingReachTarget) = CheckForHandContactWithTheBall(currentReachAngle);
        //Debug.Log($"{debugTag}ballTouchedFlag: {ballTouchedFlag}, handTouchingReachTarget: {handTouchingReachTarget?.name ?? "null"}");

        // Get the reach height specifier
        int reachHeightSpecifier = reachingYValueOrderListThisBlock[currentTrialNumber];
        //Debug.Log($"{debugTag}reachHeightSpecifier: {reachHeightSpecifier}");

        // Get the reach height in frame 0 (i.e., the x-axis position containing the ball travel plane). 
        float ballXAxisPosFrame0 = GetBallXAxisPositionInFrame0(reachHeightSpecifier);
        //Debug.Log($"{debugTag}ballXAxisPosFrame0: {ballXAxisPosFrame0:F3}");

        // Compute the "origin" of the ball travel vector, directly above the frame 0 origin in frame 0, at the right height!
        Vector3 ballTravelOriginInFrame0 = new Vector3(ballXAxisPosFrame0, 0.0f, 0.0f);
        //Debug.Log($"{debugTag}ballTravelOriginInFrame0: {ballTravelOriginInFrame0.ToString("F3")}");

        // Compute the "origin" of the ball travel vector in Unity frame
        Vector3 ballTravelOriginInUnityFrame = transformationFrame0ToUnityFrame.MultiplyPoint3x4(ballTravelOriginInFrame0);
        //Debug.Log($"{debugTag}ballTravelOriginInUnityFrame: {ballTravelOriginInUnityFrame.ToString("F3")}");

        // Determine if the ball position should be updated and, if so, update it
        bool ballPositionUpdated = false; // whether or not the ball position has been updated yet.
        if (ballTouchedFlag == true)
        {
            Debug.Log(debugTag + "Hand touched ball this frame.");

            Vector3 vectorHandToBallCenter = TheBall.transform.position - handTouchingReachTarget.transform.position;
            Debug.Log($"{debugTag}vectorHandToBallCenter: {vectorHandToBallCenter.ToString("F3")}");

            float distanceHandToBallInTransverseGroundPlane = Vector3.Dot(vectorHandToBallCenter, unitVectorReachingDirectionUnityFrame);
            Debug.Log($"{debugTag}distanceHandToBallInTransverseGroundPlane: {distanceHandToBallInTransverseGroundPlane.ToString("F3")}");

            float magnitudeHandToBall = vectorHandToBallCenter.magnitude;
            float thresholdDistance = handSphereRadius + ballRadius;
            Debug.Log($"{debugTag}Hand-to-ball magnitude: {magnitudeHandToBall.ToString("F3")}, threshold: {thresholdDistance.ToString("F3")}");

            if (magnitudeHandToBall < thresholdDistance && distanceHandToBallInTransverseGroundPlane > 0.0f)
            {
                float verticalDistHandToBall = Vector3.Dot(vectorHandToBallCenter, frame0VerticalInUnityFrame);
                float orthogonalToReachDistHandToBall = Vector3.Dot(vectorHandToBallCenter, unitVectorReachingDirectionOrthogonal);
                Debug.Log($"{debugTag}Vertical component: {verticalDistHandToBall.ToString("F3")}, Orthogonal component: {orthogonalToReachDistHandToBall.ToString("F3")}");

                float desiredDistHandToBallAlongReachDir = Mathf.Sqrt(
                    Mathf.Pow(thresholdDistance, 2.0f) -
                    Mathf.Pow(verticalDistHandToBall, 2.0f) -
                    Mathf.Pow(orthogonalToReachDistHandToBall, 2.0f));
                Debug.Log($"{debugTag}Desired distance along reach direction: {desiredDistHandToBallAlongReachDir.ToString("F3")}");

                float distanceHandAlongBallTravelLineUnityFrame = Vector3.Dot(
                    handTouchingReachTarget.transform.position - ballTravelOriginInUnityFrame,
                    unitVectorReachingDirectionUnityFrame);
                Debug.Log($"{debugTag}Distance along ball travel line: {distanceHandAlongBallTravelLineUnityFrame.ToString("F3")}");

                Vector3 newBallPosition = ballTravelOriginInUnityFrame +
                    (distanceHandAlongBallTravelLineUnityFrame + desiredDistHandToBallAlongReachDir) * unitVectorReachingDirectionUnityFrame;
                Debug.Log($"{debugTag} Old ball position: {TheBall.transform.position.ToString("F3")}, New ball position: {newBallPosition.ToString("F3")}");

                // Update the ball position
                TheBall.transform.position = newBallPosition;
                ballPositionUpdated = true;

                // Store the best reach distance so far for this trial
                bestReachingDistancesPerDirectionInTrial[currentTrialNumber] = distanceHandAlongBallTravelLineUnityFrame + desiredDistHandToBallAlongReachDir;
            }
        }

        /* // Store the current shuolder midpoint distance if it is greater than the max recorded so far
         float bestChestExcursionDistanceThisTrial = bestChestExcursionDistancesPerDirection[currentTrialNumber];
         if (projectedChestExcursionDistance > bestChestExcursionDistanceThisTrial && isChestTrackerInRightArea)
         {
             bestChestExcursionDistancesPerDirection[currentTrialNumber] = projectedChestExcursionDistance;
         }*/

        // Update the best reach text
        bestReachDistanceText.text = "Furthest reach:" + "\n" +
            bestReachingDistancesPerDirectionInTrial[currentTrialNumber].ToString("F2") + " [m]";

        // HANDLE STATE TRANSITION OUT OF ACTIVE REACH.
        // If the ball is currently NOT being touched (pushed), but it was being touched last frame, then 
        // start the state transition stopwatch. 
        if ((ballTouchedFlag != lastFrameBallTouchedFlag) && (lastFrameBallTouchedFlag == true))
        {
            stateTransitionStopwatch.Restart(); // set to 0 and start

        } // Else if the ball is currently being touched but was NOT being touched last frame, then reset the stopwatch.
        // Note that reset is not restarting, and the stopwatch will not be counting up.
        else if ((ballTouchedFlag != lastFrameBallTouchedFlag) && (ballTouchedFlag == true))
        {
            stateTransitionStopwatch.Reset(); // set to 0 but leave in the stopped state
        }

        // Return ball touched flag
        return ballTouchedFlag;

    }


    private (bool, GameObject) CheckForHandContactWithTheBall(float currentReachDirectionInDegrees)
    {
        uint forwardDirSpecifier = 1000; // init to dummy value 
        // Programatically find the index that corresponds to forward reaching = 90 degrees
        for (uint dirIndex = 0; dirIndex < reachingDirectionsInDegreesCcwFromRight.Length; dirIndex++)
        {
            // Check if the direction is 90
            if (reachingDirectionsInDegreesCcwFromRight[dirIndex] == 90.0f)
            {
                forwardDirSpecifier = dirIndex;
            }
        }

        // Initialize a ball touched flag to false
        bool ballTouchedFlag = false;

        // Store the hand game object touching the ball, if either
        GameObject handTouchingBall = null;

        // Store if either hand is touching the ball
        //leftHandTouchingBallThisFrameFlag = ballTouchDetectorScript.GetLeftHandTouchingBallFlag();
        leftHandTouchingBallThisFrameFlag = false; // Left hand is disabled for now - right hand only testing.
        rightHandTouchingBallThisFrameFlag = ballTouchDetectorScript.GetRightHandTouchingBallFlag();

        Debug.Log("Current reach direction: " + currentReachDirectionInDegrees + ", current forward dir specifier: " + forwardDirSpecifier);

        // If the target is on the right
        if (currentReachDirectionInDegrees < 90.0f)
        {
            // Query the ball to see if the right hand contacted the ball
            ballTouchedFlag = rightHandTouchingBallThisFrameFlag;

            // Store the right hand game object
            handTouchingBall = RightHand;

            // Store the right hand position as the reaching hand position
            currentReachingHandPositionUnityFrame = RightHand.transform.position;

        }
        // else if the target is on the left
        else if (currentReachDirectionInDegrees > 90.0f)
        {
            // Query the ball to see if the left hand contacted the ball
            ballTouchedFlag = leftHandTouchingBallThisFrameFlag;

            // Store the left hand game object
            handTouchingBall = LeftHand;

            // Store the left hand position as the reaching hand position
            currentReachingHandPositionUnityFrame = LeftHand.transform.position;
        }
        else // else if the target is in the forward direction
        {
            // Select the hand that can touch the ball based on which side we're testing
            // If left side
            if (whichSideToPerformTaskSelector == WhichSideSelectEnum.LeftSide)
            {
                // Query left hand
                ballTouchedFlag = leftHandTouchingBallThisFrameFlag;

                // Store the left hand game object
                handTouchingBall = LeftHand;

                // Store the left hand position as the reaching hand position
                currentReachingHandPositionUnityFrame = LeftHand.transform.position;
            }
            else // if the right side
            {
                // Query right hand
                ballTouchedFlag = rightHandTouchingBallThisFrameFlag;

                // Store the right hand game object
                handTouchingBall = RightHand;

                // Store the right hand position as the reaching hand position
                currentReachingHandPositionUnityFrame = RightHand.transform.position;
            }
        }

        // Return
        return (ballTouchedFlag, handTouchingBall);
    }


    private float ConvertAngleDependingOnCurrentReachAngle(float angleToConvertInDegrees, float currentReachAngleInDegrees)
    {
        // If current reach angle is near 180 degrees, then convert the negative angles to be between 180 and 360 (instead of 0 to -180)
        float convertedAngle = angleToConvertInDegrees;

        if (currentReachAngleInDegrees >= 180.0f)
        {
            if(angleToConvertInDegrees < 0.0f)
            {
                convertedAngle = angleToConvertInDegrees + 360.0f;
            }
        }


            /*        if (currentReachAngleInDegrees >= 180.0f - 30.0f && currentReachAngleInDegrees <= 180.0f + 30.0f)
                    {
                        if (angleToConvertInDegrees < 0) // if the angle is negative
                        {
                            convertedAngle = angleToConvertInDegrees + 360.0f; // add 360 degrees to get the same angle as a positive
                        }
                    // If the angle is near 360 (which can happen depending on the subject rotation)
                    } else if (currentReachAngleInDegrees >= 360.0f - 30.0f && currentReachAngleInDegrees <= 360.0f)
                    {
                        if (angleToConvertInDegrees < 0) // if the angle is negative
                        {
                            convertedAngle = angleToConvertInDegrees + 360.0f; // add 360 degrees to get the same angle as a positive
                        }
                    }
                    else if (currentReachAngleInDegrees >= 0 - 30.0f && currentReachAngleInDegrees <= 30.0f)
                    {

                    }*/

            return convertedAngle;
    }




    /*    private void ManageStateTransitionStopwatch()
        {
            if (currentState == setupStateString)
            {
                if (stateTransitionStopwatch.ElapsedMilliseconds > timeForReadingInSetup)
                {
                    stateTransitionStopwatchCallback();
                }
            }
            else if (currentState == trialActiveState)
            {
                if (stateTransitionStopwatch.ElapsedMilliseconds > timeAfterLastBallTouchToLeaveActiveStateInMs)
                {
                    stateTransitionStopwatchCallback();
                }
            }
            else if (currentState == givingFeedbackStateString)
            {
                if (stateTransitionStopwatch.ElapsedMilliseconds > feedbackTime)
                {
                    stateTransitionStopwatchCallback();
                }
            }
        }*/


    private void UpdateTransformationFromRightHandedTrackerToUnityFrame()
    {
        // Get the Vive reference tracker unit vectors in Unity global frame
        Vector3 trackerXAxisInUnityFrame = viveReferenceTracker.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));
        Vector3 trackerYAxisInUnityFrame = viveReferenceTracker.transform.TransformDirection(new Vector3(0.0f, 1.0f, 0.0f));
        Vector3 trackerZAxisInUnityFrame = viveReferenceTracker.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f));

        // Get the tracker origin in Unity global frame
        Vector3 trackerOriginUnityFrame = viveReferenceTracker.transform.position;

        // Construct the transformation matrix and store it as an instance variable.
        transformationReferenceTrackerToUnity = getTransformationMatrix(trackerXAxisInUnityFrame, trackerYAxisInUnityFrame,
            trackerZAxisInUnityFrame, trackerOriginUnityFrame);
    }

    //Given the three normalized/unit axes of a local coordinate system and the translation FROM the target coordinate system
    //TO the local coordinate system defined in the target coordinate system, construct a transformation matrix
    //that will transform points in the local coordinate system to the target coordinate system
    private Matrix4x4 getTransformationMatrix(Vector3 xAxisVector, Vector3 yAxisVector, Vector3 zAxisVector, Vector3 translationTargetToLocalInTargetFrame)
    {
        Matrix4x4 transformationMatrixLocalToTarget = new Matrix4x4();


        //fill the columns of the transformation matrix
        transformationMatrixLocalToTarget.SetColumn(0, new Vector4(xAxisVector.x, xAxisVector.y,
            xAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(1, new Vector4(yAxisVector.x, yAxisVector.y,
            yAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(2, new Vector4(zAxisVector.x, zAxisVector.y,
            zAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(3, new Vector4(translationTargetToLocalInTargetFrame.x,
            translationTargetToLocalInTargetFrame.y, translationTargetToLocalInTargetFrame.z, 1)); //last element is one

        return transformationMatrixLocalToTarget;
    }




    /*    private void stateTransitionStopwatchCallback()
        {
            if (currentState == setupStateString)
            {
                changeActiveState(trialActiveState);
            }
            else if (currentState == trialActiveState)
            {
                changeActiveState(givingFeedbackStateString);
            }
            else if (currentState == givingFeedbackStateString)
            {
                if (currentTrialNumber < trialsPerBlock - 1)
                {
                    // switch states
                    changeActiveState(trialActiveState);
                }
                else
                {
                    // switch states
                    changeActiveState(gameOverStateString);
                }
            }
            else
            {
                // invalid state for timer to fire in.
            }
        }*/


    private (List<float[]>, List<float[]>) RetrieveBestHandAndChestExcursionsPerDirectionPerHeight()
    {
        // Store the maximum reaching heights in the format of List<float[]>. 
        // The 0th element in the list is the first reaching height maximum reach directions. 
        // The 1st element in the list is the second reaching height maximum reach directions, etc. 
        List<float[]> maximumReachingDistancesPerTargetHeightPerDirection = new List<float[]>();
        // Store the maximum chest excursions in the same way as above
        List<float[]> maximumChestDistancesPerTargetHeightPerDirection = new List<float[]>();

        // Fill the list of float[] with one float[] per reaching target height
        for (int reachingTargetHeightIndex = 0; reachingTargetHeightIndex < reachingTargetZHeightsSpecifierStrings.Length; reachingTargetHeightIndex++)
        {
            // Add an empty float[] of length = number of reaching directions to the List
            maximumReachingDistancesPerTargetHeightPerDirection.Add(new float[reachingDirectionsInDegreesCcwFromRight.Length]);
            maximumChestDistancesPerTargetHeightPerDirection.Add(new float[reachingDirectionsInDegreesCcwFromRight.Length]);
        }

        // For each trial (reach excursion)
        for (int trialIndex = 0; trialIndex < trialsPerBlock; trialIndex++)
        {
            // Get the row number from the current reaching height (0 through number of heights used)
            int currentReachingHeightIndex = reachingYValueOrderListThisBlock[trialIndex];
            // Get the column number by the reaching direction (0 through number of directions used
            int reachingDirectionIndex = reachingDirectionOrderListThisBlock[trialIndex];

            // DEBUGGING ONLY: PRINT THE STORED MAXIMUM FOR THAT TRIAL
            Debug.Log("Maximum (reach, chest) excursion distances for trial " + trialIndex +
                " with matrix indices: [" + currentReachingHeightIndex + ", " + reachingDirectionIndex +
                "] are: (" + bestReachingDistancesPerDirectionInTrial[trialIndex] + ", " + bestChestExcursionDistancesPerDirection[trialIndex] + ")");

            // If the value is greater than the value currently stored in that index
            // Store the value as the maximum for this reaching height (row) and reach direction (column).
            if (bestReachingDistancesPerDirectionInTrial[trialIndex] > maximumReachingDistancesPerTargetHeightPerDirection[currentReachingHeightIndex][reachingDirectionIndex])
            {
                // Store the reaching distance for this trial as the best one
                Debug.Log("Storing as BEST maximum reach excursion distances for trial " + trialIndex + " in index: [" + currentReachingHeightIndex + ", " + reachingDirectionIndex + "]");
                maximumReachingDistancesPerTargetHeightPerDirection[currentReachingHeightIndex][reachingDirectionIndex] = bestReachingDistancesPerDirectionInTrial[trialIndex];
            }
        }

        // For each trial (chest excursion)
        for (int trialIndex = 0; trialIndex < trialsPerBlock; trialIndex++)
        {
            // Get the row number from the current reaching height (0 through number of heights used)
            int currentReachingHeightIndex = reachingYValueOrderListThisBlock[trialIndex];
            // Get the column number by the reaching direction (0 through number of directions used
            int reachingDirectionIndex = reachingDirectionOrderListThisBlock[trialIndex];
            // If the value is greater than the value currently stored in that index
            // Store the value as the maximum for this reaching height (row) and reach direction (column).
            if (bestChestExcursionDistancesPerDirection[trialIndex] > maximumChestDistancesPerTargetHeightPerDirection[currentReachingHeightIndex][reachingDirectionIndex])
            {
                // Store the chest distance for this trial as the best one
                Debug.Log("Storing as BEST maximum chest excursion distances for trial " + trialIndex + " in index: [" + currentReachingHeightIndex + ", " + reachingDirectionIndex + "]");
                maximumReachingDistancesPerTargetHeightPerDirection[currentReachingHeightIndex][reachingDirectionIndex] = bestReachingDistancesPerDirectionInTrial[trialIndex];
                // Store the reaching distance for this trial as the best one
                maximumChestDistancesPerTargetHeightPerDirection[currentReachingHeightIndex][reachingDirectionIndex] = bestChestExcursionDistancesPerDirection[trialIndex];
            }
        }

        return (maximumReachingDistancesPerTargetHeightPerDirection, maximumChestDistancesPerTargetHeightPerDirection);

    }




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
            else if(currentState == waitingForEmgReadyStateString)
            {
                exitWaitingForEmgReadyState();
            }
            else if (currentState == instructionsStateString)
            {
                exitInstructionsState();
            }
            else if (currentState == trialActiveState)
            {
                exitTrialActiveState();
            }
            else if (currentState == givingFeedbackStateString)
            {
                exitGivingFeedbackState();
            }

            //then call the entry function for the new state
            if (newState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            else if (newState == instructionsStateString)
            {
                enterInstructionsState();
            }
            else if (newState == trialActiveState)
            {
                enterTrialActiveState();
            }
            else if (newState == givingFeedbackStateString)
            {
                enterGivingFeedbackStateString();
            }
            else if (newState == gameOverStateString)
            {
                enterGameOverState();
            }
            else
            {
                Debug.Log("Level manager cannot enter a non-existent state");
            }
        }
    }


    private void exitWaitingForSetupState()
    {
        //nothing needs to happen
    }

    private void enterWaitingForEmgReadyState()
    {
        // Set the new state
        currentState = waitingForEmgReadyStateString;
    }

    private void exitWaitingForEmgReadyState()
    {
        //nothing needs to happen
    }

    private void enterInstructionsState()
    {
        // If we're waiting for the experimenter to toggle the subject viewport to the home position
        // before starting the instructions timer
        if (waitForToggleHomeToStartInstructions == true)
        {
            // Reset the stopwatch (leaving it stopped at 0)
            stateTransitionStopwatch.Reset();

        }
        else // if we want the instructions timer to start immediately
        {
            // Start the stopwatch so that we can time how long the instructions appear
            stateTransitionStopwatch.Restart();
        }

        // Set the new state
        currentState = instructionsStateString;
    }

    private void exitInstructionsState()
    {
        //nothing needs to happen
    }

    private void enterTrialActiveState()
    {
        // switch states
        currentState = trialActiveState;

        // Ensure the mesh renderer for the ball is active/visible
        TheBall.GetComponent<MeshRenderer>().enabled = true;

        // Store the trial start time for this trial
        currentTrialStartTime = Time.time;

        // give directions for the current lean direction
        instructionsText.text = "Trial: " + currentTrialNumber + "\n" + "The ball is \n" +  
            textPerHeight[reachingYValueOrderListThisBlock[currentTrialNumber]] + " and \n" 
            + textPerDirection[reachingDirectionOrderListThisBlock[currentTrialNumber]];

        // Move the ball to the "spawn location"
        MoveBallToReachingDirectionSpawnLocation();

        // Store the ball starting position
        currentTrialBallStartPos = TheBall.transform.position;

        // Update the best reach text orientation
        Vector3 centerAtBallLevelUnityFrame = mapPointFromViconFrameToUnityFrame(centerOfBaseOfSupportViconFramePos);
        centerAtBallLevelUnityFrame = new Vector3(centerAtBallLevelUnityFrame.x, TheBall.transform.position.y, centerAtBallLevelUnityFrame.z);
        Vector3 desiredTextForwardsDirection = TheBall.transform.position - centerAtBallLevelUnityFrame;
        Quaternion textOrientation = Quaternion.LookRotation(desiredTextForwardsDirection, Vector3.up);
        bestReachDistanceCanvas.transform.rotation = textOrientation;

        // Reset the stopwatch that keeps track of how long we have per trial/state to 0, but do not start it.
        // We will instead start it after they have touched the ball and stop touching it.
        stateTransitionStopwatch.Reset();
    }



    private void exitTrialActiveState()
    {
        // No action needed
    }


    private void enterGivingFeedbackStateString()
    {
        //switch states
        currentState = givingFeedbackStateString;

        // Mark the end time for the trial
        currentTrialEndTime = Time.time;

        // hide the ball (this game object)
        TheBall.GetComponent<MeshRenderer>().enabled = false;

        // Provide feedback?
        float currentReachAngle = reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]];
        Debug.Log("Best reaching distance for angle" + currentReachAngle + "is " + bestReachingDistancesPerDirectionInTrial[reachingDirectionOrderListThisBlock[currentTrialNumber]]);

        // Reset the stopwatch that keeps track of how long we have for feedback
        stateTransitionStopwatch.Restart();
    }



    private void exitGivingFeedbackState()
    {
        // Store the ball ending position
        currentTrialBallEndPos = TheBall.transform.position;

        // Store a row of trial data for the completed trial
        StoreTrialData();

        // Icncrement trial number
        currentTrialNumber += 1;
    }

    private void enterGameOverState()
    {
        // switch states
        currentState = gameOverStateString;

        // Send the stop sync signal via the Photon
        communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();

        // Call a function that will get the best reach distance and chest excursion distance
        // per direction and per height!
        // We must do this in case we measured more than one trial per direction per height.
        (List<float[]> bestReachDistancesPerTargetHeightPerDirection, List<float[]> bestChestDistancesPerTargetHeightPerDirection) = RetrieveBestHandAndChestExcursionsPerDirectionPerHeight();

        // Store the performance data
        float[] excursionPerformanceData = new float[2 * reachingDirectionsInDegreesCcwFromRight.Length * reachingTargetZHeightsSpecifierStrings.Length]; // 2 is b/c we store both reach and chest data
        int currentDataRowIndex = 0;
        // Store best reaching distances in data row
        for (int targetHeightIndex = 0; targetHeightIndex < bestReachDistancesPerTargetHeightPerDirection.Count; targetHeightIndex++)
        {
            bestReachDistancesPerTargetHeightPerDirection[targetHeightIndex].CopyTo(excursionPerformanceData, currentDataRowIndex);
            currentDataRowIndex = currentDataRowIndex + bestReachDistancesPerTargetHeightPerDirection[targetHeightIndex].Length;
            // Debug only:
            Debug.Log("Writing data row for height index " + targetHeightIndex + " to file. ");
            for (int index = 0; index < bestReachDistancesPerTargetHeightPerDirection[targetHeightIndex].Length; index++)
            {
                Debug.Log("Element " + index + " has excursion distance: " + bestReachDistancesPerTargetHeightPerDirection[targetHeightIndex][index]);
                ;
            }
        }
        // Store best chest excursion distances in data row
        for (int targetHeightIndex = 0; targetHeightIndex < bestChestDistancesPerTargetHeightPerDirection.Count; targetHeightIndex++)
        {
            bestChestDistancesPerTargetHeightPerDirection[targetHeightIndex].CopyTo(excursionPerformanceData, currentDataRowIndex);
            currentDataRowIndex = currentDataRowIndex + bestChestDistancesPerTargetHeightPerDirection[targetHeightIndex].Length;

            // Debug only:
            Debug.Log("Writing data row for height index " + targetHeightIndex + " to file. ");
            for (int index = 0; index < bestChestDistancesPerTargetHeightPerDirection[targetHeightIndex].Length; index++)
            {
                Debug.Log("Element " + index + " has excursion distance: " + bestChestDistancesPerTargetHeightPerDirection[targetHeightIndex]);
                ;
            }
        }

        // Store the row of best reach excursions and chest excursions
        generalDataRecorderScript.storeRowOfExcursionPerformanceSummaryData(excursionPerformanceData);

        // write data to file
        tellDataRecorderToWriteStoredDataToFile();

        // Display text that the task is over
        instructionsText.text = "You data has been recorded. \n Thank you for testing!";
    }

    // END: State machine state-transitioning functions *********************************************************************************

    private (int[], int[]) ShuffleArray(int[] array)
    {

        // Initialize an index array to keep track of the shuffle
        int[] shuffledIndices = new int[array.Length];
        for (int index = 0; index < shuffledIndices.Length; index++)
        {
            // Store the index value in the corresponding element
            shuffledIndices[index] = index;
        }

        // Now we shuffle the array
        // This was from a forum, based on the Fisher-Yates shuffle
        int n = array.Length;
        while (n > 0)
        {
            n--;
            int k = randomNumberGenerator.Next(n + 1);
            int temp = array[k];
            array[k] = array[n];
            array[n] = temp;
            //Debug.Log("Trial in index " + n + " has value " + temp);

            // Also shuffle the index array
            temp = shuffledIndices[k];
            shuffledIndices[k] = shuffledIndices[n];
            shuffledIndices[n] = temp;
        }

        return (array, shuffledIndices);
    }


    private void CreateReachingDirectionsThisTrial()
    {
        // Create the reaching order list for this trial
        reachingDirectionOrderListThisBlock = new int[trialsPerDirectionPerYValue * reachingTargetZHeightsSpecifierStrings.Length * reachingDirectionsInDegreesCcwFromRight.Length];
        reachingYValueOrderListThisBlock = new int[trialsPerDirectionPerYValue * reachingTargetZHeightsSpecifierStrings.Length * reachingDirectionsInDegreesCcwFromRight.Length];
        //for each trial, populate direction and y-value
        for (int reachIndex = 0; reachIndex < reachingDirectionOrderListThisBlock.Length; reachIndex++)
        {
            // We have to generate the reaching direction list. 
            // Using the modulus, the vector will be[0, 1, 2, 3, ..., n, 0, 1, 2, ... n, 0, etc.], where n is the number of reaching directions minus 1.
            reachingDirectionOrderListThisBlock[reachIndex] = reachIndex % (reachingDirectionsInDegreesCcwFromRight.Length);

            // We also have to generate a reaching target height list such that each height is paired with all reaching direction values. 
            // For this reason, we use the quotient instead of the modulo, so the vector will be [0,0,..., 1, 1, ... 2, 2, ... etc.]
            // where the same value repeats for (trialsPerDirection * numberOfDirections) times.
            reachingYValueOrderListThisBlock[reachIndex] = (int)Mathf.Floor(reachIndex / (reachingDirectionsInDegreesCcwFromRight.Length * trialsPerDirectionPerYValue));

            // STORED reaching direction 
            Debug.Log("(Reaching direction, reaching Height) order specifier list: elements at index " + reachIndex + " are (" +
                reachingDirectionOrderListThisBlock[reachIndex] + ", " + reachingYValueOrderListThisBlock[reachIndex] + ")");
        }

        // Shuffle the reaching directions
        int[] shuffledIndices = new int[reachingDirectionOrderListThisBlock.Length];
        (reachingDirectionOrderListThisBlock, shuffledIndices) = ShuffleArray(reachingDirectionOrderListThisBlock);

        // Apply the SAME shuffle to the reaching target heights array
        int[] shuffledYValueOrderList = new int[reachingYValueOrderListThisBlock.Length];
        for (int index = 0; index < shuffledIndices.Length; index++)
        {
            // Retrieve the new shuffled element and store it in the current index.
            shuffledYValueOrderList[index] = reachingYValueOrderListThisBlock[shuffledIndices[index]];
        }
        // Reassign the reaching target heights to be the shuffled order
        reachingYValueOrderListThisBlock = shuffledYValueOrderList;
    }

    private void MoveBallToReachingDirectionSpawnLocation()
    {
        // Get the ball angle for this trial
        // Here is where we say the first value in the reachingDirectionOrderListThisBlock array is direction 0, 
        // the second is direction 1, and so on.
        float BallRebornAngle = reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]];

        // Convert to radians
        float BallRebornAngleRadians = BallRebornAngle * (Mathf.PI / 180.0f);

        int reachDirSpecifier = -1;
        // The reach distances are stored in the order: right (0), FR (45), forward (90), FL (135), L (180)
        // We will map from the reach dir angle (0, 45, 90, 135, or 180) to an index specifier. 
        if (BallRebornAngle == 0f)
        {
            reachDirSpecifier = 0;
        }
        else if (BallRebornAngle == 45.0f)
        {
            reachDirSpecifier = 1;
        }
        else if (BallRebornAngle == 90.0f)
        {
            reachDirSpecifier = 2;
        }
        else if (BallRebornAngle == 135.0f)
        {
            reachDirSpecifier = 3;
        }
        else if (BallRebornAngle == 180.0f)
        {
            reachDirSpecifier = 4;
        }

        // Get the reach height specifier
        int reachHeightSpecifier = reachingYValueOrderListThisBlock[currentTrialNumber];

        // Get the reach height in frame 0 (i.e., the x-axis position containing the ball travel plane). 
        float ballXAxisPosFrame0 = GetBallXAxisPositionInFrame0(reachHeightSpecifier);

        // Compute the "origin" of the ball travel vector, directly above the frame 0 origin in frame 0, at the right height!
        Vector3 ballTravelOriginInFrame0 = new Vector3(ballXAxisPosFrame0, 0.0f, 0.0f);

        // Compute the "origin" of the ball travel vector in Unity frame
        Matrix4x4 transformationFrame0ToUnityFrame = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
        Vector3 ballTravelOriginInUnityFrame = transformationFrame0ToUnityFrame.MultiplyPoint3x4(ballTravelOriginInFrame0);

        // Get the reaching ball travel direction vector in frame 0 of the model
        Vector3 unitVectorReachingDirectionFrame0 =
            new Vector3(0.0f, Mathf.Sin(BallRebornAngle * Mathf.Deg2Rad), -Mathf.Cos(BallRebornAngle * Mathf.Deg2Rad));

        // The reach direction unit vector in Unity frame
        Vector3 unitVectorReachingDirectionUnityFrame = transformationFrame0ToUnityFrame.MultiplyVector(unitVectorReachingDirectionFrame0);

        // Get the reach direction and height-specific ball spawn radius


        // Get the ball spawn position in Unity frame
        Vector3 ballSpawnPositionUnityFrame = ballTravelOriginInUnityFrame + unitVectorReachingDirectionUnityFrame * ballSpawnRadius;

        // Position the ball/reach target for the trial that has just begun
        TheBall.transform.position = ballSpawnPositionUnityFrame;
    }

    private float GetBallXAxisPositionInFrame0(int reachHeightSpecifier)
    {
        // Init x-axis position in Unity frame storage
        float xAxisHeightInFrame0 = 0.0f;
        
        // Get the desired height using the neutral pose data.
        if(reachHeightSpecifier == 0)
        {
            xAxisHeightInFrame0 = neutralPelvisPosInFrame0.x;
        }else if(reachHeightSpecifier == 1)
        {
            xAxisHeightInFrame0 = neutralChestPosInFrame0.x;
        }else if(reachHeightSpecifier == 2)
        {
            xAxisHeightInFrame0 = neutralHmdPosInFrame0.x;
        }
        else
        {
            Debug.LogError("Invalid reach height specifier!");
        }

        // Return
        return xAxisHeightInFrame0;
    }


    private string GetStringRepresentationOfReachingHeightGivenTrialIndex(int currentTrialNumber)
    {
        int reachingHeightSpecifier = reachingYValueOrderListThisBlock[currentTrialNumber];

        return reachingTargetZHeightsSpecifierStrings[reachingHeightSpecifier];
    }


    private Vector3 PlaceBallGivenTrialDirectionAndCurrentRadiusFromCenter(float angleToReachingTargetInRadians, float distanceToReachingTargetInMm)
    {
        // Compute the ball position (ignoring height) in Vicon frame, taking into account the expected orientation of the subject relative to the Vicon frame.
        Vector3 ballSpawnLocationViconFrame = centerOfBaseOfSupportViconFramePos +
            new Vector3(distanceToReachingTargetInMm * Mathf.Cos(angleToReachingTargetInRadians),
            distanceToReachingTargetInMm * Mathf.Sin(angleToReachingTargetInRadians),
            0);

        return ballSpawnLocationViconFrame;
    }


    private void setFrameAndTrialDataNaming()
    {
        // Set the frame data column headers
        string[] csvFrameDataHeaderNames = new string[]{
            "TIME_AT_UNITY_FRAME_START", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT",
            "STATE_TRANSITION_TIMER_ELAPSED_MS", "HMD_TOGGLED_HOME_FLAG",
            "POSITION_OF_HEADSET_X", "POSITION_OF_HEADSET_Y", "POSITION_OF_HEADSET_Z",
            "HEADSET_X_AXIS_UNITY_FRAME_X", "HEADSET_X_AXIS_UNITY_FRAME_Y", "HEADSET_X_AXIS_UNITY_FRAME_Z",
            "HEADSET_Y_AXIS_UNITY_FRAME_X", "HEADSET_Y_AXIS_UNITY_FRAME_Y", "HEADSET_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_REF_TRACKER_X", "POSITION_OF_REF_TRACKER_Y", "POSITION_OF_REF_TRACKER_Z",
            "REF_TRACKER_X_AXIS_UNITY_FRAME_X", "REF_TRACKER_X_AXIS_UNITY_FRAME_Y", "REF_TRACKER_X_AXIS_UNITY_FRAME_Z",
            "REF_TRACKER_Y_AXIS_UNITY_FRAME_X", "REF_TRACKER_Y_AXIS_UNITY_FRAME_Y", "REF_TRACKER_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_HAND_X", "POSITION_OF_RIGHT_HAND_Y", "POSITION_OF_RIGHT_HAND_Z",
            "RIGHT_HAND_X_AXIS_UNITY_FRAME_X", "RIGHT_HAND_X_AXIS_UNITY_FRAME_Y", "RIGHT_HAND_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_HAND_Y_AXIS_UNITY_FRAME_X", "RIGHT_HAND_Y_AXIS_UNITY_FRAME_Y", "RIGHT_HAND_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_CHEST_X", "POSITION_OF_CHEST_Y", "POSITION_OF_CHEST_Z",
            "CHEST_X_AXIS_UNITY_FRAME_X", "CHEST_X_AXIS_UNITY_FRAME_Y", "CHEST_X_AXIS_UNITY_FRAME_Z",
            "CHEST_Y_AXIS_UNITY_FRAME_X", "CHEST_Y_AXIS_UNITY_FRAME_Y", "CHEST_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_WAIST_X", "POSITION_OF_WAIST_Y", "POSITION_OF_WAIST_Z",
            "WAIST_X_AXIS_UNITY_FRAME_X", "WAIST_X_AXIS_UNITY_FRAME_Y", "WAIST_X_AXIS_UNITY_FRAME_Z",
            "WAIST_Y_AXIS_UNITY_FRAME_X", "WAIST_Y_AXIS_UNITY_FRAME_Y", "WAIST_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_LEFT_ANKLE_X", "POSITION_OF_LEFT_ANKLE_Y", "POSITION_OF_LEFT_ANKLE_Z",
            "LEFT_ANKLE_X_AXIS_UNITY_FRAME_X", "LEFT_ANKLE_X_AXIS_UNITY_FRAME_Y", "LEFT_ANKLE_X_AXIS_UNITY_FRAME_Z",
            "LEFT_ANKLE_Y_AXIS_UNITY_FRAME_X", "LEFT_ANKLE_Y_AXIS_UNITY_FRAME_Y", "LEFT_ANKLE_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_ANKLE_X", "POSITION_OF_RIGHT_ANKLE_Y", "POSITION_OF_RIGHT_ANKLE_Z",
            "RIGHT_ANKLE_X_AXIS_UNITY_FRAME_X", "RIGHT_ANKLE_X_AXIS_UNITY_FRAME_Y", "RIGHT_ANKLE_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_X", "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_Y", "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_LEFT_SHANK_X", "POSITION_OF_LEFT_SHANK_Y", "POSITION_OF_LEFT_SHANK_Z",
            "LEFT_SHANK_X_AXIS_UNITY_FRAME_X", "LEFT_SHANK_X_AXIS_UNITY_FRAME_Y", "LEFT_SHANK_X_AXIS_UNITY_FRAME_Z",
            "LEFT_SHANK_Y_AXIS_UNITY_FRAME_X", "LEFT_SHANK_Y_AXIS_UNITY_FRAME_Y", "LEFT_SHANK_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_SHANK_X", "POSITION_OF_RIGHT_SHANK_Y", "POSITION_OF_RIGHT_SHANK_Z",
            "RIGHT_SHANK_X_AXIS_UNITY_FRAME_X", "RIGHT_SHANK_X_AXIS_UNITY_FRAME_Y", "RIGHT_SHANK_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_SHANK_Y_AXIS_UNITY_FRAME_X", "RIGHT_SHANK_Y_AXIS_UNITY_FRAME_Y", "RIGHT_SHANK_Y_AXIS_UNITY_FRAME_Z",
            "KNEE_CENTER_IN_FRAME_0_X",  "KNEE_CENTER_IN_FRAME_0_Y",  "KNEE_CENTER_IN_FRAME_0_Z",
            "PELVIC_CENTER_IN_FRAME_0_X",  "PELVIC_CENTER_IN_FRAME_0_Y",  "PELVIC_CENTER_IN_FRAME_0_Z",
            "CHEST_CENTER_IN_FRAME_0_X",  "CHEST_CENTER_IN_FRAME_0_Y",  "CHEST_CENTER_IN_FRAME_0_Z",
            "HMD_POS_IN_FRAME_0_X", "HMD_POS_IN_FRAME_0_Y",  "HMD_POS_IN_FRAME_0_Z"
            };

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // Set the trial data column headers
        string[] csvTrialDataHeaderNames = new string[]{
                "TRIAL_NUMBER",  "UPPER_ARM_LENGTH_METERS", "FOREARM_LENGTH_METERS",
                "HMD_START_POS_X_UNITY_FRAME", "HMD_START_POS_Y_UNITY_FRAME", "HMD_START_POS_Z_UNITY_FRAME",
                "HMD_START_POS_X_VICON_FRAME", "HMD_START_POS_Y_VICON_FRAME", "HMD_START_POS_Z_VICON_FRAME",
                "HMD_TOGGLE_HOME_VECTOR_X", "HMD_TOGGLE_HOME_VECTOR_Y", "HMD_TOGGLE_HOME_VECTOR_Z",
                "TRANSFORM_VICON_TO_TRACKER_1_1", "TRANSFORM_VICON_TO_TRACKER_1_2", "TRANSFORM_VICON_TO_TRACKER_1_3", "TRANSFORM_VICON_TO_TRACKER_1_4",
                "TRANSFORM_VICON_TO_TRACKER_2_1", "TRANSFORM_VICON_TO_TRACKER_2_2", "TRANSFORM_VICON_TO_TRACKER_2_3", "TRANSFORM_VICON_TO_TRACKER_2_4",
                "TRANSFORM_VICON_TO_TRACKER_3_1", "TRANSFORM_VICON_TO_TRACKER_3_2", "TRANSFORM_VICON_TO_TRACKER_3_3", "TRANSFORM_VICON_TO_TRACKER_3_4",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_3",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_4",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_3",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_4",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_3",
                "TRANSFORM_VICON_TO_TRACKER_3_4",
                "TRIAL_START_TIME", "TRIAL_END_TIME", "REACHING_DIRECTION_SPECIFIER", "REACHING_HEIGHT_SPECIFIER",
                "BALL_START_POS_X", "BALL_START_POS_Y", "BALL_START_POS_Z", "BALL_END_POS_X", "BALL_END_POS_Y", "BALL_END_POS_Z",
                "HAND_THAT_ACHIEVED_MAX_WAS_RIGHT_HAND_FLAG", "MAX_REACH_EXCURSION_THIS_TRIAL", "MAX_CHEST_EXCURSION_THIS_TRIAL"};

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvTrialDataHeaderNames);

        // Excursion data naming
        // A string array with all of the reach and lean excursion header names
        string maxReachingStringStub = "MAX_REACHING_DIRECTION_";
        string maxChestStringStub = "MAX_CHEST_DIRECTION_";
        // Create a vector of the reaching direction strings
        string[] reachingHeaderNameStubs = prependStringToAllElementsOfStringArray(reachingDirectionsAsStrings, maxReachingStringStub);
        string[] chestHeaderNameStubs = prependStringToAllElementsOfStringArray(reachingDirectionsAsStrings, maxChestStringStub);
        // Next, append the height specifier to each vector of string[] with the reaching directions.
        List<string> reachMaxColumnHeaders = new List<string>();
        List<string> chestMaxColumnHeaders = new List<string>();
        // For each target height
        for (int targetHeightIndex = 0; targetHeightIndex < reachingTargetZHeightsSpecifierStrings.Length; targetHeightIndex++)
        {
            // Append the target height to the string array for the reach column headers
            reachMaxColumnHeaders.AddRange(appendStringToAllElementsOfStringArray(reachingHeaderNameStubs, "_HEIGHT_" + reachingTargetZHeightsSpecifierStrings[targetHeightIndex]));
            // Append the target height to the string array for the chest column headers
            chestMaxColumnHeaders.AddRange(appendStringToAllElementsOfStringArray(chestHeaderNameStubs, "_HEIGHT_" + reachingTargetZHeightsSpecifierStrings[targetHeightIndex]));
        }
        // Add all the column headers to a single string[]
        string[] excursionPerformanceSummaryHeaderNames = new string[reachMaxColumnHeaders.Count + chestMaxColumnHeaders.Count];
        reachMaxColumnHeaders.ToArray().CopyTo(excursionPerformanceSummaryHeaderNames, 0);
        chestMaxColumnHeaders.CopyTo(excursionPerformanceSummaryHeaderNames, reachMaxColumnHeaders.ToArray().Length);

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryRowHeaderNames(excursionPerformanceSummaryHeaderNames);

        // DEBUGGING ONLY
        for (int headerIndex = 0; headerIndex < excursionPerformanceSummaryHeaderNames.Length; headerIndex++)
        {
            Debug.Log("Output header name in index  is: " + excursionPerformanceSummaryHeaderNames[headerIndex]);
        }

        // 3.) Data subdirectory naming for the task's data
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).

        // Build the name of the subdirectory that will contain all of the output files for trajectory trace this session
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + thisTaskNameString + "/" + dateString + "/";
        subdirectoryName = subdirectoryString; //store as an instance variable so that it can be used for the marker and trial data

        // Build the name of the subdirectory containing the transformation from Vicon frame to reference tracker frame. 
        string nameOfViconToTrackerTask = "CalibrateViconAndVive";
        subdirectoryViconTrackerTransformString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" +
            nameOfViconToTrackerTask + "/" + dateString + "/";

        // Build the name of the subdirectory that contains the neutral pose joint variables and positions in frame 0
        subdirectoryNeutralPoseDataString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + neutralPoseTaskName + "/" + dateString + "/";

        //set the frame data and the reach and lean performance trial subdirectory name (will go inside the CSV folder in Assets)
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvExcursionPerformanceSummarySubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvEmgDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvStanceModelDataSubdirectoryName(subdirectoryString);

        // 4.) Call the function to set the file names (within the subdirectory) for the current block
        setFileNamesForCurrentBlockTrialAndFrameData();

    }

    private string[] appendStringToAllElementsOfStringArray(string[] stringArray, string stringToAppend)
    {

        //we must append to a clone of the string array, or else we'd be modifying the original string array!
        string[] stringArrayClone = (string[])stringArray.Clone();

        //for each element in the string array
        for (uint index = 0; index < stringArray.Length; index++)
        {
            //append the string to the element
            stringArrayClone[index] = stringArrayClone[index] + stringToAppend;
        }

        //return the modified string array
        return stringArrayClone;
    }

    private string[] prependStringToAllElementsOfStringArray(string[] stringArray, string stringToPrepend)
    {

        //we must prepend to a clone of the string array, or else we'd be modifying the original string array!
        string[] stringArrayClone = (string[])stringArray.Clone();

        //for each element in the string array
        for (uint index = 0; index < stringArray.Length; index++)
        {
            //append the string to the element
            stringArrayClone[index] = stringToPrepend + stringArrayClone[index];
        }

        //return the modified string array
        return stringArrayClone;
    }

    private string[] ConvertFloatArrayToStringArray(float[] floatArray)
    {

        // Initialize return string array
        string[] stringArray = new string[floatArray.Length];

        // for each element of the float array
        for (int index = 0; index < floatArray.Length; index++)
        {
            // Convert the element to a single decimal point value
            string elementString = floatArray[index].ToString("F1");
            // sub the decimal point for a file-safe underscoare
            elementString = elementString.Replace(".", "_");
            // store the string representation
            stringArray[index] = elementString;
        }

        // return the new string array
        return stringArray;
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
        //string blockNumberAsString = "Block" + currentBlockNumber; // We'll save each block into its own file
        string fileNameStub = thisTaskNameString + delimiter + subjectSpecificInfoString + delimiter + currentStimulationStatus + delimiter + dateAndTimeString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);

        // Set the EMG data file name
        string fileNameEmgData = fileNameStub + "_Emg_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvEmgDataFileName(fileNameEmgData);

        // Set the stnace model data file name
        string fileNameStanceModelData = fileNameStub + "_StanceModel_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvStanceModelDataFileName(fileNameStanceModelData);

        // Set the reaching performance SPECIFIC time-stamped file name. 
        // We'll save to this file name, then overwrite the "best" reaching limits file if desired.
        string fileNameReachingLimitsData = fileNameStub + "_Reach_Distances.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(fileNameReachingLimitsData);

    }


    private void ComputeTransformationFromViconTrackerToUnityTrackerFrameAndBack()
    {
        // 1.) Initialize the transformation frmo Vicon tracker frame to Unity tracker frame
        // Flip x-axis
        transformViconTrackerToUnityTrackerFrame.SetColumn(0, new Vector4(-1.0f, 0.0f, 0.0f, 0.0f));
        // New y-axis is the old negative z-axis
        transformViconTrackerToUnityTrackerFrame.SetColumn(1, new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
        // New z-axis is the old y-axis
        transformViconTrackerToUnityTrackerFrame.SetColumn(2, new Vector4(0.0f, -1.0f, 0.0f, 0.0f));
        // The translation is zero
        transformViconTrackerToUnityTrackerFrame.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

        // 1.) Initialize the transformation frmo Unity tracker frame to Vicon tracker frame
        // Really it is just the transpose of the opposite rotation with no translation.
        Matrix4x4 tempTransposeOfViconToUnity = transformViconTrackerToUnityTrackerFrame.transpose;
        Vector4 firstColumn = new Vector4(tempTransposeOfViconToUnity[0, 0], tempTransposeOfViconToUnity[1, 0], tempTransposeOfViconToUnity[2, 0], 0.0f);
        Vector4 secondColumn = new Vector4(tempTransposeOfViconToUnity[0, 1], tempTransposeOfViconToUnity[1, 1], tempTransposeOfViconToUnity[2, 1], 0.0f);
        Vector4 thirdColumn = new Vector4(tempTransposeOfViconToUnity[0, 2], tempTransposeOfViconToUnity[1, 2], tempTransposeOfViconToUnity[2, 2], 0.0f);
        Vector4 fourthColumn = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        transformUnityTrackerToViconTrackerFrame.SetColumn(0, firstColumn);
        transformUnityTrackerToViconTrackerFrame.SetColumn(1, secondColumn);
        transformUnityTrackerToViconTrackerFrame.SetColumn(2, thirdColumn);
        transformUnityTrackerToViconTrackerFrame.SetColumn(3, fourthColumn);


        /*        // Flip x-axis
                transformUnityTrackerToViconTrackerFrame.SetColumn(0, new Vector4(-1.0f, 0.0f, 0.0f, 0.0f));
                // New y-axis is the old z-axis
                transformUnityTrackerToViconTrackerFrame.SetColumn(1, new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
                // New z-axis is the old negative y-axis
                transformUnityTrackerToViconTrackerFrame.SetColumn(2, new Vector4(0.0f, -1.0f, 0.0f, 0.0f));
                // The translation is zero
                transformUnityTrackerToViconTrackerFrame.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));*/
    }

    private void ComputeTransformationFromMidfootViconFrameOriginToUnityOrigin()
    {
        // 1.) Deal with translations
        // Transform the mid-foot (mid center of BoS) origin in Vicon frame into Unity frame
        Vector3 midFootOriginInUnityFrame = mapPointFromViconFrameToUnityFrameWithoutColocalizingOrigins(centerOfBaseOfSupportViconFramePos);
        Debug.Log("Mid-foot origin in Unity frame: (x, y, z) = " + midFootOriginInUnityFrame.x + ", " + midFootOriginInUnityFrame.y + ", " + midFootOriginInUnityFrame.z + ")");

        // The translation from the Unity origin to the mid-foot origin is just the position of the mid-foot origin expressed in Unity frame. 
        Vector3 translationFromUnityFrameOriginToMidfootOriginInUnityFrame = midFootOriginInUnityFrame;

        // 2.) We have to ascertain the orientation of the midfoot origin frame axes in Unity frame, then 
        //     find the rotation from that orientation to the desired orientation in Unity frame.
        // forward direction Unity frame = local z-axis (local should be defined left-handed)
        midFootForwardDirectionUnityFrame = mapPointFromViconFrameToUnityFrameWithoutColocalizingOrigins(
            centerOfBaseOfSupportViconFramePos + midFootForwardDirectionViconFrame);
        midFootForwardDirectionUnityFrame = midFootForwardDirectionUnityFrame - midFootOriginInUnityFrame;
        midFootForwardDirectionUnityFrame = midFootForwardDirectionUnityFrame / midFootForwardDirectionUnityFrame.magnitude;
        // rightwards direction Unity frame = local x-axis (local should be defined left-handed)
        midFootRightwardDirectionUnityFrame = mapPointFromViconFrameToUnityFrameWithoutColocalizingOrigins(
    centerOfBaseOfSupportViconFramePos + midFootRightwardDirectionViconFrame);
        midFootRightwardDirectionUnityFrame = midFootRightwardDirectionUnityFrame - midFootOriginInUnityFrame;
        midFootRightwardDirectionUnityFrame = midFootRightwardDirectionUnityFrame / midFootRightwardDirectionUnityFrame.magnitude;
        // upwards direction Unity frame = local y-axis (local should be defined left-handed)
        midFootUpwardsDirectionUnityFrame = mapPointFromViconFrameToUnityFrameWithoutColocalizingOrigins(
    centerOfBaseOfSupportViconFramePos + midFootUpwardsDirectionViconFrame);
        midFootUpwardsDirectionUnityFrame = midFootUpwardsDirectionUnityFrame - midFootOriginInUnityFrame;
        midFootUpwardsDirectionUnityFrame = midFootUpwardsDirectionUnityFrame / midFootUpwardsDirectionUnityFrame.magnitude;

        // Get a 4x4 matrix that just contains the rotation from the midfoot frame in Unity
        Matrix4x4 rotationMidfootToUnity = getTransformationMatrix(midFootForwardDirectionUnityFrame, midFootRightwardDirectionUnityFrame,
            midFootUpwardsDirectionUnityFrame, new Vector3(0.0f, 0.0f, 0.0f));

        // Get a 4x4 matrix that just contains the rotation from the final desired orientation to the midfoot frame in Unity
        Matrix4x4 rotationFinalToMidfoot = getTransformationMatrix(new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f),
                                            new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 0.0f));

        // Right-hand multiply to get a rotation matrix from desired to Unity
        Matrix4x4 rotationFinalDesiredToUnity =  rotationMidfootToUnity * rotationFinalToMidfoot;

        // Extract the columns of the final rotation so that we can create a new transformation with a translation
        Vector3 newDesiredForwardsDirectionUnityFrame = new Vector3(rotationFinalDesiredToUnity[0, 0], rotationFinalDesiredToUnity[0, 1],
            rotationFinalDesiredToUnity[0, 2]);
        Vector3 newDesiredRightwardsDirectionUnityFrame = new Vector3(rotationFinalDesiredToUnity[1, 0], rotationFinalDesiredToUnity[1, 1],
            rotationFinalDesiredToUnity[1, 2]);
        Vector3 newDesiredUpwardsDirectionUnityFrame = new Vector3(rotationFinalDesiredToUnity[2, 0], rotationFinalDesiredToUnity[2, 1],
            rotationFinalDesiredToUnity[2, 2]);

        /*        // Do the final rotation so that the forward direction is +z-axis in global Unity, rightwards is +x-axis in global Unity
                Matrix4x4 finalOrientationTransformation = getTransformationMatrix(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 0.0f));
                transformationMidfootSubjectFrameToUnityGlobalFrame = finalOrientationTransformation * transformationMidfootSubjectFrameToUnityGlobalFrame;

                // Multiply old rotation matrix components by the final orientation transformation
                Vector3 newMidfootForwardDirectionUnityFrame = finalOrientationTransformation.MultiplyVector(midFootForwardDirectionUnityFrame);
                Vector3 newMidfootRightwardsDirectionUnityFrame = finalOrientationTransformation.MultiplyVector(midFootRightwardDirectionUnityFrame);
                Vector3 newMidfootUpwardsDirectionUnityFrame = finalOrientationTransformation.MultiplyVector(midFootUpwardsDirectionUnityFrame);*/

        // Build the transformation from Unity global frame to the midfoot frame, taking into account the final orientation change.
        // By defining the midfoot frame forward direction as its z-axis and rightwards direction as its x-axis,
        // the transformation will align these axes to their corresponding global frame axes.
        transformationMidfootSubjectFrameToUnityGlobalFrame = getTransformationMatrix(newDesiredForwardsDirectionUnityFrame,
                    newDesiredRightwardsDirectionUnityFrame, newDesiredUpwardsDirectionUnityFrame, midFootOriginInUnityFrame);

        Debug.Log("Subject-relative frame to Unity frame forwards axis (x,y,z): (" + newDesiredForwardsDirectionUnityFrame.x + ", "
            + newDesiredForwardsDirectionUnityFrame.y + ", " + newDesiredForwardsDirectionUnityFrame.z + ")");
        Debug.Log("Subject-relative frame to Unity frame rightwards axis (x,y,z): (" + newDesiredRightwardsDirectionUnityFrame.x + ", "
    + newDesiredRightwardsDirectionUnityFrame.y + ", " + newDesiredRightwardsDirectionUnityFrame.z + ")");
        Debug.Log("Subject-relative frame to Unity frame upwards axis (x,y,z): (" + newDesiredUpwardsDirectionUnityFrame.x + ", "
    + newDesiredUpwardsDirectionUnityFrame.y + ", " + newDesiredUpwardsDirectionUnityFrame.z + ")");
        /* transformationMidfootSubjectFrameToUnityGlobalFrame = getTransformationMatrix(midFootRightwardDirectionUnityFrame, midFootUpwardsDirectionUnityFrame,
             midFootForwardDirectionUnityFrame, midFootOriginInUnityFrame);*/

        // Get the inverse of this matrix
        transformationUnityGlobalFrameToMidfootSubjectFrame = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(transformationMidfootSubjectFrameToUnityGlobalFrame,
            ref transformationUnityGlobalFrameToMidfootSubjectFrame);

        Debug.Log("Transformation midfoot subject frame to Unity global.");
        printTransformationMatrix(transformationUnityGlobalFrameToMidfootSubjectFrame);

    }

    private void printTransformationMatrix(Matrix4x4 transformationMatrix)
    {
        for(int colIndex = 0; colIndex < 4; colIndex++)
        {
            for (int rowIndex = 0; rowIndex < 4; rowIndex++)
            {
                Debug.Log("T (" + colIndex + ", " + rowIndex + ") = " + transformationMatrix[colIndex, rowIndex]);
            }

        }
    }

    private Vector3 mapPointFromViconFrameToUnityFrameWithoutColocalizingOrigins(Vector3 pointInViconFrame)
    {
        // First multiply the point in Vicon frame by the transformation to the tracker frame
        Vector3 pointInViconTrackerFrame = transformationViconToReferenceTracker.MultiplyPoint3x4(pointInViconFrame);

        // Convert the units from mm to m 
        pointInViconTrackerFrame = pointInViconTrackerFrame / 1000.0f;

        // Convert from Vicon tracker frame to the left-handed (and just different) Unity tracker frame
        Vector3 pointInUnityTrackerFrame = transformViconTrackerToUnityTrackerFrame.MultiplyPoint3x4(pointInViconTrackerFrame);

        // Then multiply the point by the transformation from tracker to Unity frame 
        Vector3 pointInUnityWorldCoords = transformationReferenceTrackerToUnity.MultiplyPoint3x4(pointInUnityTrackerFrame);

        return pointInUnityWorldCoords;
    }


    // NOTE: this function IS the inverse of mapPointFromViconFrameToUnityFrame() in this script, because
    // the data we pass to it is typically extracted from Vive in the colocalized unity frame.
    private Vector3 mapPointFromColocalizedUnityFrameToViconFrame(Vector3 pointInColocalizedUnityFrame)
    {
        // Compute the transformation from the "co-localized" frame to the global Unity frame
        Vector3 pointInUnityFrame = transformationMidfootSubjectFrameToUnityGlobalFrame.MultiplyPoint3x4(pointInColocalizedUnityFrame);

        // Compute the transformation from Unity to reference tracker
        Matrix4x4 transformationUnityToReferenceTracker = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(transformationReferenceTrackerToUnity, ref transformationUnityToReferenceTracker);

        // Compute the point in tracker frame
        Vector3 pointInUnityTrackerFrame = transformationUnityToReferenceTracker.MultiplyPoint3x4(pointInUnityFrame);

        // Convert from the left-handed (and just different) Unity tracker frame to the Vicon tracker frame
        Vector3 pointInViconTrackerFrame = transformUnityTrackerToViconTrackerFrame.MultiplyPoint3x4(pointInUnityTrackerFrame);

        // Convert from m to mm
        pointInViconTrackerFrame = pointInViconTrackerFrame * 1000.0f;

        // Transform the point from Vicon reference tracker frame to Vicon frame
        Vector3 pointInViconFrame = transformationReferenceTrackerToViconFrame.MultiplyPoint3x4(pointInViconTrackerFrame);

        // return the point in Vicon coordinates
        return pointInViconFrame;
    }


    // BEGIN: Vicon <-> Unity mapping functions and other public functions*********************************************************************************

    public override string GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    // The mapping function from Vicon frame to Unity frame. This function is a member of the 
    // parent class of this script, so that other GameObjects can access it. 
    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        // First multiply the point in Vicon frame by the transformation to the tracker frame
        Vector3 pointInViconTrackerFrame = transformationViconToReferenceTracker.MultiplyPoint3x4(pointInViconFrame);

        // Convert the units from mm to m 
        pointInViconTrackerFrame = pointInViconTrackerFrame / 1000.0f;

        // Convert from Vicon tracker frame to the left-handed (and just different) Unity tracker frame
        Vector3 pointInUnityTrackerFrame = transformViconTrackerToUnityTrackerFrame.MultiplyPoint3x4(pointInViconTrackerFrame);

        // Then multiply the point by the transformation from tracker to Unity frame 
        Vector3 pointInUnityWorldCoords = transformationReferenceTrackerToUnity.MultiplyPoint3x4(pointInUnityTrackerFrame);

        // Last, adjust the point so that the subject-centric midfoot frame origin is located at the Unity global frame origin.
        Vector3 colocalizedOriginsPointInUnityWorldCoords = transformationUnityGlobalFrameToMidfootSubjectFrame.MultiplyPoint3x4(pointInUnityWorldCoords);

        return colocalizedOriginsPointInUnityWorldCoords;
    }

    // NOTE: this function is NOT the inverse of mapPointFromViconFrameToUnityFrame() in this script, because
    // the data we pass to it is typically extracted from Vive in the raw Unity frame, not the colocalized unity frame.
    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // Compute the transformation from the colocalized subject-centric frame to its raw Unity location (before frame co-localization)
        //Vector3 pointInUnityFrameBeforeColocalization = transformationUnityGlobalFrameToMidfootSubjectFrame.MultiplyPoint3x4(pointInUnityFrame);

        // Compute the transformation from the "co-localized" frame to the global Unity frame
        //Vector3 pointInUnityFrame = transformationMidfootSubjectFrameToUnityGlobalFrame.MultiplyPoint3x4(pointInColocalizedUnityFrame);

        // Compute the transformation from Unity to reference tracker
        Matrix4x4 transformationUnityToReferenceTracker = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(transformationReferenceTrackerToUnity, ref transformationUnityToReferenceTracker);

        // Compute the point in tracker frame
        Vector3 pointInUnityTrackerFrame = transformationUnityToReferenceTracker.MultiplyPoint3x4(pointInUnityFrame);

        // Convert from the left-handed (and just different) Unity tracker frame to the Vicon tracker frame
        Vector3 pointInViconTrackerFrame = transformUnityTrackerToViconTrackerFrame.MultiplyPoint3x4(pointInUnityTrackerFrame);

        // Convert from m to mm
        pointInViconTrackerFrame = pointInViconTrackerFrame * 1000.0f;

        // Transform the point from Vicon reference tracker frame to Vicon frame
        Vector3 pointInViconFrame = transformationReferenceTrackerToViconFrame.MultiplyPoint3x4(pointInViconTrackerFrame);

        // return the point in Vicon coordinates
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



    private void storeFrameData()
    {

        // the list that will store the data
        List<float> frameDataToStore = new List<float>();

        /*// A string array with all of the frame data header names
/*        "TIME_AT_UNITY_FRAME_START", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT",
            "STATE_TRANSITION_TIMER_ELAPSED_MS",
            "HMD_TOGGLED_HOME_FLAG", 
            "POSITION_OF_HEADSET_X", "POSITION_OF_HEADSET_Y", "POSITION_OF_HEADSET_Z",
            "HEADSET_X_AXIS_UNITY_FRAME_X", "HEADSET_X_AXIS_UNITY_FRAME_Y", "HEADSET_X_AXIS_UNITY_FRAME_Z",
            "HEADSET_Y_AXIS_UNITY_FRAME_X", "HEADSET_Y_AXIS_UNITY_FRAME_Y", "HEADSET_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_REF_TRACKER_X", "POSITION_OF_REF_TRACKER_Y", "POSITION_OF_REF_TRACKER_Z",
            "REF_TRACKER_X_AXIS_UNITY_FRAME_X", "REF_TRACKER_X_AXIS_UNITY_FRAME_Y", "REF_TRACKER_X_AXIS_UNITY_FRAME_Z",
            "REF_TRACKER_Y_AXIS_UNITY_FRAME_X", "REF_TRACKER_Y_AXIS_UNITY_FRAME_Y", "REF_TRACKER_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_HAND_X", "POSITION_OF_RIGHT_HAND_Y", "POSITION_OF_RIGHT_HAND_Z",
            "RIGHT_HAND_X_AXIS_UNITY_FRAME_X", "RIGHT_HAND_X_AXIS_UNITY_FRAME_Y", "RIGHT_HAND_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_HAND_Y_AXIS_UNITY_FRAME_X", "RIGHT_HAND_Y_AXIS_UNITY_FRAME_Y", "RIGHT_HAND_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_CHEST_X", "POSITION_OF_CHEST_Y", "POSITION_OF_CHEST_Z",
            "CHEST_X_AXIS_UNITY_FRAME_X", "CHEST_X_AXIS_UNITY_FRAME_Y", "CHEST_X_AXIS_UNITY_FRAME_Z",
            "CHEST_Y_AXIS_UNITY_FRAME_X", "CHEST_Y_AXIS_UNITY_FRAME_Y", "CHEST_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_WAIST_X", "POSITION_OF_WAIST_Y", "POSITION_OF_WAIST_Z",
            "WAIST_X_AXIS_UNITY_FRAME_X", "WAIST_X_AXIS_UNITY_FRAME_Y", "WAIST_X_AXIS_UNITY_FRAME_Z",
            "WAIST_Y_AXIS_UNITY_FRAME_X", "WAIST_Y_AXIS_UNITY_FRAME_Y", "WAIST_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_LEFT_ANKLE_X", "POSITION_OF_LEFT_ANKLE_Y", "POSITION_OF_LEFT_ANKLE_Z",
            "LEFT_ANKLE_X_AXIS_UNITY_FRAME_X", "LEFT_ANKLE_X_AXIS_UNITY_FRAME_Y", "LEFT_ANKLE_X_AXIS_UNITY_FRAME_Z",
            "LEFT_ANKLE_Y_AXIS_UNITY_FRAME_X", "LEFT_ANKLE_Y_AXIS_UNITY_FRAME_Y", "LEFT_ANKLE_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_ANKLE_X", "POSITION_OF_RIGHT_ANKLE_Y", "POSITION_OF_RIGHT_ANKLE_Z",
            "RIGHT_ANKLE_X_AXIS_UNITY_FRAME_X", "RIGHT_ANKLE_X_AXIS_UNITY_FRAME_Y", "RIGHT_ANKLE_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_X", "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_Y", "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_LEFT_SHANK_X", "POSITION_OF_LEFT_SHANK_Y", "POSITION_OF_LEFT_SHANK_Z",
            "LEFT_SHANK_X_AXIS_UNITY_FRAME_X", "LEFT_SHANK_X_AXIS_UNITY_FRAME_Y", "LEFT_SHANK_X_AXIS_UNITY_FRAME_Z",
            "LEFT_SHANK_Y_AXIS_UNITY_FRAME_X", "LEFT_SHANK_Y_AXIS_UNITY_FRAME_Y", "LEFT_SHANK_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_SHANK_X", "POSITION_OF_RIGHT_SHANK_Y", "POSITION_OF_RIGHT_SHANK_Z",
            "RIGHT_SHANK_X_AXIS_UNITY_FRAME_X", "RIGHT_SHANK_X_AXIS_UNITY_FRAME_Y", "RIGHT_SHANK_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_SHANK_Y_AXIS_UNITY_FRAME_X", "RIGHT_SHANK_Y_AXIS_UNITY_FRAME_Y", "RIGHT_SHANK_Y_AXIS_UNITY_FRAME_Z",
            "KNEE_CENTER_IN_FRAME_0_X",  "KNEE_CENTER_IN_FRAME_0_Y",  "KNEE_CENTER_IN_FRAME_0_Z",
            "PELVIC_CENTER_IN_FRAME_0_X",  "PELVIC_CENTER_IN_FRAME_0_Y",  "PELVIC_CENTER_IN_FRAME_0_Z",
            "CHEST_CENTER_IN_FRAME_0_X",  "CHEST_CENTER_IN_FRAME_0_Y",  "CHEST_CENTER_IN_FRAME_0_Z", 
            "HMD_POS_IN_FRAME_0_X", "HMD_POS_IN_FRAME_0_Y",  "HMD_POS_IN_FRAME_0_Z"
        */

        // Current time since game start
        frameDataToStore.Add(Time.time);

        // Current state
        // Convert state to float, then store
        float stateAsFloat = -1.0f;
        if (currentState == setupStateString)
        {
            stateAsFloat = 1.0f;
        }
        else if (currentState == waitingForEmgReadyStateString)
        {
            stateAsFloat = 2.0f;
        }
        else if (currentState == trialActiveState)
        {
            stateAsFloat = 3.0f;
        }
        else if (currentState == givingFeedbackStateString)
        {
            stateAsFloat = 4.0f;
        }
        else if (currentState == gameOverStateString)
        {
            stateAsFloat = 5.0f;
        }
        else
        {
            // invalid
        }

        // store the state
        frameDataToStore.Add(stateAsFloat);

        // Store the elapsed milliseconds on the state transition timer
        frameDataToStore.Add(stateTransitionStopwatch.ElapsedMilliseconds);

        // Whether or not the heaset has been toggled home
        bool hmdToggledHomeFlag = playerRepositioningScript.GetToggleHmdStatus();
        frameDataToStore.Add(System.Convert.ToSingle(hmdToggledHomeFlag));

        // Headset position (x,y,z)
        frameDataToStore.Add(headsetCameraGameObject.transform.position.x);
        frameDataToStore.Add(headsetCameraGameObject.transform.position.y);
        frameDataToStore.Add(headsetCameraGameObject.transform.position.z);

        // Headset orientation. Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 headsetXAxisUnityFrame, Vector3 headsetYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(headsetCameraGameObject);
        frameDataToStore.Add(headsetXAxisUnityFrame.x);
        frameDataToStore.Add(headsetXAxisUnityFrame.y);
        frameDataToStore.Add(headsetXAxisUnityFrame.z);
        frameDataToStore.Add(headsetYAxisUnityFrame.x);
        frameDataToStore.Add(headsetYAxisUnityFrame.y);
        frameDataToStore.Add(headsetYAxisUnityFrame.z);

        // Ref tracker position (x,y,z)
        frameDataToStore.Add(RefTracker.transform.position.x);
        frameDataToStore.Add(RefTracker.transform.position.y);
        frameDataToStore.Add(RefTracker.transform.position.z);

        // Ref tracker orientation: Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 refTrackerXAxisUnityFrame, Vector3 refTrackerYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(RefTracker);
        frameDataToStore.Add(refTrackerXAxisUnityFrame.x);
        frameDataToStore.Add(refTrackerXAxisUnityFrame.y);
        frameDataToStore.Add(refTrackerXAxisUnityFrame.z);
        frameDataToStore.Add(refTrackerYAxisUnityFrame.x);
        frameDataToStore.Add(refTrackerYAxisUnityFrame.y);
        frameDataToStore.Add(refTrackerYAxisUnityFrame.z);

        // Right controller position (x,y,z)
        frameDataToStore.Add(RightHand.transform.position.x);
        frameDataToStore.Add(RightHand.transform.position.y);
        frameDataToStore.Add(RightHand.transform.position.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 rightHandXAxisUnityFrame, Vector3 rightHandYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(RightHand);
        frameDataToStore.Add(rightHandXAxisUnityFrame.x);
        frameDataToStore.Add(rightHandXAxisUnityFrame.y);
        frameDataToStore.Add(rightHandXAxisUnityFrame.z);
        frameDataToStore.Add(rightHandYAxisUnityFrame.x);
        frameDataToStore.Add(rightHandYAxisUnityFrame.y);
        frameDataToStore.Add(rightHandYAxisUnityFrame.z);

        // Disable left hand since we're at max no. of trackers
        /*
        // Left controller position (x,y,z)
        frameDataToStore.Add(LeftHand.transform.position.x);
        frameDataToStore.Add(LeftHand.transform.position.y);
        frameDataToStore.Add(LeftHand.transform.position.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 leftHandXAxisUnityFrame, Vector3 leftHandYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(LeftHand);
        frameDataToStore.Add(leftHandXAxisUnityFrame.x);
        frameDataToStore.Add(leftHandXAxisUnityFrame.y);
        frameDataToStore.Add(leftHandXAxisUnityFrame.z);
        frameDataToStore.Add(leftHandYAxisUnityFrame.x);
        frameDataToStore.Add(leftHandYAxisUnityFrame.y);
        frameDataToStore.Add(leftHandYAxisUnityFrame.z);*/



        // Chest tracker position (x,y,z)
        frameDataToStore.Add(ChestTracker.transform.position.x);
        frameDataToStore.Add(ChestTracker.transform.position.y);
        frameDataToStore.Add(ChestTracker.transform.position.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 chestXAxisUnityFrame, Vector3 chestYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(ChestTracker);
        frameDataToStore.Add(chestXAxisUnityFrame.x);
        frameDataToStore.Add(chestXAxisUnityFrame.y);
        frameDataToStore.Add(chestXAxisUnityFrame.z);
        frameDataToStore.Add(chestYAxisUnityFrame.x);
        frameDataToStore.Add(chestYAxisUnityFrame.y);
        frameDataToStore.Add(chestYAxisUnityFrame.z);

        // Waist tracker position (x,y,z)
        frameDataToStore.Add(WaistTracker.transform.position.x);
        frameDataToStore.Add(WaistTracker.transform.position.y);
        frameDataToStore.Add(WaistTracker.transform.position.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 waistXAxisUnityFrame, Vector3 waistYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(WaistTracker);
        frameDataToStore.Add(waistXAxisUnityFrame.x);
        frameDataToStore.Add(waistXAxisUnityFrame.y);
        frameDataToStore.Add(waistXAxisUnityFrame.z);
        frameDataToStore.Add(waistYAxisUnityFrame.x);
        frameDataToStore.Add(waistYAxisUnityFrame.y);
        frameDataToStore.Add(waistYAxisUnityFrame.z);

        // Left ankle position
        frameDataToStore.Add(LeftAnkleTracker.transform.position.x);
        frameDataToStore.Add(LeftAnkleTracker.transform.position.y);
        frameDataToStore.Add(LeftAnkleTracker.transform.position.z);
        
        // Left ankle orientation unit vectors
        (Vector3 leftAnkleXAxisUnityFrame, Vector3 leftAnkleYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(LeftAnkleTracker);
        frameDataToStore.Add(leftAnkleXAxisUnityFrame.x);
        frameDataToStore.Add(leftAnkleXAxisUnityFrame.y);
        frameDataToStore.Add(leftAnkleXAxisUnityFrame.z);
        frameDataToStore.Add(leftAnkleYAxisUnityFrame.x);
        frameDataToStore.Add(leftAnkleYAxisUnityFrame.y);
        frameDataToStore.Add(leftAnkleYAxisUnityFrame.z);

        // Right ankle position
        frameDataToStore.Add(RightAnkleTracker.transform.position.x);
        frameDataToStore.Add(RightAnkleTracker.transform.position.y);
        frameDataToStore.Add(RightAnkleTracker.transform.position.z);

        // Right ankle orientation unit vectors
        (Vector3 rightAnkleXAxisUnityFrame, Vector3 rightAnkleYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(RightAnkleTracker);
        frameDataToStore.Add(rightAnkleXAxisUnityFrame.x);
        frameDataToStore.Add(rightAnkleXAxisUnityFrame.y);
        frameDataToStore.Add(rightAnkleXAxisUnityFrame.z);
        frameDataToStore.Add(rightAnkleYAxisUnityFrame.x);
        frameDataToStore.Add(rightAnkleYAxisUnityFrame.y);
        frameDataToStore.Add(rightAnkleYAxisUnityFrame.z);

        // Left shank position
        frameDataToStore.Add(LeftShankTracker.transform.position.x);
        frameDataToStore.Add(LeftShankTracker.transform.position.y);
        frameDataToStore.Add(LeftShankTracker.transform.position.z);

        // Left shank orientation unit vectors
        (Vector3 leftShankXAxisUnityFrame, Vector3 leftShankYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(LeftShankTracker);
        frameDataToStore.Add(leftShankXAxisUnityFrame.x);
        frameDataToStore.Add(leftShankXAxisUnityFrame.y);
        frameDataToStore.Add(leftShankXAxisUnityFrame.z);
        frameDataToStore.Add(leftShankYAxisUnityFrame.x);
        frameDataToStore.Add(leftShankYAxisUnityFrame.y);
        frameDataToStore.Add(leftShankYAxisUnityFrame.z);

        // Right shank position
        frameDataToStore.Add(RightShankTracker.transform.position.x);
        frameDataToStore.Add(RightShankTracker.transform.position.y);
        frameDataToStore.Add(RightShankTracker.transform.position.z);

        // Right shank orientation unit vectors
        (Vector3 rightShankXAxisUnityFrame, Vector3 rightShankYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(RightShankTracker);
        frameDataToStore.Add(rightShankXAxisUnityFrame.x);
        frameDataToStore.Add(rightShankXAxisUnityFrame.y);
        frameDataToStore.Add(rightShankXAxisUnityFrame.z);
        frameDataToStore.Add(rightShankYAxisUnityFrame.x);
        frameDataToStore.Add(rightShankYAxisUnityFrame.y);
        frameDataToStore.Add(rightShankYAxisUnityFrame.z);

        // Knee center pos in frame 0
        Vector3 kneeCenterPosFrame0 = viveTrackerDataManagerScript.GetKneeCenterInFrame0();
        frameDataToStore.Add(kneeCenterPosFrame0.x);
        frameDataToStore.Add(kneeCenterPosFrame0.y);
        frameDataToStore.Add(kneeCenterPosFrame0.z);

        // Waist center pos in frame 0
        Vector3 waistCenterPosFrame0 = viveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0();
        frameDataToStore.Add(waistCenterPosFrame0.x);
        frameDataToStore.Add(waistCenterPosFrame0.y);
        frameDataToStore.Add(waistCenterPosFrame0.z);

        // Chest center pos in frame 0
        Vector3 chestCenterPosFrame0 = viveTrackerDataManagerScript.GetChestCenterPositionInFrame0();
        frameDataToStore.Add(chestCenterPosFrame0.x);
        frameDataToStore.Add(chestCenterPosFrame0.y);
        frameDataToStore.Add(chestCenterPosFrame0.z);

        // Convert the HMD pos to frame 0, then store
        Matrix4x4 transformationUnityFrameToFrame0 = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame(), ref transformationUnityFrameToFrame0);
        Vector3 hmdPosInFrame0 = transformationUnityFrameToFrame0.MultiplyPoint3x4(headsetCameraGameObject.transform.position);
        frameDataToStore.Add(hmdPosInFrame0.x);
        frameDataToStore.Add(hmdPosInFrame0.y);
        frameDataToStore.Add(hmdPosInFrame0.z);

        //send all of this trial's summary data to the general data recorder
        generalDataRecorderScript.storeRowOfFrameData(frameDataToStore);
    }


    private (Vector3, Vector3, Vector3) GetGameObjectUnitVectorsInUnityFrame(GameObject objectOfInterest)
    {
        // Get the object's local unit x-, y- and z-axes converted to Unity frame
        Vector3 objectXAxisInGlobalFrame = objectOfInterest.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));
        Vector3 objectYAxisInGlobalFrame = objectOfInterest.transform.TransformDirection(new Vector3(0.0f, 1.0f, 0.0f));
        Vector3 objectZAxisInGlobalFrame = objectOfInterest.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f));

        // Return
        return (objectXAxisInGlobalFrame, objectYAxisInGlobalFrame, objectZAxisInGlobalFrame);

    }


    // This function stores a single trial's summary data by sending a "row" of data to the general data recorder. 
    private void StoreTrialData()
    {

        /*        string[] csvTrialDataHeaderNames = new string[]{
                "TRIAL_NUMBER",  "UPPER_ARM_LENGTH_METERS", "FOREARM_LENGTH_METERS",
                "HMD_START_POS_X_UNITY_FRAME", "HMD_START_POS_Y_UNITY_FRAME", "HMD_START_POS_Z_UNITY_FRAME",
                "HMD_START_POS_X_VICON_FRAME", "HMD_START_POS_Y_VICON_FRAME", "HMD_START_POS_Z_VICON_FRAME",
                "HMD_TOGGLE_HOME_VECTOR_X", "HMD_TOGGLE_HOME_VECTOR_Y", "HMD_TOGGLE_HOME_VECTOR_Z",
                "TRANSFORM_VICON_TO_TRACKER_1_1", "TRANSFORM_VICON_TO_TRACKER_1_2", "TRANSFORM_VICON_TO_TRACKER_1_3", "TRANSFORM_VICON_TO_TRACKER_1_4", 
                "TRANSFORM_VICON_TO_TRACKER_2_1", "TRANSFORM_VICON_TO_TRACKER_2_2", "TRANSFORM_VICON_TO_TRACKER_2_3", "TRANSFORM_VICON_TO_TRACKER_2_4", 
                "TRANSFORM_VICON_TO_TRACKER_3_1", "TRANSFORM_VICON_TO_TRACKER_3_2", "TRANSFORM_VICON_TO_TRACKER_3_3", "TRANSFORM_VICON_TO_TRACKER_3_4", 
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_3", 
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_4", 
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_3", 
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_4", 
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_3", 
                "TRANSFORM_VICON_TO_TRACKER_3_4", 
                "TRIAL_START_TIME", "TRIAL_END_TIME", "REACHING_DIRECTION_SPECIFIER", "REACHING_HEIGHT_SPECIFIER",
                "BALL_START_POS_X", "BALL_START_POS_Y", "BALL_START_POS_Z", "BALL_END_POS_X", "BALL_END_POS_Y", "BALL_END_POS_Z",
                "HAND_THAT_ACHIEVED_MAX_WAS_RIGHT_HAND_FLAG", "MAX_REACH_EXCURSION_THIS_TRIAL", "MAX_CHEST_EXCURSION_THIS_TRIAL"};*/

        // the list that will store the data
        List<float> trialDataToStore = new List<float>();

        // ADD: axis-angle error, directionValue (0 is right, 1 is left)

        // Get the trial #
        float trialNumber = (float)currentTrialNumber; //we only have trials for now. Should we implement a block format for excursion?
        trialDataToStore.Add(trialNumber);

        // Store subject-specific info: upper arm length, forearm length, and distance from chest to sternal notch (shoulders)
        trialDataToStore.Add(upperArmLengthInMeters);
        trialDataToStore.Add(forearmLengthInMeters);

        // The start position of the headset in Unity frame. This is enforced with the home toggle.
        (Vector3 playerViewStartPosition, _) = playerRepositioningScript.GetNeutralPlayerOrientationAndStartingPosition();
        trialDataToStore.Add(playerViewStartPosition.x);
        trialDataToStore.Add(playerViewStartPosition.y);
        trialDataToStore.Add(playerViewStartPosition.z);

        // The start position of the headset in Vicon frame. This is enforced with the home toggle.
        trialDataToStore.Add(hmdHomePositionInViconFrame.x);
        trialDataToStore.Add(hmdHomePositionInViconFrame.y);
        trialDataToStore.Add(hmdHomePositionInViconFrame.z);

        // Headset/Hmd offset from position at toggle home to desired home position
        Vector3 offsetHmdToggleStartToHomeVector = playerRepositioningScript.GetToggleHmdToHomePositionOffsetVector();
        trialDataToStore.Add(offsetHmdToggleStartToHomeVector.x);
        trialDataToStore.Add(offsetHmdToggleStartToHomeVector.y);
        trialDataToStore.Add(offsetHmdToggleStartToHomeVector.z);

        // Store the transformation from Vicon to the reference tracker.
        // This is technically saved in file already, but if we recalibrate, we'd like to be certain we 
        // have the transform that was actually used for the block. 
        // Row 1
        trialDataToStore.Add(transformationViconToReferenceTracker[0, 0]);
        trialDataToStore.Add(transformationViconToReferenceTracker[0, 1]);
        trialDataToStore.Add(transformationViconToReferenceTracker[0, 2]);
        trialDataToStore.Add(transformationViconToReferenceTracker[0, 3]);
        // Row 2
        trialDataToStore.Add(transformationViconToReferenceTracker[1, 0]);
        trialDataToStore.Add(transformationViconToReferenceTracker[1, 1]);
        trialDataToStore.Add(transformationViconToReferenceTracker[1, 2]);
        trialDataToStore.Add(transformationViconToReferenceTracker[1, 3]);
        // Row 3
        trialDataToStore.Add(transformationViconToReferenceTracker[2, 0]);
        trialDataToStore.Add(transformationViconToReferenceTracker[2, 1]);
        trialDataToStore.Add(transformationViconToReferenceTracker[2, 2]);
        trialDataToStore.Add(transformationViconToReferenceTracker[2, 3]);

        // Store the transformation from the subject midfoot frame (a frame with the some orientation as
        // the global Vicon frame, but located at the subject midfoot or center of base of support)
        // expressed in Unity frame TO the adjusted position of the midfoot frame. 
        // This transformation is what places the subject at the correct position in the VR environment.
        // Row 1
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[0, 0]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[0, 1]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[0, 2]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[0, 3]);
        // Row 2
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[1, 0]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[1, 1]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[1, 2]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[1, 3]);
        // Row 3
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[2, 0]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[2, 1]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[2, 2]);
        trialDataToStore.Add(transformationUnityGlobalFrameToMidfootSubjectFrame[2, 3]);

        // Store the trial start time
        trialDataToStore.Add(currentTrialStartTime);

        // Store the trial end time
        trialDataToStore.Add(currentTrialEndTime);

        // Store the ball direction angle from rightwards (CCW) in degrees for this trial
        trialDataToStore.Add(reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]]);

        // Store the ball height specifier (int) for this trial
        trialDataToStore.Add((float)reachingYValueOrderListThisBlock[currentTrialNumber]);

        // Store ball starting position this trial
        trialDataToStore.Add(currentTrialBallStartPos.x);
        trialDataToStore.Add(currentTrialBallStartPos.y);
        trialDataToStore.Add(currentTrialBallStartPos.z);

        // Store ball ending position this trial
        trialDataToStore.Add(currentTrialBallEndPos.x);
        trialDataToStore.Add(currentTrialBallEndPos.y);
        trialDataToStore.Add(currentTrialBallEndPos.z);

        // the hand that achieved the maximum reach for this trial direction
        trialDataToStore.Add(Convert.ToSingle(rightHandCausedFurthestPushThisTrial));

        // Store the max reaching excursion along the direction, in meters
        trialDataToStore.Add(bestReachingDistancesPerDirectionInTrial[currentTrialNumber]);

        // Store the max chest excursion along the direction, in meters
        trialDataToStore.Add(bestChestExcursionDistancesPerDirection[currentTrialNumber]);

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

        // Save out the run-specific excursion limits file
        generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();

        // Determine if the default reaching limits file already exists
        bool defaultReachLimitsFileExistsFlag = File.Exists(getDirectoryPath() + subdirectoryName + defaultExcursionPerformanceSummaryFileName);

       
        // Tell the general data recorder to write the excursion distances data to the default file name for saving/loading limits for all tasks,
        // if the limit file does NOT already exist or we are willing to overwrite them.
        if (!defaultReachLimitsFileExistsFlag || (defaultReachLimitsFileExistsFlag && overwriteExcursionLimits))
        {
            string sourcePath = generalDataRecorderScript.GetExcursionPerformanceDataFilePath(); ; // Get the time-stamped excursion peformance data file path
            string targetPath = getDirectoryPath() + subdirectoryName + defaultExcursionPerformanceSummaryFileName; // Specify the default/generic file path

            generalDataRecorderScript.CopyCsvFile(sourcePath, targetPath, overwriteExcursionLimits);
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


    public enum WhichSideSelectEnum
    {
        LeftSide,
        RightSide
    }

    private string getDirectoryPath()
    {
#if UNITY_EDITOR
        return Application.dataPath + "/CSV/";

#elif UNITY_STANDALONE
        return Application.dataPath + "/" ;
#else
        return Application.dataPath + "/";
#endif
    }


}
