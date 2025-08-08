/* To switch the orientation of the subject (i.e. to make a different motor front left), we must 
 * do two things:
 *  1.) Label the pulley pointers appropriately in Vicon. So, label the pulley pointer that you want as 
 *  front left as the front left pulley pointer!
 *  2.) Change the motor number variables (e.g. frontLeftTrunkBeltPulleyNumber) below to match the designation.
 */



using System.Collections.Generic;
using System.Globalization;
using System;
using UnityEngine;

public class BuildStructureMatricesForBeltsThisFrameScript : MonoBehaviour
{
    // Number of cables, 4 or 8, selector for the pelvic belt
    private PelvicBeltCableNumberSelector pelvicBeltCableNumberSelector;

    //the constant part of a setup data for structure matrix file name
    private const string dataForStructureMatrixSummaryPrefix = "Data_To_Build_Structure_Matrix"; // For Vicon-based structure matrix construction
    private const string dataForViveBasedStructureMatrixSummaryPrefix = "Data_To_Build_ViveBased_Structure_Matrix"; // For Vive-based structure matrix construction

    // Mathematical constants
    private const float convertMetersToMillimeters = 1000.0f;

    // Public objects
    // COM manager
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    public GameObject forceFieldRobotTcpServerObject;
    private CommunicateWithRobustLabviewTcpServer forceFieldRobotTcpServerScript;

    // Force field high level controller
    public ForceFieldHighLevelControllerScript highLevelControllerScript;
    // Flags indicating which belts are being used. Get from high level controller.
    private bool usingChestBelt = false;
    private bool usingPelvicBelt = false;
    private bool usingShankBelts = false;
    private bool setupOnFirstCallByComManagerFlag = false;

    // Store the loaded setup data for building the structure matrix
    private string subdirectoryWithStructureMatrixDataName;
    private string[] setupDataHeadersForColumns;
    private float[] setupDataForComputingStructureMatrix;

    // Trunk belt pulley and cable attachment position storage (from setup)
    // Note: order is key. There MUST be correspondence between the order of the 
    // pulley positions and the order of the trunk belt attachment points in these vectors. 
    private string[] orderedTrunkBeltPulleyNames;
    private Vector3[] trunkBeltPulleyPositionsViconFrame;
    private string[] orderedTrunkBeltAttachmentPointNames;
    private Vector3[] trunkBeltAttachmentPointsBeltFrame;
    private List<Vector3> chestPulleyPositionInFrame0ForStore = new List<Vector3>();

    // Vive trunk 
    private Vector3[] trunkBeltPulleyPositionsViveFrame;
    // Vive pelvis
    private Vector3[] pelvicBeltPulleyPositionsViveFrame;
    // The trunk belt attachment points will have to be converted to Vicon frame. 
    // Initialize storage for this data. 
    private Vector3[] trunkBeltAttachmentPointsViconFrameThisFrame;
    private Vector3[] trunkBeltAttachmentPointsViveFrameThisFrame;
    private Vector3[] pelvicBeltAttachmentPointsViveFrameThisFrame;

    // Trunk belt fixed marker names needed for building trunk belt frame
    private const string trunkBeltBackCenterMarker = "TrunkBeltMiddle";
    private const string trunkBeltBackRightMarker = "TrunkBeltRight";
    private const string trunkBeltBackLeftMarker = "TrunkBeltLeft";
    private const string trunkBeltFrontRightMarker = "TrunkBeltFrontRight";
    private const string trunkBeltFrontLeftMarker = "TrunkBeltFrontLeft";

    // Trunk belt pulley names (matching the column headers in the setup file data). 
    // These should be stored in the proper order in the orderedTrunkBeltPulleyNames array.
    private const string frontLeftTrunkBeltPulleyName = "TRUNK_FRONT_LEFT_PULLEY";
    private const string frontRightTrunkBeltPulleyName = "TRUNK_FRONT_RIGHT_PULLEY";
    private const string backRightTrunkBeltPulleyName = "TRUNK_BACK_RIGHT_PULLEY";
    private const string backLeftTrunkBeltPulleyName = "TRUNK_BACK_LEFT_PULLEY";

    // Trunk belt pulley numbers
    // EDIT IF YOU WANT TO SWITCH THE SUBJECT ORIENTATION!
    // MUST ALSO MAKE SURE NEXUS SETUP SKELETON WAS LABELED WITH DESIRED ORIENTATION!
    private int[] orderedTrunkBeltPulleyMotorNumbers;
    private const int frontLeftTrunkBeltPulleyNumber = 6; // "TYPICAL VALUE" : 6
    private const int frontRightTrunkBeltPulleyNumber = 9; // "TYPICAL VALUE" : 9
    private const int backRightTrunkBeltPulleyNumber = 12; // "TYPICAL VALUE" : 12
    private const int backLeftTrunkBeltPulleyNumber = 3; // "TYPICAL VALUE" : 3

    // Trunk belt attachment point names (matching the column headers in the setup file data).
    // These should be stored in the proper order, corresponding to pulley names,
    // in the orderedTrunkBeltAttachmentPointNames array.
    private const string frontLeftTrunkBeltAttachmentPointName = "TrunkBeltCableFrontLeft";
    private const string frontRightTrunkBeltAttachmentPointName = "TrunkBeltCableFrontRight";
    private const string backRightTrunkBeltAttachmentPointName = "TrunkBeltCableBackRight";
    private const string backLeftTrunkBeltAttachmentPointName = "TrunkBeltCableBackLeft";

    // Chest belt cable attachment points in stance model frame 0
    private List<Vector3> chestBeltAttachmentPointsFrame0ForStore = new List<Vector3>();

    // Trunk belt cable attachment points in most recent frame
    private Vector3[] trunkBeltMostRecentAttachmentPointsViconFrame;

    // The most recent force and torque rows of the structure matrix
    private Vector3[] trunkColumnsOfForceStructureMatrix;
    private Vector3[] trunkColumnsOfTorqueStructureMatrix;

    // Pelvic belt pulley and cable attachment position storage (from setup)
    // Note: order is key. There MUST be correspondence between the order of the 
    // pulley positions and the order of the pelvic belt attachment points in these vectors. 
    private string[] orderedPelvicBeltPulleyNames;
    private Vector3[] pelvicBeltPulleyPositionsViconFrame;
    private string[] orderedPelvicBeltAttachmentPointNames;
    private Vector3[] pelvicBeltAttachmentPointsBeltFrame;
    private List<Vector3> pelvicPulleyPositionInFrame0ForStore = new List<Vector3>();
    
    
    // The pelvic belt attachment points will have to be converted to Vicon frame. 
    // Initialize storage for this data. 
    private Vector3[] pelvicBeltAttachmentPointsViconFrameThisFrame;

    // Pelvic belt fixed marker names needed for building pelvic belt frame
    private const string pelvicBeltBackCenterMarker = "PelvisBackCenter";
    private const string pelvicBeltBackRightMarker = "RPSI";
    private const string pelvicBeltBackLeftMarker = "LPSI";
    private const string pelvicBeltFrontRightMarker = "RASI";
    private const string pelvicBeltFrontLeftMarker = "LASI";

    // Pelvic belt pulley names (matching the column headers in the setup file data). 
    // These should be stored in the proper order in the orderedPelvicBeltPulleyNames array.
    // Lower pulleys in CW direction starting from front left
    private const string frontLeftLowerPelvicBeltPulleyName = "PELVIS_LOWER_FRONT_LEFT_PULLEY";
    private const string frontRightLowerPelvicBeltPulleyName = "PELVIS_LOWER_FRONT_RIGHT_PULLEY";
    private const string backRightLowerPelvicBeltPulleyName = "PELVIS_LOWER_BACK_RIGHT_PULLEY";
    private const string backLeftLowerPelvicBeltPulleyName = "PELVIS_LOWER_BACK_LEFT_PULLEY";
    // Upper pulleys in CW direction starting from front left
    private const string frontLeftUpperPelvicBeltPulleyName = "PELVIS_UPPER_FRONT_LEFT_PULLEY";
    private const string frontRightUpperPelvicBeltPulleyName = "PELVIS_UPPER_FRONT_RIGHT_PULLEY";
    private const string backRightUpperPelvicBeltPulleyName = "PELVIS_UPPER_BACK_RIGHT_PULLEY";
    private const string backLeftUpperPelvicBeltPulleyName = "PELVIS_UPPER_BACK_LEFT_PULLEY";

    // Pelvic belt pulley numbers
    // EDIT IF YOU WANT TO SWITCH THE SUBJECT ORIENTATION!
    // MUST ALSO MAKE SURE NEXUS SETUP SKELETON WAS LABELED WITH DESIRED ORIENTATION!
    private int[] orderedPelvicBeltPulleyMotorNumbers;
    // Lower pelvic pulleys
    private const int frontLeftLowerPelvicBeltPulleyMotorNumber = 7; // "TYPICAL VALUE" : 7
    private const int frontRightLowerPelvicBeltPulleyMotorNumber = 8; // "TYPICAL VALUE" : 8
    private const int backRightLowerPelvicBeltPulleyMotorNumber = 13; // "TYPICAL VALUE" : 13
    private const int backLeftLowerPelvicBeltPulleyMotorNumber = 2; // "TYPICAL VALUE" : 2
    // Upper pelvic pulleys
    private const int frontLeftUpperPelvicBeltPulleyMotorNumber = 5; // "TYPICAL VALUE" : 5
    private const int frontRightUpperPelvicBeltPulleyMotorNumber = 10; // "TYPICAL VALUE" : 10
    private const int backRightUpperPelvicBeltPulleyMotorNumber = 11; // "TYPICAL VALUE" : 11
    private const int backLeftUpperPelvicBeltPulleyMotorNumber = 4; // "TYPICAL VALUE" : 4

    // Pelvic belt attachment point names (matching the column headers in the setup file data).
    // These should be stored in the proper order, corresponding to pulley names,
    // in the orderedPelvicBeltAttachmentPointNames array.
    private const string frontLeftPelvicBeltAttachmentPointName = "PelvisBeltCableFrontLeft";
    private const string frontRightPelvicBeltAttachmentPointName = "PelvisBeltCableFrontRight";
    private const string backRightPelvicBeltAttachmentPointName = "PelvisBeltCableBackRight";
    private const string backLeftPelvicBeltAttachmentPointName = "PelvisBeltCableBackLeft";

    // Pelvic belt cable attachment points in stance model frame 0
    private List<Vector3> pelvicBeltAttachmentPointsFrame0ForStore = new List<Vector3>();

    // Pelvic belt cable attachment points in most recent frame
    private Vector3[] pelvicBeltMostRecentAttachmentPointsViconFrame;

    // The most recent force and torque rows of the pelvis structure matrix
    private Vector3[] pelvisColumnsOfForceStructureMatrix;
    private Vector3[] pelvisColumnsOfTorqueStructureMatrix;

    private const string rightShankBeltPulleyName = "RIGHT_SHANK_BELT_PULLEY";
    private const string leftShankBeltPulleyName = "LEFT_SHANK_BELT_PULLEY";

    // Both shank belt pulley and cable attachment position storage (from setup)
    // Note: order is key. There MUST be correspondence between the order of the 
    // pulley positions and the order of the knee belt attachment points in these vectors. 
    private string[] orderedRightShankBeltPulleyNames;
    private Vector3[] rightShankBeltPulleyPositionsViconFrame;
    private Vector3[] rightShankBeltPulleyPositionsViveFrame;
    private string[] orderedRightShankBeltAttachmentPointNames;
    private Vector3[] rightShankBeltAttachmentPointsBeltFrame;

