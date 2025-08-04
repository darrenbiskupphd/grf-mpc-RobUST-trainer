using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below
using Debug = UnityEngine.Debug;

public class CollectNeutralPoseLevelManager : LevelManagerScriptAbstractClass
{
    // Toggle to start the recording
    public bool requireButtonPressToStartRecordingFlag;
    public bool toggleToStartRecording = false;
    private bool toggleToStartRecordingLastValue;
    private bool startNeutralPoseRecordingFlag = false; // a bool that controls whether we can start to record the neutral pose (true; after button press only) or not (false).

    // Whether or not to wait until the HMD is toggled to the home position before displaying
    // instructions and starting the instructions timer. 
    public bool waitForToggleHomeToStartInstructions; // true is preferred. 

    // General data recorder
    public GeneralDataRecorder generalDataRecorderScript;

    // Data saving
    private const string thisTaskNameString = "NeutralPose";
    private string subdirectoryName; // the folder name where all data will be saved.
    private string mostRecentFileNameStub; // the "stub" for the current run (e.g. Subject number, start time, task name - does not include type of data, e.g. trial)

    // The path to the folder containing the transformation matrix from Vicon frame to reference tracker frame.
    // Not used?
    private string subdirectoryViconTrackerTransformString; // the path to the folder containing the transformation matrix from Vicon frame to reference tracker frame.

    // Subject-specific info
    public SubjectInfoStorageScript subjectSpecificDataScript;

    //stimulation status
    private string currentStimulationStatus; //the current stimulation status for this block
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // State machine states
    private string currentState;
    private const string setupStateString = "SETUP";
    private const string instructionsStateString = "INSTRUCTIONS";
    private const string recordNeutralPoseStateString = "RECORD_NEUTRAL_POSE";
    private const string gameOverStateString = "GAME_OVER";

    // Instructions for holding still string
    private const string holdNeutralInstructionsString = "Please stand comfortably, \n look at the bullseye, \n and stand very still.";

    // State transition timer
    private Stopwatch stateTransitionStopwatch = new Stopwatch();

    // State transition times
    private const float timeForReadingInstructionsInMs = 3000.0f; //10000.0f;
    private const float timeToRecordNeutralPoseInMs = 5000.0f; //30000.0f;

    // The start time of the current "trial" = single trial of the neutral recording
    private float currentTrialStartTime;
    private float currentTrialEndTime;

    // Startup support script
    public LevelManagerStartupSupportScript levelManagerStartupSupportScript;
    private bool startupSupportScriptCompleteFlag = false;

    // Vive tracker data manager
    public ViveTrackerDataManager viveTrackerDataManagerScript;
    private bool viveTrackerDataInitializedFlag = false;

    // Player/HMD positioning at startup
    public MovePlayerToPositionOnStartup playerRepositioningScript;
    private bool playerToggledHomeFlag = false;

    // Photon communication (for hardware sync)
    public CommunicateWithPhotonViaSerial communicateWithPhotonScript;
    public bool syncingWithExternalHardwareFlag; // Whether or not we're using the System Sync object to sync with EMGs and Vicon nexus
    private float unityFrameTimeAtWhichHardwareSyncSent = 0.0f; // The Time.time of the Update() frame when the hardware sync signal command was sent.

    // Vive tracker game objects
    public GameObject LeftHand;
    public GameObject RightHand;
    public GameObject WaistTracker;
    public GameObject ChestTracker;
    public GameObject LeftAnkleTracker;
    public GameObject RightAnkleTracker;
    public GameObject LeftShankTracker;
    public GameObject RightShankTracker;
    public GameObject RefTracker; // Ref tracker mounted to RobUST's frame
    public GameObject headsetCameraGameObject;

    // Instructions text 
    public Text instructionsText;

    // The file name of the median postures file we're trying to create
    private const string medianPostureFileName = "medianPostures.csv";

    // Start is called before the first frame update
    void Start()
    {
        // Store the initial value of the toggle to start recording button.
        toggleToStartRecordingLastValue = toggleToStartRecording;

        // Set the file paths, file names, and column headers for the frame and trial data
        SetFrameAndTrialDataNaming();

        // Get the stimulation status from the subject-specific info object
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();

        // We always start in the setup state
        currentState = setupStateString;

        // Clear the instructions text 
        instructionsText.text = "";


    }

