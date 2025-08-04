//#define ENABLE_LOGS //may want to comment out this define to suppress user-defined logging

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Diagnostics;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;



//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;


// Set force field mode for the pelvis and chest
public enum ForceFieldTypeEnum
{
    Disabled, // all belt tensions set to 0 Newtons
    Transparent, // all belt tensions set to minimum possible tension
    Active
}

public enum GravityCompensationTypeEnum
{
    Disabled,
    GravityComp
}

// We either compensate for 100% of torques from higher segment forces or do not compensate at all.
public enum TorqueCompensationTypeEnum
{
    Disabled,
    FullCompensation
}

public enum PdControlTypeEnum
{
    Disabled, 
    ConstantPosition, 
    PositionTrajectory
}

public enum BoundaryForceFieldTypeEnum
{
    Disabled,
    MeasuredLimits,
    EstimatedLimits
}

public enum ChestForceTypeEnum
{
    ground_plane, 
    perpendicular_to_trunk
}

public enum PelvicForceTypeEnum
{
    ground_plane, // all belt tensions set to 0 Newtons
    perpendicular_to_thigh, // all belt tensions set to minimum possible tension
    no_constraints
}

public enum PelvicForceJointsToControl
{
    ankles_only, 
    ankles_and_knee
}

// Right now we'll make the number of pelvic cables be either 4 (for a more simple, planar approach)
// or 8 (full setup).
public enum PelvicBeltCableNumberSelector
{
    Four,
    Eight
};


public class ForceFieldHighLevelControllerScript : MonoBehaviour
{

    // Chest control enum
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class ChestForceControlPropertiesClass
    {
        // Public properties
        // The FF type
        public ForceFieldTypeEnum chestForceFieldType;
        // Chest force constraints on direction (e.g. ground plane)
        public ChestForceTypeEnum chestForceDirection;
        // The gravity compensation settings selector
        public GravityCompensationTypeEnum chestGravityCompSettings;
        // Chest gravity compensation outside/inside boundaries
        public float chestGravityCompInsideBounds; // fraction represents percent assistance (e.g. 0.50 = 50%). 
        public float chestGravityCompOutsideBounds; // fraction represents percent assistance (e.g. 0.50 = 50%). Overwritten with
                                                           // inside bounds value if bounds not used. 
        // Bubble-type PD and bubble radius
        public PdControlTypeEnum chestPdControlSettings;
        public bool useBubbleTypePdControl;
        public float bubblePdRadiusInMeters;

        // Boundary FF
        public BoundaryForceFieldTypeEnum boundaryForceSettings; 

        // Private properties - to be hard-coded only
        // Bubble FF
        private float chestKpInsideBubble = 0.0f;
        private float chestKpOutsideBubble = 0.0f;
        private float chestKvInsideBubble = 0.0f;
        private float chestKvOutsideBubble = 0.0f;

        // Boundary FF
        private float chestKpInsideBounds = 0.0f; // This number may be a fraction of total body weight over moment-arm.
        private float chestKpOutsideBounds = 1.0f; // This number may be a fraction of total body weight over moment-arm. Overwritten with
                                                   // inside bounds value if bounds not used. 
        private float chestKvInsideBounds = 0.0f; // This number may be a fraction of total body weight over moment-arm.
        private float chestKvOutsideBounds = 1.0f; // This number may be a fraction of total body weight over moment-arm. Overwritten with
                                                   // inside bounds value if bounds not used.
        private bool modifyChestPdWithProximityFlag = false; // whether or not PD values change with proximity to the desired control point.

        // Getters for private properties
        // Bubble FF
        public float GetKpInsideBubble()
        {
            return chestKpInsideBubble;
        }

        public float GetKvInsideBubble()
        {
            return chestKvInsideBubble;
        }

        public float GetKpOutsideBubble()
        {
            return chestKpOutsideBubble;
        }

        public float GetKvOutsideBubble()
        {
            return chestKvOutsideBubble;
        }
        // Boundary FF
        public float GetKpInsideBoundary()
        {
            return chestKpInsideBounds;
        }

        public float GetKvInsideBoundary()
        {
            return chestKvInsideBounds;
        }

        public float GetKpOutsideBoundary()
        {
            return chestKpOutsideBounds;
        }

        public float GetKvOutsideBoundary()
        {
            return chestKvOutsideBounds;
        }

    }

    // Pelvis control enum
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class PelvisForceControlPropertiesClass
    {
        // Public properties
        // Number of cables
        public PelvicBeltCableNumberSelector pelvicBeltCableNumberSelector;
        // The FF type
        public ForceFieldTypeEnum pelvisForceFieldType;
        // The gravity compensation settings selector
        public GravityCompensationTypeEnum pelvisGravityCompSettings;
        // The multisegment torque compensation settings selector (e.g., should pelvis counteract chest force-generated torques?)
        public TorqueCompensationTypeEnum multisegmentTorqueCompSettings; 
        // Which direction to compute the non-vertical/non-body-weight-support aspect of the force.
        public PelvicForceTypeEnum pelvicForceDirection;
        // Which joints to control torque at. 
        // Note that how this is accomplished may depend on the pelvicForceDirection constraints.
        public PelvicForceJointsToControl pelvicForceControlledJoints;
        // Ignores the vertical force component, if used.
        // Pelvis gravity compensation outside/inside boundaries
        public float pelvisGravityCompInsideBounds = 0.36f; // fraction represents percent assistance (e.g. 0.50 = 50%). 
        public float pelvisGravityCompOutsideBounds = 0.36f; // fraction represents percent assistance (e.g. 0.50 = 50%). Overwritten with
                                                             // inside bounds value if bounds not used. 
        // Bubble-type PD and bubble radius
        public PdControlTypeEnum pelvisPdControlSettings;
        public bool useBubbleTypePdControl;
        public float bubblePdRadiusInMeters;

        // Boundary FF
        public BoundaryForceFieldTypeEnum boundaryForceSettings;

        // The use of vertical force
        public bool useVerticalForce;
        public float verticalForceBodyWeightFraction;


        // Private properties - to be hard-coded only
        // Bubble FF
        private float pelvisKpInsideBubble = 0.0f;
        private float pelvisKpOutsideBubble = 0.0f;
        private float pelvisKvInsideBubble = 0.0f;
        private float pelvisKvOutsideBubble = 0.0f;

        // Boundary FF
        private float pelvisKpInsideBounds = 0.0f; // This number may be a fraction of total body weight over moment-arm.
        private float pelvisKpOutsideBounds = 0.0f; // This number may be a fraction of total body weight over moment-arm. Overwritten with
                                                    // inside bounds value if bounds not used. 
        private float pelvisKvInsideBounds = 0.0f; // This number may be a fraction of total body weight over moment-arm.
        private float pelvisKvOutsideBounds = 1.0f; // This number may be a fraction of total body weight over moment-arm. Overwritten with
                                                    // inside bounds value if bounds not used.

        // This is a pre-bubble flag. See if it's useful anywhere, if not, delete.
        private bool modifyPelvisPdWithProximityFlag = false; // whether or not PD values change with proximity to the desired control point.

        // Getters for private properties
        // Bubble FF
        public float GetKpInsideBubble()
        {
            return pelvisKpInsideBubble;
        }

        public float GetKvInsideBubble()
        {
            return pelvisKvInsideBubble;
        }

        public float GetKpOutsideBubble()
        {
            return pelvisKpOutsideBubble;
        }

        public float GetKvOutsideBubble()
        {
            return pelvisKvOutsideBubble;
        }
        // Boundary FF
        public float GetKpInsideBoundary()
        {
            return pelvisKpInsideBounds;
        }

        public float GetKvInsideBoundary()
        {
            return pelvisKvInsideBounds;
        }

        public float GetKpOutsideBoundary()
        {
            return pelvisKpOutsideBounds;
        }

        public float GetKvOutsideBoundary()
        {
            return pelvisKvOutsideBounds;
        }
    }

    // Shank control enum
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class ShankForceControlPropertiesClass
    {
        // Public properties
        // The FF type
        public ForceFieldTypeEnum shankForceFieldType;

        // The gravity compensation settings selector
        public GravityCompensationTypeEnum shankGravityCompSettings;

        // The multisegment torque compensation settings selector (e.g., should shank counteract chest or pelvis force-generated torques?)
        public TorqueCompensationTypeEnum multisegmentTorqueCompSettings;

        // Private properties
        // Shank force Kp value (not currently used)
        private float shankForceKp = 1.0f; // multiplied by the avereage knee angle in degrees.

    }

    // The force field control classes, by segment
    // Chest 
    public ChestForceControlPropertiesClass chestForceFieldSettings;
    public PelvisForceControlPropertiesClass pelvisForceFieldSettings;
    public ShankForceControlPropertiesClass shankForceFieldSettings;


    // Whether or not to apply a cable tension rate limiter. 
    // For now, we will choose to apply a pelvis rate limiter 
    // based on the force field type, so we will leave the enum private. 
    // Initialize as disabled.
    private float maximumCableTensionRateNewtonsPerSecond = 100.0f; // Max cable tension rate change in N/sec. Only applies if enabled for a given belt
    private CableTensionRateLimiterEnableEnum trunkCableTensionRateLimiterEnableEnum = CableTensionRateLimiterEnableEnum.Disabled; // disabled
    private CableTensionRateLimiterEnableEnum pelvicCableTensionRateLimiterEnableEnum = CableTensionRateLimiterEnableEnum.Disabled; // disabled;
    private CableTensionRateLimiterEnableEnum rightShankCableTensionRateLimiterEnableEnum = CableTensionRateLimiterEnableEnum.Disabled; // disabled;
    private CableTensionRateLimiterEnableEnum leftShankCableTensionRateLimiterEnableEnum = CableTensionRateLimiterEnableEnum.Disabled; // disabled;
    //public ModelDataSourceSelector dataSourceSelectorInputForControl;


    // Overwrite tensions with constant values?
    public bool constantTensionModeFlag;
    public float constantTensionInNewtons; 
    
    // Flags indicating which belts are being used. Set based on the ForceFieldTypeEnum for each belt. 
    private bool usingChestBelt = false;
    private bool usingPelvicBelt = false;
    private bool usingShankBelts = false;
   






    // Gravity constant 
    private float gravity = 9.81f;
    private float convertMillimetersToMeters = 1000.0f;

    // Subject characteristics
    private float verticalDistanceMidAnkleToMidTrunkBeltInMeters;
    private float subjectMass; // [kg]
    private float springConstantTrunkBelt; // The position-based spring constant for the trunk belt FF

    // Joint torques due to gravity in most recent frame
    private double[] jointAngles; 
    private double[] jointTorquesFromGravity;

    // Desired forces and torques on subject chest
    private Vector3 desiredForcesOnTrunkFrame0;
    private Vector3 desiredTorquesOnTrunkViconFrame;
    private Vector3 desiredForcesOnTrunkViveFrame;
    private Vector3 desiredTorquesOnTrunkViveFrame;
    private Vector<double> resultantJointTorquesFromDesiredChestForce; // the joint torques we would ideally apply
    private Vector<double> resultantJointTorquesFromTensionSolvedChestForce; // the joint torques we try to apply after solving for feasible tensions

    // Desired forces and torques on subject pelvis
    private Vector3 desiredForcesOnPelvisViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredTorquesOnPelvisViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredForcesOnPelvisViveFrame = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredTorquesOnPelvisViveFrame = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredForcesOnPelvisFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredTorquesOnPelvisFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
    // Possibly modified pelvic forces. Modified to meet constraints set by the attachment/pulley arrangement (structure matrix constraints)
    private Vector3 possiblyModifiedForcesOnPelvisFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector<double> resultantJointTorquesFromDesiredPelvicForce; // the joint torques we would ideally apply
    private Vector<double> resultantJointTorquesFromPossiblyModifiedPelvicForce; // the joint torques we apply after modifying pelvic forces to meet structure matrix constraints.
    private Vector<double> resultantJointTorquesFromTensionSolvedPelvicForce; // the joint torques we try to apply after solving for feasible tensions

    private Vector3 forceFromCableOnPelvis;
    // Desired forces and torques on subject shank
    private Vector3 desiredForcesOnShankViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredTorquesOnShankViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredForcesOnShankFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 desiredTorquesOnShankFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector<double> resultantJointTorquesFromDesiredShankForce; // the joint torques we would ideally apply
    private Vector<double> resultantJointTorquesFromTensionSolvedShankForce; // the joint torques we try to apply after solving for feasible tensions

    private Vector<double> computedJointTorquesFromTensionSolvedShankForce;
    

    // Set the minimum allowable cable tension here, in Newtons.
    private float minimumCableTension = 15.0f; // [N]
    private float maximumCableTension = 200.0f; // NOTE: this is applied AFTER the quadratic programmer computes tensions. [N]


    // The last computed cable tension solution. 
    // Let the default value be the minimum tension value.
    private float[] lastComputedCableTensionsTrunk;
    private float[] lastComputedCableTensionsPelvis;
    private float lastComputedCableTensionLeftShank;
    private float lastComputedCableTensionRightShank;
    private float timeOfLastCableTensionComputationUnityFrame; 

    // The object that builds the structure matrix for the current frame
    public GameObject BuildStructureMatrixServiceObject; // the object that contains the script that computes structure matrices for the robot
    private BuildStructureMatricesForBeltsThisFrameScript buildStructureMatrixScript; // the script that computes structure matrices
    public GeneralDataRecorder generalDataRecorderScript;                                                                                  // for the robot each frame. The reference is obtained
                                                                                      // when the public function is called that states we're 
                                                                                      // using a cable-driven robot.

    // The vertices of the excursion polygon in Vicon frame
    private List<Vector3> chestBoundaryVerticesViconFrame = new List<Vector3>();
    private List<Vector3> pelvisBoundaryVerticesViconFrame = new List<Vector3>();

    // The scaled (debounce) vertices for the chest
    private List<Vector3> innerChestBoundaryVerticesViconFrame = new List<Vector3>();
    private List<Vector3> outerChestBoundaryVerticesViconFrame = new List<Vector3>();

    // The scaled (debounce) vertices for the chest
    private List<Vector3> innerPelvisBoundaryVerticesViconFrame = new List<Vector3>();
    private List<Vector3> outerPelvisBoundaryVerticesViconFrame = new List<Vector3>();
    
    // The vertices of the excursion polygon in Vicon frame
    private List<Vector3> chestBoundaryVerticesViveFrame = new List<Vector3>();
    private List<Vector3> pelvisBoundaryVerticesViveFrame = new List<Vector3>();

    // The scaled (debounce) vertices for the chest
    private List<Vector3> innerChestBoundaryVerticesViveFrame = new List<Vector3>();
    private List<Vector3> outerChestBoundaryVerticesViveFrame = new List<Vector3>();

    // The scaled (debounce) vertices for the chest
    private List<Vector3> innerPelvisBoundaryVerticesViveFrame = new List<Vector3>();
    private List<Vector3> outerPelvisBoundaryVerticesViveFrame = new List<Vector3>();

    // If perturbations are used in this task or not. 
    public bool perturbationsUsedFlag;

    // The center of mass manager, which can provide trunk belt position 
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    // The Vive tracker data manager
    public ViveTrackerDataManager viveTrackerDataManagerScript;
    // The cable tension planner
    public GameObject CableTensionPlannerObject; // the object that contains the script that has a quadratic programmer to compute cable tensions.
    private CableTensionPlannerScript cableTensionPlannerScript; // the script that has a quadratic programmer to compute cable tensions.

    // The boundary of stability loader/renderer
    public GameObject boundaryOfStabilityRenderer; //the renderer that draws the functional boundary of stability
    private RenderBoundaryOfStabilityScript boundaryOfStabilityRendererScript; // the script in the functional boundary of stability renderer

    // subject-specific data
    public SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data

    // The TCP host that sends data ot the robot
    public GameObject forceFieldRobotTcpServerObject;
    private CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;

    // The perturbation controller 
    public GameObject perturbationControllerObject;
    private PerturbationController perturbationControllerScript;

    // The player game object and script. NOTE: only needed to visualize force vector when using keyboard control. Otherwise, coudl remove. 
    public GameObject player; //  the player game object
    private PlayerControllerComDrivenBasic playerControlScript; // the script attached to player object
    private bool allowKeyboardControlOverPlayer = false;

    // The level manager and script. NOTE: only needed to visualize force vector when using keyboard control,
    // since the level manager can convert unity coordinates to vicon coordinates. Otherwise, could remove. 
    public GameObject levelManager; //  the player game object
    private LevelManagerScriptAbstractClass levelManagerScript; // the script attached to player object

    // Force field active status
    private bool assistiveForceFieldActive = false; // A flag indicating if, per the last call to this function,
                                                    // the FF is active (true) or not (false).
    // import reference to kinematicModelClass script
    public KinematicModelClass kinematicModelStanceScript;

    //instantiate stanceModel variable
    private KinematicModelOfStance stanceModel;
    
    // The force field force visualizer
    public VisualizeForceFieldForceScript forceFieldVisualizerScript;

    // The high-level force field mode for the trunk belt (NOT REALLY USED FOR NOW)
    private string currentTrunkForceFieldMode;
    private string forceFieldAtExcursionBoundaryPositionBasedForcesOnlyString = "TRUNK_MODE_EXCURSION_BOUNDS_POSITION_ONLY";
    private string forceFieldAtExcursionBoundaryPositionAndVelocityBasedForcesString = "TRUNK_MODE_EXCURSION_BOUNDS_POSITION_AND_VELOCITY";

    // Perturbation mode active flag. 
    private bool trunkOngoingPerturbationModeFlag = false; // A perturbation is ongoing when true. Overrides the force field high-level controller. 

    // Boundary of stability FF 
    public bool usingFunctionalStabilityBoundaryForceFieldFlag = false;

    // Key high-level force field constants
    private float chestInnerBoundaryScaler = 0.95f;
    private float chestOuterBoundaryScaler = 1.05f;

    private float debounceBoundaryScaler = 0.95f; // a scale constant for the excursion boundaries.
                                                 // Debounce ensures stability at the boundary and makes a smooth increase in force.
    // Constant values computed during setup, i.e. the first call of this script by the COM manager
    private bool setupOnFirstCallCompleteFlag = false; // if we have finished setup on the first call by the COM manager
    private Vector3 centerOfBaseOfSupportViconFrame;

    // Information about force field state
    private bool boundaryForceFieldWasActiveLastCallFlag = false; // a flag indicating whether the force field was active the last time new Vicon data was read.

    // Whole-workspace-assist force field
    public bool usingWholeWorkspaceAssistForceFieldFlag; // whether or not we're layering on this extra assistive force field
    public float wholeWorkspaceAssistPercentScaler = 0.50f;
    
    // Computed calbe tension
    // Pelvis
    private float[] computedPelvisCableTensionsThisFrame = new float[] { 0.00f, 0.0f, 0.0f, 0.0f }; // reset to zero
    private float[] safePelvisCableTensionsThisFrame = new float[] { 0.00f, 0.0f, 0.0f, 0.0f }; // reset to zero
    // Shank
    private float[] safeLeftShankCableTensionsThisFrame = new float[] { 0.00f }; // start at zero. Note: only one left shank cable
    private float[] safeRightShankCableTensionsThisFrame = new float[] { 0.00f }; // start at zero. Note: only one right shank cable
    
    // Forces on rigid body segments computed from (Structure Matrix) * (Tensions)
    private float[] forcesTorquesOnPelvisFromCable;
    private float[] forcesTorquesOnLeftShank; // 6x1 vector, 3 forces, 3 torques
    private float[] forcesTorquesOnRightShank; // 6x1 vector, 3 forces, 3 torques

    private float[] computedLeftShankCableTensionsThisFrame = new float[] { 0.00f };
    private float[] computedRightShankCableTensionsThisFrame = new float[] { 0.00f };
    
    // DEBUG MODE 
    private bool DEBUG_MODE_FLAG = false; // assume we don't want debug prints. Set by level manager.
    
    // Start is called before the first frame update
    void Start()
    {

        Debug.Log("Start() in high level controller.");
        // Set the trunk FF mode 
        currentTrunkForceFieldMode = forceFieldAtExcursionBoundaryPositionBasedForcesOnlyString;

        // Get the reference to the structure matrix script, since we are using a cable-driven robot
        buildStructureMatrixScript = BuildStructureMatrixServiceObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

        // marker data and center of mass manager script
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // Get the script inside the functional boundary of stability renderer
        GameObject[] boundaryOfStabilityRenderers = GameObject.FindGameObjectsWithTag("BoundaryOfStability");
        if (boundaryOfStabilityRenderers.Length > 0) //if there are any boundary of stability renderers
        {
            boundaryOfStabilityRenderer = boundaryOfStabilityRenderers[0];
        }
        //boundaryOfStabilityRendererScript = boundaryOfStabilityRenderer.GetComponent<RenderBoundaryOfStabilityScript>();

        // Get the reference to the cable tension planner script, since we are using a cable-driven robot
        cableTensionPlannerScript = CableTensionPlannerObject.GetComponent<CableTensionPlannerScript>();

        // Get the communication with force field robot (e.g. RobUST) TCP host script
        forceFieldRobotTcpServerScript = forceFieldRobotTcpServerObject.GetComponent<CommunicateWithRobustLabviewTcpServer>();

        // Get the perturbation controller script
        if (perturbationsUsedFlag)
        {
            perturbationControllerScript = perturbationControllerObject.GetComponent<PerturbationController>();
        }

        // Get the player control script
        playerControlScript = player.GetComponent<PlayerControllerComDrivenBasic>();

        levelManagerScript = levelManager.GetComponent<LevelManagerScriptAbstractClass>();

        // Store the current time as the time the cable tensions were last computed. 
        // This value is only used in the tension rate change limiter, and we must initialize it to something.
        timeOfLastCableTensionComputationUnityFrame = Time.time;

        // Let the default value for the last computed tension solution be the minimum tension value 
        // or zero, depending on if those cables are being used or not. 
        if (usingShankBelts == true)
        {
            lastComputedCableTensionLeftShank = minimumCableTension;
            lastComputedCableTensionRightShank = minimumCableTension;
        }
        else
        {
            lastComputedCableTensionLeftShank = 0.0f;
            lastComputedCableTensionRightShank = 0.0f;
        }


        // Initialize the flags saying which belts we're using
        if (chestForceFieldSettings.chestForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            // Set the flag indicating we're using the chest belt
            usingChestBelt = true;
        }

        // If we're using a pelvis FF
        // Manage the pelvic FF if we're using one (including visualization)\
        Debug.Log("Pelvis force field type: " + pelvisForceFieldSettings.pelvisForceFieldType);
        if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            Debug.Log("Using pelvic belt.");
            // Set the flag indicating we're using the pelvic belt
            usingPelvicBelt = true;

        }

        // If we're using a knee FF
        if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            Debug.Log("Using shank belts.");
            // Set the flag indicating we're using the shank belts
            usingShankBelts = true;
        }
        
        // Set the stance model data column names for the given RobUST setup
        SetStanceModelDataCsvColumnNames();

        // If the pelvic force field force direction is vertical, we'll turn on the cable tension rate change limiter. 