    private string[] orderedLeftShankBeltPulleyNames;
    private Vector3[] leftShankBeltPulleyPositionsViconFrame;
    private Vector3[] leftShankBeltPulleyPositionsViveFrame;
    private string[] orderedLeftShankBeltAttachmentPointNames;
    private Vector3[] leftShankBeltAttachmentPointsBeltFrame;

    // The shank belt attachment points will have to be converted to Vicon frame. 
    // Initialize storage for this data. 
    private Vector3[] rightShankBeltAttachmentPointsViconFrameThisFrame;
    private Vector3[] leftShankBeltAttachmentPointsViconFrameThisFrame;
    
    private Vector3[] rightShankBeltAttachmentPointsViveFrameThisFrame;
    private Vector3[] leftShankBeltAttachmentPointsViveFrameThisFrame;
    
    // Shank belt pulley locations
    private List<Vector3> shankPulleyPositionInFrame0ForStore = new List<Vector3>();
    private List<Vector3> shankAttachmentPointPositionInFrame0ForStore = new List<Vector3>();

    // Knee belt fixed marker names needed for building knee belt frame
    private const string rightAnkleMarkerName = "RANK";
    private const string rightAnkleMedialMarkerName = "RANKM";
    private const string rightKneeMarkerName = "RKNE";
    private const string rightKneeMedialMarkerName = "RKNEEM";
    // Right shank tibial tuberosity
    private const string rightTibialTuberosityMarkerName = "R.TibTubero";
    private const string leftTibialTuberosityMarkerName = "L.TibTubero";

    private const string leftAnkleMarkerName = "LANK";
    private const string leftAnkleMedialMarkerName = "LANKM";
    private const string leftKneeMarkerName = "LKNE";
    private const string leftKneeMedialMarkerName = "LKNEEM";

    // Knee belt pulley names (matching the column headers in the setup file data). 
    // These should be stored in the proper order in the orderedRightShankBeltPulleyNames array.
    private const string backLeftKneeBeltPulleyName = "SHANK_BACK_LEFT_PULLEY";
    private const string backRightKneeBeltPulleyName = "SHANK_BACK_RIGHT_PULLEY";

    // Knee belt pulley numbers
    // EDIT IF YOU WANT TO SWITCH THE SUBJECT ORIENTATION!
    // MUST ALSO MAKE SURE NEXUS SETUP SKELETON WAS LABELED WITH DESIRED ORIENTATION!
    private int[] orderedRightShankBeltPulleyMotorNumbers;
    private int[] orderedLeftShankBeltPulleyMotorNumbers;
    private const int rightShankBeltPulleyNumber = 14;
    private const int leftShankBeltPulleyNumber = 1;

    // Knee belt attachment point names (matching the column headers in the setup file data).
    // These should be stored in the proper order, corresponding to pulley names,
    // in the orderedKneeBeltAttachmentPointNames array.
    private const string rightShankBeltCableAttachmentPointName = "RightShankAttachmentMarker";
    private const string leftShankBeltCableAttachmentPointName = "LeftShankAttachmentMarker";

    // Knee belt cable attachment points in most recent frame
    private Vector3[] kneeBeltMostRecentAttachmentPointsViconFrame;

    // The most recent force and torque rows of the shank structure matrices
    private Vector3[] leftShankColumnsOfForceStructureMatrix;
    private Vector3[] leftShankColumnsOfTorqueStructureMatrix;
    private Vector3[] rightShankColumnsOfForceStructureMatrix;
    private Vector3[] rightShankColumnsOfTorqueStructureMatrix;
    private KinematicModelOfStance stanceModel;
    public KinematicModelClass KinematicModel;
    
    // Vive tracker data manager
    public ViveTrackerDataManager viveTrackerDataManagerScript; // manages Vive tracker data, especially belt-relevant things like transformations

    // Start is called before the first frame update
    void Start()
    {
        // Get reference to marker data and center of mass manager script
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // Get the communication with force field robot (e.g. RobUST) script
        forceFieldRobotTcpServerScript = forceFieldRobotTcpServerObject.GetComponent<CommunicateWithRobustLabviewTcpServer>();

        // Get the number of pelvic cables from the force field high level controller. 
        // We put it there so that there are fewer places to select options.
        pelvicBeltCableNumberSelector = highLevelControllerScript.GetPelvicBeltNumberOfCablesSelector();
    }


    private void SetupForChestBelt()
    {
        // Order the trunk belt pulley names. 
        // KEY NOTE: THIS is where the mapping from belt attachment point to pulley is made!
        // DO NOT CHANGE THIS ORDER (there is no need)
        orderedTrunkBeltPulleyNames = new string[] { frontLeftTrunkBeltPulleyName , frontRightTrunkBeltPulleyName,
                                                    backRightTrunkBeltPulleyName, backLeftTrunkBeltPulleyName};

        // Now, choose the corresponding motor numbers.
        // Change the motor number instance variable values when declared to 
        // change the subject orientation.
        orderedTrunkBeltPulleyMotorNumbers = new int[] { frontLeftTrunkBeltPulleyNumber, frontRightTrunkBeltPulleyNumber,
                                                    backRightTrunkBeltPulleyNumber, backLeftTrunkBeltPulleyNumber};

        // Order the trunk belt attachment point names. 
        // Key note: the order itself doesn't matter, but there MUST be correspondence between the attachment point order
        // and the pulley order!
        orderedTrunkBeltAttachmentPointNames = new string[] { frontLeftTrunkBeltAttachmentPointName , frontRightTrunkBeltAttachmentPointName ,
                                                        backRightTrunkBeltAttachmentPointName, backLeftTrunkBeltAttachmentPointName};

        // Initialize the trunk belt attachment points in Vicon frame storage array to the correct size. 
        trunkBeltAttachmentPointsViconFrameThisFrame = new Vector3[orderedTrunkBeltAttachmentPointNames.Length];
        trunkBeltAttachmentPointsViveFrameThisFrame = new Vector3[orderedTrunkBeltAttachmentPointNames.Length];
        
    }


    private void SetupForPelvicBelt()
    {
        // Order the pelvic belt pulley names. 
        // KEY NOTE: THIS is where the mapping from belt attachment point to pulley is made!
        // DO NOT CHANGE THIS ORDER (there is no need)
        // If four cables only
        if(pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Four)
        {
            // We'll use the "lower" pulley names in this case
            orderedPelvicBeltPulleyNames = new string[] { frontLeftLowerPelvicBeltPulleyName , frontRightLowerPelvicBeltPulleyName,
                                                    backRightLowerPelvicBeltPulleyName, backLeftLowerPelvicBeltPulleyName};

            // Now, choose the corresponding motor numbers.
            // Change the motor number instance variable values when declared to 
            // change the subject orientation.
            orderedPelvicBeltPulleyMotorNumbers = new int[] { frontLeftLowerPelvicBeltPulleyMotorNumber, frontRightLowerPelvicBeltPulleyMotorNumber,
                                                    backRightLowerPelvicBeltPulleyMotorNumber, backLeftLowerPelvicBeltPulleyMotorNumber};

            // Order the pelvic belt attachment point names. 
            // Key note: the order itself doesn't matter, but there MUST be correspondence between the attachment point order
            // and the pulley order!
            orderedPelvicBeltAttachmentPointNames = new string[] { frontLeftPelvicBeltAttachmentPointName , frontRightPelvicBeltAttachmentPointName ,
                                                        backRightPelvicBeltAttachmentPointName, backLeftPelvicBeltAttachmentPointName};
        }else if(pelvicBeltCableNumberSelector == PelvicBeltCableNumberSelector.Eight)
        {
            // We'll use all pulley names in this case
            orderedPelvicBeltPulleyNames = new string[] { frontLeftLowerPelvicBeltPulleyName , frontRightLowerPelvicBeltPulleyName,
                                                    backRightLowerPelvicBeltPulleyName, backLeftLowerPelvicBeltPulleyName,
                                                    frontLeftUpperPelvicBeltPulleyName , frontRightUpperPelvicBeltPulleyName,
                                                    backRightUpperPelvicBeltPulleyName, backLeftUpperPelvicBeltPulleyName};

            // Now, choose the corresponding motor numbers.
            // Change the motor number instance variable values when declared to 
            // change the subject orientation.
            orderedPelvicBeltPulleyMotorNumbers = new int[] { frontLeftLowerPelvicBeltPulleyMotorNumber, frontRightLowerPelvicBeltPulleyMotorNumber,
                                                    backRightLowerPelvicBeltPulleyMotorNumber, backLeftLowerPelvicBeltPulleyMotorNumber,
                                                    frontLeftUpperPelvicBeltPulleyMotorNumber, frontRightUpperPelvicBeltPulleyMotorNumber,
                                                    backRightUpperPelvicBeltPulleyMotorNumber, backLeftUpperPelvicBeltPulleyMotorNumber};

            // Order the pelvic belt attachment point names. 
            // Key note: the order itself doesn't matter, but there MUST be correspondence between the attachment point order
            // and the pulley order!
            // Note: in the case of 8 cables, we still only have 4 attachment points, but we'll repeat
            // the names twice since we need one-to-one index matching.
            orderedPelvicBeltAttachmentPointNames = new string[] { frontLeftPelvicBeltAttachmentPointName , frontRightPelvicBeltAttachmentPointName ,
                                                        backRightPelvicBeltAttachmentPointName, backLeftPelvicBeltAttachmentPointName,
                                                        frontLeftPelvicBeltAttachmentPointName , frontRightPelvicBeltAttachmentPointName ,
                                                        backRightPelvicBeltAttachmentPointName, backLeftPelvicBeltAttachmentPointName};
        }


        // Initialize the pelvic belt attachment points in Vicon frame storage array to the correct size. 
        pelvicBeltAttachmentPointsViconFrameThisFrame = new Vector3[orderedPelvicBeltAttachmentPointNames.Length];

    }

    private void SetupForRightShankBelt()
    {
        // Order the right shank belt pulley names. 
        // KEY NOTE: THIS is where the mapping from belt attachment point to pulley is made!
        // DO NOT CHANGE THIS ORDER (there is no need)
        orderedRightShankBeltPulleyNames = new string[] {rightShankBeltPulleyName};

        // Now, choose the corresponding motor numbers.
        // Change the motor number instance variable values when declared to 
        // change the subject orientation.
        orderedRightShankBeltPulleyMotorNumbers = new int[] { rightShankBeltPulleyNumber};

        // Order the right shank belt attachment point names. 
        // Key note: the order itself doesn't matter, but there MUST be correspondence between the attachment point order
        // and the pulley order!
        orderedRightShankBeltAttachmentPointNames = new string[] { rightShankBeltCableAttachmentPointName };

        // Initialize the right shank belt attachment points in Vicon frame storage array to the correct size. 
        rightShankBeltAttachmentPointsViconFrameThisFrame = new Vector3[orderedRightShankBeltAttachmentPointNames.Length];
    }

    private void SetupForLeftShankBelt()
    {
        // Order the left shank belt pulley names. 
        // KEY NOTE: THIS is where the mapping from belt attachment point to pulley is made!
        // DO NOT CHANGE THIS ORDER (there is no need)
        orderedLeftShankBeltPulleyNames = new string[] { leftShankBeltPulleyName };

        // Now, choose the corresponding motor numbers.
        // Change the motor number instance variable values when declared to 
        // change the subject orientation.
        orderedLeftShankBeltPulleyMotorNumbers = new int[] { leftShankBeltPulleyNumber };

        // Order the left shank belt attachment point names. 
        // Key note: the order itself doesn't matter, but there MUST be correspondence between the attachment point order
        // and the pulley order!
        orderedLeftShankBeltAttachmentPointNames = new string[] { leftShankBeltCableAttachmentPointName };

        // Initialize the left shank belt attachment points in Vicon frame storage array to the correct size. 
        leftShankBeltAttachmentPointsViconFrameThisFrame = new Vector3[orderedLeftShankBeltAttachmentPointNames.Length];
    }


