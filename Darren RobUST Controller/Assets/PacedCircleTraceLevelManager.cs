using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.Linq;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class PacedCircleTraceLevelManager : LevelManagerScriptAbstractClass
{

    // BEGIN: Public variables needed during experiment, for experimental control*********************
    public uint numberOfBlocks;
    public uint numberOfRevolutionsPerBlock; //add two opening throw-aways for acclimatization in each block
    public bool alternateDirectionsEachBlock;
    public bool startWithClockwiseBlockFlag; // The first block is clockwise (true) or counterclockwise (false). 
                                             // Only takes effect if the alternating directions each block flag is tru
    public bool usingBeltsToApplyForce;
    // END: Public variables needed during experiment, for experimental control*********************

    // BEGIN: Public GameObject variables and attached scripts*********************

    public GameObject player; //  the player game object
    public float circleTraceTargetStartAngleCounterclockwise = 100; // CCW degrees from +x-axis (right) (in degrees)
    public float circleTraceTargetStartAngleClockwise = 80; // CCW degrees from +x-axis (right) (in degrees
    private float circleTraceTargetStartAngleCounterclockwiseViconFrame; // CCW degrees from +x-axis of Vicon frame (in degrees - usually different by 180)
    private float circleTraceTargetStartAngleClockwiseViconFrame; // CCW degrees from +x-axis of Vicon frame (in degrees - usually different by 180)
    private float currentTraceTargetAngleInDegrees; //keeps track of the current trace target angle
    private float currentTraceTargetAngleInDegreesViconFrame; //keeps track of the current trace target angle in the Vicon frame
    private float previousPlayerAngleViconFrame = -1.0f; // keeps track of the last angle of the player (last frame). Initialize as negative.
    private Vector3 currentTraceIndicatorPositionViconFrame; // keeps track of the current trace indicator (target) position in the Vicon frame
    public float minimumAngularProximityOfPlayerToTargetDegrees; //specifies how close the player's angle can get to the target before the target advances/moves.
    public GameObject circle; //the circle game object
    private float circleRadiusInWorldUnits; // the radius of the circle in world units. Call the circle object to get this value in Start()
    public GameObject circleCenter; //the circle center game object
    public Material defaultCircleTraceMaterial; //the defualt material (color) of the circle center.
    public Material moveToHomeMaterial; //the material (color) of the circle center indicating the player should move there.
    private RenderCircle circleRenderScript; //the script that draws the circle
    private Vector3 circleCenterPosition; //the position of the center of the circle in world coordinates. Call the circle center object to get this value in Start()
    public GameObject circleTraceTarget; // the target for the tracing
    private ControlCircleTraceTargetPaced circleTraceTargetControlScript; // the script that controls the target on the circle
    private GameObject boundaryOfStabilityRenderer; //the renderer that draws the functional boundary of stability
    private RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript; // the script in the functional boundary of stability renderer
    public Camera mainCamera;
    private CameraSettingsController mainCameraSettingsControlScript;
    // The Vicon device data access object (this includes force plates and the analog sync pin)
    public GameObject forcePlateDataAccessObject;
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;

    // Coordinating with RobUST (or force field robot, generally)
    public GameObject computeStructureMatrixThisFrameServiceObject;
    private BuildStructureMatricesForBeltsThisFrameScript computeStructureMatrixScript;

    // END: Public GameObject variables and attached scripts*********************

    // Task  name
    private const string thisTaskNameString = "PacedCircleTrace";

    // states used to control program flow
    private string currentState;
    private const string waitingForSetupStateString = "WAITING_FOR_SETUP"; // Waiting for COM manager and other services. As float, let it be state 0.0f.
    private const string waitingForEmgReadyStateString = "WAITING_FOR_EMG_READY"; // We only enter this mode in setup if we're using EMGs (see flags)
    private const string waitingForHomePositionStateString = "WAITING_FOR_HOME_POSITION"; // Wait for player to move to home position for some time. state 1.0f.
    private const string activeBlockStateString = "ACTIVE_BLOCK"; // Player can chase the trace target. State 2.0f
    private const string waitingToSaveBlockDataToFileStateString = "WAITING_TO_SAVE_BLOCK_DATA"; // A state visited briefly while we let any residual COM/EMG data come in after a stop sync pulse.
    private const string gameOverStateString = "GAME_OVER"; // Game over. State 3.0f.


    //other logic flags that monitor trial progress
    private bool playerHasPassedZeroOnItsTripAroundTheCircle = false;

    //an array storing the travel directions per block, based on the public parameters chosen
    public bool[] travelingClockwiseEachBlock;

    // the circle center aka the "home" area properties
    private float circleCenterRadiusAsPercentOfLargerCircularTrajectoryRadius = 0.25f;
    private uint millisecondsRequiredAtHomeToStartNewBlock = 3000;
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
    private uint currentTrialNumber = 1;
    private uint currentBlockNumber = 0;

    // subject-specific data
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data

    // data recording
    public GameObject generalDataRecorder; //the GameObject that stores data and ultimately writes it to file (CSV)
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    private string subdirectoryName; //the string specifying the subdirectory (name) we'll be saving to in this session
    private string mostRecentFileNameStub; //the string specifying the .csv file save name for the frame, without the suffix specifying whether it's marker, frame, or trial data.

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

    // A float which chooses how close to draw our trace trajectory to the functional 
    // boundary of stability limits, as a percent of those limits measured
    // from the center of the base of support.
    public float scalingRatioPercentOfBoundaryOfStabiliy;

    // Base of support
    private float centerOfBaseOfSupportXPosViconFrame;
    private float centerOfBaseOfSupportYPosViconFrame;
    private float leftEdgeBaseOfSupportXPosInViconCoords;
    private float rightEdgeBaseOfSupportXPosInViconCoords;
    private float frontEdgeBaseOfSupportYPosInViconCoords;
    private float backEdgeBaseOfSupportYPosInViconCoords;

    // Excursion distances read from file (from THAT day's excursion trial)
    private float[] excursionDistancesPerDirectionViconFrame;


    // The tracing trajectory 
    private int numberOfPointsInTracingTrajectoryLessOne;
    private Vector3[] traceTrajectoryPointPositionsUnityCoords;  // The tracing trajectory points in Unity coordinates
    private float tracingTrajectoryWidthViconUnits;
    private float tracingTrajectoryHeightViconUnits;
    private float tracingTrajectoryPercentOfScreenToFill = 0.7f; //used in mapping from Vicon to Unity frame

    // define the ellipse to be traced by defining the width and heighth 
    // used in each quadrant. This allows for a better fit than a standard ellipse.
    private float quadrantOneWidthRadius;
    private float quadrantOneHeightRadius;
    private float quadrantTwoWidthRadius;
    private float quadrantTwoHeightRadius;
    private float quadrantThreeWidthRadius;
    private float quadrantThreeHeightRadius;
    private float quadrantFourWidthRadius;
    private float quadrantFourHeightRadius;

    // current direction of travel
    private bool currentlyTravelingClockwise; // whether the direction of travel is clockwise (true) or not (false)

    // The mapping function from Vicon frame to Unity frame variables.
    private float trajectoryAspectScalingFactor = 0.0f; // We scale the trajectory based on the ratio of base of support to screen aspect
    private float fitToScreenScalingFactor = 0.0f; // We also scale the trajectory to fill a certain percentage of the screen width or height
    private float rightwardsSign; // +1 if x-axes of Vicon and Unity are aligned, -1 if they are inverted
    private float forwardsSign; // +1 if y-axes of Vicon and Unity are aligned, -1 if they are inverted

    // Timing each lap
    private bool hasIndicatorMovedThisTrial = false; // A flag indicating whether or not the player has "pushed" the indicator yet this trial
    private float currentTrialStartTime; // When the current trial started, measured as when the indicator first moves that trial
    private float currentTrialEndTime; // When the current trial ends, measured as when the full lap is completed
    public float pacerDesiredLapTimeInSeconds; // How long the pacer should take to move around the trajectory, when in pacing mode.

    // Assigning points to each trial by integrating the area variables. 
    // We need to keep track of some player (COM) position variables from the last Unity frame in order to do the integration
    private float previousPlayerAngleAwayFromHomeInDegreesViconFrame; // angle the to player/COM in Vicon frame in the previous Unity frame (i.e. previous FixedUpdate() call)
    private float previousPlayerDistanceAwayFromCenterInViconUnitsMm; // distance from player/COM to center of base of support in Vicon frame in the previous Unity frame (i.e. previous FixedUpdate() call)
    private float minimumPlayerAngleChangeInDegreesForTrajectoryAreaIntegration = 0.5f; // the minimum angular change of the player/COM we integrate area for (in degrees)
    private float additionalAreaEnclosedByPlayerThisFrameViconFrame; // the area of the segment ("pie wedge") created by the player movement this frame
    private float additionalAreaErrorOfPlayerComThisFrame; // the error between the COM-enclosed pie wedge area and the ellipse pie wedge area this frame
    private float runningTotalAreaEnclosedByPlayerThisFrameViconFrame; // the total enclosed area of the player for this trial.                                                           
    private float runningTotalAreaErrorThisTrial = 0.0f; // The area error from the player to the ellipse. Integrated "pie wedges" for each frame. 
                                                         // This value is used to compute points for the player.
    private float tracingTrajectoryEnclosedAreaViconUnitsMm; // the total enclosed area of the tracing trajectory, in Vicon (real-world) units of mm^2. 
    private float maximumPercentEnclosedAreaErrorToStillEarnPoints = 0.4f; // How far off the area enclosed by the player this trial can be from the trajectory area to still
                                                                           // earn points. For example, 0.2f would mean if the area
                                                                           // enclosed by the player was off from the trajectory area by +/- 20%, points would be earned, 
                                                                           // but a greater proportional area error would not earn points. 
    private float maximumPointsEarnedPerTrial = 100.0f; // The maximum number of points the player can earn each trial.
    private float pointsEarnedByPlayerThisTrial; // The points earned by the player for this trial. 
    private float totalPointsEarnedByPlayerThisBlock = 0.0f; // The total points earned by the player this block.

    // Feedback, displaying the points earned to the user
    public GameObject FeedbackManagerGameObject;
    private ProvideOnscreenTextFeedbackScript feedbackManagerScript;

    // Communication with force field robot (Robust)
    public GameObject forceFieldRobotTcpServerObject;
    private CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;
    public bool communicateWithForceFieldRobot;

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
    private uint millisecondsDelayForStartEmgSyncSignal = 2000; // How long to wait between base station being armed for sync and actually
                                                                // sending the start sync signal (at minimum)
    private uint millisecondsDelayForStopEmgSyncSignal = 1000; // How long to wait after finishing a block to write data to file.
    private bool hasEmgSyncStartSignalBeenSentFlag = false; // A flag that is flipped to true when the EMG sync signal (and, thus, start data stream)
                                                            // was sent to the Delsys base station.
    private Stopwatch delayStartSyncSignalStopwatch = new Stopwatch(); // A stopwatch to add a small delay to sending our photon START sync signal
                                                                       // Seems necessary for Delsys base station.

    // For plotting performance curve for block
    public GameObject windowGraphCanvas; // The canvas containing the window graph for plotting error vs trial at the end.
    public WindowGraph windowGraphPlottingScript;
    private List<float> trialErrorAllTrials = new List<float>();


    // Start is called before the first frame update
    void Start()
    {

        // TESTING ONLY - set the fixed update rate to 100HZ
        //Time.fixedDeltaTime = 0.01f;

        // Set the initial state. We start in the waiting for home state 
        enterWaitingForSetupState();

        // Initialize the array that sets the travel direction for each block 
        travelingClockwiseEachBlock = new bool[numberOfBlocks];
        setBlockTravelDirections();

        // Retrieve the script from the circular trajectory object, get the circle trajectory radius
        circleRenderScript = circle.GetComponent<RenderCircle>();

        // Get the script that controls the circle trace target
        circleTraceTargetControlScript = circleTraceTarget.GetComponent<ControlCircleTraceTargetPaced>();

        // Get the script inside the functional boundary of stability renderer
        GameObject[] boundaryOfStabilityRenderers = GameObject.FindGameObjectsWithTag("BoundaryOfStability");
        if (boundaryOfStabilityRenderers.Length > 0) //if there are any boundary of stability renderers
        {
            boundaryOfStabilityRenderer = boundaryOfStabilityRenderers[0];
        }
        boundaryOfStabilityRendererScript = boundaryOfStabilityRenderer.GetComponent<RenderBoundaryOfStabilityScript>();

        // Get the script that computes the structure matrix for the current frame (for when using a cable-driven robot)
        computeStructureMatrixScript =
            computeStructureMatrixThisFrameServiceObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

        // marker data and center of mass manager
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // data saving
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // get the camera settings control script 
        mainCameraSettingsControlScript = mainCamera.GetComponent<CameraSettingsController>();

        // Get the communication with force field robot (e.g. RobUST) script
        forceFieldRobotTcpServerScript = forceFieldRobotTcpServerObject.GetComponent<CommunicateWithRobustLabviewTcpServer>();

        // Get a reference to the force plate data access script 
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();

        // If we're syncing with external hardware (typically the EMGs are included)
        if (streamingEmgDataFlag == true)
        {
            // Get a reference to the EMGs
            emgDataStreamerScript = emgDataStreamerObject.GetComponent<StreamAndRecordEmgData>();
        }

        // Get reference to Photon-based hardware sync object
        communicateWithPhotonScript = communicateWithPhotonViaSerialObject.GetComponent<CommunicateWithPhotonViaSerial>();

        // get the number of points in the trajectory from the game object storing the line renderer
        numberOfPointsInTracingTrajectoryLessOne = circleRenderScript.getNumberOfTrajectoryPoints() - 1;
        Debug.Log("Number of points in tracing trajectory will be: " + (numberOfPointsInTracingTrajectoryLessOne + 1));

        // get the feedback manager's script 
        feedbackManagerScript = FeedbackManagerGameObject.GetComponent<ProvideOnscreenTextFeedbackScript>();

        // Set the stimulation status.
        currentStimulationStatus = subjectSpecificDataScript.getStimulationStatusStringForFileNaming();
        Debug.Log("Before setting file naming, set the current stimulation status string to: " + currentStimulationStatus);

        //set the header names for the saved-out data CSV headers
        setFrameAndTrialDataNaming();

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

        // Hide the window graph
        windowGraphCanvas.SetActive(false);

        // Proceed by moving to the Waiting For Setup state
        changeActiveState(waitingForSetupStateString);

    }

    // We use Update(), not FixedUpdate(), to achieve higher execution rates.
    void Update()
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


                // Define the shape that will be traced and a mapping from Vicon to Unity that is 
                // unique to the tracing task. Send the shape to the rendering object to be rendered.
                Debug.Log("Input to useEllipse... centerBoSX_ViconFrame is: " + centerOfBaseOfSupportXPosViconFrame);
                Debug.Log("Input to useEllipse... centerBoSY_ViconFrame is: " + centerOfBaseOfSupportYPosViconFrame);
                Debug.Log("Input to useEllipse... excursion distance 0 is: " + excursionDistancesPerDirectionViconFrame[0]);
                useEllipseForTraceTrajectory(centerOfBaseOfSupportXPosViconFrame,
                    centerOfBaseOfSupportYPosViconFrame);

                //conduct setup that required the circle trajectory radius to be determined 
                setupAfterTrajectoryCreation();

                // Tell the boundary of stability renderer to draw the boundary of stability.
                // Based on the current working directory (by subject and date), load the functional boundary of 
                // stability based on the stored file within the Excursion folder. 
                // Note that the subject would have to have completed an Excursion test that day, or else the file must be 
                // manually copied into the proper folder location. 
                drawFunctionalBoundaryOfStability();

                // Send a command to Labview with the task-specific info string (subject number, date, time). 
                forceFieldRobotTcpServerScript.SendCommandWithCurrentTaskInfoString(mostRecentFileNameStub);

                Debug.Log("Level manager setup complete");

                // If we're syncing with external hardware (EMGs), we should move to a special state 
                // for EMG setup
                if (streamingEmgDataFlag == true)
                {
                    // then move to the waiting for EMG state
                    changeActiveState(waitingForEmgReadyStateString);
                }
                else // If not syncing with hardware, then move on to the active block
                {
                    // then proceed by moving to the Waiting For Setup state
                    changeActiveState(waitingForHomePositionStateString);
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
                // NOTE: we do NOT send the start data stream signal because 
                // we send that at the start of the block.
                // Also note that currently the EMG storing only works if we have only one block. 
                // FUTURE: could try to re-signal the delsys base station to start and stop for each block.
                // Likely it would require just setting the triggers, sending start, and then raising the trigger pin for each block.

                // Stop the stopwatch and reset it to zero
                delayStartSyncSignalStopwatch.Reset();

                // We've accomplished a minimum delay for sending the sync signal, so 
                if (currentBlockNumber == 0)
                {
                    // If we haven't started the first block, finish setup
                    changeActiveState(waitingForHomePositionStateString);
                }
                else
                {
                    // If we just finished a block, switch to the Active Block state
                    changeActiveState(activeBlockStateString);
                }
            }
        }

        else if (currentState == activeBlockStateString) // an active block is on (subject should be tracing ellipse)
        {
            bool trialCompleted = checkPlayerPositionAndUpdateTraceTarget();

            // Regardless of state, send the control point to the
            // force field robot, if it is being used
            if (communicateWithForceFieldRobot)
            {
                //forceFieldRobotTcpServerScript.SendUpdatedControlPointForForceFieldToRobot();
            }

            // store the new Unity frame rate data (e.g. trace trajectory position, on-screen player position + COM position for good measure)
            // for this frame
            storeFrameData();

            // Reset variables for the next frame
            additionalAreaEnclosedByPlayerThisFrameViconFrame = 0.0f;
            additionalAreaErrorOfPlayerComThisFrame = 0.0f;

            // If the trial was completed, increment the trial number and update the state accordingly
            if (trialCompleted)
            {
                // Assign a score to the trial, provide visual feedback
                assignTrialScoreAndProvideFeedback();

                //If the trial is completed, mark the end-time for the trial 
                currentTrialEndTime = Time.time;

                // Store the trial area error (total) in a List for plotting
                trialErrorAllTrials.Add(runningTotalAreaErrorThisTrial);

                // Store the trial data for the completed trial
                storeTrialData();

                // Reset variables for the next trial 
                Debug.Log("Area enclosed by player this trial in mm^2: " + runningTotalAreaEnclosedByPlayerThisFrameViconFrame);
                runningTotalAreaEnclosedByPlayerThisFrameViconFrame = 0.0f; // We reset the area enclosed by the player to zero
                runningTotalAreaErrorThisTrial = 0.0f;
                pointsEarnedByPlayerThisTrial = 0.0f;
                hasIndicatorMovedThisTrial = false;

                //increment the trial. This function changes the state and block number if appropriate.
                incrementTrialNumberAndManageState();
            }
        }
        else if (currentState == waitingForHomePositionStateString)
        {
            monitorIfPlayerIsInHomePosition();

            // Compute the player angle in Vicon frame, which we won't use in this state but would like to write to file for later use
            (Vector3 playerPositionInViconFrame, float playerXRelativeToCenterViconFrame, float playerYRelativeToCenterViconFrame,
                float currentPlayerAngleViconFrame) = computePlayerAngleInViconFrame();
            currentPlayerAngleInDegreesViconFrame = currentPlayerAngleViconFrame; // Store in instance variable

            // store the new Unity frame rate data (e.g. trace trajectory position, on-screen player position + COM position for good measure)
            // for this frame
            storeFrameData();

            // If the player has been at home long enough
            if (timeAtHomeStopwatch.IsRunning && (timeAtHomeStopwatch.ElapsedMilliseconds > millisecondsRequiredAtHomeToStartNewBlock))
            {
                //Either they are starting the first block or ending a block.
                if (currentBlockNumber != 0) // if they are ending a block (not starting the first block)
                {
                    // the current block is over, so send a stop signal to the external hardware 
                    // (only if using external hardware)
                    if (syncingWithExternalHardwareFlag)
                    {
                        // Stop the EMG data because we have finished the block 
                        // Sends stop to both EMGS and Vicon link box.
                        communicateWithPhotonScript.tellPhotonToPulseSyncStopPin();
                    }

                    // Restart the stopwatch that will enforce a minimum time until we save the data to file. 
                    // This allows any data that's still streaming (even though we sent the stop signal, there is latency) 
                    // to come in before saving it to file.
                    delayStartSyncSignalStopwatch.Restart();

                    // Transition to the Waiting to Save Block Data state
                    changeActiveState(waitingToSaveBlockDataToFileStateString);

                }
                else
                {
                    //start the next block by entering the Active Block state
                    changeActiveState(activeBlockStateString);
                }
            }
        }
        else if (currentState == waitingToSaveBlockDataToFileStateString)
        {
            if (delayStartSyncSignalStopwatch.IsRunning &&
                delayStartSyncSignalStopwatch.ElapsedMilliseconds > millisecondsDelayForStopEmgSyncSignal)
            {
                // Write the block data to file
                tellDataRecorderToWriteStoredDataToFile();

                // Plot the trial error data vs. trial number in the plotting window
                float plottingWindowTimeStep = 1.0f; // 1 because it is trial number
                windowGraphCanvas.SetActive(true); // show the window graph
                windowGraphPlottingScript.PlotDataPointsOnWindowGraph(trialErrorAllTrials, plottingWindowTimeStep);

                // The data has been saved to file, so clear it from memory, 
                // as we're writing each block to its own file
                generalDataRecorderScript.clearMarkerAndFrameAndTrialData();


                // Since we already incremented the block number, update the file names for 
                // trial and frame data saving to include the new block number.
                setFileNamesForCurrentBlockTrialAndFrameData();

                // Reset the needed variables so that the EMG data streamer can set up the base station 
                // for the next block
                //emgDataStreamerScript.ResetVariablesToEnableResettingBaseStation();

                // Stop the stopwatch and reset it to zero
                delayStartSyncSignalStopwatch.Reset();

                // if more blocks remain
                if (currentBlockNumber < numberOfBlocks)
                {
                    //start the next block by entering the Active Block state
                    changeActiveState(activeBlockStateString);
                }
                else // else if there are no more blocks remaining
                {
                    //transition to the game over state 
                    changeActiveState(gameOverStateString);
                }
            }
        }
        else if (currentState == gameOverStateString)
        {
            //do nothing in the game over state
        }
    }




    // BEGIN: Mapping from Vicon to Unity frame and back functions*********************************************************************************

    private void defineMappingFromViconFrameToUnityFrameAndBack()
    {
        // Choose the first part of the scaling factor, based on tracing trajectory 
        // aspect ratio
        float tracingTrajectoryAspectRatio = (tracingTrajectoryWidthViconUnits / tracingTrajectoryHeightViconUnits);
        float ratioOfTrajectoryToScreenAspectRatios = tracingTrajectoryAspectRatio / mainCameraSettingsControlScript.getAspectRatio();

        // Get the screen height and width in Unity frame
        float screenWidthInUnityCoords = (mainCamera.ViewportToWorldPoint(new Vector3(1, 0, 0)) - mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0))).x;
        float screenHeightInUnityCoords = (mainCamera.ViewportToWorldPoint(new Vector3(0, 1, 0)) - mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0))).y;

        // the scaling will depend on this ratio of aspect ratios
        if (ratioOfTrajectoryToScreenAspectRatios >= 1.0f) //if the ratio is greater than 1, the tracing trajectory
                                                           //is relatively wide, so we normalize by that dimension
        {

            Debug.Log("Mapping from Vicon to Unity based on width of trajectory");

            //normalize by width of tracing trajectory
            trajectoryAspectScalingFactor = 1.0f / tracingTrajectoryWidthViconUnits;

            // We also want to scale the shape to take up a certain percent of the screen
            fitToScreenScalingFactor = tracingTrajectoryPercentOfScreenToFill * screenWidthInUnityCoords;
        }
        else
        {
            Debug.Log("Mapping from Vicon to Unity based on height of trajectory");

            //normalize by height of tracing trajectory
            trajectoryAspectScalingFactor = 1.0f / tracingTrajectoryHeightViconUnits;

            // We also want to scale the shape to take up a certain percent of the screen
            fitToScreenScalingFactor = tracingTrajectoryPercentOfScreenToFill * screenHeightInUnityCoords;

        }
    }

    public override String GetCurrentTaskName()
    {
        return thisTaskNameString;
    }


    // THE MAPPING FUNCTION from Vicon frame to Unity frame for the trajectory-tracing task (this task). 
    // Key point: the center of the base of support maps to (0,0) in Unity for this task.
    // Key point 2: the x- and y-axis coordinates are multiplied by -1 in the mapping, 
    // because the Unity frame is rotated 180 degrees relative to the Vicon frame (assuming the axes are actually parallel...) 

    // Really, we should multiply by a rotation vector to account for flipping of axes (or misalignment, although we assume perfect alignment), 
    // then multiply by a scaling matrix (identity matrix times some constant). 
    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        // Carry out the mapping from Vicon frame to Unity frame
        float pointInUnityFrameX = rightwardsSign * trajectoryAspectScalingFactor * fitToScreenScalingFactor
            * (pointInViconFrame.x - centerOfBaseOfSupportXPosViconFrame);

        float pointInUnityFrameY = forwardsSign * trajectoryAspectScalingFactor * fitToScreenScalingFactor
            * (pointInViconFrame.y - centerOfBaseOfSupportYPosViconFrame);

        //return the point in Unity frame
        return new Vector3(pointInUnityFrameX, pointInUnityFrameY, player.transform.position.z);
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // Carry out the mapping from Vicon frame to Unity frame
        float pointInViconFrameX = (rightwardsSign / (trajectoryAspectScalingFactor * fitToScreenScalingFactor)) * pointInUnityFrame.x +
            centerOfBaseOfSupportXPosViconFrame;

        float pointInViconFrameY = (forwardsSign / (trajectoryAspectScalingFactor * fitToScreenScalingFactor)) * pointInUnityFrame.y +
            centerOfBaseOfSupportYPosViconFrame;

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


    public override bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }



    private (float, float) convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(float widthXAxisViconFrame, float heightYAxisViconFrame)
    {
        float widthInUnityFrameX = trajectoryAspectScalingFactor * fitToScreenScalingFactor * widthXAxisViconFrame;

        float heightInUnityFrameY = trajectoryAspectScalingFactor * fitToScreenScalingFactor * heightYAxisViconFrame;

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

    Function: setBlockTravelDirections()

    Parameters: none

    Returns: none

    Description: Creates a list of travel directions per trial, either clockwise or counterclockwise. 
                 Set up so that the direction alternates from trial to trial. 

    Notes: Called only once, a setup function

    **************************/
    private void setBlockTravelDirections()
    {
        bool evenBlockNumberValue = startWithClockwiseBlockFlag;
        for (uint blockIndex = 0; blockIndex < travelingClockwiseEachBlock.Length; blockIndex++)
        {
            if ((blockIndex % 2) == 0)
            {
                travelingClockwiseEachBlock[blockIndex] = evenBlockNumberValue;
            }
            else
            {
                travelingClockwiseEachBlock[blockIndex] = !evenBlockNumberValue;
            }
        }
    }

    /************************

    Function: useEllipseForTraceTrajectory()

    Parameters: 
        - float centerOfBaseOfSupportXPosViconFrame: the x-coordinate of the center of the base of support as measured in Vicon frame [mm]
        - float centerOfBaseOfSupportYPosViconFrame: the y-coordinate of the center of the base of support as measured in Vicon frame [mm]

    Returns: none

    Description: Defines a compound ellipse as the tracing trajectory for the subject to move along. 
                    Defines the ellipse based on the excursion distances measured in the Excursion task. 
                    Defines the ellipse in Vicon frame, transforms the ellipse points into Unity coordinates, 
                    and sends these points to the trajectory rendering object to be displayed on-screen. 

    Notes: Called only once, a setup function

    **************************/
    private void useEllipseForTraceTrajectory(float centerOfBaseOfSupportXPosViconFrame,
        float centerOfBaseOfSupportYPosViconFrame)
    {
        // Define the horizontal and vertical radius for each quadrant
        // These are in Vicon units [mm] and defined in a helper function.
        defineCompoundEllipseQuadrantDimensions();

        // Set the variables measuring the tracing trajectory width and height. These are needed to fit the 
        // trajectory to the screen 
        tracingTrajectoryWidthViconUnits = (quadrantOneWidthRadius + quadrantTwoWidthRadius);
        tracingTrajectoryHeightViconUnits = (quadrantOneHeightRadius + quadrantThreeHeightRadius);

        // Determine if there is axis-flipping in the Vicon to Unity mapping (equivalent to a rotation matrix with axes either aligned or flipped)
        rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        forwardsSign = Mathf.Sign(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);

        // Define the constants that determine the mapping from Vicon frame to Unity frame (and back)
        defineMappingFromViconFrameToUnityFrameAndBack();

        // Next, generate the points of the trajectory in Vicon (real world) frame
        Vector3[] traceTrajectoryPointPositionsUnityCoords = new Vector3[numberOfPointsInTracingTrajectoryLessOne + 1];  // stores
        for (uint index = 0; index <= numberOfPointsInTracingTrajectoryLessOne; index++)
        {
            float indexAsFloat = (float)index;
            float radiansOfCurrentIndex = (indexAsFloat / numberOfPointsInTracingTrajectoryLessOne) * 2 * Mathf.PI;
            uint quadrant = getQuadrantFromAngle(radiansOfCurrentIndex);
            (float ellipseWidth, float ellipseHeight) = getEllipseHeightAndWidthBasedOnQuadrant(quadrant);
            float radiusAtCurrentAngleViconFrame = (ellipseWidth * ellipseHeight) / Mathf.Sqrt(Mathf.Pow(ellipseHeight * Mathf.Cos(radiansOfCurrentIndex), 2.0f)
                + Mathf.Pow(ellipseWidth * Mathf.Sin(radiansOfCurrentIndex), 2.0f));
            float xCoordinateOnTrajectory = centerOfBaseOfSupportXPosViconFrame + radiusAtCurrentAngleViconFrame * Mathf.Cos(radiansOfCurrentIndex);
            float yCoordinateOnTrajectory = centerOfBaseOfSupportYPosViconFrame + radiusAtCurrentAngleViconFrame * Mathf.Sin(radiansOfCurrentIndex);
            Vector3 trajectoryPointPositionViconCoords = new Vector3(xCoordinateOnTrajectory, yCoordinateOnTrajectory, 0);

            Debug.Log("Ellipse creation - (angle rads, width, height, radius): ( "
                + radiansOfCurrentIndex + ", "
                + ellipseWidth + ", "
                + ellipseHeight + ", "
                + radiusAtCurrentAngleViconFrame + ")");

            // Map those points to a coordinate in Unity (using a uniform scaling to preserve similarity)
            Vector3 trajectoryPointInUnityFrame = mapPointFromViconFrameToUnityFrame(trajectoryPointPositionViconCoords);

            // Store in the array
            traceTrajectoryPointPositionsUnityCoords[index] = trajectoryPointInUnityFrame;
        }

        // Send the points to the trajectory renderer, which will set them as the control points in the 
        // line renderer for the trajectory
        circleRenderScript.setPointsOfTrajectory(traceTrajectoryPointPositionsUnityCoords);

        // Compute the enclosed area of the ellipse to be traced. We can use this and the area enclosed by the player on each trial 
        // to assign points
        computeAreaOfCompoundEllipseTracingTrajectory();

    }


    private void defineCompoundEllipseQuadrantDimensions()
    {
        if (rightwardsSign > 0) // if Unity and Vicon axes are aligned, then 0,1,2,3,4,5,6,7 indices from the excursion distances
                                //start to the person's right and proceed CCW. 
        {
            // Define the horizontal and vertical radius for each quadrant
            // These are all in Vicon units [mm].
            quadrantOneWidthRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[0];
            quadrantOneHeightRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[2];
            quadrantTwoWidthRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[4];
            quadrantTwoHeightRadius = quadrantOneHeightRadius; //scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[2];
            quadrantThreeWidthRadius = quadrantTwoWidthRadius; //scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[0];
            quadrantThreeHeightRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[6];
            quadrantFourWidthRadius = quadrantOneWidthRadius; // scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[0];
            quadrantFourHeightRadius = quadrantThreeHeightRadius; //scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[2];
        }
        else
        // else, Vicon's +x-axis is to the person's left. Since the task is defined in Vicon frame, quadrant one is now the person's back left. 
        // We still proceed counterclockwise.
        {
            quadrantOneWidthRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[4];
            quadrantOneHeightRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[6];
            quadrantTwoWidthRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[0];
            quadrantTwoHeightRadius = quadrantOneHeightRadius; //scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[2];
            quadrantThreeWidthRadius = quadrantTwoWidthRadius; //scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[0];
            quadrantThreeHeightRadius = scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[2];
            quadrantFourWidthRadius = quadrantOneWidthRadius; // scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[0];
            quadrantFourHeightRadius = quadrantThreeHeightRadius; //scalingRatioPercentOfBoundaryOfStabiliy * excursionDistancesPerDirectionViconFrame[2];
        }
    }

    private void computeAreaOfCompoundEllipseTracingTrajectory()
    {
        // The area of an ellipse with major-axis radius a and minor-axis radius b is just pi*a*b.
        // Compute the total area of our compound ellipse by dividing this by four for each quadrant.
        float quadrantOneArea = (Mathf.PI * quadrantOneWidthRadius * quadrantOneHeightRadius) / (4.0f);
        float quadrantTwoArea = (Mathf.PI * quadrantTwoWidthRadius * quadrantTwoHeightRadius) / (4.0f);
        float quadrantThreeArea = (Mathf.PI * quadrantThreeWidthRadius * quadrantThreeHeightRadius) / (4.0f);
        float quadrantFourArea = (Mathf.PI * quadrantFourWidthRadius * quadrantFourHeightRadius) / (4.0f);

        tracingTrajectoryEnclosedAreaViconUnitsMm = quadrantOneArea + quadrantTwoArea + quadrantThreeArea + quadrantFourArea;

    }



    // Given an angle (measured CCW from +x-axis), return an integer representing the quadrant (1-4). 
    // We define upper right as quadrant 1 and proceed CCW. 
    public uint getQuadrantFromAngle(float angleInRadians)
    {
        // Take the modulus of the angle and 2*pi, since we want to express the angle as 
        // between 0 and 2*pi
        angleInRadians = angleInRadians % (2 * Mathf.PI);

        //determine the quadrant
        uint quadrantIndex = 100; //set a meaningless initial value
        if ((angleInRadians >= 0) && (angleInRadians < (Mathf.PI / 2)))
        {
            quadrantIndex = 1;
        }
        else if ((angleInRadians >= (Mathf.PI / 2)) && (angleInRadians < Mathf.PI))
        {
            quadrantIndex = 2;
        }
        else if ((angleInRadians >= Mathf.PI) && (angleInRadians < ((3.0f / 2.0f) * Mathf.PI)))
        {
            quadrantIndex = 3;
        }
        else if ((angleInRadians >= ((3.0f / 2.0f) * Mathf.PI)) && (angleInRadians < (2.0f * Mathf.PI)))
        {
            quadrantIndex = 4;
        }
        else
        {
            quadrantIndex = 100; //an error case, impossible
        }

        // DEBUG: add this print such that it prints if a debugging flag is active
        //Debug.Log("Getting quadrant for angle (radians): " + angleInRadians + "and got quadrant " + quadrantIndex);


        return quadrantIndex;
    }




    private float getCompoundEllipseRadiusInViconUnitsOfMmGivenAngleInRadians(float angleInRadians)
    {
        // First get the quadrant
        uint quadrant = getQuadrantFromAngle(angleInRadians);

        // Then get the ellipse width and height in that quadrant
        (float ellipseWidthViconUnitsMm, float ellipseHeightViconUnitsMm) = getEllipseHeightAndWidthBasedOnQuadrant(quadrant);

        // Then get the (x,y) coordinate of the tracing trajectory at that angle from the +x-axis
        float radiusAtCurrentAngleViconFrame = (ellipseWidthViconUnitsMm * ellipseHeightViconUnitsMm) /
            Mathf.Sqrt(Mathf.Pow(ellipseHeightViconUnitsMm * Mathf.Cos(angleInRadians), 2.0f)
            + Mathf.Pow(ellipseWidthViconUnitsMm * Mathf.Sin(angleInRadians), 2.0f));

        // Return the radius. Note that it's in Vicon units of mm.
        return radiusAtCurrentAngleViconFrame;
    }




    public (float, float) getEllipseHeightAndWidthBasedOnQuadrant(uint quadrant)
    {
        //get the correct height and width for the quadrant (note, these are all in Vicon frame units of mm)
        if (quadrant == 1)
        {
            return (quadrantOneWidthRadius, quadrantOneHeightRadius);
        }
        else if (quadrant == 2)
        {
            return (quadrantTwoWidthRadius, quadrantTwoHeightRadius);
        }
        else if (quadrant == 3)
        {
            return (quadrantThreeWidthRadius, quadrantThreeHeightRadius);
        }
        else if (quadrant == 4)
        {
            return (quadrantFourWidthRadius, quadrantFourHeightRadius);
        }
        else
        {
            Debug.LogError("Specified a non-existent quadrant, quadrant " + quadrant);
            return (0.0f, 0.0f);
        }
    }

    // Accepts an angle in radians (from the +x-axis in Vicon frame! not Unity frame!)
    // and outputs the Unity tracing trajectory (x,y) coordinates at that angle from 
    // the +x-axis.
    // Because of the conversion, we pass the computed point in Vicon frame through the mapping function
    // to get a Unity point.
    private (Vector3, Vector3) getUnityCoordinateForEllipseTrajectoryGivenAngleFromXAxis(float radiansOfAngleInViconFrame)
    {

        // Determine if there is axis-flipping (equivalent to a rotation matrix with axes either aligned or flipped)
        float rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
        float forwardsSign = Mathf.Sign(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);

        // Get the current quadrant to determine the ellipse properties
        uint quadrant = getQuadrantFromAngle(radiansOfAngleInViconFrame);
        (float ellipseWidthViconUnitsMm, float ellipseHeightViconUnitsMm) = getEllipseHeightAndWidthBasedOnQuadrant(quadrant);

        // Given the passed-in angle, get the (x,y) coordinate of the tracing trajectory at that angle from the +x-axis
        float radiusAtCurrentAngleViconFrame = (ellipseWidthViconUnitsMm * ellipseHeightViconUnitsMm) /
            Mathf.Sqrt(Mathf.Pow(ellipseHeightViconUnitsMm * Mathf.Cos(radiansOfAngleInViconFrame), 2.0f)
            + Mathf.Pow(ellipseWidthViconUnitsMm * Mathf.Sin(radiansOfAngleInViconFrame), 2.0f));
        float xCoordinateOnTrajectory = centerOfBaseOfSupportXPosViconFrame + radiusAtCurrentAngleViconFrame * Mathf.Cos(radiansOfAngleInViconFrame);
        float yCoordinateOnTrajectory = centerOfBaseOfSupportYPosViconFrame + radiusAtCurrentAngleViconFrame * Mathf.Sin(radiansOfAngleInViconFrame);

        Vector3 trajectoryPointPositionViconCoords = new Vector3(xCoordinateOnTrajectory, yCoordinateOnTrajectory, 0);

        // Map those points to a coordinate in Unity (using a uniform scaling to preserve similarity)
        Vector3 trajectoryPointInUnityFrame = mapPointFromViconFrameToUnityFrame(trajectoryPointPositionViconCoords);

        // Return the point on the trajectory that corresponds to the angle from +x-axis
        return (trajectoryPointInUnityFrame, trajectoryPointPositionViconCoords);
    }



    private void usePiecewiseFunctionForTraceTrajectory(float centerOfBaseOfSupportXPos, float centerOfBaseOfSupportYPos)
    {
        // First, fit the polynomial radius function to the excursion limits and a scaling factor
        fitPiecewisePolynomialRadialFunction();
    }



    private void fitPiecewisePolynomialRadialFunction()
    {

    }



    private void setupAfterTrajectoryCreation()
    {
        //circleRadiusInWorldUnits = circleRenderScript.getCircleRadiusInWorldUnits();

        // Determine which is hte shortest dimension, bounds of stability in the x-axis/ML direction 
        // or y-axis/AP direction
        float shortestDimensionViconUnits;
        if (tracingTrajectoryWidthViconUnits <= tracingTrajectoryHeightViconUnits)
        {
            shortestDimensionViconUnits = tracingTrajectoryWidthViconUnits;
            Debug.Log("Shortest dimension of traced trajectory is width, with dimension in mm: " + shortestDimensionViconUnits);

            // set the central circle radius in Vicon units
            circleInCenterOfTrajectoryDiameterViconUnits = shortestDimensionViconUnits * circleCenterRadiusAsPercentOfLargerCircularTrajectoryRadius;
            Debug.Log("Circle center: diameter in Vicon units is " + circleInCenterOfTrajectoryDiameterViconUnits);
            Debug.Log("Circle center: mapping traj scaling factor and screen scaling are ( " + trajectoryAspectScalingFactor + ", " + fitToScreenScalingFactor + ")");

            //convert the radius in Vicon units to Unity units
            Vector3 test = mapPointFromViconFrameToUnityFrame(new Vector3(centerOfBaseOfSupportXPosViconFrame + rightwardsSign * circleInCenterOfTrajectoryDiameterViconUnits, 0.0f, 0.0f));
            (circleInCenterOfTrajectoryDiameterUnityUnits, _) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(circleInCenterOfTrajectoryDiameterViconUnits, 0.0f);
            Debug.Log("Circle center: scaling factor is " + circleInCenterOfTrajectoryDiameterUnityUnits);
        }
        else
        {
            shortestDimensionViconUnits = tracingTrajectoryHeightViconUnits;
            Debug.Log("Circle center: Shortest dimension of traced trajectory is height, with dimension in mm: " + shortestDimensionViconUnits);

            // set the central circle radius in Vicon units
            circleInCenterOfTrajectoryDiameterViconUnits = shortestDimensionViconUnits * circleCenterRadiusAsPercentOfLargerCircularTrajectoryRadius;
            Debug.Log("Circle center: diameter in Vicon units is " + circleInCenterOfTrajectoryDiameterViconUnits);
            Debug.Log("Circle center: mapping traj scaling factor and screen scaling are ( " + trajectoryAspectScalingFactor + ", " + fitToScreenScalingFactor + ")");

            //convert the radius in Vicon units to Unity units
            Vector3 test = mapPointFromViconFrameToUnityFrame(new Vector3(centerOfBaseOfSupportXPosViconFrame + rightwardsSign * circleInCenterOfTrajectoryDiameterViconUnits, 0.0f, 0.0f));
            (_, circleInCenterOfTrajectoryDiameterUnityUnits) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(0.0f, circleInCenterOfTrajectoryDiameterViconUnits);
            Debug.Log("Circle center: scaling factor is " + circleInCenterOfTrajectoryDiameterUnityUnits);
        }

        // Save the circle center position into its own variable for readability.
        circleCenterPosition = circleCenter.transform.position;

        //Set the radius of the circle center / "home" area.
        circleCenter.transform.localScale = new Vector3(circleInCenterOfTrajectoryDiameterUnityUnits,
            circleInCenterOfTrajectoryDiameterUnityUnits,
            circleCenter.transform.localScale.z);

        // Since we now know if the Unity and Vicon frame are flipped 180 from each other, we need to define the trace 
        // trajectory starting angle in the Vicon frame
        if (rightwardsSign < 0)
        {
            circleTraceTargetStartAngleClockwiseViconFrame = convertAngleFromViconFrameToUnityFrameOrViceVersa(circleTraceTargetStartAngleClockwise);
            circleTraceTargetStartAngleCounterclockwiseViconFrame = convertAngleFromViconFrameToUnityFrameOrViceVersa(circleTraceTargetStartAngleCounterclockwise);
        }
        else
        {
            circleTraceTargetStartAngleClockwiseViconFrame = circleTraceTargetStartAngleClockwise;
            circleTraceTargetStartAngleCounterclockwiseViconFrame = circleTraceTargetStartAngleCounterclockwise;

        }

        //set the indicator to indicate the direction of travel
        if (!currentlyTravelingClockwise)
        { //if traveling clockwise
            setCircleTraceTargetAngle(circleTraceTargetStartAngleCounterclockwiseViconFrame);
        }
        else //if traveling clockwise
        {
            setCircleTraceTargetAngle(circleTraceTargetStartAngleClockwiseViconFrame);

        }
    }

    public float ResetTraceTargetToStartingPosition()
    {
        // Initialize the initial angle in Vicon frame
        float startingAngleViconFrameInRadians = 0.0f;
        //set the indicator to indicate the direction of travel
        if (!currentlyTravelingClockwise)
        { //if traveling clockwise
            setCircleTraceTargetAngle(circleTraceTargetStartAngleCounterclockwiseViconFrame);
            startingAngleViconFrameInRadians = circleTraceTargetStartAngleCounterclockwiseViconFrame;
        }
        else //if traveling clockwise
        {
            setCircleTraceTargetAngle(circleTraceTargetStartAngleClockwiseViconFrame);
            startingAngleViconFrameInRadians = circleTraceTargetStartAngleClockwiseViconFrame;
        }

        return (startingAngleViconFrameInRadians * (Mathf.PI / 180.0f)); // convert from degrees to radians
    }

    public bool GetIsThisTrialCounterClockwiseFlag()
    {
        if (!currentlyTravelingClockwise)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool IsLevelManagerSetupComplete()
    {
        if(currentState == activeBlockStateString || currentState == waitingForHomePositionStateString)
        {
            return true;
        }
        else
        {
            return false;
        }
    }



    public void setCircleTraceTargetAngle(float desiredAngleToTargetInViconFrame)
    {
        float desiredAngleToTargetInRadians = desiredAngleToTargetInViconFrame * (Mathf.PI / 180.0f);

        (Vector3 newIndicatorPositionUnityFrame, Vector3 indicatorPositionViconFrame) = getUnityCoordinateForEllipseTrajectoryGivenAngleFromXAxis(desiredAngleToTargetInRadians);

        //float circleTargetXPos = circleCenterPosition.x + circleRadiusInWorldUnits * Mathf.Cos(desiredAngleToTargetInRadians);
        //float circleTargetYPos = circleCenterPosition.y + circleRadiusInWorldUnits * Mathf.Sin(desiredAngleToTargetInRadians);
        //Vector3 newTargetPosition = new Vector3(circleTargetXPos, circleTargetYPos, circleTraceTarget.transform.position.z);
        circleTraceTargetControlScript.SetPosition(newIndicatorPositionUnityFrame);

        //Also, set the current trace target angle and position in the Vicon frame, which should drive the movement
        currentTraceTargetAngleInDegreesViconFrame = desiredAngleToTargetInViconFrame;
        currentTraceIndicatorPositionViconFrame = indicatorPositionViconFrame;
    }



    private void setFrameAndTrialDataNaming()
    {
        // 1.) Frame data naming
        // A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "TIME_AT_UNITY_FRAME_START_SYNC_SENT",
            "SYNC_PIN_ANALOG_VOLTAGE", "COM_POS_X","COM_POS_Y", "COM_POS_Z", "IS_COM_POS_FRESH_FLAG",
           "MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER", "BLOCK_NUMBER", "TRIAL_NUMBER",
           "PLAYER_ANGLE_RADIANS", "PLAYER_POS_X", "PLAYER_POS_Y", "PLAYER_ANGLE_RADIANS_VICON_FRAME", "PLAYER_POS_VICON_FRAME_X",
           "PLAYER_POS_VICON_FRAME_Y", "DIRECTION", "CIRCLE_INDICATOR_ANGLE_RADIANS_UNITY",
           "CIRCLE_INDICATOR_POS_X_UNITY", "CIRCLE_INDICATOR_POS_Y_UNITY", "CIRCLE_INDICATOR_ANGLE_RADIANS_VICON", "CIRCLE_INDICATOR_POS_X_VICON",
           "CIRCLE_INDICATOR_POS_Y_VICON", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT", "IS_PLAYER_AT_HOME_FLAG",
           "ADDITIONAL_AREA_ENCLOSED_BY_PLAYER_THIS_FRAME_VICON_UNITS_MM2",
           "AREA_ERROR_OF_PLAYER_COM_THIS_FRAME_VICON_UNITS_MM2"};

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // 2.) Trial data naming
        // A string array with all of the header names
        string[] csvTrialDataHeaderNames = new string[]{"BLOCK_NUMBER", "TRIAL_NUMBER", "DIRECTION", "CIRCLE_CENTER_POS_X_UNITY_FRAME",
            "CIRCLE_CENTER_POS_Y_UNITY_FRAME", "CIRCLE_CENTER_DIAMETER_VICON_MM", "CIRCLE_CENTER_DIAMETER_UNITY",
            "TRACE_INDICATOR_CCW_STARTING_ANGLE_UNITY_FRAME", "TRACE_INDICATOR_CW_STARTING_ANGLE_UNITY_FRAME",
            "TRACE_INDICATOR_CCW_STARTING_ANGLE_VICON_FRAME", "TRACE_INDICATOR_CW_STARTING_ANGLE_VICON_FRAME",
            "SCALING_RATIO_FOR_TRACING_TRAJECTORY_AS_FRACTION_OF_EXCURSION", "LEFT_BASEOFSUPPORT_VICON_POS_X",
            "RIGHT_BASE_OF_SUPPORT_VICON_POS_X", "FRONT_BASEOFSUPPORT_VICON_POS_Y", "BACK_BASEOFSUPPORT_VICON_POS_Y", "BASE_OF_SUPPORT_CENTER_X",
            "BASE_OF_SUPPORT_CENTER_Y", "EXCURSION_DISTANCE_DIR_0_VICON_MM", "EXCURSION_DISTANCE_DIR_1_VICON_MM",
            "EXCURSION_DISTANCE_DIR_2_VICON_MM", "EXCURSION_DISTANCE_DIR_3_VICON_MM", "EXCURSION_DISTANCE_DIR_4_VICON_MM",
            "EXCURSION_DISTANCE_DIR_5_VICON_MM", "EXCURSION_DISTANCE_DIR_6_VICON_MM", "EXCURSION_DISTANCE_DIR_7_VICON_MM",
            "QUADRANT_ONE_VICON_FRAME_WIDTH", "QUADRANT_ONE_VICON_FRAME_HEIGHT", "QUADRANT_TWO_VICON_FRAME_WIDTH",
            "QUADRANT_TWO_VICON_FRAME_HEIGHT", "QUADRANT_THREE_VICON_FRAME_WIDTH", "QUADRANT_THREE_VICON_FRAME_HEIGHT",
            "QUADRANT_FOUR_VICON_FRAME_WIDTH", "QUADRANT_FOUR_VICON_FRAME_HEIGHT", "UNITY_VICON_MAPPING_FCN_BOS_SCREEN_ASPECT_RATIO_SCALER",
            "UNITY_VICON_MAPPING_FCN_FILL_SCREEN_PERCENTAGE_SCALER", "UNITY_VICON_MAPPING_FCN_RIGHTWARD_SIGN_AXIS_FLIP",
            "UNITY_VICON_MAPPING_FCN_FORWARD_SIGN_AXIS_FLIP", "STIMULATION_STATUS", "TRIAL_START_TIME_SECONDS","TRIAL_END_TIME_SECONDS",
            "TRIAL_DURATION_SECONDS", "TOTAL_AREA_ENCLOSED_BY_PLAYER_COM_THIS_TRIAL_VICON_UNITS_MM2",
            "TOTAL_AREA_ERROR_OF_PLAYER_COM_THIS_TRIAL_VICON_UNITS_MM2", "POINTS_EARNED_THIS_TRIAL"};

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
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "PacedCircleTrace" + "/" + dateString + "/";

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
        string fileNameStub = "CircleTrace" + delimiter + subjectSpecificInfoString + delimiter + currentStimulationStatus + delimiter + dateAndTimeString + delimiter + blockNumberAsString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the task-specific trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);

        // Set the EMG data file name
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
            else if (currentState == waitingForHomePositionStateString)
            {
                exitWaitingForHomeState();
            }
            else if (currentState == activeBlockStateString)
            {
                exitActiveBlockState();
            }
            else if (currentState == waitingForSetupStateString)
            {
                exitWaitingForSetupState();
            }
            else if (currentState == waitingToSaveBlockDataToFileStateString)
            {
                exitWaitingToSaveBlockDataToFileState();
            }

            //then call the entry function for the new state
            if (newState == waitingForEmgReadyStateString)
            {
                enterWaitingForEmgReadyState();
            }
            else if (newState == waitingForSetupStateString)
            {
                enterWaitingForSetupState();
            }
            else if (newState == waitingForHomePositionStateString)
            {
                enterWaitingForHomeState();
            }
            else if (newState == activeBlockStateString)
            {
                enterActiveBlockState();
            }
            else if (newState == gameOverStateString)
            {
                enterGameOverState();
            }
            else if (newState == waitingToSaveBlockDataToFileStateString)
            {
                enterWaitingToSaveBlockDataToFileState();
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

    private void enterWaitingForSetupState()
    {
        //set the current state
        currentState = waitingForSetupStateString;
    }

    private void exitWaitingForSetupState()
    {
        //nothing needs to happen 
    }

    private void enterWaitingForHomeState()
    {

        Debug.Log("Changing to Waiting for Home state");
        //change the current state to the Waiting For Home state
        currentState = waitingForHomePositionStateString;

        // Sync the external hardware at the start of a bout of quiet standing
        // (if we're syncing with external hardware (e.g. EMGs))
        if (syncingWithExternalHardwareFlag == true)
        {

            communicateWithPhotonScript.tellPhotonToPulseSyncStartPin();

            // Store the new time at which we sent the Photon start sync 
            unityFrameTimeAtWhichHardwareSyncSent = Time.time;
        }

        //set the center circle color to the color indicating the player should move there
        circleCenter.GetComponent<Renderer>().material.color = Color.red;

        //make the circle trajectory indicator invisible/inactive
        circleTraceTarget.SetActive(false);

    }


    private void exitWaitingForHomeState()
    {
        //set the center circle color to its default
        circleCenter.GetComponent<Renderer>().material.color = Color.blue;

        //reset and stop the stop watch
        timeAtHomeStopwatch.Reset();
    }

    private void enterActiveBlockState()
    {
        Debug.Log("Changing to Enter Active Block state");

        //change the current state to the Active Block state
        currentState = activeBlockStateString;

        //determine if this block will be clockwise or counterclockwise
        currentlyTravelingClockwise = travelingClockwiseEachBlock[currentBlockNumber];

        //reactivate the indicator so that it is visible
        circleTraceTarget.SetActive(true);

        // reset the indicator position to the correct location given the direction of travel
        if (currentlyTravelingClockwise == true)
        {
            setCircleTraceTargetAngle(circleTraceTargetStartAngleClockwiseViconFrame);
        }
        else
        {
            setCircleTraceTargetAngle(circleTraceTargetStartAngleCounterclockwiseViconFrame);
        }

        // Set the flag indicating that the indicator has not moved yet this trial 
        hasIndicatorMovedThisTrial = false;
    }

    private void exitActiveBlockState()
    {
        // Reset the total points counter to zero
        totalPointsEarnedByPlayerThisBlock = 0.0f;
        feedbackManagerScript.SetTotalPointsEarned(totalPointsEarnedByPlayerThisBlock);
    }

    private void enterWaitingToSaveBlockDataToFileState()
    {
        // Change the state 
        currentState = waitingToSaveBlockDataToFileStateString;
    }

    private void exitWaitingToSaveBlockDataToFileState()
    {
        // do nothing for now
    }

    private void enterGameOverState()
    {
        Debug.Log("Changing to Game Over state");

        // Send a TCP command to the RobUST labview telling it to write data to file
        forceFieldRobotTcpServerScript.SendCommandWithTaskOverSpecifier();

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


    private bool checkPlayerPositionAndUpdateTraceTarget()
    {
        //initialize the return value
        bool trialCompleted = false; // only indicate that the trial is complete after one whole revolution of the circle

        // Get the current player distance from the center (of base of support) and angle. This should be defined in
        // Vicon/real world coordinates [mm] and then mapped back into Unity for rendering.
        float playerDistanceFromCenter = Mathf.Sqrt(Mathf.Pow(player.transform.position.y - circleCenterPosition.y, 2.0f) + Mathf.Pow(player.transform.position.x - circleCenterPosition.x, 2.0f));
        float playerYRelativeToCenter = (player.transform.position.y - circleCenterPosition.y);
        float playerXRelativeToCenter = (player.transform.position.x - circleCenterPosition.x);

        //convert to Vicon frame (really, could just use (COM position = player position), but we want the
        //game to work with the keyboard for testing)
        (Vector3 playerPositionInViconFrame, float playerXRelativeToCenterViconFrame, float playerYRelativeToCenterViconFrame,
                float currentPlayerAngleViconFrame) = computePlayerAngleInViconFrame();

        //store the current player angle as an instance variable
        currentPlayerAngleInDegreesViconFrame = currentPlayerAngleViconFrame;

        //see if the player is "at home" in the central circle. If so, they cannot push the trajectory trace target.
        bool hasPlayerJustLeftHome;
        (isPlayerAtHome, hasPlayerJustLeftHome) = isPlayerInHomeCircle();

        //only move the trace if the player is some distance from the circle center
        if (!isPlayerAtHome) //if the player is far out enough
        {
            // Depending on the direction, see if the player has approached close enough to the indicator 
            // to move/"push" it around the circle. If so, get the new angle (else just return the current angle).
            float newTargetAngleViconFrame = getUpdatedDirectionSpecificTargetAngleToIndicator(currentPlayerAngleInDegreesViconFrame,
                playerXRelativeToCenterViconFrame, playerYRelativeToCenterViconFrame);

            // Update parameters tracking the trial/revolution progress. 
            trialCompleted = updateProgressAroundCircleBasedOnNewPlayerAngle(currentPlayerAngleInDegreesViconFrame);

            // If the tracer is pushable
            if(circleTraceTargetControlScript.GetPacerPushableStatus() == true)
            {
                // Set the new target angle
                setCircleTraceTargetAngle(newTargetAngleViconFrame);
            }

            // Manage area integration to assign a score to the trial. 
            if (hasPlayerJustLeftHome)
            {
                // If the player has just left home this frame, we store the current angle as 
                // the previous angle for area integration
                Debug.Log("Player has just left home. Current angle to player in Vicon frame is: " + currentPlayerAngleInDegreesViconFrame);
                previousPlayerAngleAwayFromHomeInDegreesViconFrame = currentPlayerAngleInDegreesViconFrame;

                // We also need to store the distance from the center of the base of support in Vicon frame (center of trajectory)
                previousPlayerDistanceAwayFromCenterInViconUnitsMm = Mathf.Sqrt(Mathf.Pow(playerYRelativeToCenterViconFrame, 2.0f) + Mathf.Pow(playerXRelativeToCenterViconFrame, 2.0f));
            }
            else
            {
                // Continue to integrate the area enclosed by the subject's COM trajectory (i.e. by the player position in Vicon frame)
                // This will allow us to assign a score at the end of each trial. 
                float playerDistanceFromCenterViconFrame = Mathf.Sqrt(Mathf.Pow(playerYRelativeToCenterViconFrame, 2.0f) + Mathf.Pow(playerXRelativeToCenterViconFrame, 2.0f));
                computeIntegrationStepForAreaEnclosedByThePlayerInViconFrame(playerDistanceFromCenterViconFrame);
            }

        }

        // Store the previous angle to the player COM so that we can compute change

        return trialCompleted;
    }



    private void assignTrialScoreAndProvideFeedback()
    {
        // Ensure that the points earned by the player this trial is reset to zero before computing it.
        pointsEarnedByPlayerThisTrial = 0.0f;

        // Assign points based on ratio of area error to area inside of the desired tracing trajectory
        float ratioOfAreaErrorTracedByPlayerToTrajectoryArea = runningTotalAreaErrorThisTrial / tracingTrajectoryEnclosedAreaViconUnitsMm;

        // If the player earned points this trial, compute how many
        if (ratioOfAreaErrorTracedByPlayerToTrajectoryArea <= maximumPercentEnclosedAreaErrorToStillEarnPoints)
        {
            pointsEarnedByPlayerThisTrial = (1.0f - (ratioOfAreaErrorTracedByPlayerToTrajectoryArea /
                maximumPercentEnclosedAreaErrorToStillEarnPoints)) * maximumPointsEarnedPerTrial;
        }

        //Add the points earned this trial to the total for the block
        Debug.Log("Tracing trajectory has area " + tracingTrajectoryEnclosedAreaViconUnitsMm
            + ", player had area error of: " + runningTotalAreaErrorThisTrial
            + "and fractional area error of " + ratioOfAreaErrorTracedByPlayerToTrajectoryArea
            + "and earned points this trial of: " + pointsEarnedByPlayerThisTrial);
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


    private float getUpdatedDirectionSpecificTargetAngleToIndicator(float currentPlayerAngleViconFrame,
    float playerXRelativeToCenterViconFrame, float playerYRelativeToCenterViconFrame)
    {


        //Add 360 degrees to the current trace/indicator angle IF it has a small positive value less than the 
        //minimumAngularProximityOfPlayerToTargetDegrees. This is for the case when the player is say, at 358 degrees.
        float currentTraceTargetAngleInDegreesViconFrameLocal = currentTraceTargetAngleInDegreesViconFrame;

        float degreesInCircle = 360.0f;

        // DEBUG: add to print if debug flag is active
        //Debug.Log("Target angle is " + currentTraceTargetAngleInDegreesViconFrameLocal);


        // Figure out if the player is in the region just ahead of the target (e.g., if
        // travel is counterclockwise, then within 5 degrees more CCW).
        bool playerJustAheadOfIndicatorFlag = false;
        float newTargetAngle = currentTraceTargetAngleInDegreesViconFrameLocal; // set equal to current target angle for now, will be updated
                                                                                // if a change occurs
        if (!currentlyTravelingClockwise) //if traveling counterclockwise
        {
            float angleBetweenPlayerAndIndicator = currentPlayerAngleViconFrame - currentTraceTargetAngleInDegreesViconFrameLocal;

            // Adjust for the case where the player is a small positive (0 - 10), the indicator is a large positive (350-360)
            bool currentPlayerAngleSmallPositive = (currentPlayerAngleViconFrame > 0f) && (currentPlayerAngleViconFrame < 10.0f);
            bool currentIndicatorAngleLargePositive = (currentTraceTargetAngleInDegreesViconFrameLocal > 355.0f)
                && (currentTraceTargetAngleInDegreesViconFrameLocal < 360.0f);
            if (currentPlayerAngleSmallPositive && currentIndicatorAngleLargePositive)
            {
                // Adjust by adding 360.0 degrees to the angle between player and indicator
                angleBetweenPlayerAndIndicator = angleBetweenPlayerAndIndicator + 360.0f;
            }

            playerJustAheadOfIndicatorFlag = (angleBetweenPlayerAndIndicator > 0) && (angleBetweenPlayerAndIndicator < 5.0f);


            //if the player is just ahead of the indicator
            if (playerJustAheadOfIndicatorFlag)
            {
/*                Debug.Log("Vicon coordinates relative to center and angle of indicator (x,y, theta): ( " + (currentTraceIndicatorPositionViconFrame.x - centerOfBaseOfSupportXPosViconFrame)
                    + ", " + (currentTraceIndicatorPositionViconFrame.y - centerOfBaseOfSupportYPosViconFrame)
                    + ", " + currentTraceTargetAngleInDegreesViconFrameLocal);
                Debug.Log("Vicon coordinates and angle of player (x,y, theta): ( " + playerXRelativeToCenterViconFrame
                    + ", " + playerYRelativeToCenterViconFrame
                    + ", " + currentPlayerAngleViconFrame);
                Debug.Log("Current (trace indicator angle, player angle, min angle): ( " + currentTraceTargetAngleInDegreesViconFrameLocal + ", "
                    + currentPlayerAngleViconFrame + ", "
                    + minimumAngularProximityOfPlayerToTargetDegrees + ")");*/


                newTargetAngle = currentPlayerAngleViconFrame;

                //if the target angle is greater than or equal to 360, subtract 360 from it
                if (newTargetAngle >= degreesInCircle)
                {
                    newTargetAngle = newTargetAngle - degreesInCircle;
                }

                // Note that the target has moved this trial
                if (hasIndicatorMovedThisTrial == false)
                {
                    hasIndicatorMovedThisTrial = true;
                    currentTrialStartTime = Time.time;
                }
            }
        }
        else //if traveling clockwise
        {
            float angleBetweenPlayerAndIndicator = currentPlayerAngleViconFrame - currentTraceTargetAngleInDegreesViconFrameLocal;

            // Adjust for the case where the player is a large positive (355-360), the indicator is a small positive (0-10)
            bool currentPlayerAngleLargePositive = (currentPlayerAngleViconFrame > 355.0f) && (currentPlayerAngleViconFrame < 360.0f);
            bool currentIndicatorAngleSmallPositive = (currentTraceTargetAngleInDegreesViconFrameLocal > 0f)
                && (currentTraceTargetAngleInDegreesViconFrameLocal < 10.0f);
            if (currentPlayerAngleLargePositive && currentIndicatorAngleSmallPositive)
            {
                // Adjust by subtracting 360.0 degrees from the angle between player and indicator
                // (recall, the angle should be negative as the player leads the indicator CW)
                angleBetweenPlayerAndIndicator = angleBetweenPlayerAndIndicator - 360.0f;
            }

            playerJustAheadOfIndicatorFlag = (angleBetweenPlayerAndIndicator < 0) && (angleBetweenPlayerAndIndicator > -5.0f);

            //if the player is just ahead of the indicator
            if (playerJustAheadOfIndicatorFlag)
            {
/*                Debug.Log("Vicon coordinates relative to center and angle of indicator (x,y, theta): ( " + (currentTraceIndicatorPositionViconFrame.x - centerOfBaseOfSupportXPosViconFrame)
                                       + ", " + (currentTraceIndicatorPositionViconFrame.y - centerOfBaseOfSupportYPosViconFrame)
                                       + ", " + currentTraceTargetAngleInDegreesViconFrameLocal);
                Debug.Log("Vicon coordinates and angle of player (x,y, theta): ( " + playerXRelativeToCenterViconFrame
                    + ", " + playerYRelativeToCenterViconFrame
                    + ", " + currentPlayerAngleViconFrame);
                Debug.Log("Current (trace indicator angle, player angle, min angle): ( " + currentTraceTargetAngleInDegreesViconFrameLocal + ", "
                    + currentPlayerAngleViconFrame + ", "
                    + minimumAngularProximityOfPlayerToTargetDegrees + ")");*/


                newTargetAngle = currentPlayerAngleViconFrame;

                //if the target angle is less than 0, add 360 to it
                if (newTargetAngle < 0)
                {
                    newTargetAngle = newTargetAngle + degreesInCircle;
                }

                // Note that the target has moved this trial
                if (hasIndicatorMovedThisTrial == false)
                {
                    hasIndicatorMovedThisTrial = true;
                    currentTrialStartTime = Time.time;
                }
            }
        }
        return newTargetAngle;
    }


    private bool updateProgressAroundCircleBasedOnNewPlayerAngle(float newPlayerAngleViconFrame)
    {
        // If the previous player angle is negative 
        if(previousPlayerAngleViconFrame < 0)
        {
            // Then this is the first time we've called this function. Initialize previous angle to be 
            // equal to current angle.
            previousPlayerAngleViconFrame = newPlayerAngleViconFrame;
        }

        uint thresholdForRollingAroundCircle = 300; // the minimum difference, in degrees, at which the difference
                                                    // between the new and old trace target angle indicates that it has
                                                    // passed the 0 degree mark (+x-axis). Could be a small value, 
                                                    // but we'll choose a large one to make it more clear.

        // First, see if the trace target has passed the zero mark on its way around the circle
        // Note: this would be true when the current trace target angle is 359.8 degrees and the new 
        // target angle is 0.2 degrees - because we express the angle as between 0 and 360, 
        // this means the target has crossed over the +x-axis. 
        if (Mathf.Abs(newPlayerAngleViconFrame - previousPlayerAngleViconFrame) > thresholdForRollingAroundCircle)
        {
            playerHasPassedZeroOnItsTripAroundTheCircle = true;
        }

        //then, see if it has returned to its original starting location (trial has been completed)
        bool trialCompleted = false;
        if (!currentlyTravelingClockwise) // if traveling counterclockwise
        {
            //if the trace target has passed zero on this trial and is larger than the starting angle
            if (playerHasPassedZeroOnItsTripAroundTheCircle && (newPlayerAngleViconFrame > circleTraceTargetStartAngleCounterclockwiseViconFrame))
            {
                //then we have completed a full revolution. Note this by changing the flag.
                trialCompleted = true;

                //reset the target has passed zero flag
                playerHasPassedZeroOnItsTripAroundTheCircle = false;
            }
        }
        else // if traveling clockwise
        {
            //if the trace target has passed zero on this trial and is smaller than the starting angle
            if (playerHasPassedZeroOnItsTripAroundTheCircle && (newPlayerAngleViconFrame < circleTraceTargetStartAngleClockwiseViconFrame))
            {
                //then we have completed a full revolution. Note this by changing the flag.
                trialCompleted = true;

                //reset the target has passed zero flag
                playerHasPassedZeroOnItsTripAroundTheCircle = false;
            }
        }

        // Update the previous player angle to be the current one
        previousPlayerAngleViconFrame = newPlayerAngleViconFrame;

        return trialCompleted;

    }



    private void computeIntegrationStepForAreaEnclosedByThePlayerInViconFrame(float playerDistanceFromCenterViconFrame)
    {
        // Compute the change in player angle from this frame to last frame. 
        // If the change in angle is close to 360 degrees, 
        // this is probably a rollover error (since angle defined between 0 and 360). In that case,
        // the change in angle is 360 - the change in angle.
        float changeInPlayerAngleViconFrame = Mathf.Abs(currentPlayerAngleInDegreesViconFrame - previousPlayerAngleAwayFromHomeInDegreesViconFrame);
        float angleChangeDegreesThresholdForRolloverErrorDetection = 355.0f;
        if (changeInPlayerAngleViconFrame > angleChangeDegreesThresholdForRolloverErrorDetection)
        {
            changeInPlayerAngleViconFrame = 360.0f - changeInPlayerAngleViconFrame;
        }

        //Convert angle to radians for integration!
        changeInPlayerAngleViconFrame = changeInPlayerAngleViconFrame * (Mathf.PI / 180.0f);

        // Convert the minimum angular change for area integration from degrees to radians as well
        float minimumPlayerAngleChangeInRadiansForTrajectoryAreaIntegration = minimumPlayerAngleChangeInDegreesForTrajectoryAreaIntegration * (Mathf.PI / 180.0f);

        // If the subject's COM (i.e. player position in Vicon frame) has moved by a minimum angular amount
        if (changeInPlayerAngleViconFrame > minimumPlayerAngleChangeInRadiansForTrajectoryAreaIntegration)
        {
            // Then we should integrate the area enclosed, relative to the center of the tracing trajectory (i.e. center of base of support)
            // First compute the area enclosed by the change in COM angle this frame
            additionalAreaEnclosedByPlayerThisFrameViconFrame = 0.5f * Mathf.Abs(changeInPlayerAngleViconFrame) *
                Mathf.Pow(((playerDistanceFromCenterViconFrame + previousPlayerDistanceAwayFromCenterInViconUnitsMm) / 2.0f), 2.0f);


            // DEBUG: add to print if Debug flag is active
            /* Debug.Log("Two angles (prev,new): (" + previousPlayerAngleAwayFromHomeInDegreesViconFrame + ", " +
                 currentPlayerAngleInDegreesViconFrame + ") ;" + " Angular change: " + changeInPlayerAngleViconFrame + " and two radii(prev, now): (" +
                 previousPlayerDistanceAwayFromCenterInViconUnitsMm + ", " + playerDistanceFromCenterViconFrame + " )" + "; Area added: " +
                 additionalAreaEnclosedByPlayerThisFrameViconFrame);*/

            // Next, compute the area enclosed (pie wedge area) by the ellipse between this angle and the previous angle
            float currentPlayerAngleInRadiansViconFrame = currentPlayerAngleInDegreesViconFrame * (Mathf.PI / 180.0f);
            float previousPlayerAngleInRadiansViconFrame = previousPlayerAngleAwayFromHomeInDegreesViconFrame * (Mathf.PI / 180.0f);
            float currentPlayerAngleEllipseRadius = getCompoundEllipseRadiusInViconUnitsOfMmGivenAngleInRadians(currentPlayerAngleInRadiansViconFrame);
            float previousPlayerAngleEllipseRadius = getCompoundEllipseRadiusInViconUnitsOfMmGivenAngleInRadians(previousPlayerAngleInRadiansViconFrame);

            float desiredAreaEnclosedByPlayerThisViconFrameBasedOnEllipse = 0.5f * Mathf.Abs(changeInPlayerAngleViconFrame) *
                Mathf.Pow(((currentPlayerAngleEllipseRadius + previousPlayerAngleEllipseRadius) / 2.0f), 2.0f);

            // Add the new area enclosed to the running total for this trial
            runningTotalAreaEnclosedByPlayerThisFrameViconFrame += additionalAreaEnclosedByPlayerThisFrameViconFrame;

            // Also add the area error to the running total area error
            additionalAreaErrorOfPlayerComThisFrame = Mathf.Abs(additionalAreaEnclosedByPlayerThisFrameViconFrame
                - desiredAreaEnclosedByPlayerThisViconFrameBasedOnEllipse);
            runningTotalAreaErrorThisTrial += additionalAreaErrorOfPlayerComThisFrame;

/*            Debug.Log("Computing area error: current and previous player radius (c,p): ( "
                + playerDistanceFromCenterViconFrame
                + ", " + previousPlayerDistanceAwayFromCenterInViconUnitsMm
                + ") and current and previous ellipse radii (c,p): ( " + currentPlayerAngleEllipseRadius
                + ", " + previousPlayerAngleEllipseRadius
                + ") and thetaChangeRads is " + changeInPlayerAngleViconFrame
                + " and area error is " + additionalAreaErrorOfPlayerComThisFrame);*/

/*            if (additionalAreaErrorOfPlayerComThisFrame > 30.0f)
            {
                Debug.Log("Large area error of " + additionalAreaErrorOfPlayerComThisFrame);
            }*/

            // We only store the current player angle and distance as previous values for area integration when the angle has changed by the minimum amount
            // (i.e. so only inside of this if statement)
            previousPlayerAngleAwayFromHomeInDegreesViconFrame = currentPlayerAngleInDegreesViconFrame;
            previousPlayerDistanceAwayFromCenterInViconUnitsMm = playerDistanceFromCenterViconFrame;
        }

    }



    private void incrementTrialNumberAndManageState()
    {
        //increment the trial number 
        currentTrialNumber = currentTrialNumber + 1;

        // In the paced task, we switch the pacer state from pushed to paced 
        // after the first trial. 
        // We can call this function every trial with number > 1, and the pacer script will 
        // only switch to paced mode if it is currently in pushedByPlayer mode or pacerIdle mode. 
        if(currentTrialNumber > 1)
        {
            circleTraceTargetControlScript.StartPacerLap(pacerDesiredLapTimeInSeconds);
        }

        //if the block is over
        if (currentTrialNumber > numberOfRevolutionsPerBlock)
        {
            //increment the block number
            currentBlockNumber = currentBlockNumber + 1;

            // Reset the current trial number
            currentTrialNumber = 1;

            //always transition to the Waiting for Home state when leaving the Active Block state
            changeActiveState(waitingForHomePositionStateString);
        }
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

        // Direction of travel (CW, CCW)
        if (currentlyTravelingClockwise)
        {
            frameDataToStore.Add(1.0f); // let clockwise travel be +1.0f
        }
        else
        {
            frameDataToStore.Add(-1.0f); // let counterclockwise travel be -1.0f
        }

        // Trace indicator angle, Unity frame
        Vector3 trajectoryTraceTargetPositionUnity = circleTraceTargetControlScript.GetPosition();
        float currentTrajectoryTraceTargetAngleUnity = Mathf.Atan2(trajectoryTraceTargetPositionUnity.y, trajectoryTraceTargetPositionUnity.x) * (180.0f / Mathf.PI);
        frameDataToStore.Add(currentTrajectoryTraceTargetAngleUnity);

        // Trace indicator position, Unity frame
        frameDataToStore.Add(trajectoryTraceTargetPositionUnity.x);
        frameDataToStore.Add(trajectoryTraceTargetPositionUnity.y);

        // Trace indicator angle, Vicon frame
        Vector3 trajectoryTraceTargetPositionInViconFrame = mapPointFromUnityFrameToViconFrame(trajectoryTraceTargetPositionUnity);
        float currentTrajectoryTraceTargetAngleViconFrame = Mathf.Atan2(trajectoryTraceTargetPositionInViconFrame.y, trajectoryTraceTargetPositionInViconFrame.x) * (180.0f / Mathf.PI);
        frameDataToStore.Add(currentTrajectoryTraceTargetAngleViconFrame);

        // Trace indicator position, Vicon frame
        frameDataToStore.Add(trajectoryTraceTargetPositionInViconFrame.x);
        frameDataToStore.Add(trajectoryTraceTargetPositionInViconFrame.y);



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
        else if (currentState == activeBlockStateString)
        {
            currentStateFloat = 2.0f;
        }
        else if (currentState == gameOverStateString)
        {
            currentStateFloat = 3.0f;
        }
        else
        {
            //let the state remain as -1.0f, some error occurred
        }
        frameDataToStore.Add(currentStateFloat);

        // Also, store the "Player at Home" flag when we're in the Waiting for Home state
        frameDataToStore.Add(Convert.ToSingle(isPlayerAtHome));

        // Store the area enclosed by the player COM in Vicon units this frame (the area of the "pie wedge" covered with respect to center of base of support)
        frameDataToStore.Add(additionalAreaEnclosedByPlayerThisFrameViconFrame);

        // Store the area error of the player COM in Vicon units this frame (the absolute value of the difference between
        // the COM-enclosed area and ellipse area)
        frameDataToStore.Add(additionalAreaErrorOfPlayerComThisFrame);

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

        // Direction of travel (CW, CCW)
        if (currentlyTravelingClockwise)
        {
            trialDataToStore.Add(1.0f); // let clockwise travel be +1.0f
        }
        else
        {
            trialDataToStore.Add(-1.0f); // let counterclockwise travel be -1.0f
        }

        // Store the trajectory center position. (0,0) in Unity frame, but could theoretically move, shifting the 
        // center of the on-screen circular trajectory in the process. 
        trialDataToStore.Add(circleCenterPosition.x);
        trialDataToStore.Add(circleCenterPosition.y);

        // Get the size of the center circle, which determines when the player is "At Home" and cannot push the trace indicator. 
        // If the player is outside of this circle radius, they can push the trace indicator.
        trialDataToStore.Add(circleInCenterOfTrajectoryDiameterViconUnits);
        trialDataToStore.Add(circleInCenterOfTrajectoryDiameterUnityUnits);

        // Store the initial angles (and thus trial-end angles) of the trajectory trace indicator
        // that the player chases
        trialDataToStore.Add(circleTraceTargetStartAngleCounterclockwise);
        trialDataToStore.Add(circleTraceTargetStartAngleClockwise);
        trialDataToStore.Add(circleTraceTargetStartAngleCounterclockwiseViconFrame);
        trialDataToStore.Add(circleTraceTargetStartAngleClockwiseViconFrame);


        // Also, store the scaling ratio used for the shape. This determines how challenging the task is 
        // (i.e. what percent of the excursion limit we create the trajectory at).
        trialDataToStore.Add(scalingRatioPercentOfBoundaryOfStabiliy);

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

        //Store the elliptical trajectory widths and heights by quadrant
        trialDataToStore.Add(quadrantOneWidthRadius);
        trialDataToStore.Add(quadrantOneHeightRadius);
        trialDataToStore.Add(quadrantTwoWidthRadius);
        trialDataToStore.Add(quadrantTwoHeightRadius);
        trialDataToStore.Add(quadrantThreeWidthRadius);
        trialDataToStore.Add(quadrantThreeHeightRadius);
        trialDataToStore.Add(quadrantFourWidthRadius);
        trialDataToStore.Add(quadrantFourHeightRadius);

        //store Vicon to Unity (and back) mapping function variables
        trialDataToStore.Add(trajectoryAspectScalingFactor);
        trialDataToStore.Add(fitToScreenScalingFactor);
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

        // Store the total area enclosed by the player/COM position this trial
        trialDataToStore.Add(runningTotalAreaEnclosedByPlayerThisFrameViconFrame);

        // Store the total area error for this trial
        trialDataToStore.Add(runningTotalAreaErrorThisTrial);

        // store the points earned this trial 
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
        Debug.Log("EMG data has num. samples: " + numberEmgSamplesStored);
        if (numberEmgSamplesStored != 0)
        {
            Debug.Log("Writing EMG data to file.");
            // Tell the general data recorder to write the EMG data to file
            generalDataRecorderScript.writeEmgDataToFile();
        }
    }

    void OnApplicationQuit()
    {
        //tellDataRecorderToWriteStoredDataToFile();
        Debug.Log("Running quit routine");

        // In case of early app termination, the tellDataRecorderToWriteStoredDataToFile() function will not be called, but we 
        // still should pulse the stop pin. Note, pulsing the stop pin twice is not harmful. 
        if (syncingWithExternalHardwareFlag)
        {
            communicateWithPhotonScript.tellPhotonToPulseSyncStopPin(); // sends stop to both EMGS and Vicon link box.
        }
    }

    // END: Data storage functions **************************************************************************************

}