/*        if (pelvisForceFieldSettings.pelvicForceDirection == PelvicForceTypeEnum.vertical)
        {
            pelvicCableTensionRateLimiterEnableEnum = CableTensionRateLimiterEnableEnum.Enabled;
        }
        else // else in any other pelvic force direction mode
        {
            // Don't apply a cable tension rate limiter
            pelvicCableTensionRateLimiterEnableEnum = CableTensionRateLimiterEnableEnum.Disabled;
        }*/
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void SetDebugModeFlag(bool debugModeFlagFromLevelManager)
    {
        DEBUG_MODE_FLAG = debugModeFlagFromLevelManager;
        Debug.Log("Force field high level controller - debug mode flag set to: " + DEBUG_MODE_FLAG);
    }

    public KinematicModelOfStance CreateKinematicModelOfStanceForTestingOnly()
    {
        return kinematicModelStanceScript.CreateKinematicModelOfStance(centerOfMassManagerScript, viveTrackerDataManagerScript);
    }
    
   public void SetStanceModelDataCsvColumnNames()
    {
        // Switch to List<string> for easier manipulation
        List<string> csvStanceModelDataHeaderNames = new List<string>();

        // Set initial column headers, used regardless of which belt is being used
        csvStanceModelDataHeaderNames.AddRange(new string[]
        {
            "TIME_UNITY_FRAME_START",
            "Theta_1", "Theta_2","Theta_3","Theta_4","Theta_5",
            "GRAVITY_TORQUE_ON_JOINT_1","GRAVITY_TORQUE_ON_JOINT_2","GRAVITY_TORQUE_ON_JOINT_3" ,"GRAVITY_TORQUE_ON_JOINT_4","GRAVITY_TORQUE_ON_JOINT_5"
        });

        // Append pelvic belt column headers if we're using the pelvic force field
        if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            csvStanceModelDataHeaderNames.AddRange(new string[]
            {
                "FORCE_ON_PELVIS_DESIRED_X", "FORCE_ON_PELVIS_DESIRED_Y", "FORCE_ON_PELVIS_DESIRED_Z",
                "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_1", "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_2", "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_3",
                "FORCE_ON_PELVIS_MODIFIED_X", "FORCE_ON_PELVIS_MODIFIED_Y", "FORCE_ON_PELVIS_MODIFIED_Z",
                "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_1", "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_2", "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_3",
                "CABLE_TENSION_ON_PELVIS_FRONT_LEFT","CABLE_TENSION_ON_PELVIS_FRONT_RIGHT","CABLE_TENSION_ON_PELVIS_BACK_RIGHT","CABLE_TENSION_ON_PELVIS_BACK_LEFT",
                "SAFE_CABLE_TENSION_ON_PELVIS_FRONT_LEFT","SAFE_CABLE_TENSION_ON_PELVIS_FRONT_RIGHT","SAFE_CABLE_TENSION_ON_PELVIS_BACK_RIGHT","SAFE_CABLE_TENSION_ON_PELVIS_BACK_LEFT",
                "FORCE_FROM_SAFE_CABLE_ON_PELVIS_X", "FORCE_FROM_SAFE_CABLE_ON_PELVIS_Y", "FORCE_FROM_SAFE_CABLE_ON_PELVIS_Z",
                "TORQUE_FROM_SAFE_CABLE_ON_PELVIS_X", "TORQUE_FROM_SAFE_CABLE_ON_PELVIS_Y", "TORQUE_FROM_SAFE_CABLE_ON_PELVIS_Z",
                "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_1", "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_2", "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_3",
                "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_X_FRAME_0", "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_Y_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_Z_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_X_FRAME_0", "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_Y_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_Z_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_X_FRAME_0", "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_Y_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_Z_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_X_FRAME_0", "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_Y_FRAME_0",
                "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_Z_FRAME_0",
                "PELVIC_FL_ATTACHMENT_IN_FRAME_0_X", "PELVIC_FL_ATTACHMENT_IN_FRAME_0_Y", "PELVIC_FL_ATTACHMENT_IN_FRAME_0_Z",
                "PELVIC_FR_ATTACHMENT_IN_FRAME_0_X", "PELVIC_FR_ATTACHMENT_IN_FRAME_0_Y", "PELVIC_FR_ATTACHMENT_IN_FRAME_0_Z", 
                "PELVIC_BR_ATTACHMENT_IN_FRAME_0_X", "PELVIC_BR_ATTACHMENT_IN_FRAME_0_Y", "PELVIC_BR_ATTACHMENT_IN_FRAME_0_Z",
                "PELVIC_BL_ATTACHMENT_IN_FRAME_0_X","PELVIC_BL_ATTACHMENT_IN_FRAME_0_Y","PELVIC_BL_ATTACHMENT_IN_FRAME_0_Z"
            });

            // Append shank belt column headers if we're using the shank force field
            if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Disabled)
            {
                csvStanceModelDataHeaderNames.AddRange(new string[]
                {
                    "FORCE_ON_SHANK_DESIRED_X", "FORCE_ON_SHANK_DESIRED_Y", "FORCE_ON_SHANK_DESIRED_Z",
                    "TORQUE_FROM_SHANK_DESIRED_FORCE_JOINT_1", "TORQUE_FROM_SHANK_DESIRED_FORCE_JOINT_2",              
                    "COMPUTED_CABLE_TENSION_ON_LEFT_SHANK","COMPUTED_CABLE_TENSION_ON_RIGHT_SHANK",             
                    "TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_1","TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_2",             
                    "SAFE_CABLE_TENSION_ON_LEFT_SHANK","SAFE_CABLE_TENSION_ON_RIGHT_SHANK",               
                    "SAFE_FORCE_FROM_LEFT_CABLE_ON_SHANK_X", "SAFE_FORCE_FROM_LEFT_CABLE_ON_SHANK_Y", "SAFE_FORCE_FROM_LEFT_CABLE_ON_SHANK_Z",                 
                    "SAFE_FORCE_FROM_RIGHT_CABLE_ON_SHANK_X", "SAFE_FORCE_FROM_RIGHT_CABLE_ON_SHANK_Y", "SAFE_FORCE_FROM_RIGHT_CABLE_ON_SHANK_Z",           
                    "SAFE_TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_1", "SAFE_TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_2",
                    "PULLEY_POSITION_FOR_LEFT_SHANK_X_FRAME_0", "PULLEY_POSITION_FOR_LEFT_SHANK_Y_FRAME_0",
                    "PULLEY_POSITION_FOR_LEFT_SHANK_Z_FRAME_0",
                    "PULLEY_POSITION_FOR_RIGHT_SHANK_X_FRAME_0", "PULLEY_POSITION_FOR_RIGHT_SHANK_Y_FRAME_0",
                    "PULLEY_POSITION_FOR_RIGHT_SHANK_Z_FRAME_0",
                    "ATTACHMENT_POINT_FOR_LEFT_SHANK_X_FRAME_0", "ATTACHMENT_POINT_FOR_LEFT_SHANK_Y_FRAME_0",
                    "ATTACHMENT_POINT_FOR_LEFT_SHANK_Z_FRAME_0",
                    "ATTACHMENT_POINT_FOR_RIGHT_SHANK_X_FRAME_0", "ATTACHMENT_POINT_FOR_RIGHT_SHANK_Y_FRAME_0",
                    "ATTACHMENT_POINT_FOR_RIGHT_SHANK_Z_FRAME_0"
                });
            }
        
            // Add more non-belt-specific columns 
            
        }

        csvStanceModelDataHeaderNames.AddRange(new string[]
            {
                "ANKLE_CENTER_IN_FRAME_0_X", "ANKLE_CENTER_IN_FRAME_0_Y", "ANKLE_CENTER_IN_FRAME_0_Z",
                "KNEE_CENTER_IN_FRAME_0_X", "KNEE_CENTER_IN_FRAME_0_Y", "KNEE_CENTER_IN_FRAME_0_Z",
                "PELVIS_CENTER_IN_FRAME_0_X", "PELVIS_CENTER_IN_FRAME_0_Y", "PELVIS_CENTER_IN_FRAME_0_Z",
                "CHEST_CENTER_IN_FRAME_0_X", "CHEST_CENTER_IN_FRAME_0_Y", "CHEST_CENTER_IN_FRAME_0_Z"
            });
        // Set the column headers in the data recorder script
        generalDataRecorderScript.setCsvStanceModelDataRowHeaderNames(csvStanceModelDataHeaderNames.ToArray());
    }

    public Vector<double> ConvertVector3ToNetNumericsVector(Vector3 inputVector)
    {
        Vector<double> outputVector = Vector<double>.Build.DenseOfArray(new double[] { inputVector.x, inputVector.y, inputVector.z });
        return outputVector;
    }

    // Return a flag indicating whether the COM manager has called this script to do initial setup.
    public bool GetForceFieldLevelManagerSetupCompleteFlag()
    {
        return setupOnFirstCallCompleteFlag;
    }

    public PelvicBeltCableNumberSelector GetPelvicBeltNumberOfCablesSelector()
    {
        return pelvisForceFieldSettings.pelvicBeltCableNumberSelector;
    }

    // Returns the most recently computed cable tensions for the trunk belt. 
    // These will be sent on to the robot. 
    public float[] GetMostRecentlyComputedTrunkCableTensions()
    {
        return lastComputedCableTensionsTrunk;
    }

    public KinematicModelOfStance GetStanceModel()
    {
        return stanceModel;
    }
    
    // Returns the most recently computed cable tensions for the pelvic belt. 
    // These will be sent on to the robot. 
    public float[] GetMostRecentlyComputedPelvicCableTensions()
    {
        return lastComputedCableTensionsPelvis;
    }

    // Returns the most recently computed cable tensions for the pelvic belt. 
    // These will be sent on to the robot. 
    public float[] GetMostRecentlyComputedRightShankCableTensions()
    {
        return new float[] { lastComputedCableTensionRightShank };
    }

    public float[] GetMostRecentlyComputedLeftShankCableTensions()
    {
        return new float[] { lastComputedCableTensionLeftShank };
    }

    public bool GetAssistiveForceFieldActiveLastCallFlag()
    {
        return boundaryForceFieldWasActiveLastCallFlag;
    }

    
    public ChestForceControlPropertiesClass GetChestBeltSettingsSelector()
    {
        return chestForceFieldSettings;
    }

    public bool GetChestBeltBeingUsedFlag()
    {
        return usingChestBelt;
    }

    public int GetChestBeltAssistanceType()
    {
        // Return the force field enum as an int, it's native underlying type. 
        // I believe the int will be in the order of the declared values, starting at 0.
        return (int) chestForceFieldSettings.chestForceFieldType;
    }


    public PelvisForceControlPropertiesClass GetPelvicBeltSettingsSelector()
    {
        return pelvisForceFieldSettings;
    }

    public bool GetPelvicBeltBeingUsedFlag()
    {
        return usingPelvicBelt;
    }

    public int GetPelvicBeltAssistanceType()
    {
        // Return the force field enum as an int, it's native underlying type. 
        // I believe the int will be in the order of the declared values, starting at 0.
        return (int) pelvisForceFieldSettings.pelvisForceFieldType;
    }

    public float GetPelvicBeltGravCompensationFractionInsideBoundary()
    {
        return pelvisForceFieldSettings.pelvisGravityCompInsideBounds;
    }

    public ShankForceControlPropertiesClass GetShankBeltSettingsSelector()
    {
        return shankForceFieldSettings;
    }

    public bool GetShankBeltsBeingUsedFlag()
    {
        return usingShankBelts;
    }

    public int GetShankBeltAssistanceType()
    {
        // Return the force field enum as an int, it's native underlying type. 
        // I believe the int will be in the order of the declared values, starting at 0.
        return (int) shankForceFieldSettings.shankForceFieldType;
    }

    public void GetForceOnPelvisFromCable()
    {
        
        
    }

    // START: High-level force field controller functions*********************

    // This function is called by the COM manager (with Vicon data) or by the _____ (with Vive data) when new marker/tracker data is ready
    public void ComputeDesiredForceFieldForcesAndTorquesOnSubject()
    {
        PrintDebugIfDebugModeFlagIsTrue("COM manager told high level controller to compute forces.");
        // If the script has not been "set up" yet
        if (!setupOnFirstCallCompleteFlag)
        {
            
            // Do first-call setup
            SetupOnFirstCallByComManager();

            // Note that we've finished first-call setup
            setupOnFirstCallCompleteFlag = true;

            // Note that setup is complete
            PrintDebugIfDebugModeFlagIsTrue("Force field high-level controller: setup complete.");
        }
        else // if setup is complete, then run the force selection and cable tension computation pipeline for all belts
        {
            // For now, just set to a constant for testing
            /*desiredForcesOnTrunkFrame0 = new Vector3(-60.0f, -60.0f, 0.0f);
            desiredTorquesOnTrunkViconFrame = new Vector3(0.0f, 0.0f, 0.0f);*/

            // Only run the high-level controller if our stance model is ready to serve data.
            if (kinematicModelStanceScript.GetStanceModelReadyToServeDataFlag())
            {

                // Get the model's joint angles in radians stored as an instance variable.
                // Calling this updates the model's stored joint variables.
                jointAngles = stanceModel.GetJointVariableValuesFromMarkerDataInverseKinematics();

                PrintDebugIfDebugModeFlagIsTrue("5R model joint angles: ( " + string.Join(", ", jointAngles) + ")");

                // Then store the gravity torque at each joint
                jointTorquesFromGravity = stanceModel.GetGravityTorqueAtEachModelJoint(); // - of gravity torques at all joints

                // Clamp to safe limits
                if (constantTensionInNewtons < 15.0f)
                {
                    constantTensionInNewtons = 15.0f;
                }
                else if (constantTensionInNewtons > 50.0f)
                {
                    constantTensionInNewtons = 50.0f;
                }

                // Manage the chest FF if we're using one (including visualization)
                if (chestForceFieldSettings.chestForceFieldType != ForceFieldTypeEnum.Disabled)
                {
                    // Compute desired chest forces (unless using constant tension mode, in which case we just set constant tensions)
                    ComputeChestForceFieldForces();


                    // Compute cable tensions to generate desired forces (or, as close as possible solution).
                    if (!constantTensionModeFlag)
                    {
                        ComputeChestCableTensionsThisFrame();
                    }
                    else
                    {
                        lastComputedCableTensionsTrunk = new float[] { constantTensionInNewtons, constantTensionInNewtons,
                        constantTensionInNewtons, constantTensionInNewtons };
                    }
                }
                else // if the chest belt is disabled, fill desired tensions with zeros
                {
                    lastComputedCableTensionsTrunk = new float[] { 0.0f, 0.0f,
                        0.0f, 0.0f };
                }

                // Manage the pelvic FF if we're using one (including visualization)
                if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled)
                {
                    // Compute desired pelvic forces
                    if(pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Transparent) // if not transparent mode
                    {
                        ComputePelvisForceFieldForces();
                    }
                    else
                    {
                        // We won't estimate resultant joint torques and just store 0s.
                        resultantJointTorquesFromDesiredPelvicForce = Vector<double>.Build.Dense(3, 0.0);
                    }



                    // Compute cable tensions to generate desired forces (or, as close as possible solution).
                    // Compute cable tensions to generate desired forces (or, as close as possible solution).
                    if (!constantTensionModeFlag && pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Transparent)
                    {

                        ComputePelvicCableTensionsThisFrame();
                    }
                    else // if constant tension flag is set OR we're in transparent mode
                    { 
                        // We'll use the minimum tensions
                        if(pelvisForceFieldSettings.pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Four)
                        {
                            lastComputedCableTensionsPelvis = new float[] { constantTensionInNewtons, constantTensionInNewtons,
                                constantTensionInNewtons, constantTensionInNewtons };
                        }else if (pelvisForceFieldSettings.pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Eight)
                        {
                            lastComputedCableTensionsPelvis = new float[] { constantTensionInNewtons, constantTensionInNewtons,
                                constantTensionInNewtons, constantTensionInNewtons, constantTensionInNewtons, constantTensionInNewtons,
                                constantTensionInNewtons, constantTensionInNewtons };
                        }
                        else
                        {
                            Debug.LogError("Undefined enumerator value for number of pelvic cables!");
                        }

                    }
                }
                else // if the pelvic belt is disabled
                {
                    // Set desired (last computed) tensions for pelvic belt to zero
                    // We'll use the minimum tensions
                    if (pelvisForceFieldSettings.pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Four)
                    {
                        lastComputedCableTensionsPelvis = new float[] { 0.0f, 0.0f,
                                0.0f, 0.0f };
                    }
                    else if (pelvisForceFieldSettings.pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Eight)
                    {
                        lastComputedCableTensionsPelvis = new float[] { 0.0f, 0.0f,
                                0.0f, 0.0f, 0.0f, 0.0f,
                                0.0f, 0.0f };
                    }
                }

                // Manage the knee FF if we're using one (including visualization)
                if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Disabled)
                {

                    //if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Transparent) // if not transparent mode
                    //{
                        // Compute desired shank forces
                        ComputeKneeForceFieldForces();
                    //}


                    // Compute cable tensions to generate desired forces (or, as close as possible solution).
                    if (!constantTensionModeFlag)
                    {
                        ComputeShankCableTensionsThisFrame();
                        
                        if(shankForceFieldSettings.shankForceFieldType == ForceFieldTypeEnum.Transparent)
                        {
                            lastComputedCableTensionLeftShank = constantTensionInNewtons;
                            lastComputedCableTensionRightShank = constantTensionInNewtons;
                        }
                    }
                    else // if we're in constant tensions mode or transparent
                    {

                    }
                }
                else // if we're not using the shank belts
                {
                    // Set the desired (last computed) tensions to zero
                    lastComputedCableTensionLeftShank = 0.0f;
                    lastComputedCableTensionRightShank = 0.0f;
                }

                // Update the time storing when the tensions were last computed
                timeOfLastCableTensionComputationUnityFrame = Time.time;

                // Send computed tensions to TCP service, so they can be forwarded to the robot.
                forceFieldRobotTcpServerScript.SendMotorNumbersAndDesiredTensionsAndViconFrameNumberToRobot();

                // Store key computed values computed on this call to the high-level controller. 
                // This includes key parameters from the stance model (joint angles, grav torques), 
                // from the high level controller (desired belt forces, associated joint torques), 
                // and from the cable tension planner (cable tensions)
                StoreRowOfHighLevelControllerAndStanceModelData();
            }

            // DEBUG ONLY: print out joint torques resulting from gravity, pelvic force (AFTER cable tension computation), 
            // and shank force (computed, before cable tension computation), and sum. 
            // Gravity always has a negative sign, because the computed phi term has a built-in negative sign.
            // While these will NOT sum to zero because we consider achievable cable tensions and may not be providing 100% torque balance, 
            // they can still provide insight into the predicted effects of the intervention.
            /*        float[] summedNetJointTorques = new float[jointTorquesFromGravity.Length];
                    for (int jointIndex = 0; jointIndex < jointTorquesFromGravity.Length; jointIndex++)
                    {
                        if(jointIndex < 2)
                        {
                            summedNetJointTorques[jointIndex] = -jointTorquesFromGravity[jointIndex] +
                                (float) resultantJointTorquesFromTensionSolvedPelvicForce[jointIndex] +
                                (float) resultantJointTorquesFromDesiredShankForce[jointIndex];
                        }
                        else if(jointIndex < 3)
                        {
                            summedNetJointTorques[jointIndex] = -jointTorquesFromGravity[jointIndex] +
                                (float)resultantJointTorquesFromTensionSolvedPelvicForce[jointIndex];
                        }
                        else
                        {
                            summedNetJointTorques[jointIndex] = -jointTorquesFromGravity[jointIndex];
                        }
                    }*/

            /*        Debug.Log("Gravity joint torques (joints 1,2,3,4,5): (" + jointTorquesFromGravity[0] + ", " +
                        jointTorquesFromGravity[1] + ", " + jointTorquesFromGravity[2] + "," +
                        jointTorquesFromGravity[3] + ", " + jointTorquesFromGravity[4] + "), and " +
                        "pelvis cable force joint torques (joints 1,2,3): (" +
                        resultantJointTorquesFromTensionSolvedPelvicForce[0] + ", " + resultantJointTorquesFromTensionSolvedPelvicForce[1] + "," +
                        resultantJointTorquesFromTensionSolvedPelvicForce[2] + "), and " +
                        "shank force computed joint torques (joints 1,2): (" +
                        resultantJointTorquesFromDesiredShankForce[0] + ", " + resultantJointTorquesFromDesiredShankForce[1] + ")" +
                        ", and sum (joints 1,2,3,4,5): (" + summedNetJointTorques[0] + ", " +
                        summedNetJointTorques[1] + ", " + summedNetJointTorques[2] + "," +
                        summedNetJointTorques[3] + ", " + summedNetJointTorques[4] + ")");*/

        }
    }


   // When in debug mode, we'll do extra debug prints by calling this function.
    private void PrintDebugIfDebugModeFlagIsTrue(string debugMessage)
    {
        if (DEBUG_MODE_FLAG == true)
        {
            Debug.Log(debugMessage);
        }
    }

    private void StoreRowOfHighLevelControllerAndStanceModelData()
    {
        /*csvFrameDataHeaderNames = new string[]
                {
                    "TIME_UNITY_FRAME_START",
                    "Theta_1", "Theta_2","Theta_3","Theta_4","Theta_5",
                    "GRAVITY_TORQUE_ON_JOINT_1","GRAVITY_TORQUE_ON_JOINT_2","GRAVITY_TORQUE_ON_JOINT_3" ,"GRAVITY_TORQUE_ON_JOINT_4","GRAVITY_TORQUE_ON_JOINT_5"
                    ,"FORCE_ON_PELVIS_DESIRED_X" , "FORCE_ON_PELVIS_DESIRED_Y","FORCE_ON_PELVIS_DESIRED_Z",
                    "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_1" , "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_2" , "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_3" ,
                    "FORCE_ON_PELVIS_MODIFIED_X" , "FORCE_ON_PELVIS_MODIFIED_Y","FORCE_ON_PELVIS_MODIFIED_Z",
                    "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_1" , "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_2" , "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_3" ,
                    "CABLE_TENSION_ON_PELVIS_FRONT_LEFT","CABLE_TENSION_ON_PELVIS_FRONT_RIGHT","CABLE_TENSION_ON_PELVIS_BACK_RIGHT","CABLE_TENSION_ON_PELVIS_BACK_LEFT",
                    "CABLE_TENSION_SAFE_ON_PELVIS_FRONT_LEFT","CABLE_TENSION_SAFE_ON_PELVIS_FRONT_RIGHT","CABLE_TENSION_SAFE_ON_PELVIS_BACK_RIGHT","CABLE_TENSION_SAFE_ON_PELVIS_BACK_LEFT",
                    "FORCE_FROM_SAFE_CABLE_ON_PELVIS_X", "FORCE_FROM_SAFE_CABLE_ON_PELVIS_Y", "FORCE_FROM_SAFE_CABLE_ON_PELVIS_Z",
                    "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_1", "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_2", "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_3",
                    "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_X", "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_Y", "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_Z",
                    "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_X", "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_Y", "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_Z",
                    "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_X", "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_Y", "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_Z",
                    "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_X", "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_Y", "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_Z",
                    "FORCE_ON_SHANK_DESIRED_X" , "FORCE_ON_SHANK_DESIRED_Y","FORCE_ON_SHANK_DESIRED_Z",
                    "TORQUE_FROM_SHANK_DESIRED_FORCE_JOINT_1" , "TORQUE_FROM_SHANK_DESIRED_FORCE_JOINT_2" ,
                    "CABLE_TENSION_ON_LEFT_SHANK","CABLE_TENSION_ON_RIGHT_SHANK",
                    "SAFE_CABLE_TENSION_ON_LEFT_SHANK","SAFE_CABLE_TENSION_ON_RIGHT_SHANK",
                    "FORCE_FROM_SAFE_CABLE_ON_LEFT_SHANK_X", "FORCE_FROM_SAFE_CABLE_ON_LEFT_SHANK_Y", "FORCE_FROM_SAFE_CABLE_ON_LEFT_SHANK_Z",
                    "FORCE_FROM_SAFE_CABLE_ON_RIGHT_SHANK_X", "FORCE_FROM_SAFE_CABLE_ON_RIGHT_SHANK_Y", "FORCE_FROM_SAFE_CABLE_ON_RIGHT_SHANK_Z",
                    "TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_1","TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_2",
                    "PULLEY_POSITION_FOR_LEFT_SHANK_X", "PULLEY_POSITION_FOR_LEFT_SHANK_Y","PULLEY_POSITION_FOR_LEFT_SHANK_Z",
                    "PULLEY_POSITION_FOR_RIGHT_SHANK_Z","PULLEY_POSITION_FOR_RIGHT_SHANK_Y","PULLEY_POSITION_FOR_RIGHT_SHANK_Z",
                    "KNEE_CENTER_IN_FRAME_0_X","KNEE_CENTER_IN_FRAME_0_Y","KNEE_CENTER_IN_FRAME_0_Z",
                    "PELVIS_CENTER_IN_FRAME_0_X","PELVIS_CENTER_IN_FRAME_0_Y","PELVIS_CENTER_IN_FRAME_0_Z",
                    "CHEST_CENTER_IN_FRAME_0_X","CHEST_CENTER_IN_FRAME_0_Y","CHEST_CENTER_IN_FRAME_0_Z"
                };*/
        
        // Initialize list to store a row of high-level controller and stance model data
        List<float> dataToStore = new List<float>();
        
        
        
    /*    
        "TIME_UNITY_FRAME_START",
        "Theta_1", "Theta_2","Theta_3","Theta_4","Theta_5",
        "GRAVITY_TORQUE_ON_JOINT_1","GRAVITY_TORQUE_ON_JOINT_2","GRAVITY_TORQUE_ON_JOINT_3" ,"GRAVITY_TORQUE_ON_JOINT_4","GRAVITY_TORQUE_ON_JOINT_5"
    });

    // Append pelvic belt column headers if we're using the pelvic force field
    if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled)
    {
        csvStanceModelDataHeaderNames.AddRange(new string[]
        {
            "FORCE_ON_PELVIS_DESIRED_X", "FORCE_ON_PELVIS_DESIRED_Y", "FORCE_ON_PELVIS_DESIRED_Z",
            
            "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_1", "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_2", "TORQUE_FROM_PELVIS_DESIRED_FORCE_JOINT_3",
            
            "FORCE_ON_PELVIS_MODIFIED_X", "FORCE_ON_PELVIS_MODIFIED_Y", "FORCE_ON_PELVIS_MODIFIED_Z",
            
            "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_1", "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_2", "TORQUE_FROM_PELVIS_MODIFIED_FORCE_JOINT_3",
            
            "CABLE_TENSION_ON_PELVIS_FRONT_LEFT","CABLE_TENSION_ON_PELVIS_FRONT_RIGHT","CABLE_TENSION_ON_PELVIS_BACK_RIGHT","CABLE_TENSION_ON_PELVIS_BACK_LEFT",
            
             "SAFE_CABLE_TENSION_ON_PELVIS_FRONT_LEFT","SAFE_CABLE_TENSION_ON_PELVIS_FRONT_RIGHT","SAFE_CABLE_TENSION_ON_PELVIS_BACK_RIGHT","SAFE_CABLE_TENSION_ON_PELVIS_BACK_LEFT",
            
            "FORCE_FROM_SAFE_CABLE_ON_PELVIS_X", "FORCE_FROM_SAFE_CABLE_ON_PELVIS_Y", "FORCE_FROM_SAFE_CABLE_ON_PELVIS_Z",
            
            "TORQUE_FROM_SAFE_CABLE_ON_PELVIS_X", "TORQUE_FROM_SAFE_CABLE_ON_PELVIS_Y", "TORQUE_FROM_SAFE_CABLE_ON_PELVIS_Z",
            
            "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_1", "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_2", "TORQUE_FROM_PELVIC_CABLE_FORCE_ON_JOINT_3",
            
            "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_X", "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_Y", "PULLEY_POSITION_FOR_PELVIS_FRONT_LEFT_Z",
            "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_X", "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_Y", "PULLEY_POSITION_FOR_PELVIS_FRONT_RIGHT_Z",
            "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_X", "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_Y", "PULLEY_POSITION_FOR_PELVIS_BACK_RIGHT_Z",
            "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_X", "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_Y", "PULLEY_POSITION_FOR_PELVIS_BACK_LEFT_Z"
        */
        
        
        
        // START: Store values exported regardless of which belt is used ******************************************************
        // Store time this Unity frame
        dataToStore.Add(Time.time);
        // Store joint model angles
        double[] modelJointAnglesInRads = jointAngles;
        for (uint jointIndex = 0; jointIndex < modelJointAnglesInRads.Length; jointIndex++)
        {
            dataToStore.Add((float) modelJointAnglesInRads[jointIndex]);
        }

        // Store gravity torques at each joint
        for (uint jointIndex = 0; jointIndex < jointTorquesFromGravity.Length; jointIndex++)
        {
            dataToStore.Add((float) -jointTorquesFromGravity[jointIndex]);
        }
        
        // END: Store values exported regardless of which belt is used ******************************************************

        
        // Data output columns will depend on which belts are being used. 

        // TO DO: ADD CHEST BELT DATA! Follow the pelvic belt pattern below.

        // START: Store pelvic belt force/torque/tension values ******************************************************
        // Pelvis belt
        if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled &&
            pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Transparent)
        {
            // Store desired pelvis and chest forces in frame 0
            dataToStore.Add(desiredForcesOnPelvisFrame0.x);
            dataToStore.Add(desiredForcesOnPelvisFrame0.y);
            dataToStore.Add(desiredForcesOnPelvisFrame0.z);
            // Store joint torques from desired pelvic force
            for (int jointIndex = 0; jointIndex < resultantJointTorquesFromDesiredPelvicForce.Count; jointIndex++)
            {
                dataToStore.Add((float)resultantJointTorquesFromDesiredPelvicForce[jointIndex]);
            }

            // Store modified pelvic force (modified = changed by tension solver to be in a feasible direction)
            // FOR NOW, STORE ZEROS
            dataToStore.Add(possiblyModifiedForcesOnPelvisFrame0.x);
            dataToStore.Add(possiblyModifiedForcesOnPelvisFrame0.y);
            dataToStore.Add(possiblyModifiedForcesOnPelvisFrame0.z);
            // Torques due to modified pelvic force
            // FOR NOW STORE ZEROES
            dataToStore.Add((float) resultantJointTorquesFromPossiblyModifiedPelvicForce[0]);
            dataToStore.Add((float) resultantJointTorquesFromPossiblyModifiedPelvicForce[1]);
            dataToStore.Add((float) resultantJointTorquesFromPossiblyModifiedPelvicForce[2]);
            // Cable tensions on pelvis
            for (uint cableIndex = 0; cableIndex < computedPelvisCableTensionsThisFrame.Length; cableIndex++)
            {
                dataToStore.Add(computedPelvisCableTensionsThisFrame[cableIndex]);
            }

            // Safe cable tensions on pelvis
            for (uint cableIndex = 0; cableIndex < safePelvisCableTensionsThisFrame.Length; cableIndex++)
            {
                dataToStore.Add(safePelvisCableTensionsThisFrame[cableIndex]);
            }


            // Net force on pelvis due to cables
            // forcesTorquesOnPelvisFromCable is the safe force calculated by safe tension F = S * "safe"T
            dataToStore.Add(forcesTorquesOnPelvisFromCable[0]);
            dataToStore.Add(forcesTorquesOnPelvisFromCable[1]);
            dataToStore.Add(forcesTorquesOnPelvisFromCable[2]);
            
            dataToStore.Add(forcesTorquesOnPelvisFromCable[3]);
            dataToStore.Add(forcesTorquesOnPelvisFromCable[4]);
            dataToStore.Add(forcesTorquesOnPelvisFromCable[5]);
            
            dataToStore.Add( (float) resultantJointTorquesFromTensionSolvedPelvicForce[0]);
            dataToStore.Add( (float) resultantJointTorquesFromTensionSolvedPelvicForce[1]);
            dataToStore.Add( (float) resultantJointTorquesFromTensionSolvedPelvicForce[2]);
            
            List<Vector3> pulleyPositionInFrame0 = buildStructureMatrixScript.GetPelvicBeltPulleyPositionInFrame0();
            List<Vector3> pelvisBeltAttachmentPointInFrame0 = buildStructureMatrixScript.GetPelvisBeltAttachmentPointInFrame0();
            // FL pulley x,y,z in frame 0
            dataToStore.Add(pulleyPositionInFrame0[0].x);
            dataToStore.Add(pulleyPositionInFrame0[0].y);
            dataToStore.Add(pulleyPositionInFrame0[0].z);
            // FR pulley x,y,z in frame 0
            dataToStore.Add(pulleyPositionInFrame0[1].x);
            dataToStore.Add(pulleyPositionInFrame0[1].y);
            dataToStore.Add(pulleyPositionInFrame0[1].z);
            // BR pulley x,y,z in frame 0
            dataToStore.Add(pulleyPositionInFrame0[2].x);
            dataToStore.Add(pulleyPositionInFrame0[2].y);
            dataToStore.Add(pulleyPositionInFrame0[2].z);
            // BL pulley x,y,z in frame 0
            dataToStore.Add(pulleyPositionInFrame0[3].x);
            dataToStore.Add(pulleyPositionInFrame0[3].y);
            dataToStore.Add(pulleyPositionInFrame0[3].z);
            //FL attachment point x,y,z in frame 0
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[0].x);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[0].y);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[0].z);
            //FR attachment point x,y,z in frame 0
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[1].x);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[1].y);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[1].z);
            //BR attachment point x,y,z in frame 0
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[2].x);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[2].y);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[2].z);
            //BL attachment point x,y,z in frame 0
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[3].x);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[3].y);
            dataToStore.Add(pelvisBeltAttachmentPointInFrame0[3].z);
        }
        
        // END: Store pelvic belt force/torque/tension values ******************************************************

        /*
         "FORCE_ON_SHANK_DESIRED_X", "FORCE_ON_SHANK_DESIRED_Y", "FORCE_ON_SHANK_DESIRED_Z",
                "TORQUE_FROM_SHANK_DESIRED_FORCE_JOINT_1", "TORQUE_FROM_SHANK_DESIRED_FORCE_JOINT_2",              
                "COMPUTED_CABLE_TENSION_ON_LEFT_SHANK","COMPUTED_CABLE_TENSION_ON_RIGHT_SHANK",             
                "COMPUTED_TORQUE_CABLE_TENSION_ON_LEFT_SHANK","COMPUTED_TORQUE_CABLE_TENSION_ON_RIGHT_SHANK",             
                "SAFE_CABLE_TENSION_ON_LEFT_SHANK","SAFE_CABLE_TENSION_ON_RIGHT_SHANK",               
                "SAFE_FORCE_FROM_LEFT_CABLE_ON_SHANK_X", "SAFE_FORCE_FROM_LEFT_CABLE_ON_SHANK_Y", "SAFE_FORCE_FROM_LEFT_CABLE_ON_SHANK_Z",                 
                "SAFE_FORCE_FROM_RIGHT_CABLE_ON_SHANK_X", "SAFE_FORCE_FROM_RIGHT_CABLE_ON_SHANK_Y", "SAFE_FORCE_FROM_RIGHT_CABLE_ON_SHANK_Z",           
                "SAFE_TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_1", "SAFE_TORQUE_FROM_SHANK_CABLE_FORCE_ON_JOINT_2",
                "PULLEY_POSITION_FOR_LEFT_SHANK_X", "PULLEY_POSITION_FOR_LEFT_SHANK_Y", "PULLEY_POSITION_FOR_LEFT_SHANK_Z",
                "PULLEY_POSITION_FOR_RIGHT_SHANK_X", "PULLEY_POSITION_FOR_RIGHT_SHANK_Y", "PULLEY_POSITION_FOR_RIGHT_SHANK_Z",
                "KNEE_CENTER_IN_FRAME_0_X", "KNEE_CENTER_IN_FRAME_0_Y", "KNEE_CENTER_IN_FRAME_0_Z",
                "PELVIS_CENTER_IN_FRAME_0_X", "PELVIS_CENTER_IN_FRAME_0_Y", "PELVIS_CENTER_IN_FRAME_0_Z",
                "CHEST_CENTER_IN_FRAME_0_X", "CHEST_CENTER_IN_FRAME_0_Y", "CHEST_CENTER_IN_FRAME_0_Z"         
        */
        
        
        
        // START: Store shank belt force/torque/tension values ******************************************************
        if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Disabled)
        {
                // Store desired shank forces in frame 0
                dataToStore.Add(desiredForcesOnShankFrame0.x);
                dataToStore.Add(desiredForcesOnShankFrame0.y);
                dataToStore.Add(desiredForcesOnShankFrame0.z);
                
                // Store joint torques from desired shank force
                for (int jointIndex = 0; jointIndex < resultantJointTorquesFromDesiredShankForce.Count; jointIndex++)
                {
                    dataToStore.Add((float)resultantJointTorquesFromDesiredShankForce[jointIndex]);
                }
                
                // Computed cable tensions on left, right shank
                dataToStore.Add(computedLeftShankCableTensionsThisFrame[0]);
                dataToStore.Add(computedRightShankCableTensionsThisFrame[0]);
                
                // Computed joint torque
                if(shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Transparent){
                    dataToStore.Add((float)computedJointTorquesFromTensionSolvedShankForce[0]);
                    dataToStore.Add((float)computedJointTorquesFromTensionSolvedShankForce[1]);
                } else // if transparent
                {
                dataToStore.Add(0.0f);
                dataToStore.Add(0.0f);
                }

                
                // Safe cable tensions on shank
                dataToStore.Add(safeLeftShankCableTensionsThisFrame[0]);
                dataToStore.Add(safeRightShankCableTensionsThisFrame[0]);

                // Safe force on left shank due to safe cable tensions
                dataToStore.Add(forcesTorquesOnLeftShank[0]);
                dataToStore.Add(forcesTorquesOnLeftShank[1]);
                dataToStore.Add(forcesTorquesOnLeftShank[2]);
                
                // Safe force on right shank due to safe cable tensions
                dataToStore.Add(forcesTorquesOnRightShank[0]);
                dataToStore.Add(forcesTorquesOnRightShank[1]);
                dataToStore.Add(forcesTorquesOnRightShank[2]);

                // safe resultant joint torque
                dataToStore.Add((float)resultantJointTorquesFromTensionSolvedShankForce[0]);
                dataToStore.Add((float)resultantJointTorquesFromTensionSolvedShankForce[1]);
                
                // Order of list is left shank pulley pos, then right (in frame 0)
                List<Vector3> shankPulleyPositionInFrame0 = buildStructureMatrixScript.GetLeftAndRightShankBeltPulleyPositionInFrame0();
                List<Vector3> shankAttachmentPointInFrame0 =
                    buildStructureMatrixScript.GetLeftAndRightShankAttachmentPositionInFrame0();
                // L pulley x,y,z in frame 0
                dataToStore.Add(shankPulleyPositionInFrame0[0].x);
                dataToStore.Add(shankPulleyPositionInFrame0[0].y);
                dataToStore.Add(shankPulleyPositionInFrame0[0].z);
                // R pulley x,y,z in frame 0
                dataToStore.Add(shankPulleyPositionInFrame0[1].x);
                dataToStore.Add(shankPulleyPositionInFrame0[1].y);
                dataToStore.Add(shankPulleyPositionInFrame0[1].z);
                
                // L attachment point x,y,z in frame 0
                dataToStore.Add(shankAttachmentPointInFrame0[0].x);
                dataToStore.Add(shankAttachmentPointInFrame0[0].y);
                dataToStore.Add(shankAttachmentPointInFrame0[0].z);
                // R attachment point x,y,z in frame 0
                dataToStore.Add(shankAttachmentPointInFrame0[1].x);
                dataToStore.Add(shankAttachmentPointInFrame0[1].y);
                dataToStore.Add(shankAttachmentPointInFrame0[1].z);
        }
        // END: Store shank belt force/torque/tension values ******************************************************

        // Add mid-ankle position in frame 0 (x,y,z)
        dataToStore.Add(viveTrackerDataManagerScript.GetAnkleCenterInFrame0().x);
        dataToStore.Add(viveTrackerDataManagerScript.GetAnkleCenterInFrame0().y);
        dataToStore.Add(viveTrackerDataManagerScript.GetAnkleCenterInFrame0().z);
        // Add mid-knee position in frame 0 (x,y,z)
        dataToStore.Add(viveTrackerDataManagerScript.GetKneeCenterInFrame0().x);
        dataToStore.Add(viveTrackerDataManagerScript.GetKneeCenterInFrame0().y);
        dataToStore.Add(viveTrackerDataManagerScript.GetKneeCenterInFrame0().z);
        // Add mid-pelvis position in frame 0 (x,y,z)
        dataToStore.Add(viveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0().x);
        dataToStore.Add(viveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0().y);
        dataToStore.Add(viveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0().z);
        // Add mid-chest position in frame 0 (x,y,z)
        dataToStore.Add(viveTrackerDataManagerScript.GetChestCenterPositionInFrame0().x);
        dataToStore.Add(viveTrackerDataManagerScript.GetChestCenterPositionInFrame0().y);
        dataToStore.Add(viveTrackerDataManagerScript.GetChestCenterPositionInFrame0().z);
        
        // Send row of data to data recorder
        generalDataRecorderScript.storeRowOfStanceModelData(dataToStore);
    }

    /// <summary>
    ///  This function is called by the Perturbation Controller when a perturbation start has been requested. 
    ///  The Perturbation Controller only makes the call once it confirms the subject is safely in the "At Home" position.
    ///  It sets a flag indicating a perturbation is ongoing, which shifts the high-level controller into "pert mode."
    /// </summary>
    /// <returns></returns>
    public bool EnterTrunkPerturbationModeIfAllowed()
    {
        // Set the trunk perturbation mode flag to true
        // NOTE: a safety check is not necessary here. The perturbation controller, which 
        // should make the only call to this function, checks to see if the person is in the "Home" 
        // position just before calling this function.
        
        // Then allow the perturbation ongoing flag to be set to true.
        trunkOngoingPerturbationModeFlag = true;
        
        // Return the perturbation ongoing flag.
        return trunkOngoingPerturbationModeFlag;
    }



    public void ExitTrunkPerturbationMode()
    {
        // Set the trunk perturbatio mode flag to false
        trunkOngoingPerturbationModeFlag = false;
    }


    private void SetupOnFirstCallByComManager()
    {

        //1.) Get the center of the base of support in Vicon coordinates
        (float leftEdgeBaseOfSupportXPosInViconCoords, float rightEdgeBaseOfSupportXPosInViconCoords,
            float frontEdgeBaseOfSupportYPosInViconCoords,
            float backEdgeBaseOfSupportYPosInViconCoords) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();
        float centerOfBaseOfSupportXPosViconFrame = (leftEdgeBaseOfSupportXPosInViconCoords + rightEdgeBaseOfSupportXPosInViconCoords) / 2.0f;
        float centerOfBaseOfSupportYPosViconFrame = (backEdgeBaseOfSupportYPosInViconCoords + frontEdgeBaseOfSupportYPosInViconCoords) / 2.0f;
        centerOfBaseOfSupportViconFrame = new Vector3(centerOfBaseOfSupportXPosViconFrame, centerOfBaseOfSupportYPosViconFrame, 0.0f);

        // 2.) Do belt-specific force field setup
        // Chest setup 
        // If we're using a chest FF
        if (chestForceFieldSettings.chestForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            // Then call chest-specific setup based on the selected mode
            DoChestForceFieldSetup();
        }

        // If we're using a pelvis FF
        // Manage the pelvic FF if we're using one (including visualization)
        if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            // Then call pelvis-specific setup based on the selected mode
            DoPelvisForceFieldSetup();

        }


        // If we're using a knee FF
        if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            // Then call knee-specific setup based on the selected mode
            DoKneeForceFieldSetup();
        }

        // 3.) Set up dynamics model of stance for the task, if needed
        // If the task uses a dynamics model (see enum)
        // Call the function that sets up the CORRECT dynamics model using subject-specific parameters (use an if if-else else here)
        stanceModel = kinematicModelStanceScript.CreateKinematicModelOfStance(centerOfMassManagerScript, viveTrackerDataManagerScript);

        // Get the subject mass
        PrintDebugIfDebugModeFlagIsTrue("Subject mass in kg from subjectSpecificDataScript: " + subjectSpecificDataScript.getSubjectMassInKilograms());
        subjectMass = subjectSpecificDataScript.getSubjectMassInKilograms();

    }


    // Do chest FF-specific setup
    private void DoChestForceFieldSetup()
    {
        // Load the excursion boundaries if the mode needs them
        // NOTE! Need to implement estimated vs. measured limits. Currently, assumes measured.
        if(chestForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.EstimatedLimits 
            || chestForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.MeasuredLimits)
        {
            // Get the excursion limit points as a list of Vector3 in vicon frame, in units of millimeters
            List<Vector3> excursionsPerDirectionWithSignInMillimeters = boundaryOfStabilityRendererScript.getExcursionLimitsPerDirectionWithProperSign();
            // Create polygon points in Vicon frame from the excursion limits by adding them to the center of base of support position. 
            // We also must convert excursions from mm to m.
            for (int vertexIndex = 0; vertexIndex < excursionsPerDirectionWithSignInMillimeters.Count; vertexIndex++)
            {
                chestBoundaryVerticesViconFrame.Add(centerOfBaseOfSupportViconFrame +
                    excursionsPerDirectionWithSignInMillimeters[vertexIndex]);
                string debugString = "Boundary of stability vertex " + vertexIndex + " in vicon frame is at : (" +
                    chestBoundaryVerticesViconFrame[vertexIndex].x + ", " +
                    chestBoundaryVerticesViconFrame[vertexIndex].y + ", " +
                    chestBoundaryVerticesViconFrame[vertexIndex].z + ")";
                printLogMessageToConsoleIfDebugModeIsDefined(debugString);
            }

            // Get the scaled (debounced) excursion limit points as a list of Vector3 in vicon frame, in units of millimeters
            // Create polygon points in Vicon frame from the excursion limits by adding them to the center of base of support position. 
            // We also must convert excursions from mm to m.
            for (int vertexIndex = 0; vertexIndex < excursionsPerDirectionWithSignInMillimeters.Count; vertexIndex++)
            {
                // Compute the inner chest boundary
                innerChestBoundaryVerticesViconFrame.Add(centerOfBaseOfSupportViconFrame +
                    chestInnerBoundaryScaler * excursionsPerDirectionWithSignInMillimeters[vertexIndex]);

                // Compute the outer chest boundary
                outerChestBoundaryVerticesViconFrame.Add(centerOfBaseOfSupportViconFrame +
                    chestOuterBoundaryScaler * excursionsPerDirectionWithSignInMillimeters[vertexIndex]);
                
                // Debug
                string debugString = "Boundary of stability scaled vertex " + vertexIndex + " in vicon frame is at : (" +
                    innerChestBoundaryVerticesViconFrame[vertexIndex].x + ", " +
                    innerChestBoundaryVerticesViconFrame[vertexIndex].y + ", " +
                    innerChestBoundaryVerticesViconFrame[vertexIndex].z + ")";
                printLogMessageToConsoleIfDebugModeIsDefined(debugString);
            }
        }

        // Define a neutral position of the chest if needed. NOTE: may be better to do this dynamically.

        // Get the spring constant for the trunk belt if needed
        springConstantTrunkBelt = (subjectMass * gravity) / verticalDistanceMidAnkleToMidTrunkBeltInMeters;

        // Get moment-arm distances if needed (e.g. ankle to chest length or pelvis to chest length. NOTE: may be better to do this dynamically.)
        // Get the vertical distance from mid-ankle point to middle of trunk belt
        verticalDistanceMidAnkleToMidTrunkBeltInMeters =  centerOfMassManagerScript.GetVerticalDistanceMidAnkleToMidTrunkBeltInMeters();
        string debugString2 = "Vertical distance ankles to trunk belt in meters: " + verticalDistanceMidAnkleToMidTrunkBeltInMeters;
        printLogMessageToConsoleIfDebugModeIsDefined(debugString2);

        // Set last computed cable tensions to zero
        lastComputedCableTensionsTrunk = new float[] { 0.0f, 0.0f,
                        0.0f, 0.0f };
    }

    // Do pelvis FF-specific setup
    private void DoPelvisForceFieldSetup()
    {
        // Load the excursion boundaries if the mode needs them
        if (pelvisForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.EstimatedLimits
            || pelvisForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.MeasuredLimits)
        {
            // Get the excursion limit points as a list of Vector3 in vicon frame, in units of millimeters. 
            // NOTE: these are currently the chest limits!!!! We need to implement pelvis-limits storage and retrieval!!!!!
            List<Vector3> excursionsPerDirectionWithSignInMillimeters = boundaryOfStabilityRendererScript.getExcursionLimitsPerDirectionWithProperSign();
            // Create polygon points in Vicon frame from the excursion limits by adding them to the center of base of support position. 
            // We also must convert excursions from mm to m.
            for (int vertexIndex = 0; vertexIndex < excursionsPerDirectionWithSignInMillimeters.Count; vertexIndex++)
            {
                pelvisBoundaryVerticesViconFrame.Add(centerOfBaseOfSupportViconFrame +
                    excursionsPerDirectionWithSignInMillimeters[vertexIndex]);
                string debugString = "Boundary of stability vertex " + vertexIndex + " in vicon frame is at : (" +
                    pelvisBoundaryVerticesViconFrame[vertexIndex].x + ", " +
                    pelvisBoundaryVerticesViconFrame[vertexIndex].y + ", " +
                    pelvisBoundaryVerticesViconFrame[vertexIndex].z + ")";
                printLogMessageToConsoleIfDebugModeIsDefined(debugString);
            }

            // Get the scaled (debounced) excursion limit points as a list of Vector3 in vicon frame, in units of millimeters
            // Create polygon points in Vicon frame from the excursion limits by adding them to the center of base of support position. 
            // We also must convert excursions from mm to m.
            for (int vertexIndex = 0; vertexIndex < excursionsPerDirectionWithSignInMillimeters.Count; vertexIndex++)
            {
                // Compute the inner chest boundary
                innerPelvisBoundaryVerticesViconFrame.Add(centerOfBaseOfSupportViconFrame +
                    chestInnerBoundaryScaler * excursionsPerDirectionWithSignInMillimeters[vertexIndex]);

                // Compute the outer chest boundary
                outerPelvisBoundaryVerticesViconFrame.Add(centerOfBaseOfSupportViconFrame +
                    chestOuterBoundaryScaler * excursionsPerDirectionWithSignInMillimeters[vertexIndex]);

                // Debug
                string debugString = "Boundary of stability scaled vertex " + vertexIndex + " in vicon frame is at : (" +
                    innerPelvisBoundaryVerticesViconFrame[vertexIndex].x + ", " +
                    innerPelvisBoundaryVerticesViconFrame[vertexIndex].y + ", " +
                    innerPelvisBoundaryVerticesViconFrame[vertexIndex].z + ")";
                printLogMessageToConsoleIfDebugModeIsDefined(debugString);
            }
        }

        // Depending on how many cables were using
        // Let the default value for the last computed tension solution be the minimum tension value 
        // or zero, depending on if those cables are being used or not. 
        if (usingPelvicBelt == true)
        {
            if(pelvisForceFieldSettings.pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Four)
                lastComputedCableTensionsPelvis = new float[] { minimumCableTension, minimumCableTension, minimumCableTension, minimumCableTension };
            else if (pelvisForceFieldSettings.pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Eight)
            {
                lastComputedCableTensionsPelvis = new float[] { minimumCableTension, minimumCableTension, minimumCableTension, minimumCableTension,
                    minimumCableTension, minimumCableTension, minimumCableTension, minimumCableTension};
            }
        }
        else // if we're not using the pelvic belt
        {
            // leave an arbitrary value, not using
            lastComputedCableTensionsPelvis = new float[] { 0.0f, 0.0f, 0.0f, 0.0f };
        }

        // Define a neutral position of the pelvis if needed. NOTE: may be better to do this dynamically.

        // Get moment-arm distances if needed (e.g. ankle to pelvis length or knee to pelvis length. NOTE: may be better to do this dynamically.)

    }

    // Do knee FF-specific setup
    private void DoKneeForceFieldSetup()
    {
        // Define a neutral (AP-axis and vertical axis) position of the knees if needed. NOTE: may be better to do this dynamically.

        // Get moment-arm distances if needed (e.g. ankle to shank attachment length. NOTE: may be better to do this dynamically.)

    }