    // This is the key function for this script. It is called by the COM manager once new Vicon marker data has been read. 
    // It will compute the structure matrices for all belts being used and pass them to the TCP server object to send to 
    // the Labview control script of the robot.
    public void BuildStructureMatricesForThisFrame()
    {
        // If we have not yet set up on the first call to this function by the COM/marker data manager
        if(setupOnFirstCallByComManagerFlag == false)
        {
            Debug.Log("Setting up for building structure matrices.");
            // Get which robot belts we'll be using
            usingChestBelt = highLevelControllerScript.GetChestBeltBeingUsedFlag();
            usingPelvicBelt = highLevelControllerScript.GetPelvicBeltBeingUsedFlag();
            usingShankBelts = highLevelControllerScript.GetShankBeltsBeingUsedFlag();

            // Setup for each belt should be done regardless of whether or not we use it, so we 
            // can map motor numbers to desired tensions for ALL motors.
            SetupForChestBelt();
            SetupForPelvicBelt();
            SetupForRightShankBelt();
            SetupForLeftShankBelt();

/*            if (usingChestBelt)
            {
                Debug.Log("Structure matrix builder: setting up for chest belt calculations.");
                SetupForChestBelt();
            }

            Debug.Log("Structure matrix builder: during setup, Using pelvic belt flag is: " + usingPelvicBelt);
            if (usingPelvicBelt)
            {
                Debug.Log("Structure matrix builder: setting up for pelvic belt calculations.");
                SetupForPelvicBelt();
            }

            if (usingShankBelts)
            {
                Debug.Log("Structure matrix builder: setting up for shank belt calculations.");
                SetupForRightShankBelt();
                SetupForLeftShankBelt();
            }*/

            // Load the structure matrix data from file
            loadDailySetupDataForStructureMatrixConstruction(subdirectoryWithStructureMatrixDataName);

            // If the high-level controller has instantiated a stance model
            if (highLevelControllerScript.GetForceFieldLevelManagerSetupCompleteFlag() == true)
            {
                // Get the stance model instance
                stanceModel = highLevelControllerScript.GetStanceModel();
            }

            setupOnFirstCallByComManagerFlag = true;
        }



        // Compute and store the trunk belt structure matrix
        if (usingChestBelt)
        {
           // Debug.Log("Computing trunk belt structure matrix this frame.");
           ComputeAndStoreTrunkBeltStructureMatrix();
        }

        // Compute and store pelvic belt structure matrix (if using)
        if (usingPelvicBelt)
        {
            //Debug.Log("Computing pelvic belt structure matrix this frame.");
            ComputeAndStorePelvicBeltStructureMatrix();
        }

        // Compute and store right shank belt structure matrix (if using)
        if (usingShankBelts)
        {
            //Debug.Log("Computing R, L shank belt structure matrices this frame.");
            shankPulleyPositionInFrame0ForStore.Clear();
            shankAttachmentPointPositionInFrame0ForStore.Clear();
            (Vector3 rightShankPulleyPositionInFrame0, Vector3 rightShankAttachmentPositionInFrame0 )= ComputeAndStoreRightShankBeltStructureMatrix();
            (Vector3 leftShankPulleyPositionInFrame0, Vector3 leftShankAttachmentPositionInFrame0) = ComputeAndStoreLeftShankBeltStructureMatrix();
            shankPulleyPositionInFrame0ForStore.Add(leftShankPulleyPositionInFrame0);
            shankPulleyPositionInFrame0ForStore.Add(rightShankPulleyPositionInFrame0);
            shankAttachmentPointPositionInFrame0ForStore.Add(leftShankAttachmentPositionInFrame0);
            shankAttachmentPointPositionInFrame0ForStore.Add(rightShankAttachmentPositionInFrame0);
        }

        // Call the Vive tracker data manager and tell it to visualize the cable attachments. 
        // It may or may not visualize depending on its internal flag setting.
        viveTrackerDataManagerScript.VisualizeAllBeltAttachmentsInRenderingFrame0();




        // ************************************************************
        // 2.) Run the sequence of steps to get the structure matrix for the pelvic belt
        // NOT IMPLEMENTED YET!

        // ************************************************************


        // NOTE: we no longer send the structure matrix to RobUST - we instead send cable tensions. 
        // 3.) Send the structure matrix (or matrices) to the TCP service to be sent on to the robot (RobUST)
        //forceFieldRobotTcpServerScript.SendTrunkStructureMatrixToRobot(trunkColumnsOfForceStructureMatrix, trunkColumnsOfTorqueStructureMatrix);
    }


    // Returns the (6xm) trunk structure matrix as separate force rows and torque rows.
    // m is # of cables acting on the body segment.
    public (Vector3[], Vector3[]) GetMostRecentTrunkStructureMatrixForceAndTorqueRows()
    {
        
        
        return (trunkColumnsOfForceStructureMatrix, trunkColumnsOfTorqueStructureMatrix);
    }

    // Returns the (6xm) pelvis structure matrix as separate force rows and torque rows.
    // m is # of cables acting on the body segment.
    public (Vector3[], Vector3[]) GetMostRecentPelvicStructureMatrixForceAndTorqueRows()
    { // we need to reconstruct the structure matrix
        
        return (pelvisColumnsOfForceStructureMatrix, pelvisColumnsOfTorqueStructureMatrix);
    }

    // Returns the (6xm) shank structure matrices for left, then right legs as separate force rows and torque rows.
    // m is # of cables acting on the body segment.
    public (Vector3[], Vector3[], Vector3[], Vector3[]) GetMostRecentShankLeftRightStructureMatricesForceAndTorqueRows()
    {
        return (leftShankColumnsOfForceStructureMatrix, leftShankColumnsOfTorqueStructureMatrix,
                rightShankColumnsOfForceStructureMatrix, rightShankColumnsOfTorqueStructureMatrix);
    }


    public void SetStructureMatrixDirectoryName(string subdirectoryName)
    {
        subdirectoryWithStructureMatrixDataName = subdirectoryName;
    }


    // This function tells this game object to load the boundary of stability. It must be called
    // by the level manager, which knows the subject number and other details
    // specifying the path to save to/load from.
    public void loadDailySetupDataForStructureMatrixConstruction(string pathToDirectoryWithFile, string keyword = "")
    {
        Debug.Log("Loading structure matrix data from local path: " + pathToDirectoryWithFile);

        //load the setup data (pulley positions, belt markers in belt frame) for the current subject, if available. 
        setupDataForComputingStructureMatrix = loadStructureMatrixDataFromFile(pathToDirectoryWithFile, keyword);
        
        Debug.Log("Successfully loaded structure matrix data");

        // Sort the loaded data so it is easily accessible
        SortLoadedSetupData();
    }


    public int[] GetOrderedTrunkBeltPulleyMotorNumbers()
    {
        // This returns the motor numbers that correspond to the columns
        // of the S matrix for the trunk belt. 
        // E.g. if the first pulley motor number is 12, then the first column
        // of the S matrix corresponds to that motor/tension.
        return orderedTrunkBeltPulleyMotorNumbers;
    }

    public int[] GetOrderedPelvicBeltPulleyMotorNumbers()
    {
        // This returns the motor numbers that correspond to the columns
        // of the S matrix for the pelvic belt. 
        // E.g. if the first pulley motor number is 12, then the first column
        // of the S matrix corresponds to that motor/tension.
        return orderedPelvicBeltPulleyMotorNumbers; //MAKE VARIABLE
    }

    public int[] GetOrderedRightShankBeltPulleyMotorNumbers()
    {
        // This returns the motor numbers that correspond to the columns
        // of the S matrix for the right shank belt. 
        // E.g. if the first pulley motor number is 12, then the first column
        // of the S matrix corresponds to that motor/tension.
        return orderedRightShankBeltPulleyMotorNumbers; //MAKE VARIABLE
    }

    public int[] GetOrderedLeftShankBeltPulleyMotorNumbers()
    {
        // This returns the motor numbers that correspond to the columns
        // of the S matrix for the right shank belt. 
        // E.g. if the first pulley motor number is 12, then the first column
        // of the S matrix corresponds to that motor/tension.
        return orderedLeftShankBeltPulleyMotorNumbers; //MAKE VARIABLE
    }



    private void ComputeAndStoreTrunkBeltStructureMatrix()
    {
        if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViconOnly)
        {
            // 1.) Run the sequence of steps to get the structure matrix for the trunk belt
            // Get current trunk belt fixed/permanent marker positions for this frame and 
            // build the transformation matrix from trunk belt frame to Vicon frame
            Matrix4x4 transformationMatrixTrunkBeltToViconThisFrame =
                ConstructTransformationMatrixFromTrunkBeltFrameToViconFrame();

            // 2.) "Reconstruct" temporary marker positions by converting their stored belt frame positions
            // to Vicon frame (using this frame's belt transformation matrix)
            // For each attachment point
            for (int attachmentPointIndex = 0;
                 attachmentPointIndex < trunkBeltAttachmentPointsBeltFrame.Length;
                 attachmentPointIndex++)
            {
                // Convert the position from trunk belt frame to Vicon frame and store
                trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex] =
                    transformationMatrixTrunkBeltToViconThisFrame.MultiplyPoint3x4(
                        trunkBeltAttachmentPointsBeltFrame[attachmentPointIndex]);

                // Testing only
                /*Debug.Log("Trunk belt attachemnt in belt frame at ( " + trunkBeltAttachmentPointsBeltFrame[attachmentPointIndex].x +
                    ", " + trunkBeltAttachmentPointsBeltFrame[attachmentPointIndex].y +
                    ", " + trunkBeltAttachmentPointsBeltFrame[attachmentPointIndex].z + ")" +
                    "and the trunk belt attachment in Vicon frame is at ( " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].x +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].y +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].z + ")"); */
            }

            // 3.) Retrieve the current trunk belt center position from the COM manager
            Vector3 mostRecentTrunkBeltCenterPosition = GetMostRecentTrunkBeltCenterPositionInViconFrame();

