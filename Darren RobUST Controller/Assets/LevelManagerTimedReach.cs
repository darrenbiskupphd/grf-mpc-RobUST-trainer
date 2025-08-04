using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below
using Debug = UnityEngine.Debug;

public class LevelManagerTimedReach : LevelManagerScriptAbstractClass
{
    
    // Declare class to load baseline reach trajectory data
    public class ReachDataReader
    {
        // The TrajectoryData struct stores a single data point from the trajectory history (i.e., 1 row from file)
        public struct TrajectoryData
        {
            public float time;
            public Vector3 handPosInFrame0;
            public float[] jointVars; // size 6
        }

        // This function takes in a reach direction and height, 
        // loads all data from file where each row is stored in a TrajectoryData struct, 
        // and returns a List<TrajectoryData> containing the entire trajectory history.
        public static List<TrajectoryData> LoadReachTrajectoryThisHeightDir(float reachDirDeg, int reachHeightID, string baselineTrajectoriesDirectoryFilePath)
        {
            string reachDirSafe = reachDirDeg.ToString("F1").Replace('.', '_');
            string fileName = $"reach_h_{reachHeightID}_dir_{reachDirSafe}_trajectory.csv";
            string filePath = Path.Combine(baselineTrajectoriesDirectoryFilePath, fileName);

            // Init the whole trajectory history for this reach height and dir
            List<TrajectoryData> trajectoryHistoryThisReachHeightAndDir = new List<TrajectoryData>();

            if (!File.Exists(filePath))
            {
                Debug.LogError($"File not found: {filePath}");
                return trajectoryHistoryThisReachHeightAndDir;
            }

            string[] lines = File.ReadAllLines(filePath);

            for (int i = 1; i < lines.Length; i++) // Skip header
            {
                string[] tokens = lines[i].Split(',');

                if (tokens.Length < 10) continue; // skip malformed lines

                
                TrajectoryData trajectoryDataPoint = new TrajectoryData()
                {
                    time = float.Parse(tokens[0]),
                    handPosInFrame0 = new Vector3(
                        float.Parse(tokens[1]),
                        float.Parse(tokens[2]),
                        float.Parse(tokens[3])),
                    jointVars = new float[6]
                };

                // Fill the joint variable data for this data point
                for (int j = 0; j < 6; j++)
                {
                    trajectoryDataPoint.jointVars[j] = float.Parse(tokens[4 + j]);
                }

                // Store this trajectory data point
                trajectoryHistoryThisReachHeightAndDir.Add(trajectoryDataPoint);
            }

            // Now that we've filled the trajectory history, return it
            return trajectoryHistoryThisReachHeightAndDir;
        }
    }


    // This class is used only to store the previous reach hand position history
    // for point assignment. NOT used for trajectory reconstruction or saving to file. 
    public class SingleReachHistoryStorageClass
    {
        // Class variables
        private List<HandTrajectoryData> mostRecentReachHistory = new List<HandTrajectoryData>();
        // Define the TrajectoryData struct
        public struct HandTrajectoryData
        {
            public float reachTimeInMs;
            public Vector3 handPosUnityFrame;
        }

        public void StoreCurrentTimeAndHandPosInUnityFrame(float timeSinceReachBeganInMs, Vector3 handPosInUnityFrame)
        {
            // Create a TrajectoryData object to store the most recent time and hand pos
            HandTrajectoryData trajectoryDataPoint = new HandTrajectoryData()
            {
                reachTimeInMs = timeSinceReachBeganInMs,
                handPosUnityFrame = handPosInUnityFrame
            };
            
            // Add the most recent TrajectoryData to the history list
            mostRecentReachHistory.Add(trajectoryDataPoint);
        }

        public List<HandTrajectoryData> GetMostRecentReachHistoryData()
        {
            return mostRecentReachHistory;
        }

        public void ClearReachHistoryData()
        {
            // Clear the history (at the end of a trial, after point assignment)
            mostRecentReachHistory.Clear();
        }
    }
    
    // INSTANCE VARIABLES ********************************************************************************************
    // Which side to conduct the task on
    public WhichSideSelectEnum whichSideToPerformTaskSelector;

    // Name of the task
    private string thisTaskNameString = "PacedReachingDifferentHeights";

    // Reach limits - needed to spawn ball at correct distance
    private string subdirectoryWithReachingLimitsData;
    private const string nameOfReachingLimitsTestTask = "ReachingDifferentHeights";
    private Dictionary<string, float> reachLimitsByHeightDir = new Dictionary<string, float>();
    private float fractionOfReachDistanceToSpawnBall = 0.80f;

    private float StartTime;
    private float CurrentTime;
    public GameObject TheBall;
    
    // The ball touch detector
    private BallTouchDetector ballTouchDetectorScript; 

    // Vive tracker game objects
    public GameObject LeftHand;
    public GameObject RightHand;
    public GameObject WaistTracker;
    public GameObject ChestTracker;
    public GameObject LeftAnkleTracker;
    public GameObject RightAnkleTracker;
    public GameObject headsetCameraGameObject;
    
    // Maximum distance from desired reaching hand start position for trial to begin
    private const float maxDistanceHandToDesiredStartPosInMeters = 0.1f; // meters

    // Reaching target (Ball) and hand radius (set in start)
    float ballRadius;
    float handSphereRadius;
    
    // Flags to store if either hand was touching the ball this unity frame
    private bool leftHandTouchingBallThisFrameFlag = false;
    private bool rightHandTouchingBallThisFrameFlag = false;
    
    // Reaching limits loader
    public LoadReachingAndLeaningLimits reachAndLeanLimitsLoaderScript;

    // Tracker in correct region for measurement flags
    private bool useRightControllerDistanceThisFrameFlag = false;
    private bool useLeftControllerDistanceThisFrameFlag = false;
    private bool useChestTrackerDistanceThisFrameFlag = false;

    // Player/HMD start position
    public Transform PlayerPosition;
    private MovePlayerToPositionOnStartup playerRepositioningScript;
    private bool playerToggledHomeFlag = false;
    private Vector3 startingPointOfThePlayer;

    // State machine states
    private string currentState;
    private string setupStateString = "SETUP";
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string instructionsStateString = "INSTRUCTIONS_STATE";
    private const string specificTrialInstructionsState = "TRIAL_INSTRUCTIONS_STATE";
    private string trialActiveOutgoingState = "TRIAL_ACTIVE_OUTGOING";
    private string trialActiveHoldState = "TRIAL_ACTIVE_HOLD";
    private string trialActiveIncomingState = "TRIAL_ACTIVE_INCOMING";
    private string givingFeedbackStateString = "FEEDBACK";
    private string gameOverStateString = "GAME_OVER";
    
    // Time in states
    private const float timeForReadingInSetup = 10000.0f; // milliseconds
    private const float trialSpecificInstructionsTimeInMs = 3000.0f;
    private float reachOutgoingTimeThisTrialInMs; // subject- and height/dir-specific!
    private float pacedReachWaitTimeAfterObservingTrajectoryBeforeStartInMs = 2000.0f;
    private float reachHoldTimeThisTrialInMs = 2000.0f; // constant for all trials.
    private float reachIncomingTimeThisTrialInMs; // subject- and height/dir-specific!
    private const float timeForFeedbackInMs = 5000f; // milliseconds
    
    // Instructions
    private const string generalInstructionsSelfPaced =
        "Reach out and touch the target! \n Please reach at a \n comfortable pace.";
    private const string generalInstructionsIndicatorPaced =
        "Follow the movement \n to the target!";
    private const string trialSpecificPrefix = "The target is \n";

    // Trial and block structure
    private int trialsPerBlock; // a trial is one reach
    public int trialsPerDirectionPerYValue;
    private int currentTrialNumber = 0; // the current trial number

    // Overall reaching direcitions in this game 
    public float[] reachingDirectionsInDegreesCcwFromRight; // the reaching angles specified CCW from the rightwards direction (player's right)
    private string[] reachingDirectionsAsStrings; // a string representation of the reaching directions, initialized in Start();
    private int[] reachingDirectionOrderListThisBlock; // this array stores the reaching direction for all trials this block.
    private int[] reachingYValueOrderListThisBlock; // the positional y-values
    public String[] reachingTargetYHeightsSpecifierStrings; // this array stores the 3 preset categories of reaching target height for all trials this block.
    private float[] reachingTargetYHeightsSpecifierValues; // stores the y-pos values corresponding to the string values in reachingTargetYHeightsSpecifierStrings.

    // Storing some trial-specific variables
    private float currentTrialStartTime;
    private float currentTrialEndTime;
    private Vector3 currentTrialBallStartPos;
    private bool rightHandCausedFurthestPushThisTrial = false;
    
    // The reach timer stopwatch
    private Stopwatch reachTimerStopwatch = new Stopwatch();
    
    // Storage for the baseline trajectory at the "slow" speed
    private List<Vector3> reachingHandSlowTrajectoryHistory = new List<Vector3>();
    private List<float> reachingHandSlowTrajectoryTimeStamps = new List<float>();
    // Store how many times the subject has reached along this reach height and dir. 
    // It will be initialized with zeros and incremented at the START of each trial. 
    // First index = reach direction
    // Second index = reach height
    private List<List<int>> trialOccurrenceThisReachHeightDir = new List<List<int>>();
    



    // Reaching direction order for the current trial
    // The integer value in each element corresponds to the index
    // of the reaching direction in the reachingDirectionsInDegreesCcwFromRight variable


    // Pseudorandom number generator for randomizing reach order
    private static System.Random randomNumberGenerator = new System.Random();

    // trial transition timer
    private Stopwatch stateTransitionStopwatch = new Stopwatch();
    private float baselineReachTimeThisHeightDirInMs; // the baseline time the subject took to complete the reach at this dir/height.
    private float timeAllowedToPerformReachInMs = 6000.0f; // If we're replaying a baseline trajectory, this is set on a trial-specific basis
                                                            // and it reflects the time the reach took in baseline AND
                                                            // the playback speed multiplier. 
    private float baselineTrajectoryPlaybackSpeedMultiplier = 2.0f; // speed scaling factor for playing back the baseline trajectory.
    private float timeToPerformMostRecentTrialInMs; // track time to perform the most recent trial in milliseconds
    
    // Points earned
    private float maxPointsEarnedInATrial = 100.0f;

    // feedback
    private string[] textPerDirection = new string[] { "Right",
                                                        "Forward-Right",
                                                        "Forward",
                                                        "Forward-Left",
                                                        "Left"};

    private string[] textPerHeight = new string[]
    {
        "Waist-level",
        "Chest-level",
        "Eye-level"
    };