    // Update is called once per frame
    void Update()
    {
        // Now depending on the current state
        if (currentState == setupStateString)
        {

            // See if the level manager startup support script has finished (initialized EMG if needed, told other files to be loaded, etc). 

            if (startupSupportScriptCompleteFlag == false)
            {
                
                startupSupportScriptCompleteFlag = levelManagerStartupSupportScript.GetServicesStartupCompleteStatusFlag();

                if(startupSupportScriptCompleteFlag == true)
                {
                    Debug.Log("Startup: Startup support script finished!");
                }
            }

            // See if the Vive tracker data manager is ready to serve data
            if (viveTrackerDataInitializedFlag == false)
            {
                viveTrackerDataInitializedFlag = viveTrackerDataManagerScript.GetViveTrackerDataHasBeenInitializedFlag();
                if(viveTrackerDataInitializedFlag == true)
                {
                    Debug.Log("Startup: Vive tracker data accessible by level manager!");
                }
            }

            // See if the player has been toggled home
            if (playerToggledHomeFlag == false)
            {      
                playerToggledHomeFlag = playerRepositioningScript.GetToggleHmdStatus();
                if(playerToggledHomeFlag == true)
                {
                    Debug.Log("Startup: Player toggled home!");
                }
            }

            bool systemsReadyFlag = startupSupportScriptCompleteFlag && viveTrackerDataInitializedFlag && playerToggledHomeFlag;


            // If the needed systems have been set up.
            if (systemsReadyFlag == true)
            {
                // Send the start sync signal via the Photon, if using a sync signal
                if (syncingWithExternalHardwareFlag)
                {
                    communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

                    // Store the hardware sync signal sent time
                    unityFrameTimeAtWhichHardwareSyncSent = Time.time;
                }

                // Change to the instructions state
                ChangeActiveState(instructionsStateString);

            }
        }
        // If we're currently just displaying instructions to the user
        else if (currentState == instructionsStateString)
        {
            storeFrameData();

            // If we're waiting for the HMD toggle home to start the instructions timer (and so the timer is not running)
            if(waitForToggleHomeToStartInstructions == true && stateTransitionStopwatch.IsRunning == false)
            {
                // If the HMD has been toggled home
                if(playerRepositioningScript.GetToggleHmdStatus() == true)
                {
                    // Start the instructions reading timer
                    stateTransitionStopwatch.Restart();
                }
                
            }

            // If the time for reading instructions has elapsed
            if (stateTransitionStopwatch.ElapsedMilliseconds >= timeForReadingInstructionsInMs)
            {
                Debug.Log("The time for reading instructions has elapsed.");
                // Stop the stopwatch that delayed the Instructions timer until the player camera had been toggled home.
                stateTransitionStopwatch.Reset();

                // If we don't want to record the neutral until the experimenter presses a button
                if(requireButtonPressToStartRecordingFlag == true)
                {
                    // If the experimenter indicated that the neutral pose recording should start with the button press
                    if(startNeutralPoseRecordingFlag == true)
                    {
                        // Change states to the recording state
                        ChangeActiveState(recordNeutralPoseStateString);
                    }
                }
                // Else if we'd like to start recording the neutral pose as soon as the time for instructions expires
                else
                {
                    // Change states to the recording state without waiting for any button presses
                    ChangeActiveState(recordNeutralPoseStateString);
                }
            }
        }
        // If we're currently measuring the neutral pose
        else if (currentState == recordNeutralPoseStateString)
        {
            // Store the frame data
            storeFrameData();

            // If enough time has elapsed since the person stopped touching the reach target/ball
            if (stateTransitionStopwatch.ElapsedMilliseconds >= timeToRecordNeutralPoseInMs)
            {
                // Switch to the game over state.
                ChangeActiveState(gameOverStateString);
            }

        }
        else if (currentState == gameOverStateString)
        {
            // Do nothing in the game over state
        }
        else
        {

        }


        // Ongoing monitoring of the recording toggle value, regardless of state
        if (toggleToStartRecording != toggleToStartRecordingLastValue)
        {
            // Update the recording toggle value
            toggleToStartRecordingLastValue = toggleToStartRecording;

            // Note that the experimenter requested that the recording start
            startNeutralPoseRecordingFlag = true;
        }
    }






    // BEGIN: Private functions*********************************************************************************

