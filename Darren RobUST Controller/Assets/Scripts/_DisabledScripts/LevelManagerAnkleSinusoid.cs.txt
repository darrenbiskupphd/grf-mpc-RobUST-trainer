using System.Collections;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class LevelManagerAnkleSinusoid : MonoBehaviour
{

    private const string leftAnkleActiveString = "Left";
    private const string rightAnkleActiveString = "Right";
    private const string ankleRomDataFileNameToSaveTo = "Ankle_ROM_data";
    public GameObject AnkleAngleManager;  //the GameObject containing the script which computes ankle angles from the Vicon data.
    private AnkleAngleManagementScript AnkleAngleManagerScript; //the script which computes ankle angle from the Vicon data.
    public GameObject fileNameGenerator; // the object containing the script that generates save file names
    private FileNameGenerationScript fileNameGeneratorScript; //the script that generates file names for saving data
    public GameObject ankleRangeOfMotionFinder; //the GameObject containing the script that tracks ankle motion to find it's ROM
    private MeasureAnkleRomScript ankleRangeOfMotionFinderScript; //the GameObject containing the script that tracks ankle motion to find it's ROM
    public GameObject dataWriterObject; //the GameObject containing the script that writes data to a .csv file 
    private StoreAndWriteDataToFileScript dataWriterScript; // used only to write ROM data to file. 
    public GameObject drawSinusoidGameObject; // the GameObject containting the script that animates the sinusoid waveform. 
    private DrawSinusoid drawSinusoidScript; // the script that animates the sinusoid waveform.
    public GameObject experimentalSettingsObject;
    private SinusoidExperimentalParametersScript experimentalSettingsScript;
    public GameObject generalDataRecorderObject;
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    // subject-specific data (object, script)
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data


    bool isAnkleAngleManagerReady = false; //whether or not the script that computes ankle angles is ready to distribute data
    public Toggle leftAnkleIsActiveToggle; // the UI Toggle object. When the .isOn property is true, we should be measuring the left ankle.
    public Toggle rightAnkleIsActiveToggle; // the UI Toggle object. When the .isOn property is true, we should be measuring the right ankle.
    public Toggle currentlyMeasuringRangeOfMotionToggle; // the UI Toggle object. When the .isOn property is true, we should be measuring the right ankle.

    // State management
    private string currentState;
    private const string setupStateString = "SETUP";
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string collectingPassiveRomStateString = "COLLECT_PASSIVE_ROM";
    private const string activeSinusoidTrackingStateString = "ACTIVE_SINUSOID_TRACKING";
    private const string idlingStateString = "IDLING";

    // Data saving and file naming
    private string subdirectoryName; // the subdirectory (folder) in which the subject's data will be stored, for the given day.
    private string mostRecentFileNameStub; //the string specifying the .csv file save name for the frame,
                                           //without the suffix specifying whether it's marker, frame, or trial data.
    private string nameOfThisTask = "Sinusoid"; // The name for this game/task, which will identify files generated from it

    // Subject-specific data
    private string currentStimulationStatus; //the current stimulation status for this block as a string. Used for inclusion in saved file names.
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // Tracking ongoing sinusoid 
    private float timeOfMostRecentSinusoidStartInSeconds = Mathf.Infinity; // time of most recent sinusoid tracking start [s].
                                                                           // Note, set to +Infinity to say that there is no ongoing sinusoid.

    //Frame data: ankle angles
    private Vector3 rightAnkleEulerAnglesInDegrees;
    private Vector3 leftAnkleEulerAnglesInDegrees;

    // Synchronize with external hardware
    public GameObject communicateWithPhotonViaSerialObject;
    private CommunicateWithPhotonViaSerial communicateWithPhotonScript;
    public bool syncingWithExternalHardwareFlag; // Whether or not we're using the System Sync object to sync (via Photon)
                                                 // with EMGs and Vicon nexus

    // EMG data streaming
    public GameObject emgDataStreamerObject;
    private StreamAndRecordEmgData emgDataStreamerScript; // communicates with Delsys base station, reads and saves EMG data
    private bool emgBaseIsReadyForTriggerStatus = false; // whether the EMG base station is ready for the sync trigger (true) or not (false)
    private uint millisecondsDelayForStartEmgSyncSignal = 1000; // How long to wait between base station being armed for sync and actually
                                                                // sending the start sync signal (at minimum)
    private bool hasEmgSyncStartSignalBeenSentFlag = false; // A flag that is flipped to true when the EMG sync signal (and, thus, start data stream)
                                                            // was sent to the Delsys base station.
    private Stopwatch delayStartSyncSignalStopwatch = new Stopwatch(); // A stopwatch to add a small delay to sending our photon START sync signal
                                                                       // Seems necessary for Delsys base station.

    // Time stamps
    private float unityFrameTimeAtWhichHardwareSyncSent = 0.0f; // The Time.time of the Update() frame when the hardware sync signal command was sent.

    // The Vicon device data access object (this includes force plates and the analog sync pin. We only care about the sync pin in this task!)
    public GameObject forcePlateDataAccessObject; // Poorly named, since we are actually only interested in the analog sync pin "device" in this task.
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;


    // Start is called before the first frame update
    void Start()
    {


        AnkleAngleManagerScript = AnkleAngleManager.GetComponent<AnkleAngleManagementScript>(); //
        fileNameGeneratorScript = fileNameGenerator.GetComponent<FileNameGenerationScript>();
        ankleRangeOfMotionFinderScript = ankleRangeOfMotionFinder.GetComponent<MeasureAnkleRomScript>();
        dataWriterScript = dataWriterObject.GetComponent<StoreAndWriteDataToFileScript>();
        drawSinusoidScript = drawSinusoidGameObject.GetComponent<DrawSinusoid>();
        experimentalSettingsScript = experimentalSettingsObject.GetComponent<SinusoidExperimentalParametersScript>();
        communicateWithPhotonScript = communicateWithPhotonViaSerialObject.GetComponent<CommunicateWithPhotonViaSerial>();

        // data saving
        generalDataRecorderScript = generalDataRecorderObject.GetComponent<GeneralDataRecorder>();

        // get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // If we're syncing with external hardware (typically the EMGs are included)
        if (syncingWithExternalHardwareFlag == true)
        {
            // Get a reference to the EMGs
            emgDataStreamerScript = emgDataStreamerObject.GetComponent<StreamAndRecordEmgData>();
        }

        // Get a reference to the force plate data access script 
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();

        // Set the stimulation status.
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();
        Debug.Log("Before setting file naming, set the current stimulation status string to: " + currentStimulationStatus);

        // Set the directory and file naming
        setFrameAndTrialDataNaming();

        // We start in the setup staet
        currentState = setupStateString;


        // TESTING ONLY!
        //communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();





    }

    // Update is called once per frame
    void Update()
    {
        // TESTING ONLY: if we want to test the Photon hardware stop signal.
       /* if(Time.time > 5.0f)
        {
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();
        }*/

        //if we're still setting up
        if (currentState == setupStateString)
        {
            // See if the ankle angle manager ready now
            isAnkleAngleManagerReady = AnkleAngleManagerScript.getAnkleAngleManagerReadyStatus();

            if (isAnkleAngleManagerReady) // if the ankle manager is ready
            {
                // If we're syncing with external hardware (EMGs), we should move to a special state 
                // for EMG setup
                if (syncingWithExternalHardwareFlag == true)
                {
                    // then move to the waiting for EMG state
                    changeActiveState(waitingForEmgReadyStateString);
                }
                else // If not syncing with hardware, then set up is complete
                {
                    // then proceed by moving to the idling state
                    changeActiveState(idlingStateString);

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
                // Synchronize the external hardware via the Photon's hardware sync start signal 
                communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

                // Store the time at which the external sensory hardware was synced
                unityFrameTimeAtWhichHardwareSyncSent = Time.time;

                // Now that setup is complete, switch to the Idling state. 
                changeActiveState(idlingStateString);

                Debug.Log("Level manager setup complete");
            }
        }
        else // if we're no longer in the setup state
        {
            // Then decide on what should be done based on the current state
            if(currentState == collectingPassiveRomStateString)
            {
                // Gather the frame data and store it
                getFrameDataForThisFrame();
                //storeFrameData();
            }
            else if(currentState == activeSinusoidTrackingStateString)
            {
                // Gather the frame data and store it
                getFrameDataForThisFrame();
                storeFrameData();

            }
            else if (currentState == idlingStateString)
            {
                // there's no need to collect data while the subject is not doing any activity.
            }


        }
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
            else if (currentState == idlingStateString)
            {
                exitIdlingState();
            }
            else if (currentState == collectingPassiveRomStateString)
            {
                exitPassiveRomMeasurementState();
            }
            else if (currentState == activeSinusoidTrackingStateString)
            {
                exitActiveSinusoidTrackingState();
            }else // not a valid state to exit
            {

            }

            //then call the entry function for the new state
            if (newState == idlingStateString)
            {
                enterIdlingState();
            }
            else if (newState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            else if (newState == collectingPassiveRomStateString)
            {
                enterPassiveRomMeasurementState();
            }
            else if (newState == activeSinusoidTrackingStateString)
            {
                enterActiveSinusoidTrackingState();
            }
        }

    }

    private void enterSetupState()
    {
        //set the current state
        currentState = setupStateString;
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

    private void enterIdlingState()
    {
        Debug.Log("Changing to Idling state");
        currentState = idlingStateString;
    }

    private void exitIdlingState()
    {

    }

    private void enterPassiveRomMeasurementState()
    {
        Debug.Log("Changing to Passive ROM Measurement state");

        // Send the start signal to the EMG, Vicon (and any other) 
        // sensor systems
          

        // Tell the ankle manager to start sending marker data to 
        // the General Data Recorder while we collect the ROM,
        // so it can be stored to file later.
        // Note that this will only "work" if 
        // the ankle manager has completed setup and is ready to stream Vicon data.
        AnkleAngleManagerScript.ankleManagerStartStoringMarkerDataToFile();

        // Update the file name (including the "stub") so that we have a current time for starting 
        // the ROM collection 
        setFileNamesForCurrentBlockTrialAndFrameData();

        //change the current state to the Collecting ROM state
        currentState = collectingPassiveRomStateString;
    }


    private void exitPassiveRomMeasurementState()
    {
        // Terminate recording on external sensors
        communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();

        // We should save the measured ROM data to file. 
        //First, get the file name
        string fileNamePrefix = fileNameGeneratorScript.getFileSaveNamePrefix();
        string fileName = fileNamePrefix + ankleRomDataFileNameToSaveTo + ".csv";

        //Get the ankle ROM data for the two ankles
        (string[] ankleRomDataFileHeaders, float[] ankleRomDataToSave) = ankleRangeOfMotionFinderScript.getAnkleRomDataInFormatToSave();
        Debug.Log("Right ankle (min, max): (" + ankleRomDataToSave[0] + ", " + ankleRomDataToSave[1] +
            " and left ankle (min, max): (" + ankleRomDataToSave[2] + ", " + ankleRomDataToSave[3]);

        //format the data as a list of float[], where each entry in the list will be a row of the output .csv file
        List<float[]> dataToSaveAsList = new List<float[]>();
        dataToSaveAsList.Add(ankleRomDataToSave);

        //send the data to the file writer to be saved to a .csv file
        dataWriterScript.writePassedInListOfFloatArraysToFile(ankleRomDataFileHeaders, dataToSaveAsList, fileName);

        // Tell the ankle angle manager to write the marker data to file and 
        // to stop sending marker data to the General Data Recorder.
        AnkleAngleManagerScript.ankleManagerStopStoringMarkerDataToFile(); 
        AnkleAngleManagerScript.tellDataRecorderToSaveStoredDataToFile(subdirectoryName, mostRecentFileNameStub + "Collecting_ROM_marker_data"); // give a file name specific for the Collecting ROM state exit

    }

    private void enterActiveSinusoidTrackingState()
    {
        Debug.Log("Changing to Tracking Sinusoid state");

        // Change the current state to the Active Block state
        currentState = activeSinusoidTrackingStateString;

        // Tell the ankle manager to start sending marker data to 
        // the General Data Recorder while we perform the tracking task,
        // so it can be stored to file later.
        // Note that this will only "work" if 
        // the ankle manager has completed setup and is ready to stream Vicon data.
        AnkleAngleManagerScript.ankleManagerStartStoringMarkerDataToFile();

        // Start the sinusoid animator for the preset number of cycles
        drawSinusoidScript.startSinusoidForAGivenNumberOfCycles(experimentalSettingsScript.getNumberOfCyclesToTrackPerBlock());


    }

    private void exitActiveSinusoidTrackingState()
    {

        // Terminate recording on external sensors
        communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();

        // Tell the ankle angle manager to write the marker data to file and 
        // to stop sending marker data to the General Data Recorder.
        AnkleAngleManagerScript.ankleManagerStopStoringMarkerDataToFile();
        AnkleAngleManagerScript.tellDataRecorderToSaveStoredDataToFile(subdirectoryName, mostRecentFileNameStub + "Track_Sinusoid_marker_data"); // give a file name specific for the Active Sinusoid Tracking state exit

        // Store the current trial data so it can be written to file later
        tellDataRecorderToWriteStoredDataToFile();
    }

    // END: State machine state-transitioning functions *********************************************************************************




    // BEGIN: Public functions *********************************************************************************

    public void startSinusoidWaveformForCertainNumberOfCycles()
    {
        // If we're in the Idling state
        if(currentState == idlingStateString)
        {
            // Then we can initiate a new period of sinusoid tracking
            changeActiveState(activeSinusoidTrackingStateString);

            // Set the current file name for the frame and trial data to the ankle
            // active at the START of data collection.
            setFileNamesForCurrentBlockTrialAndFrameData();

            // Note the time at which the sinusoid started
            timeOfMostRecentSinusoidStartInSeconds = Time.time;
        }
    }


    public void sinusoidWaveformHasFinishedCertainNumberOfCycles()
    {
        // Once the waveform has finished displaying a certain number of cycles for tracking, 
        // return to the idle state
        changeActiveState(idlingStateString);

        // Set the time to + Infinity to note that there is no ongoing sinusoid
        timeOfMostRecentSinusoidStartInSeconds = Mathf.Infinity;

    }


    public bool getMeasuringRangeOfMotionStatus()
    {
        bool currentlyMeasuringRangeOfMotion = currentlyMeasuringRangeOfMotionToggle.isOn;
        return currentlyMeasuringRangeOfMotion;
    }


    public string getActiveAnkle()
    {
        if (leftAnkleIsActiveToggle.isOn) //if the left ankle is active (left ankle toggle is on)
        {
            return leftAnkleActiveString;
        }
        else //if the right ankle is active (right ankle toggle is on)
        {
            return rightAnkleActiveString;
        }
    }



    //Get the ankle plantflexion/dorsiflexion angle of the active ankle. 
    //Note: The Euler angles are reported relative to the shank coordinate system (they are extrinsic). 
    //The order is z,y,x. This means that the .y component of the vector is the plantflexion/dorsiflexion angle.
    public (bool, float) getPlantarflexionAngleOfActiveAnkle()
    {
        Vector3 rightAnkleEulerAngles;
        Vector3 leftAnkleEulerAngles;
        float anklePlantarflexionAngle = -999.9f;
        bool successfullyGotAnkleAngle = false; 
        
        //if the ankle data manager is ready to dispense data, get the shank-to-ankle Euler angles
        if (isAnkleAngleManagerReady)
        {
            (rightAnkleEulerAngles, leftAnkleEulerAngles) = AnkleAngleManagerScript.getAnkleAngles();

            if (rightAnkleIsActiveToggle.isOn) //if the right ankle is active
            {
                anklePlantarflexionAngle = rightAnkleEulerAngles.y;
                successfullyGotAnkleAngle = true;
            }
            else //else if the left ankle is active
            {
                anklePlantarflexionAngle = leftAnkleEulerAngles.y;
                successfullyGotAnkleAngle = true;
            }
        }

        return (successfullyGotAnkleAngle, anklePlantarflexionAngle);
    }





    public void rightAnkleToggleStateChangeCallback()
    {
        if (rightAnkleIsActiveToggle.isOn) //if the right ankle toggle is now on
        {
            //turn off the left ankle toggle
            leftAnkleIsActiveToggle.isOn = false;

        } else // if the right ankle toggle is now off
        {
            //turn on the left ankle toggle
            leftAnkleIsActiveToggle.isOn = true;
        }
    }

    public void leftAnkleToggleStateChangeCallback()
    {
        if (leftAnkleIsActiveToggle.isOn) //if the right ankle toggle is now on
        {
            //turn off the left ankle toggle
            rightAnkleIsActiveToggle.isOn = false;
        }
        else // if the right ankle toggle is now off
        {
            //turn on the left ankle toggle
            rightAnkleIsActiveToggle.isOn = true;
        }
    }



    public void measureRomToggleCallback()
    {
        if (!currentlyMeasuringRangeOfMotionToggle.isOn) //if we just stopped measuring ankle angle ROM 
        {
            (bool rightAnkleHasValidRom, bool leftAnkleHasValidRom) = ankleRangeOfMotionFinderScript.getRomValidityFlagForEachAnkle();

            if (rightAnkleHasValidRom && leftAnkleHasValidRom)//if we have valid ROMs for each ankle, then save
            {
                // Switch back to the Idling state. The Collecting ROM state exit function 
                // will handle various state exit tasks, such as saving the ROM data and marker data 
                // to file.
                changeActiveState(idlingStateString);

            }
            else // if both ankles do not have a valid ROM, do not save and print a message stating why we aren't saving
            {
                Debug.LogWarning("Exited the Measure ROM mode, but not saving ankle ROMs because at least one ankle did not have a valid/sufficiently large ROM.");
            }

        }
        else // if we have just started measuring the ankle angle ROM
        {
            // if we're in the idle state, switch states to the passive ankle measuring state
            // (do not switch to the ROM state if we're in the active ROM measurement state, for example)
            if(currentState == idlingStateString)
            {
                // When we change to the Collecting ROM state, we 
                // do various things, such as tell the the ankle angle manager
                // to start recording marker data to file. See the entry function for the state.
                changeActiveState(collectingPassiveRomStateString);
            }
        }
    }


    public string getFilePathToAnkleRomSavedData() {
        string fileNamePrefix = fileNameGeneratorScript.getFileSaveNamePrefixFromProjectFolder();
        string fileName = fileNamePrefix + ankleRomDataFileNameToSaveTo + ".csv";
        return fileName;
    }


    // END: Public functions *********************************************************************************



    private void getFrameDataForThisFrame()
    {
        // Get the current ankle angle (Euler angles in degrees)
        (rightAnkleEulerAnglesInDegrees, leftAnkleEulerAnglesInDegrees) = AnkleAngleManagerScript.getAnkleAngles();

        // 
    }




    private void setFrameAndTrialDataNaming()
    {
        // 1.) Frame data naming
        // A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE", "TIME_OF_MOST_RECENT_SINUSOID_START",
            "ACTIVE_ANKLE_IS_RIGHT_ANKLE_FLAG", "ACTIVE_ANKLE_IS_LEFT_ANKLE_FLAG", "RIGHT_ANKLE_EULER_X",
            "RIGHT_ANKLE_EULER_Y", "RIGHT_ANKLE_EULER_Z", "LEFT_ANKLE_EULER_X",
            "LEFT_ANKLE_EULER_Y", "LEFT_ANKLE_EULER_Z", "CURRENT_ANKLE_ANGLE", "CURRENT_TARGET_INDICATOR_AS_ANGLE",
            "CURRENT_TARGET_INDICATOR_PHASE", "CURRENT_ANKLE_ROM_ANGLE_MIN", "CURRENT_ANKLE_ROM_ANGLE_MAX"};

        // DESIRED! Add missing values to above.
        /*new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_OF_MOST_RECENT_SINUSOID_START",
            "ACTIVE_ANKLE_IS_RIGHT_ANKLE_FLAG", "ACTIVE_ANKLE_IS_LEFT_ANKLE_FLAG", "RIGHT_ANKLE_EULER_X",
            "RIGHT_ANKLE_EULER_Y", "RIGHT_ANKLE_EULER_Z", "LEFT_ANKLE_EULER_X",
            "LEFT_ANKLE_EULER_Y", "LEFT_ANKLE_EULER_Z",
            "CURRENT_ANKLE_ANGLE", "CURRENT_ANKLE_INDICATOR_Y_POS_UNITY_COORDS",
            "CURRENT_ANKLE_INDICATOR_Y_POS_VIEWPORT_COORDS", "CURRENT_TARGET_INDICATOR_AS_ANGLE", "CURRENT_TARGET_INDICATOR_Y_POS_UNITY_COORDS",
            "CURRENT_TARGET_INDICATOR_Y_POS_VIEWPORT_COORDS", "CURRENT_ANKLE_ROM_ANGLE_MIN", "CURRENT_ANKLE_ROM_ANGLE_MAX"};*/



        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // 2.) Trial data naming
        // A string array with all of the header names
        string[] csvTrialDataHeaderNames = new string[]{"ACTIVE_ANKLE_IS_RIGHT_ANKLE_FLAG", "ACTIVE_ANKLE_ROM_ANGLE_MIN", 
            "ACTIVE_ANKLE_ROM_ANGLE_MAX", "INTENDED_DORSIFLEXION_INTEGRATED_ABSEMENT_ERROR", "INTENDED_PLANTARFLEXION_INTEGRATED_ABSEMENT_ERROR",
            "TOTAL_ABSOLUTE_VALUE_INTEGRATED_ABSEMENT_ERROR_THIS_CYCLE", "EXPECTED_ABSOLUTE_VALUE_ABSEMENT_THIS_CYCLE", 
            "RATIO_OBSERVED_ABSEMENT_TO_EXPECTED_THIS_CYCLE"};

        //tell the data recorder what the trial data CSV headers will be
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvTrialDataHeaderNames);

        // 3.) Data subdirectory naming for trajectory tracing data
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).

        // Build the name of the subdirectory that will contain all of the output files for ankle sinusoid tracking this session
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + nameOfThisTask + "/" + dateString + "/";

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
        string currentAnkleAsString = "Active_Ankle_" + getActiveAnkle(); // We'll save each block into its own file
        string fileNameStub = nameOfThisTask + delimiter + subjectSpecificInfoString + delimiter + currentStimulationStatus + delimiter + dateAndTimeString + delimiter + currentAnkleAsString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the task-specific trial data file name (NOT IMPLEMENTED YET! 7/13/22).
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);

        // Set the EMG data file name
        string fileNameEmgData = fileNameStub + "_Emg_Data.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvEmgDataFileName(fileNameEmgData);
    }

    //Note, this function is called from Update(), not FixedUpdate(), and thus will record at a higher frequency than
    //FixedUpdate() most of the time.
    private void storeFrameData()
    {
        // The list that will store the data
        // Note: the header names for all of the data we will store are specifed in Start()
        List<float> frameDataToStore = new List<float>();

        //get the time called at the beginning of this frame (this call to Update())
        frameDataToStore.Add(Time.time);

        // Store the time at which the external hardware was synced via the Photon
        frameDataToStore.Add(unityFrameTimeAtWhichHardwareSyncSent);

        // The analog sync pin voltage (high = EMG streaming, low = EMG stopped)
        float analogSyncPinVoltage = scriptToRetrieveForcePlateData.GetMostRecentSyncPinVoltageValue();
        frameDataToStore.Add(analogSyncPinVoltage);

        // Store the time of the most recent sinusoid start, IF there is an active sinusoid. 
        // If inactive, value will be +Infinity.
        frameDataToStore.Add(timeOfMostRecentSinusoidStartInSeconds);

        // Store the active states of the ankles (only one should ever be true)
        frameDataToStore.Add(Convert.ToSingle(rightAnkleIsActiveToggle.isOn));
        frameDataToStore.Add(Convert.ToSingle(leftAnkleIsActiveToggle.isOn));

        // Store the right ankle angles. Recall, order is Z,X,Y in the extrinsic Euler ankles. 
        // Rotations are about the fixed foot frame, not the shank frame! 
        // Note that, since a ball-and-socket model of the foot-shank complex is not ideal, the concepts of 
        // plantarflexion/dorsiflexion are not a perfect fit.
        // Right side:
        //      - A small positive .z angle is eversion, whereas a small negative (or large positive close to 360) is 
        //        inversion
        //      - A small positive .x ankle is adduction, whereas a small negative (or large positive close to 360) is
        //        abduction
        //      - A value greater than 90 for .y angle is dorsiflexion, 90 is a right angle between shank z-axis and foot z-axis,
        //        and less than 90 is plantarflexion. 
        frameDataToStore.Add(rightAnkleEulerAnglesInDegrees.x); // .x is akin to the abudction/adduction angle. 
        frameDataToStore.Add(rightAnkleEulerAnglesInDegrees.y); // .y is the plantarflexion/dorsiflexion angle
        frameDataToStore.Add(rightAnkleEulerAnglesInDegrees.z); // .z is the ? angle

        // Store the left ankle angles. Recall, order is Z,X,Y in the extrinsic Euler ankles. 
        // Rotations are about the fixed foot frame, not the shank frame! 
        // Note that, since a ball-and-socket model of the foot-shank complex is not ideal, the concepts of 
        // plantarflexion/dorsiflexion are not a perfect fit.
        // Left side:
        //      - A small positive .z angle is inversion, whereas a small negative (or large positive close to 360) is 
        //        eversion (opposite of right leg!)
        //      - A small positive .x ankle is abduction, whereas a small negative (or large positive close to 360) is
        //        adduction (opposite of right leg!)
        //      - A value greater than 90 for .y angle is dorsiflexion, 90 is a right angle between shank z-axis and foot z-axis,
        //        and less than 90 is plantarflexion (SAME as right leg).
        frameDataToStore.Add(leftAnkleEulerAnglesInDegrees.x); // .x is akin to the abduction/adduction angle.
        frameDataToStore.Add(leftAnkleEulerAnglesInDegrees.y); // .y is akin to the plantarflexion/dorsiflexion angle
        frameDataToStore.Add(leftAnkleEulerAnglesInDegrees.z); // .z is akin to the inversion/eversion angle

        // Store the current ankle dorsiflexion/plantarflexion angle
        float anklePlantarflexionAngle = 0.0f;
        if (rightAnkleIsActiveToggle.isOn) //if the right ankle is active
        {
            anklePlantarflexionAngle = rightAnkleEulerAnglesInDegrees.y; // .y is the plantarflexion/dorsiflexion angle
        }
        else //else if the left ankle is active
        {
            anklePlantarflexionAngle = leftAnkleEulerAnglesInDegrees.y; // .y is the plantarflexion/dorsiflexion angle
        }
        frameDataToStore.Add(anklePlantarflexionAngle);

        // Store the current target ankle angle
        (bool successfulFetch, float targetAnkleAngle, float phaseModuloTwoPi) = drawSinusoidScript.getCurrentPhaseOfTargetSinusoidAsTargetAnkleAngle(); 
        frameDataToStore.Add(targetAnkleAngle);
        frameDataToStore.Add(phaseModuloTwoPi);

        // Store the current ankle minimum and maximum ROM 
        (bool successfulFetchOfAnkleData, string activeAnkle, float minimumAnkleAngle,
            float maximumAnkleAngle) = ankleRangeOfMotionFinderScript.getActiveAnkleIdentifierAndRangeOfMotion();
        frameDataToStore.Add(minimumAnkleAngle);
        frameDataToStore.Add(maximumAnkleAngle);

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
        //centerOfMassManagerScript.tellDataRecorderToSaveStoredDataToFile(subdirectoryName, mostRecentFileNameStub);

        //Also, tell the general data recorder to write the task-specific trial data to  file
        //generalDataRecorderScript.writeTrialDataToFile();
    }







}
