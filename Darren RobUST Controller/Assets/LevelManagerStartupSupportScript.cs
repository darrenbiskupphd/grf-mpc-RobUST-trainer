// This script helps the level manager do setup that shouldn't change with the Unity task. 
// For example, initializing the EMG system (if using), telling the structure matrix script to load needed files, etc. 
// We'll access settings set in the LevelManager here, but otherwise this script will have it's own references. 
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below
using Debug = UnityEngine.Debug;

public class LevelManagerStartupSupportScript : MonoBehaviour
{

    // Public local flags for controlling setup
    public bool DEBUG_MODE_FLAG; // whether or not to print debugs. Gets passed to other scripts by calling their public functions.
    public bool streamingEmgDataFlag; // Whether to run the EMG service (true) or not (false)
    public bool syncingWithExternalHardwareFlag; // Whether we're pulsing a microcontroller (Photon) pin to signal START to external hardware (for time sync).

    // Script to control settings for, create, and access the kinematic model
    public KinematicModelClass kinematicModelAccessScript;
    public ManageCenterOfMassScript centerOfMassManagerScript;
    public ViveTrackerDataManager viveTrackerDataManagerScript;
    public ForceFieldHighLevelControllerScript forceFieldHighLevelControllerScript;
    public CableTensionPlannerScript cableTensionPlannerScript;
    public CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;
    public BuildStructureMatricesForBeltsThisFrameScript structureMatrixBuilderScript;
    public GameObject emgDataStreamerObject;
    private StreamAndRecordEmgData emgDataStreamerScript;
    public SubjectInfoStorageScript subjectSpecificDataScript;
    public CommunicateWithPhotonViaSerial communicateWithPhotonScript;

    // Reference to the task-specific level manager, using it's abstract parent-class for generalizability.
    public LevelManagerScriptAbstractClass levelManagerScript;

    // More niche controls
    // Toggle to force saving tensions data to file on the PXI - used mostly for testing.
    public bool sendTaskOverCommandToRobustToggle;
    private bool sendTaskOverCommandToRobustTogglePrevious;
    // MANUAL write EMG data to file toggle
    public bool writeEmgDataToFileNowToggle;
    private bool writeEmgDataToFileNowTogglePrevious;

    // Private vars
    // Variables for running the service-specific setup code ************************************
    private KinematicModelOfStance stanceModel;
    private bool stanceModelAvailableFlag = false;
    private float unityFrameTimeAtWhichHardwareSyncSent; // IMPORTANT - likely access and save this out if using.
    // Variables for state transition ********************
    private string currentState;
    private const string waitingForEmgReadyStateString = "waiting_for_emg_ready";
    private const string waitingForSetupStateString = "waiting_for_setup";
    private const string startupCompleteStateString = "startup_complete";
    // Variable indicating that we've finished all setup performed by this script *******************
    private bool startupServicesScriptCompleteFlag = false; // init false
    // Variables for data storage (e.g., file paths)**********************************
    private string subdirectoryName; // the subdirectory for saving this session's data
    private string mostRecentFileNameStub; // the prefix/stub for the current file name (prefix to trial, marker, frame, emg data).
    private string subdirectoryWithSetupDataForStructureMatrixComputation;
    // EMG Startup variables****************************************************************************
    private bool emgBaseIsReadyForTriggerStatus = false;
    private Stopwatch delayStartSyncSignalStopwatch = new Stopwatch();
    private const float millisecondsDelayForStartEmgSyncSignal = 500.0f;

    // Start is called before the first frame update
    void Start()
    {
        // Tell the COM manager that we will be using the RobUST
        if (kinematicModelAccessScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            centerOfMassManagerScript.SetUsingCableDrivenRobotFlagToTrue();
        }
        else if (kinematicModelAccessScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)

        {
            viveTrackerDataManagerScript.SetUsingCableDrivenRobotFlagToTrue();
        }

        // Get the task-specific name from the level manager, then build the current file name for save-out. 
        // We can send the current file name to the PXI. 


        // Build the path to the directory with Structure Matrix data. 
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).
        subdirectoryWithSetupDataForStructureMatrixComputation = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "StructureMatrixData" + "/" + dateString + "/";

        // Send the file name stub to the RobUST so that it can properly named the saved-out tensions file.
        // (subject number, date, time). 
        forceFieldRobotTcpServerScript.SendCommandWithCurrentTaskInfoString(mostRecentFileNameStub);

        // Tell the structure matrix builder to load the data needed to build the structure matrices from file
        structureMatrixBuilderScript.SetStructureMatrixDirectoryName(subdirectoryWithSetupDataForStructureMatrixComputation);