    private bool Right0Flag = false;
    private bool Right45Flag = false;
    private bool Left0Flag = false;
    private bool Left45Flag = false;
    private bool StraightForwardFlag = false;
    private bool TestEnding = false;

    private bool WaitForEveryThingReadyFlag = true;
    private bool RuleFlag = false;
    public float BallsRebornRadius = 3f;

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

    public int TrialNumber;//how  many times you want to do in each direction
    private int TrialIndex;

    public GameObject generalDataRecorder;
    private GeneralDataRecorder generalDataRecorderScript;
    public GameObject subjectSpecificData;
    private SubjectInfoStorageScript subjectSpecificDataScript;

    // subject-specific distances
    private float upperArmLengthInMeters;
    private float forearmLengthInMeters;
    private float chestToSternalNotchInMeters;

    // Offsets to the desired hand start position, using fractions of the upper limb length
    private float verticalOffsetWaistLevelReachAsFractionOfArmLength = 0.15f; // e.g., 0.15 = a vertical offset of 15% of the arm length
    private float verticalOffsetChestAndEyeReachAsFractionOfArmLength = 0.35f; // e.g., 0.15 = a vertical offset of 15% of the arm length
    private float groundPlaneOffsetAllTargetsAsFractionOfArmLength = 0.50f; // e.g., 0.50 = a radial/ground plane offset of 50% of the arm length


    public Text instructionsText;
    public Text pointsText; // how many points total
    private uint pointsEarnedSoFar = 0;
    private float pointsEarnedThisTrial;
    public Canvas timeRemainingInReachTextCanvas;
    public Text timeRemainingInReachText;

    // Using EMGs?
    public bool streamingEmgDataFlag; // NOTE: Not really implemented in this version of the task. If you want EMGs, refer to the "uptown" version we implemented for the pelvis/chest coord. study.

    // Tracking interactions with the ball (updated each frame)
    private bool lastFrameBallTouchedFlag = false; // whether or not the ball is in contact with EITHER hand.
    private bool leftHandCouldPushReachTarget = false; // if the ball is in contact with the left hand
    private bool rightHandCouldPushReachTarget = false;  // if the ball is in contact with the right hand

    // Player reorientation controls. 
    public bool waitForToggleHomeToStartInstructions; // whether the game should require the experimenter to toggle the player home to start (true) or not (false)
    private bool hmdToggledToHomeByExperimenter = false; // whether or not the experimenter has toggled the player home this run
    

    
    // Store the curernt reaching hand position
    private Vector3 currentReachingHandPositionUnityFrame;
    
    // If using, variables to store the loaded height/dir-specific baseline reaching hand trajectory
    private List<float> baselineReachingHandTimestampsThisHeightDir;
    private List<Vector3> baselineReachingHandPositionHistoryThisHeightDir;
    
    // Enum to specify if we're either 1.) storing hand and postural info for the last trial in a given reach height/dir or 2.) loading baseline trajectory data
    public ReadOrStoreBaselineTrajectories selectReadOrStoreBaselineTrajectories;
    // We only store the baseline trajectory if it is the last trial for the given reach height and dir. 
    // Maintain a flag indicating if that is the case for the current trial. 
    private bool lastTrialThisReachHeightAndDirFlag = false;
    // Maintain a dictionary with key = tuple of (reachDirInDegs, reachHeightAsIntSpecifier) and an int value of how many reaches we've done in that direction. 
    // If it's the last reach, then we can set the flag to store. 
    private Dictionary<(float, int), int> countTrialsPerReachDirAndHeight = new Dictionary<(float, int), int>();
    
    // Writing the baseline reaching trajectories to file
    private string baselineTrajectoriesDirectoryFilePath;
    private string filePathBaselineTrajectoriesThisHeightDir; // the subject-specific trajectory, unique for each reach height/dir!
    
    // The reaching hand target position (moving -> the trajectory!)
    public GameObject reachingHandDesiredPositionIndicator;
    public Text timeToStartDesiredReachTrajectoryText; 
    
    // The reaching hand target start position indicator
    public GameObject reachingHandDesiredStartPosIndicator; // marks start position only
    
    // Loading the baseline reach trajectories. 
    // A struct to store the current loaded trajectory for the current reach dir/height. 
    private List<ReachDataReader.TrajectoryData> trajectoryHistoryThisReachHeightAndDir;
    // Rendering the baseline reach trajectory with a dashed line.
    public LineRenderer desiredReachTrajectoryLineRenderer;
    
    // Create a hand trajectory storage class object for point assignment after each trial. 
    private SingleReachHistoryStorageClass mostRecentReachHandTrajectoryForScoring = new SingleReachHistoryStorageClass();
    private const float minimumVelocityForReachInitiationWhenScoring = 0.1f; // m/s
    private const float maximumPointsEarnedPerTrial = 100.0f;
    private const float maxMeanDistanceThresholdToEarnNoPointsInMeters = .25f; // m
    private const float minMeanDistanceThresholdToEarnFullPointsInMeters = 0.02f; //m

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

    // Vive tracker data manager
    public ViveTrackerDataManager viveTrackerDataManagerScript;
    private bool viveTrackerDataInitializedFlag = false;

    // Startup support script (load needed data, set up RobUST, interface with hardware, etc.)
    public LevelManagerStartupSupportScript levelManagerStartupSupportScript;
    private bool startupSupportScriptCompleteFlag = false;

    void Start()
    {
        StartTime = Time.time;
        CurrentTime = Time.time;

        // If the reach is self-paced
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
        {
            instructionsText.text = generalInstructionsSelfPaced;
        }
        // Else if the reach is paced by the indicator
        else
        {
            instructionsText.text = generalInstructionsIndicatorPaced;
        }
        
        // Based on which side we want to test on, include the forward direction
        // and the two reach directions on that side. 
        // We do so by removing elements from XXX that we don't need. 
        // If left hand
        if (whichSideToPerformTaskSelector == WhichSideSelectEnum.LeftSide)
        {
            // Reaching on forward and left side is 90, 135, 180 degree directions
            reachingDirectionsInDegreesCcwFromRight = new float[]
            {
                reachingDirectionsInDegreesCcwFromRight[2],
                reachingDirectionsInDegreesCcwFromRight[3],
                reachingDirectionsInDegreesCcwFromRight[4]
            };
            
            // Also select the correct text per direction values by removing unused ones
            textPerDirection = new string[] {
                textPerDirection[2],
                textPerDirection[3],
                textPerDirection[4]
            };
        }
        else
        {
            // Reaching on forward and right side is 0, 45, 90 degree directions
            reachingDirectionsInDegreesCcwFromRight = new float[]
            {
                reachingDirectionsInDegreesCcwFromRight[0],
                reachingDirectionsInDegreesCcwFromRight[1],
                reachingDirectionsInDegreesCcwFromRight[2]
            };
            
            // Also select the correct text per direction values by removing unused ones
            textPerDirection = new string[] {
                textPerDirection[0],
                textPerDirection[1],
                textPerDirection[2]
            };
        }
        
        // Get the ball touch detector
        ballTouchDetectorScript = TheBall.GetComponent<BallTouchDetector>();

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
        trialsPerBlock = trialsPerDirectionPerYValue * reachingTargetYHeightsSpecifierStrings.Length * reachingDirectionsInDegreesCcwFromRight.Length;

        // Initalize best distances per direction
        bestReachingDistancesPerDirectionInTrial = new float[trialsPerBlock];
        bestChestExcursionDistancesPerDirection = new float[trialsPerBlock];

        // Set a timer that will transition us out of the setup state
        stateTransitionStopwatch.Start();

        // Make the balls spawn at some percentage of the upper limb length (NO! Make them spawn at some percent of the H/D specific reaching distance).
        //ballSpawnRadius = ballSpawnFractionUpperLimbLength * (subjectSpecificDataScript.getUpperArmLengthInMeters() + subjectSpecificDataScript.getForearmLengthInMeters());

        // Get ball and hand object radius
        ballRadius = (TheBall.transform.localScale.x / 2.0f); // Default diameter is 1.0m, so the radius is 0.5 * the scaling factor (Assuming a sphere)
        handSphereRadius = RightHand.transform.localScale.x / 2.0f;

        setFrameAndTrialDataNaming();

        // Build the path to the neutral posture file and load the neutral posture
        medianPostureCsvPath = getDirectoryPath() + subdirectoryNeutralPoseDataString + neutralPoseTaskFileName;
        LoadNeutralPostureFromCsv(medianPostureCsvPath);

        // Assign subject-specific distances
        upperArmLengthInMeters = subjectSpecificDataScript.getUpperArmLengthInMeters();
        forearmLengthInMeters = subjectSpecificDataScript.getForearmLengthInMeters();
        chestToSternalNotchInMeters = subjectSpecificDataScript.getVerticalDistanceChestToShouldersInMeters();
        
        // tell reaching limits loader to load reaching limits data
        reachAndLeanLimitsLoaderScript.loadBoundaryOfStability(subdirectoryWithReachingLimitsData);
        reachLimitsByHeightDir = reachAndLeanLimitsLoaderScript.GetReachingLimits();
        
        // Set the text above the ball to the empty string
        timeRemainingInReachText.text = "";

    Debug.Log("Atan2 test, expect a negative value in radians close to -pi: " + Mathf.Atan2(-0.01f, -.99f));

        //StartCoroutine(Sparwn());
    }


