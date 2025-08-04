using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Diagnostics; 
using Debug = UnityEngine.Debug; 
using TMPro;

public class PartialSquatLevelManager : LevelManagerScriptAbstractClass
{
    // BEGIN: Variable Definitions *********************************************************************************
    public rendering renderingScript;

    public bool sendTaskOverCommandToRobustToggle;
    private bool sendTaskOverCommandToRobustTogglePrevious;

    // Task name
    private const string thisTaskNameString = "SquattingTask";

    //stoptwatch initialization
    private Stopwatch stopwatch = new Stopwatch(); //overall stopwatch to measure length of each trial
    private Stopwatch stopwatchLeg = new Stopwatch(); //stopwatch for leg sin function

    // variables for state transition ********************
    private string currentState; 
    private const string waitingForEmgReadyStateString = "waiting_for_emg_ready";
    private const string waitingForSetupStateString = "waiting_for_setup"; 
    private const string waitingForHomePosStateString = "waiting_for_home_pos"; 
    private const string preSquatStateString = "pre_squat";
    private const string squatDescentString = "squat_descent"; 
    private const string squatHoldString = "squat_hold"; 
    private const string squatAscentString = "squat_ascent";
    private const string feedbackString = "feedback"; 
    private const string gameOverStateString = "gameOverState";

    //variables for point allocation ********************
    float pointsEarnedThisTrial;
    float totalPointsEarned = 0;


    //Knee angle function variables ********************
    public float kneeAngleTargetDeg; // target knee angle at full squat depth

    //define lines
    public LineRenderer leg;
    private Vector3[] positions3;  //define leg positions outside function to be used in other variables
    private Vector3 shankBeltAttachmentPointRepresentation; // where to render the shank force point of application
    // define variables for sin wave
    private float kneeAngleDeg;
    float period = 10f; //in seconds; same for knee and ankle
    float time;  //in seconds
    float kneeWaveMean = 90f; //in degrees
    float kneeAmp = 45f;     

    //for finding pelvis velocities ********************
    private float previousPelvisPos;
    private float currentPelvisPos;

    // Variables to track mean angle of knee in squatHold ********************
    private float meanKneeAngle = 0.0f; 
    private int count = 0;

    // Variable to track maxKneeAngle in a given trial. Reset to zero at end of each trial. 
    private float maxKneeAngleThisTrial = 0.0f;
    
    //variables for feedback
    public int totalTrials; 
    private int trialCounter = 1; // start at 1

    //variables for storing pelvis pos data and velocity
    private Vector3[] pelvisPosHistory;
    private float[] pelvisPosHistoryTimeStamps;
    private int pelvisPosHistoryLen = 10; // how many observations we average to get the pelvis velocity
    private int numPelvisPositionsStoredAtStartup = 0;
    private Vector3[] pelvisVelocityHistory; 
    private float velocityXMean;
    private float velocityYMean;
    private float velocityZMean;

    private Vector3 newPelvisPos; 

    //text functions 
    public TMP_Text textElement;
    public TMP_Text totalPointsTextElement;

    // COM/marker manager 
    public ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    public ViveTrackerDataManager ViveTrackerDataManagerScript;
    public KinematicModelClass KinematicModel;
    
    // General data recorder
    public GeneralDataRecorder generalDataRecorderScript;
    private string subdirectoryName; // the subdirectory for saving this session's data
    private string mostRecentFileNameStub; // the prefix/stub for the current file name (prefix to trial, marker, frame, emg data).

    // TCP script that sends data to RobUST
    public CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;
    
    // ForceFieldHighLevelControllerScript
    public ForceFieldHighLevelControllerScript forceFieldHighLevelControllerScript;

    // Cable tension planner
    public CableTensionPlannerScript cableTensionPlannerScript;
    
    // Subject-specific data (e.g. mass, subject number, etc.)
    public SubjectInfoStorageScript subjectSpecificDataScript;

    // Boundary of stability
    private string subdirectoryWithBoundaryOfStabilityData;


    // Structure matrix builder
    public BuildStructureMatricesForBeltsThisFrameScript structureMatrixBuilderScript;
    private string subdirectoryWithSetupDataForStructureMatrixComputation;
    
    private KinematicModelOfStance stanceModel;
    private bool stanceModelAvailableFlag = false;

    // Photon sync with external hardware vars
    public bool syncingWithExternalHardwareFlag; // if we're using EMGs or other external hardware that needs to be synced, set to true by user.
    public CommunicateWithPhotonViaSerial communicateWithPhotonScript;
    private float unityFrameTimeAtWhichHardwareSyncSent;

    // Vicon devices (force plates, handlebars) data access
    public bool storeForcePlateAndViconDeviceData;
    public RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;
    // Force plate data
    private bool isForcePlateDataReadyForAccess = false;
    private Vector3 CopPositionViconFrame;
    Vector3[] allForcePlateForces; // a Vector3[] array of force plate forces. First vector is one force plate, second is the other.
    Vector3[] allForcePlateTorques; // a Vector3[] array of force plate forces. First vector is one force plate, second is the other.

    // EMG data streaming
    public bool streamingEmgDataFlag; // Whether to run the EMG service (true) or not (false)
    public GameObject emgDataStreamerObject;
    private StreamAndRecordEmgData emgDataStreamerScript; // communicates with Delsys base station, reads and saves EMG data

    // Trial start/end time
    private float mostRecentTrialStartTime; // of Unity frame
    private float mostRecentTrialEndTime; // of Unity frame
    
    // State transition criteria
    // At home -> waiting for squat start criteria
    private float kneeAngleAtHomeInDegs = 0.0f; // the fully extended, "at-home" angle
    private float maxKneeDeviationForAtHomeInDegrees = 5.0f;
    private float minimumTimeAtHomeBeforeSquatInSeconds = 2.0f;
    // waiting for squat start -> Squat descent criteria
    private float minKneeAngleForSquatStartInDegs = 7.5f;
    private float minPelvisDownwardsVelocityForSquatStartInMetersPerSec = -0.03f;
    // Squat descent -> squat hold criteria
    private float maxPelvisUpDownVelocityToStartSquatHoldInMetersPerSec = 0.03f; // m/s 
    private float pelvisStationaryTimeToStartSquatHoldInMilliseconds = 500.0f; 
    // Squat hold -> Squat ascent
    private float minPelvisUpVelocityToStartSquatAscentInMetersPerSec = 0.05f; // m/s
    private float minChangeInKneeAngleFromSquatHoldMeanToStartAscentInDegs = 2.0f;
    // Squat ascent -> feedback 
    private float maxKneeAngleForAscentToFeedbackStateSwitchInDegs = 12.0f;
    // Feedback -> Waiting for Home or Game Over state
    private float feedbackTimeInSeconds = 5.0f;
    private float thresholdForEnteringTheHoldingState = 10.0f;
    private float thresholdForExitingTheHoldingState = 15.0f;

    // MANUAL write EMG data to file toggle
    public bool writeEmgDataToFileNowToggle;
    private bool writeEmgDataToFileNowTogglePrevious;

    // BEGIN: Start and update functions *********************************************************************************
    void Start()
    {
        pelvisPosHistory = new Vector3[pelvisPosHistoryLen];
        pelvisPosHistoryTimeStamps = new float[pelvisPosHistoryLen];
        pelvisVelocityHistory = new Vector3[pelvisPosHistoryLen-1];

        // Initialize line renderer positions (for ankle, knee, and pelvis)
        Vector3 zeroVector = new Vector3(0.0f, 0.0f, 0.0f);
        positions3 = new Vector3[] { zeroVector, zeroVector, zeroVector };

        //start stopwatch
        stopwatch.Start();
        stopwatchLeg.Start();

        // Tell the COM manager that we will be using the RobUST
        if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            centerOfMassManagerScript.SetUsingCableDrivenRobotFlagToTrue();
        }
        else if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
            
        {
            ViveTrackerDataManagerScript.SetUsingCableDrivenRobotFlagToTrue(); 
        }

        // Build file naming for both saving marker, frame, etc. data and also for loading data
        SetFrameAndTrialDataNaming();