        // Init the toggle that can be used to say the task is over (for tension saving testing)
        sendTaskOverCommandToRobustTogglePrevious = sendTaskOverCommandToRobustToggle;

        // Set the debug flags in print-heavy scripts
        forceFieldHighLevelControllerScript.SetDebugModeFlag(DEBUG_MODE_FLAG); // turn on/off debug mode in the high-level controller
        cableTensionPlannerScript.SetDebugModeFlag(DEBUG_MODE_FLAG);



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

    // Update is called once per frame
    void Update()
    {
        // Check to see if the task-over command toggle has changed states.
        SendTaskOverCommandToRobustOnToggle();

        // Depending on the current state of the level manager state machine,
        // take action at the start of each frame and transition between states as needed.
        switch (currentState)
        {
            case waitingForEmgReadyStateString:
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

                    // We've accomplished a minimum delay for sending the sync signal and sent it, so 
                    // switch to the Waiting For Home state
                    changeActiveState(waitingForSetupStateString);
                }
                break;
            case waitingForSetupStateString:
                //SetCurrentAngles(90, 90);
                bool forceFieldLevelManagerSetupCompleteFlag =
                    forceFieldHighLevelControllerScript.GetForceFieldLevelManagerSetupCompleteFlag();
                if (forceFieldLevelManagerSetupCompleteFlag == true)
                {
                    // Get the stance model and note that it's ready
                    stanceModel = forceFieldHighLevelControllerScript.GetStanceModel();
                    stanceModelAvailableFlag = true; // a flag indicating that stance model data (e.g., live joint variable values) are ready for access

                    // Enter the Startup Complete state
                    changeActiveState(startupCompleteStateString);
                }

                break;
        }
    }


    // BEGIN: Public Getter/Setter Functions *********************************************************************************

    // Get a flag indicating whether or not this script has completed its required setup steps.
    public bool GetServicesStartupCompleteStatusFlag()
    {
        return startupServicesScriptCompleteFlag;
    }

    public bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }

    public bool GetSyncingWithExternalHardwareFlag()
    {
        return syncingWithExternalHardwareFlag;
    }


    public void TellPhotonToPulseSyncPin()
    {
        // Pulse the pin
        communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

        // Store the hardware sync signal sent time
        unityFrameTimeAtWhichHardwareSyncSent = Time.time;
    }


    // END: Public Getter/Setter Functions  ***********************************************************************************



    // BEGIN: Niche Testing Functions *********************************************************************************

    // Testing only function to say the task is over. Tests tension-saving in labview.
    private void SendTaskOverCommandToRobustOnToggle()
    {
        if (sendTaskOverCommandToRobustToggle != sendTaskOverCommandToRobustTogglePrevious)
        {
            forceFieldRobotTcpServerScript.SendCommandWithTaskOverSpecifier();
            sendTaskOverCommandToRobustTogglePrevious = sendTaskOverCommandToRobustToggle;
        }
    }

    // END: Niche Testing Functions *********************************************************************************


    // BEGIN: State Machine Management Functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if (newState != currentState)
        {
            Debug.Log("Level Manager Startup Support: Transitioning states from " + currentState + " to " + newState);
            // Call exit functions
            if (currentState == waitingForEmgReadyStateString)
            {
                ExitWaitingForEmgReadyState();
            }
            else if (currentState == waitingForSetupStateString)
            {
                ExitWaitingForSetupState();
            }

            //then call the entry function for the new state
            if (newState == waitingForEmgReadyStateString)
            {
                EnterWaitingForEmgReadyState();
            }
            else if (newState == waitingForSetupStateString)
            {
                EnterWaitingForSetupState();
            }
            else if (newState == startupCompleteStateString)
            {
                EnterStartupCompleteStateString();
            }
        }
    }


    private void EnterWaitingForEmgReadyState()
    {
        currentState = waitingForEmgReadyStateString;
    }
    private void ExitWaitingForEmgReadyState()
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


    private void EnterWaitingForSetupState()
    {
        currentState = waitingForSetupStateString;
    }
    private void ExitWaitingForSetupState()
    {
        // do nothing for now
    }

    private void EnterStartupCompleteStateString()
    {
        // Set the state to the startup complete state. 
        currentState = startupCompleteStateString;

        // Set a flag indicating setup is complete! 
        startupServicesScriptCompleteFlag = true;
    }

    private void ExitStartupCompleteStateString()
    {
        // do nothing for now
    }




    // END: State Machine Management Functions ***********************************************************************************


}
