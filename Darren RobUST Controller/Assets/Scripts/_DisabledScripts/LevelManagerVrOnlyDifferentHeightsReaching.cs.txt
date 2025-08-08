using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below
using Debug = UnityEngine.Debug;

public class LevelManagerVrOnlyDifferentHeightsReaching : LevelManagerScriptAbstractClass
{
    // Start is called before the first frame update

    // Name of the task
    private const string thisTaskNameString = "DifferentHeightsReachTest";

    private float StartTime;
    private float CurrentTime;
    public GameObject TheBall;

    public GameObject LeftHand;
    public GameObject RightHand;
    public GameObject WaistTracker;
    public GameObject ChestTracker;
    public GameObject headsetCameraGameObject;

    // Reaching target (Ball) and hand radius (set in start)
    float ballRadius;
    float handSphereRadius;

    // Tracker in correct region for measurement flags
    private bool useRightControllerDistanceThisFrameFlag = false;
    private bool useLeftControllerDistanceThisFrameFlag = false;
    private bool useChestTrackerDistanceThisFrameFlag = false;


    public Transform PlayerPosition;
    private MovePlayerToPositionOnStartup playerRepositioningScript;
    private Vector3 startingPointOfThePlayer;
    private const float timeForReadingInSetup = 10000.0f; // milliseconds
    private const float timePerReachingDirectionAfterLastBallTouchInMs = 4000; // milliseconds
    private const float timeForFeedbackInMs = 100; // milliseconds
    private float GameRuleUnderstandingTime = 3f;
    private float FinalCertifyTime = 10f;

    // State machine states
    private string currentState;
    private string setupStateString = "SETUP";
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string instructionsStateString = "INSTRUCTIONS_STATE";
    private string trialActiveState = "TRIAL_ACTIVE";
    private string givingFeedbackStateString = "FEEDBACK";
    private string gameOverStateString = "GAME_OVER";


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
    private Vector3 currentTrialBallEndPos;
    private bool rightHandCausedFurthestPushThisTrial = false;



    // Reaching direction order for the current trial
    // The integer value in each element corresponds to the index
    // of the reaching direction in the reachingDirectionsInDegreesCcwFromRight variable


    // Pseudorandom number generator for randomizing reach order
    private static System.Random randomNumberGenerator = new System.Random();

    // trial transition timer
    private Stopwatch stateTransitionStopwatch = new Stopwatch();

    // feedback
    private string[] textPerDirection = new string[] { "Turn your head to the RIGHT! \n Push the ball away!",
                                                        "It's on your RIGHT 45 degrees! \n Push the ball away!",
                                                        "It's in front of you! \n Push the ball away!",
                                                        "It's on your LEFT 45 degrees! \n Push the ball away!",
                                                        "Turn your head to the LEFT! \n Push the ball away!"};

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
    private float ballSpawnFractionUpperLimbLength; // Set in editor!!!!!
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

    // Reaching and chest excursion file name and saving settings
    private string defaultExcursionPerformanceSummaryFileName; // save a "default" summary of max chest and reach excursion, in case we want to load them.
    public bool overwriteExcursionLimits; // whether or not to overwrite the excursion limits already recorded for this subject on this day, if they exist.
    private bool excursionLimitsAlreadyExist = false; // whether or not the excursion limit file already exists. We assume false and check when creating file names.

    // subject-specific distances
    private float upperArmLengthInMeters;
    private float forearmLengthInMeters;
    private float chestToSternalNotchInMeters;

    public Text text;
    public Canvas bestReachDistanceCanvas;
    public Text bestReachDistanceText;

    // Tracking interactions with the ball (updated each frame)
    private bool lastFrameBallTouchedFlag = false; // whether or not the ball is in contact with EITHER hand.
    private bool leftHandCouldPushReachTarget = false; // if the ball is in contact with the left hand
    private bool rightHandCouldPushReachTarget = false;  // if the ball is in contact with the right hand

    // Player reorientation controls. 
    public bool waitForToggleHomeToStartInstructions; // whether the game should require the experimenter to toggle the player home to start (true) or not (false)
    private bool hmdToggledToHomeByExperimenter = false; // whether or not the experimenter has toggled the player home this run

    // center of mass manager
    public ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    private bool centerOfMassManagerReadyStatus = false; // whether or not the COM manager is ready to dispense Vicon data (initialize as no = false).

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

    //stimulation status
    private string currentStimulationStatus; //the current stimulation status for this block
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";