    //FixedUpdate Should Always Record The Maximum of the Reaching Distance;
    void FixedUpdate()
    {
        //float timestep = Time.time - CurrentTime;

        //ManageStateTransitionStopwatch();
        // Now depending on the current state
        if (currentState == setupStateString)
        {

            // See if the level manager startup support script has finished. 
            // Only do task-specific setup if it has been completed. 
            if (startupSupportScriptCompleteFlag == false)
            {
                startupSupportScriptCompleteFlag = levelManagerStartupSupportScript.GetServicesStartupCompleteStatusFlag();
            }

            // See if the Vive tracker data manager is ready to serve data
            if (viveTrackerDataInitializedFlag == false)
            {
                viveTrackerDataInitializedFlag = viveTrackerDataManagerScript.GetViveTrackerDataHasBeenInitializedFlag();
            }

            // See if the player has been toggled home
            if (playerToggledHomeFlag == false)
            {
                playerToggledHomeFlag = playerRepositioningScript.GetToggleHmdStatus();

                // If the player has just been toggled home
                if (playerToggledHomeFlag == true)
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
                if (levelManagerStartupSupportScript.GetSyncingWithExternalHardwareFlag() == true)
                {
                    levelManagerStartupSupportScript.TellPhotonToPulseSyncPin();
                }

                // Change to the instructions state
                changeActiveState(instructionsStateString);

            }
        }
        // If we're currently displaying instructions at task start
        else if (currentState == instructionsStateString)
        {
            storeFrameData();

            if(waitForToggleHomeToStartInstructions == true)
            {
                bool toggledHome = playerRepositioningScript.GetToggleHmdStatus();
                if (toggledHome == true) // if the experimenter has toggled the subject to the home position
                {
                    // If the instance variable flag tracking the toggleHome state is still false
                    // NOTE: code inside the conditional will only run once.
                    if(hmdToggledToHomeByExperimenter == false)
                    {
                        // Note that the player has been toggled home
                        hmdToggledToHomeByExperimenter = true;
                        
                        // Reset the stopwatch so that the Instructions time starts from this moment (the moment of toggling home)
                        stateTransitionStopwatch.Restart();
                        
                        // Recompute ball heights on tracker and HMD y-axis position, now that the headset is on the head and the subject is in neutral position
                        AssignReachingTargetHeightsBasedOnTrackerPos();
                    }

                    // If the instructions state time has elapsed
                    if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForReadingInSetup)
                    {
                        changeActiveState(specificTrialInstructionsState);
                    }
                }
                else
                {
                    // Restart the state transition stopwatch to 0. We do not want to truly start the Instructions timer until 
                    // the person has been toggled home.
                    stateTransitionStopwatch.Restart();
                }
            }
            else // if we do not want to wait for the subject to be toggled to the home position
            {
                // Transition out of the Instructions state as soon as the time is up.
                if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForReadingInSetup)
                {
                    changeActiveState(specificTrialInstructionsState);
                }
            }
        }
        // If we are providing a window of time to read which height/dir the target will appear in
        else if (currentState == specificTrialInstructionsState)
        {   
            // We'll require a hand start position for BOTH the baseline recorded reach trajectory and the 
            // paced trajectory. 
            // If the hand is further than a minimum distance from the desired start trajectory, then 
            // reset the timer. 
            Vector3 currentReachingHandPos = new Vector3();
            if (whichSideToPerformTaskSelector == WhichSideSelectEnum.RightSide)
            {
                currentReachingHandPos = RightHand.transform.position;
            }else if (whichSideToPerformTaskSelector == WhichSideSelectEnum.LeftSide)
            {
                currentReachingHandPos = LeftHand.transform.position;
            }

            // If the hand is too far from the desired start position
            if (Vector3.Distance(currentReachingHandPos, reachingHandDesiredStartPosIndicator.transform.position) >
                maxDistanceHandToDesiredStartPosInMeters)
            {
                // Restart the stopwatch 
                stateTransitionStopwatch.Restart();
            }
            
            // Render the current time until the hand trajectory starts as text above the reach indicator
            timeToStartDesiredReachTrajectoryText.text = ((trialSpecificInstructionsTimeInMs - stateTransitionStopwatch.ElapsedMilliseconds) / 1000f).ToString("F1") + " s";
            
            // If the time for the outgoing portion of the reach has elapsed, switch to the active hold state
            if (stateTransitionStopwatch.ElapsedMilliseconds >= trialSpecificInstructionsTimeInMs)
            {
                changeActiveState(trialActiveOutgoingState);
            }
        }
        // If we're currently measuring reaching distance along some direction
        else if (currentState == trialActiveOutgoingState)
        {
            // Track reach progress, update the reach trajectory indicator (if using)
            (bool reachTargetTouchedFlag, Vector3 currentReachingHandPos) = TrackOutgoingReachProgressAndUpdateTrajectory();

            // Store the frame data
            storeFrameData();
            
            // Transition out of the state, if needed
            // If we're storing the baseline trajectory, then we transition when the reach target is touched
            if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
            {
                // if the reach target has been touched
                if (reachTargetTouchedFlag == true)
                {
                    // Change directly to the feedback state
                    changeActiveState(givingFeedbackStateString);
                }
            }
            // Else if we're reading the baseline trajectory and playing it back
            else
            {
                if (stateTransitionStopwatch.IsRunning)
                {
                    // Update the time shown on the desired reach indicator, which now counts down to subject reach start
                    float secondsBeforeReachStart =
                        (pacedReachWaitTimeAfterObservingTrajectoryBeforeStartInMs -
                        stateTransitionStopwatch.ElapsedMilliseconds) / 1000.0f;
                    // Stop countdown at 0
                    if (secondsBeforeReachStart < 0.0f)
                    {
                        secondsBeforeReachStart = 0.0f;
                    }
                    timeToStartDesiredReachTrajectoryText.text = secondsBeforeReachStart.ToString("F1") + " s";
                    
                    // If the hand is too far from the desired start position
                    if (Vector3.Distance(currentReachingHandPos, reachingHandDesiredStartPosIndicator.transform.position) >
                        maxDistanceHandToDesiredStartPosInMeters)
                    {
                        // Restart the stopwatch 
                        stateTransitionStopwatch.Restart();
                    }
                    
                    // If the time for the outgoing portion of the reach has elapsed
                    if (stateTransitionStopwatch.ElapsedMilliseconds >= pacedReachWaitTimeAfterObservingTrajectoryBeforeStartInMs)
                    {
                        // If the target has been touched 
                        if (reachTargetTouchedFlag == true)
                        {
                            // Transition to the hold state
                            changeActiveState(trialActiveHoldState);
                        }
                    }
                }
            }
        }else if (currentState == trialActiveHoldState)
        {
            // Store the frame data
            storeFrameData();
            
            // Hold time remaining in seconds
            float holdTimeRemainingInSeconds =
                ((reachHoldTimeThisTrialInMs - stateTransitionStopwatch.ElapsedMilliseconds) / 1000.0f);
            
            // Update the hold text
            if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.ReadBaseline)
            {
                // Tell the subject that they can start the reach movement.
                timeToStartDesiredReachTrajectoryText.text = "Hold! \n "  + holdTimeRemainingInSeconds.ToString("F1") + " s";
            }
            
            // If the time for the hold portion of the reach has elapsed, switch to the active incoming state. 
            // Note: this state is NOT used when we are storing baseline reach trajectory data
            if (stateTransitionStopwatch.ElapsedMilliseconds >= reachHoldTimeThisTrialInMs)
            {
                changeActiveState(trialActiveIncomingState);
            }

        }else if (currentState == trialActiveIncomingState)
        {
            // Track reach progress, update the reach trajectory indicator (if using)
            TrackIncomingReachProgressAndUpdateTrajectory();
            
            // Store the frame data
            storeFrameData();
            
            // If the time for the hold portion of the reach has elapsed, switch to the active incoming state.
            // Note: this state is NOT used when we are storing baseline reach trajectory data.
            if (stateTransitionStopwatch.ElapsedMilliseconds >= reachIncomingTimeThisTrialInMs)
            {
                changeActiveState(givingFeedbackStateString);
            }
        }
        else if (currentState == givingFeedbackStateString)
        {
            
            // Currently the feedback state just provides buffer time. No feedback is given.
            if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForFeedbackInMs)
            {
                // if there are trials remaining
                if (currentTrialNumber < trialsPerBlock - 1)
                {
                    // switch states
                    changeActiveState(specificTrialInstructionsState);
                }
                // if the block is over
                else
                {
                    // switch states to game over, since we only ever do 1 block.
                    changeActiveState(gameOverStateString);
                }
            }
            
            // Store the frame data
            storeFrameData();
        }
        else if (currentState == gameOverStateString)
        {
            storeFrameData();
        }
        else
        {

        }
    }


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



    private (bool, Vector3) TrackOutgoingReachProgressAndUpdateTrajectory()
    {
        // Get the current target direction
        float currentReachDirection = reachingDirectionOrderListThisBlock[currentTrialNumber];
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
        
        // Store if either hand is touching the ball
        //leftHandTouchingBallThisFrameFlag = ballTouchDetectorScript.GetLeftHandTouchingBallFlag();
        rightHandTouchingBallThisFrameFlag = ballTouchDetectorScript.GetRightHandTouchingBallFlag();
        
        Debug.Log("Current reach direction: " + currentReachDirection + ", current forward dir specifier: " + forwardDirSpecifier);
        
        // If the target is on the right
        if (currentReachDirection < forwardDirSpecifier)
        {
            // Query the ball to see if the right hand contacted the ball
            ballTouchedFlag = rightHandTouchingBallThisFrameFlag;
            
            // Store the right hand position as the reaching hand position
            currentReachingHandPositionUnityFrame = RightHand.transform.position;

        }
        // else if the target is on the left
        else if(currentReachDirection > forwardDirSpecifier) 
        {
            // Query the ball to see if the left hand contacted the ball
            ballTouchedFlag = leftHandTouchingBallThisFrameFlag;
            
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
                
                // Store the left hand position as the reaching hand position
                currentReachingHandPositionUnityFrame = LeftHand.transform.position;
            }
            else // if the right side
            {
                // Query right hand
                ballTouchedFlag = rightHandTouchingBallThisFrameFlag;
                
                // Store the right hand position as the reaching hand position
                currentReachingHandPositionUnityFrame = RightHand.transform.position;
            }
        }
        
        // If the target was reached
        if (ballTouchedFlag == true)
        {
            // Store the time it took to complete the reach
            timeToPerformMostRecentTrialInMs = reachTimerStopwatch.ElapsedMilliseconds;
        }

        float elapsedMillisecondsThisReach = reachTimerStopwatch.ElapsedMilliseconds;
        
        // Get the effective elapsed milliseconds this reach
        float effectiveElapsedMillisecondsThisReach =
            elapsedMillisecondsThisReach * baselineTrajectoryPlaybackSpeedMultiplier;
        
        // Clamp the "effective" elapsed time in the reach to be less than or equal to the total reach time allowed.
        if (effectiveElapsedMillisecondsThisReach > baselineReachTimeThisHeightDirInMs)
        {
            effectiveElapsedMillisecondsThisReach = baselineReachTimeThisHeightDirInMs;
        }
        
        // Format a string showing the time remaining in this reach
        //string timeRemainingInSecondsString = (timeRemaining / 1000.0f).ToString("F2");
        //timeRemainingInReachText.text = timeRemainingInSecondsString + " s";
        
        // Store the hand position and time since reach began for scoring purposes
        mostRecentReachHandTrajectoryForScoring.StoreCurrentTimeAndHandPosInUnityFrame(reachTimerStopwatch.ElapsedMilliseconds, currentReachingHandPositionUnityFrame);
        
        // If we're storing the baseline trajectory for the current subject
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
        {
            // If this is the last trial for the current reach height/dir combo
            if (lastTrialThisReachHeightAndDirFlag)
            {
                // Call the function that stores the subject-specific progression data for this reach height/dir combo. 
                // Note: this function writes directly to file, so there's no need to "save" at the end of the trial.
                LogSubjectSpecificReachProgressionData();
            }
        }
        // else if we're reading the baseline trajectory
        else if(selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.ReadBaseline)
        {
        
            // Loop through the baseline trajectory history to get the current hand position using the current time progress proportion.
            int indexOfCorrespondingHandPositionAtDesiredTime = -1; // dummy value is -1
            bool historyIndexFoundFlag = false;
            for (int baselineTimeStampIndex = 0;
                 baselineTimeStampIndex < trajectoryHistoryThisReachHeightAndDir.Count;
                 baselineTimeStampIndex++)
            {
                // Find the nearest time stamp greater than or equal to the desired time stamp
                if ((trajectoryHistoryThisReachHeightAndDir[baselineTimeStampIndex].time >=
                     effectiveElapsedMillisecondsThisReach) && (historyIndexFoundFlag == false))
                {
                    // Store the index of the time stamp
                    indexOfCorrespondingHandPositionAtDesiredTime = baselineTimeStampIndex;
                
                    // Set flag saying we found the needed index
                    historyIndexFoundFlag = true;
                }
            }
            
            Debug.Log("Outgoing trajectory history index right now is: " + indexOfCorrespondingHandPositionAtDesiredTime);

            // Get the desired hand pos in frame 0
            Vector3 currentDesiredHandPosFrame0 =
                trajectoryHistoryThisReachHeightAndDir[indexOfCorrespondingHandPositionAtDesiredTime].handPosInFrame0;
            
            // Transform the hand position from frame 0 to Unity frame.
            // TEMP ONLY - set to identity.
            Matrix4x4 transformationUnityFrameToFrame0 = Matrix4x4.identity;
            Matrix4x4 transformationFrame0ToUnityFrame = transformationUnityFrameToFrame0.inverse;
            Vector3 currentDesiredHandPosUnityFrame =
                transformationFrame0ToUnityFrame.MultiplyPoint(currentDesiredHandPosFrame0);
            
            // Update the desired position of the reaching hand indicator
            reachingHandDesiredPositionIndicator.transform.position = currentDesiredHandPosUnityFrame;
            
            // If the hand has reached the end of its desired trajectory
            if (indexOfCorrespondingHandPositionAtDesiredTime >= trajectoryHistoryThisReachHeightAndDir.Count - 1)
            {
                // Start the state transition stopwatch, which will be used to count down an extra period 
                // after the desired reach trajectory has been shown, ensuring the subject observes the ball movement.
                stateTransitionStopwatch.Restart();
            }
        }
        
        // return if the reach target has been touched (true) or not (false)
        return (ballTouchedFlag, currentReachingHandPositionUnityFrame);
    }
    
    void WriteHeader()
    {
        string header = "elapsedTimeInReachInMs,ReachHandFrame0PosX,ReachHandFrame0PosY,ReachHandFrame0PosZ,theta1,theta2,d3,theta4,theta5,d6";
        File.WriteAllText(filePathBaselineTrajectoriesThisHeightDir, header + "\n");
    }
    
    // Log frame data 
    void LogSubjectSpecificReachProgressionData()
    {
        float time = reachTimerStopwatch.ElapsedMilliseconds;
        
        // Get the transformation from Unity frame to frame 0.
        // TEMP ONLY - set to Identity matrix
        Matrix4x4 transformationUnityFrameToFrame0 = Matrix4x4.identity;
        
        // Get the reaching hand position in frame 0
        Vector3 handPosInFrame0 = transformationUnityFrameToFrame0.MultiplyPoint(currentReachingHandPositionUnityFrame);
        
        // Get 6-DOF model joint values (theta1, theta2, d3, theta4, theta5, d6)
        float[] jointVals = new float[6]; //GetJointValues(); // Should return 6 floats

        string line = string.Format("{0:F4},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4}",
            time,
            handPosInFrame0.x, handPosInFrame0.y, handPosInFrame0.z,
            jointVals[0], jointVals[1], jointVals[2],
            jointVals[3], jointVals[4], jointVals[5]);

        File.AppendAllText(filePathBaselineTrajectoriesThisHeightDir, line + "\n");
    }
    
    
    // Draw the loaded reach trajectory
    void DrawTrajectoryLine(List<ReachDataReader.TrajectoryData> trajectoryHistory)
    {
        // If the trajectory is empty and we failed to load it.
        if (trajectoryHistory == null || trajectoryHistory.Count == 0)
        {
            Debug.LogWarning("No trajectory data to render.");
            desiredReachTrajectoryLineRenderer.positionCount = 0;
            return;
        }
        
        // Empty the desired trajectory line renderer
        desiredReachTrajectoryLineRenderer.positionCount = 0;

        // Init the proper number of points in the line renderer
        desiredReachTrajectoryLineRenderer.positionCount = trajectoryHistory.Count;

        // Set all the line renderer positions
        for (int i = 0; i < trajectoryHistory.Count; i++)
        {
            desiredReachTrajectoryLineRenderer.SetPosition(i, trajectoryHistory[i].handPosInFrame0);
        }
    }


    private void TrackIncomingReachProgressAndUpdateTrajectory()
    {
        // Get the time remaining in the reach, both in ms and as a proportion of the total time
        float elapsedMillisecondsThisReach = reachTimerStopwatch.ElapsedMilliseconds;
        
        // Get the effective elapsed milliseconds this reach. 
        // Note, since we're playing the trajectory in reverse, we subtract elapsed time from the total reach time.
        float effectiveElapsedMillisecondsThisReach =
            baselineReachTimeThisHeightDirInMs - elapsedMillisecondsThisReach * baselineTrajectoryPlaybackSpeedMultiplier;
        
        // Clamp the "effective" elapsed time in the reach to be greater than zero
        if (effectiveElapsedMillisecondsThisReach < 0.0f)
        {
            effectiveElapsedMillisecondsThisReach = 0.0f;
        }
        
        // Format a string showing the time remaining in this reach
        //string timeRemainingInSecondsString = (timeRemaining / 1000.0f).ToString("F2");
        //timeRemainingInReachText.text = timeRemainingInSecondsString + " s";
        
        // If we're reading the baseline trajectory
        if(selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.ReadBaseline)
        {
            // Loop through the baseline trajectory history to get the current hand position using the current time progress proportion.
            int indexOfCorrespondingHandPositionAtDesiredTime = -1; // dummy value is -1
            for (int baselineTimeStampIndex = 0;
                 baselineTimeStampIndex < trajectoryHistoryThisReachHeightAndDir.Count;
                 baselineTimeStampIndex++)
            {
                // Find the nearest time stamp greater than or equal to the desired time stamp
                if (trajectoryHistoryThisReachHeightAndDir[baselineTimeStampIndex].time >=
                    effectiveElapsedMillisecondsThisReach)
                {
                    // Store the index of the time stamp
                    indexOfCorrespondingHandPositionAtDesiredTime = baselineTimeStampIndex;
                
                    // Break out of the loop
                    break;
                }
            }

            // Get the time stamp and reaching hand position we want to render now
            Vector3 currentDesiredHandPosFrame0 =
                trajectoryHistoryThisReachHeightAndDir[indexOfCorrespondingHandPositionAtDesiredTime].handPosInFrame0;
            
            // Transform the hand position from frame 0 to Unity frame.
            // TEMP ONLY - set to identity.
            Matrix4x4 transformationUnityFrameToFrame0 = Matrix4x4.identity;
            Matrix4x4 transformationFrame0ToUnityFrame = transformationUnityFrameToFrame0.inverse;
            Vector3 currentDesiredHandPosUnityFrame = transformationFrame0ToUnityFrame.MultiplyPoint(currentDesiredHandPosFrame0);
            
            // Update the reaching hand desired position indicator
            reachingHandDesiredPositionIndicator.transform.position = currentDesiredHandPosUnityFrame;
        }
    }



    private float ConvertAngleDependingOnCurrentReachAngle(float angleToConvertInDegrees, float currentReachAngleInDegrees)
    {
        // If current reach angle is near 180 degrees, then convert the negative angles to be between 180 and 360 (instead of 0 to -180)
        float convertedAngle = angleToConvertInDegrees;
        if (currentReachAngleInDegrees >= 180.0f - 30.0f && currentReachAngleInDegrees <= 180.0f + 30.0f)
        {
            if (angleToConvertInDegrees < 0) // if the angle is negative
            {
                convertedAngle = angleToConvertInDegrees + 360.0f; // add 360 degrees to get the same angle as a positive
            }
        }

        return convertedAngle;
    }


    private (List<float[]>, List<float[]>) RetrieveBestHandAndChestExcursionsPerDirectionPerHeight()
    {
        // Store the maximum reaching heights in the format of List<float[]>. 
        // The 0th element in the list is the first reaching height maximum reach directions. 
        // The 1st element in the list is the second reaching height maximum reach directions, etc. 
        List<float[]> maximumReachingDistancesPerTargetHeightPerDirection = new List<float[]>();
        // Store the maximum chest excursions in the same way as above
        List<float[]> maximumChestDistancesPerTargetHeightPerDirection = new List<float[]>();

        // Fill the list of float[] with one float[] per reaching target height
        for (int reachingTargetHeightIndex = 0; reachingTargetHeightIndex < reachingTargetYHeightsSpecifierValues.Length; reachingTargetHeightIndex++)
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
            if (currentState == waitingForEmgReadyStateString)
            {
                exitWaitingForEmgReadyState();
            }
            else if (currentState == instructionsStateString)
            {
                exitInstructionsState();
            }
            else if (currentState == specificTrialInstructionsState)
            {
                ExitTrialSpecificInstructionsState();
            }
            else if (currentState == trialActiveOutgoingState)
            {
                exitTrialActiveOutgoingState();
            }
            else if (currentState == trialActiveHoldState)
            {
                ExitTrialActiveHoldState();
            }
            else if (currentState == trialActiveIncomingState)
            {
                ExitTrialActiveIncomingState();
            }
            else if (currentState == givingFeedbackStateString)
            {
                exitGivingFeedbackState();
            }

            //then call the entry function for the new state
            if (currentState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            else if (newState == instructionsStateString)
            {
                enterInstructionsState();
            }
            else if (newState == specificTrialInstructionsState)
            {
                EnterTrialSpecificInstructionsState();
            }
            else if (newState == trialActiveOutgoingState)
            {
                enterTrialActiveOutgoingState();
            }
            else if (newState == trialActiveHoldState)
            {
                EnterTrialActiveHoldState();
            }
            else if (newState == trialActiveIncomingState)
            {
                EnterTrialActiveIncomingState();
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
        // Note that general instruction text has been displayed from the start.
        
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

    private void EnterTrialSpecificInstructionsState()
    {
        // switch states
        currentState = specificTrialInstructionsState;
        
        // Store the trial start time for this trial
        currentTrialStartTime = Time.time;
        
        // Ensure the desired reaching hand position indicator is visible
        reachingHandDesiredPositionIndicator.GetComponent<MeshRenderer>().enabled = true;
        reachingHandDesiredStartPosIndicator.GetComponent<MeshRenderer>().enabled = true;
        
        // give directions for the current target height/direction
        instructionsText.text = "Trial: " + currentTrialNumber + "\n" +
                                trialSpecificPrefix + textPerHeight[reachingYValueOrderListThisBlock[currentTrialNumber]] + " " + textPerDirection[reachingDirectionOrderListThisBlock[currentTrialNumber]];

        // Move the ball to the "spawn location"
        (float reachDirectionInDegsCcwFromRightward, int reachHeightSpecifier) = MoveBallToReachingDirectionSpawnLocation();
        
        // Ensure the mesh renderer for the ball is active/visible
        TheBall.GetComponent<MeshRenderer>().enabled = true;
        
        // Set the text above the ball to the empty string
        timeRemainingInReachText.text = "";

        // 
        (float, int) reachSpecifierKey = (reachDirectionInDegsCcwFromRightward, reachHeightSpecifier);
        
        // Increment the dictionary storing how many trials we've done per reach dir and height combination
        if (countTrialsPerReachDirAndHeight.ContainsKey(reachSpecifierKey))
        {
            // Increment if the key exists already
            countTrialsPerReachDirAndHeight[reachSpecifierKey]++;
        }
        else
        {
            // Init the counter value at 1
            countTrialsPerReachDirAndHeight[reachSpecifierKey] = 1;
        }

        // Store the ball starting position
        currentTrialBallStartPos = TheBall.transform.position;

        // Update the best reach text orientation
        Vector3 desiredTextForwardsDirection = TheBall.transform.position - new Vector3(startingPointOfThePlayer.x, TheBall.transform.position.y, startingPointOfThePlayer.z);
        Quaternion textOrientation = Quaternion.LookRotation(desiredTextForwardsDirection, Vector3.up);
        timeRemainingInReachTextCanvas.transform.rotation = textOrientation;
        
        // If we are storing reaching trajectories (in general)
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
        {
            // Determine if this is the last trial for this reach height and dir.
            // If it's the last trial this reach dir/height
            if (countTrialsPerReachDirAndHeight[reachSpecifierKey] == trialsPerDirectionPerYValue)
            {
                // Set the flag to true
                lastTrialThisReachHeightAndDirFlag = true;
            }
            // Else if we'll do more reaches at this dir/height combination
            else
            {
                // Set the flag to false
                lastTrialThisReachHeightAndDirFlag = false;
            }
            
            // Set the desired start location for the reaching hand
            SetDesiredReachingHandStartLocation(reachDirectionInDegsCcwFromRightward, reachHeightSpecifier);
            
            // If this is the last trial this reach height and dir
            if (lastTrialThisReachHeightAndDirFlag)
            {
                // Initiate the file that will store the history for this height/dir 
                string reachDirSafe = reachDirectionInDegsCcwFromRightward.ToString("F1").Replace('.', '_');
                string fileName = $"reach_h_{reachHeightSpecifier}_dir_{reachDirSafe}_trajectory.csv";
                filePathBaselineTrajectoriesThisHeightDir = Path.Combine(baselineTrajectoriesDirectoryFilePath, fileName);

                // If the file already exists
                if (File.Exists(filePathBaselineTrajectoriesThisHeightDir))
                {
                    // delete it!
                    File.Delete(filePathBaselineTrajectoriesThisHeightDir);
                }

                // If the file does not exist (it should not, we just deleted it)
                if (!File.Exists(filePathBaselineTrajectoriesThisHeightDir))
                { 
                    // If the directory does not yet exist, create it
                    if (!Directory.Exists(baselineTrajectoriesDirectoryFilePath))
                    {
                        Directory.CreateDirectory(baselineTrajectoriesDirectoryFilePath);
                    }
                    // Store the headers (time, hand pos components, joint variables)
                    WriteHeader();
                }
            }
        }
        // Else if we are loading and recreating a baseline reach trajectory
        else if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.ReadBaseline)
        {
            // Load the baseline trajectory for this reach dir/height combination
            trajectoryHistoryThisReachHeightAndDir =
                ReachDataReader.LoadReachTrajectoryThisHeightDir(reachSpecifierKey.Item1, reachSpecifierKey.Item2, baselineTrajectoriesDirectoryFilePath);
            
            // Render the desired reaching trajectory as a dashed line
            DrawTrajectoryLine(trajectoryHistoryThisReachHeightAndDir);
            
            // Set the duration of this reach, which depends on the baseline reach duration
            // AND the playback speed multiplier!
            float reachDurationInMilliseconds =
                trajectoryHistoryThisReachHeightAndDir[trajectoryHistoryThisReachHeightAndDir.Count - 1].time;
            baselineReachTimeThisHeightDirInMs = reachDurationInMilliseconds;
            timeAllowedToPerformReachInMs =
                reachDurationInMilliseconds  / baselineTrajectoryPlaybackSpeedMultiplier;
            
            // Also update the state transition control times for this reach
            reachOutgoingTimeThisTrialInMs = timeAllowedToPerformReachInMs;
            reachIncomingTimeThisTrialInMs = timeAllowedToPerformReachInMs;
            
            // Hand starting pos frame 0
            Vector3 desiredHandStartPosFrame0 = trajectoryHistoryThisReachHeightAndDir[0].handPosInFrame0;
        
            // Transform from frame 0 to Unity frame
            // Transform the hand position from frame 0 to Unity frame.
            // TEMP ONLY - set to identity.
            Matrix4x4 transformationUnityFrameToFrame0 = Matrix4x4.identity;
            Matrix4x4 transformationFrame0ToUnityFrame = transformationUnityFrameToFrame0.inverse;
            Vector3 desiredHandStartPosUnityFrame = transformationFrame0ToUnityFrame.MultiplyPoint(desiredHandStartPosFrame0);
        
            // Set the reaching hand desired trajectory indicator to it's starting position for this reach
            reachingHandDesiredPositionIndicator.transform.position =
                desiredHandStartPosUnityFrame;
            
            // Also set the reaching hand start pos indicator to the starting position
            reachingHandDesiredStartPosIndicator.transform.position = desiredHandStartPosUnityFrame;

            Debug.Log("Time allowed for reach this trial: " + timeAllowedToPerformReachInMs + " [ms]");
        }
        
        // Start the state transition timer from zero
        stateTransitionStopwatch.Restart();
        
        // Reset the reaching timer stopwatch to zero
        reachTimerStopwatch.Reset();
    }
    
    private void ExitTrialSpecificInstructionsState()
    {
        // If we're storing the baseline reach trajectory, we make the indicator disappear when we exit ]
        // the trial-specific instructions state (hand has been near indicator for long enough). 
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
        {
            // Stop rendering both the moving and start pos indicators
            reachingHandDesiredPositionIndicator.GetComponent<MeshRenderer>().enabled = false;
            reachingHandDesiredStartPosIndicator.GetComponent<MeshRenderer>().enabled = false;
        }
    }

    private void enterTrialActiveOutgoingState()
    {
        // switch states
        currentState = trialActiveOutgoingState;
        
        // If we're storing reaching baseline trajectories
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
        {
            // Hide the text over the desired reach indicator by setting it to the empty string.
            timeToStartDesiredReachTrajectoryText.text = "";
        }else if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.ReadBaseline)
        {
            // Tell the subject that they can start the reach movement.
            timeToStartDesiredReachTrajectoryText.text = "Start!";
        }
        
        // Reset the state transition stopwatch to zero, but leave it stopped.
        stateTransitionStopwatch.Reset();
        
        // Restart the reaching time remaining timer
        reachTimerStopwatch.Restart();
    }

    
    private void exitTrialActiveOutgoingState()
    {
        // Reset key quantities
        lastTrialThisReachHeightAndDirFlag = false;
    }
    
    private void EnterTrialActiveHoldState()
    {
        // switch states
        currentState = trialActiveHoldState;
        
        // Hide the desired reach start pos indicator in the paced reach once the reach is "active"
        reachingHandDesiredStartPosIndicator.GetComponent<MeshRenderer>().enabled = false;
        
        // Start the state transition timer from zero
        stateTransitionStopwatch.Restart();
        
        // Reset the reaching time remaining timer to zero
        reachTimerStopwatch.Reset();
        
    }
    
    private void ExitTrialActiveHoldState()
    {
        // Nothing for now
    }
    
    private void EnterTrialActiveIncomingState()
    {
        // switch states
        currentState = trialActiveIncomingState;
        
        // Hide the text above the desired reach indicator
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.ReadBaseline)
        {
            timeToStartDesiredReachTrajectoryText.text = "";
        }
        
        // Start the state transition timer from zero
        stateTransitionStopwatch.Restart();
        
        // Restart the reaching time remaining timer
        reachTimerStopwatch.Restart();
    }
    
    private void ExitTrialActiveIncomingState()
    {
        // Nothing for now
    }

    private void enterGivingFeedbackStateString()
    {
        //switch states
        currentState = givingFeedbackStateString;

        // Mark the end time for the trial
        currentTrialEndTime = Time.time;
        
        // Hide the desired reach trajectory by emptying the line renderer.
        desiredReachTrajectoryLineRenderer.positionCount = 0;

        // hide the ball (this game object)
        TheBall.GetComponent<MeshRenderer>().enabled = false;
        
        // Assign points
        pointsEarnedThisTrial = AssignPointsToThisPacedReachTrial();

        // If negative
        if(pointsEarnedThisTrial < 0.0f)
        {
            pointsEarnedThisTrial = 0.0f;
        }
        
        // Add rounded up points to total points earned so far
        pointsEarnedSoFar = pointsEarnedSoFar + (uint) Mathf.Ceil(pointsEarnedThisTrial);
        
        // Set points text
        pointsText.text = "Points: " + pointsEarnedSoFar.ToString();
        
        // Change the text above the ball to show the points earned that touch
        timeRemainingInReachText.text = "+ " + Mathf.Ceil(pointsEarnedThisTrial).ToString("F0") + " points!";
        
        // Change the Instructions text to give some feedback about the reach.
        if(pointsEarnedSoFar == 0){
            instructionsText.text = "Try again! You earned \n " + Mathf.Ceil(pointsEarnedThisTrial).ToString("F0") + " points.";
        }else if(pointsEarnedSoFar < 50){
            instructionsText.text = "Not bad! You earned \n " + Mathf.Ceil(pointsEarnedThisTrial).ToString("F0") + " points.";
        }else{
            instructionsText.text = "Great! You earned \n " + Mathf.Ceil(pointsEarnedThisTrial).ToString("F0") + " points.";
        }
        
        // Clear the record of this reach trajectory to prepare for the next reach 
        mostRecentReachHandTrajectoryForScoring.ClearReachHistoryData();
        
        // Reset the stopwatch that keeps track of how long we have per trial/state
        stateTransitionStopwatch.Restart();
        
        // Reset the reaching timer stopwatch to zero
        reachTimerStopwatch.Reset();
    }

    private float AssignPointsToThisPacedReachTrial()
    {
        // Init points earned
        float pointsEarnedThisTrial = 0.0f; 
        // If we're storing baseline
        if (selectReadOrStoreBaselineTrajectories == ReadOrStoreBaselineTrajectories.StoreBaseline)
        {
            // Give 100 points
            pointsEarnedThisTrial = maximumPointsEarnedPerTrial;
        }
        // else if we're reading baseline and doing a paced reach
        else
        {
            // Assign all points based on LSED to the desired trajectory, 
            // considering time as well. 
            List<SingleReachHistoryStorageClass.HandTrajectoryData> handTrajectoryDataThisReach = mostRecentReachHandTrajectoryForScoring.GetMostRecentReachHistoryData();
            
            // Loop over the history and get the first time point when velocity was greater than
            // the threshold defining movement initiation
            int reachStartedIndex = -1; // dummy value
            bool reachStartBasedOnVelFoundFlag = false;
            for (int handTrajectoryIndex = 0;
                 handTrajectoryIndex < handTrajectoryDataThisReach.Count - 1;
                 handTrajectoryIndex++)
            {
                if (reachStartBasedOnVelFoundFlag == false)
                {
                    // Compute the velocity for time span (i+1 - i)
                    float deltaTimeInMs = handTrajectoryDataThisReach[handTrajectoryIndex + 1].reachTimeInMs -
                                          handTrajectoryDataThisReach[handTrajectoryIndex].reachTimeInMs;
                    float deltaPosInMeters = Vector3.Distance(handTrajectoryDataThisReach[handTrajectoryIndex + 1].handPosUnityFrame,
                        handTrajectoryDataThisReach[handTrajectoryIndex].handPosUnityFrame);
                    float velocityMetersPerSecond = (deltaPosInMeters / deltaTimeInMs) * 1000.0f; // 1000 = ms to s conversion
                
                    // If the velocity is over threshold
                    if (velocityMetersPerSecond > minimumVelocityForReachInitiationWhenScoring)
                    {
                        // Store the index when the reach started based on the velocity metric
                        reachStartedIndex = handTrajectoryIndex + 1;
                    
                        // Set the flag that will stop the search
                        reachStartBasedOnVelFoundFlag = true;
                    }
                }
            }

            // If we found a start index
            if (reachStartedIndex > 0)
            {
                // Delete the hand trajectory before the movement started 
                handTrajectoryDataThisReach.RemoveRange(0,reachStartedIndex);
                
                // Get the hand trajectory start pos
                Vector3 handTrajectoryStartPosUnityFrame = handTrajectoryDataThisReach[0].handPosUnityFrame;
                float minDistanceHandStartToRef = 100000.0f;
                // Find the spatially nearest point in the ref trajectory
                int nearestPosIndexInRef = -1;
                for (int refTrajectoryIndex = 0;
                     refTrajectoryIndex < trajectoryHistoryThisReachHeightAndDir.Count;
                     refTrajectoryIndex++)
                {
                    float distanceHandStartToRefDataPoint = Vector3.Distance(handTrajectoryStartPosUnityFrame,
                        trajectoryHistoryThisReachHeightAndDir[refTrajectoryIndex].handPosInFrame0);
                    
                    // If it's a minimum distance
                    if(distanceHandStartToRefDataPoint < minDistanceHandStartToRef)
                    {
                        // Update the minimum distance
                        minDistanceHandStartToRef = distanceHandStartToRefDataPoint;
                        
                        // Store the index
                        nearestPosIndexInRef = refTrajectoryIndex;
                    }
                }

                // Find the starting time for the ref trajectory and the hand pos trajectory history. 
                // These will be time zero for each trajectory, respectively.
                float startTimeHandTrajectoryInMs = handTrajectoryDataThisReach[0].reachTimeInMs;
                float startTimeRefTrajectoryInMs = trajectoryHistoryThisReachHeightAndDir[nearestPosIndexInRef].time;

                // Loop over each hand pos trajectory history
                float runningTotalDistanceInMeters = 0.0f;
                float mostRecentHandTrajectoryTime = 0.0f;
                float mostRecentComparedRefTrajectoryTime = 0.0f;
                for (int handTrajectoryIndex = 0;
                     handTrajectoryIndex < handTrajectoryDataThisReach.Count;
                     handTrajectoryIndex++)
                {
                    // Loop over reach ref trajectory data point and find the nearest point in time that is lesser or equal in time.
                    float distanceBetweenHandTrajectoryAndNearestRef = 0.0f;
                    for (int refTrajectoryIndex = nearestPosIndexInRef;
                         refTrajectoryIndex < trajectoryHistoryThisReachHeightAndDir.Count;
                         refTrajectoryIndex++)
                    {
                        // Compute the distance between the two if the hand trajectory time is greater
                        // and overwrite the distance
                        if ((trajectoryHistoryThisReachHeightAndDir[refTrajectoryIndex].time - startTimeRefTrajectoryInMs)  <=
                            (handTrajectoryDataThisReach[handTrajectoryIndex].reachTimeInMs - startTimeHandTrajectoryInMs))
                        {
                            mostRecentHandTrajectoryTime =
                                handTrajectoryDataThisReach[handTrajectoryIndex].reachTimeInMs -
                                startTimeHandTrajectoryInMs;
                            mostRecentComparedRefTrajectoryTime =
                                trajectoryHistoryThisReachHeightAndDir[refTrajectoryIndex].time -
                                startTimeRefTrajectoryInMs;
                            distanceBetweenHandTrajectoryAndNearestRef = Vector3.Distance(
                                trajectoryHistoryThisReachHeightAndDir[refTrajectoryIndex].handPosInFrame0,
                                handTrajectoryDataThisReach[handTrajectoryIndex].handPosUnityFrame);
                        }
                    }
                    
                    Debug.Log($"Most recent match: handTimeRel = {mostRecentHandTrajectoryTime} ms, refTimeRel = {mostRecentComparedRefTrajectoryTime} ms, dist = {distanceBetweenHandTrajectoryAndNearestRef} m");

                    // Add the distance to the running total
                    runningTotalDistanceInMeters =
                        runningTotalDistanceInMeters + distanceBetweenHandTrajectoryAndNearestRef;
                }
                
                // DEBUG ONLY
                
                // Take the mean of the running total by dividing by number of data points
                float meanDistHandTrajectoryToRefTrajectory = runningTotalDistanceInMeters / handTrajectoryDataThisReach.Count;
                Debug.Log("Point assignment: average distance to target trajectory is: " + meanDistHandTrajectoryToRefTrajectory + " [m]");
                
                Debug.Log("Trajectory comparison: handStart = " + startTimeHandTrajectoryInMs + " ms, handEnd = " + handTrajectoryDataThisReach[handTrajectoryDataThisReach.Count - 1].reachTimeInMs +
                          " ms, refStart = " + startTimeRefTrajectoryInMs + " ms, refEnd = " + trajectoryHistoryThisReachHeightAndDir[trajectoryHistoryThisReachHeightAndDir.Count - 1].time +
                          " ms, meanDist = " + meanDistHandTrajectoryToRefTrajectory + " m");

                // Assign points based on a threshold in meters. 0 error is 100 points, a mean error at the threshold is 0 points, 
                // linear interpolation in between. 
                if (meanDistHandTrajectoryToRefTrajectory < maxMeanDistanceThresholdToEarnNoPointsInMeters && meanDistHandTrajectoryToRefTrajectory >= minMeanDistanceThresholdToEarnFullPointsInMeters)
                {
                    pointsEarnedThisTrial =
                        maximumPointsEarnedPerTrial * ((maxMeanDistanceThresholdToEarnNoPointsInMeters - meanDistHandTrajectoryToRefTrajectory) / (maxMeanDistanceThresholdToEarnNoPointsInMeters - minMeanDistanceThresholdToEarnFullPointsInMeters));
                }else if(meanDistHandTrajectoryToRefTrajectory < minMeanDistanceThresholdToEarnFullPointsInMeters)
                {
                    pointsEarnedThisTrial = maximumPointsEarnedPerTrial;
                }
                
                // Clamp to be greater than zero
                if (pointsEarnedThisTrial < 0.0f)
                {
                    pointsEarnedThisTrial = 0.0f;
                }
            }
        } // end paced reach conditional

        // Return the points earned
        return pointsEarnedThisTrial;
    }



    private void exitGivingFeedbackState()
    {
        // Store a row of trial data for the completed trial
        StoreTrialData();

        // Icncrement trial number
        currentTrialNumber += 1;
    }

    private void enterGameOverState()
    {
        // switch states
        currentState = gameOverStateString;

        // Call a function that will get the best reach distance and chest excursion distance
        // per direction and per height!
        // We must do this in case we measured more than one trial per direction per height.
        (List<float[]> bestReachDistancesPerTargetHeightPerDirection, List<float[]> bestChestDistancesPerTargetHeightPerDirection) = RetrieveBestHandAndChestExcursionsPerDirectionPerHeight();

        // Store the performance data
        float[] excursionPerformanceData = new float[2 * reachingDirectionsInDegreesCcwFromRight.Length * reachingTargetYHeightsSpecifierValues.Length]; // 2 is b/c we store both reach and chest data
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
                ; }
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
        generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();
        generalDataRecorderScript.writeFrameDataToFile();
        generalDataRecorderScript.writeTrialDataToFile();

        //
        TestEnding = true;

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
        reachingDirectionOrderListThisBlock = new int[trialsPerDirectionPerYValue * reachingTargetYHeightsSpecifierStrings.Length * reachingDirectionsInDegreesCcwFromRight.Length];
        reachingYValueOrderListThisBlock = new int[trialsPerDirectionPerYValue * reachingTargetYHeightsSpecifierStrings.Length * reachingDirectionsInDegreesCcwFromRight.Length];
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

    private void AssignReachingTargetHeightsBasedOnTrackerPos()
    {
        // Initialize reaching targets height array
        reachingTargetYHeightsSpecifierValues = new float[reachingTargetYHeightsSpecifierStrings.Length];
        // Map from each reaching target height string to a float value, based on current HMD and tracker locations. 
        for (int reachingHeightSpecifierIndex = 0; reachingHeightSpecifierIndex < reachingTargetYHeightsSpecifierStrings.Length; reachingHeightSpecifierIndex++)
        {
            string currentReachTargetSpecifier = reachingTargetYHeightsSpecifierStrings[reachingHeightSpecifierIndex];
            if (currentReachTargetSpecifier == "chest" || currentReachTargetSpecifier == "Chest")
            {
                // Get the current chest tracker y-pos
                reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex] = ChestTracker.transform.position.y;
                Debug.Log("Chest tracker y-pos = " + reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex]);
            }
            else if (currentReachTargetSpecifier == "waist" || currentReachTargetSpecifier == "Waist")
            {
                // Get the current waist/pelvis tracker y-pos
                reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex] = WaistTracker.transform.position.y;
                Debug.Log("Waist tracker y-pos = " + reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex]);
            }
            else if (currentReachTargetSpecifier == "hmd" || currentReachTargetSpecifier == "Hmd" || currentReachTargetSpecifier == "HMD")
            {
                // Get the current waist/pelvis tracker y-pos
                reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex] = headsetCameraGameObject.transform.position.y;
                Debug.Log("HMD tracker y-pos = " + reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex]);
            }
            else if (currentReachTargetSpecifier == "shoulder" || currentReachTargetSpecifier == "Shoulder")
            {
                float verticalDistanceChestToShoulders = subjectSpecificDataScript.getVerticalDistanceChestToShouldersInMeters();
                reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex] = ChestTracker.transform.position.y + verticalDistanceChestToShoulders;
            }
            else
            {
                Debug.LogError("Reaching target y-pos category string " + currentReachTargetSpecifier + " was invalid.");
            }

        }
    }





    private (float, int) MoveBallToReachingDirectionSpawnLocation()
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

        // Get the reach-specific ball spawn radius as the height- and direction-specific reach distance times some fraction (e.g. 0.9)
        string reachDirHeightKey = BallRebornAngle.ToString("F0") + reachHeightSpecifier.ToString();
        float reachLimitThisHeightDir = reachLimitsByHeightDir[reachDirHeightKey];
        ballSpawnRadius = fractionOfReachDistanceToSpawnBall * reachLimitThisHeightDir;

        // Get the ball spawn position in Unity frame
        Vector3 ballSpawnPositionUnityFrame = ballTravelOriginInUnityFrame + unitVectorReachingDirectionUnityFrame * ballSpawnRadius;

        // Position the ball/reach target for the trial that has just begun
        TheBall.transform.position = ballSpawnPositionUnityFrame;

        // Return the reach angle in degrees as a float and the reach height specifier as an int, packaged into tuple
        return (BallRebornAngle, reachingYValueOrderListThisBlock[currentTrialNumber]);
    }


    private float GetBallXAxisPositionInFrame0(int reachHeightSpecifier)
    {
        // Init x-axis position in Unity frame storage
        float xAxisHeightInFrame0 = 0.0f;

        // Get the desired height using the neutral pose data.
        if (reachHeightSpecifier == 0)
        {
            xAxisHeightInFrame0 = neutralPelvisPosInFrame0.x;
        }
        else if (reachHeightSpecifier == 1)
        {
            xAxisHeightInFrame0 = neutralChestPosInFrame0.x;
        }
        else if (reachHeightSpecifier == 2)
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



    private void SetDesiredReachingHandStartLocation(float reachDirectionInDegsCcwFromRightward, int reachHeightSpecifier)
    {

        // Compute upper arm length 
        float armLengthInMeters = upperArmLengthInMeters + forearmLengthInMeters;

        // Adjust the reach direction to slightly offset the desired hand start pos indicator, avoiding occlusions. 
        float occlusionOffsetInDegrees = 10.0f;
        float directionToHandStartPosInDegsCcwFromRightward = reachDirectionInDegsCcwFromRightward;
        if (reachDirectionInDegsCcwFromRightward < 90.0f)
        {
            // Offset towards the left (positive offset)
            directionToHandStartPosInDegsCcwFromRightward = directionToHandStartPosInDegsCcwFromRightward + occlusionOffsetInDegrees;
        }
        // For forward and leftward reaches
        else
        {
            // Offset towards the right (negative offset)
            directionToHandStartPosInDegsCcwFromRightward = directionToHandStartPosInDegsCcwFromRightward - occlusionOffsetInDegrees;
        }

        // Get the postural model frame 0 unit vectors along which the ball will be displaced
        // Recall:
        // Frame 0: y-axis is forward, z-axis is left, x-axis is up
        Vector3 unitVectorReachingDirectionFrame0 =
            new Vector3(0.0f, 
            Mathf.Sin(directionToHandStartPosInDegsCcwFromRightward * Mathf.Deg2Rad),
            -Mathf.Cos(directionToHandStartPosInDegsCcwFromRightward * Mathf.Deg2Rad));

        // Compute the ground plane position for the desired hand start pos
        Vector3 desiredHandStartPosFrame0 = unitVectorReachingDirectionFrame0 * groundPlaneOffsetAllTargetsAsFractionOfArmLength * armLengthInMeters;

        // Add the vertical (x-axis) position in frame 0, depending on the reach height
        // If it's a waist-height reach
        if (reachHeightSpecifier == 0)
        {
            // Use the vertical position of the waist tracker from the neutral pose, plus a small vertical offset
            desiredHandStartPosFrame0.x = neutralPelvisPosInFrame0.x + verticalOffsetWaistLevelReachAsFractionOfArmLength * armLengthInMeters;
        }
        // We use the same height for chest and eye-level reaches, since we want the person to be able to see the 
        // hand start pos indicator without looking down. 
        else
        {
            // Use the vertical position of the chest tracker from the neutral pose, plus a vertical offset
            desiredHandStartPosFrame0.x = neutralChestPosInFrame0.x + verticalOffsetChestAndEyeReachAsFractionOfArmLength * armLengthInMeters;
        }

        // Transform the desired hand start pos from frame 0 to Unity frame
        Matrix4x4 transformFrame0ToUnityFrame = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
        Vector3 desiredHandStartPosUnityFrame = transformFrame0ToUnityFrame.MultiplyPoint3x4(desiredHandStartPosFrame0);

        // Set the desired hand pos indicator's position
        reachingHandDesiredStartPosIndicator.transform.position = desiredHandStartPosUnityFrame;

        // Also set the hand pos trajectory indicator to the same start position
        reachingHandDesiredPositionIndicator.transform.position = desiredHandStartPosUnityFrame;
    }


    private void setFrameAndTrialDataNaming()
    {
        // Set the frame data column headers
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT", "TRIAL_NUMBER",
            "HMD_TOGGLED_HOME_FLAG", "LEFT_HAND_TOUCHED_BALL_LAST_FRAME_FLAG", "RIGHT_HAND_TOUCHED_BALL_LAST_FRAME_FLAG", 
            "POSITION_OF_HEADSET_X", "POSITION_OF_HEADSET_Y", "POSITION_OF_HEADSET_Z",
            "HEADSET_X_AXIS_UNITY_FRAME_X", "HEADSET_X_AXIS_UNITY_FRAME_Y", "HEADSET_X_AXIS_UNITY_FRAME_Z",
            "HEADSET_Y_AXIS_UNITY_FRAME_X", "HEADSET_Y_AXIS_UNITY_FRAME_Y", "HEADSET_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_RIGHT_CONTROLLER_X", "POSITION_OF_RIGHT_CONTROLLER_Y", "POSITION_OF_RIGHT_CONTROLLER_Z",
            "RIGHT_CONTROLLER_X_AXIS_UNITY_FRAME_X", "RIGHT_CONTROLLER_X_AXIS_UNITY_FRAME_Y", "RIGHT_CONTROLLER_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_CONTROLLER_Y_AXIS_UNITY_FRAME_X", "RIGHT_CONTROLLER_Y_AXIS_UNITY_FRAME_Y", "RIGHT_CONTROLLER_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_LEFT_CONTROLLER_X", "POSITION_OF_LEFT_CONTROLLER_Y", "POSITION_OF_LEFT_CONTROLLER_Z",
            "LEFT_CONTROLLER_X_AXIS_UNITY_FRAME_X", "LEFT_CONTROLLER_X_AXIS_UNITY_FRAME_Y", "LEFT_CONTROLLER_X_AXIS_UNITY_FRAME_Z",
            "LEFT_CONTROLLER_Y_AXIS_UNITY_FRAME_X", "LEFT_CONTROLLER_Y_AXIS_UNITY_FRAME_Y", "LEFT_CONTROLLER_Y_AXIS_UNITY_FRAME_Z",
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
            "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_X", "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_Y", "RIGHT_ANKLE_Y_AXIS_UNITY_FRAME_Z"
            };

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // Set the trial data column headers
       string[] csvTrialDataHeaderNames = new string[]{
        "TRIAL_NUMBER", "UPPER_ARM_LENGTH_METERS", "FOREARM_LENGTH_METERS", "CHEST_TO_STERNAL_NOTCH_LENGTH_METERS", 
        "HMD_START_POS_X", "HMD_START_POS_Y", "HMD_START_POS_Z", "HMD_TOGGLE_HOME_VECTOR_X", "HMD_TOGGLE_HOME_VECTOR_Y", "HMD_TOGGLE_HOME_VECTOR_Z",
        "TRIAL_START_TIME", "TRIAL_END_TIME", "TIME_TO_REACH_BALL_IN_MS", "REACHING_DIRECTION_SPECIFIER", "REACHING_HEIGHT_SPECIFIER", 
        "BALL_START_POS_X", "BALL_START_POS_Y", "BALL_START_POS_Z", 
        "POINTS_EARNED_THIS_TRIAL", 
        "T_0_TO_UNITY_COL_0_ROW_0","T_0_TO_UNITY_COL_0_ROW_1","T_0_TO_UNITY_COL_0_ROW_2",
        "T_0_TO_UNITY_COL_1_ROW_0","T_0_TO_UNITY_COL_1_ROW_1","T_0_TO_UNITY_COL_1_ROW_2",
        "T_0_TO_UNITY_COL_2_ROW_0","T_0_TO_UNITY_COL_2_ROW_1","T_0_TO_UNITY_COL_2_ROW_2",
        "T_0_TO_UNITY_COL_3_ROW_0","T_0_TO_UNITY_COL_3_ROW_1","T_0_TO_UNITY_COL_3_ROW_2"};

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
        for (int targetHeightIndex = 0; targetHeightIndex < reachingTargetYHeightsSpecifierStrings.Length; targetHeightIndex++)
        {
            // Append the target height to the string array for the reach column headers
            reachMaxColumnHeaders.AddRange(appendStringToAllElementsOfStringArray(reachingHeaderNameStubs, "_HEIGHT_" + reachingTargetYHeightsSpecifierStrings[targetHeightIndex]));
            // Append the target height to the string array for the chest column headers
            chestMaxColumnHeaders.AddRange(appendStringToAllElementsOfStringArray(chestHeaderNameStubs, "_HEIGHT_" + reachingTargetYHeightsSpecifierStrings[targetHeightIndex]));
        }
        // Add all the column headers to a single string[]
        string[] excursionPerformanceSummaryHeaderNames = new string[reachMaxColumnHeaders.Count + chestMaxColumnHeaders.Count];
        reachMaxColumnHeaders.ToArray().CopyTo(excursionPerformanceSummaryHeaderNames,0);
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
        baselineTrajectoriesDirectoryFilePath = Application.dataPath + "/CSV/" + subdirectoryString;
        
        // Now that we've done this formatting, we should be able to reconstruct the directory containing the 
        // Excursion limits collected on the same day.
        subdirectoryWithReachingLimitsData = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + nameOfReachingLimitsTestTask + "/" + dateString + "/";

        // Build the name of the subdirectory that contains the neutral pose joint variables and positions in frame 0
        subdirectoryNeutralPoseDataString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + neutralPoseTaskName + "/" + dateString + "/";

        //set the frame data and the reach and lean performance trial subdirectory name (will go inside the CSV folder in Assets)
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvExcursionPerformanceSummarySubdirectoryName(subdirectoryString);

        // 4.) Call the function to set the file names (within the subdirectory) for the current block
        string stringfilename = "BestReachAndLeanDistances.csv";
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(stringfilename);
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

     private string[] ConvertFloatArrayToStringArray(float[] floatArray){

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
        string fileNameStub = thisTaskNameString + delimiter + subjectSpecificInfoString + delimiter + dateAndTimeString;
        //mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);
    }



    private void storeFrameData()
    {

        // the list that will store the data
        List<float> frameDataToStore = new List<float>();

        /*// A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT", "TRIAL_NUMBER",
           "POSITION_OF_HEADSET_X", "POSITION_OF_HEADSET_Y", "POSITION_OF_HEADSET_Z",
            "ORIENTATION_EULER_OF_HEADSET_X", "ORIENTATION_EULER_OF_HEADSET_Y", "ORIENTATION_EULER_OF_HEADSET_Z",
            "POSITION_OF_RIGHT_CONTROLLER_X", "POSITION_OF_RIGHT_CONTROLLER_Y", "POSITION_OF_RIGHT_CONTROLLER_Z",
            "ORIENTATION_EULER_OF_RIGHT_CONTROLLER_X", "ORIENTATION_EULER_OF_RIGHT_CONTROLLER_Y", "ORIENTATION_EULER_OF_RIGHT_CONTROLLER_Z",
            "POSITION_OF_LEFT_CONTROLLER_X", "POSITION_OF_LEFT_CONTROLLER_Y", "POSITION_OF_LEFT_CONTROLLER_Z",
            "ORIENTATION_EULER_OF_LEFT_CONTROLLER_X", "ORIENTATION_EULER_OF_LEFT_CONTROLLER_Y", "ORIENTATION_EULER_OF_LEFT_CONTROLLER_Z",
            "POSITION_OF_RIGHT_SHOULDER_X", "POSITION_OF_RIGHT_SHOULDER_Y", "POSITION_OF_RIGHT_SHOULDER_Z",
            "ORIENTATION_EULER_OF_RIGHT_SHOULDER_X", "ORIENTATION_EULER_OF_RIGHT_SHOULDER_Y", "ORIENTATION_EULER_OF_RIGHT_SHOULDER_Z",
            "POSITION_OF_LEFT_SHOULDER_X", "POSITION_OF_LEFT_SHOULDER_Y", "POSITION_OF_LEFT_SHOULDER_Z",
            "ORIENTATION_EULER_OF_LEFT_SHOULDER_X", "ORIENTATION_EULER_OF_LEFT_SHOULDER_Y", "ORIENTATION_EULER_OF_LEFT_SHOULDER_Z",
            };*/

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
        else if (currentState == instructionsStateString)
        {
            stateAsFloat = 3.0f;
        }
        else if (currentState == specificTrialInstructionsState)
        {
            stateAsFloat = 4.0f;
        }
        else if (currentState == trialActiveOutgoingState)
        {
            stateAsFloat = 5.0f;
        }
        else if (currentState == trialActiveHoldState)
        {
            stateAsFloat = 6.0f;
        }
        else if (currentState == trialActiveIncomingState)
        {
            stateAsFloat = 7.0f;
        }
        else if (currentState == givingFeedbackStateString)
        {
            stateAsFloat = 8.0f;
        }
        else if (currentState == gameOverStateString)
        {
            stateAsFloat = 9.0f;
        }
        else
        {
            // invalid
        }

        // store the state
        frameDataToStore.Add(stateAsFloat);

        // Get the trial # and block #
        int trialIndex = currentTrialNumber;
        frameDataToStore.Add((float)trialIndex);

        // Whether or not the heaset has been toggled home
        bool hmdToggledHomeFlag = playerRepositioningScript.GetToggleHmdStatus();
        frameDataToStore.Add(System.Convert.ToSingle(hmdToggledHomeFlag));
        
        // Whether right or left hand were touching this last frame flag
        //frameDataToStore.Add(System.Convert.ToSingle(leftHandTouchingBallThisFrameFlag)); // whether or not the right hand was in contact with the reaching target this last frame
        frameDataToStore.Add(System.Convert.ToSingle(rightHandTouchingBallThisFrameFlag));  // whether or not the left hand was in contact with the reaching target this last frame

        // Headset position (x,y,z)
        frameDataToStore.Add(headsetCameraGameObject.transform.position.x);
        frameDataToStore.Add(headsetCameraGameObject.transform.position.y);
        frameDataToStore.Add(headsetCameraGameObject.transform.position.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 headsetXAxisUnityFrame, Vector3 headsetYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(headsetCameraGameObject);
        frameDataToStore.Add(headsetXAxisUnityFrame.x);
        frameDataToStore.Add(headsetXAxisUnityFrame.y);
        frameDataToStore.Add(headsetXAxisUnityFrame.z);
        frameDataToStore.Add(headsetYAxisUnityFrame.x);
        frameDataToStore.Add(headsetYAxisUnityFrame.y);
        frameDataToStore.Add(headsetYAxisUnityFrame.z);
        
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

        // the list that will store the data
        List<float> trialDataToStore = new List<float>();

        // ADD: axis-angle error, directionValue (0 is right, 1 is left)

        // Get the trial #
        float trialNumber = (float) currentTrialNumber; //we only have trials for now. Should we implement a block format for excursion?
        trialDataToStore.Add(trialNumber);

        // Store subject-specific info: upper arm length, forearm length, and distance from chest to sternal notch (shoulders)
        trialDataToStore.Add(upperArmLengthInMeters);
        trialDataToStore.Add(forearmLengthInMeters);
        trialDataToStore.Add(chestToSternalNotchInMeters);


        // The start position of the headset in Unity frame. This is enforced with the home toggle.
        (Vector3 playerViewStartPosition, _) = playerRepositioningScript.GetNeutralPlayerOrientationAndStartingPosition();
        trialDataToStore.Add(playerViewStartPosition.x);
        trialDataToStore.Add(playerViewStartPosition.y);
        trialDataToStore.Add(playerViewStartPosition.z);

        // Headset/Hmd offset from position at toggle home to desired home position (roughly the player height)
        Vector3 offsetHmdToggleStartToHomeVector = playerRepositioningScript.GetToggleHmdToHomePositionOffsetVector();
        trialDataToStore.Add(offsetHmdToggleStartToHomeVector.x);
        trialDataToStore.Add(offsetHmdToggleStartToHomeVector.y);
        trialDataToStore.Add(offsetHmdToggleStartToHomeVector.z);

        // Store the trial start time = beginning of trialActive state
        trialDataToStore.Add(currentTrialStartTime);

        // Store the trial end time = end of trialActive state
        trialDataToStore.Add(currentTrialEndTime);
        
        // Store the measured reach duration
        trialDataToStore.Add(timeToPerformMostRecentTrialInMs); // does NOT have to match trial start and end time. Will be off by a few ms.

        // Store the ball direction angle from rightwards (CCW) in degrees for this trial
        trialDataToStore.Add(reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]]);

        // Store the ball height specifier (int) for this trial
        trialDataToStore.Add((float) reachingYValueOrderListThisBlock[currentTrialNumber]);

        // Store ball starting position this trial
        trialDataToStore.Add(currentTrialBallStartPos.x);
        trialDataToStore.Add(currentTrialBallStartPos.y);
        trialDataToStore.Add(currentTrialBallStartPos.z);
        
        // Store points earned this trial
        trialDataToStore.Add(pointsEarnedThisTrial);

        // Transformation matrix frame 0 to Unity frame
        Matrix4x4 transformationFrame0ToUnity = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();

        // Add the first 3 rows (ignoring the 4th row) of each column to the list in column-order
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 3; row++) // only rows 0 to 2
            {
                trialDataToStore.Add(transformationFrame0ToUnity[row, col]);
            }
        }


        //send all of this trial's summary data to the general data recorder
        generalDataRecorderScript.storeRowOfTrialData(trialDataToStore.ToArray());
    }



    // BEGIN: LEVEL MANAGER ABSTRACT CLASS FUNCTIONS**********************************************************************
    // These have to be implemented by all level managers 

    public override string GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        return new Vector3(0.0f, 0.0f, 0.0f);
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        return new Vector3(0.0f, 0.0f, 0.0f);
    }
    public override Vector3 GetControlPointForRobustForceField()
    {
        return new Vector3(0.0f, 0.0f, 0.0f);
    }
    public override List<Vector3> GetExcursionLimitsFromExcursionCenterInViconUnits()
    {
        return new List<Vector3>();
    }
    public override Vector3 GetCenterOfExcursionLimitsInViconFrame()
    {
        return new Vector3(0.0f, 0.0f, 0.0f);
    }

    // Not really used, return dummy value
    public override string GetCurrentDesiredForceFieldTypeSpecifier() // Retrieve a string specifying what type of RobUST FF is currently desired. OK to have idle as default.
    {
        return "";
    }
    public override bool GetEmgStreamingDesiredStatus() // Get the flag set by level manager that activates (true) or inactivates (false) the EMG streaming service
    {
        return streamingEmgDataFlag;
    }

    // END: LEVEL MANAGER ABSTRACT CLASS FUNCTIONS**********************************************************************


    public bool GetTheFlagOfTheTestingResult()
    {
        return TestEnding;
    }

    public enum WhichSideSelectEnum
    {
        LeftSide, 
        RightSide
    }

    public enum ReadOrStoreBaselineTrajectories
    {
        StoreBaseline,
        ReadBaseline
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