            // 4.) Build the trunk belt structure matrix for this frame
            (trunkColumnsOfForceStructureMatrix, trunkColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrix(mostRecentTrunkBeltCenterPosition, trunkBeltPulleyPositionsViconFrame,
                    trunkBeltAttachmentPointsViconFrameThisFrame);
        }
        else if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViveOnly)
        {
            // Run the sequence of steps to get the structure matrix for the trunk belt
            // 1.) Retrieve the current pelvic belt center position in  frame 0
            Vector3 chestBeltCenterInUnityFrame = viveTrackerDataManagerScript.GetChestCenterPositionInUnityFrame();
            // Get the transformation from frame 0 to Unity frame
            Matrix4x4 transformationFromFrame0ToUnityFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationFromUnityFrameToFrame0 = transformationFromFrame0ToUnityFrame.inverse;
            //Matrix4x4.Inverse3DAffine(transformationFromFrame0ToUnityFrame, ref transformationFromUnityFrameToFrame0);
            Vector3 chestBeltCenterInFrame0 =
                transformationFromUnityFrameToFrame0.MultiplyPoint3x4(chestBeltCenterInUnityFrame);

            // 2.) Get chest Belt Attachment Points in frame 0
            GameObject chestViveTracker = viveTrackerDataManagerScript.GetChestViveTrackerGameObject();
            List<Vector3> chestBeltAttachmentPointsFrame0 = new List<Vector3>();
            foreach (Vector3 localPosition in trunkBeltAttachmentPointsBeltFrame)
            {
                // Transform from Vive tracker to Unity coordinates, accounting for Vive tracker object scaling.
                Vector3 chestBeltAttachmentPointsUnityFrame = chestViveTracker.transform.TransformPoint(localPosition / chestViveTracker.transform.localScale.x);
                // 
                Vector3 chestBeltAttachmentPointsInFrame0 =
                    transformationFromUnityFrameToFrame0.MultiplyPoint3x4(chestBeltAttachmentPointsUnityFrame);
                chestBeltAttachmentPointsFrame0.Add(chestBeltAttachmentPointsInFrame0);
            }

            // Store the attachment points in frame 0 for other scripts to access (e.g. attachment visualization)
            chestBeltAttachmentPointsFrame0ForStore = chestBeltAttachmentPointsFrame0;

            // 3.) Get chest Belt Pulley Positions in Frame0
            Matrix4x4 transformationFromViveReferenceTrackerLeftHandedFrameToUnityFrame = viveTrackerDataManagerScript
                .GetTransformationFromViveReferenceTrackerLeftHandedToUnityFrame();

            // Debug Only: compose the transformation from left-handed Vive ref frame to frame 0 and print
           /* Matrix4x4 transformationLeftHandedViveToRightHandedFrame0 = transformationFromUnityFrameToFrame0 *
                                                                        transformationFromViveReferenceTrackerLeftHandedFrameToUnityFrame;*/
            //Debug.Log("Transformation matrix Vive left-handed ref. to frame 0: " + transformationLeftHandedViveToRightHandedFrame0);

            // Transform from reference Vive tracker frame to frame 0 of the biomechanical model of stance.
            List<Vector3> chestBeltPulleyPositionsFrame0 = new List<Vector3>();

            // Loop through the trunk/chest belt pulley positions expressed in the Vive reference frame
            foreach (var position in trunkBeltPulleyPositionsViveFrame)
            {
                Vector3 positionInUnity =
                    transformationFromViveReferenceTrackerLeftHandedFrameToUnityFrame.MultiplyPoint3x4(position);
                Vector3 positionInFrame0 = transformationFromUnityFrameToFrame0.MultiplyPoint3x4(positionInUnity);
                chestBeltPulleyPositionsFrame0.Add(positionInFrame0);
            }
            // Store the pulley positions
            chestPulleyPositionInFrame0ForStore = chestBeltPulleyPositionsFrame0;

            // 4.) Build the trunk belt structure matrix for this frame
            // The ConstructStructureMatrix...() function expects 
            // a belt center, pulley positions, and cable attachment points in 
            // THE SAME global frame (frame 0 now).
            (trunkColumnsOfForceStructureMatrix, trunkColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrixInVive(chestBeltCenterInFrame0,
                    chestBeltPulleyPositionsFrame0.ToArray(),
                    chestBeltAttachmentPointsFrame0.ToArray());
        }
    }


    private void ComputeAndStorePelvicBeltStructureMatrix()
    {
        if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViconOnly)
        {
            // 1.) Run the sequence of steps to get the structure matrix for the trunk belt
            // Get current trunk belt fixed/permanent marker positions for this frame and 
            // build the transformation matrix from trunk belt frame to Vicon frame
            Matrix4x4 transformationMatrixPelvicBeltToViconThisFrame =
                ConstructTransformationMatrixFromPelvicBeltFrameToViconFrame();

            // 2.) "Reconstruct" temporary marker positions by converting their stored belt frame positions
            // to Vicon frame (using this frame's belt transformation matrix)
            // For each attachment point
            for (int attachmentPointIndex = 0;
                 attachmentPointIndex < pelvicBeltAttachmentPointsBeltFrame.Length;
                 attachmentPointIndex++)
            {
                // Convert the position from trunk belt frame to Vicon frame and store
                pelvicBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex] =
                    transformationMatrixPelvicBeltToViconThisFrame.MultiplyPoint3x4(
                        pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex]);

                // Testing only
                /*Debug.Log("Trunk belt attachemnt in belt frame at ( " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].x +
                    ", " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].y +
                    ", " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].z + ")" +
                    "and the trunk belt attachment in Vicon frame is at ( " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].x +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].y +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].z + ")"); */
            }

            // 3.) Retrieve the current trunk belt center position from the COM manager
            Vector3 mostRecentPelvicBeltCenterPosition = GetMostRecentPelvicBeltCenterPositionInViconFrame();

            // 4.) Build the trunk belt structure matrix for this frame
            (pelvisColumnsOfForceStructureMatrix, pelvisColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrix(mostRecentPelvicBeltCenterPosition, pelvicBeltPulleyPositionsViconFrame,
                    pelvicBeltAttachmentPointsViconFrameThisFrame);
        }
        else if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViveOnly)
        {  
            // 1.) Retrieve the current pelvic belt center position in  frame 0
            Vector3 pelvicBeltCenterInUnityFrame = viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();
            Matrix4x4 transformationFromFrame0ToUnityFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationFromUnityFrameToFrame0 = transformationFromFrame0ToUnityFrame.inverse;
            //Matrix4x4.Inverse3DAffine(transformationFromFrame0ToUnityFrame, ref transformationFromUnityFrameToFrame0);
            Vector3 pelvicBeltCenterInFrame0 =
                transformationFromUnityFrameToFrame0.MultiplyPoint3x4(pelvicBeltCenterInUnityFrame);

            // 2.) Get pelvic Belt Attachment Points in frame 0
            GameObject pelvicViveTracker = viveTrackerDataManagerScript.GetPelvicViveTrackerGameObject();
            List<Vector3> pelvicBeltAttachmentPointsFrame0 = new List<Vector3>();
            foreach (Vector3 localPosition in pelvicBeltAttachmentPointsBeltFrame)
            {
                Vector3 pelvicBeltAttachmentPointsUnityFrame = pelvicViveTracker.transform.TransformPoint(localPosition / pelvicViveTracker.transform.localScale.x);
                Vector3 vectorPelvicBeltAttachmentPointsInFrame0 =
                    transformationFromUnityFrameToFrame0.MultiplyPoint3x4(pelvicBeltAttachmentPointsUnityFrame);
                pelvicBeltAttachmentPointsFrame0.Add(vectorPelvicBeltAttachmentPointsInFrame0);
            }

            pelvicBeltAttachmentPointsFrame0ForStore = pelvicBeltAttachmentPointsFrame0;
            
            // 3.) Get pelvic Belt Pulley Positions in Frame0
            Matrix4x4 transformationFromViveReferenceTrackerLeftHandedFrameToUnityFrame = viveTrackerDataManagerScript
                .GetTransformationFromViveReferenceTrackerLeftHandedToUnityFrame();
            
            // Debug Only: compose the transformation from left-handed Vive ref frame to frame 0 and print
            Matrix4x4 transformationLeftHandedViveToRightHandedFrame0 = transformationFromUnityFrameToFrame0 *
                                                                        transformationFromViveReferenceTrackerLeftHandedFrameToUnityFrame;
            //Debug.Log("Transformation matrix Vive left-handed ref. to frame 0: " + transformationLeftHandedViveToRightHandedFrame0);
            
            // Transform from reference Vive tracker frame to frame 0 of the biomechanical model of stance.
            List<Vector3> pelvicBeltPulleyPositionsFrame0 = new List<Vector3>();
            List<Vector3> pelvicBeltAttachmentPointFrame0 = new List<Vector3>();
            GameObject pelvisTracker = viveTrackerDataManagerScript.GetPelvicViveTrackerGameObject();
            
            foreach (var position in pelvicBeltPulleyPositionsViveFrame)
            {
                Vector3 positionInUnity =
                    transformationFromViveReferenceTrackerLeftHandedFrameToUnityFrame.MultiplyPoint3x4(position);
                Vector3 positionInFrame0 = transformationFromUnityFrameToFrame0.MultiplyPoint3x4(positionInUnity);
                pelvicBeltPulleyPositionsFrame0.Add(positionInFrame0);
            }
            
            // 4.) Build the pelvic belt structure matrix for this frame
/*            Debug.Log("The pelvicBeltPulleyPositionsFrame0 is " + pelvicBeltPulleyPositionsViveFrame.Length + " elements long.");
            foreach (var pos in pelvicBeltPulleyPositionsViveFrame)
            {
                Debug.Log("pelvicBeltPulleyPositions position in frame Vive: " + pos);
            }
            foreach (var pos in pelvicBeltPulleyPositionsFrame0)
            {
                Debug.Log("pelvicBeltPulleyPositions position in frame 0: " + pos);
            }
            
            Debug.Log("The pelvicBeltAttachmentPointsBeltFrame is " + pelvicBeltAttachmentPointsBeltFrame.Length + " elements long.");
            foreach (var pos in pelvicBeltAttachmentPointsBeltFrame)
            {
                Debug.Log("pelvicBeltAttachmentPointsBeltFrame: " + pos);
            }*/

            // The ConstructStructureMatrix...() function expects 
            // a belt center, pulley positions, and cable attachment points in 
            // THE SAME global frame (frame 0 now).
            if (pelvicPulleyPositionInFrame0ForStore == null)
            {
                pelvicPulleyPositionInFrame0ForStore = new List<Vector3>();
            }

            pelvicPulleyPositionInFrame0ForStore.Clear(); // empty the previously computed pulley positions in frame 0
            pelvicPulleyPositionInFrame0ForStore = pelvicBeltPulleyPositionsFrame0; 
            (pelvisColumnsOfForceStructureMatrix, pelvisColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrixInVive(pelvicBeltCenterInFrame0,
                    pelvicBeltPulleyPositionsFrame0.ToArray(),
                    pelvicBeltAttachmentPointsFrame0.ToArray());

            // DEBUG ONLY - print S matrix for pelvis
           /* int indexNumber = 0;
            foreach (var columns in pelvisColumnsOfForceStructureMatrix)
            {
                Debug.Log(" the " + indexNumber + " column of the pelvis Columns Of ForceStructureMatrix is " + columns);
                indexNumber = indexNumber + 1;
            }*/
        }
        
    }



    private (Vector3,Vector3) ComputeAndStoreRightShankBeltStructureMatrix()
    {
        Vector3 rightShankPulleyPos = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 rightShankAttachmentPosInFrame0 = new Vector3();
        if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViconOnly)
        {
            // 1.) Run the sequence of steps to get the structure matrix for the trunk belt
            // Get current trunk belt fixed/permanent marker positions for this frame and 
            // build the transformation matrix from trunk belt frame to Vicon frame
            Matrix4x4 transformationMatrixRightShankBeltToViconThisFrame =
                ConstructTransformationMatrixFromRightShankBeltFrameToViconFrame();

            // 2.) "Reconstruct" temporary marker positions by converting their stored belt frame positions
            // to Vicon frame (using this frame's belt transformation matrix)
            // For each attachment point
            for (int attachmentPointIndex = 0;
                 attachmentPointIndex < rightShankBeltAttachmentPointsViconFrameThisFrame.Length;
                 attachmentPointIndex++)
            {
                // Convert the position from trunk belt frame to Vicon frame and store
                rightShankBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex] =
                    transformationMatrixRightShankBeltToViconThisFrame.MultiplyPoint3x4(
                        rightShankBeltAttachmentPointsBeltFrame[attachmentPointIndex]);

                // Testing only
                /*Debug.Log("Trunk belt attachemnt in belt frame at ( " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].x +
                    ", " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].y +
                    ", " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].z + ")" +
                    "and the trunk belt attachment in Vicon frame is at ( " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].x +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].y +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].z + ")"); */
            }

            // 3.) Retrieve the current trunk belt center position from the COM manager
            Vector3 mostRecentRightShankBeltCenterPosition = GetMostRecentRightShankBeltCenterPositionInViconFrame();

            // 4.) Build the trunk belt structure matrix for this frame
            (rightShankColumnsOfForceStructureMatrix, rightShankColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrix(mostRecentRightShankBeltCenterPosition,
                    rightShankBeltPulleyPositionsViconFrame,
                    rightShankBeltAttachmentPointsViconFrameThisFrame);
            rightShankPulleyPos = rightShankBeltPulleyPositionsViconFrame[0];
        }
        else if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViveOnly)
        {
            // Transform the right shank pulley position from Vive ref left-handed frame (in csv file) to frame 0
            Vector3 rightShankBeltPulleyPositionViveFrame = rightShankBeltPulleyPositionsViveFrame[0];
            Matrix4x4 transformationFromViveReferenceTrackerLeftHandedToUnityFrame = viveTrackerDataManagerScript
                .GetTransformationFromViveReferenceTrackerLeftHandedToUnityFrame();
            Matrix4x4 transformationMatrixFromFrame0ToUnityFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnityFrame.inverse;
            Vector3 rightShankBeltPulleyPositionFrame0 =
                transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(
                    transformationFromViveReferenceTrackerLeftHandedToUnityFrame.MultiplyPoint3x4(
                        rightShankBeltPulleyPositionViveFrame));

            // Get attachment point in frame 0
            Vector3 rightShankAttachmentPosInBeltFrame = rightShankBeltAttachmentPointsBeltFrame[0];
            Matrix4x4 transformationMatrixRightShankViveTrackerToRefTrackerLeftHandedFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixRightShankViveTrackerToRefTrackerLeftHandedFrame();
            GameObject rightShankTracker = viveTrackerDataManagerScript.rightShankViveTracker;
            Vector3 rightShankAttachmentPosInUnityFrame =
                rightShankTracker.transform.TransformPoint(rightShankAttachmentPosInBeltFrame / rightShankTracker.transform.localScale.x);
            rightShankAttachmentPosInFrame0 =
                transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(rightShankAttachmentPosInUnityFrame);

            // Get right shank belt center in frame 0
            Vector3 shankBeltCenterInFrame0 = viveTrackerDataManagerScript.GetRightShankBeltCenterPositionInFrame0();

            // Pass the belt center, pulley pos, and attachment point (all in frame 0) to our function to build S matrix
            (rightShankColumnsOfForceStructureMatrix, rightShankColumnsOfTorqueStructureMatrix) = ConstructStructureMatrixInVive(shankBeltCenterInFrame0,
                new Vector3[] { rightShankBeltPulleyPositionFrame0 },
                new Vector3[] { rightShankAttachmentPosInFrame0 });

            rightShankPulleyPos = rightShankBeltPulleyPositionFrame0;
        }

        return (rightShankPulleyPos, rightShankAttachmentPosInFrame0);
    }


    private (Vector3, Vector3) ComputeAndStoreLeftShankBeltStructureMatrix()
    {

        Vector3 leftShankPulleyPos = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 leftShankAttachmentPosInFrame0 = new Vector3();
        if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViconOnly)
        {

            Matrix4x4 transformationMatrixLeftShankBeltToViconThisFrame =
                ConstructTransformationMatrixFromLeftShankBeltFrameToViconFrame();

            // 2.) "Reconstruct" temporary marker positions by converting their stored belt frame positions
            // to Vicon frame (using this frame's belt transformation matrix)
            // For each attachment point
            for (int attachmentPointIndex = 0;
                 attachmentPointIndex < leftShankBeltAttachmentPointsViconFrameThisFrame.Length;
                 attachmentPointIndex++)
            {
                // Convert the position from trunk belt frame to Vicon frame and store
                leftShankBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex] =
                    transformationMatrixLeftShankBeltToViconThisFrame.MultiplyPoint3x4(
                        leftShankBeltAttachmentPointsBeltFrame[attachmentPointIndex]);

                // Testing only
                /*Debug.Log("Trunk belt attachemnt in belt frame at ( " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].x +
                    ", " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].y +
                    ", " + pelvicBeltAttachmentPointsBeltFrame[attachmentPointIndex].z + ")" +
                    "and the trunk belt attachment in Vicon frame is at ( " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].x +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].y +
                    ", " + trunkBeltAttachmentPointsViconFrameThisFrame[attachmentPointIndex].z + ")"); */
            }

            // 3.) Retrieve the current trunk belt center position from the COM manager
            Vector3 mostRecentLeftShankBeltCenterPosition = GetMostRecentLeftShankBeltCenterPositionInViconFrame();

            // 4.) Build the left shank belt structure matrix for this frame