    void Start()
    {
        StartTime = Time.time;
        CurrentTime = Time.time;
        text.text = "This is a test of how far you can reach! \n When the game starts,\n push the ball as far as you can!";

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

        // Make the balls spawn at some percentage of the upper limb length
        ballSpawnRadius = ballSpawnFractionUpperLimbLength * (subjectSpecificDataScript.getUpperArmLengthInMeters() + subjectSpecificDataScript.getForearmLengthInMeters());

        // Get ball and hand object radius
        ballRadius = (TheBall.transform.localScale.x / 2.0f); // Default diameter is 1.0m, so the radius is 0.5 * the scaling factor (Assuming a sphere)
        handSphereRadius = LeftHand.transform.localScale.x / 2.0f;

        setFrameAndTrialDataNaming();

        // Assign subject-specific distances
        upperArmLengthInMeters = subjectSpecificDataScript.getUpperArmLengthInMeters();
        forearmLengthInMeters = subjectSpecificDataScript.getForearmLengthInMeters();
        chestToSternalNotchInMeters = subjectSpecificDataScript.getVerticalDistanceChestToShouldersInMeters();

        Debug.Log("Atan2 test, expect a negative value in radians close to -pi: " + Mathf.Atan2(-0.01f, -.99f));

        //StartCoroutine(Sparwn());
    }