    private void SetFrameAndTrialDataNaming()
    {
        // Set the frame data column headers
        string[] csvFrameDataHeaderNames = new string[]{
            "TIME_AT_UNITY_FRAME_START", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT",
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
            };

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // Set the trial data column headers
        string[] csvTrialDataHeaderNames = new string[]{
                "HMD_START_POS_X_UNITY_FRAME", "HMD_START_POS_Y_UNITY_FRAME", "HMD_START_POS_Z_UNITY_FRAME",
                "TRANSFORM_VICON_TO_TRACKER_1_1", "TRANSFORM_VICON_TO_TRACKER_1_2", "TRANSFORM_VICON_TO_TRACKER_1_3", "TRANSFORM_VICON_TO_TRACKER_1_4",
                "TRANSFORM_VICON_TO_TRACKER_2_1", "TRANSFORM_VICON_TO_TRACKER_2_2", "TRANSFORM_VICON_TO_TRACKER_2_3", "TRANSFORM_VICON_TO_TRACKER_2_4",
                "TRANSFORM_VICON_TO_TRACKER_3_1", "TRANSFORM_VICON_TO_TRACKER_3_2", "TRANSFORM_VICON_TO_TRACKER_3_3", "TRANSFORM_VICON_TO_TRACKER_3_4",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_3",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_1_4",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_3",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_2_4",
                "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_1", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_2", "TRANSFORM_UNITY_MF_TO_ADJUSTED_MF_3_3",
                "TRANSFORM_VICON_TO_TRACKER_3_4",
                "TRIAL_START_TIME", "TRIAL_END_TIME"};

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvTrialDataHeaderNames);

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

        //set the frame data and the reach and lean performance trial subdirectory name (will go inside the CSV folder in Assets)
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvExcursionPerformanceSummarySubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvEmgDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvStanceModelDataSubdirectoryName(subdirectoryString);

