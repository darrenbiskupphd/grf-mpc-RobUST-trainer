using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.Linq;
using UnityEngine.UI;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class LevelManager : LevelManagerScriptAbstractClass
{


    public float totalPointsEarnedByPlayer = 0;
    public Camera mainCamera; //the camera rendering this scene
    public LineRenderer strikeLineRenderer; //aka the target line renderer
    public GameObject centerOfMassDataDistributorGameObject; //the Game object containing the script that computes and distributes COM and base of support data 
    private ManageCenterOfMassScript centerOfMassDistributorScript; //the script that computes and distributes center of mass (and base of support) data from the Vicon data stream
    public float timeInactivatedAfterCollisionSeconds; //how long both player and target are frozen for after a collision, so that we have time to provide feedback
    public float maximumPointsEarned;
    public bool usingPenaltyZone; //boolean, set each block, indicating if we're using a penalty zone on target final position (or not).
    public GameObject homeCircle; // The "home circle" provides a visual indicator of the "home" position where the subject should start each trial. 
                                  // In the Interception task, it looks like a planet/moon.
    private CircleCollider2D homeCircleCollider; // The collider of the home circle
    private SpriteRenderer homeCircleSpriteRenderer; // The sprite renderer of the home circle
    public GameObject strikeZone; //the GameObject of the strike zone
    float centerOfStrikeZoneBoxColliderXPosInWorldCoords; //the center of the strike zone in viewport coordinates
    public GameObject penaltyZone; //the GameObject of the penalty zone
    private float leftEdgeOfPenaltyZone; //the X position of the left edge of the penalty zone
    private bool usePenaltyZoneForThisBlock; // If this block will include a penalty zone (true) or not (false)
    public int lowPointPenalty; //penalty for the low penalty condition
    public int highPointPenalty; //penalty for the high penalty condition
    public int penaltyZonePointPenalty; //the currently active penalty associated with hitting the target into the penalty zone
    public int numberOfTrialsPerBlock;
    public GameObject levelCanvas; //the canvas GameObject that displays all UI text in this level
    public GameObject continueFromPauseButton; //the UI button used to continue ahead after instructions. 
    public Text pointsText;
    public Text collisionFeedbackText;
    public Text instructionsText;
    private GameObject[] targets;
    public float[] targetYPositionInViewportCoordinates = new float[3];
    private float minimumTargetYPosWorldCoordinates; //store the y-axis position of the target lowest on the screen, in world coordinates
    private float maximumTargetYPosWorldCoordinates; //store the y-axis position of the target highest on the screen, in world coordinates
    private GameObject player;
    private PlayerControllerComDrivenInterception playerController; // the script that controls the player
    Vector3 playerRespawnPositionInWorldCoordinates; //the respawn point of the player in world/Unity coordinates s

    // Task name
    private const string thisTaskNameString = "Excursion";

    //experiment process control
    public int currentExperimentalCondition;
    private int currentTrialNumber = 1;
    private int currentBlockNumber = 0;
    public bool firstBlockIsRightwardStrikesFlag; // Whether or not the person strikes targets on the right (true) or left (false) this block
    public bool strikeDirectionAlternatesEachBlock; // Whether or not the strike direction (right or left) alternates each block (true) or stays the same (false)
    public bool targetIsForwardFromCenterOfBaseOfSupport = true;
    public int[] blockConditionOrders; // The condition: no penalty (1), low penalty (2), high penalty (3). The length of this array 
                                       // also determines the total number of blocks.
    private bool[] blockStrikeDirections; // An array containing the strike direction for each block: right (true) or left (false). Initialized in Setup().
    //UI
    public bool useKeyboardAsInput; //whether we use COM for input (false) or the keyboard (true)

    // subject-specific data
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data

    // stimulation status
    public bool stimulationOnThisBlock; // a public boolean for the experimenter to set to indicate stimulation will be used this block.
    private string currentStimulationStatus; //the current stimulation status for this block as a string. Used for inclusion in saved file names.
    private static string stimulationOnStatusName = "Stim_On";
    private static string stimulationOffStatusName = "Stim_Off";

    // COM data distributor
    private bool isCenterOfMassManagerReady = false; //whether or not the COM data distributor is ready to distribute data. Starts as false.
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    private Vector3 lastComPositionViconCoords = new Vector3(-1.0f, -1.0f, -1.0f); //the last position of the COM retrieved from the center of mass manager. Used to see if the new COM position has been updated.

    // Functional boundary of stability loading
    private string subdirectoryWithBoundaryOfStabilityData; // the string specifying the subdirectory (name) we'll load the 
                                                            // boundary of stability data from

    // Rendering functional boundary of stability
    private GameObject boundaryOfStabilityRenderer; //the renderer that draws the functional boundary of stability
    private RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript; // the script in the functional boundary of stability renderer

    // Base of support
    private float centerOfBaseOfSupportXPosViconFrame;
    private float centerOfBaseOfSupportYPosViconFrame;
    private float leftEdgeBaseOfSupportXPosInViconCoords;
    private float rightEdgeBaseOfSupportXPosInViconCoords;
    private float frontEdgeBaseOfSupportYPosInViconCoords;
    private float backEdgeBaseOfSupportYPosInViconCoords;

    // Mapping function parameters
    private float rightwardsSign; // +1 if x-axes of Vicon and Unity are aligned, -1 if they are inverted
    private float forwardsSign; // +1 if y-axes of Vicon and Unity are aligned, -1 if they are inverted. Should equal rightwardsSign. 
    private float targetsInLongerExcursionDirectionPercentFromHomeToEdgeOfScreen = 0.9f; // The proportion of the distance from home circle center to lateral edge of 
                                                                                         // the screen at which we place the targets, along the further 
                                                                                         // excursion direction. 
    private float furtherAnteroposteriorBoundaryPercentFromHomeToEdgeOfScreen = 0.9f; // The proportion of the distance from home circle center to top/bottom edge of 
                                                                                      // the screen at which we place the anterior or posterior boundary, 
                                                                                      // whichever is further
    // X-axis mapping parameters
    private float mappingViconToUnityAndBackMovingTargetPositionScalingFactor; // Normalizes the Vicon frame x-axis coordinate to the furthest target position 
    private float mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor; // Normalizes the Vicon frame x-axis coordinate to the lateral
                                                                                         // edges of the screen in Unity
    // Y-axis mapping parameters
    private float mappingViconToUnityAndBackApBoundaryScalingFactor; // Normalizes the Vicon frame y-axis coordinate to the A/P boundary, whichever is further (A or P)
    private float mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor; // Normalizes the Vicon frame y-axis coordinate to
                                                                                      // the top/bottom edges of the screen in Unity

    // States used to control program flow
    private string currentState;
    private const string waitingForSetupStateString = "WAITING_FOR_SETUP"; // Waiting for COM manager and other services. As float, let it be state 0.0f.
    private const string waitingForHomePositionStateString = "WAITING_FOR_HOME_POSITION"; // Wait for player to move to home position for some time. state 1.0f.
    private const string targetMovingStateString = "TARGET_MOVING"; // The target is live and moving. State 2.0f
    private const string trialFeedbackStateString = "TRIAL_FEEDBACK"; // The target was struck or missed - player is frozen and feedback is provided. State 3.0f
    private const string gameOverStateString = "GAME_OVER"; // Game over. State 4.0f.

    // other boolean flags that control program flow
    private bool isPlayerAtHome = false; // keep track of whether or not the player is at home,
                                         // when we're in the waitingForHomePosition state. An initial state of
                                         // false is desirable.

    // a stopwatch to monitor time spent in the "home area"
    private Stopwatch timeAtHomeStopwatch = new Stopwatch();
    private float millisecondsRequiredAtHomeToStartNewTrial = 3000.0f;

    // Interacting with COM 
    private string interceptionGameSpecificComMappingSpecifierString = "Interception";                                                                           //to the lowest target. 

    // Variable game parameters that define the task in Vicon (real world) space
    private float strikeZoneWidthInMillimeters = 38.1f; // The width of the area in which the moving target can be struck. 1 inch is 25.4 mm.
    private float perfectCenterOfStrikeZoneDistanceComToXcomAsPercentOfMos = 0.25f; // this value sets the perfect strike velocity at the center
                                                                                    // of the strike zone, based on the direction-specific
                                                                                    // margin of stability and XCOM model.
    private float rightwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond;
    private float leftwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond;
    private float currentPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond; // The strike x-axis velocity resulting in perfect performance, if strike occurs in 
                                                                                          // the middle of the strike zone. Computed during setup, based on the
                                                                                          // perfectCenterOfStrikeZoneDistanceComToXcomAsPercentOfMos
                                                                                          // variable. Units are mm/s.
    public float fractionOfMarginOfStabilityTraversedAtStrikeOnAverage; //the fraction of the margin of stability travelled in the given lateral direction
                                                                        //to reach the strike zone at the correct velocity. Note that this considers both 
                                                                        //position and velocity and incorporates the inverted pendulum model built into XCOM.
                                                                        //For example, 0.6 would position the strike zone center at 60% along the margin of stability 
                                                                        // measured from the center of the base of support to the edge (left, right) in question.

    private float timeForTargetToCrossStrikeZoneInSeconds = 1.0f; // the amount of time (seconds) it takes for the target to cross the strike zone. Determines
                                                                  // target velocity
    private float timeForTargetToReachStrikeZoneAfterMovementStartInSeconds = 2.5f; // the amount of time (seconds) it takes for the target to reach the 
                                                                                    // strike zone. Once set for an experiment, this should never change. 
    private float bodyAsInvertedPendulumLengthInMillimeters; // For computing XCOM, we model the body as an inverted pendulum. The COM manager computes this length
                                                             // from marker data and we retrieve it below during setup.

    private float maximumDistanceToEarnAnyPointsViconFrameInMm = 25.0f; // The max distance from the post-collision target knockback position to
                                                                        // the target line that earns any points. Note, this distance applies in both 
                                                                        // directions, so the total scoring region has a width of twice this value [mm].

    // The locations of objects in Vicon space, determined from our selection of game parameters
    private float currentStrikeZoneCenterXAxisPositionInViconFrame; // Which center of strike zone x-axis position we're currently using (left or right)
    private float leftwardsStrikeZoneCenterXAxisPositionInViconFrame;
    private float rightwardsStrikeZoneCenterXAxisPositionInViconFrame;
    private float movingTargetXAxisSpeedInViconFrameMmPerSecond; // units are [mm/s]
    private float currentMovingTargetStartingXPositionInViconFrame; // Which target starting position we're currently using (left or right)
    private float movingTargetStartingYPositionInViconFrame;
    private float leftwardsMovingTargetStartingXPositionInViconFrame;
    private float rightwardsMovingTargetStartingXPositionInViconFrame;
    private float currentTargetLineXPositionInViconFrame; // Which target line x-axis position we're currently using (left or right)
    private float leftwardsGoalLineXPositionInViconFrame;
    private float rightwardsGoalLineXPositionInViconFrame;
    private Vector3 homeCirclePositionInViconFrame;
    public float proportionOfAnteroposteriorExcursionDistanceToPositionTarget = 0.4f;
    private float yAxisDistanceFromCenterOfBaseOfSupportToMovingTargetInViconFrame;
    private float currentStartOfPenaltyZoneXPositionInViconFrame;
    private float leftwardsStartOfPenaltyZoneXPositionInViconFrame;
    private float rightwardsStartOfPenaltyZoneXPositionInViconFrame;
    private float currentTargetMissLineXAxisPositionViconFrame; // The x-axis location at which the target will be deemed a "miss" (i.e. not struck and 
                                                      // completely passed through the strike zone)
    private float leftwardsTargetMissLineXAxisPositionViconFrame;
    private float rightwardsTargetMissLineXAxisPositionViconFrame;
    
    // Current locations of objects in Vicon space for the block. Computed so that they can be written to file.
    private float currentNearStrikeZoneEdgeXPosViconFrame;
    private float currentFarStrikeZoneEdgeXPosViconFrame;
    private float currentTargetMissedLineXPosViconFrame; 


    // The target knockback scaling constant - computed from the environmental setup in Vicon space and defined game parameters
    private float targetKnockbackScalingConstantViconFrame; // The scaling constant for the function that takes
                                                            // player strike velocity during the collision as input and outputs
                                                            // target knockback distance.



    // The locations of objects in Unity space, determined from our selection of game parameters
    private float strikeZoneMiddleXPositionInUnityFrame;
    private float strikeZoneWidthInUnityUnits;
    private float perfectStrikeInCenterOfStrikeZoneXVelocityUnityUnits;
    private float targetLineXPositionInUnityFrame;
    private float movingTargetsStartingXPositionInUnityFrame;
    private float movingTargetsStartingYPositionInUnityFrame;
    private float movingTargetXAxisSpeedInUnityFrame;
    private float homeCircleDiameterAsFractionOfShortestFunctionalBoundaryOfStabilityDimension = 0.15f;
    private Vector3 homeCirclePositionInUnityFrame = new Vector3(0.0f, 0.0f, 15.0f);
    private float homeCircleDiameterUnityUnits; // The diameter of the home circle in Unity units. Some fraction of the
                                                // shorter dimension of the functional boundary of stability.
    private float currentTargetMissLineXPositionUnityFrame; // The x-axis position at which point the target is deemed a "miss", in Unity frame
    // Unity game objects
    private BoxCollider2D strikeZoneBoxCollider; 


    // Saving data to file
    public GameObject generalDataRecorder; //the GameObject that stores data and ultimately writes it to file (CSV)
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    private string nameOfThisTask = "Interception"; // The name for this game/task, which will identify files generated from it
    private string subdirectoryName; //the string specifying the subdirectory (name) we'll be saving to in this session
    private string mostRecentFileNameStub; //the string specifying the .csv file save name for the frame, without the suffix specifying whether it's marker, frame, or trial data.

    // Trial-specific data to store as instance variables
    private Vector3 targetPostCollisionKnockbackPositionInViconFrame; // where the target was knocked back to after the interception collision this trial.
    private bool targetCollisionInStrikeZone; // Whether the player collided with the target in the valid "strike zone" (true) or not (false) this trial.
    private Vector3 playerCollisionPositionViconFrame; // The player position at the moment of the collision with the target this trial, in Vicon frame [mm]
    private Vector3 playerCollisionVelocityViconFrame; // The player velocity at the moment of the collision with the target this trial, in Vicon frame [mm/s]
    private Vector2 targetLocationAtCollisionViconFrame; // The target location at the moment of collision this trial. Converted from Unity frame to Vicon frame [mm]
    private Vector3 updatedTargetLocationAtCollisionViconFrame; // The updated target location at the moment of collision this trial.
                                                                // An update is necessary to account for the fact that the player and target should be rigid, 
                                                                // but can "overlap" at the moment of collision in Unity because of the discrete time updates 
                                                                // used by the physics engine. Converted from Unity frame to Vicon frame [mm]
    private Vector3 targetPostCollisionKnockbackPositionInUnityFrame; // // where the target was knocked back to after the interception collision this trial, Unity frame.
    private float playerKnockonDistanceAfterCollisionUnityFrame; // How far the player moves after a collision in Unity frame. Mimics momentum.
    private Vector3 playerKnockonPositionAfterCollisionUnityFrame; // Where the player ends movement after a collision in Unity frame
    private bool targetMissedThisTrial; // Whether the active target was missed this trial (true) or was hit (false)
    private float pointsEarnedThisTrial; // The points earned by the player this trial
    private bool targetHitTooFarThisTrial = false; // If the target was hit beyond the scoring region (true) or never made it to the scoring region (false). 
                                                   // We only look at this flag if the points earned was equal to zero.
    private bool targetHitIntoPenaltyZone = false;
    private TargetController activeTargetThisTrialControlScript; // The control script for the target that was struck on this trial.

    // Voluntary excursion distances i.e. functional boundary of stability
    private float[] excursionDistancesPerDirectionViconFrame; // Excursion distances read from file (from THAT day's excursion trial)

    // Positions of the feedback text 
    private Vector3 totalPointsTextPositionInViewportCoords = new Vector3(0.20f, 0.85f, 0.0f);
    private Vector3 collisionPointsTextPositionInViewportCoords = new Vector3(0.50f, 0.7f, 0.0f);

    // RobUST force field type specifier
    private const string forceFieldIdleModeSpecifier = "I";
    private const string forceFieldAtExcursionBoundary = "B";
    private string currentDesiredForceFieldTypeSpecifier;

    // Using EMGs flag 
    public bool streamingEmgDataFlag;



    // Start is called before the first frame update
    void Start()
    {

        // We start the Level Manager's state machine in the waitingForSetup state.
        currentState = waitingForSetupStateString;

        // Get references to other game objects with tags
        targets = GameObject.FindGameObjectsWithTag("Target");
        player = GameObject.FindGameObjectsWithTag("Player")[0];

        // Get the player controller script 
        playerController = player.GetComponent<PlayerControllerComDrivenInterception>();

        // Get the script inside the functional boundary of stability renderer by using its tag
        GameObject[] boundaryOfStabilityRenderers = GameObject.FindGameObjectsWithTag("BoundaryOfStability");
        if (boundaryOfStabilityRenderers.Length > 0) //if there are any boundary of stability renderers
        {
            boundaryOfStabilityRenderer = boundaryOfStabilityRenderers[0];
        }
        boundaryOfStabilityRendererScript = boundaryOfStabilityRenderer.GetComponent<RenderBoundaryOfStabilityScript>();

        // Get the marker data and center of mass manager script
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        //Get a reference to the script that distributes COM and base of support data about the subject
        centerOfMassDistributorScript = centerOfMassDataDistributorGameObject.GetComponent<ManageCenterOfMassScript>();

        // Saving data to file
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // Get the home circle collider and sprite renderer
        homeCircleCollider = homeCircle.GetComponent<CircleCollider2D>();
        homeCircleSpriteRenderer = homeCircle.GetComponent<SpriteRenderer>();

        // Set the home circle position 
        homeCirclePositionInUnityFrame = new Vector3(0.0f, 0.0f, player.transform.position.z);

        // Get the strike zone's box collider so we can set its position and width later
        strikeZoneBoxCollider = strikeZone.GetComponent<BoxCollider2D>();

        // Set the string indicating whether stimulation is occurring, which we'll use for file naming when writing data to file
        if (stimulationOnThisBlock)
        {
            currentStimulationStatus = stimulationOnStatusName;
        }
        else
        {
            currentStimulationStatus = stimulationOffStatusName;
        }


        //set the header names for the saved-out data CSV headers
        setFrameAndTrialDataNaming();

        // Initialize the text in the Text objects in the game
        pointsText.text = "Points: " + totalPointsEarnedByPlayer;
        instructionsText.text = ""; //clear the instructions text at the start
        collisionFeedbackText.gameObject.SetActive(false); //hide the feedback text until it is needed
        // Position all of the feedback text
        pointsText.transform.position = ConvertViewportCoordinateToCanvasPositionInUnityWorldCoords(totalPointsTextPositionInViewportCoords);
        collisionFeedbackText.transform.position = ConvertViewportCoordinateToCanvasPositionInUnityWorldCoords(collisionPointsTextPositionInViewportCoords);


        // Retrieve the first block condition from the array of block conditions
        currentExperimentalCondition = blockConditionOrders[currentBlockNumber]; //the current experimental condition is the first one in the array


        // All old below? Delete?

        //Get the x-position of the player respawn point and the middle of the strike zone in world coordinates. 
        //We need this to construct the scaling from Vicon coordinates to world coordinates, 
        //in order to control the player with COM as computed by Vicon. 
        centerOfStrikeZoneBoxColliderXPosInWorldCoords = strikeZoneBoxCollider.bounds.center.x;

        //tell the COM-controlled player object to use the COM-to-Unity mapping that is unique to the Interception game 
        // I think this is obsolete code!!!!!!!!! Review and delete if true.
        playerController.setTypeOfCenterOfMassToUnityControlBySpecifierString(interceptionGameSpecificComMappingSpecifierString);
        
        //store the player respawn point in world coordinates, which will help us construct the COM-to-Unity mapping
        // SAME - OLD! (confirm, delete)
        playerRespawnPositionInWorldCoordinates = playerController.returnRespawnPoint();

        // Set the default force field mode as Idle
        currentDesiredForceFieldTypeSpecifier = forceFieldIdleModeSpecifier;


    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // If we're still in the setting up state
        if (currentState == waitingForSetupStateString)
        {
            if (!isCenterOfMassManagerReady) //if the center of mass information distributor is not ready
            {
                isCenterOfMassManagerReady = centerOfMassDistributorScript.getCenterOfMassManagerReadyStatus(); //see if it's ready now
                if (isCenterOfMassManagerReady) //if the COM data distributor is ready
                {

                    // Now that the save folder name has been constructed and the COM manager has been set up, we can tell the functional 
                    // boundary of stability renderer to load the saved excursion limits
                    boundaryOfStabilityRendererScript.loadBoundaryOfStability(subdirectoryWithBoundaryOfStabilityData);

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

                    // Determine if there is axis-flipping in the Vicon to Unity mapping (equivalent to a rotation matrix with axes either aligned or flipped)
                    rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportXPosInViconCoords - leftEdgeBaseOfSupportXPosInViconCoords);
                    forwardsSign = Mathf.Sign(frontEdgeBaseOfSupportYPosInViconCoords - backEdgeBaseOfSupportYPosInViconCoords);


                    // Set the block strike directions
                    blockStrikeDirections = new bool[blockConditionOrders.Length]; // The "condition" order array sets the number of blocks.
                    setBlockStrikeDirections();

                    // Compute the locations of the virtual objects in Vicon frame, for both left and right directions
                    setUpGameParametersInViconSpace();

                    // Given the strike direction, choose the proper locations for the game objects in Vicon frame
                    chooseViconFrameEnvironmentSetupGivenCurrentBlockStrikeDirection();

                    // Now that the game has been designed in Vicon space, we can create the mapping function from Vicon space to Unity space
                    defineMappingFromViconFrameToUnityFrameAndBack();

                    // Next, we determine the positions of all of the game objects in Unity frame (as well as velocities, e.g. target movement speed)
                    Debug.Log("About to call setUpGameInUnityFrame() function.");
                    setUpGameInUnityFrame();

                    //get the location of the left edge of the penalty zone. Cannot get bounds of the collider when it's disabled, so just do it now.
                    BoxCollider2D penaltyZoneBoxCollider = penaltyZone.GetComponent<BoxCollider2D>();
                    leftEdgeOfPenaltyZone = penaltyZoneBoxCollider.bounds.center.x - penaltyZoneBoxCollider.bounds.extents.x;

                    //set up the experiment for the first block
                    SetUpCurrentExperimentalCondition();

                    // Tell the boundary of stability renderer to draw the boundary of stability.
                    // Based on the current working directory (by subject and date), load the functional boundary of 
                    // stability based on the stored file within the Excursion folder. 
                    // Note that the subject would have to have completed an Excursion test that day, or else the file must be 
                    // manually copied into the proper folder location. 
                    drawFunctionalBoundaryOfStability();

                    Debug.Log("Setup complete");


                    // Transition to the waiting for home state
                    changeActiveState(waitingForHomePositionStateString);
                }
            }
        }
        else if (currentState == waitingForHomePositionStateString) // If we're in the Waiting for Home state, we wait for the subject to move to the center of their 
                                                                    // base of support
        {
            //Debug.Log("In waiting for home state's FixedUpdate() call.");

            //More debug prints
            Vector3 playerPosViconFrame = mapPointFromUnityFrameToViconFrame(playerController.transform.position);
           /* Debug.Log("Player has Vicon frame position (x,y,z): (" + playerPosViconFrame.x + ", " + playerPosViconFrame.y +
                ", " + playerPosViconFrame.z + ")");*/


            // See if the player has been in the home position for sufficient time to trigger the trial to start
            monitorIfPlayerIsInHomePosition();

            // Write the frame data to file
            storeFrameData();

            // If the player has been at home long enough
            if (timeAtHomeStopwatch.IsRunning && (timeAtHomeStopwatch.ElapsedMilliseconds > millisecondsRequiredAtHomeToStartNewTrial))
            {
                // transition to the moving target state
                changeActiveState(targetMovingStateString);
            }
        }
        else if (currentState == targetMovingStateString)
        {

            // We still want to monitor and update the "at home" position flag in this state
            (isPlayerAtHome, _) = isPlayerInHomeCircle();

            // Write the frame data to file
            storeFrameData();

            // Otherwise, events in this state are handled by the player and target objects calling public functions in the level manager
        }
        else if (currentState == trialFeedbackStateString)
        {
            // Events in this state are handled by the player and target objects calling public functions in the level manager       
        }
        else if (currentState == gameOverStateString)
        {
            // Nothing should happen in this state
        }
        else
        {
            //Error: the state is invalid
        }
    }




    private Vector3 ConvertViewportCoordinateToCanvasPositionInUnityWorldCoords(Vector3 desiredPositionInViewportCoords)
    {
        RectTransform canvasTransform = levelCanvas.GetComponent<RectTransform>();
        Vector3 canvasCenterLocation = canvasTransform.position;
        Vector3 canvasTopLeftCornerLocation = new Vector3(canvasCenterLocation.x - (canvasTransform.rect.width / 2.0f),
            (canvasCenterLocation.y + (canvasTransform.rect.height / 2.0f)), 0.0f);
        Vector3 canvasBottomRightCornerLocation = new Vector3(canvasCenterLocation.x + (canvasTransform.rect.width / 2.0f),
            (canvasCenterLocation.y - (canvasTransform.rect.height / 2.0f)), 0.0f);

        // Convert viewport location to a "canvas location" in Unity world coordinates
        float canvasDesiredXLocation = desiredPositionInViewportCoords.x * (canvasTransform.rect.width) + canvasTopLeftCornerLocation.x;
        float canvasDesiredYLocation = desiredPositionInViewportCoords.y * (canvasTransform.rect.height) + canvasBottomRightCornerLocation.y;

        return new Vector3(canvasDesiredXLocation, canvasDesiredYLocation, canvasTransform.position.z);
    }




    /************************

Function: setBlockStrikeDirections()

Parameters: none

Returns: none

Description: Creates a list of strike directions per block, either right or left. 
             Depending on flags set by the experimenter, the direction alternates each block 
             or stays the same for the entire experiment.

Notes: Called only once, a setup function

**************************/
    private void setBlockStrikeDirections()
    {
        bool evenBlockNumberValue = firstBlockIsRightwardStrikesFlag; // The direction of the first and subsequent even blocks.
                                                                      // Rightward (true) or leftward (false).
        for (uint blockIndex = 0; blockIndex < blockStrikeDirections.Length; blockIndex++)
        {
            if (strikeDirectionAlternatesEachBlock) // If the strike direction will alternate each block
            {
                if ((blockIndex % 2) == 0) // If the block number is even (e.g. block 0, 2, 4, ...)
                {
                    blockStrikeDirections[blockIndex] = evenBlockNumberValue;
                }
                else // If the block number is odd (e.g. 1, 3, ...)
                {
                    blockStrikeDirections[blockIndex] = !evenBlockNumberValue;
                }
            } 
            else // If the strike direction will remain the same each block
            {
                // Then each block will have the same direction as the first block
                blockStrikeDirections[blockIndex] = firstBlockIsRightwardStrikesFlag;
            }

        }
    }


    private (float, float) getPerfectStrikeMeanVelocitiesForRightAndLeftDirections(float gravitationalConstantMetersPerSecondSquared, float convertMetersToMillimeters)
    {
        // 1.) Get the rightwards strikes perfect strike velocity in the center of the strike zone
        float rightwardsPerfectCenterStrikeZoneVelocity = perfectCenterOfStrikeZoneDistanceComToXcomAsPercentOfMos * 
            excursionDistancesPerDirectionViconFrame[0] * Mathf.Sqrt(gravitationalConstantMetersPerSecondSquared * convertMetersToMillimeters / bodyAsInvertedPendulumLengthInMillimeters);

        // 2.) Get the leftwards strikes perfect strike velocity in the center of the strike zone
        float leftwardsPerfectCenterStrikeZoneVelocity = perfectCenterOfStrikeZoneDistanceComToXcomAsPercentOfMos *
            excursionDistancesPerDirectionViconFrame[4] * Mathf.Sqrt(gravitationalConstantMetersPerSecondSquared * convertMetersToMillimeters / bodyAsInvertedPendulumLengthInMillimeters);

        // 3.) Return both as tuple
        return (rightwardsPerfectCenterStrikeZoneVelocity, leftwardsPerfectCenterStrikeZoneVelocity);

    }



    private void setUpGameParametersInViconSpace()
    {

        // Get the length of the "inverted pendulum" representing the person
        bodyAsInvertedPendulumLengthInMillimeters = centerOfMassDistributorScript.getLengthParameterOfBodyInvertedPendulumModel();

        // Declare constants needed to compute the extrapolated COM (XCOM)
        float gravitational_acceleration_in_meters_per_second = 9.80665f;
        float convertMetersToMillimeters = 1000f;

        // Set the strike velocity that results in a perfect hit in the center of the strike zone, for both left and right directions.
        (rightwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond, 
            leftwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond) = getPerfectStrikeMeanVelocitiesForRightAndLeftDirections(
            gravitational_acceleration_in_meters_per_second, convertMetersToMillimeters);

        // Determine the strike zone center location based on the desired margin of stability after a perfect strike in the middle 
        // of the strike zone.
        // Note 1: We use the XCOM distance from the functional boundary of stability to compute margin of stability.
        // Note 2: We have already selected the desired strike velocity for the perfect middle-of-strike-zone strike, so we can compute the
        // strike zone center x-axis position to set the desired margin of stability for such a strike.
        float leftwardsMaxExcursionDistance = excursionDistancesPerDirectionViconFrame[4];
        float leftwardsDistanceFromComToXcomGivenPerfectMiddleStrikeZoneStrikeVelocity = leftwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond *
                                                                        Mathf.Sqrt(bodyAsInvertedPendulumLengthInMillimeters / (gravitational_acceleration_in_meters_per_second * convertMetersToMillimeters));
        float desiredLeftwardsMarginOfStabilityGivenPerfectMiddleStrikeZoneStrike = fractionOfMarginOfStabilityTraversedAtStrikeOnAverage
                                                                                    * leftwardsMaxExcursionDistance;
        leftwardsStrikeZoneCenterXAxisPositionInViconFrame = centerOfBaseOfSupportXPosViconFrame 
                                                        - rightwardsSign * desiredLeftwardsMarginOfStabilityGivenPerfectMiddleStrikeZoneStrike
                                                        + rightwardsSign * leftwardsDistanceFromComToXcomGivenPerfectMiddleStrikeZoneStrikeVelocity;
        // Determine strike zone center location, rightwards strike
        float rightwardsMaxExcursionDistance = excursionDistancesPerDirectionViconFrame[0];
        float rightwardsDistanceFromComToXcomGivenPerfectMiddleStrikeZoneStrikeVelocity = rightwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond *
                                                                        Mathf.Sqrt(bodyAsInvertedPendulumLengthInMillimeters / (gravitational_acceleration_in_meters_per_second * convertMetersToMillimeters));
        float desiredRightwardsMarginOfStabilityGivenPerfectMiddleStrikeZoneStrike = fractionOfMarginOfStabilityTraversedAtStrikeOnAverage
                                                                                    * rightwardsMaxExcursionDistance;
        rightwardsStrikeZoneCenterXAxisPositionInViconFrame = centerOfBaseOfSupportXPosViconFrame 
                                                              + rightwardsSign * desiredRightwardsMarginOfStabilityGivenPerfectMiddleStrikeZoneStrike
                                                              - rightwardsSign * rightwardsDistanceFromComToXcomGivenPerfectMiddleStrikeZoneStrikeVelocity;

        // Determine the target speed in Vicon frame based on the specified time to cross the strike zone and the strike zone width
        movingTargetXAxisSpeedInViconFrameMmPerSecond = strikeZoneWidthInMillimeters / timeForTargetToCrossStrikeZoneInSeconds;

        // Determine the target x-axis positions in Vicon frame based on time to reach the strike zone
        float xAxisDistanceFromTargetStartingLineToStrikeZoneCenterInMm = movingTargetXAxisSpeedInViconFrameMmPerSecond *
                                                                timeForTargetToReachStrikeZoneAfterMovementStartInSeconds + (strikeZoneWidthInMillimeters / 2.0f);
        leftwardsMovingTargetStartingXPositionInViconFrame = leftwardsStrikeZoneCenterXAxisPositionInViconFrame - rightwardsSign * xAxisDistanceFromTargetStartingLineToStrikeZoneCenterInMm;
        rightwardsMovingTargetStartingXPositionInViconFrame = rightwardsStrikeZoneCenterXAxisPositionInViconFrame + rightwardsSign * xAxisDistanceFromTargetStartingLineToStrikeZoneCenterInMm;

        // The strike line will be placed some percentage of the way from the strike zone center to the target starting position. 
        // The actual distance is arbitrary, as we will scale the knockback distance accordingly. 
        float targetLineAtPercentOfDistanceBetweenCenterStrikeAndTarget = 0.8f;
        leftwardsGoalLineXPositionInViconFrame = leftwardsStrikeZoneCenterXAxisPositionInViconFrame - rightwardsSign *
                                                        targetLineAtPercentOfDistanceBetweenCenterStrikeAndTarget * xAxisDistanceFromTargetStartingLineToStrikeZoneCenterInMm;
        rightwardsGoalLineXPositionInViconFrame = rightwardsStrikeZoneCenterXAxisPositionInViconFrame + rightwardsSign *
                                                        targetLineAtPercentOfDistanceBetweenCenterStrikeAndTarget * xAxisDistanceFromTargetStartingLineToStrikeZoneCenterInMm;

        // Set the home circle position in Vicon frame. Typically, we'll use the center of the base of support!
        homeCirclePositionInViconFrame = new Vector3(centerOfBaseOfSupportXPosViconFrame, centerOfBaseOfSupportYPosViconFrame, -1.0f);

        // Set the target y-axis position(s) in Vicon frame. 
        if (targetIsForwardFromCenterOfBaseOfSupport)
        {
            yAxisDistanceFromCenterOfBaseOfSupportToMovingTargetInViconFrame = forwardsSign * proportionOfAnteroposteriorExcursionDistanceToPositionTarget * excursionDistancesPerDirectionViconFrame[2];
        }
        else
        {
            yAxisDistanceFromCenterOfBaseOfSupportToMovingTargetInViconFrame = -forwardsSign * proportionOfAnteroposteriorExcursionDistanceToPositionTarget * excursionDistancesPerDirectionViconFrame[6];
        }
        movingTargetStartingYPositionInViconFrame = centerOfBaseOfSupportYPosViconFrame + yAxisDistanceFromCenterOfBaseOfSupportToMovingTargetInViconFrame;

        // Figure out the x-axis location at which the target will be deemed a "miss" (i.e. not struck and 
        // completely passed through the strike zone)
        leftwardsTargetMissLineXAxisPositionViconFrame = leftwardsStrikeZoneCenterXAxisPositionInViconFrame + rightwardsSign * (strikeZoneWidthInMillimeters/2.0f);
        rightwardsTargetMissLineXAxisPositionViconFrame = rightwardsStrikeZoneCenterXAxisPositionInViconFrame - rightwardsSign * (strikeZoneWidthInMillimeters / 2.0f);

        // Figure out where the penalty zone would go (IF we're using a penalty zone) for each striking direction. 
        // For now, I'll just use the far end of the scoring region. However, if we ever use penalty zones in an experiment, 
        // it would be wise to define the penalty zone to be at some XCOM MOS location a percentage greater (say, 5%) 
        // than the largest XCOM MOS that can earn points, assuming a middle-strike zone strike.
        leftwardsStartOfPenaltyZoneXPositionInViconFrame = leftwardsGoalLineXPositionInViconFrame - rightwardsSign* maximumDistanceToEarnAnyPointsViconFrameInMm;
            rightwardsStartOfPenaltyZoneXPositionInViconFrame = rightwardsGoalLineXPositionInViconFrame + rightwardsSign * maximumDistanceToEarnAnyPointsViconFrameInMm;
    }



    private void chooseViconFrameEnvironmentSetupGivenCurrentBlockStrikeDirection()
    {
        if (blockStrikeDirections[currentBlockNumber] == true) // if striking rightward (rightwards strikes are denoted by "true" for the strike direction)
        {
            // Use the rightward locations for the strike zone, target line, targets, and target miss line location
            currentStrikeZoneCenterXAxisPositionInViconFrame = rightwardsStrikeZoneCenterXAxisPositionInViconFrame;
            currentMovingTargetStartingXPositionInViconFrame = rightwardsMovingTargetStartingXPositionInViconFrame;
            currentTargetLineXPositionInViconFrame = rightwardsGoalLineXPositionInViconFrame;
            currentTargetMissLineXAxisPositionViconFrame = rightwardsTargetMissLineXAxisPositionViconFrame;
            currentPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond = rightwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond;

            // Compute some extra parameters just to save to file, based on these parameters
            currentNearStrikeZoneEdgeXPosViconFrame = rightwardsStrikeZoneCenterXAxisPositionInViconFrame - rightwardsSign * (strikeZoneWidthInMillimeters/2.0f);
            currentFarStrikeZoneEdgeXPosViconFrame = rightwardsStrikeZoneCenterXAxisPositionInViconFrame + rightwardsSign * (strikeZoneWidthInMillimeters / 2.0f);
            currentTargetMissedLineXPosViconFrame = rightwardsTargetMissLineXAxisPositionViconFrame;

            Debug.Log("The strike zone has x-axis center position in Vicon frame of: " + currentStrikeZoneCenterXAxisPositionInViconFrame);
            Debug.Log("The target line for knocking back the target will be located at x-axis Vicon frame position: " + currentTargetLineXPositionInViconFrame);

            if (usePenaltyZoneForThisBlock) // if we're using a penalty zone, then set it's location
            {
                currentStartOfPenaltyZoneXPositionInViconFrame = rightwardsStartOfPenaltyZoneXPositionInViconFrame;
            }
            else // if we're not using a penalty zone, then set it's location infinitely far beyond the strike zone
            {
                currentStartOfPenaltyZoneXPositionInViconFrame = (float) (rightwardsSign * Double.PositiveInfinity);
            }
        }
        else // if striking leftwards
        {
            // Use the leftward locations for the strike zone, target line, and targets
            currentStrikeZoneCenterXAxisPositionInViconFrame = leftwardsStrikeZoneCenterXAxisPositionInViconFrame;
            currentMovingTargetStartingXPositionInViconFrame = leftwardsMovingTargetStartingXPositionInViconFrame;
            currentTargetLineXPositionInViconFrame = leftwardsGoalLineXPositionInViconFrame;
            currentTargetMissLineXAxisPositionViconFrame = leftwardsTargetMissLineXAxisPositionViconFrame;
            currentPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond = leftwardsPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond;

            // Compute some extra parameters just to save to file, based on these parameters
            currentNearStrikeZoneEdgeXPosViconFrame = leftwardsStrikeZoneCenterXAxisPositionInViconFrame + rightwardsSign * (strikeZoneWidthInMillimeters / 2.0f);
            currentFarStrikeZoneEdgeXPosViconFrame = leftwardsStrikeZoneCenterXAxisPositionInViconFrame - rightwardsSign * (strikeZoneWidthInMillimeters / 2.0f);
            currentTargetMissedLineXPosViconFrame = leftwardsTargetMissLineXAxisPositionViconFrame;

            if (usePenaltyZoneForThisBlock) // if we're using a penalty zone, then set it's location 
            {
                currentStartOfPenaltyZoneXPositionInViconFrame = leftwardsStartOfPenaltyZoneXPositionInViconFrame;
            }
            else // if we're not using a penalty zone, then set it's location infinitely far beyond the strike zone
            {
                currentStartOfPenaltyZoneXPositionInViconFrame = (float) (rightwardsSign * -1.0f * Double.PositiveInfinity);
            }
        }

        // Now that the current perfect strike velocity (in center of strike zone) has been selected for the current strike direction, 
        // set the knockback scaling constant.
        setPlayerStrikeVelocityScalingBasedOnUserInputs();
    }




    private void setUpGameInUnityFrame()
    {
        Debug.Log("Inside setUpGameInUnityFrame() function.");

        // Set the strike zone width in Unity frame, based on its width in Vicon frame
        (strikeZoneWidthInUnityUnits, _) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(strikeZoneWidthInMillimeters, 0.0f);
        Debug.Log("Mapping issue: strike zone width is " + strikeZoneWidthInUnityUnits);
        float strikeZoneXAxisScalingFactor = (strikeZoneWidthInUnityUnits / (2.0f * strikeZoneBoxCollider.bounds.extents.x));
        float newStrikeZoneXAxisScale = strikeZone.transform.localScale.x;
        newStrikeZoneXAxisScale = newStrikeZoneXAxisScale * strikeZoneXAxisScalingFactor;
        strikeZone.transform.localScale = new Vector3(newStrikeZoneXAxisScale, strikeZone.transform.localScale.y, strikeZone.transform.localScale.z);

        // Set the home circle in the designated location (set by experimenter in public variables)
        homeCircle.transform.position = homeCirclePositionInUnityFrame;

        // Set the home circle radius 
        // First find the shorter dimension of the functional boundary of stability (x- or y-axis)
        float mediolateralWidthOfFunctionalBoundaryOfStability = excursionDistancesPerDirectionViconFrame[0] + excursionDistancesPerDirectionViconFrame[4];
        float anteroposteriorWidthOfFunctionalBoundaryOfStability = excursionDistancesPerDirectionViconFrame[2] + excursionDistancesPerDirectionViconFrame[6];
        float shorterFunctionalBoundaryOfStabilityDimensionUnityUnits;
        if(mediolateralWidthOfFunctionalBoundaryOfStability >= anteroposteriorWidthOfFunctionalBoundaryOfStability)
        {
            float shorterFunctionalBoundaryOfStabilityDimensionViconUnitsMm = anteroposteriorWidthOfFunctionalBoundaryOfStability;
            (_,shorterFunctionalBoundaryOfStabilityDimensionUnityUnits) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(0.0f, shorterFunctionalBoundaryOfStabilityDimensionViconUnitsMm);
        }
        else
        {
            float shorterFunctionalBoundaryOfStabilityDimensionViconUnitsMm = mediolateralWidthOfFunctionalBoundaryOfStability;
            (shorterFunctionalBoundaryOfStabilityDimensionUnityUnits, _) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(shorterFunctionalBoundaryOfStabilityDimensionViconUnitsMm, 0.0f);

        }
        homeCircleDiameterUnityUnits = homeCircleDiameterAsFractionOfShortestFunctionalBoundaryOfStabilityDimension * shorterFunctionalBoundaryOfStabilityDimensionUnityUnits;
        // Scale the home circle transform, scaling the attached collider and sprite in the process
        float homeCircleScaleFactor = (homeCircleDiameterUnityUnits / 2.0f) / homeCircleCollider.radius;
        homeCircle.transform.localScale = new Vector3(homeCircle.transform.localScale.x * homeCircleScaleFactor, homeCircle.transform.localScale.y * homeCircleScaleFactor, homeCircle.transform.localScale.z);

        // Find the moving target x-axis speed in Unity frame units
        movingTargetXAxisSpeedInUnityFrame = Mathf.Abs(convertVelocityInViconFrameToVelocityInUnityFrame(new Vector3(movingTargetXAxisSpeedInViconFrameMmPerSecond, 0.0f, 0.0f)).x);

        // Set up block-specific features. For example, starting target position, target strike line position, strike zone position
        SetUpBlockSpecificFeaturesInUnityFrame();
    }


    private void SetUpBlockSpecificFeaturesInUnityFrame()
    {
        // Find the middle of the strike zone in Unity frame
        strikeZoneMiddleXPositionInUnityFrame = mapPointFromViconFrameToUnityFrame(new Vector3(currentStrikeZoneCenterXAxisPositionInViconFrame, 0.0f, 0.0f)).x;
        Debug.Log("Strike zone x-axis center position in Unity frame should be: " + strikeZoneMiddleXPositionInUnityFrame);
        //Set the strike zone middle x-axis position
        strikeZone.transform.position = new Vector3(strikeZoneMiddleXPositionInUnityFrame, strikeZone.transform.position.y, strikeZone.transform.position.z);

        // Find the moving target starting x-axis position in Unity frame
        movingTargetsStartingXPositionInUnityFrame = mapPointFromViconFrameToUnityFrame(new Vector3(currentMovingTargetStartingXPositionInViconFrame, 0.0f, 0.0f)).x;

        // Find the moving target starting y-axis position in Unity frame
        movingTargetsStartingYPositionInUnityFrame = mapPointFromViconFrameToUnityFrame(new Vector3(0.0f, movingTargetStartingYPositionInViconFrame, 0.0f)).y;

        //set the target positions 
        setTargetPositions();

        // Find the x-axis position at which the target is deemed a "miss" and the trial ends
        currentTargetMissLineXPositionUnityFrame = mapPointFromViconFrameToUnityFrame(new Vector3(currentTargetMissLineXAxisPositionViconFrame, 0.0f, 0.0f)).x;

        // Find the target line x-axis position in Unity frame
        targetLineXPositionInUnityFrame = mapPointFromViconFrameToUnityFrame(new Vector3(currentTargetLineXPositionInViconFrame, 0.0f, 0.0f)).x;

        // Get the target line renderer positions
        Vector3[] strikeLineRendererPositions = new Vector3[strikeLineRenderer.positionCount];
        strikeLineRenderer.GetPositions(strikeLineRendererPositions); // Get line renderer positions
        // For each line renderer position
        for (uint lineRendererPositionIndex = 0; lineRendererPositionIndex < strikeLineRendererPositions.Length; lineRendererPositionIndex++)
        {
            // Reset the x-axis coordinate to the computed x-axis position for the target line
            Vector3 currentLineRendererPosition = strikeLineRendererPositions[lineRendererPositionIndex];
            strikeLineRendererPositions[lineRendererPositionIndex] = new Vector3(targetLineXPositionInUnityFrame,
                                                                     currentLineRendererPosition.y, currentLineRendererPosition.z);
        }
        // Set the line renderer positions
        strikeLineRenderer.SetPositions(strikeLineRendererPositions);

        // Rotate the targets to have the proper orientation in Unity frame
        rotateAllTargetsToHaveProperOrientationForThisBlock();
    }


    private void rotateAllTargetsToHaveProperOrientationForThisBlock()
    {
        for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            GameObject target = targets[targetIndex];
            if (blockStrikeDirections[currentBlockNumber] == true) //if striking rightwards
            {
                target.transform.eulerAngles = new Vector3(target.transform.eulerAngles.x, target.transform.eulerAngles.y, -90.0f);
            }
            else
            {
                target.transform.eulerAngles = new Vector3(target.transform.eulerAngles.x, target.transform.eulerAngles.y, 90.0f);
            }
        }
    }


    private void defineMappingFromViconFrameToUnityFrameAndBack()
    {
        // Choose the scaling factor, which will be based on the target distance from the center. 
        // We choose the larger of the left and right distance to target in Vicon frame. 
        float leftDistanceFromHomeCircleToMovingTargetStartXPos = Mathf.Abs(leftwardsMovingTargetStartingXPositionInViconFrame - centerOfBaseOfSupportXPosViconFrame);
        float rightDistanceFromHomeCircleToMovingTargetStartXPos = Mathf.Abs(rightwardsMovingTargetStartingXPositionInViconFrame - centerOfBaseOfSupportXPosViconFrame);

        // Get the screen height and width in Unity frame
        float screenWidthInUnityCoords = (mainCamera.ViewportToWorldPoint(new Vector3(1, 0, 0)) - mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0))).x;
        float screenHeightInUnityCoords = (mainCamera.ViewportToWorldPoint(new Vector3(0, 1, 0)) - mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0))).y;

        // Get the distance from the home circle center to the edge of the screen in Unity units
        Vector3 homeCirclePositionInViewportCoords = mainCamera.WorldToViewportPoint(homeCirclePositionInUnityFrame);

        // X-axis mapping ***********************

        // If the right moving targets are further from the home circle
        if (rightDistanceFromHomeCircleToMovingTargetStartXPos >= leftDistanceFromHomeCircleToMovingTargetStartXPos)
        {
            // Then the scaling factors are based on the rightwards distance to moving targets
            // First, compute the scaling factor that normalizes the coordinates in Vicon frame
            mappingViconToUnityAndBackMovingTargetPositionScalingFactor = 1.0f / rightDistanceFromHomeCircleToMovingTargetStartXPos;

            // Compute the distance from the home circle to the edge of the screen along the further excursion direction (L or R)
            float distanceFromHomeCircleToRightEdgeInUnityFrame = (1.0f - homeCirclePositionInViewportCoords.x) * screenWidthInUnityCoords;

            // Then compute the scaling factor that normalizes the coordinates in Unity frame, such that the targets are at some proportion 
            // of the distance from the home circle to the edge of the screen
            mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor = targetsInLongerExcursionDirectionPercentFromHomeToEdgeOfScreen *
                                                                                     distanceFromHomeCircleToRightEdgeInUnityFrame;
        }
        // Else if the left moving targets are further from the home circle
        else
        {
            // Then the scaling factors are based on the leftwards distance to moving targets
            mappingViconToUnityAndBackMovingTargetPositionScalingFactor = 1.0f/ leftDistanceFromHomeCircleToMovingTargetStartXPos;

            // Compute the distance from the home circle to the edge of the screen along the further lateral excursion direction (L or R)
            float distanceFromHomeCircleToLeftEdgeInUnityFrame = homeCirclePositionInViewportCoords.x * screenWidthInUnityCoords;

            // Then compute the scaling factor that normalizes the coordinates in Unity frame, such that the targets are at some proportion 
            // of the distance from the home circle to the edge of the screen
            mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor = furtherAnteroposteriorBoundaryPercentFromHomeToEdgeOfScreen *
                                                                                     distanceFromHomeCircleToLeftEdgeInUnityFrame;
        }

        Debug.Log("X-axis scaling factors for Vicon-Unity mapping are (targetScaler,screenScaler): (" + mappingViconToUnityAndBackMovingTargetPositionScalingFactor + ", " +
            mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor + ")");


        // Y-axis mapping ***********************
        float forwardsExcursionDistance = excursionDistancesPerDirectionViconFrame[2];
        float backwardsExcursionDistance = excursionDistancesPerDirectionViconFrame[6];

        // If the forward excursion distance is greater than or equal to backwards excursion distance
        if (rightDistanceFromHomeCircleToMovingTargetStartXPos >= leftDistanceFromHomeCircleToMovingTargetStartXPos)
        {
            // Then we scale by the forwards excursion distance. 
            // First, compute the scaling factor that normalizes the coordinates in Vicon frame
            mappingViconToUnityAndBackApBoundaryScalingFactor = 1.0f / (forwardsExcursionDistance);

            // Compute the distance from the home circle to the edge of the screen along the further anteroposterior excursion direction (F, B)
            float distanceFromHomeCircleToForwardEdgeInUnityFrame = (1.0f - homeCirclePositionInViewportCoords.y) * screenHeightInUnityCoords;

            // Then compute the scaling factor that normalizes the coordinates in Unity frame, such that the forward boundary of stability 
            // is always some proportion of the distance from the home circle to the top edge of the screen.
            mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor = furtherAnteroposteriorBoundaryPercentFromHomeToEdgeOfScreen *
                                                                                 distanceFromHomeCircleToForwardEdgeInUnityFrame;

        }
        // Else if the backward excursion distance is greater than the forwards excursion distance
        else
        {
            // Then we scale by the backwards excursion distance. 
            // First, compute the scaling factor that normalizes the coordinates in Vicon frame
            mappingViconToUnityAndBackApBoundaryScalingFactor = 1.0f / (backwardsExcursionDistance);

            // Compute the distance from the home circle to the edge of the screen along the further anteroposterior excursion direction (F, B)
            float distanceFromHomeCircleToBackwardEdgeInUnityFrame = homeCirclePositionInViewportCoords.y * screenHeightInUnityCoords;

            // Then compute the scaling factor that normalizes the coordinates in Unity frame, such that the forward boundary of stability 
            // is always some proportion of the distance from the home circle to the top edge of the screen.
            mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor = furtherAnteroposteriorBoundaryPercentFromHomeToEdgeOfScreen *
                                                                                 distanceFromHomeCircleToBackwardEdgeInUnityFrame;
        }
    }


    public override string GetCurrentTaskName()
    {
        return thisTaskNameString;
    }

    public override Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame)
    {
        // Carry out the mapping from Vicon frame to Unity frame
        float pointInUnityFrameX = rightwardsSign * mappingViconToUnityAndBackMovingTargetPositionScalingFactor *
            mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor * (pointInViconFrame.x - centerOfBaseOfSupportXPosViconFrame)
            + homeCirclePositionInUnityFrame.x;

        float pointInUnityFrameY = forwardsSign * mappingViconToUnityAndBackApBoundaryScalingFactor *
            mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor * (pointInViconFrame.y - centerOfBaseOfSupportYPosViconFrame)
            + homeCirclePositionInUnityFrame.y;

        //return the point in Unity frame
        return new Vector3(pointInUnityFrameX, pointInUnityFrameY, player.transform.position.z);
    }

    public override Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame)
    {
        // Carry out the mapping from Vicon frame to Unity frame
        // IMPLEMENT!
        float pointInViconFrameX = (pointInUnityFrame.x - homeCirclePositionInUnityFrame.x) / (rightwardsSign * 
            mappingViconToUnityAndBackMovingTargetPositionScalingFactor * mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor)
            + centerOfBaseOfSupportXPosViconFrame; 

        float pointInViconFrameY = (pointInUnityFrame.y - homeCirclePositionInUnityFrame.y) / (forwardsSign *
            mappingViconToUnityAndBackApBoundaryScalingFactor * mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor)
            + centerOfBaseOfSupportYPosViconFrame;

        //return the point in Vicon frame
        return new Vector3(pointInViconFrameX, pointInViconFrameY, player.transform.position.z);
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


    public override bool GetEmgStreamingDesiredStatus()
    {
        return streamingEmgDataFlag;
    }



    private (float, float) convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(float widthXAxisViconFrame, float heightYAxisViconFrame)
    {
        float widthInUnityFrameX = mappingViconToUnityAndBackMovingTargetPositionScalingFactor *
            mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor * widthXAxisViconFrame;

        float heightInUnityFrameY = mappingViconToUnityAndBackApBoundaryScalingFactor *
            mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor * heightYAxisViconFrame;

        return (widthInUnityFrameX, heightInUnityFrameY);
    }


    private (float, float) convertWidthXAndHeightYInUnityFrameToWidthAndHeightInViconFrame(float widthXAxisUnityFrame, float heightYAxisUnityFrame)
    {
        float widthInViconFrameX = widthXAxisUnityFrame /
            (mappingViconToUnityAndBackMovingTargetPositionScalingFactor * mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor);

        float heightInViconFrameX = heightYAxisUnityFrame /
            (mappingViconToUnityAndBackApBoundaryScalingFactor * mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor);

        return (widthInViconFrameX, heightInViconFrameX);
    }



    private Vector3 convertVelocityInViconFrameToVelocityInUnityFrame(Vector3 velocityInViconFrame)
    {
        // Carry out the mapping from Vicon frame to Unity frame
        float velocityInUnityFrameX = rightwardsSign * mappingViconToUnityAndBackMovingTargetPositionScalingFactor *
            mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor * velocityInViconFrame.x;

        float velocityInUnityFrameY = forwardsSign * mappingViconToUnityAndBackApBoundaryScalingFactor *
            mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor * velocityInViconFrame.y;

        //return the point in Unity frame
        return new Vector3(velocityInUnityFrameX, velocityInUnityFrameY, 0.0f);
    }


    private Vector3 convertVelocityInUnityFrameToVelocityInViconFrame(Vector3 velocityInUnityFrame)
    {
        // Carry out the mapping from Unity frame velocity to Vicon frame velocity

        float velocityInViconFrameX = velocityInUnityFrame.x / (rightwardsSign *
            mappingViconToUnityAndBackMovingTargetPositionScalingFactor * mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor);

        float velocityInViconFrameY = velocityInUnityFrame.y / (forwardsSign *
            mappingViconToUnityAndBackApBoundaryScalingFactor * mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor);

        Debug.Log("Converting from Unity frame x-axis velocity, " + velocityInUnityFrame.x + " to Vicon frame x-axis velocity, " + velocityInViconFrameX);


        //return the point in Unity frame
        return new Vector3(velocityInViconFrameX, velocityInViconFrameY, 0.0f);
    }




    private void drawFunctionalBoundaryOfStability()
    {

        //tell the functional boundary of stability drawing object to draw the BoS, if the object is active.
        boundaryOfStabilityRendererScript.renderBoundaryOfStability();
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
            if (currentState == waitingForSetupStateString)
            {
                exitWaitingForSetupState();
            }
            else if (currentState == waitingForHomePositionStateString)
            {
                exitWaitingForHomeState(); 
            }
            else if (currentState == targetMovingStateString)
            {
                exitTargetMovingState();
            }
            else if (currentState == trialFeedbackStateString)
            {
                exitTrialFeedbackState();
            }

            //then call the entry function for the new state
            if (newState == waitingForHomePositionStateString)
            {
                enterWaitingForHomeState();
            }
            else if (newState == targetMovingStateString)
            {
                enterTargetMovingState();
            }
            else if (newState == trialFeedbackStateString)
            {
                enterTrialFeedbackState();
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

    private void enterWaitingForHomeState()
    {

        Debug.Log("Changing to Waiting for Home state");
        // Change the current state to the Waiting For Home state
        currentState = waitingForHomePositionStateString;

        // Reset the "at home" flag to false. Even if the player is at home, we want the system to detect a change
        // of "re-entry" into home so that the timer measuring time at home can restart.
        isPlayerAtHome = false;

        // Set the home circle color to the color indicating the player should move there
        //circleCenter.GetComponent<Renderer>().material.color = Color.red;

    }


    private void exitWaitingForHomeState()
    {
        //set the home circle color to its default
        //circleCenter.GetComponent<Renderer>().material.color = Color.blue;

        //reset and stop the stop watch
        timeAtHomeStopwatch.Reset();
    }


    private void enterTargetMovingState()
    {
        Debug.Log("Changing to Target Moving state.");

        // Change the current state to the Target Moving state
        currentState = targetMovingStateString;

        // Trigger the target to start moving
        StartRandomTargetMovement();

    }


    private void exitTargetMovingState()
    {
        // Nothing needs to happen, I think
    }

    private void enterTrialFeedbackState()
    {
        Debug.Log("Changing to Trial feedback state.");

        // Change the current state to the Trial Feedback state
        currentState = trialFeedbackStateString;

        // Compute the points earned this trial, update the total, and format and show feedback to the user
        ProvideTrialFeedbackToSubjectAndUpdateScore();

        // Store the current trial data so it can be written to file later
        storeTrialData();

        // Increment the trial number
        currentTrialNumber += 1;

        // Figure out what state should occur after we provide feedback (Waiting For Home or Game Over)
        // and if we need to change the striking direction (i.e. a new block with the opposite strike direction has started)
        string nextState;
        bool changeStrikingDirection;
        if(currentTrialNumber <= numberOfTrialsPerBlock) // If we're in the middle of a block, the next state will be Waiting For Home
        {
            nextState = waitingForHomePositionStateString;
            changeStrikingDirection = false;
        }
        else // if the block has been completed
        {
            // Write the frame, trial, and marker data for this block to CSV format on-disk
            tellDataRecorderToWriteStoredDataToFile();

            // The data has been saved to file, so clear it from memory, 
            // as we're writing each block to its own file
            generalDataRecorderScript.clearMarkerAndFrameAndTrialData();

            //increment the block number 
            currentBlockNumber += 1;

            // Reset the trial number
            currentTrialNumber = 1; // trial number starts at 1

            if(currentBlockNumber < blockConditionOrders.Length) // if there are more blocks remaining
            {
                nextState = waitingForHomePositionStateString;
                changeStrikingDirection = strikeDirectionAlternatesEachBlock;

                // Since we already incremented the block number, update the file names for 
                // trial and frame data saving to include the new block number.
                setFileNamesForCurrentBlockTrialAndFrameData();

                // Reset the knockback scaling constant, depending on the current block strike direction
            }
            else // Then the task has ended, so we move to the Game Over state
            {
                nextState = gameOverStateString;
                changeStrikingDirection = false;
            }
        }

        // Start the couroutine that will end the Trial Feedback period and transition the level manager into the next appropriate state
        StartCoroutine(ProvideFeedbackPeriodAtEndOfEachTrialRoutine(timeInactivatedAfterCollisionSeconds,
            playerController, activeTargetThisTrialControlScript, nextState, changeStrikingDirection));

    }


    private void exitTrialFeedbackState()
    {
        // Hide the feedback text? 
    }


    private void enterGameOverState()
    {
        Debug.Log("Changing to Game Over state");

        //change the current state to the Game Over state
        currentState = gameOverStateString;

        //change the instructions text to read that the game is over
        instructionsText.text = "GAME COMPLETE.";

        //then pause the game so that the message can be read. Inactivate the continue button.
        continueFromPauseButton.SetActive(false);
        PauseMenuScript pauseMenuScript = levelCanvas.GetComponent<PauseMenuScript>();
        pauseMenuScript.PauseGame();

    }

    // END: State machine state-transitioning functions *********************************************************************************


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
            homeCircle.transform.position);

       Debug.Log("Player pos is: (" + player.transform.position.x + ", " + player.transform.position.y + ") "
            + "and home pos is : (" + homeCircle.transform.position.x + ", " + homeCircle.transform.position.y + ") " + " and distance between is: "
            + playerDistanceFromCircleCenter);

        // See if the player is at home based on distance from the center in the Unity frame (have they left the home circle?)
        bool isPlayerCurrentlyAtHome = (playerDistanceFromCircleCenter < (0.5f * homeCircleDiameterUnityUnits));

        // If the player is no longer at home but just was, then they have just left home
        bool hasPlayerJustLeftHome = ((isPlayerCurrentlyAtHome == false) && (isPlayerAtHome == true));

        return (isPlayerCurrentlyAtHome, hasPlayerJustLeftHome);
    }




    private void setPlayerStrikeVelocityScalingBasedOnUserInputs()
    {
        targetKnockbackScalingConstantViconFrame = Mathf.Abs(currentTargetLineXPositionInViconFrame - 
            currentStrikeZoneCenterXAxisPositionInViconFrame)
        / (Mathf.Pow(currentPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond, 2));

        

        //set the strike scaling factor on the player
        //Debug.Log("Computed strike scaling factor is: " + strikeVelocityScalingConstantForPlayer);
        //PlayerControllerComDriven playerController = player.GetComponent<PlayerControllerComDriven>();
        //playerController.setTargetStrikeKnockbackScalingFactor(strikeVelocityScalingConstantForPlayer);
    }


    //Set the target (x,y) positions, based on the user's specifications
    private void setTargetPositions()
    {
        List<float> targetYPositionsInWorldCoordinates = new List<float>();
        for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++) //for each target
        {
            GameObject target = targets[targetIndex];
            
            // Set the target position based on our game setup
            target.transform.position = new Vector3(movingTargetsStartingXPositionInUnityFrame, movingTargetsStartingYPositionInUnityFrame, target.transform.position.z);

        }
    }



    public void handlePlayerStrikingTargetEvent(Collider2D struckTargetCollider)
    {
        // See if the target is an "active" advancing target
        TargetController targetControlScript = struckTargetCollider.gameObject.GetComponent<TargetController>();
        bool targetAdvancingStatus = targetControlScript.returnAdvancingStatus();

        // Also, check if the player has already impacted this target (it should only collide once)
        bool impactStunTimeActive = playerController.GetPlayerImpactStunnedStatus();

        // If the target is advancing, then it is our active target that we activated earlier this trial.
        if (!impactStunTimeActive && targetAdvancingStatus) //only respond to a player-target impact if the target is active and if the player is not already stunned from a collision
        {
            // Set the flag indicating that the target was not missed this trial (it was intercepted)
            targetMissedThisTrial = false;

            //get needed components from the target
            Rigidbody2D struckTargetRigidBody = struckTargetCollider.GetComponent<Rigidbody2D>();
            BoxCollider2D struckTargetBoxCollider = struckTargetCollider.GetComponent<BoxCollider2D>();

            // Get needed components from the player 
            Rigidbody2D playerRigidBody = player.GetComponent<Rigidbody2D>();
            BoxCollider2D playerBoxCollider = player.GetComponent<BoxCollider2D>();

            // Collect information to record in the trial data from the player and struck target


            // Print some basic info about the setup parameters for debugging
            Debug.Log("Collision: center of strike zone is located at Vicon frame x-axis position: " + currentStrikeZoneCenterXAxisPositionInViconFrame);
            Debug.Log("Collision: strike zone has width (mm) " + strikeZoneWidthInMillimeters + " and boundaries are thus at: (" +
                (currentStrikeZoneCenterXAxisPositionInViconFrame - (strikeZoneWidthInMillimeters / 2.0f)) + ", " +
                (currentStrikeZoneCenterXAxisPositionInViconFrame + (strikeZoneWidthInMillimeters / 2.0f)) + ")");


            //see if the strike occurred in the strike zone
            playerCollisionPositionViconFrame = mapPointFromUnityFrameToViconFrame(player.transform.position);
            Debug.Log("Collision: player collided with target when it had Vicon frame x-axis position: " + playerCollisionPositionViconFrame.x);
            if (Mathf.Abs(playerCollisionPositionViconFrame.x - currentStrikeZoneCenterXAxisPositionInViconFrame) <= (strikeZoneWidthInMillimeters / 2.0f))
            {
                targetCollisionInStrikeZone = true;
                Debug.Log("Collision: player hit target in strike zone.");

            }
            else
            {
                targetCollisionInStrikeZone = false;
                Debug.Log("Collision: player hit target outside strike zone.");
            }

            // Get the player x-axis velocity at collision, in Vicon frame
            float playerXAxisCollisionSpeedViconFrame;
            if (playerController.getPlayerBeingControlledByKeyboardStatus()) // if the player is being controlled by keyboard commands (i.e. we're testing)
            {
                // Then generate a velocity in Vicon frame based on the virtual movements in Unity 
                playerCollisionVelocityViconFrame = convertVelocityInUnityFrameToVelocityInViconFrame(new Vector3(playerRigidBody.velocity.x, playerRigidBody.velocity.y, 0.0f));
                playerXAxisCollisionSpeedViconFrame = playerCollisionVelocityViconFrame.x;
            }
            else // if the player is being controlled by the subject's COM movements (i.e. an actual experiment)
            {
                // get an estimate of the current COM velocity, which is computed by the center of mass manager script
                bool comVelocityIsAvailable;
                (comVelocityIsAvailable, playerCollisionVelocityViconFrame) = centerOfMassManagerScript.getEstimateOfMostRecentCenterOfMassVelocity();
                if (comVelocityIsAvailable)
                {
                    playerXAxisCollisionSpeedViconFrame = playerCollisionVelocityViconFrame.x;
                }
                else
                {
                    playerXAxisCollisionSpeedViconFrame = 0.0f;
                    Debug.LogError("A collision occurred but there were not enough Vicon frames available to compute COM velocity.");
                }
            }

            Debug.Log("Collision: player strike speed along x-axis in Vicon frame was: " + playerXAxisCollisionSpeedViconFrame);

            //set the current player velocity equal to zero so that it will obey the impact physics instead of its old velocity
            playerRigidBody.velocity = new Vector2(0, 0);

            // Store the target's position at the moment of the collision, before updating it to enforce rigid bodies
            targetLocationAtCollisionViconFrame = mapPointFromUnityFrameToViconFrame(struckTargetRigidBody.transform.position);

            // Compute the target knockback distance in Vicon frame (units of mm)
            float targetKnockbackDistanceViconFrameInMm = computeTargetKnockbackDistance(Mathf.Abs(playerXAxisCollisionSpeedViconFrame));

            // Convert the target knockback distance to Unity frame
            (float targetKnockbackDistanceUnityFrame, _) = convertWidthXAndHeightYInViconFrameToWidthAndHeightInUnityFrame(targetKnockbackDistanceViconFrameInMm, 0.0f);
            Debug.Log("Collision: computed target knockback distance of (Vicon [mm], Unity units): (" + targetKnockbackDistanceViconFrameInMm + ", " + targetKnockbackDistanceUnityFrame + ")");

            // Compute where the target should be based on square rigid bodies of player and target 
            float targetImpactXPositionBasedOnRigidBodiesUnityFrame;
            if (blockStrikeDirections[currentBlockNumber] == true) // if striking rightwards
            {
                targetImpactXPositionBasedOnRigidBodiesUnityFrame = player.transform.position.x + (playerBoxCollider.size.y / 2) +
                                (struckTargetBoxCollider.size.y / 2); // we add y-components b/c player and target are rotated 90 degrees!
            } else
            {
                targetImpactXPositionBasedOnRigidBodiesUnityFrame = player.transform.position.x - (playerBoxCollider.size.y / 2) -
                               (struckTargetBoxCollider.size.y / 2); // we subtract y-components b/c player and target are rotated 90 degrees!
            }

            Debug.Log("Collision: width of half of player and target colliders is: " + ((playerBoxCollider.size.y / 2) + (struckTargetBoxCollider.size.y / 2)));

            // Update the target position so that the collision distance is exactly predictable
            Vector3 updatedTargetLocationAtCollisionUnityFrame = activeTargetThisTrialControlScript.updateXPositionAtImpact(targetImpactXPositionBasedOnRigidBodiesUnityFrame);

            // Get the updated target position, to correct for rigid body overlap, in Vicon frame
            updatedTargetLocationAtCollisionViconFrame = mapPointFromUnityFrameToViconFrame(updatedTargetLocationAtCollisionUnityFrame);

            Debug.Log("Collision: target collision location before rigid body overlap correction: " + targetLocationAtCollisionViconFrame);
            Debug.Log("Collision: target collision location after rigid body overlap correction: " + updatedTargetLocationAtCollisionViconFrame);


            // Compute the player knockon distance based on the target knockback distance 
            playerKnockonDistanceAfterCollisionUnityFrame = targetKnockbackDistanceUnityFrame / 2.0f;

            // Compute the target knockback position and player knockon position
            if (blockStrikeDirections[currentBlockNumber] == true) // if the player is striking rightwards
            {
                // target knockback position
                targetPostCollisionKnockbackPositionInViconFrame = new Vector3(updatedTargetLocationAtCollisionViconFrame.x + rightwardsSign *
                    targetKnockbackDistanceViconFrameInMm, updatedTargetLocationAtCollisionViconFrame.y, updatedTargetLocationAtCollisionViconFrame.z);

                //player knockon position
                playerKnockonPositionAfterCollisionUnityFrame = new Vector3(player.transform.position.x + 
                    playerKnockonDistanceAfterCollisionUnityFrame, player.transform.position.y, player.transform.position.z);
            }
            else
            {
                // target knockback position
                targetPostCollisionKnockbackPositionInViconFrame = new Vector3(updatedTargetLocationAtCollisionViconFrame.x - rightwardsSign *
                    targetKnockbackDistanceViconFrameInMm, updatedTargetLocationAtCollisionViconFrame.y, updatedTargetLocationAtCollisionViconFrame.z);

                //player knockon position
                playerKnockonPositionAfterCollisionUnityFrame = new Vector3(player.transform.position.x - 
                    playerKnockonDistanceAfterCollisionUnityFrame, player.transform.position.y, player.transform.position.z);
            }

            // Finally, convert the target knockback positon in Vicon frame to Unity frame
            targetPostCollisionKnockbackPositionInUnityFrame = mapPointFromViconFrameToUnityFrame(targetPostCollisionKnockbackPositionInViconFrame);

            Debug.Log("Target knockback x-axis position is (Vicon frame, Unity frame): (" + targetPostCollisionKnockbackPositionInViconFrame.x + ", " +
                targetPostCollisionKnockbackPositionInUnityFrame.x + ")");


            // Tell the target that a collision occurred (inactivating it) and where to be knocked back to.
            activeTargetThisTrialControlScript.RespondToCollisionWithPlayer(targetPostCollisionKnockbackPositionInUnityFrame,
                targetCollisionInStrikeZone); //tell the target how far to be knocked back and whether or not the impact was in the strike zone     

            // Send post-collision instructions to the player GameObject
            // Sets a flag disabling control of the player position so that feedback can be provided,
            // sets post-collision stun-time, sets the player knockon distance.
            playerController.BeginPostTargetCollisionResponse(playerKnockonPositionAfterCollisionUnityFrame);

            // The level manager should now enter the Trial Feedback state, in which we provide 
            // visual feedback to the user about their performance this trial
            changeActiveState(trialFeedbackStateString);
        }
    }



    // This function is called by the target when it passes a "point-of-no-return", i.e. the subject missed the opportunity to strike the target. 
    // It is one of the two functions that ends a trial, along with the function called by the player when a target is successfully intercepted. 
    public void TargetMissedResponse()
    {

        // Tell the player object that a miss occurred, so that it can become nonresponsive to allow for a feedback period. 
        // Note, the player object will call the level manager's EndOfTrialResponse() after the feedback delay.
        playerController.TargetMissedResponse();

        // Set the flag indicating the target was missed this trial
        targetMissedThisTrial = true;

        // The level manager should transition to the Trial Feedback state
        changeActiveState(trialFeedbackStateString);


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


    // Given the impact velocity in Vicon frame (mm/s), compute the target knockback distance in 
    // Vicon frame (mm)
    private float computeTargetKnockbackDistance(float playerImpactVelocityXViconFrame)
    {
        Debug.Log("The player hit the target with Vicon-frame x-axis velocity: " + playerImpactVelocityXViconFrame);
        Debug.Log("The player hit the target with Unity-frame x-axis velocity: " + (targetKnockbackScalingConstantViconFrame * Mathf.Pow(playerImpactVelocityXViconFrame, 2.0f)));
        Debug.Log("knockbackScaler relating squared vicon frame velocity and knockback : " + targetKnockbackScalingConstantViconFrame);
        return targetKnockbackScalingConstantViconFrame * Mathf.Pow(playerImpactVelocityXViconFrame, 2.0f);
    }



    private void StartRandomTargetMovement()
    {
        //first, see if there's already an active target. If there is, we do not start a target
        bool anyActiveTarget = false;
        for(int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            GameObject target = targets[targetIndex];
            TargetController targetControlScript = target.GetComponent<TargetController>();
            if (targetControlScript.getActiveStatus()) //if the target is active
            {
                anyActiveTarget = true; //then note that at least one target is already active
                break; //end the search, since we know at least one is active
            }
        }

        if (!anyActiveTarget) //if no targets are active
        {
            // Then start a target's movement
            Debug.Log("Level manager starting random target with speed: " + movingTargetXAxisSpeedInUnityFrame);
            int randomTargetIndex = UnityEngine.Random.Range(0, targets.Length);
            GameObject target = targets[randomTargetIndex];
            activeTargetThisTrialControlScript = target.GetComponent<TargetController>();
            activeTargetThisTrialControlScript.StartMovement(movingTargetXAxisSpeedInUnityFrame, blockStrikeDirections[currentBlockNumber],
                currentTargetMissLineXPositionUnityFrame); //set the target moving
        }
    }

    private void ProvideTrialFeedbackToSubjectAndUpdateScore()
    {
        Debug.Log("Level manager providing feedback after target collision or target miss");
        if (targetMissedThisTrial) // If the subject missed the target
        {
            // No points were earned
            pointsEarnedThisTrial = 0;

            // We reset variables that may still be set to the wrong value from the previous trial
            targetHitTooFarThisTrial = false;
            targetHitIntoPenaltyZone = false;

        }
        else // If the subject struck the target
        {
            targetHitIntoPenaltyZone = false; // Initialize as false, in case we're not using the penalty zone at all.
            if (targetCollisionInStrikeZone) //only assign points if the strike was in the strike zone
            {
                if (usingPenaltyZone)
                {
                    (pointsEarnedThisTrial, targetHitTooFarThisTrial, targetHitIntoPenaltyZone) = computePointsEarnedWithPenaltyZone();
                }
                else
                {
                    (pointsEarnedThisTrial, targetHitTooFarThisTrial) = computePointsEarnedWithNoPenaltyZone();
                }

            }
            else //if player struck the target outside the strike zone
            {
                pointsEarnedThisTrial = 0; //no points earned 
            }

            //add the points earned to the total score and update the points display text
            Debug.Log("Earned points: " + pointsEarnedThisTrial);
            totalPointsEarnedByPlayer += pointsEarnedThisTrial;
            pointsText.text = "Points: " + totalPointsEarnedByPlayer.ToString("0.0"); //display with one decimal point
        }

        // Whether the target was missed or intercepted, we provide feedback to the user with a pop-up text
        FormatAndDisplayCollisionFeedbackText();
    }


    private (float, bool) computePointsEarnedWithNoPenaltyZone()
    {
        float pointsEarned = 0;
        bool tooFar = false;
        float distanceToStrikeLineInViconFrameMm = Mathf.Abs(currentTargetLineXPositionInViconFrame - targetPostCollisionKnockbackPositionInViconFrame.x);
        Debug.Log("Distance to strike line: " + distanceToStrikeLineInViconFrameMm);
        //assign points
        if ((distanceToStrikeLineInViconFrameMm / maximumDistanceToEarnAnyPointsViconFrameInMm) <= 1.0f) //if the distance to the strike line is within the point-earning range
        {
            Debug.Log("Assigning non-zero points to collision");
            pointsEarned = (1 - (distanceToStrikeLineInViconFrameMm / maximumDistanceToEarnAnyPointsViconFrameInMm)) * maximumPointsEarned; //assign points based on distance to strike line
        }
        else //if no points were earned because the target stopped too far from the line
        {
            //determine, in the case that no points were earned, whether the target was hit too far or not far enough
            float signedDistanceToStrikeLine = targetPostCollisionKnockbackPositionInViconFrame.x - currentTargetLineXPositionInViconFrame;
            Debug.Log("Assigning zero points to collision");

            if ((rightwardsSign >= 0 && blockStrikeDirections[currentBlockNumber] == true) ||
                (rightwardsSign < 0 && blockStrikeDirections[currentBlockNumber] == false)) // if we're striking rightwards and the x-axes between Unity and Vicon 
                                                                                            // are flipped OR if we're striking leftwards and the x-axes are not flipped
            {
                Debug.Log("Collision fit condition 1.");
                // Then the sign of the error will determine if we hit too far or not far enough
                if (signedDistanceToStrikeLine <= -maximumDistanceToEarnAnyPointsViconFrameInMm) // negative sign of error is not far enough
                {
                    tooFar = false; //hit not far enough
                }
                else if (signedDistanceToStrikeLine >= maximumDistanceToEarnAnyPointsViconFrameInMm) // positive sign of error is too far
                {
                    tooFar = true; //hit too far
                }
            }
            else if ((rightwardsSign < 0 && blockStrikeDirections[currentBlockNumber] == true) ||
                (rightwardsSign >= 0 && blockStrikeDirections[currentBlockNumber] == false)) // In the other two cases (1.) x-axes flipped, striking right and 2.) x-axes not flipped, striking left)
            {
                Debug.Log("Collision fit condition 2.");
                // Then the sign of the error will determine if we hit too far or not far enough
                if (signedDistanceToStrikeLine <= -maximumDistanceToEarnAnyPointsViconFrameInMm) // negative sign of error is too far
                {
                    tooFar = true; //hit too far
                }
                else if (signedDistanceToStrikeLine >= maximumDistanceToEarnAnyPointsViconFrameInMm) // positive sign of error is not far enough
                {
                    tooFar = false; //hit not far enough
                }
            }
            else
            {
                Debug.LogWarning("When computing if strike was too fast or slow, an impossible mix of conditions occurred.");
            }
        }

        //return values
        return (pointsEarned, tooFar);
    }



    private (float, bool, bool) computePointsEarnedWithPenaltyZone()
    {
        float pointsEarned = 0;
        bool tooFar = false;

        //see if the target was knocked back into the penalty zone
        bool inPenaltyZone;
        if (blockStrikeDirections[currentBlockNumber] == true) // if rightwards striking
        {
            if (rightwardsSign > 0) // If further right means a more positive x-axis value
            {
                inPenaltyZone = (targetPostCollisionKnockbackPositionInViconFrame.x >= currentStartOfPenaltyZoneXPositionInViconFrame);
            }
            else // If further right means a more negative x-axis value
            {
                inPenaltyZone = (targetPostCollisionKnockbackPositionInViconFrame.x <= currentStartOfPenaltyZoneXPositionInViconFrame);
            }
        }
        else // if striking leftwards
        {
            if (rightwardsSign > 0) // If further right means a more positive x-axis value
            {
                inPenaltyZone = (targetPostCollisionKnockbackPositionInViconFrame.x <= currentStartOfPenaltyZoneXPositionInViconFrame);
            }
            else // If further right means a more negative x-axis value
            {
                inPenaltyZone = (targetPostCollisionKnockbackPositionInViconFrame.x >= currentStartOfPenaltyZoneXPositionInViconFrame);
            }
        }

        if (!inPenaltyZone) // If the target was knocked back into the penalty zone
        {
            //find the distance of the target position 
            float distanceToStrikeLineInViconFrameMm = Mathf.Abs(currentTargetLineXPositionInViconFrame - targetPostCollisionKnockbackPositionInViconFrame.x);
            Debug.Log("Distance to strike line: " + distanceToStrikeLineInViconFrameMm);
            //assign points
            if (distanceToStrikeLineInViconFrameMm / maximumDistanceToEarnAnyPointsViconFrameInMm <= 1.0F) //if the distance to the strike line is within the point-earning range
            {
                pointsEarned = (1 - (distanceToStrikeLineInViconFrameMm / maximumDistanceToEarnAnyPointsViconFrameInMm)) * maximumPointsEarned; //assign points based on distance to strike line
            }
            else //if no points were earned because the target stopped too far from the line
            {
                float signedDistanceToStrikeLine = currentTargetLineXPositionInViconFrame - targetPostCollisionKnockbackPositionInViconFrame.x;
                //determine, in the case that no points were earned, whether the target was hit too far or not far enough
                if (signedDistanceToStrikeLine < -maximumDistanceToEarnAnyPointsViconFrameInMm)
                {
                    tooFar = true; //hit too far
                }
                else if (signedDistanceToStrikeLine > maximumDistanceToEarnAnyPointsViconFrameInMm)
                {
                    tooFar = false; //hit not far enough
                }
            }
        } else //if the target was knocked back into the penalty zone
        {
            tooFar = true;
            pointsEarned = penaltyZonePointPenalty;
        }
        

        //return values
        return (pointsEarned, tooFar, inPenaltyZone);
    }



    private float ConvertPlayerStrikeXPosition(float playerStrikePositionX)
    {
        PlayerControllerComDrivenInterception playerController= player.GetComponent<PlayerControllerComDrivenInterception>();
        Vector3 respawnPoint = playerController.returnRespawnPoint();
        return (playerStrikePositionX - respawnPoint.x);
    }



    private void FormatAndDisplayCollisionFeedbackText()
    {
        string feedbackText = "";
        if (targetMissedThisTrial)
        {
            feedbackText = "Target missed!";
        }
        else
        {
            if (targetCollisionInStrikeZone) //if the impact was in the strike zone
            {
                if (!targetHitIntoPenaltyZone)
                {  //if the target was not hit into the penalty zone
                    if (pointsEarnedThisTrial != 0) //if the target was knocked back close enough to the line to earn points
                    {
                        feedbackText = "+" + pointsEarnedThisTrial.ToString("0.0"); //display one decimal for the points
                    }
                    else //if the target was knocked back too far or too close
                    {
                        if (targetHitTooFarThisTrial)
                        {
                            feedbackText = "Collision too fast!";
                        }
                        else
                        {
                            feedbackText = "Collision too slow!";
                        }
                    }
                }
                else //if the target was hit back into the penalty zone
                {
                    feedbackText = "Penalty! -" + pointsEarnedThisTrial.ToString("0.0") + "points.";
                }
            }
            else //if the impact was outside of the strike zone
            {
                feedbackText = "Collision outside green area!";
            }
        }
        
        // Set the collision feedback text and make it visible to the user in the correct location.
        // The text will be hidden when the feedback period has ended.
        collisionFeedbackText.gameObject.SetActive(true);
        collisionFeedbackText.text = feedbackText;
    }


    private void HideUserFeedbackPeriodText()
    {
        collisionFeedbackText.gameObject.SetActive(false);
    }



    private void RearrangeUnityEnvironmentForCurrentBlockStrikeDirection()
    {
        // Update the target starting position, starting line, and strike zone position in Vicon frame
        chooseViconFrameEnvironmentSetupGivenCurrentBlockStrikeDirection();

        // Set up the Unity environment to reflect the setup in Vicon frame
        SetUpBlockSpecificFeaturesInUnityFrame();
    }




    public IEnumerator ProvideFeedbackPeriodAtEndOfEachTrialRoutine(float timeProvidedForFeedbackAtEndOfTrialInSeconds, 
        PlayerControllerComDrivenInterception playerController, TargetController activeTargetThisTrialControlScript, string nextState, bool changeStrikingDirection)
    {
        yield return new WaitForSeconds(timeProvidedForFeedbackAtEndOfTrialInSeconds);

        // Inform the player controller script that the feedback period has ended, so the player can be activated
        playerController.StopTheEndOfTrialFeedbackPeriod();

        // Reset the player position to the home position if we're using keyboard as input (for testing)
        if (playerController.getPlayerBeingControlledByKeyboardStatus() == true)
        {
            playerController.transform.position = new Vector3(homeCirclePositionInUnityFrame.x, homeCirclePositionInUnityFrame.y,
                playerController.transform.position.z);
        }

        // Inform the target that was moving (and was either intercepted or missed) that the feedback period has ended, so it can be reset.
        activeTargetThisTrialControlScript.StopTheEndOfTrialFeedbackPeriod();

        // Move the target that was struck back to its original location
        activeTargetThisTrialControlScript.transform.position = new Vector3(movingTargetsStartingXPositionInUnityFrame,
            activeTargetThisTrialControlScript.transform.position.y, activeTargetThisTrialControlScript.transform.position.z);

        // Hide the collision feedback text 
        HideUserFeedbackPeriodText();

        // If we are starting a new block that has the opposite striking direction, then we must set up the Unity environment accordingly. 
        if (changeStrikingDirection)
        {
            RearrangeUnityEnvironmentForCurrentBlockStrikeDirection();
        }

        // Transition out of the Trial Feedback state
        changeActiveState(nextState);
    }


    //Function to respawn the player back at the starting position
    public void RespawnPlayer(PlayerControllerComDrivenInterception playerControlScript, Vector3 playerRespawnPoint)
    {
        playerControlScript.transform.position = playerRespawnPoint;
    }


    //This function will set up the level to fit the current experimental condition. 
    //As of right now, not needed.
    public void SetUpCurrentExperimentalCondition()
    {
        if(blockConditionOrders[currentBlockNumber] == 1) //Condition 1 = basic, no penalty
        {
            usePenaltyZoneForThisBlock = false;
            penaltyZone.SetActive(false);
            DisplayPreBlockInstructions();
        }

        if (blockConditionOrders[currentBlockNumber] == 2) //Condition 2 = low penalty
        {
            usePenaltyZoneForThisBlock = true;
            penaltyZone.SetActive(true);
            penaltyZonePointPenalty = lowPointPenalty;
            DisplayPreBlockInstructions();
        }

        if (blockConditionOrders[currentBlockNumber] == 3) //Condition 3 = high penalty
        {
            usePenaltyZoneForThisBlock = true;
            penaltyZone.SetActive(true);
            penaltyZonePointPenalty = highPointPenalty;
            DisplayPreBlockInstructions();
        }

    }

    private void DisplayPreBlockInstructions()
    {
        //first change the instructions text
        if (blockConditionOrders[currentBlockNumber] == 1) //Condition 1 = basic, no penalty
        {
            instructionsText.text = "Move the player (blue bar) with the arrow keys. Passing through the moon will trigger an enemy. You must " +
                "intercept the enemy in the green zone. Your goal is to knock the the enemy back, getting it as close to the red line as possible. The closer the enemy " +
                "stops to the red line, the more points you earn.";
        }

        if ((blockConditionOrders[currentBlockNumber] == 2) || (blockConditionOrders[currentBlockNumber] == 3)) //Condition 2 = low penalty or Condition 3 = high penalty
        {
            instructionsText.text = "Move the player (blue bar) with the arrow keys. Passing through the moon will trigger an enemy. You must " +
                "intercept the enemy in the green zone. Your goal is to knock the the enemy back, getting it as close to the red line as possible. The closer the enemy " +
                "stops to the red line, the more points you earn. \n \n " +
                "However, if you knock the enemy back into the red zone, a point penalty is applied. \n \n " +
                "THE PENALTY VALUE HAS BEEN CHANGED TO: " + penaltyZonePointPenalty + " points.";
        }

        //then pause the game so that the instructions can be read. The user hits "continue" to start the block.
        PauseMenuScript pauseMenuScript = levelCanvas.GetComponent<PauseMenuScript>();
        pauseMenuScript.PauseGame();
    }





    // BEGIN: Saving data to file functions *********************************************************************************

    private void setFrameAndTrialDataNaming()
    {
        // 1.) Frame data naming
        // A string array with all of the frame data header names
        string[] csvFrameDataHeaderNames = new string[]{"TIME_AT_UNITY_FRAME_START", "COM_POS_X","COM_POS_Y", "COM_POS_Z", "IS_COM_POS_FRESH_FLAG",
           "MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER", "CURRENT_LEVEL_MANAGER_STATE_AS_FLOAT", "BLOCK_NUMBER", "TRIAL_NUMBER",
           "PLAYER_POS_UNITY_FRAME_X", "PLAYER_POS_UNITY_FRAME_Y", "PLAYER_POS_VICON_FRAME_X", "PLAYER_POS_VICON_FRAME_Y",
           "PLAYER_VELOCITY_ESTIMATE_VICON_FRAME_X", "PLAYER_VELOCITY_ESTIMATE_VICON_FRAME_Y",
           "IS_PLAYER_AT_HOME_FLAG", "IS_TIME_AT_HOME_STOPWATCH_RUNNING_FLAG", "PLAYER_TIME_AT_HOME_STOPWATCH_TIME_MS",
           "IS_TARGET_ACTIVE_FLAG", "ACTIVE_TARGET_POSITION_UNITY_FRAME_X",
           "ACTIVE_TARGET_POSITION_UNITY_FRAME_Y", "ACTIVE_TARGET_POSITION_VICON_FRAME_X", "ACTIVE_TARGET_POSITION_VICON_FRAME_Y",
           "ACTIVE_TARGET_MISSED_FLAG"};

        //tell the data recorder what the CSV headers will be
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvFrameDataHeaderNames);

        // 2.) Trial data naming
        // A string array with all of the header names
        string[] csvTrialDataHeaderNames = new string[]{"BLOCK_NUMBER", "TRIAL_NUMBER", "STRIKING_DIRECTION_IS_RIGHTWARDS",
            "FRACTION_OF_MOS_TRAVERSED_AT_PERFECT_CENTER_STRIKE_ZONE_HIT", "PERFECT_STRIKE_COM_TO_XCOM_DISTANCE_AS_MOS_FRACTION",
            "PERFECT_STRIKE_VEL_AT_CENTER_STRIKE_ZONE_VICON_MM_PER_S",
            "TIME_FOR_TARGET_TO_CROSS_STRIKE_ZONE_SECONDS", "TIME_FOR_TARGET_TO_REACH_STRIKE_ZONE_AFTER_MOVEMENT_START_SECONDS",
            "BODY_INVERTED_PENDULUM_LENGTH_MM", "MAX_DISTANCE_TO_EARN_POINTS_VICON_FRAME_MM",
            "HOME_CIRCLE_CENTER_POS_X_UNITY_FRAME", "HOME_CIRCLE_CENTER_POS_Y_UNITY_FRAME","HOME_CIRCLE_DIAMETER_UNITY_FRAME",
            "CURRENT_STRIKE_ZONE_CENTER_VICON_FRAME_X",
            "CURRENT_STRIKE_ZONE_NEAR_EDGE_VICON_FRAME_X", "CURRENT_STRIKE_ZONE_FAR_EDGE_VICON_FRAME_X", "STRIKE_ZONE_WIDTH_MM",
            "STRIKE_ZONE_GAME_OBJECT_CENTER_POS_UNITY_FRAME_X", "STRIKE_ZONE_GAME_OBJECT_EXTENTS_X", "CURRENT_TARGET_LINE_POS_X_VICON_FRAME",
            "CURRENT_TARGET_LINE_GAME_OBJECT_X_POS", "CURRENT_MOVING_TARGET_STARTING_X_POS_VICON_FRAME",
            "CURRENT_MOVING_TARGET_STARTING_X_POS_UNITY_FRAME", "TARGET_X_AXIS_SPEED_VICON_FRAME", "TARGET_X_AXIS_SPEED_UNITY_FRAME",
            "TARGET_DEEMED_A_MISS_X_AXIS_POS_VICON_FRAME", "TARGET_DEEMED_A_MISS_X_AXIS_POS_UNITY_FRAME",
            "PLAYER_OBJECT_WIDTH_UNITY_UNITS", "PLAYER_OBJECT_WIDTH_VICON_UNITS",  "TARGET_OBJECT_WIDTH_UNITY_UNITS", 
            "TARGET_OBJECT_WIDTH_VICON_UNITS", "LEFT_BASEOFSUPPORT_VICON_POS_X",
            "RIGHT_BASE_OF_SUPPORT_VICON_POS_X", "FRONT_BASEOFSUPPORT_VICON_POS_Y", "BACK_BASEOFSUPPORT_VICON_POS_Y",
            "BASE_OF_SUPPORT_CENTER_X", "BASE_OF_SUPPORT_CENTER_Y", "EXCURSION_DISTANCE_DIR_0_VICON_MM", "EXCURSION_DISTANCE_DIR_1_VICON_MM",
            "EXCURSION_DISTANCE_DIR_2_VICON_MM", "EXCURSION_DISTANCE_DIR_3_VICON_MM", "EXCURSION_DISTANCE_DIR_4_VICON_MM",
            "EXCURSION_DISTANCE_DIR_5_VICON_MM", "EXCURSION_DISTANCE_DIR_6_VICON_MM", "EXCURSION_DISTANCE_DIR_7_VICON_MM",
            "UNITY_VICON_MAPPING_FCN_X_AXIS_VICON_FRAME_NORMALIZE_BY_TARGET_SCALER",
            "UNITY_VICON_MAPPING_FCN_X_AXIS_UNITY_FRAME_FIT_TO_SCREEN_SCALER",
            "UNITY_VICON_MAPPING_FCN_RIGHTWARD_SIGN_AXIS_FLIP", "UNITY_VICON_MAPPING_FCN_Y_AXIS_VICON_FRAME_NORMALIZE_BY_AP_BOUNDARY",
            "UNITY_VICON_MAPPING_FCN_Y_AXIS_UNITY_FRAME_FIT_TO_SCREEN_SCALER","UNITY_VICON_MAPPING_FCN_FORWARD_SIGN_AXIS_FLIP",
            "STIMULATION_STATUS", "PLAYER_COLLISION_X_POS_VICON_FRAME", "PLAYER_COLLISION_Y_POS_VICON_FRAME",
            "PLAYER_COLLISION_X_SPEED_VICON_FRAME_MM_PER_S", "PLAYER_COLLISION_Y_SPEED_VICON_FRAME_MM_PER_S",
            "PLAYER_COLLISION_Z_SPEED_VICON_FRAME_MM_PER_S", "TARGET_COLLISION_X_POS_VICON_FRAME", "TARGET_COLLISION_Y_POS_VICON_FRAME",
            "TARGET_COLLISION_X_POS_UPDATED_FOR_RIGID_BODY_OVERLAP_VICON_FRAME", "TARGET_COLLISION_Y_POS_UPDATED_FOR_RIGID_BODY_OVERLAP_VICON_FRAME",
            "TARGET_KNOCKBACK_VICON_SPEED_SCALER", "TARGET_FINAL_KNOCKBACK_X_POS_VICON_FRAME",
            "DID_COLLISION_OCCUR_IN_STRIKE_ZONE", "WAS_THE_TARGET_MISSED_THIS_TRIAL", "TARGET_HIT_TOO_FAR_FLAG",
            "POINTS_EARNED_THIS_TRIAL"};

        //tell the data recorder what the trial data CSV headers will be
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvTrialDataHeaderNames);

        // 3.) Data subdirectory naming for the task's data
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).

        // Now that we've done this formatting, we should be able to reconstruct the directory containing the 
        // Excursion limits collected on the same day.
        subdirectoryWithBoundaryOfStabilityData = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + "Excursion" + "/" + dateString + "/";

        // Build the name of the subdirectory that will contain all of the output files for trajectory trace this session
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + nameOfThisTask + "/" + dateString + "/";

        //set the frame data and the task-specific trial subdirectory name (will go inside the CSV folder in Assets)
        subdirectoryName = subdirectoryString; //store as an instance variable so that it can be used for the marker and trial data
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);

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
        string fileNameStub = nameOfThisTask + delimiter + subjectSpecificInfoString + delimiter + currentStimulationStatus + delimiter + dateAndTimeString + delimiter + blockNumberAsString;
        mostRecentFileNameStub = fileNameStub; //save the file name stub as an instance variable, so that it can be used for the marker and trial data
        string fileNameFrameData = fileNameStub + "_Frame.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvFrameDataFileName(fileNameFrameData);

        // Set the task-specific trial data file name
        string fileNameTrialData = fileNameStub + "_Trial.csv"; //the final file name. Add any block-specific info!
        generalDataRecorderScript.setCsvTrialDataFileName(fileNameTrialData);
    }




    // This function stores data generated in each Unity frame (FixedUpdate() call) by sending a "row" 
    // of data to the general data recorder object.
    private void storeFrameData()
    {
        // The list that will store the data
        List<float> frameDataToStore = new List<float>();
        // Note, the header names for all of the data we will store are specifed in Start()

        // Get the time called at the beginning of this frame (this call to Update())
        frameDataToStore.Add(Time.time); // Time.time does just that - gets the time at the start of the Update() call.


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

        // Store the level manager state, using a float to represent the state.
        float currentStateFloat = -1.0f;
        if (currentState == waitingForSetupStateString)
        {
            currentStateFloat = 0.0f;
        }
        else if (currentState == waitingForHomePositionStateString)
        {
            currentStateFloat = 1.0f;
        }
        else if (currentState == targetMovingStateString)
        {
            currentStateFloat = 2.0f;
        }
        else if (currentState == trialFeedbackStateString)
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

        // Get the block # and trial #
        float blockNumber = (float)currentBlockNumber;
        frameDataToStore.Add(blockNumber);
        float trialNumber = (float)currentTrialNumber; //we only have trials for now. Should we implement a block format for excursion?
        frameDataToStore.Add(trialNumber);

        // Store the player position in Unity frame
        frameDataToStore.Add(player.transform.position.x);
        frameDataToStore.Add(player.transform.position.y);

        // Store the player position in Vicon frame
        Vector3 playerPositionViconFrame = mapPointFromUnityFrameToViconFrame(player.transform.position);
        frameDataToStore.Add(playerPositionViconFrame.x);
        frameDataToStore.Add(playerPositionViconFrame.y);

        // Store the player/COM velocity estimate in Vicon frame
        (_, Vector3 playerVelocityViconFrame) = centerOfMassManagerScript.getEstimateOfMostRecentCenterOfMassVelocity();
        frameDataToStore.Add(playerVelocityViconFrame.x);
        frameDataToStore.Add(playerVelocityViconFrame.y);

        // Is player at home flag
        frameDataToStore.Add(Convert.ToSingle(isPlayerAtHome));

        // Is the stopwatch measuring time at home running?
        frameDataToStore.Add(Convert.ToSingle(timeAtHomeStopwatch.IsRunning));

        // Time at home stopwatch time
        if (timeAtHomeStopwatch.IsRunning) // if the stopwatch is running
        {
            frameDataToStore.Add(timeAtHomeStopwatch.ElapsedMilliseconds); // store the elapsed time in milliseconds
        }
        else // if the stopwatch is not running
        {
            frameDataToStore.Add(0.0f); // store the elapsed time as 0.0 milliseconds
        }

        // Is target active flag
        (bool anyActiveTarget, TargetController targetControlScript) = FindFirstActiveTargetAndGetControlScript();
        frameDataToStore.Add(Convert.ToSingle(anyActiveTarget));

        if (anyActiveTarget)
        {
            Vector3 activeTargetPositionUnityFrame = targetControlScript.transform.position;

            // Active target position in Unity frame
            frameDataToStore.Add(activeTargetPositionUnityFrame.x);
            frameDataToStore.Add(activeTargetPositionUnityFrame.y);

            // Active target position in Vicon frame
            Vector3 activeTargetPositionViconFrame = mapPointFromUnityFrameToViconFrame(activeTargetPositionUnityFrame);
            frameDataToStore.Add(activeTargetPositionViconFrame.x);
            frameDataToStore.Add(activeTargetPositionViconFrame.y);

            // Has the target been missed status flag
            frameDataToStore.Add(Convert.ToSingle(targetControlScript.returnWhetherOrNotTargetHasBeenMissedThisTrialFlag()));
        }
        else
        {
            // Active target position in Unity frame, marked as (0,0) since there is no active target
            frameDataToStore.Add(0.0f);
            frameDataToStore.Add(0.0f);

            // Active target position in Vicon frame, marked as (0,0) since there is no active target
            frameDataToStore.Add(0.0f);
            frameDataToStore.Add(0.0f);

            // Has the target been missed status flag, set to false since there is no active target
            frameDataToStore.Add(Convert.ToSingle(false));
        }

        //Send the data to the general data recorder. It will be stored in memory until it is written to a CSV file.
        generalDataRecorderScript.storeRowOfFrameData(frameDataToStore);

    }


    private (bool, TargetController) FindFirstActiveTargetAndGetControlScript()
    {
        //first, see if there's already an active target. If there is, we do not start a target
        bool anyActiveTarget = false;
        TargetController targetControlScript = null;
        for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            GameObject target = targets[targetIndex];
            targetControlScript = target.GetComponent<TargetController>();
            if (targetControlScript.getActiveStatus()) //if the target is active
            {
                anyActiveTarget = true; //then note that at least one target is already active
                break; //end the search, since we know at least one is active
            }
        }

        // Return the bool indicating if there is an active target, and the corresponding active target's control script, if applicable
        return (anyActiveTarget, targetControlScript);
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

        // Striking direction
        trialDataToStore.Add(Convert.ToSingle(blockStrikeDirections[currentBlockNumber]));

        // Store settings that define the task in Vicon space
        trialDataToStore.Add(fractionOfMarginOfStabilityTraversedAtStrikeOnAverage);
        trialDataToStore.Add(perfectCenterOfStrikeZoneDistanceComToXcomAsPercentOfMos);
        trialDataToStore.Add(currentPerfectStrikeInCenterOfStrikeZoneXVelocityViconFrameMmPerSecond);
        trialDataToStore.Add(timeForTargetToCrossStrikeZoneInSeconds);
        trialDataToStore.Add(timeForTargetToReachStrikeZoneAfterMovementStartInSeconds);
        trialDataToStore.Add(bodyAsInvertedPendulumLengthInMillimeters);
        trialDataToStore.Add(maximumDistanceToEarnAnyPointsViconFrameInMm);

        // Store parameters related to the "home" circle, where the subject must start each movement
        // Home circle Unity frame (x,y) position
        trialDataToStore.Add(homeCirclePositionInUnityFrame.x);
        trialDataToStore.Add(homeCirclePositionInUnityFrame.y);

        // Home circle Unity frame diameter
        trialDataToStore.Add(homeCircleDiameterUnityUnits);

        // Strike zone parameters
        // Center, near, and far edges x-axis positions in Vicon frame
        trialDataToStore.Add(currentStrikeZoneCenterXAxisPositionInViconFrame);
        trialDataToStore.Add(currentNearStrikeZoneEdgeXPosViconFrame);
        trialDataToStore.Add(currentFarStrikeZoneEdgeXPosViconFrame);

        // Strike zone width in mm
        trialDataToStore.Add(strikeZoneWidthInMillimeters);

        // Strike zone game object x-axis position in Unity frame, game object x-axis "extents" parameter
        trialDataToStore.Add(strikeZone.transform.position.x);
        trialDataToStore.Add(strikeZoneBoxCollider.bounds.extents.x);

        // Target line (the red goal line) x-axis position in Vicon frame
        trialDataToStore.Add(currentTargetLineXPositionInViconFrame);

        // Target line (the red goal line) x-axis position in Unity frame
        trialDataToStore.Add(targetLineXPositionInUnityFrame);

        // Moving target starting x-axis position Vicon frame
        trialDataToStore.Add(currentMovingTargetStartingXPositionInViconFrame);

        // Moving target starting x-axis position Unity frame
        trialDataToStore.Add(movingTargetsStartingXPositionInUnityFrame);

        // Moving target x-axis speed Vicon frame
        trialDataToStore.Add(movingTargetXAxisSpeedInViconFrameMmPerSecond);

        // Moving target x-axis speed Unity frame
        trialDataToStore.Add(movingTargetXAxisSpeedInUnityFrame);

        // Target miss line x-axis position in Vicon frame
        trialDataToStore.Add(currentTargetMissedLineXPosViconFrame);

        // Target miss line x-axis position in Unity frame
        trialDataToStore.Add(mapPointFromViconFrameToUnityFrame(new Vector3(currentTargetMissedLineXPosViconFrame, 0.0f, 0.0f)).x);

        // Width of player game object, both in Unity and mapped to Vicon frame
        BoxCollider2D playerBoxCollider = player.GetComponent<BoxCollider2D>();
        float widthOfPlayerUnityUnits = playerBoxCollider.size.y; // we use the y-component b/c player and target are rotated 90 degrees!
        (float widthOfPlayerViconFrame, _) = convertWidthXAndHeightYInUnityFrameToWidthAndHeightInViconFrame(widthOfPlayerUnityUnits, 0.0f); // we use the y-component b/c player and target are rotated 90 degrees!
        trialDataToStore.Add(widthOfPlayerUnityUnits);
        trialDataToStore.Add(widthOfPlayerViconFrame);

        // Width of the target game object, both in Unity and mapped to Vicon frame.
        BoxCollider2D targetBoxCollider = targets[0].GetComponent<BoxCollider2D>(); // get the collider for the first target in the targets GameObject array
        float widthOfTargetUnityUnits = targetBoxCollider.size.y; // we use the y-component b/c player and target are rotated 90 degrees!
        (float widthOfTargetViconUnits, _) = convertWidthXAndHeightYInUnityFrameToWidthAndHeightInViconFrame(widthOfTargetUnityUnits, 0.0f);
        trialDataToStore.Add(widthOfTargetUnityUnits);
        trialDataToStore.Add(widthOfTargetViconUnits);

        // Get edges of base of support
        trialDataToStore.Add(leftEdgeBaseOfSupportXPosInViconCoords);
        trialDataToStore.Add(rightEdgeBaseOfSupportXPosInViconCoords);
        trialDataToStore.Add(frontEdgeBaseOfSupportYPosInViconCoords);
        trialDataToStore.Add(backEdgeBaseOfSupportYPosInViconCoords);
        trialDataToStore.Add(centerOfBaseOfSupportXPosViconFrame); // Also stored in trial data (so redundant), but now can analyze frame data on its own
        trialDataToStore.Add(centerOfBaseOfSupportYPosViconFrame); // Also stored in trial data (so redundant), but now can analyze frame data on its own

        // Store the read-in excursion distances along each axis. This gives us confidence about which file was read into Unity, when we analyze the data.
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[0]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[1]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[2]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[3]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[4]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[5]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[6]);
        trialDataToStore.Add(excursionDistancesPerDirectionViconFrame[7]);

        //store Vicon to Unity (and back) mapping function variables, x-axis
        trialDataToStore.Add(mappingViconToUnityAndBackMovingTargetPositionScalingFactor);
        trialDataToStore.Add(mappingViconToUnityAndBackPositionTargetAtPercentOfScreenScalingFactor);
        trialDataToStore.Add(rightwardsSign);

        //store Vicon to Unity (and back) mapping function variables, y-axis
        trialDataToStore.Add(mappingViconToUnityAndBackApBoundaryScalingFactor);
        trialDataToStore.Add(mappingViconToUnityAndBackApBoundaryAtPercentOfScreenScalingFactor);
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


        //    "STIMULATION_STATUS", "PLAYER_COLLISION_X_POS_VICON_FRAME", "PLAYER_COLLISION_Y_POS_VICON_FRAME",
        //    "PLAYER_COLLISION_X_SPEED_VICON_FRAME_MM_PER_S", "PLAYER_COLLISION_Y_SPEED_VICON_FRAME_MM_PER_S",
        //    "PLAYER_COLLISION_Z_SPEED_VICON_FRAME_MM_PER_S", "TARGET_COLLISION_X_POS_VICON_FRAME", "TARGET_COLLISION_Y_POS_VICON_FRAME"
        //    "TARGET_KNOCKBACK_VICON_SPEED_SCALER", "TARGET_FINAL_KNOCKBACK_X_POS_VICON_FRAME",
        //    "DID_COLLISION_OCCUR_IN_STRIKE_ZONE", "WAS_THE_TARGET_MISSED_THIS_TRIAL", "TARGET_HIT_TOO_FAR_FLAG",
        //    "POINTS_EARNED_THIS_TRIAL"};

        // Player collision position (x,y) in Vicon frame
        trialDataToStore.Add(playerCollisionPositionViconFrame.x);
        trialDataToStore.Add(playerCollisionPositionViconFrame.y);

        // Player collision velocity (x,y) in Vicon frame (units are mm/second)
        trialDataToStore.Add(playerCollisionVelocityViconFrame.x);
        trialDataToStore.Add(playerCollisionVelocityViconFrame.y);
        trialDataToStore.Add(playerCollisionVelocityViconFrame.z);

        // Target collision position (x,y) in Vicon frame
        trialDataToStore.Add(targetLocationAtCollisionViconFrame.x);
        trialDataToStore.Add(targetLocationAtCollisionViconFrame.y);

        // Target collision position (x,y), updated to account for rigid body overlap, in Vicon frame
        trialDataToStore.Add(updatedTargetLocationAtCollisionViconFrame.x);
        trialDataToStore.Add(updatedTargetLocationAtCollisionViconFrame.y);

        // Target knockback scaling constant, relating Vicon frame player collision velocity to target knockback distance in mm
        trialDataToStore.Add(targetKnockbackScalingConstantViconFrame);

        // Target knockback final x-axis position in Vicon frame
        trialDataToStore.Add(targetPostCollisionKnockbackPositionInViconFrame.x);

        // The flag indicating whether or not the strike occurred in the strike zone
        trialDataToStore.Add(Convert.ToSingle(targetCollisionInStrikeZone));

        // The flag indicating whether the target was intercepted or missed this trial 
        trialDataToStore.Add(Convert.ToSingle(targetMissedThisTrial));

        // The flag indicating whether the target was hit too far (beyond the strike zone, true) or not far enough (false). 
        // We only ascribe this meaning (true = too far, false = not far enough) if points earned = 0. Otherwise, 
        // the flag's default value is false.
        trialDataToStore.Add(Convert.ToSingle(targetHitTooFarThisTrial));

        //store the points earned this trial 
        trialDataToStore.Add(pointsEarnedThisTrial);

        //send all of this trial's summary data to the general data recorder
        generalDataRecorderScript.storeRowOfTrialData(trialDataToStore.ToArray());
    }





    //This function is called when the frame and trial data should be written to file (typically at the end of a block of data collection). 
    private void tellDataRecorderToWriteStoredDataToFile()
    {
        // Tell the general data recorder to write the frame data to file
        generalDataRecorderScript.writeFrameDataToFile();

        //Also, tell the center of mass Manager object to tell the general data recorder to store the marker/COM data to file. 
        centerOfMassManagerScript.tellDataRecorderToSaveStoredDataToFile(subdirectoryName, mostRecentFileNameStub);

        //Also, tell the general data recorder to write the task-specific trial data to  file
        generalDataRecorderScript.writeTrialDataToFile();
    }




    // END: Saving data to file functions ***********************************************************************************



}