    //FixedUpdate Should Always Record The Maximum of the Reaching Distance;
    void FixedUpdate()
    {
        //float timestep = Time.time - CurrentTime;

        //ManageStateTransitionStopwatch();

        if (currentState == setupStateString)
        {
            // if the player has been reoriented already
            if (playerRepositioningScript.IsCameraSetup())
            {
                // Collect information about the player starting point
                (startingPointOfThePlayer, _) = playerRepositioningScript.GetNeutralPlayerOrientationAndStartingPosition();

                // Base ball heights on tracker and HMD y-axis position, now that tracking has (likely) started.
                AssignReachingTargetHeightsOrderForAllTrials();

                // If we're syncing with external hardware (EMGs), we should move to a special state 
                // for EMG setup
                if (streamingEmgDataFlag == true)
                {
                    // then move to the waiting for EMG state
                    changeActiveState(waitingForEmgReadyStateString);
                }
                else // If not using EMGs, then move on to normal setup
                {
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
        }
        else if (currentState == waitingForEmgReadyStateString) // NOT IMPLEMENTED! See uptown implementation for EMGs if this is ever needed.
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

            changeActiveState(instructionsStateString);

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
                    if(hmdToggledToHomeByExperimenter == false)
                    {

                        // Recompute ball heights on tracker and HMD y-axis position, now that the headset is on the head and the subject is in neutral position
                        AssignReachingTargetHeightsOrderForAllTrials();

                        // Start the state transition stopwatch. We should be in the Instructions state, so we can start the stopwatch to time this stage.
                        stateTransitionStopwatch.Restart();

                        // Note that the player has been toggled home
                        hmdToggledToHomeByExperimenter = true;
                    }

                    // If the instructions state time has elapsed
                    if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForReadingInSetup)
                    {
                        changeActiveState(trialActiveState);
                    }
                }
            }
            else
            {
                if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForReadingInSetup)
                {
                    changeActiveState(trialActiveState);
                }
            }
        }
        // If we're currently measuring reaching distance along some direction
        else if (currentState == trialActiveState)
        {
            // Track the reach and update the reaching target (Ball) position if needed. 
            // Also, only start the stateTransitionStopwatch after the ball has been contacted and then contact ends.
            TrackMaxReachingDistanceAlongDirection();

            storeFrameData();

            if (stateTransitionStopwatch.ElapsedMilliseconds >= timePerReachingDirectionAfterLastBallTouchInMs)
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
            storeFrameData();
        }
        else
        {

        }
    }



    private void TrackMaxReachingDistanceAlongDirection()
    {

        // Get current reaching angle
        float currentReachAngle = reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]];

        // Create a unit vector along the reaching direction
        Vector3 unitVectorReachingDirection = new Vector3(Mathf.Cos(currentReachAngle * Mathf.Deg2Rad), 0.0f, Mathf.Sin(currentReachAngle * Mathf.Deg2Rad));

        // Compute current right controller angle (relative to player start position)
        float XaxisDiffRight = RightHand.transform.position.x - startingPointOfThePlayer.x;
        float ZaxisDiffRight = RightHand.transform.position.z - startingPointOfThePlayer.z;
        float angleRight = Mathf.Atan2(ZaxisDiffRight, XaxisDiffRight) * Mathf.Rad2Deg;
        angleRight = ConvertAngleDependingOnCurrentReachAngle(angleRight, currentReachAngle);

        // Compute current left controller angle (relative to player start position)
        float XaxisDiffLeft = LeftHand.transform.position.x - startingPointOfThePlayer.x;
        float ZaxisDiffLeft = LeftHand.transform.position.z - startingPointOfThePlayer.z;
        float angleLeft = Mathf.Atan2(ZaxisDiffLeft, XaxisDiffLeft) * Mathf.Rad2Deg;
        angleLeft = ConvertAngleDependingOnCurrentReachAngle(angleLeft, currentReachAngle);

        // Compute chest tracker position and angle (relative to player start position)
        Vector3 chestTrackerPosition = ChestTracker.transform.position; // the shoulder midpoint is replaced with hcest position
        float XaxisDiffChest = chestTrackerPosition.x - startingPointOfThePlayer.x;
        float ZaxisDiffChest = chestTrackerPosition.z - startingPointOfThePlayer.z;
        float anglePlayerStartToChest = Mathf.Atan2(ZaxisDiffChest, XaxisDiffChest) * Mathf.Rad2Deg;
        anglePlayerStartToChest = ConvertAngleDependingOnCurrentReachAngle(anglePlayerStartToChest, currentReachAngle);

        // Determine if the right controller is in a valid area for measuring its reaching distance
        bool useRightControllerDistance = false;
        rightHandCouldPushReachTarget = false; // reset the right hand touching ball flag to false
        float projectedRightControllerDistance = 0.0f;
        float rightHandDistanceToReachTarget = Vector3.Distance(RightHand.transform.position, TheBall.transform.position);
        float rightHandVerticalDistanceFromBallCenter = Mathf.Abs(RightHand.transform.position.y - TheBall.transform.position.y);
        // If the right controller is within a radius of the ball center
        if (angleRight <= currentReachAngle + 15.0f && angleRight >= currentReachAngle - 15.0f && rightHandVerticalDistanceFromBallCenter < ballRadius)
        {
            // We consider it
            useRightControllerDistance = true;

            // Compute the distance along the reaching direction by projecting the controller onto the reaching direction
            projectedRightControllerDistance = Vector3.Dot(RightHand.transform.position, unitVectorReachingDirection);

            if (rightHandDistanceToReachTarget <= (ballRadius + handSphereRadius))
            {
                rightHandCouldPushReachTarget = true;
            }
        }
        useRightControllerDistanceThisFrameFlag = useRightControllerDistance;

        // Determine if the left controller is in a valid area for measuring its reaching distance
        bool useLeftControllerDistance = false;
        leftHandCouldPushReachTarget = false; // reset the left hand touching ball flag to false
        float projectedLeftControllerDistance = 0.0f;
        float leftHandDistanceToReachTarget = Vector3.Distance(LeftHand.transform.position, TheBall.transform.position);
        float leftHandVerticalDistanceFromBallCenter = Mathf.Abs(LeftHand.transform.position.y - TheBall.transform.position.y);
        // If the left controller is within a minimum angle (in degrees) of the desired reach direction
        if (angleLeft <= currentReachAngle + 15.0f && angleLeft >= currentReachAngle - 15.0f && leftHandVerticalDistanceFromBallCenter < ballRadius)
        {
            // We consider it
            useLeftControllerDistance = true;

            // Compute the distance along the reaching direction by projecting the controller onto the reaching direction
            projectedLeftControllerDistance = Vector3.Dot(LeftHand.transform.position, unitVectorReachingDirection);

            if (leftHandDistanceToReachTarget <= (ballRadius + handSphereRadius))
            {
                leftHandCouldPushReachTarget = true;
            }

        }
        useLeftControllerDistanceThisFrameFlag = useLeftControllerDistance;

        // Determine if the shoulder midpoint is in a valid area for measuring its lean distance
        bool isChestTrackerInRightArea = false;
        float projectedChestExcursionDistance = 0.0f;
        // If the shuolder midpoint is within a minimum angle (in degrees) of the desired reach direction
        if (anglePlayerStartToChest <= currentReachAngle + 40.0f && anglePlayerStartToChest >= currentReachAngle - 40.0f)
        {
            // We consider it
            isChestTrackerInRightArea = true;

            // Compute the distance along the reaching direction by projecting the shoulder midpoint onto the reaching direction
            projectedChestExcursionDistance = Vector3.Dot(chestTrackerPosition, unitVectorReachingDirection);
        }
        useChestTrackerDistanceThisFrameFlag = isChestTrackerInRightArea;

        // Now, determine the greatest reaching distance among the valid controllers
        float currentReachingDistanceAlongDirection = 0.0f;
        bool currentBallPotentiallyPushed = false;
        bool rightHandUsedFlag = false;
        if (useLeftControllerDistance && useRightControllerDistance) // whether or not we use the controller depends on if it's in the right pie wedge
        {
            currentReachingDistanceAlongDirection = Mathf.Max(projectedRightControllerDistance, projectedLeftControllerDistance);
            if ((projectedRightControllerDistance >= projectedLeftControllerDistance) && rightHandCouldPushReachTarget)
            {
                rightHandUsedFlag = true;
                currentBallPotentiallyPushed = true;
            }
            else if ((projectedLeftControllerDistance > projectedRightControllerDistance) && leftHandCouldPushReachTarget)
            {
                rightHandUsedFlag = false;
                currentBallPotentiallyPushed = true;
            }
        }
        else if (useLeftControllerDistance)
        {
            currentReachingDistanceAlongDirection = projectedLeftControllerDistance;
            rightHandUsedFlag = false;
            if (leftHandCouldPushReachTarget)
            {
                currentBallPotentiallyPushed = true;
            }
        }
        else if (useRightControllerDistance)
        {
            currentReachingDistanceAlongDirection = projectedRightControllerDistance;
            rightHandUsedFlag = true;
            if (rightHandCouldPushReachTarget)
            {
                currentBallPotentiallyPushed = true;
            }
        }
        else
        {
            // do nothing, neither controller is valid
        }

        // Store the current reaching distance if it is greater than the max recorded so far
        float bestDistanceThisTrial = bestReachingDistancesPerDirectionInTrial[currentTrialNumber];
        if (currentReachingDistanceAlongDirection > bestDistanceThisTrial)
        {
            bestReachingDistancesPerDirectionInTrial[currentTrialNumber] = currentReachingDistanceAlongDirection;

            // If we reached the furthest this trial, set the right/left hand flag based on which hand is being used
            if(rightHandUsedFlag == true)
            {
                rightHandCausedFurthestPushThisTrial = true; // true = right hand did furthest push
            }
            else
            {
                rightHandCausedFurthestPushThisTrial = false; // false = left hand did furthest push
            }

            // Update the reaching target/ball position if we've reached further than it's starting distance.
            // Note, the ball won't move if we haven't reached further than it's starting distance
            if (currentBallPotentiallyPushed)
            {
                MoveBallToReachingDirectionCurrentMaxDistanceBeyondStart(currentReachingDistanceAlongDirection);
            }
        }



        // Store the current shuolder midpoint distance if it is greater than the max recorded so far
        float bestChestExcursionDistanceThisTrial = bestChestExcursionDistancesPerDirection[currentTrialNumber];
        if (projectedChestExcursionDistance > bestChestExcursionDistanceThisTrial && isChestTrackerInRightArea)
        {
            bestChestExcursionDistancesPerDirection[currentTrialNumber] = projectedChestExcursionDistance;
        }

        // Update the best reach text
        bestReachDistanceText.text = "Furthest reach:" + "\n" +
            bestReachingDistancesPerDirectionInTrial[currentTrialNumber].ToString("F2") + " [m]";

        // If the ball is currently NOT being touched (pushed), but it was being touched last frame, then 
        // start the state transition stopwatch. 
        if((currentBallPotentiallyPushed != lastFrameBallTouchedFlag) && (lastFrameBallTouchedFlag == true))
        {
            stateTransitionStopwatch.Restart(); // set to 0 and start

        } // Else if the ball is currently being touched but was NOT being touched last frame, then reset the stopwatch.
        // Note that reset is not restarting, and the stopwatch will not be counting up.
        else if ((currentBallPotentiallyPushed != lastFrameBallTouchedFlag) && (currentBallPotentiallyPushed == true))
        {
            stateTransitionStopwatch.Reset(); // set to 0 but leave in the stopped state
        }

        // Update the lastBallPotentiallPushedFlag
        lastFrameBallTouchedFlag = currentBallPotentiallyPushed;
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
            else if (currentState == waitingForEmgReadyStateString)
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
        text.text = "Trial: " + currentTrialNumber + "\n" + textPerDirection[reachingDirectionOrderListThisBlock[currentTrialNumber]];

        // Move the ball to the "spawn location"
        MoveBallToReachingDirectionSpawnLocation();

        // Store the ball starting position
        currentTrialBallStartPos = TheBall.transform.position;

        // Update the best reach text orientation
        Vector3 desiredTextForwardsDirection = TheBall.transform.position - new Vector3(startingPointOfThePlayer.x, TheBall.transform.position.y, startingPointOfThePlayer.z);
        Quaternion textOrientation = Quaternion.LookRotation(desiredTextForwardsDirection, Vector3.up);
        bestReachDistanceCanvas.transform.rotation = textOrientation;

        // Reset the stopwatch to zero, but leave it stopped.
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

        // Reset the stopwatch that keeps track of how long we have per trial/state
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
        tellDataRecorderToWriteStoredDataToFile();

        //
        TestEnding = true;

        // Display text that the task is over
        text.text = "You data has been recorded. \n Thank you for testing!";
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

    private void AssignReachingTargetHeightsOrderForAllTrials()
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
            }
            else if (currentReachTargetSpecifier == "waist" || currentReachTargetSpecifier == "Waist")
            {
                // Get the current waist/pelvis tracker y-pos
                reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex] = WaistTracker.transform.position.y;
            }
            else if (currentReachTargetSpecifier == "hmd" || currentReachTargetSpecifier == "Hmd" || currentReachTargetSpecifier == "HMD")
            {
                // Get the current waist/pelvis tracker y-pos
                reachingTargetYHeightsSpecifierValues[reachingHeightSpecifierIndex] = headsetCameraGameObject.transform.position.y;
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





    private void MoveBallToReachingDirectionSpawnLocation()
    {
        // Get the ball angle for this trial
        float BallRebornAngle = reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]];

        Debug.Log("Trial " + currentTrialNumber + " , ball angle is: " + BallRebornAngle);
        // Convert to radians
        float BallRebornAngleRadians = BallRebornAngle * (Mathf.PI / 180.0f);
        // Set the ball spawn position
        TheBall.transform.position = startingPointOfThePlayer + new Vector3(ballSpawnRadius * Mathf.Cos(BallRebornAngleRadians), 0, ballSpawnRadius * Mathf.Sin(BallRebornAngleRadians));

        // Modify the ball height to reflect the reaching height this trial
        float ballYPosThisTrial = reachingTargetYHeightsSpecifierValues[reachingYValueOrderListThisBlock[currentTrialNumber]];
        TheBall.transform.position = new Vector3(TheBall.transform.position.x, ballYPosThisTrial, TheBall.transform.position.z);
    }

    private void MoveBallToReachingDirectionCurrentMaxDistanceBeyondStart(float currentMaxReachDistanceThisDirection)
    {
        if (currentMaxReachDistanceThisDirection > ballSpawnRadius - ballRadius - handSphereRadius) // if the hand sphere could have contacted the ball
        {
            // Add a ball and hand radius to the max reach distance so that the ball will move as if being pushed by the hand sphere.
            float jitterBufferDistanceInMeters = 0.005f;
            float apparentReachingTargetCenterPos = currentMaxReachDistanceThisDirection + ballRadius + handSphereRadius - jitterBufferDistanceInMeters;

            float BallRebornAngle = reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]];

            float BallRebornAngleRadians = BallRebornAngle * (Mathf.PI / 180.0f);

            float ballYPosThisTrial = reachingTargetYHeightsSpecifierValues[reachingYValueOrderListThisBlock[currentTrialNumber]];
            TheBall.transform.position = new Vector3(startingPointOfThePlayer.x, ballYPosThisTrial, startingPointOfThePlayer.z) +
                new Vector3(apparentReachingTargetCenterPos * Mathf.Cos(BallRebornAngleRadians), 0, apparentReachingTargetCenterPos * Mathf.Sin(BallRebornAngleRadians));
        }
    }


    private void setFrameAndTrialDataNaming()
    {
        // Set the frame data column headers
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT", "TRIAL_NUMBER",
            "HMD_TOGGLED_HOME_FLAG", "POSITION_OF_HEADSET_X", "POSITION_OF_HEADSET_Y", "POSITION_OF_HEADSET_Z",
            "ORIENTATION_EULER_OF_HEADSET_X", "ORIENTATION_EULER_OF_HEADSET_Y", "ORIENTATION_EULER_OF_HEADSET_Z",
            "HEADSET_X_AXIS_UNITY_FRAME_X", "HEADSET_X_AXIS_UNITY_FRAME_Y", "HEADSET_X_AXIS_UNITY_FRAME_Z",
            "HEADSET_Y_AXIS_UNITY_FRAME_X", "HEADSET_Y_AXIS_UNITY_FRAME_Y", "HEADSET_Y_AXIS_UNITY_FRAME_Z",
            "BALL_TOUCHED_BY_EITHER_HAND_FLAG", "BALL_TOUCHED_BY_RIGHT_HAND_FLAG", "BALL_TOUCHED_BY_LEFT_HAND_FLAG",
            "RIGHT_CONTROLLER_IN_CORRECT_REGION_FLAG", "POSITION_OF_RIGHT_CONTROLLER_X", "POSITION_OF_RIGHT_CONTROLLER_Y", "POSITION_OF_RIGHT_CONTROLLER_Z",
            "ORIENTATION_EULER_OF_RIGHT_CONTROLLER_X", "ORIENTATION_EULER_OF_RIGHT_CONTROLLER_Y", "ORIENTATION_EULER_OF_RIGHT_CONTROLLER_Z",
            "RIGHT_CONTROLLER_X_AXIS_UNITY_FRAME_X", "RIGHT_CONTROLLER_X_AXIS_UNITY_FRAME_Y", "RIGHT_CONTROLLER_X_AXIS_UNITY_FRAME_Z",
            "RIGHT_CONTROLLER_Y_AXIS_UNITY_FRAME_X", "RIGHT_CONTROLLER_Y_AXIS_UNITY_FRAME_Y", "RIGHT_CONTROLLER_Y_AXIS_UNITY_FRAME_Z",
            "LEFT_CONTROLLER_IN_CORRECT_REGION_FLAG", "POSITION_OF_LEFT_CONTROLLER_X", "POSITION_OF_LEFT_CONTROLLER_Y", "POSITION_OF_LEFT_CONTROLLER_Z",
            "ORIENTATION_EULER_OF_LEFT_CONTROLLER_X", "ORIENTATION_EULER_OF_LEFT_CONTROLLER_Y", "ORIENTATION_EULER_OF_LEFT_CONTROLLER_Z",
            "LEFT_CONTROLLER_X_AXIS_UNITY_FRAME_X", "LEFT_CONTROLLER_X_AXIS_UNITY_FRAME_Y", "LEFT_CONTROLLER_X_AXIS_UNITY_FRAME_Z",
            "LEFT_CONTROLLER_Y_AXIS_UNITY_FRAME_X", "LEFT_CONTROLLER_Y_AXIS_UNITY_FRAME_Y", "LEFT_CONTROLLER_Y_AXIS_UNITY_FRAME_Z",
            "CHEST_IN_CORRECT_REGION_FLAG", "POSITION_OF_CHEST_X", "POSITION_OF_CHEST_Y", "POSITION_OF_CHEST_Z",
            "ORIENTATION_EULER_OF_CHEST_X", "ORIENTATION_EULER_OF_CHEST_Y", "ORIENTATION_EULER_OF_CHEST_Z",
            "CHEST_X_AXIS_UNITY_FRAME_X", "CHEST_X_AXIS_UNITY_FRAME_Y", "CHEST_X_AXIS_UNITY_FRAME_Z",
            "CHEST_Y_AXIS_UNITY_FRAME_X", "CHEST_Y_AXIS_UNITY_FRAME_Y", "CHEST_Y_AXIS_UNITY_FRAME_Z",
            "POSITION_OF_WAIST_X", "POSITION_OF_WAIST_Y", "POSITION_OF_WAIST_Z",
            "ORIENTATION_EULER_OF_WAIST_X", "ORIENTATION_EULER_OF_WAIST_Y", "ORIENTATION_EULER_OF_WAIST_Z",
            "WAIST_X_AXIS_UNITY_FRAME_X", "WAIST_X_AXIS_UNITY_FRAME_Y", "WAIST_X_AXIS_UNITY_FRAME_Z",
            "WAIST_Y_AXIS_UNITY_FRAME_X", "WAIST_Y_AXIS_UNITY_FRAME_Y", "WAIST_Y_AXIS_UNITY_FRAME_Z",
            };

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // Set the trial data column headers
       string[] csvTrialDataHeaderNames = new string[]{
        "TRIAL_NUMBER", "UPPER_ARM_LENGTH_METERS", "FOREARM_LENGTH_METERS", "CHEST_TO_STERNAL_NOTCH_LENGTH_METERS", 
        "HMD_START_POS_X", "HMD_START_POS_Y", "HMD_START_POS_Z", "HMD_TOGGLE_HOME_VECTOR_X", "HMD_TOGGLE_HOME_VECTOR_Y", "HMD_TOGGLE_HOME_VECTOR_Z",
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

        //set the frame data and the reach and lean performance trial subdirectory name (will go inside the CSV folder in Assets)
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvExcursionPerformanceSummarySubdirectoryName(subdirectoryString);

        // 4.) Call the function to set the file names (within the subdirectory) for the current block
        setFileNamesForCurrentBlockTrialAndFrameData(subdirectoryString);


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



    private void setFileNamesForCurrentBlockTrialAndFrameData(string subdirectoryString)
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

        // Also set the default and run-specific excursion performance summary  file name
        string excursionPerformanceSummaryRunSpecificFileName = fileNameStub + "_Reaching_Excursion_Performance_Summary" + delimiter + currentStimulationStatus + ".csv";
        defaultExcursionPerformanceSummaryFileName = "Reaching_Excursion_Performance_Summary" + delimiter + currentStimulationStatus + ".csv";
        // Set the excursion performance summary file name
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(excursionPerformanceSummaryRunSpecificFileName);

        // If the default excursion performance summary file already exists
        bool excursionPerformanceFileAlreadyExists = generalDataRecorderScript.DoesFileAlreadyExist(subdirectoryString, defaultExcursionPerformanceSummaryFileName);
        if (excursionPerformanceFileAlreadyExists)
        {
            // We set the flag indicating the excursion limits file already exists.
            excursionLimitsAlreadyExist = true;
            Debug.Log("Reaching excursion performance summary already existed for this date.");
        }
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
        else if (currentState == trialActiveState)
        {
            stateAsFloat = 2.0f;
        }
        else if (currentState == givingFeedbackStateString)
        {
            stateAsFloat = 3.0f;
        }
        else if (currentState == gameOverStateString)
        {
            stateAsFloat = 4.0f;
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

        // Headset position (x,y,z)
        frameDataToStore.Add(headsetCameraGameObject.transform.position.x);
        frameDataToStore.Add(headsetCameraGameObject.transform.position.y);
        frameDataToStore.Add(headsetCameraGameObject.transform.position.z);

        // Headset orientation euler angles
        frameDataToStore.Add(headsetCameraGameObject.transform.eulerAngles.x);
        frameDataToStore.Add(headsetCameraGameObject.transform.eulerAngles.y);
        frameDataToStore.Add(headsetCameraGameObject.transform.eulerAngles.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 headsetXAxisUnityFrame, Vector3 headsetYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(headsetCameraGameObject);
        frameDataToStore.Add(headsetXAxisUnityFrame.x);
        frameDataToStore.Add(headsetXAxisUnityFrame.y);
        frameDataToStore.Add(headsetXAxisUnityFrame.z);
        frameDataToStore.Add(headsetYAxisUnityFrame.x);
        frameDataToStore.Add(headsetYAxisUnityFrame.y);
        frameDataToStore.Add(headsetYAxisUnityFrame.z);

        // The ball pushed flags (updated each frame)
        frameDataToStore.Add(System.Convert.ToSingle(lastFrameBallTouchedFlag)); // whether or not either hand was in contact with the reaching target this last frame
        frameDataToStore.Add(System.Convert.ToSingle(rightHandCouldPushReachTarget)); // whether or not the right hand was in contact with the reaching target this last frame
        frameDataToStore.Add(System.Convert.ToSingle(leftHandCouldPushReachTarget));  // whether or not the left hand was in contact with the reaching target this last frame

        // Right hand in correct region flag
        frameDataToStore.Add(System.Convert.ToSingle(useRightControllerDistanceThisFrameFlag));

        // Right controller position (x,y,z)
        frameDataToStore.Add(RightHand.transform.position.x);
        frameDataToStore.Add(RightHand.transform.position.y);
        frameDataToStore.Add(RightHand.transform.position.z);

        // Right controller euler angles
        frameDataToStore.Add(RightHand.transform.eulerAngles.x);
        frameDataToStore.Add(RightHand.transform.eulerAngles.y);
        frameDataToStore.Add(RightHand.transform.eulerAngles.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 rightHandXAxisUnityFrame, Vector3 rightHandYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(RightHand);
        frameDataToStore.Add(rightHandXAxisUnityFrame.x);
        frameDataToStore.Add(rightHandXAxisUnityFrame.y);
        frameDataToStore.Add(rightHandXAxisUnityFrame.z);
        frameDataToStore.Add(rightHandYAxisUnityFrame.x);
        frameDataToStore.Add(rightHandYAxisUnityFrame.y);
        frameDataToStore.Add(rightHandYAxisUnityFrame.z);

        // Left hand in correct region flag
        frameDataToStore.Add(System.Convert.ToSingle(useLeftControllerDistanceThisFrameFlag));

        // Left controller position (x,y,z)
        frameDataToStore.Add(LeftHand.transform.position.x);
        frameDataToStore.Add(LeftHand.transform.position.y);
        frameDataToStore.Add(LeftHand.transform.position.z);

        // Left controller euler angles
        frameDataToStore.Add(LeftHand.transform.eulerAngles.x);
        frameDataToStore.Add(LeftHand.transform.eulerAngles.y);
        frameDataToStore.Add(LeftHand.transform.eulerAngles.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 leftHandXAxisUnityFrame, Vector3 leftHandYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(LeftHand);
        frameDataToStore.Add(leftHandXAxisUnityFrame.x);
        frameDataToStore.Add(leftHandXAxisUnityFrame.y);
        frameDataToStore.Add(leftHandXAxisUnityFrame.z);
        frameDataToStore.Add(leftHandYAxisUnityFrame.x);
        frameDataToStore.Add(leftHandYAxisUnityFrame.y);
        frameDataToStore.Add(leftHandYAxisUnityFrame.z);

        // Chest in correct region flag
        frameDataToStore.Add(System.Convert.ToSingle(useChestTrackerDistanceThisFrameFlag));

        // Chest tracker position (x,y,z)
        frameDataToStore.Add(ChestTracker.transform.position.x);
        frameDataToStore.Add(ChestTracker.transform.position.y);
        frameDataToStore.Add(ChestTracker.transform.position.z);

        // Chest tracker euler angles
        frameDataToStore.Add(ChestTracker.transform.eulerAngles.x);
        frameDataToStore.Add(ChestTracker.transform.eulerAngles.y);
        frameDataToStore.Add(ChestTracker.transform.eulerAngles.z);

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

        // Left shoulder euler angles
        frameDataToStore.Add(WaistTracker.transform.eulerAngles.x);
        frameDataToStore.Add(WaistTracker.transform.eulerAngles.y);
        frameDataToStore.Add(WaistTracker.transform.eulerAngles.z);

        // Get y-axis (facing downwards out of tracker face) and x-axis (when looking at tracker, points to the viewer's right; i.e., a vector directed from pig's right ear to left ear)
        // unit vectors in the Unity global frame. 
        (Vector3 waistXAxisUnityFrame, Vector3 waistYAxisUnityFrame, _) = GetGameObjectUnitVectorsInUnityFrame(WaistTracker);
        frameDataToStore.Add(waistXAxisUnityFrame.x);
        frameDataToStore.Add(waistXAxisUnityFrame.y);
        frameDataToStore.Add(waistXAxisUnityFrame.z);
        frameDataToStore.Add(waistYAxisUnityFrame.x);
        frameDataToStore.Add(waistYAxisUnityFrame.y);
        frameDataToStore.Add(waistYAxisUnityFrame.z);


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



    // BEGIN: Vicon <-> Unity mapping functions and other public functions*********************************************************************************
    // NOTE: THIS VR-ONLY TASK DOES NOT NEED MANY OF THESE FUNCTIONS!

    public override string GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    // The mapping function from Vicon frame to Unity frame. This function is a member of the 
    // parent class of this script, so that other GameObjects can access it. 
    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        return new Vector3(0.0f, 0.0f, 0.0f);
    }

    // NOTE: this function is NOT the inverse of mapPointFromViconFrameToUnityFrame() in this script, because
    // the data we pass to it is typically extracted from Vive in the raw Unity frame, not the colocalized unity frame.
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
        List<Vector3> dummyList = new List<Vector3>();
        return dummyList;
    }

    public override Vector3 GetCenterOfExcursionLimitsInViconFrame()
    {
        return new Vector3(0.0f, 0.0f, 0.0f);
    }

    // Return the current desired force field type (only applicable if coordinating Unity with RobUST). 
    // This will be sent to the RobUST Labview script via TCP. 
    // Default value: let the default value be the Idle mode specifier.
    public override string GetCurrentDesiredForceFieldTypeSpecifier()
    {
        return "dummyString";
    }

    // END: Vicon <-> Unity mapping functions *********************************************************************************


    // START: other abstract level manager public functions *******************************************************************

    public override bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }

    // END: other abstract level manager public functions *******************************************************************


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

        // Store the trial start time
        trialDataToStore.Add(currentTrialStartTime);

        // Store the trial end time
        trialDataToStore.Add(currentTrialEndTime);

        // Store the ball direction angle from rightwards (CCW) in degrees for this trial
        trialDataToStore.Add(reachingDirectionsInDegreesCcwFromRight[reachingDirectionOrderListThisBlock[currentTrialNumber]]);

        // Store the ball height specifier (int) for this trial
        trialDataToStore.Add((float) reachingYValueOrderListThisBlock[currentTrialNumber]);

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



    public bool GetTheFlagOfTheTestingResult()
    {
        return TestEnding;
    }


}