        // Init the toggle that can be used to say the task is over (for tension saving testing)
        sendTaskOverCommandToRobustTogglePrevious = sendTaskOverCommandToRobustToggle;

        // If we want EMG data
        if (streamingEmgDataFlag == true)
        {
            // Get a reference to the EMGs
            emgDataStreamerScript = emgDataStreamerObject.GetComponent<StreamAndRecordEmgData>();

            // Start in waiting for EMG state 
            currentState = waitingForEmgReadyStateString;
        }
        else
        {
            //define current state
            currentState = waitingForSetupStateString;
        }

        // Store the manual EMG toggle value
        writeEmgDataToFileNowTogglePrevious = writeEmgDataToFileNowToggle;
    }

    void Update()
    {

        // TESTING ONLY - manually writing EMG data to file
       /* if(writeEmgDataToFileNowToggle != writeEmgDataToFileNowTogglePrevious)
        {
            // Update previous
            writeEmgDataToFileNowTogglePrevious = writeEmgDataToFileNowToggle;

            // Send the command to write the EMG data to file
            generalDataRecorderScript.writeEmgDataToFile();
        }*/

        // Update pelvis position history and velocity history if we're in a state that requires it.
        if(currentState != waitingForSetupStateString && currentState != waitingForEmgReadyStateString 
                                                      && currentState != gameOverStateString)
        {
            // Update pelvis position and velocity
            PelvisPosUpdate();
            PelvisVelocity();

            // Get the knee angle from the stance model
            kneeAngleDeg = GetCurrentKneeAngleToRender(); // kneeAngleDeg is theta3 from the stance model when using Vive.
            
            // Compute the rendered knee angle
            float kneeAngleRender = 90 - kneeAngleDeg;

            // Adjust knee angle computed by marker manager to be 90 when straight and 0 when at a right angle.


            // Render the angles
            SetCurrentAngles(90.0f, kneeAngleRender);
        }

        // Check to see if the task-over command toggle has changed states.
        SendTaskOverCommandToRobustOnToggle();
        
        // Depending on the current state of the level manager state machine,
        // take action at the start of each frame and transition between states as needed.
        switch (currentState)
        {
            case waitingForEmgReadyStateString:
                if(emgDataStreamerScript.IsBaseStationReadyForSyncSignal() == true)
                {
                    changeActiveState(waitingForSetupStateString);
                }
                // do stuff
                break;

            case waitingForSetupStateString:
                //SetCurrentAngles(90, 90);
                bool forceFieldLevelManagerSetupCompleteFlag =
                    forceFieldHighLevelControllerScript.GetForceFieldLevelManagerSetupCompleteFlag();
                if (forceFieldLevelManagerSetupCompleteFlag == true)
                { 
                    stanceModel = forceFieldHighLevelControllerScript.GetStanceModel();
                    stanceModelAvailableFlag = true;
                }

                // If the stance model is available and can provide body position data
                if (stanceModelAvailableFlag == true)
                {
                    if(ViveTrackerDataManagerScript.GetViveTrackerDataHasBeenInitializedFlag() == true)
                    {
                        // We store a pelvic position history (buffer) so that we can always compute
                        // a pelvic velocity based on the previous positions.
                        if (numPelvisPositionsStoredAtStartup < pelvisPosHistoryLen - 1)
                        {
                            bool newPelvisPositionStored = PelvisPosUpdate();
                            if (newPelvisPositionStored)
                            {
                                numPelvisPositionsStoredAtStartup++;
                            }
                        }
                        else
                        { // if we've stored enough of a pelvic position history
                            Debug.Log("Squat level manager: collected a pelvic tracker history. Switching to waitingForHome state.");
                            changeActiveState(waitingForHomePosStateString);
                        }
                    }
                }

                break;

            case waitingForHomePosStateString: // wait for subject to stand up straight
                /* if knee angle is approx 0 +/1- 2 deg (standing upright) 
                    --> and stopwatch isnt running yet, restart stopwatch
                    --> and stopwatch is running for 2+ sec, change state 
                   if knee angle isnt +/- 2 deg then reset stopwatch */
                //SetCurrentAngles(90, 90);]
                
                // If knee angle is close to home (fully extended)
                
                if (kneeAngleDeg < (kneeAngleAtHomeInDegs + maxKneeDeviationForAtHomeInDegrees) ||
                    kneeAngleDeg > (kneeAngleAtHomeInDegs - maxKneeDeviationForAtHomeInDegrees)) {
                    // If our stopwatch is not running
                    if (!stopwatch.IsRunning){ 
                        // Start the stopwatch from 0 seconds
                        stopwatch.Restart();
                    }
                    // If the knee has been fully extended for the desired "at home" time
                    if(stopwatch.Elapsed.TotalSeconds >= minimumTimeAtHomeBeforeSquatInSeconds){
                        // Stop the stopwatch
                        stopwatch.Stop();
                        // Transition to pre-squat state, AKA waiting for squat start state.
                        changeActiveState(preSquatStateString); 
                    }
                } else { // if the knee has moved too far from home
                    // Reset our stopwatch to zero
                    stopwatch.Reset();
                }
                break;

            case preSquatStateString: // subject is at home and should be ready to start squat
                //SetCurrentAngles(90, kneeAngleDeg);

                /* if knee angle is > 5 and pelvis position is decreasing --> change state */ 

                //get current pelvis velocity 
                //Debug.Log("Pre-squat state: knee angle is " + kneeAngleDeg + " and x-axis velocity is " + velocityXMean);
                if (kneeAngleDeg > minKneeAngleForSquatStartInDegs){ // && velocityXMean < minPelvisDownwardsVelocityForSquatStartInMetersPerSec){ 
                    changeActiveState(squatDescentString); 
                }
                break;

            case squatDescentString: // Person is descending through their squat
/*                if(kneeAngleDeg <90){
                    SetCurrentAngles(90, kneeAngleDeg);
                }
                if(kneeAngleDeg == 45){
                    SetCurrentAngles(90, 45);
                }*/
                /* if pelvis velocity is 0 
                --> and stopwatch isnt running, restart stopwatch
                --> and stopwatch has been running over 1 sec, change state
                    --> change state 
                   if pelvis velocity isnt 0 then reset stopwatch 
                - only do this every 0.1 seconds to check previous pelvis position */

                //Debug.Log("Squat descent state: knee angle is " + kneeAngleDeg + " and x-axis velocity is " + velocityXMean);
                // if the pelvis up/down velocity is close to zero
                if (velocityXMean < maxPelvisUpDownVelocityToStartSquatHoldInMetersPerSec &&
                    velocityXMean > -maxPelvisUpDownVelocityToStartSquatHoldInMetersPerSec)
                { 
                    // Start the stopwatch
                    if (!stopwatch.IsRunning){
                        stopwatch.Start();
                    }
                    // If pelvis up/down velocity has been close to zero for long enough
                    if(stopwatch.ElapsedMilliseconds >= pelvisStationaryTimeToStartSquatHoldInMilliseconds
                        && kneeAngleDeg >= kneeAngleTargetDeg - thresholdForEnteringTheHoldingState
                       && kneeAngleDeg <= kneeAngleTargetDeg + thresholdForEnteringTheHoldingState)
                    {
                        stopwatch.Stop();
                        changeActiveState(squatHoldString); // change if pelvis velocity is 0 for 1 sec
                    }
                } else{
                    stopwatch.Reset();
                }
                break;

            case squatHoldString:
                //SetCurrentAngles(90, 45);
            /* if stopwatch is less than 3 sec, collect info on mean pelvis value
            else if stopwatch is over 3 sec, 
                and position of pelvis is increasing, knee angle is 2 more than mean hold knee angle
                --> change state */ 

                // If the time on the stopwatch is less than the total desired Squat Hold time
                if (stopwatch.Elapsed.TotalSeconds <= 3.0f) {
                    // If the subject has stopped holding their squat too early
                    if (kneeAngleDeg <= kneeAngleTargetDeg - thresholdForExitingTheHoldingState
                        || kneeAngleDeg >= kneeAngleTargetDeg + thresholdForEnteringTheHoldingState)
                    {
                        // Then let them know and terminate the trial, telling them to stand up again.
                        textElement.text= "Remember to hold the squat, try again please!";
                        changeActiveState(waitingForHomePosStateString);

                    }
                    // Else if the subject is still holding their squat
                    else
                    {
                        // Then the subject should continue to hold
                        // Update the mean knee angle
                        meanKneeAngle = (meanKneeAngle * count + kneeAngleDeg) / (count + 1); // update the mean knee angle
                        // Update the max knee angle
                        if(kneeAngleDeg > maxKneeAngleThisTrial)
                        {
                            maxKneeAngleThisTrial = kneeAngleDeg;
                        }
                        //Debug.Log("Hold state: Current knee angle: " + kneeAngleDeg + " and mean knee angle: " + meanKneeAngle);
                        count++;
                        float timeLeft = 3.0f - (float)stopwatch.Elapsed.TotalSeconds;
                        textElement.text = "Hold squat! \n" + timeLeft.ToString("F1"); //display time left w 2 decimal places
                    }
                // Else if the subject has held the squat for long enough
                } else {
                    // Tell them to stand up
                    textElement.text = "Please stand up";
                    // Transition out of the Hold state when pelvis is moving upwards and knee angle has decreased from mean squat hold angle
                    if (velocityXMean > minPelvisUpVelocityToStartSquatAscentInMetersPerSec && 
                        kneeAngleDeg <= meanKneeAngle - minChangeInKneeAngleFromSquatHoldMeanToStartAscentInDegs){
                        stopwatch.Stop();
                        changeActiveState(squatAscentString); 
                    }
                    //stop stopwatch
                }
                //add extra condition within if statement if knee angle is greater than entersquathold knee angle then you break on this
                break;

            case squatAscentString:
/*                if(kneeAngleDeg < 85){
                    SetCurrentAngles(90, kneeAngleDeg);
                }
                if(kneeAngleDeg >= 85){
                    SetCurrentAngles(90, 90);
                }*/
            //once knee angle is less than 3 degrees change state

                if (kneeAngleDeg < maxKneeAngleForAscentToFeedbackStateSwitchInDegs){
                    changeActiveState(feedbackString);
                }
                break;

            case feedbackString:
            //SetCurrentAngles(90, 90);
            /* restart stopwatch once state starts
            --> if time is over 5 sec and trials arent completed change to waiting for home state
            --> if time is over 5 sec and trials are completed, change to game over state */

                // If our feedback time has elapsed and we have more trials to go
                if (stopwatch.Elapsed.TotalSeconds >= feedbackTimeInSeconds && trialCounter < totalTrials){
                    changeActiveState(waitingForHomePosStateString); 
                // Else if the feedback time has elapsed and there are no more trials    
                } else if (stopwatch.Elapsed.TotalSeconds >= feedbackTimeInSeconds && trialCounter == totalTrials)
                {
                    // If the game is over
                    // Send the-task over command to RobUST so that it knows to save the tensions to file. 
                    forceFieldRobotTcpServerScript.SendCommandWithTaskOverSpecifier();

                    // Change to the game over state
                    changeActiveState(gameOverStateString); 
                }
                break;

            case gameOverStateString:
                break; 
        }
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





    // BEGIN: Setup functions ********************************************************************************************

    private void SetFrameAndTrialDataNaming() 
    {
        // 1.) Frame data naming
        // A string array with all of the frame data header names
        // The frame data will mostly be to store the Vive tracker position and orientation.
        if (storeForcePlateAndViconDeviceData == true)
        {
            string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE", "SQUAT_STATE_MACHINE_STATE", "IS_TRACKER_DATA_FRESH_FLAG",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_0", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_0", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_0",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_1", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_1", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_1",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_2", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_2", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_2",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_3", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_3", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_3",
            "LEFT_ANKLE_TRACKER_POS_X_UNITY_FRAME", "LEFT_ANKLE_TRACKER_POS_Y_UNITY_FRAME", "LEFT_ANKLE_TRACKER_POS_Z_UNITY_FRAME",
            "LEFT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_X", "LEFT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Y", "LEFT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "LEFT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_X", "LEFT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "LEFT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "LEFT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_X", "LEFT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "LEFT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "RIGHT_ANKLE_TRACKER_POS_X_UNITY_FRAME", "RIGHT_ANKLE_TRACKER_POS_Y_UNITY_FRAME", "RIGHT_ANKLE_TRACKER_POS_Z_UNITY_FRAME",
            "RIGHT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_X", "RIGHT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Y", "RIGHT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "RIGHT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_X", "RIGHT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "RIGHT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "RIGHT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_X", "RIGHT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "RIGHT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "LEFT_SHANK_TRACKER_TRACKER_POS_X_UNITY_FRAME", "LEFT_SHANK_TRACKER_POS_Y_UNITY_FRAME", "LEFT_SHANK_TRACKER_POS_Z_UNITY_FRAME",
            "LEFT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_X", "LEFT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Y", "LEFT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "LEFT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_X", "LEFT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "LEFT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "LEFT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_X", "LEFT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "LEFT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "RIGHT_SHANK_TRACKER_POS_X_UNITY_FRAME", "RIGHT_SHANK_TRACKER_POS_Y_UNITY_FRAME", "RIGHT_SHANK_TRACKER_POS_Z_UNITY_FRAME",
            "RIGHT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_X", "RIGHT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Y", "RIGHT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "RIGHT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_X", "RIGHT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "RIGHT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "RIGHT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_X", "RIGHT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "RIGHT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "PELVIS_TRACKER_POS_X_UNITY_FRAME", "PELVIS_TRACKER_POS_Y_UNITY_FRAME", "PELVIS_TRACKER_POS_Z_UNITY_FRAME",
            "PELVIS_TRACKER_X_VECTOR_UNITY_FRAME_X", "PELVIS_TRACKER_X_VECTOR_UNITY_FRAME_Y", "PELVIS_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "PELVIS_TRACKER_Y_VECTOR_UNITY_FRAME_X", "PELVIS_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "PELVIS_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "PELVIS_TRACKER_Z_VECTOR_UNITY_FRAME_X", "PELVIS_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "PELVIS_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "TRUNK_TRACKER_POS_X_UNITY_FRAME", "TRUNK_TRACKER_POS_Y_UNITY_FRAME", "TRUNK_TRACKER_POS_Z_UNITY_FRAME",
            "TRUNK_TRACKER_X_VECTOR_UNITY_FRAME_X", "TRUNK_TRACKER_X_VECTOR_UNITY_FRAME_Y", "TRUNK_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "TRUNK_TRACKER_Y_VECTOR_UNITY_FRAME_X", "TRUNK_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "TRUNK_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "TRUNK_TRACKER_Z_VECTOR_UNITY_FRAME_X", "TRUNK_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "TRUNK_TRACKER_Z_VECTOR_UNITY_FRAME_Z", 
            "FORCE_PLATE_1_FX", "FORCE_PLATE_1_FY", "FORCE_PLATE_1_FZ",
            "FORCE_PLATE_1_TX", "FORCE_PLATE_1_TY", "FORCE_PLATE_1_TZ",
            "FORCE_PLATE_2_FX", "FORCE_PLATE_2_FY", "FORCE_PLATE_2_FZ",
            "FORCE_PLATE_2_TX", "FORCE_PLATE_2_TY", "FORCE_PLATE_2_TZ",
            "FORCE_PLATE_COP_VICON_FRAME_X", "FORCE_PLATE_COP_VICON_FRAME_Y", "FORCE_PLATE_COP_VICON_FRAME_Z"
            };

            //tell the data recorder what the CSV headers will be
            generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);
        }
        else
        {
            string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE", "SQUAT_STATE_MACHINE_STATE", "IS_TRACKER_DATA_FRESH_FLAG",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_0", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_0", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_0",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_1", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_1", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_1",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_2", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_2", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_2",
            "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_0_COL_3", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_3", "TRANSFORMATION_UNITY_TO_FRAME_0_ROW_1_COL_3",
            "LEFT_ANKLE_TRACKER_POS_X_UNITY_FRAME", "LEFT_ANKLE_TRACKER_POS_Y_UNITY_FRAME", "LEFT_ANKLE_TRACKER_POS_Z_UNITY_FRAME",
            "LEFT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_X", "LEFT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Y", "LEFT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "LEFT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_X", "LEFT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "LEFT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "LEFT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_X", "LEFT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "LEFT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "RIGHT_ANKLE_TRACKER_POS_X_UNITY_FRAME", "RIGHT_ANKLE_TRACKER_POS_Y_UNITY_FRAME", "RIGHT_ANKLE_TRACKER_POS_Z_UNITY_FRAME",
            "RIGHT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_X", "RIGHT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Y", "RIGHT_ANKLE_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "RIGHT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_X", "RIGHT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "RIGHT_ANKLE_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "RIGHT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_X", "RIGHT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "RIGHT_ANKLE_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "LEFT_SHANK_TRACKER_TRACKER_POS_X_UNITY_FRAME", "LEFT_SHANK_TRACKER_POS_Y_UNITY_FRAME", "LEFT_SHANK_TRACKER_POS_Z_UNITY_FRAME",
            "LEFT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_X", "LEFT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Y", "LEFT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "LEFT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_X", "LEFT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "LEFT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "LEFT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_X", "LEFT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "LEFT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "RIGHT_SHANK_TRACKER_POS_X_UNITY_FRAME", "RIGHT_SHANK_TRACKER_POS_Y_UNITY_FRAME", "RIGHT_SHANK_TRACKER_POS_Z_UNITY_FRAME",
            "RIGHT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_X", "RIGHT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Y", "RIGHT_SHANK_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "RIGHT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_X", "RIGHT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "RIGHT_SHANK_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "RIGHT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_X", "RIGHT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "RIGHT_SHANK_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "PELVIS_TRACKER_POS_X_UNITY_FRAME", "PELVIS_TRACKER_POS_Y_UNITY_FRAME", "PELVIS_TRACKER_POS_Z_UNITY_FRAME",
            "PELVIS_TRACKER_X_VECTOR_UNITY_FRAME_X", "PELVIS_TRACKER_X_VECTOR_UNITY_FRAME_Y", "PELVIS_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "PELVIS_TRACKER_Y_VECTOR_UNITY_FRAME_X", "PELVIS_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "PELVIS_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "PELVIS_TRACKER_Z_VECTOR_UNITY_FRAME_X", "PELVIS_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "PELVIS_TRACKER_Z_VECTOR_UNITY_FRAME_Z",
            "TRUNK_TRACKER_POS_X_UNITY_FRAME", "TRUNK_TRACKER_POS_Y_UNITY_FRAME", "TRUNK_TRACKER_POS_Z_UNITY_FRAME",
            "TRUNK_TRACKER_X_VECTOR_UNITY_FRAME_X", "TRUNK_TRACKER_X_VECTOR_UNITY_FRAME_Y", "TRUNK_TRACKER_X_VECTOR_UNITY_FRAME_Z",
            "TRUNK_TRACKER_Y_VECTOR_UNITY_FRAME_X", "TRUNK_TRACKER_Y_VECTOR_UNITY_FRAME_Y", "TRUNK_TRACKER_Y_VECTOR_UNITY_FRAME_Z",
            "TRUNK_TRACKER_Z_VECTOR_UNITY_FRAME_X", "TRUNK_TRACKER_Z_VECTOR_UNITY_FRAME_Y", "TRUNK_TRACKER_Z_VECTOR_UNITY_FRAME_Z"
            };

            //tell the data recorder what the CSV headers will be
            generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);
        }

        // 2.) Trial data naming
        // A string array with all of the header names
        string[] csvTrialDataHeaderNames = new string[]
        {
            "TRIAL_NUMBER", "TRIAL_START_TIME_THIS_UNITY_FRAME", "TRIAL_END_TIME_THIS_UNITY_FRAME", "PELVIC_ASSISTANCE_TYPE",
            "SHANK_ASSISTANCE_TYPE", "PELVIC_ASSISTANCE_KNEE_TORQUE_FRACTION", "MAX_THETA_KNEE", "MEAN_THETA_KNEE_HOLD_STATE", "POINTS_EARNED_THIS_TRIAL", 
            "SUBJECT_MASS_KG", "SUBJECT_ANKLE_KNEE_LENGTH_METERS", "PELVIC_ML_WIDTH_METERS", "PELVIC_AP_WIDTH_METERS",
            "CHEST_ML_WIDTH_METERS", "CHEST_AP_WIDTH_METERS"
        };

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
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "SquattingTask" + "/" + dateString + "/";

        //set the frame data and the task-specific trial subdirectory name (will go inside the CSV folder in Assets)
        subdirectoryName = subdirectoryString; //store as an instance variable so that it can be used for the marker and trial data
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvEmgDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvStanceModelDataSubdirectoryName(subdirectoryString);

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
        string fileNameStub = "SquattingTask" + delimiter + subjectSpecificInfoString + delimiter + dateAndTimeString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the task-specific trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);

        // Set the EMG data file name
        string fileNameEmgData = fileNameStub + "_Emg_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvEmgDataFileName(fileNameEmgData);
        
        // Set the stnace model data file name
        string fileNameStanceModelData = fileNameStub + "_StanceModel_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvStanceModelDataFileName(fileNameStanceModelData);
    }


    // BEGIN: Additional Functions *********************************************************************************


    // Testing only function to say the task is over. Tests tension-saving in labview.
    private void SendTaskOverCommandToRobustOnToggle()
    {
        if (sendTaskOverCommandToRobustToggle != sendTaskOverCommandToRobustTogglePrevious)
        {
            forceFieldRobotTcpServerScript.SendCommandWithTaskOverSpecifier();
            sendTaskOverCommandToRobustTogglePrevious = sendTaskOverCommandToRobustToggle;
        }
    }

    public float GetTargetKneeAngleRad() {
        return kneeAngleTargetDeg*Mathf.Deg2Rad;
    }

    public Vector3 GetTrunkCenterPositionInUnityRepresentation()
    {
        return new Vector3(0.0f, 0.0f, 0.0f); // for completeness, the chest is included. But this task has no chest, so return dummy 0s.
    }


    public Vector3 GetPelvisCenterPositionInUnityRepresentation()
    {
        return positions3[2]; // the pelvis is the end of our 2-segment on-screen model
    }

    public Vector3 GetShankCenterPositionInUnityRepresentation()
    {
        return shankBeltAttachmentPointRepresentation; // the shank belt force is rendered a bit below the knee.
    }

    // A task-specific "rotation" from Vicon frame to Unity frame.
    public Vector3 RotateViconFrameVectorToUnityFrame(Vector3 vectorInViconFrame)
    {
        Vector3 vectorInUnityFrame = new Vector3(vectorInViconFrame.x, vectorInViconFrame.z, vectorInViconFrame.y);
        return vectorInUnityFrame;
    }

    //function to collect pelvis positions
    private bool PelvisPosUpdate()
    {
        if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            // Get pelvis position in frame 0
            newPelvisPos = ViveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0();
        }
        else if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            // Get the current pelvis position (sohuld be in frame 0)
            newPelvisPos = centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();
        } 
        
        // Determine if pelvis data is new
        bool pelvisPositionIsNew = false;
        Vector3 previousPelvisPos = pelvisPosHistory[0];
        if (newPelvisPos != previousPelvisPos)
        {
            pelvisPositionIsNew = true;
        }

        // If there is new pelvis position data
        if (pelvisPositionIsNew)
        {
            // Then update the pelvis position history array
            for (int i = pelvisPosHistory.Length - 1; i > 0; i--)
            { //shift all values to the 1+i index
                pelvisPosHistory[i] = pelvisPosHistory[i - 1];
                pelvisPosHistoryTimeStamps[i] = pelvisPosHistoryTimeStamps[i - 1];
            }
            pelvisPosHistory[0] = newPelvisPos; // Store the newest pelvis position in index 0

            // Store the time stamp at which the pelvis position was stored
            pelvisPosHistoryTimeStamps[0] = Time.time;
        }

        // Return if pelvis position was new
        return pelvisPositionIsNew;
    }

    //function to get velocity of pelvis
    private void PelvisVelocity()
    {

        // Initialize means at 0
        velocityXMean = 0;
        velocityYMean = 0;
        velocityZMean = 0;

        // For each entry in the pelvis position history array
        for (int i = 0; i < pelvisPosHistory.Length - 1; i++){
            float deltaTime = pelvisPosHistoryTimeStamps[i] - pelvisPosHistoryTimeStamps[i + 1];
            float instVelocityX = ((pelvisPosHistory[i].x) - (pelvisPosHistory[i+1].x))/ deltaTime;
            float instVelocityY = ((pelvisPosHistory[i].y) - (pelvisPosHistory[i+1].y))/ deltaTime;
            float instVelocityZ = ((pelvisPosHistory[i].z) - (pelvisPosHistory[i+1].z))/ deltaTime;
            pelvisVelocityHistory[i] = new Vector3(instVelocityX, instVelocityY, instVelocityZ);

            // Add instantaneous velocities to sum
            velocityXMean += instVelocityX;
            velocityYMean += instVelocityY;
            velocityZMean += instVelocityZ; 
        }

        // Take the mean of the instantaneous velocities by dividing the sum by the # of observations
        velocityXMean = velocityXMean/ pelvisVelocityHistory.Length;
        velocityYMean = velocityYMean/ pelvisVelocityHistory.Length;
        velocityZMean = velocityZMean/ pelvisVelocityHistory.Length;
    }

    //Point allocation
    private float PointFeedback(float meanKneeAngleInHold)
    {
        Debug.Log("Assigning points: knee angle is: " + meanKneeAngleInHold + " and target is: " + kneeAngleTargetDeg);
        float errorInDegrees = Mathf.Abs(meanKneeAngleInHold - kneeAngleTargetDeg);

        float pointsWindowPositiveErrorInDeg = 5.0f; // too deep limit
        float pointsWindowNegativeErrorInDeg = 15.0f; // too shallow limit

        float upperLimitForPointsInDeg = kneeAngleTargetDeg + pointsWindowPositiveErrorInDeg;
        float lowerLimitForPointsInDeg = kneeAngleTargetDeg - pointsWindowNegativeErrorInDeg;

        pointsEarnedThisTrial = 0.0f;
        if (meanKneeAngleInHold > upperLimitForPointsInDeg || meanKneeAngleInHold < lowerLimitForPointsInDeg)
        {
            pointsEarnedThisTrial = 0;
        } else {
            if(meanKneeAngleInHold > kneeAngleTargetDeg)
            {
                pointsEarnedThisTrial = 100.0f - 100.0f * (errorInDegrees / pointsWindowPositiveErrorInDeg);
            }
            else
            {
                pointsEarnedThisTrial = 100.0f - 100.0f * (errorInDegrees / pointsWindowNegativeErrorInDeg);
            }
        }

        return pointsEarnedThisTrial;
    }

    private string GetSquatDepthFeedback(float kneeAngleDeg)
    {
        string squatDepthFeedback;
        if (kneeAngleDeg > kneeAngleTargetDeg){
            squatDepthFeedback = "Squat was deeper than target angle.";
        }
        else{
            squatDepthFeedback = "Squat was shallower than target angle.";
        }

        return squatDepthFeedback;
    }


    private float GetCurrentKneeAngleToRender()
    {
        float kneeAngle = new float();
        // For now, we use the left knee angle as the angle to render
        if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            kneeAngle =  centerOfMassManagerScript.GetLeftKneeAngleInDegrees();
        }
        else if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            
            double[] jointAngles = stanceModel.GetJointVariableValuesFromInverseKinematicsVirtualPelvicTracker(); 
            kneeAngle = (float) (jointAngles[2] * (180/System.Math.PI));
        }
        return kneeAngle;
    }

    public void SetCurrentAngles(float ankleAngleDeg, float kneeAngleDeg)
    {
        //ankle
        float ankleAngleRad = ankleAngleDeg*Mathf.Deg2Rad;
        float renderedShankLength = 1.5f;
        Vector3 ankleToKnee = new Vector3(0f,Mathf.Sin(ankleAngleRad)* renderedShankLength, Mathf.Cos(ankleAngleRad)* renderedShankLength);

        //knee
        float kneeAngleRad = kneeAngleDeg*Mathf.Deg2Rad;
        float renderedThighLength = 2.0f;
        Vector3 kneeToHip = new Vector3(0f,Mathf.Sin(kneeAngleRad)* renderedThighLength, Mathf.Cos(kneeAngleRad)* renderedThighLength);

        SetPositions(ankleToKnee, kneeToHip);
    }

    private void SetPositions(Vector3 ankleToKnee, Vector3 kneeToHip)
    {
        positions3[0] = new Vector3(0f,0f,0f); //ankle point
        positions3[1] = ankleToKnee; //knee point
        positions3[2] = ankleToKnee + kneeToHip; //hip point
        shankBeltAttachmentPointRepresentation = 0.8f * ankleToKnee; // a representation for where the shank force is applied.
                                                                     // Not related to actual shank belt center pos.
        leg.positionCount = positions3.Length; 
        leg.SetPositions(positions3);

        //y element should b cos, z as sin
    }

    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if(newState != currentState)
        {
            Debug.Log("Transitioning states from " + currentState + " to " + newState);
            // Call exit functions
            if(currentState == waitingForEmgReadyStateString)
            {
                exitWaitingForEmgReadyState();
            }
            if(currentState == waitingForSetupStateString)
            {
                exitWaitingForSetupState();
            }
            if(currentState == waitingForHomePosStateString)
            {
                exitWaitingForHomePosState();
            }
            if(currentState == preSquatStateString)
            {
                exitPreSquatState();
            }
            else if (currentState == squatDescentString)
            {
                exitSquatDescentState();
            }
            else if (currentState == squatHoldString)
            {
                exitSquatHoldState();
            }
            else if (currentState == squatAscentString)
            {
                exitSquatAscentState();
            }
            else if(currentState == feedbackString)
            {
                exitFedbackState();
            }
            else if(currentState == gameOverStateString)
            {
                exitGameOverState();
            }

            //then call the entry function for the new state
            if(newState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            if(newState == waitingForSetupStateString)
            {
                enterWaitingForSetupState();
            }
            if(newState == waitingForHomePosStateString)
            {
                enterWaitingForHomePosState();
            }
            else if(newState == preSquatStateString)
            {
                enterPreSquatState();
            }
            else if (newState == squatDescentString)
            {
                enterSquatDescentState();
            }
            else if (newState == squatHoldString)
            {
                enterSquatHoldState();
            }
            else if (newState == squatAscentString)
            {
                enterSquatAscentState();
            }
            else if(newState == feedbackString)
            {
                enterFedbackState();
            }
            else if(newState == gameOverStateString)
            {
                enterGameOverState();
            }
        }

    }


    private void enterWaitingForEmgReadyState()
    {
        currentState = waitingForEmgReadyStateString;
    }
    private void exitWaitingForEmgReadyState()
    {
        // Pulse the photon start sync pin to
        // sync the external hardware
        // (if we're syncing with external hardware (e.g. EMGs))
        if (syncingWithExternalHardwareFlag == true)
        {

            communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

            // Store the new time at which we sent the Photon start sync 
            unityFrameTimeAtWhichHardwareSyncSent = Time.time;
        }
    }


    private void enterWaitingForSetupState()
    {
        currentState = waitingForSetupStateString;
    }
    private void exitWaitingForSetupState()
    {
        // do nothing for now
    }

    private void enterWaitingForHomePosState()
    {
        currentState = waitingForHomePosStateString;
        stopwatch.Reset(); //reset timer to be started again wtihin switch statement
        textElement.text = "Please stand upright with your knees straight";

        // Reset the variables that track max and mean knee angle during the SquatHold state
        meanKneeAngle = 0.0f;
        maxKneeAngleThisTrial = 0.0f;

        // Entering the waiting for home pos state marks the start of a trial! 
        // Note the time this trial started
        mostRecentTrialStartTime = Time.time;
    }
    private void exitWaitingForHomePosState()
    {
        // do nothing for now
    }

    private void enterPreSquatState()
    {
        currentState = preSquatStateString;
        // On first trial, subject should wait for go signal
        if(trialCounter == 1)
        {
            textElement.text = "Please wait for a GO from us to squat to the white line."; 
        }
        else
        {
            textElement.text = "Please squat to the white line when ready"; 
        }

    }
    private void exitPreSquatState()
    {
        // do nothing for now
    }

    private void enterSquatDescentState()
    {
        currentState = squatDescentString; 
        //store length of squat in instance variable start and stop time in squat descent/ascent
        stopwatch.Reset(); //reset timer to be started again within switch statement
    }
    private void exitSquatDescentState()
    {
        // do nothing for now
    }

    private void enterSquatHoldState()
    {
        currentState = squatHoldString;
        stopwatch.Restart(); //restart timer to be used within state
        textElement.text = "Please hold squat for 3 seconds";
        //enterSquatHoldKneeAngle = //edit this 
    }
    private void exitSquatHoldState()
    {
        //do nothing for now
    }

    private void enterSquatAscentState()
    {
        currentState = squatAscentString;
    }
    private void exitSquatAscentState()
    {
        // do nothing for now
    }

    private void enterFedbackState()
    {
        currentState = feedbackString;
        stopwatch.Restart(); //restart timer to be used within state

        string squatDepthFeedback = GetSquatDepthFeedback(meanKneeAngle);
        count = 0; //reset counter for mean knee angle

        float pointsEarned = PointFeedback(meanKneeAngle);
        totalPointsEarned += pointsEarned; 
        string pointsString = squatDepthFeedback + "\n" + "+ " + pointsEarned.ToString("F1") + " points!";
        string totalPointsString = "Total points: " + totalPointsEarned.ToString("F1");
        textElement.text = pointsString;
        totalPointsTextElement.text = totalPointsString;
        //Debug.Log("You have completed " + trialCounter + " out of " + totalTrials " trials.");
    }
    private void exitFedbackState()
    {
        // Leaving the feedback state marks the end of a trial! 
        // Note the time this trial ended
        mostRecentTrialEndTime = Time.time;
        
        // Store the trial data for this trial
        StoreRowOfTrialData();

        // Increment the trial counter
        trialCounter++;
    }

    private void enterGameOverState()
    {
        currentState = gameOverStateString;
        textElement.text = "You're done!";

        // If using EMGs
        if (syncingWithExternalHardwareFlag == true)
        {
            // Mark the end of data for the EMG base station
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();

        }


        // Tell the general data recorder to write all of the stored data to file
        generalDataRecorderScript.writeTrialDataToFile();
        generalDataRecorderScript.writeFrameDataToFile();
        generalDataRecorderScript.writeStanceModelDataToFile();

        // If using EMG data, write to file at game end
        if (streamingEmgDataFlag == true)
        {
            Debug.Log("Writing EMG data with " + generalDataRecorderScript.GetNumberOfEmgDataRowsStored() + " rows of data to file");
            generalDataRecorderScript.writeEmgDataToFile();
        }

        // Quit
        Application.Quit(); //terminates game
    }
    private void exitGameOverState()
    {
        // do nothing for now
    }


    // END: State machine state-transitioning functions *********************************************************************************
    
    
    // START: Data storage functions *********************************************************************************

    private void StoreRowOfTrialData()
    {
        /*string[] csvTrialDataHeaderNames = new string[]
        {
            "TRIAL_NUMBER", "TRIAL_START_TIME_THIS_UNITY_FRAME", "TRIAL_END_TIME_THIS_UNITY_FRAME", "ROBUST_ASSISTANCE_TYPE", 
            "MAX_THETA_KNEE", "MEAN_THETA_KNEE_HOLD_STATE", "POINTS_EARNED_THIS_TRIAL", "SUBJECT_MASS_KG", "SUBJECT_ANKLE_KNEE_LENGTH_METERS", "PELVIC_ML_WIDTH_METERS", "PELVIC_AP_WIDTH_METERS",
            "CHEST_ML_WIDTH_METERS", "CHEST_AP_WIDTH_METERS"
        };*/
        
        // The list that will store the row of data
        List<float> trialDataToStore = new List<float>();
        
        // Store trial number
        trialDataToStore.Add(trialCounter);
        // Store trial start time
        trialDataToStore.Add(mostRecentTrialStartTime);
        // Store trial end time
        trialDataToStore.Add(mostRecentTrialEndTime);
        // Store RobUST pelvic assistance type for this block
        trialDataToStore.Add((float) forceFieldHighLevelControllerScript.GetPelvicBeltAssistanceType());
        // Store RobUST shank assistance type for this block
        trialDataToStore.Add((float)forceFieldHighLevelControllerScript.GetShankBeltAssistanceType());
        // Store RobUST pelvic assistance fraction for knee torque (inside boundary value)
        trialDataToStore.Add(forceFieldHighLevelControllerScript.GetPelvicBeltGravCompensationFractionInsideBoundary());
        // Store trial max theta knee (degrees)
        trialDataToStore.Add(maxKneeAngleThisTrial); // SHORT ON TIME - ARBITRARYILY STORE 0 FOR NOW!
        // Store trial mean theta knee during squat hold (degrees)
        trialDataToStore.Add(meanKneeAngle); // SHORT ON TIME - ARBITRARYILY STORE 0 FOR NOW!
        // Store points earned
        trialDataToStore.Add(pointsEarnedThisTrial);
        // Subject mass kg
        trialDataToStore.Add(subjectSpecificDataScript.getSubjectMassInKilograms());
        // Subject distance ankle to knee in meters
        trialDataToStore.Add(subjectSpecificDataScript.GetDistanceAnkleToKneeInMeters());
        // Subject pelvic mediolateral and anteroposterior pelvic measurements in meters
        (float pelvisMlWidthMeters, float pelvisApWidthMeters) =
            subjectSpecificDataScript.GetPelvisEllipseMediolateralAndAnteroposteriorLengths();
        trialDataToStore.Add(pelvisMlWidthMeters);
        trialDataToStore.Add(pelvisApWidthMeters);
        // Subject chest mediolateral and anteroposterior pelvic measurements in meters
        (float chestMlWidthMeters, float chestApWidthMeters) =
            subjectSpecificDataScript.GetChestEllipseMediolateralAndAnteroposteriorLengths();
        trialDataToStore.Add(chestMlWidthMeters);
        trialDataToStore.Add(chestApWidthMeters);
        
        // Send the row of trial data to the data recorder
        generalDataRecorderScript.storeRowOfTrialData(trialDataToStore.ToArray());
    }

    public void StoreRowOfFrameData()
    {
        /*        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
                    "SYNC_PIN_ANALOG_VOLTAGE", "SQUAT_STATE_MACHINE_STATE",

                        

                    "LEFT_ANKLE_TRACKER_POS_X_FRAME_0", "LEFT_ANKLE_TRACKER_POS_Y_FRAME_0", "LEFT_ANKLE_TRACKER_POS_Z_FRAME_0",
                    "LEFT_ANKLE_TRACKER_X_VECTOR_FRAME_0_X", "LEFT_ANKLE_TRACKER_X_VECTOR_FRAME_0_Y", "LEFT_ANKLE_TRACKER_X_VECTOR_FRAME_0_Z",
                    "LEFT_ANKLE_TRACKER_Y_VECTOR_FRAME_0_X", "LEFT_ANKLE_TRACKER_Y_VECTOR_FRAME_0_Y", "LEFT_ANKLE_TRACKER_Y_VECTOR_FRAME_0_Z",
                    "LEFT_ANKLE_TRACKER_Z_VECTOR_FRAME_0_X", "LEFT_ANKLE_TRACKER_Z_VECTOR_FRAME_0_Y", "LEFT_ANKLE_TRACKER_Z_VECTOR_FRAME_0_Z",

                    "RIGHT_ANKLE_TRACKER_POS_X_FRAME_0", "RIGHT_ANKLE_TRACKER_POS_Y_FRAME_0", "RIGHT_ANKLE_TRACKER_POS_Z_FRAME_0",
                    "RIGHT_ANKLE_TRACKER_X_VECTOR_FRAME_0_X", "RIGHT_ANKLE_TRACKER_X_VECTOR_FRAME_0_Y", "RIGHT_ANKLE_TRACKER_X_VECTOR_FRAME_0_Z",
                    "RIGHT_ANKLE_TRACKER_Y_VECTOR_FRAME_0_X", "RIGHT_ANKLE_TRACKER_Y_VECTOR_FRAME_0_Y", "RIGHT_ANKLE_TRACKER_Y_VECTOR_FRAME_0_Z",
                    "RIGHT_ANKLE_TRACKER_Z_VECTOR_FRAME_0_X", "RIGHT_ANKLE_TRACKER_Z_VECTOR_FRAME_0_Y", "RIGHT_ANKLE_TRACKER_Z_VECTOR_FRAME_0_Z",

                    "LEFT_SHANK_TRACKER_TRACKER_POS_X_FRAME_0", "LEFT_SHANK_TRACKER_POS_Y_FRAME_0", "LEFT_SHANK_TRACKER_POS_Z_FRAME_0",
                    "LEFT_SHANK_TRACKER_X_VECTOR_FRAME_0_X", "LEFT_SHANK_TRACKER_X_VECTOR_FRAME_0_Y", "LEFT_SHANK_TRACKER_X_VECTOR_FRAME_0_Z",
                    "LEFT_SHANK_TRACKER_Y_VECTOR_FRAME_0_X", "LEFT_SHANK_TRACKER_Y_VECTOR_FRAME_0_Y", "LEFT_SHANK_TRACKER_Y_VECTOR_FRAME_0_Z",
                    "LEFT_SHANK_TRACKER_Z_VECTOR_FRAME_0_X", "LEFT_SHANK_TRACKER_Z_VECTOR_FRAME_0_Y", "LEFT_SHANK_TRACKER_Z_VECTOR_FRAME_0_Z",

                    "RIGHT_SHANK_TRACKER_POS_X_FRAME_0", "RIGHT_SHANK_TRACKER_POS_Y_FRAME_0", "RIGHT_SHANK_TRACKER_POS_Z_FRAME_0",
                    "RIGHT_SHANK_TRACKER_X_VECTOR_FRAME_0_X", "RIGHT_SHANK_TRACKER_X_VECTOR_FRAME_0_Y", "RIGHT_SHANK_TRACKER_X_VECTOR_FRAME_0_Z",
                    "RIGHT_SHANK_TRACKER_Y_VECTOR_FRAME_0_X", "RIGHT_SHANK_TRACKER_Y_VECTOR_FRAME_0_Y", "RIGHT_SHANK_TRACKER_Y_VECTOR_FRAME_0_Z",
                    "RIGHT_SHANK_TRACKER_Z_VECTOR_FRAME_0_X", "RIGHT_SHANK_TRACKER_Z_VECTOR_FRAME_0_Y", "RIGHT_SHANK_TRACKER_Z_VECTOR_FRAME_0_Z",

                    "PELVIS_TRACKER_POS_X_FRAME_0", "PELVIS_TRACKER_POS_Y_FRAME_0", "PELVIS_TRACKER_POS_Z_FRAME_0",
                    "PELVIS_TRACKER_X_VECTOR_FRAME_0_X", "PELVIS_TRACKER_X_VECTOR_FRAME_0_Y", "PELVIS_TRACKER_X_VECTOR_FRAME_0_Z",
                    "PELVIS_TRACKER_Y_VECTOR_FRAME_0_X", "PELVIS_TRACKER_Y_VECTOR_FRAME_0_Y", "PELVIS_TRACKER_Y_VECTOR_FRAME_0_Z",
                    "PELVIS_TRACKER_Z_VECTOR_FRAME_0_X", "PELVIS_TRACKER_Z_VECTOR_FRAME_0_Y", "PELVIS_TRACKER_Z_VECTOR_FRAME_0_Z",

                    "TRUNK_TRACKER_POS_X_FRAME_0", "TRUNK_TRACKER_POS_Y_FRAME_0", "TRUNK_TRACKER_POS_Z_FRAME_0",
                    "TRUNK_TRACKER_X_VECTOR_FRAME_0_X", "TRUNK_TRACKER_X_VECTOR_FRAME_0_Y", "TRUNK_TRACKER_X_VECTOR_FRAME_0_Z",
                    "TRUNK_TRACKER_Y_VECTOR_FRAME_0_X", "TRUNK_TRACKER_Y_VECTOR_FRAME_0_Y", "TRUNK_TRACKER_Y_VECTOR_FRAME_0_Z",
                    "TRUNK_TRACKER_Z_VECTOR_FRAME_0_X", "TRUNK_TRACKER_Z_VECTOR_FRAME_0_Y", "TRUNK_TRACKER_Z_VECTOR_FRAME_0_Z"
                };*/


        // The list that will store the row of data
        List<float> frameDataToStore = new List<float>();

        // Store time of current Unity Update() loop start (frame start)
        frameDataToStore.Add(Time.time);

        // Store time at which the Photon sent the hardware sync signal.
        frameDataToStore.Add(unityFrameTimeAtWhichHardwareSyncSent);

        // The analog sync pin voltage (high = EMG streaming, low = EMG stopped)
        float analogSyncPinVoltage = scriptToRetrieveForcePlateData.GetMostRecentSyncPinVoltageValue();
        frameDataToStore.Add(analogSyncPinVoltage);

        // Store the state of the squatting level manager state machine
        if (currentState == waitingForEmgReadyStateString)
        {
            // EmgReadyState = 0
            frameDataToStore.Add(0.0f);
        } 
        else if (currentState == waitingForSetupStateString)
        {
            // SetupState = 1
            frameDataToStore.Add(1.0f);
        }
        else if (currentState == waitingForHomePosStateString)
        {
            // HomePosState = 2
            frameDataToStore.Add(2.0f);
        }
        else if (currentState == preSquatStateString)
        {
            // preSquatState = 3
            frameDataToStore.Add(3.0f);
        }
        else if (currentState == squatDescentString)
        {
            // squatDescent = 4
            frameDataToStore.Add(4.0f);
        }
        else if (currentState == squatHoldString)
        {
            // squatHold = 5
            frameDataToStore.Add(5.0f);
        }
        else if (currentState == squatAscentString)
        {
            // squatAscent = 6
            frameDataToStore.Add(6.0f);
        }
        else if (currentState == feedbackString)
        {
            // feedback = 7
            frameDataToStore.Add(7.0f);
        }
        else if (currentState == gameOverStateString)
        {
            // gameOverState = 8
            frameDataToStore.Add(8.0f);
        }


        // Store the CURRENT transformation matrix from Unity to Frame 0. 
        // As this value can change (e.g., if person takes a small step or slides a foot), 
        // we want to store Vive tracker in the Unity frame, which is fixed in space.
        Matrix4x4 transformationMatrixFromFrame0ToUnity = ViveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
        Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnity.inverse;
        // Store first column of rotation matrix
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[0, 0]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[1, 0]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[2, 0]);
        // Store second column of rotation matrix
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[0, 1]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[1, 1]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[2, 1]);
        // Store third column of rotation matrix
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[0, 2]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[1, 2]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[2, 2]);
        // Store the translation column of the transformation matrix
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[0, 3]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[1, 3]);
        frameDataToStore.Add(transformationMatrixFromUnityToFrame0[2, 3]);

        // Retrieve and store all Vive tracker data in Unity frame
        (Vector3 leftAnkleViveTrackerInUnityFrame, Vector3 rightAnkleViveTrackerInUnityFrame, Vector3 leftShankViveTrackerInUnityFrame, 
            Vector3 rightShankViveTrackerInUnityFrame, Vector3 pelvicViveTrackerInUnityFrame, Vector3 chestViveTrackerInUnityFrame) =
            ViveTrackerDataManagerScript.GetAllTrackerPositionInUnityFrame();
        List<Vector3> allTrackerDirections = ViveTrackerDataManagerScript.GetAllTrackerOrientationsInUnityFrame();

        // Left ankle Vive tracker data
        frameDataToStore.Add(leftAnkleViveTrackerInUnityFrame.x);
        frameDataToStore.Add(leftAnkleViveTrackerInUnityFrame.y);
        frameDataToStore.Add(leftAnkleViveTrackerInUnityFrame.z);
        frameDataToStore.Add(allTrackerDirections[0].x);
        frameDataToStore.Add(allTrackerDirections[0].y);
        frameDataToStore.Add(allTrackerDirections[0].z);
        frameDataToStore.Add(allTrackerDirections[1].x);
        frameDataToStore.Add(allTrackerDirections[1].y);
        frameDataToStore.Add(allTrackerDirections[1].z);
        frameDataToStore.Add(allTrackerDirections[2].x);
        frameDataToStore.Add(allTrackerDirections[2].y);
        frameDataToStore.Add(allTrackerDirections[2].z);
        
        // Right ankle Vive tracker data
        frameDataToStore.Add(rightAnkleViveTrackerInUnityFrame.x);
        frameDataToStore.Add(rightAnkleViveTrackerInUnityFrame.y);
        frameDataToStore.Add(rightAnkleViveTrackerInUnityFrame.z);
        frameDataToStore.Add(allTrackerDirections[3].x);
        frameDataToStore.Add(allTrackerDirections[3].y);
        frameDataToStore.Add(allTrackerDirections[3].z);
        frameDataToStore.Add(allTrackerDirections[4].x);
        frameDataToStore.Add(allTrackerDirections[4].y);
        frameDataToStore.Add(allTrackerDirections[4].z);
        frameDataToStore.Add(allTrackerDirections[5].x);
        frameDataToStore.Add(allTrackerDirections[5].y);
        frameDataToStore.Add(allTrackerDirections[5].z);
        
        // Left shank Vive tracker data
        frameDataToStore.Add(leftShankViveTrackerInUnityFrame.x);
        frameDataToStore.Add(leftShankViveTrackerInUnityFrame.y);
        frameDataToStore.Add(leftShankViveTrackerInUnityFrame.z);
        frameDataToStore.Add(allTrackerDirections[6].x);
        frameDataToStore.Add(allTrackerDirections[6].y);
        frameDataToStore.Add(allTrackerDirections[6].z);
        frameDataToStore.Add(allTrackerDirections[7].x);
        frameDataToStore.Add(allTrackerDirections[7].y);
        frameDataToStore.Add(allTrackerDirections[7].z);
        frameDataToStore.Add(allTrackerDirections[8].x);
        frameDataToStore.Add(allTrackerDirections[8].y);
        frameDataToStore.Add(allTrackerDirections[8].z);

        // Right shank Vive tracker data
        frameDataToStore.Add(rightShankViveTrackerInUnityFrame.x);
        frameDataToStore.Add(rightShankViveTrackerInUnityFrame.y);
        frameDataToStore.Add(rightShankViveTrackerInUnityFrame.z);
        frameDataToStore.Add(allTrackerDirections[9].x);
        frameDataToStore.Add(allTrackerDirections[9].y);
        frameDataToStore.Add(allTrackerDirections[9].z);
        frameDataToStore.Add(allTrackerDirections[10].x);
        frameDataToStore.Add(allTrackerDirections[10].y);
        frameDataToStore.Add(allTrackerDirections[10].z);
        frameDataToStore.Add(allTrackerDirections[11].x);
        frameDataToStore.Add(allTrackerDirections[11].y);
        frameDataToStore.Add(allTrackerDirections[11].z);

        // Pelvic Vive tracker data
        frameDataToStore.Add(pelvicViveTrackerInUnityFrame.x);
        frameDataToStore.Add(pelvicViveTrackerInUnityFrame.y);
        frameDataToStore.Add(pelvicViveTrackerInUnityFrame.z);
        frameDataToStore.Add(allTrackerDirections[12].x);
        frameDataToStore.Add(allTrackerDirections[12].y);
        frameDataToStore.Add(allTrackerDirections[12].z);
        frameDataToStore.Add(allTrackerDirections[13].x);
        frameDataToStore.Add(allTrackerDirections[13].y);
        frameDataToStore.Add(allTrackerDirections[13].z);
        frameDataToStore.Add(allTrackerDirections[14].x);
        frameDataToStore.Add(allTrackerDirections[14].y);
        frameDataToStore.Add(allTrackerDirections[14].z);
        
        // Chest Vive tracker data
        frameDataToStore.Add(chestViveTrackerInUnityFrame.x);
        frameDataToStore.Add(chestViveTrackerInUnityFrame.y);
        frameDataToStore.Add(chestViveTrackerInUnityFrame.z);
        frameDataToStore.Add(allTrackerDirections[15].x);
        frameDataToStore.Add(allTrackerDirections[15].y);
        frameDataToStore.Add(allTrackerDirections[15].z);
        frameDataToStore.Add(allTrackerDirections[16].x);
        frameDataToStore.Add(allTrackerDirections[16].y);
        frameDataToStore.Add(allTrackerDirections[16].z);
        frameDataToStore.Add(allTrackerDirections[17].x);
        frameDataToStore.Add(allTrackerDirections[17].y);
        frameDataToStore.Add(allTrackerDirections[17].z);

        // If we're using Vicon devices (e.g., force plates)
        if(storeForcePlateAndViconDeviceData == true)
        {
            // EXTRA : we always get the COP x,y position as a rough estimate of the COM ground-plane projection. Possible (but bad) failsafe.
            if (!isForcePlateDataReadyForAccess) // if the force plate data is not ready for access
            {
                isForcePlateDataReadyForAccess = scriptToRetrieveForcePlateData.getForcePlateDataAvailableViaDataStreamStatus();
            }
            else // if the force plate data is ready for access
            {
                // Get the COP 
                CopPositionViconFrame = scriptToRetrieveForcePlateData.getMostRecentCenterOfPressureInViconFrame();
                // Get the force plate forces
                allForcePlateForces = scriptToRetrieveForcePlateData.getAllForcePlateForces();
                // Get the force plate torques
                allForcePlateTorques = scriptToRetrieveForcePlateData.GetAllForcePlateTorques();
            }

            // Store the first force plate forces and torques
            if (allForcePlateForces != null) // If the force plates have been initialized
            {
                if (allForcePlateForces.Length >= 1 && allForcePlateTorques.Length >= 1)
                {
                    frameDataToStore.Add(allForcePlateForces[0].x);
                    frameDataToStore.Add(allForcePlateForces[0].y);
                    frameDataToStore.Add(allForcePlateForces[0].z);
                    frameDataToStore.Add(allForcePlateTorques[0].x);
                    frameDataToStore.Add(allForcePlateTorques[0].y);
                    frameDataToStore.Add(allForcePlateTorques[0].z);
                }
                else
                {
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                }

                // Store the second force plate forces and torques
                if (allForcePlateForces.Length >= 2 && allForcePlateTorques.Length >= 2)
                {
                    frameDataToStore.Add(allForcePlateForces[1].x);
                    frameDataToStore.Add(allForcePlateForces[1].y);
                    frameDataToStore.Add(allForcePlateForces[1].z);
                    frameDataToStore.Add(allForcePlateTorques[1].x);
                    frameDataToStore.Add(allForcePlateTorques[1].y);
                    frameDataToStore.Add(allForcePlateTorques[1].z);
                }
                else
                {
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                    frameDataToStore.Add(0.0f);
                }

                // COP position (defaults to zeros)
                frameDataToStore.Add(CopPositionViconFrame.x);
                frameDataToStore.Add(CopPositionViconFrame.y);
                frameDataToStore.Add(CopPositionViconFrame.z);
            }
            else
            {
                // Add 15 0s, 6 F/T componenets per plate plus the COP position
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
                frameDataToStore.Add(0.0f);
            }
        }

        // Send the frame data to the general data recorder to be stored in RAM
        generalDataRecorderScript.storeRowOfFrameData(frameDataToStore);
    }
    
    
    // END: Data storage functions *********************************************************************************

}