/*            Debug.Log("When building left shank belt structure matrix, used Vicon pulley pos.: (" +
                      leftShankBeltPulleyPositionsViconFrame[0].x + ", " +
                      leftShankBeltPulleyPositionsViconFrame[0].y + ", " + leftShankBeltPulleyPositionsViconFrame[0].z +
                      ") and Vicon attachment point pos.: (" +
                      leftShankBeltAttachmentPointsViconFrameThisFrame[0].x + ", " +
                      leftShankBeltAttachmentPointsViconFrameThisFrame[0].y + ", " +
                      leftShankBeltAttachmentPointsViconFrameThisFrame[0].z + ").");*/
            (leftShankColumnsOfForceStructureMatrix, leftShankColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrix(mostRecentLeftShankBeltCenterPosition, leftShankBeltPulleyPositionsViconFrame,
                    leftShankBeltAttachmentPointsViconFrameThisFrame);
            
            leftShankPulleyPos = leftShankBeltPulleyPositionsViconFrame[0];

        }
        else if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViveOnly)
        {
            // Transform the left shank pulley position from Vive ref left-handed frame (in csv file) to frame 0
            Vector3 leftShankBeltPulleyPositionViveFrame = leftShankBeltPulleyPositionsViveFrame[0];
            Matrix4x4 transformationFromViveReferenceTrackerLeftHandedToUnityFrame = viveTrackerDataManagerScript
                .GetTransformationFromViveReferenceTrackerLeftHandedToUnityFrame();
            Matrix4x4 transformationMatrixFromFrame0ToUnityFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnityFrame.inverse;
            Vector3 leftShankBeltPulleyPositionFrame0 =
                transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(
                    transformationFromViveReferenceTrackerLeftHandedToUnityFrame.MultiplyPoint3x4(
                        leftShankBeltPulleyPositionViveFrame));

            // Get attachment point in frame 0
            Vector3 leftShankAttachmentPosInBeltFrame = leftShankBeltAttachmentPointsBeltFrame[0];
            Matrix4x4 transformationMatrixLeftShankViveTrackerToRefTrackerLeftHandedFrame =
                viveTrackerDataManagerScript.GetTransformationMatrixLeftShankViveTrackerToRefTrackerLeftHandedFrame();
            GameObject leftShankTracker = viveTrackerDataManagerScript.leftShankViveTracker;
            Vector3 leftShankAttachmentPosInUnityFrame =
                leftShankTracker.transform.TransformPoint(leftShankAttachmentPosInBeltFrame / leftShankTracker.transform.localScale.x);
            leftShankAttachmentPosInFrame0 =
                transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(leftShankAttachmentPosInUnityFrame);

            // Get left shank belt center in frame 0
            Vector3 shankBeltCenterInFrame0 = viveTrackerDataManagerScript.GetLeftShankBeltCenterPositionInFrame0();

            // Pass the belt center, pulley pos, and attachment point (all in frame 0) to our function to build S matrix
            (leftShankColumnsOfForceStructureMatrix, leftShankColumnsOfTorqueStructureMatrix) =
                ConstructStructureMatrixInVive(shankBeltCenterInFrame0,
                    new Vector3[] { leftShankBeltPulleyPositionFrame0 },
                    new Vector3[] { leftShankAttachmentPosInFrame0 });
            leftShankPulleyPos = leftShankBeltPulleyPositionFrame0;
        }

        return (leftShankPulleyPos, leftShankAttachmentPosInFrame0);
    }



    // Function to construct trunk belt local frame and return the transformation matrix
    // from Vicon frame to trunk belt frame.
    // Note, the x-axis points roughly to the subject's right, the z-axis points roughly to subject's forward, 
    // and the y-axis points roughly downwards towards the feet.
    private Matrix4x4 ConstructTransformationMatrixFromTrunkBeltFrameToViconFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 trunkBeltBackCenterMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltBackCenterMarker);
        (_, Vector3 trunkBeltBackRightMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltBackRightMarker);
        (_, Vector3 trunkBeltBackLeftMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltBackLeftMarker);

        // Define the coordinate system origin and axes
        // The x-axis will go from the back center marker to the back right marker. 
        // The z-axis will point roughly forwards. 
        // The y-axis will form a right-handed coordinate system, i.e. point down and left relative to subject's perspective
        Vector3 positionOfLocalFrameOriginInViconCoordinates = trunkBeltBackCenterMarkerPos;
        Vector3 localFrameXAxis = trunkBeltBackRightMarkerPos - trunkBeltBackLeftMarkerPos; //positive superiorly
        Vector3 localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, (trunkBeltBackLeftMarkerPos - trunkBeltBackCenterMarkerPos)); //positive anteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
        Vector3 localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXAxis); //positive to subject's left

        //normalize the axes
        localFrameXAxis.Normalize();
        localFrameYAxis.Normalize();
        localFrameZAxis.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

        // We don't need the inverse of the transformation, but I'll leave the code here just in case.
        //Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        // Return the transformation matrix
        return transformationMatrixLocalToVicon;
    }
    
    // Function to construct trunk belt local frame and return the transformation matrix
    // from Vicon frame to trunk belt frame.
    // Note, the x-axis points roughly to the subject's right, the z-axis points roughly to subject's forward, 
    // and the y-axis points roughly downwards towards the feet.
    private Matrix4x4 ConstructTransformationMatrixFromPelvicBeltFrameToViconFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 pelvicBeltBackCenterMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackCenterMarker);
        (_, Vector3 pelvicBeltBackRightMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackRightMarker);
        (_, Vector3 pelvicBeltBackLeftMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackLeftMarker);

        // Define the coordinate system origin and axes
        // The x-axis will go from the back center marker to the back right marker. 
        // The z-axis will point roughly forwards. 
        // The y-axis will form a right-handed coordinate system, i.e. point down and left relative to subject's perspective
        Vector3 positionOfLocalFrameOriginInViconCoordinates = pelvicBeltBackCenterMarkerPos;
        Vector3 localFrameXAxis = pelvicBeltBackRightMarkerPos - pelvicBeltBackCenterMarkerPos; //positive superiorly
        Vector3 localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, (pelvicBeltBackLeftMarkerPos - pelvicBeltBackCenterMarkerPos)); //positive posteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
        Vector3 localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXAxis); //positive to subject's left
        localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, (pelvicBeltBackLeftMarkerPos - pelvicBeltBackCenterMarkerPos)); //positive posteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...

        //normalize the axes
        localFrameXAxis.Normalize();
        localFrameYAxis.Normalize();
        localFrameZAxis.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

        return transformationMatrixLocalToVicon;
    }

    public List<Vector3> GetPelvicBeltPulleyPositionInFrame0()
    {

        return pelvicPulleyPositionInFrame0ForStore;
    }

    public List<Vector3> GetPelvisBeltAttachmentPointInFrame0()
    {
        return pelvicBeltAttachmentPointsFrame0ForStore;
    }

    // Since the pelvic belt can have more cables than attachments (typically, 8 cables and 4 attachment points), 
    // we currently ASSUME the first 4 elements only are the unique attachments
    public List<Vector3> GetUniquePelvisBeltAttachmentPointInFrame0()
    {
        // First argument is index, second argument is number of elements to retrieve
        return pelvicBeltAttachmentPointsFrame0ForStore.GetRange(0, 4);
    }

    public List<Vector3> GetChestBeltPulleyPositionInFrame0()
    {
        return chestPulleyPositionInFrame0ForStore;
    }

    public List<Vector3> GetChestBeltAttachmentPointInFrame0()
    {
        return chestBeltAttachmentPointsFrame0ForStore;
    }

    public List<Vector3> GetLeftAndRightShankBeltPulleyPositionInFrame0()
    {
        // Order is left shank pulley, right shank pulley
        return shankPulleyPositionInFrame0ForStore;
    }
    
    public List<Vector3> GetLeftAndRightShankAttachmentPositionInFrame0()
    {
        // Order is left shank pulley, right shank pulley
        return shankAttachmentPointPositionInFrame0ForStore;
    }
    
    