        // 4.) Call the function to set the file names (within the subdirectory) for the current block
        SetFileNamesForCurrentBlockTrialAndFrameData();

    }

    private void SetFileNamesForCurrentBlockTrialAndFrameData()
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

        // Set the stance model data file name
        string fileNameStanceModelData = fileNameStub + "_StanceModel_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvStanceModelDataFileName(fileNameStanceModelData);
    }



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
        else if (currentState == instructionsStateString)
        {
            stateAsFloat = 2.0f;
        }
        else if (currentState == recordNeutralPoseStateString)
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

    //This function is called when the frame data should be written to file (typically at the end of a block of data collection). 
    private void tellDataRecorderToWriteStoredDataToFile()
    {
        // Tell the general data recorder to write the frame data to file
        generalDataRecorderScript.writeFrameDataToFile();

        //Also, tell the general data recorder to write the task-specific trial data to  file
        generalDataRecorderScript.writeTrialDataToFile();

        // Also tell the general data recorder to write the stance model data to file
        generalDataRecorderScript.writeStanceModelDataToFile();

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


    // The main function to load the stance model data and frame data, compute needed median values that
    // represent the "neutral posture", and then save them out to a CSV file. 
    public void ExtractAndSaveMedianPosture(string stanceModelCsvPath, string frameDataCsvPath, string outputCsvPath)
    {
        // Load CSVs
        var stanceRows = LoadCsvToDictList(stanceModelCsvPath);
        var frameRows = LoadCsvToDictList(frameDataCsvPath);

        // Filter by time
        var filteredStance = stanceRows
            .Where(row => InTimeRange(row, "TIME_UNITY_FRAME_START", currentTrialStartTime, currentTrialEndTime))
            .ToList();

        var filteredFrame = frameRows
            .Where(row => InTimeRange(row, "TIME_AT_UNITY_FRAME_START", currentTrialStartTime, currentTrialEndTime))
            .ToList();

        // Extract and compute medians
        var thetaKeys = new[] { "Theta_1", "Theta_2", "Theta_3", "Theta_4", "Theta_5" };
        var positionKeys = new[]
        {
            "KNEE_CENTER_IN_FRAME_0_X", "KNEE_CENTER_IN_FRAME_0_Y", "KNEE_CENTER_IN_FRAME_0_Z",
            "PELVIC_CENTER_IN_FRAME_0_X", "PELVIC_CENTER_IN_FRAME_0_Y", "PELVIC_CENTER_IN_FRAME_0_Z",
            "CHEST_CENTER_IN_FRAME_0_X", "CHEST_CENTER_IN_FRAME_0_Y", "CHEST_CENTER_IN_FRAME_0_Z",
            "HMD_POS_IN_FRAME_0_X", "HMD_POS_IN_FRAME_0_Y", "HMD_POS_IN_FRAME_0_Z"
        };

        // Create a new dictionary to store the median values of the joint vars and key positions.
        Dictionary<string, float> medians = new Dictionary<string, float>();

        // Compute the median for each joint variable value ("theta")
        foreach (var key in thetaKeys)
            medians["Median_" + key + "_In_Rads"] = Median(filteredStance.Select(row => float.Parse(row[key])).ToList());
        
        // Compute the median for each knee, pelvis, or chest midpoint position component ("positionKey")
        foreach (var key in positionKeys)
            medians["Median_" + key] = Median(filteredFrame.Select(row => float.Parse(row[key])).ToList());

        // Save to CSV
        using StreamWriter writer = new StreamWriter(outputCsvPath, false);
        writer.WriteLine(string.Join(",", medians.Keys));
        writer.WriteLine(string.Join(",", medians.Values.Select(v => v.ToString("F4"))));

        Debug.Log("Saved median postures to: " + outputCsvPath);
    }

    // Loads a CSV file into a list of dictionaries, where each dictionary represents a row.
    // The keys are column headers, and the values are the corresponding entries as strings.
    private List<Dictionary<string, string>> LoadCsvToDictList(string path)
    {
        var lines = File.ReadAllLines(path);
        var headers = lines[0].Split(',');

        var rows = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            var row = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length; j++)
            {
                row[headers[j]] = values[j];
            }
            rows.Add(row);
        }
        return rows;
    }

    // Checks whether the specified time value in the row is within the given time range.
    // Returns true if the time value (parsed from the row) falls between [start, end].
    private bool InTimeRange(Dictionary<string, string> row, string timeKey, float start, float end)
    {
        if (float.TryParse(row[timeKey], out float t))
            return t >= start && t <= end;
        return false;
    }

    // Computes the median of a list of floats.
    // If the count is odd, returns the middle value.
    // If even, returns the average of the two middle values.
    private float Median(List<float> values)
    {
        if (values.Count == 0) return float.NaN;

        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;

        return (sorted.Count % 2 != 0) ?
            sorted[mid] :
            (sorted[mid - 1] + sorted[mid]) / 2f;
    }

    // END: Private functions***********************************************************************************



    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void ChangeActiveState(string newState)
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
            else if (currentState == instructionsStateString)
            {
                exitInstructionsState();
            }
            else if (currentState == recordNeutralPoseStateString)
            {
                exitRecordNeutralPoseState();
            }

            //then call the entry function for the new state
            if (newState == instructionsStateString)
            {
                enterInstructionsState();
            }
            else if (newState == recordNeutralPoseStateString)
            {
                enterRecordNeutralPoseState();
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

    private void enterInstructionsState()
    {
        // Set the instructions text
        instructionsText.text = holdNeutralInstructionsString; 

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

    private void enterRecordNeutralPoseState()
    {
        // switch states
        currentState = recordNeutralPoseStateString;

        // Clear the instructions text
        instructionsText.text = "";

        // Store the trial start time for this trial
        currentTrialStartTime = Time.time;

        // Restart the stopwatch that keeps track of how long we have per trial/state from zero.
        stateTransitionStopwatch.Restart();
    }



    private void exitRecordNeutralPoseState()
    {
        // Mark the trial end time
        currentTrialEndTime = Time.time;
    }

    private void enterGameOverState()
    {
        // switch states
        currentState = gameOverStateString;

        // Send the stop sync signal via the Photon
        communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();

        // write data to file
        tellDataRecorderToWriteStoredDataToFile();

        // Load the stance model data and frame data, then construct the neutral pose and positions you need. 
        // Path to stance model data and frame data
        string pathToStanceModelData = generalDataRecorderScript.GetStanceModelDataFilePath();
        string pathToFrameData = generalDataRecorderScript.GetFrameDataFilePath();
        string medianPosturesFilePath = getDirectoryPath() + subdirectoryName + medianPostureFileName;
        ExtractAndSaveMedianPosture(pathToStanceModelData, pathToFrameData, medianPosturesFilePath);

        // Display text that the task is over
        instructionsText.text = "You data has been recorded. \n Thank you for testing!";
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
        return new List<Vector3>();
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
        return "";
    }

    // END: Vicon <-> Unity mapping functions *********************************************************************************


    // START: other abstract level manager public functions *******************************************************************

    public override bool GetEmgStreamingDesiredStatus()
    {
        return levelManagerStartupSupportScript.GetEmgStreamingDesiredStatus();
    }

    // END: other abstract level manager public functions *******************************************************************


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