/*
 *if== vive
 * desiredForcesOnTrunkFrame0 == vve data
 * if == vicon
 *  desiredForcesOnTrunkFrame0 == vicon data
 * 
 */


    private void ComputeChestForceFieldForces()
    {
        if (!trunkOngoingPerturbationModeFlag) // If there is not an ongoing perturbation and the chest is instead in force-field mode
        {
            // Let the force field high-level controller compute the desired forces based on control point position.
            ComputeDesiredForcesAndTorquesTrunkSegment();
        }
        else // if there is an ongoing perturbation on the trunk (NOT typical)
        {
            PrintDebugIfDebugModeFlagIsTrue("High-level controller setting perturbation force.");
            // Set desired trunk forces equal to the desired perturbation forces
            desiredForcesOnTrunkFrame0 = perturbationControllerScript.GetCurrentPerturbationForceVectorViconFrame();
            string debugString = "High-level controller: want perturbation forces on trunk of: (" +
                desiredForcesOnTrunkFrame0.x + ", " +
                desiredForcesOnTrunkFrame0.y + ", " +
                desiredForcesOnTrunkFrame0.z + ")";
            PrintDebugIfDebugModeFlagIsTrue(debugString);
            // Set desired trunk torques equal to 0
            desiredTorquesOnTrunkViconFrame = new Vector3(0.0f, 0.0f, 0.0f);        
        }
        // Visualize the current FF force
        forceFieldVisualizerScript.UpdateForceFieldVectorTrunk(desiredForcesOnTrunkFrame0 / 50.0f);
    }



    private void ComputePelvisForceFieldForces()
    {
        // Let the force field high-level controller compute the desired pelvis forces based on net ankle torque and pelvis position compensation.
        ComputeDesiredForcesAndTorquesPelvisSegment();

        // Visualize the force
        //
            
        PrintDebugIfDebugModeFlagIsTrue("Visualizing pelvic force with components: (" + desiredForcesOnPelvisFrame0.x + ", " +
                                        desiredForcesOnPelvisFrame0.y + ", " + desiredForcesOnPelvisFrame0.z + ")");
        Vector3 mostRecentPelvisBeltCenterPosition = centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();
        
        //forceFieldVisualizerScript.UpdateForceFieldVectorPelvis(mostRecentPelvisBeltCenterPosition, desiredForcesOnPelvisViconFrame / 10.0f);
        forceFieldVisualizerScript.UpdateForceFieldVectorPelvis(desiredForcesOnPelvisFrame0 / 50.0f);
    }



    private void ComputeKneeForceFieldForces()
    {
        // Let the force field high-level controller compute the desired shank forces based on knee angle.
        ComputeDesiredForcesAndTorquesShankSegment();

        // Visualize the force
        PrintDebugIfDebugModeFlagIsTrue("Visualizing shank force with frame 0 components: (" + desiredForcesOnShankFrame0.x + ", " +
                                        desiredForcesOnShankFrame0.y + ", " + desiredForcesOnShankFrame0.z);
        forceFieldVisualizerScript.UpdateForceFieldVectorShank(desiredForcesOnShankFrame0 / 50.0f);
    }


    
    
    // Function to compute desired forces and torques on the chest belt/segment. 
    // All forces and torques are computed in "frame 0" which is the base frame of the 
    // kinematic model of stance representing the subject. 
    // Note 1: If control without a kinematic model is used, transformations from the data frame to
    // base frame could just be changed to a no-rotation, no-translation matrix (I think this would be easiest).
    private void ComputeDesiredForcesAndTorquesTrunkSegment()
    {

        Vector3 mostRecentPelvicCenterPosition = new Vector3();
        Vector3 mostRecentTrunkBeltCenterPosition = new Vector3();
        Vector3 mostRecentComPosition = new Vector3();

        // Choose the trunk and pelvic belt center positions given the data source - Vive or Vicon
        if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            // Get the positions in Vicon frame
            mostRecentTrunkBeltCenterPosition =
                centerOfMassManagerScript.GetCenterOfTrunkBeltPositionInViconFrame();
            mostRecentComPosition = centerOfMassManagerScript.getSubjectCenterOfMassInViconCoordinates();
            mostRecentPelvicCenterPosition =
                centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();
        }
        else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            // Get the positions in Unity frame
            mostRecentTrunkBeltCenterPosition = viveTrackerDataManagerScript.GetChestCenterPositionInUnityFrame();
            mostRecentPelvicCenterPosition = viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();
            mostRecentComPosition = mostRecentPelvicCenterPosition;
        }

        // If using keyboard control (probably does not work right now)
        // If we are using keyboard controls to test, replace the COM position with the current player game object position
        if (allowKeyboardControlOverPlayer == true)
        {
            if (playerControlScript.getPlayerBeingControlledByKeyboardStatus() == true)
            {
                mostRecentTrunkBeltCenterPosition =
                    levelManagerScript.mapPointFromUnityFrameToViconFrame(player.transform.position);
            }
        }

        // (Re)set desired trunk forces and torques equal to 0 for this frame.
        // These are the key variables we're computing in this function.
        desiredForcesOnTrunkFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
        desiredTorquesOnTrunkViconFrame = new Vector3(0.0f, 0.0f, 0.0f); // remain zero for now.
        
        // USE OF BOUNDARY FORCE FIELD *************************************************************************************************
        // If we're using boundary-relative control (boundary = workspace of the point), then determine whether the chest control point
        // is inside or outside of the boundary
        // NOTE FOR K,Y,C: Don't need this conditional statement, leave as is.
        bool trunkControlPointInBoundsFlag = true; // assume inside since we may not be using the boundaries. 
        // This effectively overwrites the "outside" values with the "inside" values
        // if we're not using boundary-relative control.
        if (chestForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.EstimatedLimits
            || chestForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.MeasuredLimits)
        {
            //Initialize a flag indicating whether or not the force field should still be active
            //bool boundaryForceFieldActiveThisCallFlag = false;

            // The typical excursion boundaries are used.
            // Compute whether or not the trunk control point is outside of the FF boundary. If yes, 
            // then we must compute desired forces. Else, the desired forces and torques are zero. 
            trunkControlPointInBoundsFlag = IsTestPointInPolygon(mostRecentTrunkBeltCenterPosition,
                chestBoundaryVerticesViconFrame);

            string debugString = "Trunk control point is inside of excursion bounds?" +
                                 trunkControlPointInBoundsFlag;
            printLogMessageToConsoleIfDebugModeIsDefined(debugString);
        }

        // If we're using boundary-relative control ( gravity compensation or PD control), 
        // compute the transition coefficient for the boundary region. 
        // E.g. 0 if inside transition region, 0-1 if in transition region, 1 if outside transition region
        // NOTE FOR K,Y,C: Don't need this conditional statement, leave as is.
        // CAN DISABLE BY SETTING THE KP, KV VALUES AND GRAV COMPENSATION EQUAL BOTH INSIDE AND OUTSIDE FORCE FIELD
        float transitionCoefficient = 0.0f; // 0 will use the "inside" values.

        if (chestForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.EstimatedLimits
            || chestForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.MeasuredLimits)
        {
            // Compute the distance from the control point to the nearest point on the polygons
            (float distanceFromControlPointToExcursionBounds,
                    Vector3 controlPointProjectedOntoExcursionBoundaries) =
                GetDistanceFromPointToPolygonInXyPlane(mostRecentTrunkBeltCenterPosition,
                    chestBoundaryVerticesViconFrame);

            (float distanceFromControlPointToInnerExcursionBounds,
                    Vector3 controlPointProjectedOntoInnerExcursionBoundaries) =
                GetDistanceFromPointToPolygonInXyPlane(mostRecentTrunkBeltCenterPosition,
                    innerChestBoundaryVerticesViconFrame);

            (float distanceFromControlPointToOuterExcursionBounds,
                    Vector3 controlPointProjectedOntoOuterExcursionBoundaries) =
                GetDistanceFromPointToPolygonInXyPlane(mostRecentTrunkBeltCenterPosition,
                    outerChestBoundaryVerticesViconFrame);

            // Determine if we're in the transitional region based on the sign of the distances to the boundary. 
            // NOTE: sign of the distance to boundary is negative if inside boundary. 
            // If the control point is on the interior of the transition zone

            // Get the current position of the control point for the trunk segment
            if (distanceFromControlPointToInnerExcursionBounds < 0.0f)
            {
                transitionCoefficient = 0.0f;
            }
            else if (distanceFromControlPointToInnerExcursionBounds >= 0.0f &&
                     distanceFromControlPointToOuterExcursionBounds < 0.0f)
            {
                // Compute the distance between the inner and outer boundaries (the width of the transition zone)
                float widthTransitionZoneInViconMm = (controlPointProjectedOntoOuterExcursionBoundaries -
                                                      controlPointProjectedOntoInnerExcursionBoundaries).magnitude;

                // Compute the fraction progression through the transition zone
                transitionCoefficient =
                    distanceFromControlPointToInnerExcursionBounds / widthTransitionZoneInViconMm;
            }
            else
            {
                // If the control point is outside the outer boundary
                transitionCoefficient = 1.0f;
            }
        }
        else // the control point is outside of the outer boundary of the transition zone
        {
            transitionCoefficient = 1.0f;
        }

        // Set the chest kp and kv values depending on whether we're inside boundary, outside, or transitioning
        float chestKpThisFrame = chestForceFieldSettings.GetKpInsideBoundary() +
                                 transitionCoefficient * (chestForceFieldSettings.GetKpOutsideBoundary() -
                                 chestForceFieldSettings.GetKpInsideBoundary());
        float chestKvThisFrame = chestForceFieldSettings.GetKvInsideBoundary() +
                                 transitionCoefficient * (chestForceFieldSettings.GetKvOutsideBoundary() -
                                 chestForceFieldSettings.GetKvInsideBoundary());
        // Compute gravity compensation fraction at the pelvis joints (theta4, theta5 in 5R model).
        float chestGravityCompFractionThisFrame = chestForceFieldSettings.chestGravityCompInsideBounds +
                                                  transitionCoefficient * (chestForceFieldSettings.chestGravityCompOutsideBounds -
                                                                           chestForceFieldSettings.chestGravityCompInsideBounds);

        // GRAVITY & NET TORQUE COMPENSATION PRELIMINARY COMPUTATIONS********************************************
        // Convert the chest and pelvic positions to frame 0 of the stance model before
        // doing computations of forces using the frame 0 Jacobian
        Vector3 mostRecentTrunkBeltCenterPositionFrame0 = new Vector3();
        Vector3 mostRecentComPositionFrame0 = new Vector3();
        Vector3 mostRecentPelvicCenterPositionFrame0 = new Vector3();

        if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            Matrix4x4 transformationFromViconToFrame0 =
                stanceModel.GetTransformFromViconFrameToFrameZeroOfStanceModel();
            mostRecentTrunkBeltCenterPositionFrame0 =
                transformationFromViconToFrame0.MultiplyPoint3x4(mostRecentTrunkBeltCenterPosition);
            mostRecentComPositionFrame0 = transformationFromViconToFrame0.MultiplyPoint3x4(mostRecentComPosition);
            mostRecentPelvicCenterPositionFrame0 =
                transformationFromViconToFrame0.MultiplyPoint3x4(mostRecentPelvicCenterPosition);
        }
        else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            // Get transformation from Unity frame (left-handed) to frame 0 (right handed)
            Matrix4x4 transformationFromFrame0ToUnityFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationFromUnityFrameToFrame0 = new Matrix4x4();
            Matrix4x4.Inverse3DAffine(transformationFromFrame0ToUnityFrame,
                ref transformationFromUnityFrameToFrame0);
            // Transform the pelvic and chest belt centers and COM from Unity frame to frame 0
            mostRecentTrunkBeltCenterPositionFrame0 =
                transformationFromUnityFrameToFrame0.MultiplyPoint3x4(mostRecentTrunkBeltCenterPosition);
            mostRecentComPositionFrame0 =
                transformationFromUnityFrameToFrame0.MultiplyPoint3x4(mostRecentComPosition);
            mostRecentPelvicCenterPositionFrame0 =
                transformationFromUnityFrameToFrame0.MultiplyPoint3x4(mostRecentPelvicCenterPosition);
        }

        // GRAVITY & NET TORQUE COMPENSATION**************************************************************************
        // Compute gravity/torque compensation force at the chest (to balance gravity torque at pelvic joints),
        // if needed
        Vector3 forceVectorChestGravityCompensation = new Vector3(0.0f, 0.0f, 0.0f);
        Matrix<double> chestVelocityJacobianTranspose = stanceModel.GetChestForceVelocityJacobianTranspose();
        if (chestForceFieldSettings.chestGravityCompSettings == GravityCompensationTypeEnum.GravityComp)
        {
            // Get torque at the waist/pelvis due to gravity acting on body masses above the pelvis joint
            // Call subject-specific kinematic model to get gravity torque at joints
            // NOTE: these are joint torques needed to COUNTERACT gravity torques at the joints.
            // and are already stored in an instance variable.
           
            
            // Get the pelvic center position and the pelvis-to-chest vector. 
            // Get pelvis-to-chest vector (should be in frame 0)
            Vector3 vectorPelvisToChestFrame0 =
                mostRecentTrunkBeltCenterPositionFrame0 - mostRecentPelvicCenterPositionFrame0;

            // GOAL: find the intersection point of the plane orthogonal to the pelvis-chest vector 
            // and a vertical line passing through the pelvis. 
            // The chest force will pass through this point.
            // WHY? No axial twist about pelvis(& for the chest this direction happens to be best for
            // balancing torque at BOTH pelvis joints, theta4, theta5).
            // equation of line r = p_pelvis + t*(unitV_neutralLine) = a line from pelvis to chest
            // equation of plane = n dotProduct (r - p_chest) = 0, r = a point on the plane, n = normal vector to plane, 
            // assume p_chest is in the plane.
            // substitue r to get n dotProduct (p_pelvis - p_chest + t*(unitV_neutralLine))
            // solving for t

            // Get the vector from the pelvis to the chest
            Vector3 verticalUnitVectorFrame0 =
                    new Vector3(1.0f, 0.0f, 0.0f); // vertical vector in frame 0 (x is up)
            float pelvisPosX = mostRecentPelvicCenterPositionFrame0.x;
            float pelvisPosY = mostRecentPelvicCenterPositionFrame0.y;
            float pelvisPosZ = mostRecentPelvicCenterPositionFrame0.z;

            float chestPosX = mostRecentTrunkBeltCenterPositionFrame0.x;
            float chestPosY = mostRecentTrunkBeltCenterPositionFrame0.y;
            float chestPosZ = mostRecentTrunkBeltCenterPositionFrame0.z;

            Vector3 unitVectorForce = new Vector3(0.0f, 0.0f, 0.0f);
            if (chestForceFieldSettings.chestForceDirection == ChestForceTypeEnum.perpendicular_to_trunk)
            {
                // Unit vector from pelvis to chest DEFINES a plane in which the chest force will act (hence, it is the Normal vector)
                float nx = vectorPelvisToChestFrame0.x;
                float ny = vectorPelvisToChestFrame0.y;
                float nz = vectorPelvisToChestFrame0.z;

                // Define the point of intersection of the vertical line through the pelvis and the plane perpendicular to the pelvis-chest line.
                // We should add comments to explain the computations here (and ensure they're correct)
                float bottomTermT = nx * verticalUnitVectorFrame0.x + ny * verticalUnitVectorFrame0.y + nz * verticalUnitVectorFrame0.z; // dot product of n and vertical line
                float topTermT = -nx * pelvisPosX - ny * pelvisPosY - nz * pelvisPosZ + nx * chestPosX +
                                 ny * chestPosY + nz * chestPosZ;
                float scalingFactorT = topTermT / bottomTermT;

                // substitute t back into line equation to get intersection point
                Vector3 intersectionPtPelvisAndForce = mostRecentPelvicCenterPosition + scalingFactorT * verticalUnitVectorFrame0;

                // get unit vector in direction of chest force.
                // This determines the final direction of the gravity compensation part of the chest force
                unitVectorForce = intersectionPtPelvisAndForce - mostRecentTrunkBeltCenterPosition;
                unitVectorForce = unitVectorForce / unitVectorForce.magnitude;
            }else if(chestForceFieldSettings.chestForceDirection == ChestForceTypeEnum.ground_plane)
            {
                // Get intersection point of a vertical line through the pelvis and the plane at the level
                // of the chest
                Vector3 intersectionPtPelvisAndForce = new Vector3(chestPosX, pelvisPosY, pelvisPosZ);
                PrintDebugIfDebugModeFlagIsTrue("Chest ground plane force - chest is at: (" + chestPosX + ", "
                   + chestPosY + ", " + chestPosZ + ")");
                PrintDebugIfDebugModeFlagIsTrue("Chest ground plane force - intersection point above pelvis is: (" + intersectionPtPelvisAndForce.x + ", "
                    + intersectionPtPelvisAndForce.y + ", " + intersectionPtPelvisAndForce.z + ")");
                // Chest force points from chest to this intersection point
                unitVectorForce = intersectionPtPelvisAndForce - mostRecentTrunkBeltCenterPositionFrame0;
                PrintDebugIfDebugModeFlagIsTrue("Chest unit vector force before normalization is: (" + unitVectorForce.x + ", "
                    + unitVectorForce.y + ", " + unitVectorForce.z + ")");
                // Normalize
                unitVectorForce = unitVectorForce / unitVectorForce.magnitude;
                PrintDebugIfDebugModeFlagIsTrue("Chest unit vector force after normalization is: (" + unitVectorForce.x + ", "
                    + unitVectorForce.y + ", " + unitVectorForce.z + ")");
            }
            else
            {
                Debug.LogError("Chest force direction not specified/invalid.");
            }


            // Select the 4th and 5th rows and the last 2 (y and z) columns. 
            // We'll solve the system A*x = b, where A = Jv^T, x = force vector y and z component, b = needed joint torques at theta4, theta5
            int totalRows = chestVelocityJacobianTranspose.RowCount;
            Matrix<double> reducedChestVelJacobianTranspose = chestVelocityJacobianTranspose.SubMatrix(totalRows - 2, 2, 1, chestVelocityJacobianTranspose.ColumnCount-1);
            
            // Compensate for gravity
            Vector<double> torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
            {
                        jointTorquesFromGravity[3],
                        jointTorquesFromGravity[4]
            });
            
            // Solve for required force!
            // Solution is Fchest,y and Fchest,z (only two elements, since we assume Fchest,x is zero).
            Vector<double> forceVectorChestGravityCompensationVector = reducedChestVelJacobianTranspose.Solve(torqueVectorPelvis);
            
            // Convert to vector3
            forceVectorChestGravityCompensation = new Vector3(0.0f,
                (float) forceVectorChestGravityCompensationVector[0], (float) forceVectorChestGravityCompensationVector[1]);

            // Build the string with the relevant debug info for Jacobian, torque, and force
            string debugOutput = "Jacobian (last two rows, y and z columns): \n" +
                                 chestVelocityJacobianTranspose.SubMatrix(totalRows - 2, 2, 1, chestVelocityJacobianTranspose.ColumnCount - 1).ToString("F2") + "\n" +
                                 "Torque Vector (Pelvis, joints 4 and 5): [" + torqueVectorPelvis[0] + ", " + torqueVectorPelvis[1] + "]\n" +
                                 "Desired Chest Force (gravity compensation, y and z components): [" +
                                 forceVectorChestGravityCompensation.x + ", " +
                                 forceVectorChestGravityCompensation.y + ", " +
                                 forceVectorChestGravityCompensation.z + "]";

            // Print out the consolidated debug info
            PrintDebugIfDebugModeFlagIsTrue(debugOutput);


            // Debug print useful quantities, if Debug mode flag is set
            PrintDebugIfDebugModeFlagIsTrue(" The chest force gravity compensation fraction is: " + chestGravityCompFractionThisFrame);
            PrintDebugIfDebugModeFlagIsTrue(" The desired unit vector of the chest force in frame 0 is: " + unitVectorForce.ToString("F2"));