private Matrix4x4 ConstructTransformationMatrixFromPelvicBeltFrameToViveFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 pelvicBeltBackCenterMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackCenterMarker);
        (_, Vector3 pelvicBeltBackRightMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackRightMarker);
        (_, Vector3 pelvicBeltBackLeftMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackLeftMarker);

        // Define the coordinate system origin and axes
        // The x-axis will go from the back center marker to the back right marker. 
        // The z-axis will point roughly forwards. 
        // The y-axis will form a right-handed coordinate system, i.e. point down and left relative to subject's perspective
        Vector3 positionOfLocalFrameOriginInViconCoordinates = pelvicBeltBackCenterMarkerPos;
        Vector3 localFrameXAxis = pelvicBeltBackRightMarkerPos - pelvicBeltBackCenterMarkerPos; //positive superiorly
        Vector3 localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, (pelvicBeltBackLeftMarkerPos - pelvicBeltBackCenterMarkerPos)); //positive posteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
        Vector3 localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXAxis); //positive to subject's left
        localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, (pelvicBeltBackLeftMarkerPos - pelvicBeltBackCenterMarkerPos)); //positive posteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...

        //normalize the axes
        localFrameXAxis.Normalize();
        localFrameYAxis.Normalize();
        localFrameZAxis.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

        return transformationMatrixLocalToVicon;
    }
    private Matrix4x4 ConstructTransformationMatrixFromRightShankBeltFrameToViconFrame()
    {
        // Get origin of the right shank as midpoint of two malleoli markers
        (_, Vector3 ankleMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightAnkleMarkerName);
        (_, Vector3 ankleMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightAnkleMedialMarkerName);
        Vector3 shankFrameOrigin = (ankleMarkerPos + ankleMedialMarkerPos) / (2.0f);

        // Get the x-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 shankXAxis = ankleMedialMarkerPos - ankleMarkerPos;

        // Get the y-axis of the shank = medially directed vector from lateral to medial malleolus
        (_, Vector3 kneeMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMarkerName);
        (_, Vector3 kneeMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMedialMarkerName);
        Vector3 midKneePos = (kneeMarkerPos + kneeMedialMarkerPos) / (2.0f);
        Vector3 shankYAxis = midKneePos - shankFrameOrigin;

        // For the right shank, z-axis is x-axis cross y-axis
        Vector3 shankZAxis = getRightHandedCrossProduct(shankXAxis, shankYAxis);

        // Recompute the shank y-axis as z-axis cross x-axis
        shankYAxis = getRightHandedCrossProduct(shankZAxis, shankXAxis);

        // normalize the axes
        shankXAxis.Normalize();
        shankYAxis.Normalize();
        shankZAxis.Normalize();

        //get rotation and translation from local frame to global Vicon frame by passing in the local frame vectors and origin expressed in Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(shankXAxis, shankYAxis, shankZAxis, shankFrameOrigin);

        return transformationMatrixLocalToVicon;
    }

    private Matrix4x4 ConstructTransformationMatrixFromRightShankBeltFrameToViveFrame()
    {
        // Get origin of the right shank as midpoint of two malleoli markers
        (_, Vector3 ankleMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightAnkleMarkerName);
        (_, Vector3 ankleMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightAnkleMedialMarkerName);
        Vector3 shankFrameOrigin = (ankleMarkerPos + ankleMedialMarkerPos) / (2.0f);

        // Get the x-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 shankXAxis = ankleMedialMarkerPos - ankleMarkerPos;

        // Get the y-axis of the shank = medially directed vector from lateral to medial malleolus
        (_, Vector3 kneeMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMarkerName);
        (_, Vector3 kneeMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMedialMarkerName);
        Vector3 midKneePos = (kneeMarkerPos + kneeMedialMarkerPos) / (2.0f);
        Vector3 shankYAxis = midKneePos - shankFrameOrigin;

        // For the right shank, z-axis is x-axis cross y-axis
        Vector3 shankZAxis = getRightHandedCrossProduct(shankXAxis, shankYAxis);

        // Recompute the shank y-axis as z-axis cross x-axis
        shankYAxis = getRightHandedCrossProduct(shankZAxis, shankXAxis);

        // normalize the axes
        shankXAxis.Normalize();
        shankYAxis.Normalize();
        shankZAxis.Normalize();

        //get rotation and translation from local frame to global Vicon frame by passing in the local frame vectors and origin expressed in Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(shankXAxis, shankYAxis, shankZAxis, shankFrameOrigin);

        return transformationMatrixLocalToVicon;
    }
    private Matrix4x4 ConstructTransformationMatrixFromLeftShankBeltFrameToViconFrame()
    {
        // Get origin of the left shank as midpoint of two malleoli markers
        (_, Vector3 ankleMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftAnkleMarkerName);
        (_, Vector3 ankleMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftAnkleMedialMarkerName);
        Vector3 shankFrameOrigin = (ankleMarkerPos + ankleMedialMarkerPos) / (2.0f);

        // Get the x-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 shankXAxis = ankleMarkerPos - ankleMedialMarkerPos;
        // Normalize x-axis
        shankXAxis = shankXAxis / shankXAxis.magnitude;

        // Get the y-axis of the shank = medially directed vector from lateral to medial malleolus
        (_, Vector3 kneeMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMarkerName);
        (_, Vector3 kneeMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMedialMarkerName);
        Vector3 midKneePos = (kneeMarkerPos + kneeMedialMarkerPos) / (2.0f);
        Vector3 shankYAxis = midKneePos - shankFrameOrigin;
        // Normalize y-axis
        shankYAxis = shankYAxis / shankYAxis.magnitude;

        // For the left shank, z-axis is x-axis cross y-axis
        Vector3 shankZAxis = getRightHandedCrossProduct(shankXAxis, shankYAxis);
        // Normalize y-axis
        shankZAxis = shankZAxis / shankZAxis.magnitude;

        // Recompute the shank y-axis as z-axis cross x-axis
        shankYAxis = getRightHandedCrossProduct(shankZAxis, shankXAxis);
        // Normalize y-axis
        shankYAxis = shankYAxis / shankYAxis.magnitude;

        //get rotation and translation from local frame to global Vicon frame by passing in the local frame vectors and origin expressed in Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(shankXAxis, shankYAxis, shankZAxis, shankFrameOrigin);

        return transformationMatrixLocalToVicon;
    }