/*            PrintDebugIfDebugModeFlagIsTrue(" The chest force magnitude is computed twice and should match. Joint 4 value is: " 
                + chestForceMagnitudeJoint4 + " and joint 5 value is: " + chestForceMagnitudeJoint5);*/
            PrintDebugIfDebugModeFlagIsTrue(" The desired chest force vector in frame 0 is: " + forceVectorChestGravityCompensation.ToString("F2"));
        }


        // If we're applying a position and velocity-based PD controller
        // NOTE: this force should be computed in frame 0.
        Vector3 pdChestForce = new Vector3(0.0f, 0.0f, 0.0f);
        if (chestForceFieldSettings.chestPdControlSettings == PdControlTypeEnum.ConstantPosition ||
            chestForceFieldSettings.chestPdControlSettings == PdControlTypeEnum.PositionTrajectory)
        {
            Vector3 desiredChestPosFrame0InMeters = new Vector3();
            Vector3 desiredChestVelFrame0InMetersPerSec = new Vector3();
            if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
            {
                Matrix4x4 transformationFromViconToFrame0 =
                stanceModel.GetTransformFromViconFrameToFrameZeroOfStanceModel();

                // Get the current desired chest position and velocity from the Vicon data (in mm?)
                (Vector3 desiredChestPosViconFrameInMm, Vector3 desiredChestVelViconFrameInMmPerSec) =
                    GetCurrentChestDesiredPositionAndVelocityInViconFrame();
                
                // Transform the desired chest position and velocity to frame 0 and convert to meters
                desiredChestPosFrame0InMeters = transformationFromViconToFrame0.MultiplyPoint3x4(desiredChestPosViconFrameInMm) / 1000.0f;
                
                // Rotate the desired chest velocity to frame 0 and convert to meters
                desiredChestVelFrame0InMetersPerSec = transformationFromViconToFrame0.MultiplyVector(desiredChestVelViconFrameInMmPerSec) / 1000.0f;
            }
            else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
            {
                // Get the current desired chest position and velocity from the Vive data (in m?)
                (desiredChestPosFrame0InMeters, desiredChestVelFrame0InMetersPerSec) = GetCurrentChestDesiredPositionAndVelocityInFrame0();
            }


            // Compute the proportional (position) term
            Vector3 proportionalComponentForce =
                chestKpThisFrame * (desiredChestPosFrame0InMeters - mostRecentTrunkBeltCenterPositionFrame0);

            // Compute the derivative (velocity) term - TO DO!!!!!!
            // NOTE: for now it's OK to have the desired velocity equal to zero.
            // We don't use PD control in squatting task.
            // Optional: can convert the velocities to m/s for a more intuitive selection of kv
            Vector3 derivativeComponentForce = chestKvThisFrame *
                                               (desiredChestVelFrame0InMetersPerSec -
                                                desiredChestVelFrame0InMetersPerSec);

            // Sum to get total PD chest force
            pdChestForce = proportionalComponentForce + derivativeComponentForce;
        }

        // Store the total desired chest force as a sum of the gravity compensation force and the PD control for the chest
        desiredForcesOnTrunkFrame0 = forceVectorChestGravityCompensation + pdChestForce;

        // Note that the force and Jacobian must be in frame 0 to compute joint torques resulting from external cable forces.
        resultantJointTorquesFromDesiredChestForce = chestVelocityJacobianTranspose *
                                                     ConvertVector3ToNetNumericsVector(desiredForcesOnTrunkFrame0);
        // Visualize the current FF force
        //Vector3 forceVectorInUnityFrame = new Vector3(-desiredForcesOnTrunkFrame0.x, -desiredForcesOnTrunkFrame0.y, desiredForcesOnTrunkFrame0.z);
        PrintDebugIfDebugModeFlagIsTrue(" The resultantJointTorquesFrom desired ChestForce after is " + desiredForcesOnTrunkFrame0);

    }




    private void ComputeDesiredForcesAndTorquesPelvisSegment()
    {
        Vector3 mostRecentPelvisBeltCenterPosition = new Vector3();
        Vector3 mostRecentComPosition = new Vector3();
        Matrix4x4 transformFromViconToFrame0 = new Matrix4x4();
        Matrix4x4 transformationMatrixFromFrame0ToUnityFrame = new Matrix4x4();
        Matrix4x4 transformationMatrixFromUnityFrameToFrame0 = new Matrix4x4();
        if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            transformFromViconToFrame0 =
                stanceModel.GetTransformFromViconFrameToFrameZeroOfStanceModel();
            
            // Get the current position of the control point for the pelvis segment  
            mostRecentPelvisBeltCenterPosition =
                centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();

            // Get the current position of the COM
            mostRecentComPosition = centerOfMassManagerScript.getSubjectCenterOfMassInViconCoordinates();
        }
        else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            transformationMatrixFromFrame0ToUnityFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            transformationMatrixFromUnityFrameToFrame0 = transformationMatrixFromFrame0ToUnityFrame.inverse;
            
            // Get the current position of the control point for the pelvis segment  
            mostRecentPelvisBeltCenterPosition =
                viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();
        }

        // not sure if this code can run when the option is Vicon only
        

        // If we are using keyboard controls to test, replace the COM position with the current player game object position
        if (allowKeyboardControlOverPlayer == true)
        {
            if (playerControlScript.getPlayerBeingControlledByKeyboardStatus() == true)
            {
                mostRecentPelvisBeltCenterPosition =
                    levelManagerScript.mapPointFromUnityFrameToViconFrame(player.transform.position);
            }
        }


        // (Re)set desired pelvis forces and torques equal to 0 for this frame.
        desiredForcesOnPelvisViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
        desiredTorquesOnPelvisViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
        desiredForcesOnPelvisViveFrame = new Vector3(0.0f, 0.0f, 0.0f);
        desiredTorquesOnPelvisViveFrame = new Vector3(0.0f, 0.0f, 0.0f);
        desiredForcesOnPelvisFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
        desiredTorquesOnPelvisFrame0 = new Vector3(0.0f, 0.0f, 0.0f);

        // BOUNDARY FF CONTROL*********************************************************************************************************
        // If we're using boundary-relative control (boundary = workspace of the point), then determine whether the pelvis control point
        // is inside or outside of the boundary
        bool pelvisControlPointInBoundsFlag = true; // assume inside since we may not be using the boundaries. 
        // This effectively overwrites the "outside" values with the "inside" values
        // if we're not using boundary-relative control.
        if (pelvisForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.MeasuredLimits
            || pelvisForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.EstimatedLimits)
        {
            //Initialize a flag indicating whether or not the force field should still be active
            //bool boundaryForceFieldActiveThisCallFlag = false;

            // The typical excursion boundaries are used.
            // Compute whether or not the pelvis control point is outside of the FF boundary. If yes, 
            // then we must compute desired forces. Else, the desired forces and torques are zero. 
            pelvisControlPointInBoundsFlag = IsTestPointInPolygon(mostRecentPelvisBeltCenterPosition,
                pelvisBoundaryVerticesViconFrame);

            string debugString = "Pelvis control point is inside of excursion bounds?" +
                                    pelvisControlPointInBoundsFlag;
            printLogMessageToConsoleIfDebugModeIsDefined(debugString);
        }


        // If we're using boundary-relative gravity compensation or PD control, 
        // compute the transition coefficient for the boundary region. 
        // E.g. 0 if inside transition region, 0-1 if in transition region, 1 if outside transition region
        float transitionCoefficient = 0.0f; // 0 will use the "inside" values.
        if (pelvisForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.MeasuredLimits
            || pelvisForceFieldSettings.boundaryForceSettings == BoundaryForceFieldTypeEnum.EstimatedLimits)
        {
            //string debugString = "FF should be active given most recent Vicon data";
            //printLogMessageToConsoleIfDebugModeIsDefined(debugString);

            // Compute the distance from the control point to the nearest point on the polygon
            (float distanceFromControlPointToExcursionBounds,
                    Vector3 controlPointProjectedOntoExcursionBoundaries) =
                GetDistanceFromPointToPolygonInXyPlane(mostRecentPelvisBeltCenterPosition,
                    pelvisBoundaryVerticesViconFrame);

            // Compute the distance from the control point to the nearest point on the inner boundary polygon
            (float distanceFromControlPointToInnerExcursionBounds,
                    Vector3 controlPointProjectedOntoInnerExcursionBoundaries) =
                GetDistanceFromPointToPolygonInXyPlane(mostRecentPelvisBeltCenterPosition,
                    innerPelvisBoundaryVerticesViconFrame);

            // Compute the distance from the control point to the nearest point on the outer boundary polygon
            (float distanceFromControlPointToOuterExcursionBounds,
                    Vector3 controlPointProjectedOntoOuterExcursionBoundaries) =
                GetDistanceFromPointToPolygonInXyPlane(mostRecentPelvisBeltCenterPosition,
                    outerPelvisBoundaryVerticesViconFrame);

            // Determine if we're in the transitional region based on the sign of the distances to the boundary. 
            // NOTE: sign of the distance to boundary is negative if inside boundary. 
            // If the control point is on the interior of the transition zone
            if (distanceFromControlPointToInnerExcursionBounds < 0.0f)
            {
                transitionCoefficient = 0.0f;

            }
            else if (distanceFromControlPointToInnerExcursionBounds >= 0.0f &&
                        distanceFromControlPointToOuterExcursionBounds < 0.0f)
                // else if the control point is in the transition zone
            {
                // Compute the distance between the inner and outer boundaries (the width of the trnasition zone)
                float widthTransitionZoneInViconMm = (controlPointProjectedOntoOuterExcursionBoundaries -
                                                        controlPointProjectedOntoInnerExcursionBoundaries).magnitude;

                // Compute the fraction progression through the transition zone
                transitionCoefficient =
                    distanceFromControlPointToInnerExcursionBounds / widthTransitionZoneInViconMm;
            }
            else // the control point is outside of the outer boundary of the transition zone
            {
                transitionCoefficient = 1.0f;
            }
        }


        // Depending on whether or not the control point is inside or outside of the boundaries, 
        // select kp, kd, and gravity compensation values. 
        // NOTE: the default value for transitionCoefficient is 0. 
        float pelvisKpThisFrame = pelvisForceFieldSettings.GetKpInsideBoundary() +
                                    transitionCoefficient * (pelvisForceFieldSettings.GetKpOutsideBoundary() -
                                    pelvisForceFieldSettings.GetKpInsideBoundary());
        float pelvisKvThisFrame = pelvisForceFieldSettings.GetKvInsideBoundary() +
                                    transitionCoefficient * (pelvisForceFieldSettings.GetKvOutsideBoundary() -
                                    pelvisForceFieldSettings.GetKvInsideBoundary());
        float pelvisGravityCompFractionThisFrame = pelvisForceFieldSettings.pelvisGravityCompInsideBounds +
                                                    transitionCoefficient * (pelvisForceFieldSettings.pelvisGravityCompOutsideBounds -
                                                        pelvisForceFieldSettings.pelvisGravityCompInsideBounds);


        // Get the pelvic center position and the ankle-to-pelvis vector
        // get knee markers
        Vector3 vectorKneeToPelvisFrame0 = new Vector3();
        Vector3 vectorKneeToPelvis = new Vector3();
        Vector3 mostRecentPelvisBeltCenterPositionFrame0 = new Vector3();
        if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            (_, Vector3 rightKneeMarkerPosThisFrame) =
                centerOfMassManagerScript.GetMostRecentMarkerPositionByName("RKNE");
            (_, Vector3 leftKneeMarkerPosThisFrame) =
                centerOfMassManagerScript.GetMostRecentMarkerPositionByName("LKNE");
            // get avg pos of right and left knee
            Vector3 avgPosKneeMarker = (rightKneeMarkerPosThisFrame + leftKneeMarkerPosThisFrame) / 2.0f;

            // Transform the pelvic belt center and knee center into stance model frame 0 


            // Get vector knee to pelvis in stance model frame 0
            mostRecentPelvisBeltCenterPositionFrame0 =
                transformFromViconToFrame0.MultiplyPoint(mostRecentPelvisBeltCenterPosition);
            Vector3 avgPosKneeMarkerFrame0 = transformFromViconToFrame0.MultiplyPoint(avgPosKneeMarker);
            vectorKneeToPelvisFrame0 =
                mostRecentPelvisBeltCenterPositionFrame0 - avgPosKneeMarkerFrame0;

            vectorKneeToPelvis = mostRecentPelvisBeltCenterPosition - avgPosKneeMarker;
        }
        else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {

            Vector3 rightKneeCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetRightKneeCenterPositionInUnityFrame();
            Vector3 leftKneeCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetLeftKneeCenterPositionInUnityFrame();
            Vector3 middleKneeCenterPositionInUnityFrame = (rightKneeCenterPositionInUnityFrame + leftKneeCenterPositionInUnityFrame) / 2.0f;
            Vector3 avgPosKneeMarkerFrame0 = transformationMatrixFromUnityFrameToFrame0.MultiplyPoint3x4(middleKneeCenterPositionInUnityFrame);

            Vector3 pelvicCenterPositionInUnityFrame =
                viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();

            // Get vector knee to pelvis in stance model frame 0
            mostRecentPelvisBeltCenterPositionFrame0 =
                transformationMatrixFromUnityFrameToFrame0.MultiplyPoint3x4(pelvicCenterPositionInUnityFrame);
            vectorKneeToPelvisFrame0 = mostRecentPelvisBeltCenterPositionFrame0 - avgPosKneeMarkerFrame0;

            vectorKneeToPelvis = mostRecentPelvisBeltCenterPosition - avgPosKneeMarkerFrame0;
        }


        // Get the stance model velocity jacobian transpose in frame 0 (the stance model base frame)
        Matrix<double> pelvisVelocityJacobianTransposeInFrame0 =
                stanceModel.GetPelvisForceVelocityJacobianTranspose(); //get pelvis velocity jacobian transposes

        // GRAVITY AND NET TORQUE COMPENSATION****************************************************************************
        // Compute net torque compensation (gravity compensation + balancing other forces) force at the pelvis, if needed. 
        // This code runs if either gravity compensation, torque compensation, or both are requested.
        // ***************************************************************************************************************
        Vector3 pelvisForceVectorForTorqueBalanceInFrame0 = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 pelvisForceVectorForTorqueBalanceViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 pelvisForceVectorForTorqueBalanceViveFrame = new Vector3(0.0f, 0.0f, 0.0f);
        if (pelvisForceFieldSettings.pelvisGravityCompSettings == GravityCompensationTypeEnum.GravityComp || 
            pelvisForceFieldSettings.multisegmentTorqueCompSettings == TorqueCompensationTypeEnum.FullCompensation)
        {
            // Run unit test on the gravity torque computation
            // 1.) Set the model joint variables to known values (all 0 except the knee)

            // 2.) Compute the gravity torques at each joint in this configuration


            // Get torque at the ankle due to gravity acting on body masses above the ankle joint (stored as an instance variable).
            // Call subject-specific kinematic model to get gravity torque at joints
            // Before we actually start any compensations, we tell the stance model to update it's joint positions based on the marker data, 
            // and we retrieve the velocity Jacobian transpose for force-torque conversions

            PrintDebugIfDebugModeFlagIsTrue("Knee angle is: " + jointAngles[2] + "and jointTorquesFromGravity knee is: " +
                                            jointTorquesFromGravity[2]);
            PrintDebugIfDebugModeFlagIsTrue("jointTorquesFromGravity at joint 1: " + jointTorquesFromGravity[0] +
                                            " and at joint 2: " + jointTorquesFromGravity[1]
                                            + " and at joint 3: " + jointTorquesFromGravity[2] + " and at joint 4: " +
                                            jointTorquesFromGravity[3] + " and" +
                                            "at joint 5: " + jointTorquesFromGravity[4]);

            // negative sign means ?
            // Get gravity torques at the ankle and knee joints. Add them to the net torque at these joints.
            double ankleTorqueTheta1Net = -jointTorquesFromGravity[0];
            double ankleTorqueTheta2Net = -jointTorquesFromGravity[1];
            double kneeTorqueTheta3Net = -jointTorquesFromGravity[2];
            // Theta 4,5 computed just in case we want to view them. NOT USED.
            double pelvisTorqueTheta4Net = -jointTorquesFromGravity[3];
            double pelvisTorqueTheta5Net = -jointTorquesFromGravity[4];

            // Init a second set of torques that represent the torque we'd like to compensate. 
            double ankleTorqueTheta1ToCompensate = 0.0;
            double ankleTorqueTheta2ToCompensate = 0.0; 
            double kneeTorqueTheta3ToCompensate = 0.0;

            // If doing gravity compensation
            if (pelvisForceFieldSettings.pelvisGravityCompSettings == GravityCompensationTypeEnum.GravityComp)
            {
                // Specify the gravity torques the pelvis force will compensate.
                // This might be only a portion of the gravity torque. 
                // Negative sign needed for the gravity torque terms.
                ankleTorqueTheta1ToCompensate = -pelvisGravityCompFractionThisFrame * jointTorquesFromGravity[0];
                ankleTorqueTheta2ToCompensate = -pelvisGravityCompFractionThisFrame * jointTorquesFromGravity[1];
                kneeTorqueTheta3ToCompensate = -pelvisGravityCompFractionThisFrame * jointTorquesFromGravity[2];
            }


            // Store torque compensation only - init to zero here, will add chest-generated torques below.
            double ankleTorqueTheta1ChestCompOnly = 0.0;
            double ankleTorqueTheta2ChestCompOnly = 0.0;
            double kneeTorqueTheta3ChestCompOnly = 0.0;


            // Eliminate the gravity knee torque we consider IF it creates a negative torque at the knee. 
            // We do not want our robot to work to flex the knee at any point.
            /*                if (kneeTorqueTheta3Net < 0) // if gravity is extending the knee
                            {
                                // Eliminate needed knee torque balance because we do NOT want to flex the knee
                                // using the robot.
                                kneeTorqueTheta3Net = 0;
                            }*/

            /*            if (usingShankBelts)
                        {
                            ankleTorqueTheta1Net = ankleTorqueTheta1Net + resultantJointTorquesFromDesiredShankForce.At(0);
                            ankleTorqueTheta2Net = ankleTorqueTheta2Net + resultantJointTorquesFromDesiredShankForce.At(1);

                        }*/

            // If we're using the chest belt,
            // we must consider the chest force contribution to the joint net torques
            if (usingChestBelt)
            {
                // Compute total net torque by adding the chest force-generated torques. 
                // Add them to the net torque total.
                ankleTorqueTheta1Net =
                    ankleTorqueTheta1Net + resultantJointTorquesFromTensionSolvedChestForce.At(0);
                ankleTorqueTheta2Net =
                    ankleTorqueTheta2Net + resultantJointTorquesFromTensionSolvedChestForce.At(1);
                kneeTorqueTheta3Net = kneeTorqueTheta3Net + resultantJointTorquesFromTensionSolvedChestForce.At(2);
                pelvisTorqueTheta4Net =
                    pelvisTorqueTheta4Net + resultantJointTorquesFromTensionSolvedChestForce.At(3);
                pelvisTorqueTheta5Net =
                    pelvisTorqueTheta5Net + resultantJointTorquesFromTensionSolvedChestForce.At(4);

                // However, we also want to separately compute a torque-compensation only term.
                ankleTorqueTheta1ChestCompOnly = resultantJointTorquesFromTensionSolvedChestForce.At(0);
                ankleTorqueTheta2ChestCompOnly = resultantJointTorquesFromTensionSolvedChestForce.At(1);
                kneeTorqueTheta3ChestCompOnly = resultantJointTorquesFromTensionSolvedChestForce.At(2);
            }

            // If we are doing intersegment torque compensation
            // NOTE: we always compensate for 100% of the chest force torques, if we're compensating for them at all.
            if (pelvisForceFieldSettings.multisegmentTorqueCompSettings == TorqueCompensationTypeEnum.FullCompensation)
            {
                // We ALWAYS compensate for 100% of the chest force-generated torques if "gravity compensation" mode is active
                ankleTorqueTheta1ToCompensate =
                   ankleTorqueTheta1ToCompensate + resultantJointTorquesFromTensionSolvedChestForce.At(0);
                ankleTorqueTheta2ToCompensate =
                    ankleTorqueTheta2ToCompensate + resultantJointTorquesFromTensionSolvedChestForce.At(1);
                kneeTorqueTheta3ToCompensate = kneeTorqueTheta3ToCompensate + resultantJointTorquesFromTensionSolvedChestForce.At(2);
            }
    
            //syntax : pelvisVelocityJacobianFrame0.SubMatrix(starting row, # rows to extract, starting col, # cols to extract)
            // Isolate the rows of the velocity Jacobian transpose
            Vector<double> pelvisVelocityJacobianFrame0WithNormalVectorRow1 = pelvisVelocityJacobianTransposeInFrame0.Row(0); // related to torque at joint 1
            Vector<double> pelvisVelocityJacobianFrame0WithNormalVectorRow2 = pelvisVelocityJacobianTransposeInFrame0.Row(1); // related to torque at joint 2
            Vector<double> pelvisVelocityJacobianFrame0WithNormalVectorRow3 = pelvisVelocityJacobianTransposeInFrame0.Row(2); // related to torque at joint 3
                                                                                                                                // Init. a vector that will store the computed pelvic forces.
            Vector<double> pelvisForceVectorForTorqueBalanceCompNetNumerics = Vector<double>.Build.DenseOfArray(new double[] { 0.0, 0.0, 0.0 });


            // NOTE: the 4R model is not really used currently, so this case should be viewed as buggy/not working.************************************************
            if (kinematicModelStanceScript.GetStanceModelSelector() == StanceModelSelector.FourRModel)
            {
                // Build a system of equations with the net torque at the ankles = pelvic velocity jacobian T * desired pelvic force. 
                // The in-plane requirement will be a constraint.
                // Net torque at ankle = Jv,p * Fp s.t. Fp is perpendicular to knee-pelvis line.
                // get first 2 rows of numerics matrix 
                /*pelvisVelocityJacobianTransposeInFrame0.Row(1);
                pelvisVelocityJacobianTransposeInFrame0.Row(2);*/

                // Fill a matrix that is used as the constraint matrix, A, in A*F = b. 
                // Typically, the pelvis acts to balance torque at the ankle. We want to allow both 
                Matrix<double> pelvisVelocityJacobianFrame0 = Matrix<double>.Build.Dense(2, 3);

                // The constraint matrix is just the first three rows of the velocity Jacobian transpose. 
                // For the pelvis it's a 3x3 matrix.
                // Note - we may want to exclude the third row if we're never controlling knee torque with the pelvic force.
                pelvisVelocityJacobianFrame0.SetRow(0,
                        pelvisVelocityJacobianFrame0WithNormalVectorRow1);
                pelvisVelocityJacobianFrame0.SetRow(1,
                        pelvisVelocityJacobianFrame0WithNormalVectorRow2);
                /*pelvisVelocityJacobianFrame0.SetRow(2, 
                    pelvisVelocityJacobianFrame0WithNormalVectorRow3);
*/
                // For some reason we convert it to a 2D array before printing it, if we're in Debug mode.
                double[,] pelvisVelocityJacobianFrame0WithNormalVector2DArr =
                    pelvisVelocityJacobianFrame0.ToArray();

                for (int rowIndex = 0;
                        rowIndex < pelvisVelocityJacobianFrame0WithNormalVector2DArr.GetLength(0);
                        rowIndex++)
                {
                    for (int colIndex = 0;
                            colIndex < pelvisVelocityJacobianFrame0WithNormalVector2DArr.GetLength(1);
                            colIndex++)
                    {
                        PrintDebugIfDebugModeFlagIsTrue("pelvis velocity Jacobian transpose plus normal-to-thigh constraint: row " +
                                                        rowIndex +
                                                        " col " + colIndex + " value: " +
                                                        pelvisVelocityJacobianFrame0WithNormalVector2DArr[rowIndex, colIndex]);
                    }
                }

                // Store the first column of the Jacobian transpose
                Vector<double> verticalForceColumnPelvisVelocityJacobianTranpose =
                    pelvisVelocityJacobianFrame0.Column(0, 0, 2); // column index, starting row index, length including starting row index

                // If we're adding a vertical body weight compensation, compute the resultant torque now.
                // DOES THIS APPROACH MAKE SENSE? PROBABLY NOT.
                float desiredPelvicVerticalForceComponent = 0.0f;
                Vector<double> torquesDueToPelvicVerticalBodyWeightCompensation = Vector<double>.Build.Dense(2);
                if (pelvisForceFieldSettings.useVerticalForce == true)
                {
                    // desired vertical force is fraction * mass * gravitationalAcceleration
                    desiredPelvicVerticalForceComponent =
                        pelvisForceFieldSettings.verticalForceBodyWeightFraction *
                        subjectSpecificDataScript.getSubjectMassInKilograms() * gravity;

                    // Compute the resultant torques
                    torquesDueToPelvicVerticalBodyWeightCompensation = verticalForceColumnPelvisVelocityJacobianTranpose * 
                    desiredPelvicVerticalForceComponent;

                    // Alter the net torques to reflect the upward force
                    ankleTorqueTheta1ToCompensate =
                        ankleTorqueTheta1ToCompensate + torquesDueToPelvicVerticalBodyWeightCompensation.At(0);
                    ankleTorqueTheta2ToCompensate =
                        ankleTorqueTheta2ToCompensate + torquesDueToPelvicVerticalBodyWeightCompensation.At(1);
                }

                // Now remove the first (x0 axis = vertical axis) column of the Jacobian transpose, because we 
                // balance ankle torques using a planar pelvic force component.
                Matrix<double> pelvisVelocityJacobianGroundPlaneComponentsFrame0 =
                    pelvisVelocityJacobianFrame0.RemoveColumn(0);

                // ADD FRACTION OF KNEE GRAVITY ASSISTANCE, INSTEAD OF 100%
                Vector<double> torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                {
                        -ankleTorqueTheta1ToCompensate,
                        -ankleTorqueTheta2ToCompensate
                });

                PrintDebugIfDebugModeFlagIsTrue("b term when solving for pelvic force : (" + torqueVectorPelvis[0] + ", " +
                                                torqueVectorPelvis[1] + ")");

                // Sse solver to get x (x = force vector)
                pelvisForceVectorForTorqueBalanceCompNetNumerics =
                    pelvisVelocityJacobianGroundPlaneComponentsFrame0.Solve(torqueVectorPelvis);

                // Convert force vector solution to Vector3 format
                pelvisForceVectorForTorqueBalanceInFrame0.x =
                    (float)desiredPelvicVerticalForceComponent;
                pelvisForceVectorForTorqueBalanceInFrame0.y =
                    (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(0);
                pelvisForceVectorForTorqueBalanceInFrame0.z =
                    (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(1);

            }
            // 5R model
            else if (kinematicModelStanceScript.GetStanceModelSelector() == StanceModelSelector.FiveRModelWithKnees)
            {
                // Build a system of equations with the net torque at the ankles = pelvic velocity jacobian T * desired pelvic force. 
                // The in-plane requirement will be a constraint.
                // Net torque at ankle = Jv,p * Fp s.t. Fp is perpendicular to knee-pelvis line.
                // get first 2 rows of numerics matrix 
                /*pelvisVelocityJacobianTransposeInFrame0.Row(1);
                pelvisVelocityJacobianTransposeInFrame0.Row(2);*/

                // Fill a matrix that is used as the constraint matrix, A, in A*F = b. 
                // Typically, the pelvis acts to balance torque at the ankle, but it can also balance 
                // knee torque. 
                // Initialize the Jacobian to be a 3 row (for each joint) and 3 column matrix.
                Matrix<double> pelvisVelocityJacobianFrame0 = Matrix<double>.Build.Dense(3, 3);

                // The constraint matrix is just the first three rows of the velocity Jacobian transpose. 
                // For the pelvis it's a 3x3 matrix.
                // Note - we may want to exclude the third row if we're never controlling knee torque with the pelvic force.
                pelvisVelocityJacobianFrame0.SetRow(0,
                        pelvisVelocityJacobianFrame0WithNormalVectorRow1);
                pelvisVelocityJacobianFrame0.SetRow(1,
                        pelvisVelocityJacobianFrame0WithNormalVectorRow2);
                pelvisVelocityJacobianFrame0.SetRow(2, 
                    pelvisVelocityJacobianFrame0WithNormalVectorRow3);

                // For some reason we convert it to a 2D array before printing it, if we're in Debug mode.
                double[,] pelvisVelocityJacobianFrame0WithNormalVector2DArr =
                    pelvisVelocityJacobianFrame0.ToArray();

                for (int rowIndex = 0;
                        rowIndex < pelvisVelocityJacobianFrame0WithNormalVector2DArr.GetLength(0);
                        rowIndex++)
                {
                    for (int colIndex = 0;
                            colIndex < pelvisVelocityJacobianFrame0WithNormalVector2DArr.GetLength(1);
                            colIndex++)
                    {
                        PrintDebugIfDebugModeFlagIsTrue("pelvis velocity Jacobian transpose plus normal-to-thigh constraint: row " +
                                                        rowIndex +
                                                        " col " + colIndex + " value: " +
                                                        pelvisVelocityJacobianFrame0WithNormalVector2DArr[rowIndex, colIndex]);
                    }
                }

                // Store the first column of the Jacobian transpose
                Vector<double> verticalForceColumnPelvisVelocityJacobianTranpose =
                    pelvisVelocityJacobianFrame0.Column(0, 0, pelvisVelocityJacobianFrame0.RowCount); // column index, starting row index, length including starting row index

                // If we're adding a vertical body weight compensation, compute the resultant torque now
                float desiredPelvicVerticalForceComponent = 0.0f;
                // Get torques at joints (3 joints below pelvis - 2 at ankle and knee)
                Vector<double> torquesDueToPelvicVerticalBodyWeightCompensation = 
                    Vector<double>.Build.Dense(pelvisVelocityJacobianFrame0.RowCount);
                if (pelvisForceFieldSettings.useVerticalForce == true)
                {
                    // desired vertical force is fraction * mass * gravitationalAcceleration
                    desiredPelvicVerticalForceComponent =
                        pelvisForceFieldSettings.verticalForceBodyWeightFraction *
                        subjectSpecificDataScript.getSubjectMassInKilograms() * gravity;

                    // Compute the resultant torques
                    torquesDueToPelvicVerticalBodyWeightCompensation = desiredPelvicVerticalForceComponent
                        * verticalForceColumnPelvisVelocityJacobianTranpose;

                    // Alter the net torques to reflect the upward force
                    ankleTorqueTheta1ToCompensate =
                        ankleTorqueTheta1ToCompensate + torquesDueToPelvicVerticalBodyWeightCompensation.At(0);
                    ankleTorqueTheta2ToCompensate =
                        ankleTorqueTheta2ToCompensate + torquesDueToPelvicVerticalBodyWeightCompensation.At(1);
                    kneeTorqueTheta3ToCompensate =
                        kneeTorqueTheta3ToCompensate + torquesDueToPelvicVerticalBodyWeightCompensation.At(2);
                }

                // Depending on the Control type, get the correct formulation for the Jacobian and any constraints
                // AF = torques, where F is the pelvic force, b is the desired joint torques, and A could be the Jacobian 
                // with or without constraint equations.

                // This first time setting torqueVectorPelvis could be viewed as the default mode (it will be overwritten 
                // depending on settings below)
                Vector<double> torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                        {
                                    -ankleTorqueTheta1ToCompensate,
                                    -ankleTorqueTheta2ToCompensate, 
                                    -kneeTorqueTheta3ToCompensate
                        }); ; // the joint torques we'll be controlling in our computation

                // The final Jacobian we'll use when we solve for the pelvic torques. NOTE - we make a deep copy of the Jacobian, 
                // as = actually does equality by reference!
                Matrix<double> pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.Clone(); // the "final" Jacobian we'll use for the computation

                // Flag indicating if the vertical (x-axis) force is zero
                bool zeroPelvicVerticalForceFlag = false; // init to false, set below!

                // Flag indicating whether a solution for the pelvic force has been found. 
                // In our state machine, which is needed in the case of
                // simultaneous knee flexion torque and ankle dorsiflexion torque, 
                // we solve for the pelvic force and don't want to solve again after the conditional. 
                bool solutionFoundFlag = false;

                // 1.) Ground-plane pelvic force
                if (pelvisForceFieldSettings.pelvicForceDirection == PelvicForceTypeEnum.ground_plane)
                {
                    // Set a flag noting that the x-axis component of the force will be zero.
                    zeroPelvicVerticalForceFlag = true;

                    // Controlling ankle torques (theta1, theta2) only
                    if (pelvisForceFieldSettings.pelvicForceControlledJoints == PelvicForceJointsToControl.ankles_only)
                    {
                        // Select the Jacobian i.) first two rows because we only care about ankle torques and 
                        // ii.) last two columns because we're not controlling vertical pelvic force
                        pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(2);
                        pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                        // Select the ankle joint torques
                        torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                        {
                                    -ankleTorqueTheta1ToCompensate,
                                    -ankleTorqueTheta2ToCompensate
                        });
                    }else if (pelvisForceFieldSettings.pelvicForceControlledJoints == PelvicForceJointsToControl.ankles_and_knee)
                    {
                        // We must implement a state-based controller! 
                        // Assumptions: the shank cables are also being used.
                        // If the ankle torque is positive (ankle being dorsiflexed),
                        // then the shank cables can compensate for it, and we should
                        // compensate for the knee torque
                        if (ankleTorqueTheta1Net >= 0) // consider total ankle torque, not the desired compensation torque
                        {
                            // Compensate for knee torque and ML ankle torques (theta2).
                            // So, select the Jacobian i.) second and third rows because we do NOT control AP ankle torque 
                            // and ii.) last two columsn because we're not controlling vertical pelvic force
                            pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(0);
                            pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                            // Select the ankle ML and knee joint torques
                            torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                            {
                                    -ankleTorqueTheta2ToCompensate,
                                    -kneeTorqueTheta3ToCompensate
                            });
                        }
                        // else if the ankle is being plantarflexed (falling backwards at the ankle)
                        else
                        {
                            // If the knee net torque is negative (knee being extended)
                            if(kneeTorqueTheta3Net < 0)
                            {
                                // Then we compensate for the ankle torques
                                // So, select the Jacobian i.) first and second rows because we do NOT control knee torque
                                // and ii.) last two columsn because we're not controlling vertical pelvic force
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(2);
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                                // Select the ankle joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                    -ankleTorqueTheta1ToCompensate,
                                    -ankleTorqueTheta2ToCompensate
                                });
                            }
                            // If the knee is being flexed
                            else
                            {
                                // Compensate for either theta1 or theta3 torque, depending on
                                // which requires a larger force for compensation.
                                // This depends NOT on the knee torque magnitude, but upon which requires a  
                                // larger pelvic force F in the torque = Jv*F equation.
                                // This essentially involves solving the problem both when we are controlling
                                // theta1 + theta2 and theta2 + theta3, then choosing the one that requires the larger pelvic force.

                                // Select the ankle joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                        -ankleTorqueTheta1ToCompensate,
                                        -ankleTorqueTheta2ToCompensate
                                });

                                // Select the Jacobian i.) first and second rows because we do NOT control knee torque
                                // and ii.) last two columsn because we're not controlling vertical pelvic force
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(2);
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                                // Sse solver to get x (x = pelvic force vector)
                                Vector<double> pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl =
                                    pelvicVelocityJacobianWithConstraintsFrame0.Solve(torqueVectorPelvis);

                                // Select the ankle ML and knee joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                        -ankleTorqueTheta2ToCompensate,
                                        -kneeTorqueTheta3ToCompensate
                                });

                                // Select the Jacobian i.) second and third rows because we do NOT control AP ankle torque 
                                // and ii.) last two columsn because we're not controlling vertical pelvic force
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(0);
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                                // Sse solver to get x (x = pelvic force vector)
                                Vector<double> pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl =
                                    pelvicVelocityJacobianWithConstraintsFrame0.Solve(torqueVectorPelvis);

                                // Store the pelvic force with the larger magnitude as the solution
                                double magnitudeOne = pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl.L2Norm();
                                double magnitudeTwo = pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl.L2Norm();

                                // Compare the magnitudes and store the larger vector
                                if (magnitudeOne > magnitudeTwo)
                                {
                                    pelvisForceVectorForTorqueBalanceCompNetNumerics = 
                                        pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl;
                                }
                                else
                                {
                                    pelvisForceVectorForTorqueBalanceCompNetNumerics = 
                                        pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl;
                                }

                                // Note that in this case, we have already found a solution and don't need to recompute 
                                // one after the conditional
                                solutionFoundFlag = true;

                                /*// If the knee torque compensation requires a larger pelvic force F magnitude
                                if (Mathf.Abs((float) kneeTorqueTheta3Net) > Mathf.Abs((float) ankleTorqueTheta1Net))
                                {
                                    // Then compensate for the ankle ML torque and knee torque
                                    // So, select the Jacobian i.) second and third rows because we do NOT control AP ankle torque 
                                    // and ii.) last two columsn because we're not controlling vertical pelvic force
                                    pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(0);
                                    pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                                    // Select the ankle ML and knee joint torques
                                    torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                    {
                                        -ankleTorqueTheta2ToCompensate,
                                        -kneeTorqueTheta3ToCompensate
                                    });
                                }
                                // Else if the magnitude of the AP ankle torque is greater
                                else
                                {
                                    // Then we compensate for the ankle torques
                                    // So, select the Jacobian i.) first and second rows because we do NOT control knee torque
                                    // and ii.) last two columsn because we're not controlling vertical pelvic force
                                    pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.RemoveRow(2);
                                    pelvicVelocityJacobianWithConstraintsFrame0 = pelvicVelocityJacobianWithConstraintsFrame0.RemoveColumn(0);

                                    // Select the ankle joint torques
                                    torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                    {
                                        -ankleTorqueTheta1ToCompensate,
                                        -ankleTorqueTheta2ToCompensate
                                    });
                                }*/
                            }
                        }
                    
                    }
                    else
                    {
                        // Should be unreachable or an undefined case was added to the enum
                        Debug.LogError("Invalid case in pelvic controlled joints enum.");
                    }
                } // End ground-plane pelvic force
                // Else if we're constraining the force to be perpendicular to the thigh
                else if(pelvisForceFieldSettings.pelvicForceDirection == PelvicForceTypeEnum.perpendicular_to_thigh)
                {
                    // Start by developing the constraint equation. 
                    // The constraint is that the pelvic force Fx and Fy components must be perpendicular to the knee-to-pelvis line.
                    // The pelvic force dotted with this unit vector is equal to zero.
                    float nx = vectorKneeToPelvisFrame0.x;
                    float ny = vectorKneeToPelvisFrame0.y;
                    float nz = vectorKneeToPelvisFrame0.z;

                    // Fill a matrix that is used as the constraint matrix, A, in A*F = b
                    Matrix<double> pelvisVelocityJacobianFrame0WithNormalVector = Matrix<double>.Build.Dense(3, 3);

                    // We're solving for pelvic force that torque balances theta2, theta3, and is perpendicular to the knee-pelvis line
                    // add normal vector to third row; normal vector = knee to pelvis vector
                    Vector<double> pelvicForceNormalToThighConstraintVector = Vector<double>.Build.DenseOfArray(new double[] { nx, ny, nz });
                    pelvicForceNormalToThighConstraintVector = pelvicForceNormalToThighConstraintVector.Normalize(2); //2 indicates euclidian norm
                    PrintDebugIfDebugModeFlagIsTrue("forcePlaneNormalVector: " + pelvicForceNormalToThighConstraintVector[0]);
                       

                    // If force is perpendicular to thigh, we ONLY allow ankle and knee torque control. 
                    // If targeting just ankles, use ground_plane or no_constraints pelvic force.
                    if (pelvisForceFieldSettings.pelvicForceControlledJoints == PelvicForceJointsToControl.ankles_and_knee)
                    {
                        // We must implement a state-based controller! 
                        // Assumptions: the shank cables are also being used.
                        // If the ankle torque is positive (ankle being dorsiflexed),
                        // then the shank cables can compensate for it, and we should
                        // compensate for the knee torque IF it is positive.
                        if (ankleTorqueTheta1Net >= 0)
                        {
                            //  If the knee torque is positive (knees being flexed)
                            if (kneeTorqueTheta3Net > 0)
                            {
                                // Compensate for knee torque and ML ankle torques (theta2).
                                // So, select the Jacobian i.) second and third rows because we do NOT control AP ankle torque  
                                // We overwrite the unneeded row with the constraint!
                                pelvicVelocityJacobianWithConstraintsFrame0.SetRow(0, pelvicForceNormalToThighConstraintVector);

                                // Select the ankle ML and knee joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                    0.0, // the dot product of the force and the unit vector from knee to pelvic center is 0
                                    -ankleTorqueTheta2ToCompensate,
                                    -kneeTorqueTheta3ToCompensate
                                });
                            }
                            // Else if the knee torque is negative (knees being extended), then the pelvic force can assist at theta1, reducing shank cable effort
                            else
                            {
                                // Then we compensate for the ankle torques
                                // So, select the Jacobian i.) first and second rows because we do NOT control knee torque
                                // Overwrite the unneeded row with the constraint!
                                pelvicVelocityJacobianWithConstraintsFrame0.SetRow(2, pelvicForceNormalToThighConstraintVector);

                                // Select the ankle joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                    -ankleTorqueTheta1ToCompensate,
                                    -ankleTorqueTheta2ToCompensate,
                                    0.0,
                                });
                            }

                        }
                        // else if the ankle theta1 torque is negative
                        else
                        {
                            // If the knee net torque is negative (knee being extended)
                            if (kneeTorqueTheta3Net < 0)
                            {
                                // Then we compensate for the ankle torques
                                // So, select the Jacobian i.) first and second rows because we do NOT control knee torque
                                // Overwrite the unneeded row with the constraint!
                                pelvicVelocityJacobianWithConstraintsFrame0.SetRow(2, pelvicForceNormalToThighConstraintVector);

                                // Select the ankle joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                    -ankleTorqueTheta1ToCompensate,
                                    -ankleTorqueTheta2ToCompensate,
                                    0.0,
                                });
                            }
                            // If the knee torque is positive (being flexed)
                            else
                            {

                                // STATE MACHINE REQUIRED!
                                // Compensate for either theta1 or theta3 torque, depending on
                                // which requires a larger force for compensation.
                                // This depends NOT on the knee torque magnitude, but upon which requires a  
                                // larger pelvic force F in the torque = Jv*F equation.
                                // This essentially involves solving the problem both when we are controlling
                                // theta1 + theta2 and theta2 + theta3, then choosing the one that requires the larger pelvic force.

                                // Select the ankle joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                        -ankleTorqueTheta1ToCompensate,
                                        -ankleTorqueTheta2ToCompensate, 
                                        0.0
                                });

                                Vector<double> torqueVectorPelvisForThetaOneTwo = torqueVectorPelvis;

                                // Select the Jacobian i.) first and second rows because we do NOT control knee torque
                                // Overwrite the unneeded row with the constraint!
                                pelvicVelocityJacobianWithConstraintsFrame0.SetRow(2, pelvicForceNormalToThighConstraintVector);

                                // Store Jacobiaan for print debugging only
                                Matrix<double> pelvicVelJacobianWithConstraintThetaOneTwo = pelvicVelocityJacobianWithConstraintsFrame0.Clone();

                                // Sse solver to get x (x = pelvic force vector)
                                Vector<double> pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl =
                                    pelvicVelocityJacobianWithConstraintsFrame0.Solve(torqueVectorPelvis);

                                // Select the ankle ML and knee joint torques
                                torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                {
                                        0.0,
                                        -ankleTorqueTheta2ToCompensate,
                                        -kneeTorqueTheta3ToCompensate
                                });

                                // Store
                                Vector<double> torqueVectorPelvisForThetaTwoThree = torqueVectorPelvis;

                                // Now reset the Jacobian with constraints to the jacobian by making a deep copy of the original matrix
                                // NOTE: a simple equality would then point the left hand variable to the same object in memory as the right hand variable!
                                pelvicVelocityJacobianWithConstraintsFrame0 = pelvisVelocityJacobianFrame0.Clone();

                                // Select the Jacobian i.) second and third rows because we do NOT control ankle AP torque
                                // Overwrite the unneeded row with the constraint!
                                pelvicVelocityJacobianWithConstraintsFrame0.SetRow(0, pelvicForceNormalToThighConstraintVector);

                                // Store Jacobiaan for print debugging only
                                Matrix<double> pelvicVelJacobianWithConstraintThetaTwoThree = pelvicVelocityJacobianWithConstraintsFrame0.Clone();

                                // Sse solver to get x (x = pelvic force vector)
                                Vector<double> pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl =
                                    pelvicVelocityJacobianWithConstraintsFrame0.Solve(torqueVectorPelvis);

                                // Store the pelvic force with the larger magnitude as the solution
                                double magnitudeOne = pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl.L2Norm();
                                double magnitudeTwo = pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl.L2Norm();

                                // Compare the magnitudes and store the larger vector
                                if (magnitudeOne > magnitudeTwo)
                                {
                                    pelvisForceVectorForTorqueBalanceCompNetNumerics =
                                        pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl;
                                }
                                else
                                {
                                    pelvisForceVectorForTorqueBalanceCompNetNumerics =
                                        pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl;
                                }

                                Debug.Log(
                                    $"Pelvic force solution Theta1&2: [{string.Join(", ", pelvisForceVectorForTorqueBalanceThetaOneAndTwoControl.ToArray())}], " +
                                    $"Torque: [{string.Join(", ", torqueVectorPelvisForThetaOneTwo.ToArray())}], " +
                                    $"Jacobian:\n[{MatrixToString(pelvicVelJacobianWithConstraintThetaOneTwo)}] | " +
                                    $"Pelvic force solution Theta2&3: [{string.Join(", ", pelvisForceVectorForTorqueBalanceThetaTwoAndThreeControl.ToArray())}], " +
                                    $"Torque: [{string.Join(", ", torqueVectorPelvisForThetaTwoThree.ToArray())}], " +
                                    $"Jacobian:\n[{MatrixToString(pelvicVelJacobianWithConstraintThetaTwoThree)}]"
                                );

                                // Note that in this case, we have already found a solution and don't need to recompute 
                                // one after the conditional
                                solutionFoundFlag = true;

                                // Compensate for either theta1 or theta3 torque, depending on which is greater.
                                // If the magnitude of the knee torque is greater
                                if (Mathf.Abs((float)kneeTorqueTheta3Net) > Mathf.Abs((float)ankleTorqueTheta1Net))
                                {
                                    // Then compensate for the ankle ML torque and knee torque
                                    // So, select the Jacobian i.) second and third rows because we do NOT control AP ankle torque 
                                    // Overwrite the unneeded row with the constraint!
                                    pelvicVelocityJacobianWithConstraintsFrame0.SetRow(0, pelvicForceNormalToThighConstraintVector);

                                    // Select the ankle ML and knee joint torques
                                    torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                    {
                                        0.0,
                                        -ankleTorqueTheta2ToCompensate,
                                        -kneeTorqueTheta3ToCompensate
                                    });
                                }
                                // Else if the magnitude of the AP ankle torque is greater
                                else
                                {
                                    // Then we compensate for the ankle torques
                                    // So, select the Jacobian i.) first and second rows because we do NOT control knee torque
                                    // and ii.) last two columsn because we're not controlling vertical pelvic force.
                                    // Overwrite the unneeded row with the constraint!
                                    pelvicVelocityJacobianWithConstraintsFrame0.SetRow(2, pelvicForceNormalToThighConstraintVector);

                                    // Select the ankle joint torques
                                    torqueVectorPelvis = Vector<double>.Build.DenseOfArray(new double[]
                                    {
                                        -ankleTorqueTheta1ToCompensate,
                                        -ankleTorqueTheta2ToCompensate,
                                        0.0,
                                    });
                                }
                            }
                        }
                    } // END pelvic force perpendicular to thigh case
                    // Else if no constraints are placed on the pelvic force direction
                    else if (pelvisForceFieldSettings.pelvicForceDirection == PelvicForceTypeEnum.no_constraints)
                    {
                        // No implementation! I think we should examine the jacobian transpose and see
                        // how it's conditioned near the vertical standing position...
                        Debug.LogError("Have not implemented the no-constraints case for the pelvic force.");
                    }
                    else
                    {
                        // Should be unreachable or an undefined case was added to the enum
                        Debug.LogError("Invalid case in pelvic controlled joints enum.");
                    }
                }

                // Print the terms if debugging. 
                // FIX
                PrintDebugIfDebugModeFlagIsTrue("A * F = b, b term when solving for pelvic force : (" + torqueVectorPelvis[0] + ", " +
                                                    torqueVectorPelvis[1] + ")");
        


                // If we haven't already solved for the desired pelvic force 
                // (e.g., state machine when controlling knee and ankle)
                if (solutionFoundFlag == false)
                {
                    // Sse solver to get x (x = pelvic force vector)
                    pelvisForceVectorForTorqueBalanceCompNetNumerics =
                        pelvicVelocityJacobianWithConstraintsFrame0.Solve(torqueVectorPelvis);
                }



                // Convert force vector solution to Vector3 format
                // If we allow a vertical pelvic force, then the 
                if(zeroPelvicVerticalForceFlag == false)
                {
                    pelvisForceVectorForTorqueBalanceInFrame0.x =
                        (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(0) + desiredPelvicVerticalForceComponent;
                    pelvisForceVectorForTorqueBalanceInFrame0.y =
                        (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(1);
                    pelvisForceVectorForTorqueBalanceInFrame0.z =
                        (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(2);
                }
                // Ground plane pelvic force condition
                // In this case, the Jacobian first row and first column were dropped, it is 2x2, 
                // and the pelvic force solution is the Fy and Fz in frame 0.
                else
                {
                    pelvisForceVectorForTorqueBalanceInFrame0.x =
                        (float)desiredPelvicVerticalForceComponent;
                    pelvisForceVectorForTorqueBalanceInFrame0.y =
                        (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(0);
                    pelvisForceVectorForTorqueBalanceInFrame0.z =
                        (float)pelvisForceVectorForTorqueBalanceCompNetNumerics.At(1);
                }

                Debug.Log($"Pelvic force solution: [{string.Join(", ", pelvisForceVectorForTorqueBalanceInFrame0.ToString())}], " +
                            " jointTorquesFromGravity at joint 1: " + jointTorquesFromGravity[0] +
                                            " and at joint 2: " + jointTorquesFromGravity[1]
                                            + " and at joint 3: " + jointTorquesFromGravity[2] + " and at joint 4: " +
                                            jointTorquesFromGravity[3] + " and" +
                                            "at joint 5: " + jointTorquesFromGravity[4]
                        );

            } // End 5R case
        } // end gravity/torque compensation block   
        

        // If we're applying a position and velocity-based PD controller
        Vector3 pdPelvisForce = new Vector3(0.0f, 0.0f, 0.0f);
        if (pelvisForceFieldSettings.pelvisPdControlSettings == PdControlTypeEnum.ConstantPosition 
            || pelvisForceFieldSettings.pelvisPdControlSettings == PdControlTypeEnum.PositionTrajectory)
        {
            Vector3 desiredPelvisPosViconFrameInMeter = new Vector3();
            Vector3 desiredPelvisVelViconFrameInMeterPerSec = new Vector3();
            Vector3 desiredPelvisPosViveFrameInMeter = new Vector3();
            Vector3 desiredPelvisVelViveFrameInMeterPerSec = new Vector3();
            Vector3 derivativeComponentForce = new Vector3();
            Vector3 proportionalComponentForce = new Vector3();
            // Get the current desired pelvis position and velocity
            if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
            {
                (desiredPelvisPosViconFrameInMeter, desiredPelvisVelViconFrameInMeterPerSec) =
                    GetCurrentPelvisDesiredPositionAndVelocityInViconFrame();
                // Compute the proportional (position) term
                Vector3 pelvisPositionErrorInMeter = (desiredPelvisPosViconFrameInMeter - mostRecentPelvisBeltCenterPosition)/1000.0f;
                PrintDebugIfDebugModeFlagIsTrue("Pelvis position error in meter: (" + pelvisPositionErrorInMeter.x + ", " +
                                                pelvisPositionErrorInMeter.y + ", " + pelvisPositionErrorInMeter.z + ")");
                proportionalComponentForce =
                    pelvisKpThisFrame * (1 / convertMillimetersToMeters) * pelvisPositionErrorInMeter;

                // Since we only use PD Control along the y-z plane, eliminate the x component
                proportionalComponentForce.x = 0;

                // Compute the derivative (velocity) term - TO DO!!!!!!
                derivativeComponentForce = pelvisKvThisFrame *
                                                    (desiredPelvisVelViconFrameInMeterPerSec -
                                                    mostRecentPelvisBeltCenterPosition);
            }
            else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
            {
                (desiredPelvisPosViveFrameInMeter, desiredPelvisVelViveFrameInMeterPerSec) =
                    GetCurrentChestDesiredPositionAndVelocityInFrame0();
                // Compute the proportional (position) term
                Vector3 pelvisPositionErrorInMeter = (desiredPelvisPosViveFrameInMeter - mostRecentPelvisBeltCenterPositionFrame0);
                PrintDebugIfDebugModeFlagIsTrue("Pelvis position error in meter: (" + pelvisPositionErrorInMeter.x + ", " +
                                                pelvisPositionErrorInMeter.y + ", " + pelvisPositionErrorInMeter.z + ")");
                proportionalComponentForce =
                    pelvisKpThisFrame *  pelvisPositionErrorInMeter;

                // Since we only use PD Control along the y-z plane, eliminate the x component
                proportionalComponentForce.x = 0;

                // Compute the derivative (velocity) term - TO DO!!!!!!
                derivativeComponentForce = pelvisKvThisFrame *
                                                    (desiredPelvisVelViconFrameInMeterPerSec -
                                                    mostRecentPelvisBeltCenterPosition);
            }

                

            // Sum to get total PD pelvis force
            pdPelvisForce = proportionalComponentForce + derivativeComponentForce;
                
            // Zero the PD force for the squattign task.
            pdPelvisForce = new Vector3(0.0f, 0.0f, 0.0f);

            PrintDebugIfDebugModeFlagIsTrue("Pelvic force PD control force (x,y,z): (" + pdPelvisForce.x + ", " + pdPelvisForce.y + ", " +
                                            pdPelvisForce.z + ")");
        }


        // STORE
        if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            // Store the total desired force on the pelvis (expressed in Vicon frame)
            desiredForcesOnPelvisViconFrame = pelvisForceVectorForTorqueBalanceViconFrame + pdPelvisForce;

            // Compute the joint torques we would ideally apply (before we solve for feasible cable tensions, which may affect what we can actually do). 
            // The Jacobian and force should be expressed in frame 0.
            desiredForcesOnPelvisFrame0 =
                transformFromViconToFrame0.MultiplyVector(desiredForcesOnPelvisViconFrame);
        }
        else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            // Compute the joint torques we would ideally apply (before we solve for feasible cable tensions, which may affect what we can actually do). 
            // The Jacobian and force should be expressed in frame 0.
            desiredForcesOnPelvisFrame0 = pelvisForceVectorForTorqueBalanceInFrame0 + pdPelvisForce;
            PrintDebugIfDebugModeFlagIsTrue(" the desiredForcesOnPelvisFrame0 after is" + desiredForcesOnPelvisFrame0);
        }


        Vector<double> desiredForcesOnPelvisFrame0AsNetNumerics = Vector<double>.Build.DenseOfArray(new double[]
        {
            desiredForcesOnPelvisFrame0.x,
            desiredForcesOnPelvisFrame0.y, desiredForcesOnPelvisFrame0.z
        });
        resultantJointTorquesFromDesiredPelvicForce =
            pelvisVelocityJacobianTransposeInFrame0 * desiredForcesOnPelvisFrame0AsNetNumerics;
        PrintDebugIfDebugModeFlagIsTrue(" the desired force for pelvis is " + desiredForcesOnPelvisFrame0);
            
        /*// Visualize the current FF force in Unity frame (but scaled down by a constant, so it fits on the screen)
        if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            forceFieldVisualizerScript.UpdateForceFieldVectorPelvis(desiredForcesOnPelvisViconFrame / 50.0f);
                
        }
        else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {
            forceFieldVisualizerScript.UpdateForceFieldVectorPelvis(desiredForcesOnPelvisViveFrame / 50.0f);
                
        }*/
    }
       
    private void ComputeDesiredForcesAndTorquesShankSegment()
    {
        // Get torque at the ankle due to gravity acting on body masses above the ankle joint.
            // Call subject-specific kinematic model to get gravity torque at joints

            // Get the total joint torques at the ankle joint 1 (AP joint, or plantarflexion/dorsiflexion). 
            // Note that the gravity torque sign has a built-in negative, because it represents the torque to COUNTERACT gravity,
            // so actual gravity torque has a negative.
            // TRY ignoring gravity and just counteracting torque generated by the other belts.
            float netTorqueAtJoint1 = 0.0f; // FOR NOW, NO GRAVITY COMP//- jointTorquesFromGravity[0];
            if (usingChestBelt)
            {
                netTorqueAtJoint1 = netTorqueAtJoint1 + (float)resultantJointTorquesFromTensionSolvedChestForce[0];
            }

            // if pelvic belt forces are non-zero
            if (usingPelvicBelt)
            {
                Debug.Log("The net Torque from gravity at joint 1 is " + netTorqueAtJoint1);
                netTorqueAtJoint1 = netTorqueAtJoint1 + (float)resultantJointTorquesFromTensionSolvedPelvicForce[0];
                Debug.Log("The resultant Joint Torques at joint 1 From Tension Solved Pelvic Force is " + 
                (float)resultantJointTorquesFromTensionSolvedPelvicForce[0]);
                Debug.Log("The sum of the net torque at joint 1 is " + netTorqueAtJoint1);
            }

            // Using the Jacobian, solve for the shank force needed to generate a counter-torque at the 
            // ankle joint 1 (dorsiflexion/plantarflexion), IF the net torque at this joint is positive (tipping the person forwards).
            // Otherwise, the shank cables would ideally generate no net force (or, realistically, apply minimum tensions).
            float desiredShankForceYAxis = 0.0f;
            if (netTorqueAtJoint1 > 0.0f)
            {
                // Get the transpose of the velocity Jacobian for the shank force in frame 0
                Matrix<double> shankVelocityJacobianTransposeInFrame0 =
                    stanceModel.GetKneeForceVelocityJacobianTranspose();

                // In frame 0, the forward/backward direction is the +y-axis. We want to choose a pure backward force 
                // that generates the desired torque at joint 1.
                // Note also that the shank force should generate the OPPOSITE net torque at joint 1, so we use a negative sign.
                // So, we can calculate this force 
                // as:
                // -desiredYAxisTorque / shankVelocityJacobianTranspose(column 2, first row) = desiredYAxisForce.
                float jointOneShankYAxisForceJacobianEntry =
                    (float)shankVelocityJacobianTransposeInFrame0.At(0, 1); // Net.Numerics is 0-indexed.
                desiredShankForceYAxis = -netTorqueAtJoint1 / jointOneShankYAxisForceJacobianEntry;

                PrintDebugIfDebugModeFlagIsTrue("Net torque at ankle joint 1 is: " + netTorqueAtJoint1 +
                                                ", shank transpose Jacobian y-axis force entry is: " +
                                                jointOneShankYAxisForceJacobianEntry + ", and computed desired shank y-axis force as: " +
                                                desiredShankForceYAxis);
            }
            else
            {
                PrintDebugIfDebugModeFlagIsTrue("Net torque at ankle joint 1 is negative. Minimize desired shank y-axis cable force.");
            }

            // The computed y-axis shank force was in Frame 0. The positive y-axis is forwards in frame 0 
            // but backwards in Vicon frame. So, we convert it to Vicon frame by adding a negative sign.
            desiredForcesOnShankFrame0 = new Vector3(0.0f, desiredShankForceYAxis, 0.0f);

            // Compute the desired shank force as a vector (with only a non-zero y-axis force)
            desiredForcesOnShankViconFrame = new Vector3(0.0f, -desiredForcesOnShankFrame0.y, 0.0f);

            // The shank force is a kp control based on the knee angle. 
            // For now, assume "straight back" for the subject is the +y-axis of the Vicon frame
            /*  if (avgKneeAngleDeg > 0.0f)
              {
                  desiredForcesOnShankViconFrame = new Vector3(0.0f, shankForceKp * avgKneeAngleDeg, 0.0f);
              }*/

            // Compute the joint torques this desired shank force would generate
            Vector<double> desiredForcesOnShankFrame0NetNumerics =
                ConvertVector3ToNetNumericsVector(desiredForcesOnShankFrame0);
            Matrix<double> shankForceVelocityJacobianTranspose = stanceModel.GetKneeForceVelocityJacobianTranspose();
            resultantJointTorquesFromDesiredShankForce =
                shankForceVelocityJacobianTranspose * desiredForcesOnShankFrame0NetNumerics;
        }

        
        // Debugging helper function
        private string MatrixToString(Matrix<double> matrix)
        {
            var rows = new List<string>();
            for (int i = 0; i < matrix.RowCount; i++)
            {
                var row = matrix.Row(i).ToArray();
                rows.Add("[" + string.Join(", ", row.Select(val => val.ToString("F3"))) + "]");
            }
            return string.Join("\n ", rows);
        }





    /*
    else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
    {
                    // Get the current position of the control point for the shank segment  
        Vector3 mostRecentShankBeltCenterPosition =
            centerOfMassManagerScript.GetCenterOfShankBeltPositionInViconFrame();

        // get average knee joint angle (NOT NEEDED, BUT CAN KEEP FOR NOW)
        float leftKneeAngleDeg = centerOfMassManagerScript.GetLeftKneeAngleInDegrees();
        float rightKneeAngleDeg = centerOfMassManagerScript.GetRightKneeAngleInDegrees();
        float avgKneeAngleDeg = (leftKneeAngleDeg + rightKneeAngleDeg) / 2.0f;

        // Get torque at the ankle due to gravity acting on body masses above the ankle joint.
        // Call subject-specific kinematic model to get gravity torque at joints
        float[] jointAngles = stanceModel.GetJointVariableValuesFromMarkerDataInverseKinematics();
        jointTorquesFromGravity = stanceModel.GetGravityTorqueAtEachModelJoint();

        // Get the total joint torques at the ankle joint 1 (AP joint, or plantarflexion/dorsiflexion). 
        // Note that the gravity torque sign has a built-in negative.
        // TRY ignoring gravity and just counteracting torque generated by the other belts.

        // 为什么胸口和腰上的belt只能给joint1产生torque？是不是需要改？
        float netTorqueAtJoint1 = 0.0f; // jointTorquesFromGravity[0];
        if (usingChestBelt)
        {
            netTorqueAtJoint1 = -netTorqueAtJoint1 + (float)resultantJointTorquesFromTensionSolvedChestForce[0];
        }

        if (usingPelvicBelt)
        {
            netTorqueAtJoint1 = -netTorqueAtJoint1 + (float)resultantJointTorquesFromTensionSolvedPelvicForce[0];
        }

        // Using the Jacobian, solve for the shank force needed to generate a counter-torque at the 
        // ankle joint 1 (dorsiflexion/plantarflexion), IF the net torque at this joint is positive (tipping the person forwards).
        // Otherwise, the shank cables would ideally generate no net force (or, realistically, apply minimum tensions).
        float desiredShankForceYAxis = 0.0f;
        if (netTorqueAtJoint1 > 0.0f)
        {
            // Get the transpose of the velocity Jacobian for the shank force in frame 0
            Matrix<double> shankVelocityJacobianTransposeInFrame0 =
                stanceModel.GetKneeForceVelocityJacobianTranspose();

            // In frame 0, the forward/backward direction is the +y-axis. We want to choose a pure backward force 
            // that generates the desired torque at joint 1.
            // Note also that the shank force should generate the OPPOSITE net torque at joint 1, so we use a negative sign.
            // So, we can calculate this force 
            // as:
            // -desiredYAxisTorque / shankVelocityJacobianTranspose(column 2, first row) = desiredYAxisForce.
            float jointOneShankYAxisForceJacobianEntry =
                (float)shankVelocityJacobianTransposeInFrame0.At(0, 1); // Net.Numerics is 0-indexed.
            desiredShankForceYAxis = -netTorqueAtJoint1 / jointOneShankYAxisForceJacobianEntry;

            Debug.Log("Net torque at ankle joint 1 is: " + netTorqueAtJoint1 +
                      ", shank transpose Jacobian y-axis force entry is: " +
                      jointOneShankYAxisForceJacobianEntry + ", and computed desired shank y-axis force as: " +
                      desiredShankForceYAxis);
        }
        else
        {
            Debug.Log("Net torque at ankle joint 1 is negative. Minimize desired shank y-axis cable force.");
        }

        // The computed y-axis shank force was in Frame 0. The positive y-axis is forwards in frame 0 
        // but backwards in Vicon frame. So, we convert it to Vicon frame by adding a negative sign.
        Vector3 desiredForcesOnShankFrame0 = new Vector3(0.0f, desiredShankForceYAxis, 0.0f);


        // Compute the desired shank force as a vector (with only a non-zero y-axis force)
        desiredForcesOnShankViconFrame = new Vector3(0.0f, -desiredForcesOnShankFrame0.y, 0.0f);

        // The shank force is a kp control based on the knee angle. 
        // For now, assume "straight back" for the subject is the +y-axis of the Vicon frame
        /*  if (avgKneeAngleDeg > 0.0f)
          {
              desiredForcesOnShankViconFrame = new Vector3(0.0f, shankForceKp * avgKneeAngleDeg, 0.0f);
          }

        // Compute the joint torques this desired shank force would generate
        Vector<double> desiredForcesOnShankViconFrameNetNumerics =
            ConvertVector3ToNetNumericsVector(desiredForcesOnShankFrame0);
        Matrix<double> shankForceVelocityJacobianTranspose = stanceModel.GetKneeForceVelocityJacobianTranspose();
        resultantJointTorquesFromDesiredShankForce =
            shankForceVelocityJacobianTranspose * desiredForcesOnShankViconFrameNetNumerics;
    }
}*/

    //add 2 functions to get the position and velocity in Vive frame
    private (Vector3, Vector3) GetCurrentChestDesiredPositionAndVelocityInViconFrame()
    {
        // Get the current desired chest position. 
        // This could be a constant position or a trajectory based on hand position. 
        Vector3 ankleJointCenterPositionViconFrame = centerOfMassManagerScript.GetAnkleJointCenterPositionViconFrame();
        Vector3 trunkBeltStartupAveragePosition = centerOfMassManagerScript.GetCenterOfChestBeltPositionInStartupFramesViconFrame();
        Vector3 desiredChestPosition = new Vector3(ankleJointCenterPositionViconFrame.x, ankleJointCenterPositionViconFrame.y, 
            trunkBeltStartupAveragePosition.z);

        // Get the desired chest velocity. 
        // This could be zero or based on a movement trajectory.
        // This could be zero or based on a movement trajectory. 
        Vector3 desiredChestVelocity = new Vector3(0.0f, 0.0f, 0.0f);

        return (desiredChestPosition, desiredChestVelocity);
    }
    
    
    // Get desired chest position in Unity frame.
    // This could be a constant position or a trajectory based on hand position. 
    private (Vector3, Vector3) GetCurrentChestDesiredPositionAndVelocityInFrame0()
    {
        // Get the current ankle joint center position
        (Vector3 ankleJointCenterPositionUnityFrame,_,_ )= viveTrackerDataManagerScript.GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();
        
        // Get distance from ankle to chest in meters
        float lengthAnkleToChestInMeters = viveTrackerDataManagerScript.GetLengthAnkleToChestInMeters();
        
        // Get the ankle center position in frame 0
        Matrix4x4 transformationFrame0ToUnityFrame = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
        Matrix4x4 transformationUnityFrameToFrame0 = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(transformationFrame0ToUnityFrame, ref transformationUnityFrameToFrame0);
        Vector3 ankleJointCenterPositionFrame0 = transformationUnityFrameToFrame0.MultiplyPoint3x4(ankleJointCenterPositionUnityFrame);
        
        // Compute desired chest position as ankle position plus length ankle-to-chest in vertical direction (in frame 0)
        Vector3 desiredChestPositionFrame0 = new Vector3(ankleJointCenterPositionFrame0.x + lengthAnkleToChestInMeters, ankleJointCenterPositionFrame0.y, 
            ankleJointCenterPositionFrame0.z);

        // Get the desired chest velocity. 
        // This could be zero or based on a movement trajectory.
        // This could be zero or based on a movement trajectory. 
        Vector3 desiredChestVelocityFrame0 = new Vector3(0.0f, 0.0f, 0.0f);

        return (desiredChestPositionFrame0, desiredChestVelocityFrame0);
    }
    private (Vector3, Vector3) GetCurrentPelvisDesiredPositionAndVelocityInViveFrame()
    {
        // Get the current desired  position. 
        // This could be a constant position or a trajectory based on hand position. 
        (Vector3 ankleJointCenterPositionUnityFrame,_,_ )= 
            viveTrackerDataManagerScript.GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();
        Matrix4x4 transformationFrame0ToUnityFrame = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
        Matrix4x4 transformationUnityFrameToFrame0 = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(transformationFrame0ToUnityFrame, ref transformationUnityFrameToFrame0);
        Vector3 pelvisBeltStartupAveragePositionInUnityFrame = viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();
        Vector3 ankleJointCenterPositionViveFrame = 
            transformationUnityFrameToFrame0.MultiplyPoint3x4(ankleJointCenterPositionUnityFrame);
        Vector3 pelvisBeltStartupAveragePosition = 
            transformationUnityFrameToFrame0.MultiplyPoint3x4(pelvisBeltStartupAveragePositionInUnityFrame);
        Vector3 desiredPelvisPosition = new Vector3(ankleJointCenterPositionViveFrame.x, ankleJointCenterPositionViveFrame.y,
            pelvisBeltStartupAveragePosition.z);

        // Get the desired chest velocity. 
        // This could be zero or based on a movement trajectory.
        // This could be zero or based on a movement trajectory. 
        Vector3 desiredPelvisVelocity = new Vector3(0.0f, 0.0f, 0.0f);

        return (desiredPelvisPosition, desiredPelvisVelocity);
    }
    private (Vector3, Vector3) GetCurrentPelvisDesiredPositionAndVelocityInViconFrame()
    {
        // Get the current desired  position. 
        // This could be a constant position or a trajectory based on hand position. 
        Vector3 ankleJointCenterPositionViconFrame = centerOfMassManagerScript.GetAnkleJointCenterPositionViconFrame();
        Vector3 pelvisBeltStartupAveragePosition = centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();
        Vector3 desiredPelvisPosition = new Vector3(ankleJointCenterPositionViconFrame.x, ankleJointCenterPositionViconFrame.y,
            pelvisBeltStartupAveragePosition.z);

        // Get the desired chest velocity. 
        // This could be zero or based on a movement trajectory.
        // This could be zero or based on a movement trajectory. 
        Vector3 desiredPelvisVelocity = new Vector3(0.0f, 0.0f, 0.0f);

        return (desiredPelvisPosition, desiredPelvisVelocity);
    }


    private Vector3 ComputeWholeWorkspaceAssistForceFieldForces(Vector3 trunkControlPointPosition)
    {
        // Compute the distance from player control point (often COM) to center of base of support
        // NOTE: both the trunk control point and center of base of support values are in mm, so we convert to meters.
        float distanceTrunkControlPointToCenterofBaseOfSupportInMeters =
            ((trunkControlPointPosition - centerOfBaseOfSupportViconFrame).magnitude / convertMillimetersToMeters);

        // If using, compute and add on the forces applied by the whole workspace FF.
        Vector3 wholeWorkspaceAssistForceVector = new Vector3(0.0f, 0.0f, 0.0f);
        if (usingWholeWorkspaceAssistForceFieldFlag == true)
        {
            // Compute the whole-workspace-assist fore magnitude
            float wholeWorkspaceAssistSpringForceMagnitude = wholeWorkspaceAssistPercentScaler * springConstantTrunkBelt *
            distanceTrunkControlPointToCenterofBaseOfSupportInMeters;

            // Compute the whole-workspace-assist force vector (add direction to magnitude)
            // Direction is simply the unit vector from our current position to the center of base of support
            wholeWorkspaceAssistForceVector = wholeWorkspaceAssistSpringForceMagnitude *
                (centerOfBaseOfSupportViconFrame - trunkControlPointPosition).normalized;

        }


        // The z-component of force is not desired at all, so set it equal to zero.
        wholeWorkspaceAssistForceVector.z = 0.0f;

        string debugString = "High-level controller: whole-workspace assist forces are: (" +
                wholeWorkspaceAssistForceVector.x + ", " +
                wholeWorkspaceAssistForceVector.y + ", " +
                wholeWorkspaceAssistForceVector.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(debugString);

        // Return the whole-workspace-assist forces
        return wholeWorkspaceAssistForceVector;
    }


    private bool IsTestPointInPolygon(Vector3 testPoint, List<Vector3> polygonPoints)
    {
        // Initialize the boolean of whether or not the testpoint is in the
        // polygon as false
        bool testPointInPolygon = false;

        // Initialize the second vertex (in the vertex pair) index as the last vertex index
        int secondVertexIndex = polygonPoints.Count - 1;

        //For each pairing of adjacent vertices = for each side of the polygon
        for (int firstVertexIndex = 0; firstVertexIndex < polygonPoints.Count; firstVertexIndex++)
        {
            // Get first and second vertex positions for clarity
            Vector3 firstVertexPosition = polygonPoints[firstVertexIndex];
            Vector3 secondVertexPosition = polygonPoints[secondVertexIndex];

            // If the test point has y-axis position between the two vertices
            if ((testPoint.y < firstVertexPosition.y && testPoint.y >= secondVertexPosition.y) ||
               (testPoint.y < secondVertexPosition.y && testPoint.y >= firstVertexPosition.y))
            {
                // If the line connecting the vertices is to the left of the
                // test point
                if (firstVertexPosition.x + ((secondVertexPosition.x - firstVertexPosition.x) /
                    (secondVertexPosition.y - firstVertexPosition.y)) * (testPoint.y - firstVertexPosition.y) < testPoint.x)
                {
                    // Then flip the boolean indicating whether or not the point
                    // is inside the polygon
                    testPointInPolygon = !testPointInPolygon;
                }
            }

            // Reset the second vertex index to the be the first index. That
            // way, we're only comparing neighbors.
            secondVertexIndex = firstVertexIndex;
        }

        return testPointInPolygon;
    }


    private (float, Vector3) GetDistanceFromPointToPolygonInXyPlane(Vector3 testPoint, List<Vector3> polygonPoints)
    {
        // Initialize the return parameters
        float distanceFromPoly = 0.0f;
        float projectionPointOnPolyX = 0.0f;
        float projectionPointOnPolyY = 0.0f;

        // Extract the relevant components of the polygon (x,y coordinates)
        List<float> verticesX = new List<float>();
        List<float> verticesY = new List<float>();
        for (int vertexIndex = 0; vertexIndex < polygonPoints.Count; vertexIndex++)
        {
            verticesX.Add(polygonPoints[vertexIndex].x);
            verticesY.Add(polygonPoints[vertexIndex].y);
        }


        // If(xv, yv) is not closed, close it by making the first vertex also the last in our list
        int numberOfVertices = verticesX.Count;
        if ((verticesX[0] != verticesX[numberOfVertices - 1]) || (verticesY[0] != verticesY[numberOfVertices - 1])){
            verticesX.Add(verticesX[0]);
            verticesY.Add(verticesY[0]);
            numberOfVertices = numberOfVertices + 1;
        }

        // Compute the linear parameters of segments that connect the vertices
        // Ax + By + C = 0
        float[] parameterA = new float[numberOfVertices - 1];
        float[] parameterB = new float[numberOfVertices - 1];
        float[] parameterC = new float[numberOfVertices - 1];
        for (int vertexIndex = 0; vertexIndex < numberOfVertices - 1; vertexIndex++)
        {
            parameterA[vertexIndex] = -(verticesY[vertexIndex + 1] - verticesY[vertexIndex]);
            parameterB[vertexIndex] = verticesX[vertexIndex + 1] - verticesX[vertexIndex];
            parameterC[vertexIndex] = verticesY[vertexIndex + 1] * verticesX[vertexIndex] -
                    verticesX[vertexIndex + 1] * verticesY[vertexIndex];
        }


        // find the projection of point(x, y) on each rib
        // This is not written in clear way but essentially uses the standard formula for projecting a point onto a line.
        float[] projectionX = new float[numberOfVertices - 1];
        float[] projectionY = new float[numberOfVertices - 1];
        for (int vertexIndex = 0; vertexIndex < numberOfVertices - 1; vertexIndex++)
        {
            float AB = 1.0f / (Mathf.Pow(parameterA[vertexIndex], 2.0f) + Mathf.Pow(parameterB[vertexIndex], 2.0f));
            float vv = (parameterA[vertexIndex] * testPoint.x + parameterB[vertexIndex] * testPoint.y + parameterC[vertexIndex]);
            projectionX[vertexIndex] = testPoint.x - (parameterA[vertexIndex] * AB) * vv;
            projectionY[vertexIndex] = testPoint.y - (parameterB[vertexIndex] * AB) * vv;
        }


        // Test for the case where a polygon rib is
        // either horizontal or vertical. In this case, one of the projection
        // point coordinates computed in the last step was wrong (x-coord wrong for horizontal line, y-coord wrong for vertical line)
        // From Eric Schmitz
        for (int vertexIndex = 0; vertexIndex < numberOfVertices - 1; vertexIndex++)
        {
            // If the line is horizontal
            if (verticesX[vertexIndex + 1] - verticesX[vertexIndex] == 0)
            {
                // Correct the projection point x-axis position for this side
                projectionX[vertexIndex] = verticesX[vertexIndex];
            }

            // If the line is vertical
            if (verticesY[vertexIndex + 1] - verticesY[vertexIndex] == 0)
            {
                // Correct the projection point y-axis position for this side
                projectionY[vertexIndex] = verticesY[vertexIndex];
            }
        }

        // find all cases where the projected point is "inside the segment",
        // i.e.its projection onto the line defining the polygon side is actually on
        // the polygon side, not on the extension of the line PAST the polygon edges.
        bool[] indicesWhereProjectionIsOntoPolygon = new bool[numberOfVertices - 1];
        bool anyProjectionIsOntoPolygon = false;
        for (int vertexIndex = 0; vertexIndex < numberOfVertices - 1; vertexIndex++)
        {
            bool projectionWithinSideXBoundsFlag = (((projectionX[vertexIndex] >= verticesX[vertexIndex]) && (projectionX[vertexIndex] <= verticesX[vertexIndex + 1])) | ((projectionX[vertexIndex] >= verticesX[vertexIndex + 1]) && (projectionX[vertexIndex] <= verticesX[vertexIndex])));
            bool projectionWithinSideYBoundsFlag = (((projectionY[vertexIndex] >= verticesY[vertexIndex]) && (projectionY[vertexIndex] <= verticesY[vertexIndex + 1])) | ((projectionY[vertexIndex] >= verticesY[vertexIndex + 1]) && (projectionY[vertexIndex] <= verticesY[vertexIndex])));
            indicesWhereProjectionIsOntoPolygon[vertexIndex] = (projectionWithinSideXBoundsFlag && projectionWithinSideYBoundsFlag);

            // If any of the projections of the test point onto the line parallel to the polygon side 
            // are on the polygon side itself
            if ((projectionWithinSideXBoundsFlag && projectionWithinSideYBoundsFlag) == true)
            {
                // Then flip a flag noting this. 
                anyProjectionIsOntoPolygon = true;
            }
        }


        // Compute distances from test point (x, y) to the vertices
        float[] distancesTestPointToVertices = new float[numberOfVertices - 1];
        float minimumDistanceToVertex = Mathf.Infinity;
        int minimumDistanceToVertexIndex = -1;
        for (int vertexIndex = 0; vertexIndex < numberOfVertices - 1; vertexIndex++)
        {
            distancesTestPointToVertices[vertexIndex] = Mathf.Sqrt(Mathf.Pow((verticesX[vertexIndex] - testPoint.x), 2.0f) + Mathf.Pow((verticesY[vertexIndex] - testPoint.y), 2.0f));
            if (distancesTestPointToVertices[vertexIndex] < minimumDistanceToVertex)
            {
                minimumDistanceToVertex = distancesTestPointToVertices[vertexIndex];
                minimumDistanceToVertexIndex = vertexIndex;
            }
        }

        // Finally, find the nearest point on the polygon to the test point 

        if (!anyProjectionIsOntoPolygon) // if all projections onto the lines of the polygon are outside of the polygon ribs
        {
            projectionPointOnPolyX = verticesX[minimumDistanceToVertexIndex];
            projectionPointOnPolyY = verticesY[minimumDistanceToVertexIndex];
            distanceFromPoly = minimumDistanceToVertex;
        }
        else // If the point projects onto one or more sides of the polygon
        {
            //For each side of the polygon
            float minimumDistancePointToPolygonSideProjection = Mathf.Infinity;
            int minimumDistancePointToSideVertexIndex = -1;
            for (int vertexIndex = 0; vertexIndex < numberOfVertices - 1; vertexIndex++)
            {
                if (indicesWhereProjectionIsOntoPolygon[vertexIndex] == true)
                {
                    // distance from point (x, y) to the projection on ribs
                    float distancePointToSideProjection = Mathf.Sqrt(Mathf.Pow((projectionX[vertexIndex] - testPoint.x), 2.0f) +
                        Mathf.Pow((projectionY[vertexIndex] - testPoint.y), 2.0f));
                    if (distancePointToSideProjection < minimumDistancePointToPolygonSideProjection)
                    {
                        minimumDistancePointToPolygonSideProjection = distancePointToSideProjection;
                        minimumDistancePointToSideVertexIndex = vertexIndex;
                    }
                }
            }

            // If the distance from the test point to the closest of its
            // projections onto the polygon sides is less than the distance from
            // the test point to any vertex
            if (minimumDistancePointToPolygonSideProjection < minimumDistanceToVertex)
            {
                // Then this point is the nearest point on the polygon
                projectionPointOnPolyX = projectionX[minimumDistancePointToSideVertexIndex];
                projectionPointOnPolyY = projectionY[minimumDistancePointToSideVertexIndex];
                distanceFromPoly = minimumDistancePointToPolygonSideProjection;
            }
            else // then a vertex is closer than any of the test point's projections onto the sides of the polygon
            {
                projectionPointOnPolyX = verticesX[minimumDistanceToVertexIndex];
                projectionPointOnPolyY = verticesY[minimumDistanceToVertexIndex];
                distanceFromPoly = minimumDistanceToVertex;
            }


        }

        // Return a negative distance to polygon if the point is inside the
        // polgyon
        bool inPolygonFlag = IsTestPointInPolygon(testPoint, polygonPoints);
        if (inPolygonFlag)
        {
            distanceFromPoly = -distanceFromPoly;
        }

        // Package up projection point into a Vector3
        Vector3 projectionPointOntoPoly = new Vector3(projectionPointOnPolyX, projectionPointOnPolyY, polygonPoints[0].z);

        return (distanceFromPoly, projectionPointOntoPoly);
    }



    private (Vector3, Vector3) CalculateTrunkForceFieldForcesBasedOnControlPointStateAndForceFieldMode(
                Vector3 trunkControlPointPosition, Vector3 trunkControlPointVelocity, 
                float trunkDistancePastForceBoundary, float smoothSpringForceFromDebounceBoundaryScaler)
    {
        // Set desired trunk forces and torques equal to 0 for this frame
        Vector3 desiredForcesOnTrunkInViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 desiredTorquesOnTrunkInViconFrame = new Vector3(0.0f, 0.0f, 0.0f);

        // Cases based on trunk control mode
        // NOTE: the trunk control point can be either the COM or the trunk belt center!
        // If we're using the force field at the (COM or trunk center point) excursion limits, computing forces only from position
        if (currentTrunkForceFieldMode == forceFieldAtExcursionBoundaryPositionBasedForcesOnlyString)
        {
            // Compute the spring force magnitude
            // NOTE: both the trunk control point and center of base of support values are in mm, so we convert to meters.
            float distanceTrunkControlPointToCenterofBaseOfSupportInMeters = 
                ((trunkControlPointPosition - centerOfBaseOfSupportViconFrame).magnitude / convertMillimetersToMeters);
            float springForceMagnitude = springConstantTrunkBelt *
                distanceTrunkControlPointToCenterofBaseOfSupportInMeters;

            // If debugging, print a summary of our calculations
            string debugString = "Trunk FF spring constant is: " + springConstantTrunkBelt + " and distance trunk to center is: " +
                distanceTrunkControlPointToCenterofBaseOfSupportInMeters + " and smooth spring scaler is :" +
                smoothSpringForceFromDebounceBoundaryScaler + " and spring force mag. is: " + springForceMagnitude;
            printLogMessageToConsoleIfDebugModeIsDefined(debugString);

            // Modify the spring force depending on fraction of the way from debounce bounds to full support bounds, if desired
            springForceMagnitude = smoothSpringForceFromDebounceBoundaryScaler * springForceMagnitude;

            // Now, the force is simply the unit vector from our current position to the center of base of support times the spring force magnitude
            desiredForcesOnTrunkInViconFrame = springForceMagnitude * (centerOfBaseOfSupportViconFrame - trunkControlPointPosition).normalized;

            // The z-component of force is not desired at all, so set it equal to zero.
            desiredForcesOnTrunkInViconFrame.z = 0.0f;
        }
        else if(currentTrunkForceFieldMode == forceFieldAtExcursionBoundaryPositionAndVelocityBasedForcesString)
        // If we're using the force field at the (COM or trunk center point) excursion limits, computing forces from both position and velocity
        {
            // Compute the spring constant for this subject, based on their mass and height to point of force application (trunk center z-pos)
            //float subjectMass = 

        }

        // Return forces and torques
        return (desiredForcesOnTrunkInViconFrame, desiredTorquesOnTrunkInViconFrame);
    }




    // END: High-level force field controller functions*********************





    // START: Functions to send desired forces to tension planner, send tensions to robot*********************

    // This function is called by the COM manager when new marker data is ready, 
    // after the structure matrix and desired forces have been computed. 
    // It should compute both trunk and pelvis desired cable tensions.
    public void ComputeDesiredCableTensionsThisFrameAndSendToRobot()
    {



    }


    private void ComputeChestCableTensionsThisFrame()
    {
        // CHEST BELT************************************************************************************************
        float[] computedTrunkCableTensionsThisFrame = new float[] { 0.00f, 0.0f, 0.0f, 0.0f };
        if (usingChestBelt)
        {
            // Get the most recent structure matrix (forces and torque rows) for the chest belt
            (Vector3[] columnsOfTrunkForceStructureMatrix, Vector3[] columnsOfTrunkTorqueStructureMatrix) =
            buildStructureMatrixScript.GetMostRecentTrunkStructureMatrixForceAndTorqueRows();

            // Controlled rows of the structure matrix for the trunk
            // Ground plane is y,z in frame 0
            int[] indicesOfStructureMatrixControlledRowsTrunk = new int[0]; // Control Fy, Fz 
            if (chestForceFieldSettings.chestForceDirection == ChestForceTypeEnum.ground_plane)
            {
                indicesOfStructureMatrixControlledRowsTrunk = new int[] { 2, 3 }; // Control Fy, Fz
            }
            else if(chestForceFieldSettings.chestForceDirection == ChestForceTypeEnum.perpendicular_to_trunk)
            {
                indicesOfStructureMatrixControlledRowsTrunk = new int[] { 1, 2, 3 }; // Control Fx, Fy, Fz           
            }


            // Set minimum cable tensions 

            // Set best guess for cable tensions. This is the previous solution.
            float[] bestGuessInitialCableTensionsTrunk = lastComputedCableTensionsTrunk;

            // Compute trunk desired cable tensions
            // Send the structure matrix, desired forces and torques, cable tension minimum, 
            // best guess of tensions (often the previous solution), and rows to use equality constraints on (controlled rows)
            // to the cable tension planner. 
            // Retrieve the needed cable tensions this frame.
/* if vicon
 * if vive
 *
 *
 * 
 */
            if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
            {
                computedTrunkCableTensionsThisFrame =
                    cableTensionPlannerScript.ComputeCableTensionsForDesiredTrunkForces(desiredForcesOnTrunkFrame0,
                        desiredTorquesOnTrunkViconFrame, columnsOfTrunkForceStructureMatrix,
                        columnsOfTrunkTorqueStructureMatrix, indicesOfStructureMatrixControlledRowsTrunk,
                        minimumCableTension, bestGuessInitialCableTensionsTrunk);
            }
            else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
            {
                computedTrunkCableTensionsThisFrame =
                    cableTensionPlannerScript.ComputeCableTensionsForDesiredTrunkForcesVive(desiredForcesOnTrunkFrame0,
                        desiredTorquesOnTrunkViveFrame, columnsOfTrunkForceStructureMatrix,
                        columnsOfTrunkTorqueStructureMatrix, indicesOfStructureMatrixControlledRowsTrunk,
                        minimumCableTension, bestGuessInitialCableTensionsTrunk);
            }

            // VALIDATION ********
            // THEN COMPUTE THE FORCES AND TORQUES BY MULTIPLYING THE COMPUTED CABLE TENSIONS BY THE S MATRIX!
            // WE HAVE VALIDATED IT FOR THE TRUNK BELT - IT WORKS!!!
            // Convert structure matrix to array
            float[,] structureMatrixAsArrayTrunk = ConvertStructureMatrixToArray(columnsOfTrunkForceStructureMatrix, columnsOfTrunkTorqueStructureMatrix);
            // Multiply structure matrix array by the cable tensions to get F/T applied to the trunk
            float[] forcesTorquesOnTrunkFrame0 = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayTrunk, lastComputedCableTensionsTrunk);

            // DEBUG: print computed F/T acting on trunk
            string debugString = "Computed forces/torques acting on the trunk this frame (Fx, Fy, Fz, Tx, Ty, Tz): " +
                forcesTorquesOnTrunkFrame0[0] + ", " +
                forcesTorquesOnTrunkFrame0[1] + ", " +
                forcesTorquesOnTrunkFrame0[2] + ", " +
                forcesTorquesOnTrunkFrame0[3] + ", " +
                forcesTorquesOnTrunkFrame0[4] + ", " +
                forcesTorquesOnTrunkFrame0[5] + ")";
            printLogMessageToConsoleIfDebugModeIsDefined(debugString);

            // SAFETY FILTERING ******************************************************************************************************************************
            // Filter the tensions for safety (for now, ensuring they are all positive), apply cable tension limits, 
            // and apply a cable tension rate change limiter if enabled.
            float[] safeTrunkCableTensionsThisFrame = 
                ApplySafetyFilterAndTensionLimitsToTensionPlannerTensions(computedTrunkCableTensionsThisFrame,
                    lastComputedCableTensionsTrunk, trunkCableTensionRateLimiterEnableEnum);
            forcesTorquesOnTrunkFrame0 = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayTrunk, lastComputedCableTensionsTrunk);


            // Compute the "actual" joint torques generated by the SAFE chest cable tensions
            // Get just the chest forces (not torques)
            Vector3 forcesOnTrunkDueToCablesFrame0 = new Vector3(forcesTorquesOnTrunkFrame0[0], forcesTorquesOnTrunkFrame0[1], forcesTorquesOnTrunkFrame0[2]);
            // Get the chest force Jacobian (in frame 0)
            Matrix<double> chestVelocityJacobian = stanceModel.GetChestForceVelocityJacobianTranspose();

            // Multiply the frame 0 force by the Jacobian to get the resultant joint torques
            resultantJointTorquesFromTensionSolvedChestForce = chestVelocityJacobian * ConvertVector3ToNetNumericsVector(forcesOnTrunkDueToCablesFrame0);

            // STORE COMPUTED TENSIONS ******************************************************************************************************************************
            // Store the safe tensions for the trunk as the most recent tension solutions
            lastComputedCableTensionsTrunk = safeTrunkCableTensionsThisFrame;
        }
    }


    private void ComputePelvicCableTensionsThisFrame()
    {
        // PELVIC BELT************************************************************************************************
        computedPelvisCableTensionsThisFrame = new float[] { 0.00f, 0.0f, 0.0f, 0.0f }; // reset to zero
        safePelvisCableTensionsThisFrame = new float[] { 0.00f, 0.0f, 0.0f, 0.0f }; // reset to zero

        if (usingPelvicBelt)
        {
            // Get the most recent structure matrix (forces and torque rows) for the pelvic belt
            (Vector3[] columnsOfPelvicForceStructureMatrix, Vector3[] columnsOfPelvicTorqueStructureMatrix) =
                buildStructureMatrixScript.GetMostRecentPelvicStructureMatrixForceAndTorqueRows();
            PrintDebugIfDebugModeFlagIsTrue("the force columnsOfPelvicForceStructureMatrix is " + columnsOfPelvicForceStructureMatrix);
            PrintDebugIfDebugModeFlagIsTrue("the force columnsOfPelvicTorqueStructureMatrix is " + columnsOfPelvicForceStructureMatrix);
            // Controlled rows of the structure matrix for the pelvis
            int[] indicesOfStructureMatrixControlledRowsPelvis = new int[] { 1, 2, 3 , 4, 5, 6}; // Control Fx, Fy, Fz

            // Set best guess for cable tensions for the pelvis. This is the previous solution.
            float[] bestGuessInitialCableTensionsPelvis = lastComputedCableTensionsPelvis;

            // Compute pelvis desired cable tensions
            // Send the structure matrix, desired forces and torques, cable tension minimum, 
            // best guess of tensions (often the previous solution), and rows to use equality constraints on (controlled rows)
            // to the cable tension planner. 
            // Retrieve the needed cable tensions this frame.
            if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
            {
                PrintDebugIfDebugModeFlagIsTrue("test the Vicon cable force");
                computedPelvisCableTensionsThisFrame =
                    cableTensionPlannerScript.ComputeCableTensionsForDesiredPelvisForces(
                        desiredForcesOnPelvisViconFrame,
                        desiredTorquesOnPelvisViconFrame, columnsOfPelvicForceStructureMatrix,
                        columnsOfPelvicTorqueStructureMatrix, indicesOfStructureMatrixControlledRowsPelvis,
                        minimumCableTension, bestGuessInitialCableTensionsPelvis);
            } 
            else if (kinematicModelStanceScript.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
            {
                
                
                PrintDebugIfDebugModeFlagIsTrue("test the Vive cable force");
                PrintDebugIfDebugModeFlagIsTrue(" the desiredForcesOnPelvisViveFrame is " + desiredForcesOnPelvisViveFrame);
                PrintDebugIfDebugModeFlagIsTrue(" the desiredTorquesOnPelvisViveFrame is " + desiredTorquesOnPelvisViveFrame);
                PrintDebugIfDebugModeFlagIsTrue(" the columnsOfPelvicForceStructureMatrix is " + columnsOfPelvicForceStructureMatrix.Length);
                PrintDebugIfDebugModeFlagIsTrue(" the columnsOfPelvicTorqueStructureMatrix is " + columnsOfPelvicTorqueStructureMatrix.Length);
                PrintDebugIfDebugModeFlagIsTrue(" the indicesOfStructureMatrixControlledRowsPelvis is " + indicesOfStructureMatrixControlledRowsPelvis.Length);
            
                PrintDebugIfDebugModeFlagIsTrue(" the minimumCableTension is " + minimumCableTension);
                PrintDebugIfDebugModeFlagIsTrue(" the bestGuessInitialCableTensionsPelvis is " + bestGuessInitialCableTensionsPelvis.Length);
                // Compute cable tensions corresponding to the desired pelvic forces. 
                // Note that the pelvic forces may have to be modified to meet constraints set by the pulley arrangement.
                (computedPelvisCableTensionsThisFrame, possiblyModifiedForcesOnPelvisFrame0) =
                cableTensionPlannerScript.ComputeCableTensionsForDesiredPelvisForcesInFrame0(
                    desiredForcesOnPelvisFrame0,
                    desiredTorquesOnPelvisFrame0, columnsOfPelvicForceStructureMatrix,
                    columnsOfPelvicTorqueStructureMatrix, indicesOfStructureMatrixControlledRowsPelvis,
                    minimumCableTension, bestGuessInitialCableTensionsPelvis);
            }

            PrintDebugIfDebugModeFlagIsTrue("Computed pelvic tensions: (ordered 1,2,3,4)" + computedPelvisCableTensionsThisFrame[0] + ", " +
                                            computedPelvisCableTensionsThisFrame[1] + ", " + computedPelvisCableTensionsThisFrame[2] + ", " +
                                            computedPelvisCableTensionsThisFrame[3] + ")");


            // VALIDATION ********
            // THEN COMPUTE THE FORCES AND TORQUES BY MULTIPLYING THE COMPUTED CABLE TENSIONS BY THE S MATRIX!
            // WE HAVE VALIDATED IT FOR THE TRUNK BELT - IT WORKS!!!
            // Convert structure matrix to array
            // Validate pelvic cable tensions
            float[,] structureMatrixAsArrayPelvis = ConvertStructureMatrixToArray(columnsOfPelvicForceStructureMatrix, columnsOfPelvicTorqueStructureMatrix);
            // Multiply structure matrix array by the cable tensions to get F/T applied to the trunk
            float[] forcesTorquesOnPelvis = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayPelvis, lastComputedCableTensionsPelvis);
            forcesTorquesOnPelvisFromCable = forcesTorquesOnPelvis;
            // DEBUG: print computed F/T acting on trunk
            string debugString = "Computed forces/torques acting on the pelvis this frame (Fx, Fy, Fz, Tx, Ty, Tz): " + forcesTorquesOnPelvis[0] + ", " +
                forcesTorquesOnPelvis[1] + ", " +
                forcesTorquesOnPelvis[2] + ", " +
                forcesTorquesOnPelvis[3] + ", " +
                forcesTorquesOnPelvis[4] + ", " +
                forcesTorquesOnPelvis[5] + ")";
            printLogMessageToConsoleIfDebugModeIsDefined(debugString);

            // SAFETY FILTERING ******************************************************************************************************************************
            // Filter the tensions for safety (for now, ensuring they are all positive) and for cable tension limits
            safePelvisCableTensionsThisFrame = ApplySafetyFilterAndTensionLimitsToTensionPlannerTensions(
                computedPelvisCableTensionsThisFrame, lastComputedCableTensionsPelvis, pelvicCableTensionRateLimiterEnableEnum);

            // CABLE TENSION RATE CHANGE LIMITER *************************************************************************************************************


            // Multiply structure matrix array by the safe cable tensions to get F/T applied to the trunk
            forcesTorquesOnPelvis = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayPelvis, safePelvisCableTensionsThisFrame);
            // store the first 3 elements of forcesTorquesOnPelvis, then get pulley position in s matrix script

            // STORE COMPUTED TENSIONS ******************************************************************************************************************************
            lastComputedCableTensionsPelvis = safePelvisCableTensionsThisFrame;

            // Compute the joint torques the "possibly modified" pelvic forces would create. 
            // Possible modification is to satisfy constraints on the pelvic force due to attachment/pulley arrangement.
            Matrix<double> pelvisVelocityJacobianTransposeInFrame0 = stanceModel.GetPelvisForceVelocityJacobianTranspose(); //get pelvis velocity jacobian transpose
            resultantJointTorquesFromPossiblyModifiedPelvicForce =
                pelvisVelocityJacobianTransposeInFrame0 * ConvertVector3ToNetNumericsVector(possiblyModifiedForcesOnPelvisFrame0);

            // Compute the joint torques the SAFE pelvic cable tensions would create
            Vector3 forcesOnPelvisDueToCablesFrame0 = new Vector3(forcesTorquesOnPelvis[0], forcesTorquesOnPelvis[1], forcesTorquesOnPelvis[2]);
            resultantJointTorquesFromTensionSolvedPelvicForce = pelvisVelocityJacobianTransposeInFrame0 * ConvertVector3ToNetNumericsVector(forcesOnPelvisDueToCablesFrame0);

            // DEBUGGING ONLY: print the difference in joint torques between the ideal, desired pelvic forces
            // and the achievable, cable-actuated pelvic force
            Vector<double> desiredMinusActualJointTorquesFromPelvicForce = resultantJointTorquesFromDesiredPelvicForce - 
                resultantJointTorquesFromTensionSolvedPelvicForce;
            PrintDebugIfDebugModeFlagIsTrue("Desired minus actual joint torques from pelvic force (joints 1,2,3): " + desiredMinusActualJointTorquesFromPelvicForce[0] +
                                            ", " + desiredMinusActualJointTorquesFromPelvicForce[1] + ", " + desiredMinusActualJointTorquesFromPelvicForce[2] + ")");
        }

    }



    private void ComputeShankCableTensionsThisFrame()
    {
        // SHANK BELTS******************************************************************************************************************************************
        float computedLeftShankCableTensionThisFrame = 0.0f;
        float computedRightShankCableTensionThisFrame = 0.0f;
        
        computedLeftShankCableTensionsThisFrame = new float[] { 0.0f};
        computedRightShankCableTensionsThisFrame = new float[] {0.0f};
      
        forcesTorquesOnLeftShank = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }; // reset
        forcesTorquesOnRightShank = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }; // reset

        float[] computedForcesTorquesOnLeftShank = new float[] { 0.0f, 0.0f, 0.0f };
        float[] computedForcesTorquesOnRightShank = new float[] { 0.0f, 0.0f, 0.0f};
        //float[] computedshankForceDueToBothShankCables = new float[] { 0.0f, 0.0f, 0.0f };
        
        if (usingShankBelts)
        {
            // Get the most recent structure matrix (forces and torque rows) for the pelvic belt
            (Vector3[] columnsOfShankForceStructureMatrixLeftLeg, Vector3[] columnsOfShankTorqueStructureMatrixLeftLeg,
             Vector3[] columnsOfShankForceStructureMatrixRightLeg, Vector3[] columnsOfShankTorqueStructureMatrixRightLeg) =
                buildStructureMatrixScript.GetMostRecentShankLeftRightStructureMatricesForceAndTorqueRows();

            // Debugging only: print the shank force structure matrix for left and right leg
            PrintDebugIfDebugModeFlagIsTrue("Structure matrix for left shank has " + columnsOfShankForceStructureMatrixLeftLeg.Length + "columns. " +
                                            "Column one has elements: " + columnsOfShankForceStructureMatrixLeftLeg[0].x + ", " + columnsOfShankForceStructureMatrixLeftLeg[0].y +
                                            ", " + columnsOfShankForceStructureMatrixLeftLeg[0].z + ")");
            PrintDebugIfDebugModeFlagIsTrue("Structure matrix for right shank has " + columnsOfShankForceStructureMatrixRightLeg.Length + "columns. " +
                                            "Column one has elements: " + columnsOfShankForceStructureMatrixRightLeg[0].x + ", " + columnsOfShankForceStructureMatrixRightLeg[0].y +
                                            ", " + columnsOfShankForceStructureMatrixRightLeg[0].z + ")");

            // Controlled rows of the structure matrix for the pelvis
            int[] indicesOfStructureMatrixControlledRowsShanks = new int[] { 2 }; // Control Fy (AP force) only. Fx = 1, Fy = 2, Fz = 3

            // Best guess for shank cable force required = the desired Fy force
            float bestGuessInitialCableTensionShankLeft = desiredForcesOnShankFrame0[1] / 2.0f;
            float bestGuessInitialCableTensionShankRight = bestGuessInitialCableTensionShankLeft;

            // Solve for the desired shank forces using the "tension solver." 
            // For the shank force, this is a single algebraic equation.
            (computedLeftShankCableTensionThisFrame, computedRightShankCableTensionThisFrame) =
                cableTensionPlannerScript.ComputeCableTensionsForDesiredShankForces(desiredForcesOnShankFrame0,
                desiredTorquesOnShankFrame0, columnsOfShankForceStructureMatrixLeftLeg,
                columnsOfShankTorqueStructureMatrixLeftLeg, columnsOfShankForceStructureMatrixRightLeg,
                columnsOfShankTorqueStructureMatrixRightLeg, indicesOfStructureMatrixControlledRowsShanks,
                minimumCableTension, bestGuessInitialCableTensionShankLeft, bestGuessInitialCableTensionShankRight);

            PrintDebugIfDebugModeFlagIsTrue("Before safety filtering, computed shank cable tensions of (L, R): (" + computedLeftShankCableTensionThisFrame +
                                            ", " + computedRightShankCableTensionThisFrame + ") N.");

            // SAFETY FILTERING ******************************************************************************************************************************
            // Filter the tensions for safety (for now, ensuring they are all positive) and for cable tension limits
            safeLeftShankCableTensionsThisFrame = ApplySafetyFilterAndTensionLimitsToTensionPlannerTensions(new float[] 
            { computedLeftShankCableTensionThisFrame }, new float[] { lastComputedCableTensionLeftShank }, leftShankCableTensionRateLimiterEnableEnum);
            safeRightShankCableTensionsThisFrame = ApplySafetyFilterAndTensionLimitsToTensionPlannerTensions(new float[] 
            { computedRightShankCableTensionThisFrame }, new float[] { lastComputedCableTensionRightShank }, rightShankCableTensionRateLimiterEnableEnum);

            computedLeftShankCableTensionsThisFrame[0] = computedLeftShankCableTensionThisFrame;
            computedRightShankCableTensionsThisFrame[0] = computedRightShankCableTensionThisFrame;
            
            // COMPUTE JOINT TORQUES GENERATED FROM THE SAFE SHANK TENSIONS
            // , by taking structure matrix times the shank force
            float[,] structureMatrixAsArrayLeftShank = ConvertStructureMatrixToArray(columnsOfShankForceStructureMatrixLeftLeg,
                columnsOfShankTorqueStructureMatrixLeftLeg);
            float[,] structureMatrixAsArrayRightShank = ConvertStructureMatrixToArray(columnsOfShankForceStructureMatrixRightLeg,
                columnsOfShankTorqueStructureMatrixRightLeg);
            
            //computed the force generated by the cable tension
            computedForcesTorquesOnLeftShank = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayLeftShank, computedLeftShankCableTensionsThisFrame);
            computedForcesTorquesOnRightShank = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayRightShank, computedRightShankCableTensionsThisFrame);
            Vector3 computedshankForceDueToBothShankCablesFrame0 = new Vector3(computedForcesTorquesOnLeftShank[0] + computedForcesTorquesOnRightShank[0],
                computedForcesTorquesOnLeftShank[1] + computedForcesTorquesOnRightShank[1],
                computedForcesTorquesOnLeftShank[2] + computedForcesTorquesOnRightShank[2]);
            
            // Compute the force generated by the SAFE shank tensions
            forcesTorquesOnLeftShank = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayLeftShank, safeLeftShankCableTensionsThisFrame);
            forcesTorquesOnRightShank = MultiplyMatrixArrayByVectorArray(structureMatrixAsArrayRightShank, safeRightShankCableTensionsThisFrame);
            Vector3 shankForceDueToBothShankCablesInFrame0 = new Vector3(forcesTorquesOnLeftShank[0] + forcesTorquesOnRightShank[0],
                forcesTorquesOnLeftShank[1] + forcesTorquesOnRightShank[1],
                forcesTorquesOnLeftShank[2] + forcesTorquesOnRightShank[2]);

            
            Matrix<double> shankForceVelocityJacobianTranspose = stanceModel.GetKneeForceVelocityJacobianTranspose();
            computedJointTorquesFromTensionSolvedShankForce = shankForceVelocityJacobianTranspose *
                                                              ConvertVector3ToNetNumericsVector(
                                                                  computedshankForceDueToBothShankCablesFrame0);
            
            // Note that we assume both cables forces are applied to the stance model at one point, hence why we use one Jacobian.
            resultantJointTorquesFromTensionSolvedShankForce = shankForceVelocityJacobianTranspose * ConvertVector3ToNetNumericsVector(shankForceDueToBothShankCablesInFrame0);

            // DEBUGGING ONLY: print the difference in joint torques between the desired, ideal shank force and the actual, cable-actuated shank force
            Vector<double> desiredMinusActualJointTorquesFromShankForce = resultantJointTorquesFromDesiredShankForce -
                resultantJointTorquesFromTensionSolvedShankForce;
            PrintDebugIfDebugModeFlagIsTrue("Safe shank force in frame0 (x,y,z): (" +
                                            shankForceDueToBothShankCablesInFrame0.x + ", " + shankForceDueToBothShankCablesInFrame0.y + ", " +
                                            shankForceDueToBothShankCablesInFrame0.z + "); " + 
                                            "Desired shank force joint torques (joints 1,2): (" +
                                            resultantJointTorquesFromDesiredShankForce[0] + ", " + resultantJointTorquesFromDesiredShankForce[1] +
                                            "); Actual shank force joint torques (joints 1,2): (" +
                                            resultantJointTorquesFromTensionSolvedShankForce[0] + ", " + resultantJointTorquesFromTensionSolvedShankForce[1] +
                                            "); Desired minus actual joint torques from shank force (joints 1,2): (" + desiredMinusActualJointTorquesFromShankForce[0] +
                                            ", " + desiredMinusActualJointTorquesFromShankForce[1] + ")");


            // STORE COMPUTED TENSIONS ******************************************************************************************************************************
            // Store the safe tensions for the trunk as the most recent tension solutions
            lastComputedCableTensionLeftShank = safeLeftShankCableTensionsThisFrame[0]; // Only one left shank cable
            lastComputedCableTensionRightShank = safeRightShankCableTensionsThisFrame[0]; // Only one right shank cable
        }
        else
        // If we're not using the shank cables
        {
            // We'll command 0 tension to those motors
            lastComputedCableTensionLeftShank = 0.0f;
            lastComputedCableTensionRightShank = 0.0f;
        }
    }




    private float[] ApplySafetyFilterAndTensionLimitsToTensionPlannerTensions(float[] cableTensionsFromPlanner, 
        float[] previousCableTensionSolution, CableTensionRateLimiterEnableEnum tensionRateChangeLimiterEnum)
    {

        // Initialize an array for the safe tensions
        float[] safeTensions = new float[cableTensionsFromPlanner.Length];

        // Use minimum tensions flag 
        bool useMinimumTensionsFlag = false;

        // If any tensions are negative, we consider this an error condition and set all 
        // cable tensions to minimum (see below)
        if(cableTensionsFromPlanner.Any(element => element < 0))
        {
            useMinimumTensionsFlag = true;
        }

        // Scale so that the maximum tension value is not exceeded for any cable. 
        // We must scale all elements equally to preserve the force direction.
        if (!useMinimumTensionsFlag) // if we are not defaulting to the minimum tension values
        {
            // Set the safe tensions equal to the tensions from the cable planner
            safeTensions = cableTensionsFromPlanner;

            // Enforce the tension minimum for each cable
            for (int cableIndex = 0; cableIndex < safeTensions.Length; cableIndex++)
            {
                // If the cable tension is less than the minimum
                if(safeTensions[cableIndex] < minimumCableTension)
                {
                    // Enforce the minimum
                    safeTensions[cableIndex] = minimumCableTension;
                }
            }

            // If the cable tension rate limiter is enabled
            if(tensionRateChangeLimiterEnum == CableTensionRateLimiterEnableEnum.Enabled)
            {
                // Get the maximum allowable tension given the max cable tension rate allowed by the limiter
                float[] cableTensionMaxValues = new float[previousCableTensionSolution.Length];
                for (uint index = 0; index < cableTensionsFromPlanner.Length; index++)
                {
                    // Max cable tension is the previous plus the max rate change allowed multiplied by the time step.
                    cableTensionMaxValues[index] = previousCableTensionSolution[index] + 
                        maximumCableTensionRateNewtonsPerSecond *(Time.time - timeOfLastCableTensionComputationUnityFrame);
                }

                // Get the ratio of the current tensions to the maximum tensions allowed by the rate limiter
                // and the maximum ratio
                float maxRatioTensionToRateLimiterMaxAllowedTension = 0.0f;
                for (uint index = 0; index < cableTensionsFromPlanner.Length; index++)
                {
                    float ratioTensionToRateLimiterMaxAllowed = safeTensions[index] / cableTensionMaxValues[index];
                    if(ratioTensionToRateLimiterMaxAllowed  > maxRatioTensionToRateLimiterMaxAllowedTension)
                    {
                        maxRatioTensionToRateLimiterMaxAllowedTension = ratioTensionToRateLimiterMaxAllowed;
                    }
                }
                 
                // If any ratio is greater than 1, it means that tension exceeded the max allowed by the rate limiter. 
                // In this case, we scale ALL cable tensions by 1/ratio to meet rate limiter requirements while 
                // preserving the force and torque direction vectors. 
                if(maxRatioTensionToRateLimiterMaxAllowedTension > 1.0f)
                {
                    // For each cable tension 
                    for (int cableIndex = 0; cableIndex < cableTensionsFromPlanner.Length; cableIndex++)
                    {
                        // Scale the cable tension by the scaler
                        safeTensions[cableIndex] = cableTensionsFromPlanner[cableIndex] / maxRatioTensionToRateLimiterMaxAllowedTension;
                    }
                }
            }

            // Get the highest cable tension value
            float maximumTensionThisFrame = cableTensionsFromPlanner.Max();

            // If the cable tension is greater than the maximum allowable tension limit
            if(maximumTensionThisFrame > maximumCableTension)
            {
                // Compute a scaler to reduce all tensions to meet the maximum tension limit
                float scalerToMeetTensionRequirements = maximumTensionThisFrame / maximumCableTension;

                // For each cable tension 
                for (int cableIndex = 0; cableIndex < cableTensionsFromPlanner.Length; cableIndex++)
                {
                    // Scale the cable tension by the scaler
                    safeTensions[cableIndex] = cableTensionsFromPlanner[cableIndex] / scalerToMeetTensionRequirements;
                }
            }
        }
        else // if we are defaulting to minimum tension values in all cables
        {
            // For each cable tension 
            for (int cableIndex = 0; cableIndex < cableTensionsFromPlanner.Length; cableIndex++)
            {
                // Set the cable tension equal to the minimum value
                safeTensions[cableIndex] = minimumCableTension;
            }
        }

        return safeTensions;
    }

    private float[,] ConvertStructureMatrixToArray(Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix)
    {
        // Initialize structure matrix array
        float[,] structureMatrixAsArray = new float[6, columnsOfForceSMatrix.Length];

        // For each column
        for (uint columnIndex = 0; columnIndex < columnsOfForceSMatrix.Length; columnIndex++)
        {
            // Fill in the column of the array
            structureMatrixAsArray[0, columnIndex] = columnsOfForceSMatrix[columnIndex].x;
            structureMatrixAsArray[1, columnIndex] = columnsOfForceSMatrix[columnIndex].y;
            structureMatrixAsArray[2, columnIndex] = columnsOfForceSMatrix[columnIndex].z;
            structureMatrixAsArray[3, columnIndex] = columnsOfTorqueSMatrix[columnIndex].x;
            structureMatrixAsArray[4, columnIndex] = columnsOfTorqueSMatrix[columnIndex].y;
            structureMatrixAsArray[5, columnIndex] = columnsOfTorqueSMatrix[columnIndex].z;

        }

        // Return the structure matrix array
        return structureMatrixAsArray;

    }


    private float[] MultiplyMatrixArrayByVectorArray(float[,] structureMatrixAsArray, float[] computedTrunkCableTensionsThisFrame)
    {
        // Get number of columns
        int numColumns = structureMatrixAsArray.GetLength(1);

        // Initialize return array 
        float[] result = new float[structureMatrixAsArray.GetLength(0)];

        // If the number of elements of the vector equals the number of columns
        if (numColumns == computedTrunkCableTensionsThisFrame.Length)
        {
            // Multiply
            // For each row of the matrix
            for (uint rowIndex = 0; rowIndex < structureMatrixAsArray.GetLength(0); rowIndex++)
            {
                // For each column
                float rowTotal = 0.0f;
                for (uint columnIndex = 0; columnIndex < numColumns; columnIndex++)
                {
                    // Multiply the vector[columnIndex] element by the matrix[rowIndex, columnIndex] element and add to growing sum
                    rowTotal = rowTotal + structureMatrixAsArray[rowIndex, columnIndex] * computedTrunkCableTensionsThisFrame[columnIndex];
                }

                // Store the sum as the prodcut result for that row
                result[rowIndex] = rowTotal;
            }
        }
        else // if the vector is the wrong size
        {
            Debug.LogError("Error: trying to multiply a matrix with " + numColumns + " columns by a vector of length " + computedTrunkCableTensionsThisFrame.Length);
        }

        // Return the result
        return result;
    }

    // END: Functions to send desired forces to tension planner, send tensions to robot*********************


    //Start: Debugging functions ************************************************************************

    //Use this function to print messages to console that will only appear when #ENABLE_LOGS
    //is defined. 
    [Conditional("ENABLE_LOGS")]
    private void printLogMessageToConsoleIfDebugModeIsDefined(string logMessage)
    {
        Debug.Log(logMessage); //log the message
    }

    // An enum for selecting whether or not we apply a cable tension rate limiter. 
    public enum CableTensionRateLimiterEnableEnum
    {
        Disabled, 
        Enabled
    }


    /*public enum ModelDataSourceSelector
    {
        ViconOnly,
        ViveOnly, 
        ViconAndVive
    };*/
    //End: Debugging functions ************************************************************************

}