private Matrix4x4 ConstructTransformationMatrixFromLeftShankBeltFrameToViveFrame()
    {
        // Get origin of the left shank as midpoint of two malleoli markers
        (_, Vector3 ankleMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftAnkleMarkerName);
        (_, Vector3 ankleMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftAnkleMedialMarkerName);
        Vector3 shankFrameOrigin = (ankleMarkerPos + ankleMedialMarkerPos) / (2.0f);

        // Get the x-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 shankXAxis = ankleMarkerPos - ankleMedialMarkerPos;
        // Normalize x-axis
        shankXAxis = shankXAxis / shankXAxis.magnitude;

        // Get the y-axis of the shank = medially directed vector from lateral to medial malleolus
        (_, Vector3 kneeMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMarkerName);
        (_, Vector3 kneeMedialMarkerPos) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMedialMarkerName);
        Vector3 midKneePos = (kneeMarkerPos + kneeMedialMarkerPos) / (2.0f);
        Vector3 shankYAxis = midKneePos - shankFrameOrigin;
        // Normalize y-axis
        shankYAxis = shankYAxis / shankYAxis.magnitude;

        // For the left shank, z-axis is x-axis cross y-axis
        Vector3 shankZAxis = getRightHandedCrossProduct(shankXAxis, shankYAxis);
        // Normalize y-axis
        shankZAxis = shankZAxis / shankZAxis.magnitude;

        // Recompute the shank y-axis as z-axis cross x-axis
        shankYAxis = getRightHandedCrossProduct(shankZAxis, shankXAxis);
        // Normalize y-axis
        shankYAxis = shankYAxis / shankYAxis.magnitude;

        //get rotation and translation from local frame to global Vicon frame by passing in the local frame vectors and origin expressed in Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(shankXAxis, shankYAxis, shankZAxis, shankFrameOrigin);

        return transformationMatrixLocalToVicon;
    }
    private Vector3 GetMostRecentTrunkBeltCenterPositionInViconFrame()
    {
        // Get the position of the two front markers on the trunk belt and the back center marker
        (_, Vector3 trunkBeltBackCenterMarkerPositionViconFrame) = 
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltBackCenterMarker);
        (_, Vector3 trunkBeltFrontLeftMarkerPositionViconFrame) = 
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltFrontLeftMarker);
        (_, Vector3 trunkBeltFrontRightMarkerPositionViconFrame) = 
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltFrontRightMarker);

        // Compute the midpoint of the two front markers
        Vector3 midpointFrontTrunkBelt = (trunkBeltFrontLeftMarkerPositionViconFrame + trunkBeltFrontRightMarkerPositionViconFrame) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        Vector3 midpointTrunkBelt = (trunkBeltBackCenterMarkerPositionViconFrame + midpointFrontTrunkBelt) / 2.0f;

        // Return the belt midpoint
        return midpointTrunkBelt;

    }

    
    private Vector3 GetMostRecentTrunkBeltCenterPositionInViveFrame()
    {
        // Get the position of the two front markers on the trunk belt and the back center marker
        (_, Vector3 trunkBeltBackCenterMarkerPositionViconFrame) = 
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltBackCenterMarker);
        (_, Vector3 trunkBeltFrontLeftMarkerPositionViconFrame) = 
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltFrontLeftMarker);
        (_, Vector3 trunkBeltFrontRightMarkerPositionViconFrame) = 
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(trunkBeltFrontRightMarker);

        // Compute the midpoint of the two front markers
        Vector3 midpointFrontTrunkBelt = (trunkBeltFrontLeftMarkerPositionViconFrame + trunkBeltFrontRightMarkerPositionViconFrame) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        Vector3 midpointTrunkBelt = (trunkBeltBackCenterMarkerPositionViconFrame + midpointFrontTrunkBelt) / 2.0f;

        // Return the belt midpoint
        return midpointTrunkBelt;

    }
    private Vector3 GetMostRecentPelvicBeltCenterPositionInViconFrame()
    {
        // Get the position of the two front markers on the trunk belt and the back center marker
        (_, Vector3 pelvicBeltBackCenterMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackCenterMarker);
        (_, Vector3 pelvicBeltFrontLeftMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltFrontLeftMarker);
        (_, Vector3 pelvicBeltFrontRightMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltFrontRightMarker);

        // Compute the midpoint of the two front markers
        Vector3 midpointFrontPelvicBelt = (pelvicBeltFrontLeftMarkerPositionViconFrame + pelvicBeltFrontRightMarkerPositionViconFrame) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        Vector3 midpointPelvicBelt = (pelvicBeltBackCenterMarkerPositionViconFrame + midpointFrontPelvicBelt) / 2.0f;

        // Return the belt midpoint
        return midpointPelvicBelt;

    }
    
    private Vector3 GetMostRecentPelvicBeltCenterPositionInViveFrame()
    {
        // Get the position of the two front markers on the trunk belt and the back center marker
        (_, Vector3 pelvicBeltBackCenterMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltBackCenterMarker);
        (_, Vector3 pelvicBeltFrontLeftMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltFrontLeftMarker);
        (_, Vector3 pelvicBeltFrontRightMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(pelvicBeltFrontRightMarker);

        // Compute the midpoint of the two front markers
        Vector3 midpointFrontPelvicBelt = (pelvicBeltFrontLeftMarkerPositionViconFrame + pelvicBeltFrontRightMarkerPositionViconFrame) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        Vector3 midpointPelvicBelt = (pelvicBeltBackCenterMarkerPositionViconFrame + midpointFrontPelvicBelt) / 2.0f;

        // Return the belt midpoint
        return midpointPelvicBelt;

    }


    public Vector3 GetMostRecentRightShankBeltCenterPositionInViconFrame()
    {
        // Get the position of the cable attachment for the shank belt and the tibial tuberosity
        Vector3 rightShankCableAttachmentPoint = rightShankBeltAttachmentPointsViconFrameThisFrame[0]; // the first (and only) right shank attachment point is on the back of the shank
        (_, Vector3 rightTibialTubMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightTibialTuberosityMarkerName);

        // Approximate the right shank belt center z-axis (vertical) position as the midpoint of the shank cable attachment and the tibial tuberosity.
        Vector3 roughMidpointShankBelt = (rightShankCableAttachmentPoint + rightTibialTubMarkerPositionViconFrame) / 2.0f;

        // Use the x-axis and y-axis positions of the knee center
        (_, Vector3 rightKneeMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMarkerName);
        (_, Vector3 rightKneeMedialMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMedialMarkerName);
        Vector3 kneeCenter = (rightKneeMarkerPositionViconFrame + rightKneeMedialMarkerPositionViconFrame) / 2.0f;
        roughMidpointShankBelt.x = kneeCenter.x;
        roughMidpointShankBelt.y = kneeCenter.y;

        // Return rough shank betl center
        return roughMidpointShankBelt;
    }

    public Vector3 GetMostRecentRightShankBeltCenterPositionInViveFrame()
    {
        // Get the position of the cable attachment for the shank belt and the tibial tuberosity
        Vector3 rightShankCableAttachmentPoint = rightShankBeltAttachmentPointsViconFrameThisFrame[0]; // the first (and only) right shank attachment point is on the back of the shank
        (_, Vector3 rightTibialTubMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightTibialTuberosityMarkerName);

        // Approximate the right shank belt center z-axis (vertical) position as the midpoint of the shank cable attachment and the tibial tuberosity.
        Vector3 roughMidpointShankBelt = (rightShankCableAttachmentPoint + rightTibialTubMarkerPositionViconFrame) / 2.0f;

        // Use the x-axis and y-axis positions of the knee center
        (_, Vector3 rightKneeMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMarkerName);
        (_, Vector3 rightKneeMedialMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(rightKneeMedialMarkerName);
        Vector3 kneeCenter = (rightKneeMarkerPositionViconFrame + rightKneeMedialMarkerPositionViconFrame) / 2.0f;
        roughMidpointShankBelt.x = kneeCenter.x;
        roughMidpointShankBelt.y = kneeCenter.y;

        // Return rough shank betl center
        return roughMidpointShankBelt;
    }
    
    public Vector3 GetMostRecentLeftShankBeltCenterPositionInViconFrame()
    {
        // Get the position of the cable attachment for the shank belt and the tibial tuberosity
        Vector3 leftShankCableAttachmentPoint = leftShankBeltAttachmentPointsViconFrameThisFrame[0]; // the first (and only) left shank attachment point is on the back of the shank
        (_, Vector3 leftTibialTubMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftTibialTuberosityMarkerName);

        // Approximate the left shank belt center z-axis (vertical) position as the midpoint of the shank cable attachment and the tibial tuberosity.
        Vector3 roughMidpointShankBelt = (leftShankCableAttachmentPoint + leftTibialTubMarkerPositionViconFrame) / 2.0f;

        // Use the x-axis and y-axis positions of the knee center
        (_, Vector3 leftKneeMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMarkerName);
        (_, Vector3 leftKneeMedialMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMedialMarkerName);
        Vector3 kneeCenter = (leftKneeMarkerPositionViconFrame + leftKneeMedialMarkerPositionViconFrame) / 2.0f;
        roughMidpointShankBelt.x = kneeCenter.x;
        roughMidpointShankBelt.y = kneeCenter.y;

        // Return rough shank betl center
        return roughMidpointShankBelt;
    }
    public Vector3 GetMostRecentLeftShankBeltCenterPositionInViveFrame()
    {
        // Get the position of the cable attachment for the shank belt and the tibial tuberosity
        Vector3 leftShankCableAttachmentPoint = leftShankBeltAttachmentPointsViconFrameThisFrame[0]; // the first (and only) left shank attachment point is on the back of the shank
        (_, Vector3 leftTibialTubMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftTibialTuberosityMarkerName);

        // Approximate the left shank belt center z-axis (vertical) position as the midpoint of the shank cable attachment and the tibial tuberosity.
        Vector3 roughMidpointShankBelt = (leftShankCableAttachmentPoint + leftTibialTubMarkerPositionViconFrame) / 2.0f;

        // Use the x-axis and y-axis positions of the knee center
        (_, Vector3 leftKneeMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMarkerName);
        (_, Vector3 leftKneeMedialMarkerPositionViconFrame) =
            centerOfMassManagerScript.GetMostRecentMarkerPositionByName(leftKneeMedialMarkerName);
        Vector3 kneeCenter = (leftKneeMarkerPositionViconFrame + leftKneeMedialMarkerPositionViconFrame) / 2.0f;
        roughMidpointShankBelt.x = kneeCenter.x;
        roughMidpointShankBelt.y = kneeCenter.y;

        // Return rough shank betl center
        return roughMidpointShankBelt;
    }


    private (Vector3[], Vector3[]) ConstructStructureMatrix(Vector3 centerOfEndEffectorViconFrame,
    Vector3[] pulleyPositionsViconFrame, Vector3[] correspondingAttachmentPointsViconFrame)
    {
        // The first three rows of the structure matrix relate cable tensions to force applied to the "end-effector"/body segment. 
        // These rows are simply unit vectors pointing from the cable attachment point to the pulley. 
        Vector3[] structureMatrixForceRows = new Vector3[pulleyPositionsViconFrame.Length]; // Each entry can be thought of as a 3x1 column
        // The last three rows of the structure matrix relate cable tensions to torque applied to the "end-effector"/body segment. 
        // These rows are the cross product of the vector from the end-effector center to the point of cable attachment AND the unit vector from 
        // the cable attachment point towards the pulley.
        Vector3[] structureMatrixTorqueRows = new Vector3[pulleyPositionsViconFrame.Length]; // Each entry can be thought of as a 3x1 column

        // For each pulley/cable (or, each column of the structure matrix)
        for (int pulleyIndex = 0; pulleyIndex < pulleyPositionsViconFrame.Length; pulleyIndex++)
        {
            // Compute the corresponding column of the force structure matrix as pulley location minus attachment location (in Vicon frame)
            Vector3 unitVectorCableAttachmentToPulley = (pulleyPositionsViconFrame[pulleyIndex] -
                correspondingAttachmentPointsViconFrame[pulleyIndex]).normalized;
            structureMatrixForceRows[pulleyIndex] = unitVectorCableAttachmentToPulley;

            // Compute the corresponding column of the torque structure matrix (see description above)
            // First get the vector from end effector center to the cable attachment point
            // Note: we convert from millimeters (Vicon native distance unit) to meters. As a result, if cable tensions are in N, 
            // then multiplication with the torque rows will produce units of N*m.
            Vector3 vectorEndEffectorCenterToCableAttachment = (correspondingAttachmentPointsViconFrame[pulleyIndex] -
                centerOfEndEffectorViconFrame) / convertMetersToMillimeters;
            // Then compute the column of the torque matrix
            structureMatrixTorqueRows[pulleyIndex] = getRightHandedCrossProduct(vectorEndEffectorCenterToCableAttachment, unitVectorCableAttachmentToPulley);

            // DEBUG ONLY: print the column computed
            /*Debug.Log("Structure matrix column " + pulleyIndex + " has force components (x,y,z): " +
                structureMatrixForceRows[pulleyIndex].x + ", " + structureMatrixForceRows[pulleyIndex].y + ", " +
                structureMatrixForceRows[pulleyIndex].z + ")");
            Debug.Log("Structure matrix column " + pulleyIndex + " has torque components (x,y,z): " +
                structureMatrixTorqueRows[pulleyIndex].x + ", " + structureMatrixTorqueRows[pulleyIndex].y + ", " +
                structureMatrixTorqueRows[pulleyIndex].z + ")");*/
        }

        // Eventually, we can return the structure matrix or store it in instance variables
        return (structureMatrixForceRows, structureMatrixTorqueRows);
    }
 private (Vector3[], Vector3[]) ConstructStructureMatrixInVive(Vector3 centerOfEndEffectorViveFrame,
    Vector3[] pulleyPositionsViveFrame, Vector3[] correspondingAttachmentPointsViveFrame)
    {
        // The first three rows of the structure matrix relate cable tensions to force applied to the "end-effector"/body segment. 
        // These rows are simply unit vectors pointing from the cable attachment point to the pulley. 
        Vector3[] structureMatrixForceRows = new Vector3[pulleyPositionsViveFrame.Length]; // Each entry can be thought of as a 3x1 column
        // The last three rows of the structure matrix relate cable tensions to torque applied to the "end-effector"/body segment. 
        // These rows are the cross product of the vector from the end-effector center to the point of cable attachment AND the unit vector from 
        // the cable attachment point towards the pulley.
        Vector3[] structureMatrixTorqueRows = new Vector3[pulleyPositionsViveFrame.Length]; // Each entry can be thought of as a 3x1 column

        // For each pulley/cable (or, each column of the structure matrix)
        for (int pulleyIndex = 0; pulleyIndex < pulleyPositionsViveFrame.Length; pulleyIndex++)
        {
            // Compute the corresponding column of the force structure matrix as pulley location minus attachment location (in Vicon frame)
            Vector3 unitVectorCableAttachmentToPulley = (pulleyPositionsViveFrame[pulleyIndex] -
                correspondingAttachmentPointsViveFrame[pulleyIndex]).normalized;
            structureMatrixForceRows[pulleyIndex] = unitVectorCableAttachmentToPulley;

            // Compute the corresponding column of the torque structure matrix (see description above)
            // First get the vector from end effector center to the cable attachment point
            // Note: we convert from millimeters (Vicon native distance unit) to meters. As a result, if cable tensions are in N, 
            // then multiplication with the torque rows will produce units of N*m.
            Vector3 vectorEndEffectorCenterToCableAttachment = (correspondingAttachmentPointsViveFrame[pulleyIndex] -
                centerOfEndEffectorViveFrame);
            // Then compute the column of the torque matrix
            structureMatrixTorqueRows[pulleyIndex] = getRightHandedCrossProduct(vectorEndEffectorCenterToCableAttachment, unitVectorCableAttachmentToPulley);

            // DEBUG ONLY: print the column computed
            /*Debug.Log("Structure matrix column " + pulleyIndex + " has force components (x,y,z): " +
                structureMatrixForceRows[pulleyIndex].x + ", " + structureMatrixForceRows[pulleyIndex].y + ", " +
                structureMatrixForceRows[pulleyIndex].z + ")");
            Debug.Log("Structure matrix column " + pulleyIndex + " has torque components (x,y,z): " +
                structureMatrixTorqueRows[pulleyIndex].x + ", " + structureMatrixTorqueRows[pulleyIndex].y + ", " +
                structureMatrixTorqueRows[pulleyIndex].z + ")");*/
        }

        // Eventually, we can return the structure matrix or store it in instance variables
        return (structureMatrixForceRows, structureMatrixTorqueRows);
    }


    private void SortLoadedSetupData()
    {
        // For the list of trunk pulleys and attachments, load (X,Y,Z) position data for each pulley and store as a Vector3[]
        if (usingChestBelt)
        {
            if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViconOnly)
            {
                trunkBeltPulleyPositionsViconFrame = LoadPositionDataXyzForListedPoints(orderedTrunkBeltPulleyNames);
                trunkBeltAttachmentPointsBeltFrame = LoadPositionDataXyzForListedPoints(orderedTrunkBeltAttachmentPointNames);
            }
            else if(KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViveOnly)
            {
                trunkBeltPulleyPositionsViveFrame = LoadPositionDataXyzForListedPoints(orderedTrunkBeltPulleyNames);
                trunkBeltAttachmentPointsBeltFrame = LoadPositionDataXyzForListedPoints(orderedTrunkBeltAttachmentPointNames);
            }


        }

        // Can load pelvis pulleys and attachments here, if using
        if (usingPelvicBelt)
        {
            
            pelvicBeltPulleyPositionsViveFrame = LoadPositionDataXyzForListedPoints(orderedPelvicBeltPulleyNames);
            
            //Debug.Log(" the pelvicBeltPulleyPositionsViveFrame in loded data is " + pelvicBeltPulleyPositionsViveFrame);
            pelvicBeltAttachmentPointsBeltFrame = LoadPositionDataXyzForListedPoints(orderedPelvicBeltAttachmentPointNames);
            
        }

        if (usingShankBelts)
        {
            // Load right shank cable attachment points in belt frame (or vicon frame, if using Vicon)
            rightShankBeltPulleyPositionsViconFrame = LoadPositionDataXyzForListedPoints(orderedRightShankBeltPulleyNames);
            rightShankBeltAttachmentPointsBeltFrame = LoadPositionDataXyzForListedPoints(orderedRightShankBeltAttachmentPointNames);

            // Load left shank cable attachment points in belt frame (or vicon frame, if using Vicon)
            leftShankBeltPulleyPositionsViconFrame = LoadPositionDataXyzForListedPoints(orderedLeftShankBeltPulleyNames);
            leftShankBeltAttachmentPointsBeltFrame = LoadPositionDataXyzForListedPoints(orderedLeftShankBeltAttachmentPointNames);
            
            // Load left shank pulley position in Vive ref frame (or Vicon frame, if using Vicon)
            rightShankBeltPulleyPositionsViveFrame =
                LoadPositionDataXyzForListedPoints(orderedRightShankBeltPulleyNames);

            // Load right shank pulley position in Vive ref frame (or Vicon frame, if using Vicon)
            leftShankBeltPulleyPositionsViveFrame =
                LoadPositionDataXyzForListedPoints(orderedLeftShankBeltPulleyNames);

        }

        // Print that we successfully sorted the data
        Debug.Log("Successfully sorted structure matrix data (review error log for any missed var names).");

    }


    private Vector3[] LoadPositionDataXyzForListedPoints(string[] namesOfPointsToLoadPositionDataFor)
    {
        
        List<Vector3> loadedPositionsList = new List<Vector3>();
        // Initialize storage. Each named point will produce a corresponding Vector3 of its position, loaded from the setup data
        Vector3[] loadedPositions = new Vector3[namesOfPointsToLoadPositionDataFor.Length];
        //Debug.Log(" the loadedPositions is "+ loadedPositions);
        // For each named point we want to load position data for
        for(int nameIndex = 0; nameIndex < namesOfPointsToLoadPositionDataFor.Length; nameIndex++)
        {
            // Get the name of the x-axis position data column for the desired point
            string nameOfPositionColumn = namesOfPointsToLoadPositionDataFor[nameIndex] + "_POS_X";
            //Debug.Log(" the nameOfPositionColumn is " + nameOfPositionColumn);
            // Load the x-axis position
            float xAxisPosition = LoadDesiredColumnNameFromSetupData(nameOfPositionColumn);
            //Debug.Log(" the xAxisPosition is " + xAxisPosition);
            // Get the name of the y-axis position data column for the desired point
            nameOfPositionColumn = namesOfPointsToLoadPositionDataFor[nameIndex] + "_POS_Y";
            // Load the y-axis position
            float yAxisPosition = LoadDesiredColumnNameFromSetupData(nameOfPositionColumn);

            // Get the name of the z-axis position data column for the desired point
            nameOfPositionColumn = namesOfPointsToLoadPositionDataFor[nameIndex] + "_POS_Z";
            // Load the z-axis position
            float zAxisPosition = LoadDesiredColumnNameFromSetupData(nameOfPositionColumn);

            // Store as Vector3
            
            
            loadedPositionsList.Add(new Vector3(xAxisPosition, yAxisPosition, zAxisPosition));
            //loadedPositions[nameIndex] = new Vector3(xAxisPosition, yAxisPosition, zAxisPosition);
            // Resize the array to increase its size by one
          
        }
        loadedPositions = loadedPositionsList.ToArray();

        // Return loaded positions
        return loadedPositions;
    }


    private float LoadDesiredColumnNameFromSetupData(string variableNameInSetupData)
    {
        // Get the index for the desired variable name/column name for the desired point
        (bool columnFound, int indexOfPositionColumn) = GetColumnIndexForNamedVariableInSetupData(variableNameInSetupData);
        
        // Get the desired variable from the (already loaded, but not sorted) setup file data
        float loadedData = 0.0f;
        if (columnFound)
        {
            loadedData = setupDataForComputingStructureMatrix[indexOfPositionColumn];
        }
        else
        {
            Debug.LogError("Desired variable name, " + variableNameInSetupData + ", does not exist in the structure matrix setup data file.");
        }

        // Return the loaded float data
        return loadedData;
    }


    private (bool, int) GetColumnIndexForNamedVariableInSetupData(string columnName)
    {
        // Initialize return values
        int columnIndex = -1;

        // See if the column name exists (using the Array.Exists() function and a lambda function
        bool desiredColumnNameExists = Array.Exists(setupDataHeadersForColumns, element => element == columnName);

        // If the column name exists
        if (desiredColumnNameExists)
        {
            // Get its index
            columnIndex = Array.IndexOf(setupDataHeadersForColumns, columnName);
        }

        // Return values as tuple
        return (desiredColumnNameExists, columnIndex);
    }



    private float[] loadStructureMatrixDataFromFile(string localPathToFolder, string keyword)
    {
        // Get all files in the directory
        string pathToFolder = getDirectoryPath() + localPathToFolder;
        Debug.Log("Trying to load subject setup data for belt Structure matrices from the path: " + pathToFolder);
        string[] allFiles = System.IO.Directory.GetFiles(pathToFolder);
        Debug.Log("Located the following number of data files from the specified S matrix data directory: " + allFiles.Length);

        // Get the name of the most recent setup for structure matrix data file (with a keyword, such as "No_Stim", if desired)
        string fileToUseName = "";
        DateTime dateTimeOfFileToUse = new DateTime();
        for (uint fileIndex = 0; fileIndex < allFiles.Length; fileIndex++)
        {
            //see if the file is an excursion performance summary file with the proper keywords
            string fileName = allFiles[fileIndex];
            bool fileNameOfASetupDataForStructureMatrixFile = new bool();
            if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViconOnly)
            {
                fileNameOfASetupDataForStructureMatrixFile = fileName.Contains(dataForStructureMatrixSummaryPrefix);
            }
            else if (KinematicModel.GetKinematicModelDataSourceSelector() == ModelDataSourceSelector.ViveOnly)
            {
                fileNameOfASetupDataForStructureMatrixFile = fileName.Contains(dataForViveBasedStructureMatrixSummaryPrefix);
            }
            
            bool hasKeyword = false;
            if (keyword != "") //if a keyword was specified
            {
                //see if the keyword is in the string
                hasKeyword = fileName.Contains(keyword);
            }
            else //if there is no keyword
            {
                //proceed as if the keyword were present
                hasKeyword = true;
            }

            //ensure that it is not a meta file
            bool isMetaFile = fileName.Contains("meta");

            //if the file is an excursion performance summary file with the correct keyword
            
            if (fileNameOfASetupDataForStructureMatrixFile && hasKeyword && !isMetaFile)
            {
                //if this is the first valid file we've found
                if (fileToUseName == "")
                {
                    fileToUseName = fileName;
                    dateTimeOfFileToUse = System.IO.File.GetCreationTime(fileName);
                }
                else //if we have already found a file name
                {
                    DateTime dateTimeOfCurrentFile = System.IO.File.GetCreationTime(fileName);
                    int isDateEarlierOrLater = DateTime.Compare(dateTimeOfCurrentFile, dateTimeOfFileToUse);
                    if (isDateEarlierOrLater > 0) //if the current file's date/time is the most recent one observed thus far
                    {
                        //store that one
                        fileToUseName = fileName;
                        dateTimeOfFileToUse = dateTimeOfCurrentFile;
                    }
                }
            }
        }

        Debug.Log("Loading from the following Data For Structure Matrix file path: " + fileToUseName);

        // Now that we have the file to use, read it in
        string allFileTextString = System.IO.File.ReadAllText(fileToUseName);
        //split into lines, delimited by the newline character
        // Note, Windows uses "\r\n", not just '\n'! 
        // To automatically retrieve the native new line character, use Environment.NewLine.ToCharArray()
        char[] separator = Environment.NewLine.ToCharArray(); //new char[] { '\n' };
        string[] rowsFromFile = allFileTextString.Split(separator, 2);
        //split first data row (second row) into cells/entries, delimited by commas
        separator = new char[] { ',' };
        setupDataHeadersForColumns = rowsFromFile[0].Split(separator, 100); // The "100" argument to string.Split specifies max number of returned substrings
        string[] firstDataRow = rowsFromFile[1].Split(separator, 100); // The "100" argument to string.Split specifies max number of returned substrings
        //Convert each string in the data row to a float
        float[] firstDataRowAsFloat = new float[firstDataRow.Length];
        for (uint entryIndex = 0; entryIndex < firstDataRow.Length; entryIndex++)
        {
            firstDataRowAsFloat[entryIndex] = float.Parse(firstDataRow[entryIndex], CultureInfo.InvariantCulture.NumberFormat);
        }

        //return the float array for the excursion performance summary
        return firstDataRowAsFloat;
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



    //Computes a right-handed cross-product so that we can work in the Vicon coordinate system
    private Vector3 getRightHandedCrossProduct(Vector3 leftVector, Vector3 rightVector)
    {
        float newXValue = leftVector.y * rightVector.z - leftVector.z * rightVector.y;
        float newYValue = leftVector.z * rightVector.x - leftVector.x * rightVector.z;
        float newZValue = leftVector.x * rightVector.y - leftVector.y * rightVector.x;

        return new Vector3(newXValue, newYValue, newZValue);

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

}
