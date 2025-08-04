// This script should act on the pre-recorded and pre-labeled (no missing markers) recording of a 
// subject wearing a pelvic belt and/or trunk belt. All relevant pulley pointers should be present. 

// VICON frame************************************************************************************************
// The script will 1.) compute pulley positions in Vicon frame and store them to file, and 2.) compute temporary
// belt attachment coordinates in the belt Vicon frame and store them in the same file. 
// Doing these 2 items allows us to read the stored file and compute the structure (S) matrix at any 
// given frame in the future.

// Vive frame************************************************************************************************
// THIS IS DONE ONLY IF WE SELECT "VIVE-ONLY" OR "VIVEANDVICON" IN THE SETTINGS SELECTOR. 
// Does the same 2 steps as in Vicon frame, but 1.) pulley positions relative to the Vive reference tracker and
// 2.) belt attachment coordinates in the belt Vive frame. 


#define ENABLE_CUSTOM_WARNINGS_ERRORS //recommended that this define is always present so we can see user-defined warnings and errors
#define ENABLE_LOGS //may want to comment out this define to suppress user-defined logging ("Debug Mode)")

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using UnityEngine.UI;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class CreateDataFileForSMatrixAllBelts : MonoBehaviour
{

    // The settings object for the structure matrix data computation. 
    // Can specify if we should create structure matrix files for 
    // 1.) Vicon-based control, 2.) Vive-based control, or 3.) both.
    public StructureMatrixSettingsScript structureMatrixSettingsScript;

    // We either use pelvic belt, trunk belt, shank belts, or some combination. The flags determine the skeleton used.
    public bool usingPelvicBelt;
    public bool usingTrunkBelt;
    public bool usingShankBelts;

    // subject-specific data
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data

    // The Vive structure matrix manager, if needed
    public CreateDataFileAndSMatrixViveBased viveStructureMatrixManager;

    // The skeleton name/type depends on the belts being used.
    // Could add functionality for a 3D force at the pelvis later.
    private string skeletonName;
    private const string trunkBeltOnlySkeletonName = "TRUNK_BELT_ONLY";
    private const string pelvicBeltOnlySkeletonName = "PELVIC_BELT_ONLY";
    private const string pelvicAndTrunkBeltSkeletonName = "PELVIC_AND_TRUNK_BELTS";
    private const string pelvicAndShankBeltsSkeletonName = "PELVIC_AND_SHANK_BELTS";

    // Set the pulley pointer length. This is the distance from the near pulley pointer marker
    // to the point where the cable departs from the pulley. 
    private const float pulleyPointerNearMarkerToPulleyDistances = 0.69215f; // the pulley distance from near marker to pulley in [m] (cut to 27.25")
    private const float convertMetersToMillimeters = 1000.0f;

    // Set the name of this "task". This is just the name of the directory containing the data for the given subject. 
    // I.e. Subject# -> ThisTaskName -> Date -> OutputDataFile.csv
    private const string thisTaskName = "StructureMatrixData";

    // Set the name of the output .csv file. 
    private const string outputFileName = "Data_To_Build_Structure_Matrix.csv";
    private const string outputFileNameVive = "Data_To_Build_ViveBased_Structure_Matrix.csv";

    //debugging and testing 
    Stopwatch stopWatch = new Stopwatch();
    Stopwatch tempStopWatch = new Stopwatch();
    private const string logAMessageSpecifier = "LOG";
    private const string logAWarningSpecifier = "WARNING";
    private const string logAnErrorSpecifier = "ERROR";
    public KinematicModelClass KinematicModel;

    //key public game objects
    public GameObject markerDataDistributor; //the GameObject that reads marker data each frame and has getter functions to get marker data by name. Accessses data via the dataStream prefab from the Unity/Vicon plugin.
    public Camera sceneCamera; //the camera that visulizes the scene. Used for converting viewport coordinates to world coordinates.

    //key private game object scripts
    private UnityVicon.ReadViconDataStreamScript markerDataDistributorScript; //the callable script of the GameObject that makes marker data available.

    //logic flags that control program flow
    private bool storingFramesForSetupFlag; //if true, it means we're saving each marker frame's data. This data is used for setup.
    private bool viconSetupComplete = false; //whether setup is complete (true) or not (false)
    private bool savedViconStructureMatrixDataFlag = false; // whether or not the desired data has been written to file (the goal of this script)
    private bool savedViveStructureMatrixDataFlag = false;

    // List to store computed pulley positions
    private Vector3[] pulleyPositionsTrunkBelt = new Vector3[4]; // Order: Front left, front right, back right, back left
    private Vector3[] pulleyPositionsPelvicBelt = new Vector3[8]; // Order: Front left, front right, back right, back left
    private Vector3[] pulleyPositionsPelvicBeltLower = new Vector3[4]; // In case we use 8 pulley for pelvis, these should be the lower ones. 
                                                                       //Use same order as pelvic belt!
    private Vector3[] pulleyPositionsRightShankBelt = new Vector3[1]; // Order: only one right shank pulley
    private Vector3[] pulleyPositionsLeftShankBelt = new Vector3[1]; // Order: only one left shank pulley

    // A flag for accessing pelvic belt pulley positions (says whether or not they have been computed already)
    private bool pelvicBeltPulleyPositionsAlreadyComputedFlag = false;


    // Object that contains all pulley position names for the trunk belt, pelvic belt
    // These should match the order in the position vectors
    private string[] pulleyNamesTrunkBelt = new string[4];
    private string[] pulleyNamesPelvicBelt = new string[8]; // upper and lower
    private string[] pulleyNamesRightShankBelt = new string[1];
    private string[] pulleyNamesLeftShankBelt = new string[1];


    // Trunk belt pulley names
    private const string trunkFrontLeftPulleyName = "TRUNK_FRONT_LEFT_PULLEY";
    private const string trunkFrontRightPulleyName = "TRUNK_FRONT_RIGHT_PULLEY";
    private const string trunkBackRightPulleyName = "TRUNK_BACK_RIGHT_PULLEY";
    private const string trunkBackLeftPulleyName = "TRUNK_BACK_LEFT_PULLEY";



    private string[] namesOfAllPulleyPositionsComputed; // a string array containing the names of all pulleys we compute the position of

    // Lists to store computed belt attachment positions in belt frame
    // The order of these lists should match the order of the corresponding pulley positions vector.
    private string[] attachmentPointsTrunkBeltMarkerNames; // A list of temporary markers/attachment points in the trunk belt frame. 
    private Vector3[] attachmentPointsTrunkBeltInBeltFrame = new Vector3[4];
    private string[] attachmentPointsPelvicBeltMarkerNames; // A list of temporary markers/attachment points in the pelvic belt frame.
    private Vector3[] attachmentPointsPelvicBeltInBeltFrame = new Vector3[8];
    private string[] attachmentPointsRightShankBeltMarkerNames; // A list of temporary markers/attachment points in the right shank belt frame.
    private Vector3[] attachmentPointsRightShankBeltInBeltFrame = new Vector3[1];
    private string[] attachmentPointsLeftShankBeltMarkerNames; // A list of temporary markers/attachment points in the right shank belt frame.
    private Vector3[] attachmentPointsLeftShankBeltInBeltFrame = new Vector3[1];

    // List to store computed belt attachment positions in Vicon frame. Note, this is 
    // not really needed in this script but is used to test our structure matrix computation. 
    private Vector3[] attachmentPointsTrunkBeltInViconFrame = new Vector3[4];
    private Vector3[] attachmentPointsPelvicBeltInViconFrame = new Vector3[8];
    private Vector3[] attachmentPointsRightShankBeltInViconFrame = new Vector3[1];
    private Vector3[] attachmentPointsLeftShankBeltInViconFrame = new Vector3[1];



    // Object that contains the names


    // Name of trunk belt fixed markers
    // NOTE: these differ from the names used in the full labeling skeleton!
    // That's OK! We use a different setup skeleton with this file.
    private const string trunkBeltBackCenterMarker = "TrunkBeltBackMiddle";
    private const string trunkBeltBackRightMarker = "TrunkBeltBackRight";
    private const string trunkBeltBackLeftMarker = "TrunkBeltBackLeft";
    private const string trunkBeltFrontRightMarker = "TrunkBeltFrontRight";
    private const string trunkBeltFrontLeftMarker = "TrunkBeltFrontLeft";


    // Name of trunk belt temporary markers
    private const string trunkBeltCableBackRightMarker = "TrunkBeltCableBackRight";
    private const string trunkBeltCableBackLeftMarker = "TrunkBeltCableBackLeft";
    private const string trunkBeltCableFrontRightMarker = "TrunkBeltCableFrontRight";
    private const string trunkBeltCableFrontLeftMarker = "TrunkBeltCableFrontLeft";

    // Trunk pointer markers
    // We will typically only use 4 trunk belt cables. 
    // So, we name the pointers front right, front left, back right, back left, 
    // based on which cable attachment point they connect to.
    // Front right trunk pointer
    private const string trunkFrontRightPulleyDistalMarker = "TrunkBeltFrontRPointerDistal";
    private const string trunkFrontRightPulleyNearMarker = "TrunkBeltFrontRPointerNearPlly";
    // Front left trunk pointer
    private const string trunkFrontLeftPulleyDistalMarker = "TrunkBeltFrontLPointerDistal";
    private const string trunkFrontLeftPulleyNearMarker = "TrunkBeltFrontLPointerNearPlly";
    // Back right trunk pointer
    private const string trunkBackRightPulleyDistalMarker = "TrunkBeltBackRPointerDistal";
    private const string trunkBackRightPulleyNearMarker = "TrunkBeltBackRPointerNearPlly";
    // Front left trunk pointer
    private const string trunkBackLeftPulleyDistalMarker = "TrunkBeltBackLPointerDistal";
    private const string trunkBackLeftPulleyNearMarker = "TrunkBeltBackLPointerNearPlly";

    // Vive-based trunk variables
    // Positions in the Vive reference frame are in meters.
    private Vector3[] pulleyPositionsTrunkBeltViveReferenceFrame = new Vector3[4]; // Order: Front left, front right, back right, back left
    // The order of the belt attachment points in Vive frame should match the order of the corresponding pulley positions vector (the VICON one above).
    private Vector3[] attachmentPointsTrunkBeltInBeltViveTrackerFrame = new Vector3[4];



    // PELVIS-RELEVANT VARIABLES**********************************************************************

    // Pelvic pulley names
    // Trunk belt pulley names
    private const string pelvicUpperFrontLeftPulleyName = "PELVIS_UPPER_FRONT_LEFT_PULLEY";
    private const string pelvicUpperFrontRightPulleyName = "PELVIS_UPPER_FRONT_RIGHT_PULLEY";
    private const string pelvicUpperBackRightPulleyName = "PELVIS_UPPER_BACK_RIGHT_PULLEY";
    private const string pelvicUpperBackLeftPulleyName = "PELVIS_UPPER_BACK_LEFT_PULLEY";
    private const string pelvicLowerFrontLeftPulleyName = "PELVIS_LOWER_FRONT_LEFT_PULLEY";
    private const string pelvicLowerFrontRightPulleyName = "PELVIS_LOWER_FRONT_RIGHT_PULLEY";
    private const string pelvicLowerBackRightPulleyName = "PELVIS_LOWER_BACK_RIGHT_PULLEY";
    private const string pelvicLowerBackLeftPulleyName = "PELVIS_LOWER_BACK_LEFT_PULLEY";
    // Name of trunk belt fixed markers
    // NOTE: these differ from the names used in the full labeling skeleton!
    // That's OK! We use a different setup skeleton with this file.
    private const string pelvicBeltBackCenterMarker = "PelvisBeltBackMiddle";
    private const string pelvicBeltBackRightMarker = "PelvisBeltBackRight";
    private const string pelvicBeltBackLeftMarker = "PelvisBeltBackLeft";
    private const string pelvicBeltFrontRightMarker = "PelvisBeltFrontRight";
    private const string pelvicBeltFrontLeftMarker = "PelvisBeltFrontLeft";


    // Name of pelvic belt "temporary markers" OR attachment points (Vicon or Vive)
    private const string pelvicBeltCableBackRightMarker = "PelvisBeltCableBackRight";
    private const string pelvicBeltCableBackLeftMarker = "PelvisBeltCableBackLeft";
    private const string pelvicBeltCableFrontRightMarker = "PelvisBeltCableFrontRight";
    private const string pelvicBeltCableFrontLeftMarker = "PelvisBeltCableFrontLeft";

    // Pelvic pointer markers
    // We will typically only use 4 trunk belt cables. 
    // So, we name the pointers front right, front left, back right, back left, 
    // based on which cable attachment point they connect to.
    // Front right trunk pointer
    private const string pelvicFrontRightPulleyDistalMarker = "PelvicFrontRPointerDistal";
    private const string pelvicFrontRightPulleyNearMarker = "PelvicFrontRPointerNearPlly";
    // Front left pelvic pointer
    private const string pelvicFrontLeftPulleyDistalMarker = "PelvicFrontLPointerDistal";
    private const string pelvicFrontLeftPulleyNearMarker = "PelvicFrontLPointerNearPlly";
    // Back right pelvic pointer
    private const string pelvicBackRightPulleyDistalMarker = "PelvicBackRPointerDistal";
    private const string pelvicBackRightPulleyNearMarker = "PelvicBackRPointerNearPlly";
    // Front left pelvic pointer
    private const string pelvicBackLeftPulleyDistalMarker = "PelvicBackLPointerDistal";
    private const string pelvicBackLeftPulleyNearMarker = "PelvicBackLPointerNearPlly";

    // Vive-based pelvis variables
    // Positions in the Vive reference frame are in meters.
    private Vector3[] pulleyPositionsPelvicBeltViveReferenceFrame = new Vector3[4]; // Order: Front left, front right, back right, back left
    // The order of the belt attachment points in Vive frame should match the order of the corresponding pulley positions vector (the VICON one above).
    private Vector3[] attachmentPointsPelvicBeltInBeltViveTrackerFrame = new Vector3[4];

    // SHANK-RELEVANT VARIABLES**********************************************************************

    // Shank belt pulley names
    private const string rightShankPulleyName = "RIGHT_SHANK_BELT_PULLEY";
    private const string leftShankPulleyName = "LEFT_SHANK_BELT_PULLEY";

    // Right shank belt pulley and attachment point markers
    private const string rightShankBeltCableAttachmentMarker = "RightShankAttachmentMarker";
    private const string rightShankBeltPulleyMarker = "RightShankPulleyMarker";
    private const string leftShankBeltCableAttachmentMarker = "LeftShankAttachmentMarker";
    private const string leftShankBeltPulleyMarker = "LeftShankPulleyMarker";
    
    // Vive-based shank variables
    // Positions in the Vive reference frame are in meters.
    private Vector3 leftShankPulleyPositionViveReferenceFrame; // In left-handed Vive reference tracker frame
    private Vector3 rightShankPulleyPositionViveReferenceFrame; // In left-handed Vive reference tracker frame
    private Vector3 leftShankAttachmentPointInBeltViveTrackerFrame; // In left-handed Vive shank belt tracker frame
    private Vector3 rightShankAttachmentPointInBeltViveTrackerFrame; // In left-handed Vive shank belt tracker frame

    //name of knee markers
    private const string rightKneeMarkerName = "RKNE";
    private const string leftKneeMarkerName = "LKNE";
    private const string rightKneeMedialMarkerName = "RKNEEMED";
    private const string leftKneeMedialMarkerName = "RKNEEMED";

    //name of shank markers
    private const string rightLateralShankMarkerName = "RTIB";
    private const string rightTibialTuberosityMarkerName = "R.TibTub";
    private const string leftLateralShankMarkerName = "LTIB";
    private const string leftTibialTuberosityMarkerName = "L.TibTub";


    //name of ankle markers
    private const string rightAnkleMarkerName = "RANK";
    private const string leftAnkleMarkerName = "LANK";
    private const string rightAnkleMedialMarkerName = "RANKMED";
    private const string leftAnkleMedialMarkerName = "LANKMED";


    //name of shank markers!!!!!!!!!!!!!!!!!!!!!!!!!!!!


    //Names of foot markers
    private const string rightFirstMetatarsalMarkerName = "R1MT";
    private const string leftFirstMetatarsalMarkerName = "L1MT";
    private const string rightFifthMetatarsalMarkerName = "R5MT";
    private const string leftFifthMetatarsalMarkerName = "L5MT";
    private const string rightSecondMetatarsalMarkerName = "R2MT"; //call the right second metatarsal "RTOE" in the plugin gait model
    private const string leftSecondMetatarsalMarkerName = "L2MT"; //call the left second metatarsal "LTOE" in the plugin gait model

    //marker frames stored from setup
    private uint numberOfSetupFramesAlreadyStored = 0; //keep track of how many frames we have stored for setup.
    private const uint numberOfSetupMarkerFrames = 1; //how many marker frames to store for setup.
    private List<bool[]> setupMarkerFramesOcclusionStatus = new List<bool[]>();
    private List<float[]> setupMarkerFramesXPos = new List<float[]>();
    private List<float[]> setupMarkerFramesYPos = new List<float[]>();
    private List<float[]> setupMarkerFramesZPos = new List<float[]>();

    //more variables for handling marker data from setup frames
    Vector3[] averagePositionOfModelMarkersInStartupFrames; //stores the average position of all of the model markers across the startup frames


    //object that contains all used marker names
    private string[] namesOfAllMarkersInSkeleton; //a string array containing the names of all markers used in our model to compute ankle angle
    private bool[] markersInSkeletonOcclusionStatus; // whether or not the markers in the model are occluded this frame
    private float[] markersInSkeletonXPositions;  // x-axis positions of markers in model this frame (in Vicon coords)
    private float[] markersInSkeletonYPositions;  // y-axis positions of markers in model this frame (in Vicon coords)
    private float[] markersInSkeletonZPositions;  // z-axis positions of markers in model this frame (in Vicon coords)



    //Marker reconstruction variables 
    bool[] markerInSkeletonWasReconstructedThisFrameFlags; //a bool array indicating whether or not the marker was reconstructed this frame
    List<string> namesOfAllReconstructedMarkers = new List<string>();
    List<Vector3> positionsOfAllReconstructedMarkers = new List<Vector3>();
    private string[] rigidBodiesToReconstructByName;
    private List<string[]> rigidBodiesToReconstructMarkerNames; //a list containing string arrays of the names of every marker in
                                                                //rigid bodies with markers that must be reconstructed. Each
                                                                //string array corresponds to a rigid body.
    private string[] segmentNames; //the names of the segments, which should be the same as the names of the rigid bodies
                                   //we reconstructed.
    Matrix4x4[] transformationsFromViconFrameToSegmentFrame; //stores transformation matrices from Vicon frame to
                                                             //each segment's local frame

    //Lists of markers in rigid bodies (segments or non-segments).
    //Needed for reconstruction.
    string[] markersInLeftFoot; // names of all markers in the left foot segment. Initialized in setup.
    string[] markersInRightFoot; // names of all markers in the right foot segment. Initialized in setup.
    string[] markersInLeftShank; // names of all markers in the left shank segment. Initialized in setup.
    string[] markersInRightShank; // names of all markers in the right shank segment. Initialized in setup.


    //the "key" variables that hold the most recent Euler angle values for the shank-foot transformation
    private bool hasScriptGeneratedAnkleAngleDataYetFlag = false; // When the script begins, we don't have any valid ankle angle data. Set this to true the first time some valid ankle data is available.
    private Vector3 rightShankToRightFootEulerAngles = new Vector3(0.0f, 0.0f, 0.0f);
    private Vector3 leftShankToLeftFootEulerAngles = new Vector3(0.0f, 0.0f, 0.0f);

    // Vicon marker data and saving data to file
    public GameObject generalDataRecorder; //the GameObject that stores data and ultimately writes it to file (CSV)
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    private uint mostRecentlyAccessedViconFrameNumber; //frame number for the most recent frame accessed
    private bool recordMarkerDataToFile = false;

    



    //START: Core Unity functions ( start(), update() )***********************************************************************************************

    // Start is called before the first frame update
    void Start()
    {
        string verFramework = AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName;

        Debug.Log(".NET framework is: " + verFramework);

        // Get reference to the marker data distributor, which distributes data from the Vicon data stream
        markerDataDistributorScript = markerDataDistributor.GetComponent<UnityVicon.ReadViconDataStreamScript>();

        // Get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // Get a reference to the General Data Recorder script, which will save data to file. 
        // Specifically, it will store the file needed to construct the S matrix for each belt.
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // If we're using Vicon
        if(structureMatrixSettingsScript.GetUsingViconFlag() == true)
        {
            // Do all of the setup for the Vicon-based structure matrix data
            SetupForViconBasedStuctureMatrixData();
        }

        // If we're using Vive
        if(structureMatrixSettingsScript.GetUsingViveFlag() == true)
        {
            SetupForViveBasedStructureMatrixData();
        }

        // Set the directory and file name for the output .CSV file, which will store all of the computed data
        SetOutputDataDirectoryAndFileName();

        // Set the variable naming (column names) for our output .CSV file, which will store all of the computed data
        SetOutputDataColumnNaming();
    }



    // Update is called once per frame
    void FixedUpdate()
    {

        // Try to create the Vicon frame structure matrix data file, if we're using Vicon
        if (structureMatrixSettingsScript.GetUsingViconFlag() == true && savedViconStructureMatrixDataFlag == false)
        {
            // The flag indicating that we've exported data is set within the function.
            if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViveOnly)
            {
                TryToCreateViveBasedStructureMatrixDataFile();
            }
            else if (KinematicModel.dataSourceSelector == ModelDataSourceSelector.ViconOnly)
            {
                TryToCreateViconBasedStructureMatrixDataFile();
            }
            else
            {
                // Put one for Vicon and Vive in the future
            }
            
        }


        // Try to create the Vive-based structure matrix data file, if we're using Vive ONLY. 
        // Note: if we're using both Vive and Vicon, then Vive trackers are used for reconstructing Vicon marker positions ONLY (for now, at least?)
        if (structureMatrixSettingsScript.GetUsingViveFlag() == true && structureMatrixSettingsScript.GetUsingViconFlag() == false 
            && savedViveStructureMatrixDataFlag == false)
        {
            savedViveStructureMatrixDataFlag = TryToCreateViveBasedStructureMatrixDataFile();
        }

    }

    //END: Core Unity functions ( start(), update() )***********************************************************************************************



    // START: Public functions *****************************************************************************************************************

    public (bool, Vector3[]) GetPelvicBeltPulleyPositionsInViconFrame()
    {
        if(pelvicBeltPulleyPositionsAlreadyComputedFlag == true)
        {
            // Note that this Vector3[] is ordered. 
            // The order is set in variable initialization, see the instance variables above.
            // 5/16/2024: Order is front-left, front-right, back-right, back-left.
            return (true, pulleyPositionsPelvicBelt);
        }
        else
        {
            return (false, new Vector3[4]);
        }
    }



    //START: Functions called for setup only***********************************************************************************************


    // Do all setup related to the Vicon-based computation of data
    // needed to build the structure matrices
    private void SetupForViconBasedStuctureMatrixData()
    {
        // Choose the skeleton based on which belts are used
        if (usingTrunkBelt & !usingPelvicBelt & !usingShankBelts)
        {
            Debug.Log("Get data for trunk belt structure matrix.");
            // Set skeleton name
            skeletonName = trunkBeltOnlySkeletonName;
            // Choose all markers in the skeleton
            namesOfAllMarkersInSkeleton = new string[] { trunkBeltBackCenterMarker, trunkBeltBackRightMarker, trunkBeltBackLeftMarker,
                trunkBeltFrontRightMarker, trunkBeltFrontLeftMarker, trunkBeltCableBackRightMarker, trunkBeltCableBackLeftMarker,
                trunkBeltCableFrontRightMarker, trunkBeltCableFrontLeftMarker, trunkFrontRightPulleyDistalMarker,
                trunkFrontRightPulleyNearMarker, trunkFrontLeftPulleyDistalMarker, trunkFrontLeftPulleyNearMarker,
                trunkBackRightPulleyDistalMarker, trunkBackRightPulleyNearMarker, trunkBackLeftPulleyDistalMarker,
                trunkBackLeftPulleyNearMarker };
            // Store the names of the pulleys (this is used when saving out data)
            pulleyNamesTrunkBelt = new string[] { trunkFrontLeftPulleyName, trunkFrontRightPulleyName, trunkBackRightPulleyName,
                                                  trunkBackLeftPulleyName};

            // Specify a list of trunk belt temporary markers. We'll compute all of these markers' positions in the trunk belt frame. 
            // ORDER MATTERS: it should match the order in the pulley positions list (i.e. pulley in index 0 should connect to the attachment
            // point in index 0).
            attachmentPointsTrunkBeltMarkerNames = new string[] { trunkBeltCableFrontLeftMarker,  trunkBeltCableFrontRightMarker,
                        trunkBeltCableBackRightMarker, trunkBeltCableBackLeftMarker};
        }
        else if (usingPelvicBelt & !usingTrunkBelt & !usingShankBelts)
        {
            Debug.Log("Get data for pelvic belt structure matrix.");

            // Set skeleton name
            skeletonName = pelvicBeltOnlySkeletonName;

            // Choose all markers in the skeleton
            namesOfAllMarkersInSkeleton = new string[] { pelvicBeltBackCenterMarker, pelvicBeltBackLeftMarker,
                pelvicBeltBackRightMarker, pelvicBeltFrontRightMarker, pelvicBeltFrontLeftMarker,
                pelvicBeltCableBackRightMarker, pelvicBeltCableBackLeftMarker,
                pelvicBeltCableFrontRightMarker, pelvicBeltCableFrontLeftMarker,
                pelvicFrontRightPulleyDistalMarker, pelvicFrontRightPulleyNearMarker,
                pelvicFrontLeftPulleyDistalMarker, pelvicFrontLeftPulleyNearMarker,
                pelvicBackRightPulleyDistalMarker, pelvicBackRightPulleyNearMarker,
                pelvicBackLeftPulleyDistalMarker, pelvicBackLeftPulleyNearMarker};

            // Store the names of the pulleys (this is used when saving out data)
            pulleyNamesPelvicBelt = new string[] { pelvicUpperFrontLeftPulleyName, pelvicUpperFrontRightPulleyName, pelvicUpperBackRightPulleyName,
                                                   pelvicUpperBackLeftPulleyName, 
                                                   pelvicLowerFrontLeftPulleyName, pelvicLowerFrontRightPulleyName,
                                                   pelvicLowerBackRightPulleyName, pelvicLowerBackLeftPulleyName};

            // Specify a list of trunk belt temporary markers. We'll compute all of these markers' positions in the trunk belt frame. 
            // ORDER MATTERS: it should match the order in the pulley positions list (i.e. pulley in index 0 should connect to the attachment
            // point in index 0).
            attachmentPointsPelvicBeltMarkerNames = new string[] { pelvicBeltCableFrontLeftMarker,  pelvicBeltCableFrontRightMarker,
                        pelvicBeltCableBackRightMarker, pelvicBeltCableBackLeftMarker};
        }
        else if (usingPelvicBelt & usingShankBelts)
        {
            Debug.Log("Get data for pelvic belt and shank belt structure matrices.");
            // NOT IMPLEMENTED YET
            skeletonName = pelvicAndShankBeltsSkeletonName;

            // Choose all markers in the skeleton
            namesOfAllMarkersInSkeleton = new string[] { pelvicBeltBackCenterMarker, pelvicBeltBackLeftMarker,
                pelvicBeltBackRightMarker, pelvicBeltFrontRightMarker, pelvicBeltFrontLeftMarker,
                pelvicBeltCableBackRightMarker, pelvicBeltCableBackLeftMarker,
                pelvicBeltCableFrontRightMarker, pelvicBeltCableFrontLeftMarker,
                pelvicFrontRightPulleyDistalMarker, pelvicFrontRightPulleyNearMarker,
                pelvicFrontLeftPulleyDistalMarker, pelvicFrontLeftPulleyNearMarker,
                pelvicBackRightPulleyDistalMarker, pelvicBackRightPulleyNearMarker,
                pelvicBackLeftPulleyDistalMarker, pelvicBackLeftPulleyNearMarker,
                rightLateralShankMarkerName, rightTibialTuberosityMarkerName, rightAnkleMarkerName, // start right shank belt fixed markers
                rightAnkleMedialMarkerName, rightKneeMarkerName, rightKneeMedialMarkerName,
                rightShankBeltCableAttachmentMarker, rightShankBeltPulleyMarker, // right shank temp marker and pulley marker
                leftLateralShankMarkerName, leftTibialTuberosityMarkerName, leftAnkleMarkerName, // start left shank belt fixed markers
                leftAnkleMedialMarkerName, leftKneeMarkerName, leftKneeMedialMarkerName,
                leftShankBeltCableAttachmentMarker, leftShankBeltPulleyMarker // right shank temp marker and pulley marker
            };

            // Store the names of the pulleys (this is used when saving out data)
            // Pelvic belt pulleys
            pulleyNamesPelvicBelt = new string[] { pelvicUpperFrontLeftPulleyName, pelvicUpperFrontRightPulleyName, pelvicUpperBackRightPulleyName,
                                                  pelvicUpperBackLeftPulleyName,
                                                   pelvicLowerFrontLeftPulleyName, pelvicLowerFrontRightPulleyName,
                                                   pelvicLowerBackRightPulleyName, pelvicLowerBackLeftPulleyName};
            // Shank belt pulleys
            pulleyNamesRightShankBelt = new string[] { rightShankPulleyName };
            pulleyNamesLeftShankBelt = new string[] { leftShankPulleyName };

            // Specify a list of pelvic belt temporary markers. We'll compute all of these markers' positions in the trunk belt frame. 
            // ORDER MATTERS: it should match the order in the pulley positions list (i.e. pulley in index 0 should connect to the attachment
            // point in index 0).
            attachmentPointsPelvicBeltMarkerNames = new string[] { pelvicBeltCableFrontLeftMarker,  pelvicBeltCableFrontRightMarker,
                        pelvicBeltCableBackRightMarker, pelvicBeltCableBackLeftMarker};
            // Then shank belt attachments
            attachmentPointsRightShankBeltMarkerNames = new string[] { rightShankBeltCableAttachmentMarker };
            attachmentPointsLeftShankBeltMarkerNames = new string[] { leftShankBeltCableAttachmentMarker };

        }
        else // Not valid
        {
            Debug.LogError("Must select valid belts to use: pelvic, trunk, or both.");
        }

        // Define the marker skeleton used for data collection. This could vary if we use trunk belt, 
        // pelvic belt, or both


        // Initialize variables (Including arrays to store the skeleton marker occlusion status, position, etc.)
        initializeVariables();

        // Since we still need to collect a few dozen marker frames for setup,
        //set the flag to true
        storingFramesForSetupFlag = true;
    }


    // Do all setup related to the Vive-based computation of data
    // needed to build the structure matrices
    private void SetupForViveBasedStructureMatrixData()
    {
        // If we're using the trunk belt
        if (usingTrunkBelt)
        {
            // Store the names of the pulleys (this is used when saving out data)
            pulleyNamesTrunkBelt = new string[] { trunkFrontLeftPulleyName, trunkFrontRightPulleyName, trunkBackRightPulleyName,
                                                  trunkBackLeftPulleyName};

            // Even if we're not using Vicon markers, we use the marker names as column headers for the export file. 
            // So, specify them. 
            attachmentPointsTrunkBeltMarkerNames = new string[] { trunkBeltCableFrontLeftMarker,  trunkBeltCableFrontRightMarker,
                        trunkBeltCableBackRightMarker, trunkBeltCableBackLeftMarker};

        }

        // If we're using the pelvic belt
        if (usingPelvicBelt)
        {
            // Store the names of the pulleys (this is used when saving out data)
            pulleyNamesPelvicBelt = new string[] { pelvicUpperFrontLeftPulleyName, pelvicUpperFrontRightPulleyName, pelvicUpperBackRightPulleyName,
                                                  pelvicUpperBackLeftPulleyName,
                                                   pelvicLowerFrontLeftPulleyName, pelvicLowerFrontRightPulleyName,
                                                   pelvicLowerBackRightPulleyName, pelvicLowerBackLeftPulleyName};

            // Even if we're not using Vicon markers, we use the marker names as column headers for the export file. 
            // So, specify them. 
            attachmentPointsPelvicBeltMarkerNames = new string[] { pelvicBeltCableFrontLeftMarker,  pelvicBeltCableFrontRightMarker,
                        pelvicBeltCableBackRightMarker, pelvicBeltCableBackLeftMarker};
           
        }

        // Add shank belt stuff
        if (usingShankBelts)
        {
            // Then shank belt attachments
            attachmentPointsRightShankBeltMarkerNames = new string[] { rightShankBeltCableAttachmentMarker };
            attachmentPointsLeftShankBeltMarkerNames = new string[] { leftShankBeltCableAttachmentMarker };

            // Shank belt pulley naming
            pulleyNamesRightShankBelt = new string[] { rightShankPulleyName };
            pulleyNamesLeftShankBelt = new string[] { leftShankPulleyName };
        }

    }


    private void initializeVariables()
    {
        //initialize the marker storage instance variables to the correct size
        int numberOfMarkersInModel = namesOfAllMarkersInSkeleton.Length;
        markersInSkeletonOcclusionStatus = new bool[numberOfMarkersInModel];
        markersInSkeletonXPositions = new float[numberOfMarkersInModel];
        markersInSkeletonYPositions = new float[numberOfMarkersInModel];
        markersInSkeletonZPositions = new float[numberOfMarkersInModel];
    }





    //The top-level setup function.
    //Goals:
    //1.) Ensure the marker data distributor is ready before querying it
    //2.) Store startup frames so we can use averaged data for our computations.
    //Returns: a boolean indicating whether or not setup is complete
    private bool SetupDataFramesForSMatrixComputation()
    {
        //initialize the return value as false (setup is not complete)
        bool viconSetupComplete = false;

        //check if the data stream server is ready yet
        bool viconDataStreamDistributorReadyStatus = markerDataDistributorScript.getReadyStatusOfViconDataStreamDistributor();

        if (viconDataStreamDistributorReadyStatus) //if the Vicon data stream is ready to be accessed 
        {
            printLogMessageToConsoleIfDebugModeIsDefined("Vicon data stream is ready.");

            //Store a few dozen frames of marker data so that we can obtain marker position averages.
            //This should be somewhat more robust than choosing a single frame.
            bool enoughFramesStored = storeMarkerFramesForSetup();

            if (enoughFramesStored)
            {
                printLogMessageToConsoleIfDebugModeIsDefined("Enough frames stored for setup.");

                //get the average position of all the model markers in the startup frames 
                averagePositionOfModelMarkersInStartupFrames = getAveragePositionOfAllMarkersInStartupFrames();

                //mark setup as complete
                viconSetupComplete = true;
            }
        }

        return viconSetupComplete; //replace this with an overall "setupSuccessful" bool return value
    }





    //Stores the first n frames in which none of the model markers are occluded.
    //
    private bool storeMarkerFramesForSetup()
    {

        if (storingFramesForSetupFlag) //if we're still storing frames, see if the most recent frame can be stored
        {
            bool isMarkerDataOld = getMarkerDataForAllMarkersNeededInSkeleton(namesOfAllMarkersInSkeleton);

            if (isMarkerDataOld)
            {
                printLogMessageToConsoleIfDebugModeIsDefined("Trying to store frames for setup, but marker data is old.");
            }

            if (markersInSkeletonOcclusionStatus.All(x => !x) && !isMarkerDataOld) //if no markers in the model are occluded this frame and new data is available
            {
                //store the marker occlusion status and positions as elements in a list
                setupMarkerFramesOcclusionStatus.Add(markersInSkeletonOcclusionStatus);
                setupMarkerFramesXPos.Add(markersInSkeletonXPositions);
                setupMarkerFramesYPos.Add(markersInSkeletonYPositions);
                setupMarkerFramesZPos.Add(markersInSkeletonZPositions);

                //increment the counter which keeps track of how many frames we've stored
                numberOfSetupFramesAlreadyStored = numberOfSetupFramesAlreadyStored + 1;

                Debug.Log("For setup, have stored the following number of frames: " + numberOfSetupFramesAlreadyStored);
            }
            else //if some markers are missing this frame
            {
                printLogMessageToConsoleIfDebugModeIsDefined("storeMarkerFramesForSetup(): markers missing from current frame");

                //print out which markers are missing
                for (uint markerInModelIndex = 0; markerInModelIndex < markersInSkeletonOcclusionStatus.Length; markerInModelIndex++)
                {
                    if (markersInSkeletonOcclusionStatus[markerInModelIndex] == true)
                    {
                        string logMessage = "storeMarkerFramesForSetup(): Marker with name " + namesOfAllMarkersInSkeleton[markerInModelIndex] + "is occluded or missing from the model.";
                        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
                    }
                }
            }
        }


        //manage the return boolean, which should be true if we've collected enough frames
        bool enoughFramesStored = false;  //a boolean indicating if we've stored enough startup frames of marker data
        if (numberOfSetupFramesAlreadyStored >= numberOfSetupMarkerFrames) //if we've collected enough frames
        {
            enoughFramesStored = true; //set flag indicating we've collected enough startup frames
        }

        return enoughFramesStored;
    }



    //END: Functions called for setup only***********************************************************************************************







    //START: Functions called by Update()***********************************************************************************************


    // The main function for Vicon-based structure matrix data file creation. Called by Update().
    private void TryToCreateViconBasedStructureMatrixDataFile()
    {
        if (viconSetupComplete) //if setup has been completed
        {
            // If we haven't yet computed and stored the data required
            if (!savedViconStructureMatrixDataFlag)
            {
                // Compute the pulley positions
                ComputePulleyPositionsForAllBeltPulleys();

                // Compute the belt cable attachment positions in belt frame, for all belts in use
                ComputeTemporaryMarkerPositionsInBeltFrameAllBelts();

                // TESTING ONLY: create functions to produce the S matrix from the pulley positions and 
                // the belt attachment points. This code should eventually be moved to its own GameObject 
                // in scenes that compute the S matrix each frame. 
                // Here, we're just testing, so we can assume that for trunk-only, each pulley attaches
                // to the corresponding cable attachment point/temp marker.
                // NOTE: output S matrix was examined in MATLAB and seemed reasonable.
                /* Vector3 trunkBeltCenter = GetTrunkBeltCenterFromTrunkBeltFixedMarkers();
                 ConstructStructureMatrix(trunkBeltCenter, pulleyPositionsTrunkBelt, attachmentPointsTrunkBeltInViconFrame);
 */

                // Store the data to file (.csv). 
                // The pulley positions and temp marker positions (in their respective belt frames) have been stored in instance variables.
                StorePulleyPositionsAndLocalFrameBeltTempMarkerPositionsToFile();

                // Set the flag that the data was stored, so that this code only runs once
                savedViconStructureMatrixDataFlag = true;
            }
        }
        else //if we're still setting up, then keep trying to set up
        {
            printLogMessageToConsoleIfDebugModeIsDefined("Attempting setup");

            //call the setup function
            viconSetupComplete = SetupDataFramesForSMatrixComputation();

            if (viconSetupComplete) //if setup is complete
            {
                //print that setup is complete to console, if in debug mode
                Debug.Log("Vicon-based setup complete");
                printLogMessageToConsoleIfDebugModeIsDefined("Vicon-based setup completed.");
            }
        }
    }



    private bool TryToCreateViveBasedStructureMatrixDataFile()
    {
        // If we're using the pelvic belt
        bool pelvicBeltSuccessFlag = false;
        if (usingPelvicBelt)
        {
            Debug.Log("Trying to compute Vive-based pelvic structure matrix data.");
            // Compute the pelvic belt cable attachments in Vive tracker frame. 
            // Note: if also using Vicon, these are computed using the pulley pointers. 
            // Else, we use the "hard-coded" values of the pulley positions (with user-input pulley height).
            viveStructureMatrixManager.ComputePelvicBeltCableAttachmentsInViveTrackerFrame();

            // Get the computed pelvic belt cable attachment positions in Vive left-handed software tracker frame
            (bool attachmentPosSuccessFlagPelvis, Vector3 pelvicBeltBackLeftAttachmentPositionBeltTrackerFrame,
                Vector3 pelvicBeltFrontLeftAttachmentPositionBeltTrackerFrame, Vector3 pelvicBeltBackRightAttachmentPositionBeltTrackerFrame,
                Vector3 pelvicBeltFrontRightAttachmentPositionBeltTrackerFrame) = viveStructureMatrixManager.GetPelvicBeltAttachmentsInBeltTrackerFrame();

            // Get the pelvic pulley positions in reference Vive tracker frame
            (bool pulleyPosSuccessFlagPelvis, Vector3[] pelvicPulleyPositionsViveReferenceFrame) = 
                viveStructureMatrixManager.ComputePelvicBeltPulleyPositionsInViveTrackerFrame();

            // If we successfully retrieved the data we need
            if(attachmentPosSuccessFlagPelvis == true && pulleyPosSuccessFlagPelvis == true)
            {
                // Store the pulley positions expressed in Vive reference tracker frame and 
                // the belt attachment positions in the belt Vive tracker frame.
                pulleyPositionsPelvicBeltViveReferenceFrame = pelvicPulleyPositionsViveReferenceFrame;
                attachmentPointsPelvicBeltInBeltViveTrackerFrame = new Vector3[] { pelvicBeltFrontLeftAttachmentPositionBeltTrackerFrame,
                pelvicBeltFrontRightAttachmentPositionBeltTrackerFrame, pelvicBeltBackRightAttachmentPositionBeltTrackerFrame,
                pelvicBeltBackLeftAttachmentPositionBeltTrackerFrame};

                // Mark the pelvic data export as a success
                pelvicBeltSuccessFlag = true;
            }
        }
        else
        {
            pelvicBeltSuccessFlag = true;
        }

        // If we're using the chest belt 
        // handle it
        bool trunkBeltSuccessFlag = false;
        if (usingTrunkBelt)
        {
            // Compute the chest belt cable attachments
            viveStructureMatrixManager.ComputeChestBeltCableAttachmentsInViveTrackerFrame();

            // Get the computed chest belt cable attachments in Vive-left handed software tracker frame
            (bool attachmentPosSuccessFlagChest, Vector3 chestBeltBackLeftAttachmentPositionBeltTrackerFrame,
                Vector3 chestBeltFrontLeftAttachmentPositionBeltTrackerFrame, Vector3 chestBeltBackRightAttachmentPositionBeltTrackerFrame,
                Vector3 chestBeltFrontRightAttachmentPositionBeltTrackerFrame) = 
                viveStructureMatrixManager.GetChestBeltAttachmentsInBeltTrackerFrame();


            // Get the chest pulley positions in reference Vive tracker frame
            (bool pulleyPosSuccessFlagChest, Vector3[] chestPulleyPositionsViveReferenceFrame) =
                viveStructureMatrixManager.ComputeChestBeltPulleyPositionsInViveTrackerFrame();

            // If we successfully retrieved the data we need
            if (attachmentPosSuccessFlagChest == true && pulleyPosSuccessFlagChest == true)
            {
                // Store the pulley positions expressed in Vive reference tracker frame and 
                // the belt attachment positions in the belt Vive tracker frame.
                pulleyPositionsTrunkBeltViveReferenceFrame = chestPulleyPositionsViveReferenceFrame;
                attachmentPointsTrunkBeltInBeltViveTrackerFrame = new Vector3[] { chestBeltFrontLeftAttachmentPositionBeltTrackerFrame,
                chestBeltFrontRightAttachmentPositionBeltTrackerFrame, chestBeltBackRightAttachmentPositionBeltTrackerFrame,
                chestBeltBackLeftAttachmentPositionBeltTrackerFrame};

                // Mark the pelvic data export as a success
                trunkBeltSuccessFlag = true;
            }

        }
        else // if we're not using the chest belt, mark a success without doing anything
        {
            trunkBeltSuccessFlag = true;
        }


        // If we're using the shank belts
        bool shankBeltSuccessFlag = false;
        if (usingShankBelts)
        {
            Debug.Log("Trying to compute Vive-based shank structure matrix data.");
            // Compute the shank belt cable attachments in Vive tracker frame. 
            // Note: if also using Vicon, these are computed using the pulley pointers. 
            // Else, we use the "hard-coded" values of the pulley positions (with user-input pulley height).
            viveStructureMatrixManager.ComputeLeftAndRightShankBeltsCableAttachmentsInViveTrackerFrame();

            // Get the computed pelvic belt cable attachment positions in Vive left-handed software tracker frame
            bool attachmentPosSuccessFlag = false;
            (attachmentPosSuccessFlag, leftShankAttachmentPointInBeltViveTrackerFrame,
                rightShankAttachmentPointInBeltViveTrackerFrame) = viveStructureMatrixManager.GetLeftAndRightShankBeltAttachmentsInLeftHandedBeltTrackerFrame();

            // Get the pelvic pulley positions in reference Vive tracker frame
            bool pulleyPosSuccessFlag = false;
            (pulleyPosSuccessFlag, leftShankPulleyPositionViveReferenceFrame,
                    rightShankPulleyPositionViveReferenceFrame) =
                viveStructureMatrixManager.ComputeShankBeltPulleyPositionsInViveTrackerFrame();

            // If we successfully retrieved the data we need
            if(attachmentPosSuccessFlag == true && pulleyPosSuccessFlag == true)
            {
                // Store the data in the Vector3[] format, which we use for both Vicon and Vive to export data to file. 
                pulleyPositionsRightShankBelt = new Vector3[] { rightShankPulleyPositionViveReferenceFrame };
                pulleyPositionsLeftShankBelt = new Vector3[] { leftShankPulleyPositionViveReferenceFrame };
                attachmentPointsRightShankBeltInBeltFrame = new Vector3[] { rightShankAttachmentPointInBeltViveTrackerFrame };
                attachmentPointsLeftShankBeltInBeltFrame = new Vector3[] { leftShankAttachmentPointInBeltViveTrackerFrame };

                // Mark the pelvic data export as a success
                shankBeltSuccessFlag = true;
            }
        }
        else // if we're not using the shank belt, we can mark success as true
        {
            shankBeltSuccessFlag = true;
        }


        if(pelvicBeltSuccessFlag == true && trunkBeltSuccessFlag == true && shankBeltSuccessFlag == true)
        {
            Debug.Log("Success! Writing structure matrix data to file!");
            // Write collected data to file
            StorePulleyPositionsAndLocalFrameAttachmentsInViveFrameToFile();

            // Return true, indicating success
            return true;
        }
        else
        {
            Debug.Log("Failed to store structure matrix data. Pelvic belt success flag: " + pelvicBeltSuccessFlag + ", trunk belt success flag: " + trunkBeltSuccessFlag
                + ", shank belt success flag: " + shankBeltSuccessFlag);
            // Return false, indicating we need to try to store the Vive-based structure matrix again once the data is ready
            return false;
        }

    }

    private void ComputePulleyPositionsForAllBeltPulleys()
    {
        // If we're using the trunk belt, compute the FR, FL, BR, BL pulley positions
        if(skeletonName == trunkBeltOnlySkeletonName || skeletonName == pelvicAndTrunkBeltSkeletonName)
        {
            string logMessage = "Computing trunk belt pulley positions";
            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
            ComputeTrunkBeltPulleyPositions();
        }

        // If we're using the pelvic belt, compute the FR, FL, BR, BL pulley positions
        // NOT IMPLEMENTED YET!
        if (skeletonName == pelvicBeltOnlySkeletonName || skeletonName == pelvicAndTrunkBeltSkeletonName || skeletonName == pelvicAndShankBeltsSkeletonName)
        {
            ComputePelvicBeltPulleyPositions();
        }

        // If we're using the shank belts, compute the shank pulley positions
        // NOT IMPLEMENTED YET!
        if (skeletonName == pelvicAndShankBeltsSkeletonName)
        {
            ComputeShankBeltPulleyPositions(); // these are directly observed with markers, but we'll still call a function to make the code neater.
        }

        // If we're using the 3D pelvic belt, compute the BOTTOM FR, FL, BR, BL pulley positions
        // NOT IMPLEMENTED YET!
    }


    private void ComputeTrunkBeltPulleyPositions()
    {
        // Compute the position of the front left pulley in Vicon frame
        Vector3 nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkFrontLeftPulleyNearMarker);
        Vector3 distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkFrontLeftPulleyDistalMarker);
        Vector3 frontLeftPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Compute the position of the front right pulley in Vicon frame
        nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkFrontRightPulleyNearMarker);
        distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkFrontRightPulleyDistalMarker);
        Vector3 frontRightPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Compute the position of the back right pulley in Vicon frame
        nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkBackRightPulleyNearMarker);
        distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkBackRightPulleyDistalMarker);
        Vector3 backRightPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Compute the position of the back left pulley in Vicon frame
        nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkBackLeftPulleyNearMarker);
        distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(trunkBackLeftPulleyDistalMarker);
        Vector3 backLeftPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Store pulley positions. Important: use order specified when declaring the instance variable. 
        // We must ensure the order matches between the pulley positions and the attachment point positions.
        // We used front left, front right, back right, back left for the trunk belt.
        pulleyPositionsTrunkBelt[0] = frontLeftPulleyPositonViconFrame;
        pulleyPositionsTrunkBelt[1] = frontRightPulleyPositonViconFrame;
        pulleyPositionsTrunkBelt[2] = backRightPulleyPositonViconFrame;
        pulleyPositionsTrunkBelt[3] = backLeftPulleyPositonViconFrame;

        // Print pulley positions (DEBUG ONLY)
        string logMessage = "Front left trunk pulley (x,y,z): " + frontLeftPulleyPositonViconFrame.x + ", " +
            frontLeftPulleyPositonViconFrame.y + ", " + frontLeftPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        logMessage = "Front right trunk pulley (x,y,z): " + frontRightPulleyPositonViconFrame.x + ", " +
            frontRightPulleyPositonViconFrame.y + ", " + frontRightPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        logMessage = "Back right trunk pulley (x,y,z): " + backRightPulleyPositonViconFrame.x + ", " +
            backRightPulleyPositonViconFrame.y + ", " + backRightPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        logMessage = "Back right trunk pulley (x,y,z): " + backLeftPulleyPositonViconFrame.x + ", " +
            backLeftPulleyPositonViconFrame.y + ", " + backLeftPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
    }

    private void ComputePelvicBeltPulleyPositions()
    {
        // Compute the position of the front left pulley in Vicon frame
        Vector3 nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicFrontLeftPulleyNearMarker);
        Vector3 distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicFrontLeftPulleyDistalMarker);
        Vector3 frontLeftPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Compute the position of the front right pulley in Vicon frame
        nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicFrontRightPulleyNearMarker);
        distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicFrontRightPulleyDistalMarker);
        Vector3 frontRightPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Compute the position of the back right pulley in Vicon frame
        nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicBackRightPulleyNearMarker);
        distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicBackRightPulleyDistalMarker);
        Vector3 backRightPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Compute the position of the back left pulley in Vicon frame
        nearPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicBackLeftPulleyNearMarker);
        distalPulleyPointerMarker = getMarkerAveragePositionInStartupFramesByName(pelvicBackLeftPulleyDistalMarker);
        Vector3 backLeftPulleyPositonViconFrame = ComputePulleyPositionFromNearAndDistalPointerMarkers(nearPulleyPointerMarker, distalPulleyPointerMarker);

        // Store pulley positions. Important: use order specified when declaring the instance variable. 
        // We must ensure the order matches between the pulley positions and the attachment point positions.
        // We used front left, front right, back right, back left for the pelvic belt.
        pulleyPositionsPelvicBelt[0] = frontLeftPulleyPositonViconFrame;
        pulleyPositionsPelvicBelt[1] = frontRightPulleyPositonViconFrame;
        pulleyPositionsPelvicBelt[2] = backRightPulleyPositonViconFrame;
        pulleyPositionsPelvicBelt[3] = backLeftPulleyPositonViconFrame;

        // Set the flag indicating the pelvic belt pulley positions are available
        pelvicBeltPulleyPositionsAlreadyComputedFlag = true;

        // Print pulley positions (DEBUG ONLY)
        string logMessage = "Front left pelvic pulley (x,y,z): " + frontLeftPulleyPositonViconFrame.x + ", " +
            frontLeftPulleyPositonViconFrame.y + ", " + frontLeftPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        logMessage = "Front right pelvic pulley (x,y,z): " + frontRightPulleyPositonViconFrame.x + ", " +
            frontRightPulleyPositonViconFrame.y + ", " + frontRightPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        logMessage = "Back right pelvic pulley (x,y,z): " + backRightPulleyPositonViconFrame.x + ", " +
            backRightPulleyPositonViconFrame.y + ", " + backRightPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        logMessage = "Back right pelvic pulley (x,y,z): " + backLeftPulleyPositonViconFrame.x + ", " +
            backLeftPulleyPositonViconFrame.y + ", " + backLeftPulleyPositonViconFrame.z + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
    }

    private void ComputeShankBeltPulleyPositions()
    {
        // Compute the position of the front left pulley in Vicon frame
        pulleyPositionsRightShankBelt[0] = getMarkerAveragePositionInStartupFramesByName(rightShankBeltPulleyMarker);
        pulleyPositionsLeftShankBelt[0] = getMarkerAveragePositionInStartupFramesByName(leftShankBeltPulleyMarker);
    }


        



    private Vector3 ComputePulleyPositionFromNearAndDistalPointerMarkers(Vector3 nearPulleyPointerMarker, Vector3 distalPulleyPointerMarker)
    {
        // Get a unit vector from the distal to the near marker (which points to the pulley)
        Vector3 unitVectorTowardsPulley = nearPulleyPointerMarker - distalPulleyPointerMarker;
        unitVectorTowardsPulley = unitVectorTowardsPulley.normalized;
        // Multiply the unit vector towards the pulley by the pulley pointer length and add this 
        // to the near marker position. This is the pulley position.
        Vector3 pulleyPosition = nearPulleyPointerMarker + unitVectorTowardsPulley * pulleyPointerNearMarkerToPulleyDistances *
            convertMetersToMillimeters;

        // Return the pulley position.
        return pulleyPosition;
    }


    private void resetVariablesNeededForNextFrame()
    {

        // Clear any arrays that need to be reset
        Array.Clear(markersInSkeletonOcclusionStatus, 0, markersInSkeletonOcclusionStatus.Length);
    }



    private void ComputeTemporaryMarkerPositionsInBeltFrameAllBelts()
    {
        // If we're using the trunk belt, get the transformation matrix from Vicon frame to 
        // trunk belt frame, then compute all temp marker positions in belt frame
        if (skeletonName == trunkBeltOnlySkeletonName || skeletonName == pelvicAndTrunkBeltSkeletonName)
        {
            // Get transformation matrix, Vicon frame to trunk belt frame
            string logMessage = "Computing transformation matrix from Vicon frame to trunk belt frame";
            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
            Matrix4x4 transformationViconToTrunkBeltFrame = ConstructTransformationMatrixFromViconFrameToTrunkBeltFrame();

            // Convert the temporary trunk belt marker coordinates from Vicon frame to trunk belt frame
            // Use the average position of the trunk belt temporary markers in the startup frames.
            GetTrunkBeltTemporaryMarkerPositionsInTrunkBeltFrame(transformationViconToTrunkBeltFrame);
        }


        // If we're using the pelvic belt, compute the pelvic belt temporary marker positions in pelvic belt frame
        // NOT IMPLEMENTED YET!
        if (skeletonName == pelvicBeltOnlySkeletonName || skeletonName == pelvicAndTrunkBeltSkeletonName || skeletonName == pelvicAndShankBeltsSkeletonName)
        {
            // Get transformation matrix, Vicon frame to trunk belt frame
            string logMessage = "Computing transformation matrix from Vicon frame to pelvic belt frame";
            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
            Matrix4x4 transformationViconToPelvicBeltFrame = ConstructTransformationMatrixFromViconFrameToPelvicBeltFrame();
            // Convert the temporary pelvic belt marker coordinates from Vicon frame to pelvic belt frame.
            // Use the average position of the pelvic belt temporary markers in the startup frames.
            GetPelvicBeltTemporaryMarkerPositionsInPelvicBeltFrame(transformationViconToPelvicBeltFrame);
        }

        // If we're using the shank belts, compute the shank belt temporary marker positions in shank frame
        // NOT IMPLEMENTED YET!
        if (skeletonName == pelvicAndShankBeltsSkeletonName)
        {
            // Get transformation matrix, Vicon frame to trunk belt frame
            string logMessage = "Computing transformation matrix from Vicon frame to right shank belt frame";
            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
            Matrix4x4 transformationViconToRightShankBeltFrame = ConstructTransformationMatrixFromViconFrameToRightShankBeltFrame();
            // Convert the temporary pelvic belt marker coordinates from Vicon frame to pelvic belt frame.
            // Use the average position of the pelvic belt temporary markers in the startup frames.
            GetRightShankBeltTemporaryMarkerPositionsInRightShankFrame(transformationViconToRightShankBeltFrame);

            // Get transformation matrix, Vicon frame to trunk belt frame
            logMessage = "Computing transformation matrix from Vicon frame to left shank belt frame";
            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
            Matrix4x4 transformationViconToLeftShankBeltFrame = ConstructTransformationMatrixFromViconFrameToLeftShankBeltFrame();
            // Convert the temporary pelvic belt marker coordinates from Vicon frame to pelvic belt frame.
            // Use the average position of the pelvic belt temporary markers in the startup frames.
            GetLeftShankBeltTemporaryMarkerPositionsInLeftShankFrame(transformationViconToLeftShankBeltFrame);
        }

        // If we're using the 3D pelvic belt, compute the BOTTOM FR, FL, BR, BL pulley positions
        // NOT IMPLEMENTED YET!
    }



    // Function to construct trunk belt local frame and return the transformation matrix
    // from Vicon frame to trunk belt frame.
    private Matrix4x4 ConstructTransformationMatrixFromViconFrameToTrunkBeltFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        Vector3 trunkBeltBackCenterMarkerPos = getMarkerAveragePositionInStartupFramesByName(trunkBeltBackCenterMarker);
        Vector3 trunkBeltBackRightMarkerPos = getMarkerAveragePositionInStartupFramesByName(trunkBeltBackRightMarker);
        Vector3 trunkBeltBackLeftMarkerPos = getMarkerAveragePositionInStartupFramesByName(trunkBeltBackLeftMarker);

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

        Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        return transformationMatrixViconToLocal;
    }

    // Function to construct pelvic belt local frame and return the transformation matrix
    // from Vicon frame to pelvic belt frame.
    // NEED TO IMPLEMENT/UPDATE WITH PELVIC MARKER NAMES! NOT DONE!
    private Matrix4x4 ConstructTransformationMatrixFromViconFrameToPelvicBeltFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        Vector3 pelvicBeltBackCenterMarkerPos = getMarkerAveragePositionInStartupFramesByName(pelvicBeltBackCenterMarker);
        Vector3 pelvicBeltBackRightMarkerPos = getMarkerAveragePositionInStartupFramesByName(pelvicBeltBackRightMarker);
        Vector3 pelvicBeltBackLeftMarkerPos = getMarkerAveragePositionInStartupFramesByName(pelvicBeltBackLeftMarker);

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

        // take inverse to get global/vicon frame to local frame
        Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        return transformationMatrixViconToLocal;
    }


    private Matrix4x4 ConstructTransformationMatrixFromViconFrameToRightShankBeltFrame()
    {
        // Get origin of the right shank as midpoint of two malleoli markers
        Vector3 ankleMarkerPos = getMarkerAveragePositionInStartupFramesByName(rightAnkleMarkerName);
        Vector3 ankleMedialMarkerPos = getMarkerAveragePositionInStartupFramesByName(rightAnkleMedialMarkerName);
        Vector3 shankFrameOrigin = (ankleMarkerPos + ankleMedialMarkerPos) / (2.0f);

        // Get the x-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 shankXAxis= ankleMedialMarkerPos - ankleMarkerPos;

        // Get the y-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 kneeMarkerPos = getMarkerAveragePositionInStartupFramesByName(rightKneeMarkerName);
        Vector3 kneeMedialMarkerPos = getMarkerAveragePositionInStartupFramesByName(rightKneeMedialMarkerName);
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

        Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        return transformationMatrixViconToLocal;
    }

    private Matrix4x4 ConstructTransformationMatrixFromViconFrameToLeftShankBeltFrame()
    {
        // Get origin of the left shank as midpoint of two malleoli markers
        Vector3 ankleMarkerPos = getMarkerAveragePositionInStartupFramesByName(leftAnkleMarkerName);
        Vector3 ankleMedialMarkerPos = getMarkerAveragePositionInStartupFramesByName(leftAnkleMedialMarkerName);
        Vector3 shankFrameOrigin = (ankleMarkerPos + ankleMedialMarkerPos) / (2.0f);

        // Get the x-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 shankXAxis = ankleMarkerPos - ankleMedialMarkerPos;
        // Normalize x-axis
        shankXAxis = shankXAxis / shankXAxis.magnitude;

        // Get the y-axis of the shank = medially directed vector from lateral to medial malleolus
        Vector3 kneeMarkerPos = getMarkerAveragePositionInStartupFramesByName(leftKneeMarkerName);
        Vector3 kneeMedialMarkerPos = getMarkerAveragePositionInStartupFramesByName(leftKneeMedialMarkerName);
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

        Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        return transformationMatrixViconToLocal;
    }




    private void GetTrunkBeltTemporaryMarkerPositionsInTrunkBeltFrame(Matrix4x4 transformationViconToTrunkBeltFrame)
    {
        // For each temporary marker, listed by name
        for(int tempMarkerIndex = 0; tempMarkerIndex < attachmentPointsTrunkBeltMarkerNames.Length; tempMarkerIndex++)
        {
            // Get the average position of the marker in the startup frames
            Vector3 currentTempMarkerPosViconFrame = 
                getMarkerAveragePositionInStartupFramesByName(attachmentPointsTrunkBeltMarkerNames[tempMarkerIndex]);

            // TESTING ONLY - used to store the cable attachment points in Vicon frame to 
            // compute the structure matrix
            attachmentPointsTrunkBeltInViconFrame[tempMarkerIndex] = currentTempMarkerPosViconFrame;

            // Transform position to trunk belt frame and store position
            attachmentPointsTrunkBeltInBeltFrame[tempMarkerIndex] = transformationViconToTrunkBeltFrame.MultiplyPoint3x4(currentTempMarkerPosViconFrame);
        }
    }

    private void GetPelvicBeltTemporaryMarkerPositionsInPelvicBeltFrame(Matrix4x4 transformationViconToPelvicBeltFrame)
    {
        // For each temporary marker, listed by name
        for (int tempMarkerIndex = 0; tempMarkerIndex < attachmentPointsPelvicBeltMarkerNames.Length; tempMarkerIndex++)
        {
            // Get the average position of the marker in the startup frames
            Vector3 currentTempMarkerPosViconFrame =
                getMarkerAveragePositionInStartupFramesByName(attachmentPointsPelvicBeltMarkerNames[tempMarkerIndex]);

            // TESTING ONLY - used to store the cable attachment points in Vicon frame to 
            // compute the structure matrix
            attachmentPointsPelvicBeltInViconFrame[tempMarkerIndex] = currentTempMarkerPosViconFrame;

            // Transform position to pelvic belt frame and store position
            attachmentPointsPelvicBeltInBeltFrame[tempMarkerIndex] = transformationViconToPelvicBeltFrame.MultiplyPoint3x4(currentTempMarkerPosViconFrame);
        }
    }


    private void GetRightShankBeltTemporaryMarkerPositionsInRightShankFrame(Matrix4x4 transformationViconToRightShankBeltFrame)
    {
        // For each temporary marker, listed by name
        for (int tempMarkerIndex = 0; tempMarkerIndex < attachmentPointsRightShankBeltMarkerNames.Length; tempMarkerIndex++)
        {
            // Get the average position of the marker in the startup frames
            Vector3 currentTempMarkerPosViconFrame =
                getMarkerAveragePositionInStartupFramesByName(attachmentPointsRightShankBeltMarkerNames[tempMarkerIndex]);

            // TESTING ONLY - used to store the cable attachment points in Vicon frame to 
            // compute the structure matrix
            attachmentPointsRightShankBeltInViconFrame[tempMarkerIndex] = currentTempMarkerPosViconFrame;

            Debug.Log("Right shank temp marker has Vicon frame pos: (" + currentTempMarkerPosViconFrame.x + ", " + currentTempMarkerPosViconFrame.y + ", "
                + currentTempMarkerPosViconFrame.z + ")");

            // Transform position to pelvic belt frame and store position
            attachmentPointsRightShankBeltInBeltFrame[tempMarkerIndex] = transformationViconToRightShankBeltFrame.MultiplyPoint3x4(currentTempMarkerPosViconFrame);

            Debug.Log("Right shank temp marker has right shank frame pos: (" + attachmentPointsRightShankBeltInBeltFrame[tempMarkerIndex].x + ", " + attachmentPointsRightShankBeltInBeltFrame[tempMarkerIndex].y + ", "
    + attachmentPointsRightShankBeltInBeltFrame[tempMarkerIndex].z + ")");
        }
    }

    private void GetLeftShankBeltTemporaryMarkerPositionsInLeftShankFrame(Matrix4x4 transformationViconToLeftShankBeltFrame)
    {
        // For each temporary marker, listed by name
        for (int tempMarkerIndex = 0; tempMarkerIndex < attachmentPointsLeftShankBeltMarkerNames.Length; tempMarkerIndex++)
        {
            // Get the average position of the marker in the startup frames
            Vector3 currentTempMarkerPosViconFrame =
                getMarkerAveragePositionInStartupFramesByName(attachmentPointsLeftShankBeltMarkerNames[tempMarkerIndex]);

            // TESTING ONLY - used to store the cable attachment points in Vicon frame to 
            // compute the structure matrix
            attachmentPointsLeftShankBeltInViconFrame[tempMarkerIndex] = currentTempMarkerPosViconFrame;

            // Transform position to pelvic belt frame and store position
            attachmentPointsLeftShankBeltInBeltFrame[tempMarkerIndex] = transformationViconToLeftShankBeltFrame.MultiplyPoint3x4(currentTempMarkerPosViconFrame);
        }
    }



    private Vector3 GetTrunkBeltCenterFromTrunkBeltFixedMarkers()
    {
        // Get the position of the two front markers on the trunk belt and the back center marker
        Vector3 trunkBeltBackCenterMarkerPositionViconFrame = getMarkerAveragePositionInStartupFramesByName(trunkBeltBackCenterMarker);
        Vector3 trunkBeltFrontLeftMarkerPositionViconFrame = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontLeftMarker);
        Vector3 trunkBeltFrontRightMarkerPositionViconFrame = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontRightMarker);

        // Compute the midpoint of the two front markers
        Vector3 midpointFrontTrunkBelt = (trunkBeltFrontLeftMarkerPositionViconFrame + trunkBeltFrontRightMarkerPositionViconFrame) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        Vector3 midpointTrunkBelt = (trunkBeltBackCenterMarkerPositionViconFrame + midpointFrontTrunkBelt) / 2.0f;

        // Return the belt midpoint
        return midpointTrunkBelt;

    }

    private void ConstructStructureMatrix(Vector3 centerOfEndEffectorViconFrame, 
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
            Debug.Log("Structure matrix column " + pulleyIndex + " has force components (x,y,z): " +
                structureMatrixForceRows[pulleyIndex].x + ", " + structureMatrixForceRows[pulleyIndex].y + ", " +
                structureMatrixForceRows[pulleyIndex].z + ")");
            Debug.Log("Structure matrix column " + pulleyIndex + " has torque components (x,y,z): " +
                structureMatrixTorqueRows[pulleyIndex].x + ", " + structureMatrixTorqueRows[pulleyIndex].y + ", " +
                structureMatrixTorqueRows[pulleyIndex].z + ")");
        }

        // Eventually, we can return the structure matrix or store it in instance variables

    }



    //END: Functions called by Update()***********************************************************************************************







    //START: Public access getter/setter functions***********************************************************************************************


    public bool getAnkleAngleManagerReadyStatus()
    {
        return true;
    }




    //END: Public access getter/setter functions***********************************************************************************************












    //START: Functions called to access marker data************************************************************************************


    //Gets occlusion status and position for all markers needed in our current COM model.
    //Results are stored in instance variables.
    private bool getMarkerDataForAllMarkersNeededInSkeleton(string[] listOfMarkerNamesInModel)
    {
        //make a copy, in memory (not by reference), of the current marker positions
        float[] copyMarkersInSkeletonXPositions = (float[])markersInSkeletonXPositions.Clone();
        float[] copyMarkersInSkeletonYPositions = (float[])markersInSkeletonYPositions.Clone();

        bool markerDataIsOld = false; //assume marker data is fresh, and update after checking

        //Get the frame number that was most recently accessed
        uint frameNumber = markerDataDistributorScript.getLastRetrievedViconFrameNumber();
        mostRecentlyAccessedViconFrameNumber = frameNumber;

        for (uint index = 0; index < listOfMarkerNamesInModel.Length; index++) //for each marker in the model
        {
            string markerName = listOfMarkerNamesInModel[index];

            var markerResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(markerName);
            markersInSkeletonOcclusionStatus[index] = markerResultTuple.Item1;
            markersInSkeletonXPositions[index] = markerResultTuple.Item2;
            markersInSkeletonYPositions[index] = markerResultTuple.Item3;
            markersInSkeletonZPositions[index] = markerResultTuple.Item4;
        }

        //if the marker data is the same, then new data is not ready, so don't run the rest of the pipeline
        if (Enumerable.SequenceEqual(copyMarkersInSkeletonXPositions, markersInSkeletonXPositions) &&
            Enumerable.SequenceEqual(copyMarkersInSkeletonYPositions, markersInSkeletonYPositions))
        {
            markerDataIsOld = true;
        }

        return markerDataIsOld;
    }


    //This function is used to get the average position of all the model markers across the stored startup frames. 
    private Vector3[] getAveragePositionOfAllMarkersInStartupFrames()
    {
        Vector3[] averagePositionOfAllMarkersInStartupFrames = new Vector3[namesOfAllMarkersInSkeleton.Length];
        for (int markerInModelIndex = 0; markerInModelIndex < namesOfAllMarkersInSkeleton.Length; markerInModelIndex++)
        {
            averagePositionOfAllMarkersInStartupFrames[markerInModelIndex] = getMarkerAveragePositionInStartupFramesByName(namesOfAllMarkersInSkeleton[markerInModelIndex]);
        }

        return averagePositionOfAllMarkersInStartupFrames;
    }


    //During setup, we store a few dozen marker frames.
    //This function allows us to compute the average position of a marker across these "setup frames,"
    //by name.
    private Vector3 getMarkerAveragePositionInStartupFramesByName(string markerName)
    {

        int markerIndexInEachArray = Array.IndexOf(namesOfAllMarkersInSkeleton, markerName);
        //Compute mean marker positions by summing all stored positions and dividing by
        //number of observations
        float markerXPos = 0.0f;
        float markerYPos = 0.0f;
        float markerZPos = 0.0f;


        //X-axis positions
        for (int frameIndex = 0; frameIndex < numberOfSetupMarkerFrames; frameIndex++)//for each frame stored for setup
        {

            markerXPos += setupMarkerFramesXPos[frameIndex][markerIndexInEachArray];
            markerYPos += setupMarkerFramesYPos[frameIndex][markerIndexInEachArray];
            markerZPos += setupMarkerFramesZPos[frameIndex][markerIndexInEachArray];
        }

        //now divide by number of observations to get the mean positions
        markerXPos = markerXPos / numberOfSetupMarkerFrames;
        markerYPos = markerYPos / numberOfSetupMarkerFrames;
        markerZPos = markerZPos / numberOfSetupMarkerFrames;

        //print useful logging information about average marker positions in the stored setup frames
        string logMessage = "Setup: Marker " + markerName + " has coordinates (x,y,z): (" +
            markerXPos + "," + markerYPos + "," + markerZPos + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        //create and return a Vector3 representing the position
        return new Vector3(markerXPos, markerYPos, markerZPos);
    }


    //Computes a right-handed cross-product so that we can work in the Vicon coordinate system
    private Vector3 getRightHandedCrossProduct(Vector3 leftVector, Vector3 rightVector)
    {
        float newXValue = leftVector.y * rightVector.z - leftVector.z * rightVector.y;
        float newYValue = leftVector.z * rightVector.x - leftVector.x * rightVector.z;
        float newZValue = leftVector.x * rightVector.y - leftVector.y * rightVector.x;

        return new Vector3(newXValue, newYValue, newZValue);

    }


    //Given the three normalized/unit axes of a local coordinate system expressed in target frame
    // and the translation FROM the target coordinate system
    // TO the local coordinate system defined in the target coordinate system, construct a transformation matrix
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


    // This function tells the General Data Recorder object to save stored marker data to file, then clear any saved marker data. 
    // In general, it is called at the end of a task block by the task's level manager so that we have separate saved files for each block.
    public void tellDataRecorderToSaveStoredDataToFile(string subdirectoryName, string fileNameStub)
    {
        //set the subdirectory name 
        generalDataRecorderScript.setCsvMarkerDataSubdirectoryName(subdirectoryName);
        // append to the file name stub, marking the data as marker/COM data
        string fileName = fileNameStub + "_Marker.csv";

        //set the file name
        generalDataRecorderScript.setCsvMarkerDataFileName(fileName);

        //write the stored marker data to file
        generalDataRecorderScript.writeMarkerDataToFile();

    }


    //END: Functions called to access marker data************************************************************************************







    //Start: Functions for 3D reconstruction of missing markers**************************************************************



    //End reconstruction functions ********************************************************************************************




    // START: marker data storage functions ***********************************************************************************


    private void SetOutputDataDirectoryAndFileName()
    {
        // 1.) Data subdirectory naming for the output data
        // Get the date information for this moment, and format the date and time substrings to be directory-friendly
        DateTime localDate = DateTime.Now;
        string dateString = localDate.ToString("d");
        dateString = dateString.Replace("/", "_"); //remove slashes in date as these cannot be used in a file name (specify a directory).

        // Build the name of the subdirectory that will contain all of the output files for the output data
        string subdirectoryString = "Subject" + subjectSpecificDataScript.getSubjectNumberString() + "/" + thisTaskName + "/" + dateString + "/";

        //set the frame data and the task-specific trial subdirectory name (will go inside the CSV folder in Assets)
        // Note: we use the "Excursion Performance Summary" functions of the GeneralDataRecorder object to store our data in this script.
        generalDataRecorderScript.setCsvExcursionPerformanceSummarySubdirectoryName(subdirectoryString);

        // 4.) Set the output file name. We'll always use the same one (each day) for the structure matrix data we have computed
        // Note: we use the "Excursion Performance Summary" functions of the GeneralDataRecorder object to store our data in this script.
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryFileName(outputFileName);


        // REPEAT these steps for the Vive-based data. 
        // We'll store the Vive-based structure matrix file using the frame data
        generalDataRecorderScript.setCsvFrameDataSubdirectoryName(subdirectoryString);
        generalDataRecorderScript.setCsvFrameDataFileName(outputFileNameVive);


    }


    //The names of the headers (which will specify the names of the markers or segments in question) are 
    //specified here, called by the Start() function.
    private void SetOutputDataColumnNaming()
    {
        //Since we only do this once, we're not concerned about speed. Add all of the string arrays to a list of strings, 
        //then convert the finalized list back to an array of strings.
        List<string> csvHeaderNamesAsList = new List<string>();

        if (usingTrunkBelt)
        {
            // Pulley positions for trunk belt (X,Y,Z)
            string[] trunkPulleyXPositions = appendStringToAllElementsOfStringArray(pulleyNamesTrunkBelt, "_POS_X");
            string[] trunkPulleyYPositions = appendStringToAllElementsOfStringArray(pulleyNamesTrunkBelt, "_POS_Y");
            string[] trunkPulleyZPositions = appendStringToAllElementsOfStringArray(pulleyNamesTrunkBelt, "_POS_Z");

            // Trunk belt temporary marker positions in trunk belt frame (X,Y,Z), if using
            string[] trunkTempMarkersXPositions = appendStringToAllElementsOfStringArray(attachmentPointsTrunkBeltMarkerNames, "_POS_X");
            string[] trunkTempMarkersYPositions = appendStringToAllElementsOfStringArray(attachmentPointsTrunkBeltMarkerNames, "_POS_Y");
            string[] trunkTempMarkersZPositions = appendStringToAllElementsOfStringArray(attachmentPointsTrunkBeltMarkerNames, "_POS_Z");

            // Store 
            csvHeaderNamesAsList.AddRange(trunkPulleyXPositions);
            csvHeaderNamesAsList.AddRange(trunkPulleyYPositions);
            csvHeaderNamesAsList.AddRange(trunkPulleyZPositions);
            csvHeaderNamesAsList.AddRange(trunkTempMarkersXPositions);
            csvHeaderNamesAsList.AddRange(trunkTempMarkersYPositions);
            csvHeaderNamesAsList.AddRange(trunkTempMarkersZPositions);
        }

        if (usingPelvicBelt)
        {
            // Pulley positions for pelvic belt (X,Y,Z)
            string[] pelvicPulleyXPositions = appendStringToAllElementsOfStringArray(pulleyNamesPelvicBelt, "_POS_X");
            string[] pelvicPulleyYPositions = appendStringToAllElementsOfStringArray(pulleyNamesPelvicBelt, "_POS_Y");
            string[] pelvicPulleyZPositions = appendStringToAllElementsOfStringArray(pulleyNamesPelvicBelt, "_POS_Z");

            // Pelvic belt temporary marker positions in pelvic belt frame (X,Y,Z), if using
            string[] pelvicTempMarkersXPositions = appendStringToAllElementsOfStringArray(attachmentPointsPelvicBeltMarkerNames, "_POS_X");
            string[] pelvicTempMarkersYPositions = appendStringToAllElementsOfStringArray(attachmentPointsPelvicBeltMarkerNames, "_POS_Y");
            string[] pelvicTempMarkersZPositions = appendStringToAllElementsOfStringArray(attachmentPointsPelvicBeltMarkerNames, "_POS_Z");

            // Store
            csvHeaderNamesAsList.AddRange(pelvicPulleyXPositions);
            csvHeaderNamesAsList.AddRange(pelvicPulleyYPositions);
            csvHeaderNamesAsList.AddRange(pelvicPulleyZPositions);
            csvHeaderNamesAsList.AddRange(pelvicTempMarkersXPositions);
            csvHeaderNamesAsList.AddRange(pelvicTempMarkersYPositions);
            csvHeaderNamesAsList.AddRange(pelvicTempMarkersZPositions);
        }

        if (usingShankBelts)
        {
            // Pulley positions for right shank belt (X,Y,Z)
            string[] rightShankPulleyXPositions = appendStringToAllElementsOfStringArray(pulleyNamesRightShankBelt, "_POS_X");
            string[] rightShankPulleyYPositions = appendStringToAllElementsOfStringArray(pulleyNamesRightShankBelt, "_POS_Y");
            string[] rightShankPulleyZPositions = appendStringToAllElementsOfStringArray(pulleyNamesRightShankBelt, "_POS_Z");

            // Right shank belt temporary marker positions in right shank belt frame (X,Y,Z), if using
            string[] rightShankTempMarkersXPositions = appendStringToAllElementsOfStringArray(attachmentPointsRightShankBeltMarkerNames, "_POS_X");
            string[] rightShankTempMarkersYPositions = appendStringToAllElementsOfStringArray(attachmentPointsRightShankBeltMarkerNames, "_POS_Y");
            string[] rightShankTempMarkersZPositions = appendStringToAllElementsOfStringArray(attachmentPointsRightShankBeltMarkerNames, "_POS_Z");

            // Pulley positions for left shank belt (X,Y,Z)
            string[] leftShankPulleyXPositions = appendStringToAllElementsOfStringArray(pulleyNamesLeftShankBelt, "_POS_X");
            string[] leftShankPulleyYPositions = appendStringToAllElementsOfStringArray(pulleyNamesLeftShankBelt, "_POS_Y");
            string[] leftShankPulleyZPositions = appendStringToAllElementsOfStringArray(pulleyNamesLeftShankBelt, "_POS_Z");

            // Left shank belt temporary marker positions in left shank belt frame (X,Y,Z), if using
            string[] leftShankTempMarkersXPositions = appendStringToAllElementsOfStringArray(attachmentPointsLeftShankBeltMarkerNames, "_POS_X");
            string[] leftShankTempMarkersYPositions = appendStringToAllElementsOfStringArray(attachmentPointsLeftShankBeltMarkerNames, "_POS_Y");
            string[] leftShankTempMarkersZPositions = appendStringToAllElementsOfStringArray(attachmentPointsLeftShankBeltMarkerNames, "_POS_Z");

            // Store Right shank
            csvHeaderNamesAsList.AddRange(rightShankPulleyXPositions);
            csvHeaderNamesAsList.AddRange(rightShankPulleyYPositions);
            csvHeaderNamesAsList.AddRange(rightShankPulleyZPositions);
            csvHeaderNamesAsList.AddRange(rightShankTempMarkersXPositions);
            csvHeaderNamesAsList.AddRange(rightShankTempMarkersYPositions);
            csvHeaderNamesAsList.AddRange(rightShankTempMarkersZPositions);
            // Store Left shank
            csvHeaderNamesAsList.AddRange(leftShankPulleyXPositions);
            csvHeaderNamesAsList.AddRange(leftShankPulleyYPositions);
            csvHeaderNamesAsList.AddRange(leftShankPulleyZPositions);
            csvHeaderNamesAsList.AddRange(leftShankTempMarkersXPositions);
            csvHeaderNamesAsList.AddRange(leftShankTempMarkersYPositions);
            csvHeaderNamesAsList.AddRange(leftShankTempMarkersZPositions);
        }

        //now convert back to a string array, as that's what we use in the General Data Recorder object.
        string[] csvHeaderNames = csvHeaderNamesAsList.ToArray();

        //send the .csv file column header names to the General Data Recorder object
        // Note: we use the GeneralDataRecorder object's "Excursion Performance Summary" functions to output our data, even though the
        // name no longer matches the current objective.
        generalDataRecorderScript.setCsvExcursionPerformanceSummaryRowHeaderNames(csvHeaderNames);

        // Use the same headers for the Vive-based structure matrix data (which uses the "frame" data functions).
        generalDataRecorderScript.setCsvFrameDataRowHeaderNames(csvHeaderNames);


    }

    private void StoreDataForStructureMatrixComputationInDataRecorderObject()
    {
        //Note: the names of the headers (which will specify the names of the markers or segments in question) are 
        //specified during setup.

        //create a list to store the floats
        List<float> computedDataToStore = new List<float>();

        if (usingTrunkBelt)
        {
            // Store pulley positions and temp markers for trunk belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsTrunkBelt,
                attachmentPointsTrunkBeltInBeltFrame, computedDataToStore);
        }

        if (usingPelvicBelt)
        {
            // Store pulley positions and temp markers for pelvic belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsPelvicBelt,
                attachmentPointsPelvicBeltInBeltFrame, computedDataToStore);
        }

        if (usingShankBelts)
        {
            // Store pulley positions and temp markers for right shank belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsRightShankBelt,
                attachmentPointsRightShankBeltInBeltFrame, computedDataToStore);

            // Store pulley positions and temp markers for left shank belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsLeftShankBelt,
                attachmentPointsLeftShankBeltInBeltFrame, computedDataToStore);
        }

        //send this frame of data to the General Data Recorder object to be stored on dynamic memory until it is written to file
        generalDataRecorderScript.storeRowOfExcursionPerformanceSummaryData(computedDataToStore.ToArray());
    }

    private void StoreDataForViveStructureMatrixComputationInDataRecorderObject()
    {
        //Note: the names of the headers (which will specify the names of the markers or segments in question) are 
        //specified during setup.

        //create a list to store the floats
        List<float> computedDataToStore = new List<float>();

        if (usingTrunkBelt)
        {
            // Store pulley positions and temp markers for trunk belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsTrunkBeltViveReferenceFrame,
                attachmentPointsTrunkBeltInBeltViveTrackerFrame, computedDataToStore);
        }

        if (usingPelvicBelt)
        {
            // Store pulley positions and temp markers for pelvic belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsPelvicBeltViveReferenceFrame,
                attachmentPointsPelvicBeltInBeltViveTrackerFrame, computedDataToStore);
        }

        if (usingShankBelts)
        {
            // Store pulley positions and temp markers (temp marker = attachment point!) for right shank belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsRightShankBelt,
                attachmentPointsRightShankBeltInBeltFrame, computedDataToStore);

            // Store pulley positions and temp markers (temp marker = attachment point!) for left shank belt
            AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(pulleyPositionsLeftShankBelt,
                attachmentPointsLeftShankBeltInBeltFrame, computedDataToStore);
        }

        //send this frame of data to the General Data Recorder object to be stored on dynamic memory until it is written to file
        generalDataRecorderScript.storeRowOfFrameData(computedDataToStore);
    }


    private List<float> AppendPulleyPositionsAndTempMarkerPositionsInLocalFrameToOutputData(Vector3[] pulleyPositions, 
        Vector3[] tempMarkerPositionsInBeltFrame, List<float> outputData)
    {
        // Pulley positions for belt (X,Y,Z)
        // First, extract the X,Y,Z components into their own List<float>
        List<float> pulleyPositionsBeltX = new List<float>();
        List<float> pulleyPositionsBeltY = new List<float>();
        List<float> pulleyPositionsBeltZ = new List<float>();
        // For each pulley
        for (int pulleyIndex = 0; pulleyIndex < pulleyPositions.Length; pulleyIndex++)
        {
            // Get the current pulley position as a Vector3
            Vector3 currentPulleyPosition = pulleyPositions[pulleyIndex];

            // Store the X,Y,Z components in their own listbeltTempMarkerPo
            pulleyPositionsBeltX.Add(currentPulleyPosition.x);
            pulleyPositionsBeltY.Add(currentPulleyPosition.y);
            pulleyPositionsBeltZ.Add(currentPulleyPosition.z);
        }

        // Belt temporary marker positions (X,Y,Z) in belt frame! Temp markers = belt cable attachment points.
        List<float> beltTempMarkerPositionsX = new List<float>();
        List<float> beltTempMarkerPositionsY = new List<float>();
        List<float> beltTempMarkerPositionsZ = new List<float>();
        // For each pulley
        for (int tempMarkerIndex = 0; tempMarkerIndex < tempMarkerPositionsInBeltFrame.Length; tempMarkerIndex++)
        {
            Vector3 currentTempMarkerPositionInBeltFrame = tempMarkerPositionsInBeltFrame[tempMarkerIndex];
            // Store the X,Y,Z components in their own list
            beltTempMarkerPositionsX.Add(currentTempMarkerPositionInBeltFrame.x);
            beltTempMarkerPositionsY.Add(currentTempMarkerPositionInBeltFrame.y);
            beltTempMarkerPositionsZ.Add(currentTempMarkerPositionInBeltFrame.z);

            Debug.Log("tempMarkerPositionsInBeltFrame " + currentTempMarkerPositionInBeltFrame);
            Debug.Log("tempMarkerPositionsInBeltFrame Length " + tempMarkerPositionsInBeltFrame.Length);
        }

        // Append the pulley x,y,z and then the temp marker x,y,z to the output data
        // Pulley positions
        outputData.AddRange(pulleyPositionsBeltX);
        outputData.AddRange(pulleyPositionsBeltY);
        outputData.AddRange(pulleyPositionsBeltZ);
        // Temp markers
        outputData.AddRange(beltTempMarkerPositionsX);
        outputData.AddRange(beltTempMarkerPositionsY);
        outputData.AddRange(beltTempMarkerPositionsZ);

        // Return output data
        return outputData;
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


    private void StorePulleyPositionsAndLocalFrameBeltTempMarkerPositionsToFile()
    {
        // First, send a single row of data with the needed data to the General Data Recorder
        StoreDataForStructureMatrixComputationInDataRecorderObject();

        // Then, tell the General Data Recorder to write the data to file
        generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();
    }

    private void StorePulleyPositionsAndLocalFrameAttachmentsInViveFrameToFile()
    {
        // First, send a single row of data with the needed data to the General Data Recorder
        StoreDataForViveStructureMatrixComputationInDataRecorderObject();

        // Then, tell the General Data Recorder to write the data to file
        generalDataRecorderScript.writeFrameDataToFile();
    }



    // END: marker data storage functions ***********************************************************************************





    //START: Debugging functions ********************************************************************************************************

    //Use this function to print messages to console that will only appear when #ENABLE_LOGS
    //is defined. 
    [Conditional("ENABLE_LOGS")]
    private void printLogMessageToConsoleIfDebugModeIsDefined(string logMessage)
    {
        Debug.Log(logMessage); //log the message
    }



    //Use this function to print warnings and errors to console that will only appear when #ENABLE_CUSTOM_WARNINGS_ERRORS
    //is defined. 
    //logType values: "WARNING" is a warning, "ERROR" is an error
    [Conditional("ENABLE_CUSTOM_WARNINGS_ERRORS")]
    private void printWarningOrErrorToConsoleIfDebugModeIsDefined(string logType, string logMessage)
    {
        if (logType == logAWarningSpecifier) //if a warning is being logged
        {
            Debug.LogWarning(logMessage);
        }
        else if (logType == logAnErrorSpecifier) //if an error is being logged
        {
            Debug.LogError(logMessage);
        }
    }


    //[Conditional("ENABLE_LOGS")]
    private void printStopwatchTimeElapsedToConsole()
    {
        // Get the elapsed time as a TimeSpan value.
        TimeSpan ts = stopWatch.Elapsed;

        // Format and display the TimeSpan value.
        string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:0000}",
            ts.Hours, ts.Minutes, ts.Seconds,
            ts.Milliseconds / 10);
        printLogMessageToConsoleIfDebugModeIsDefined("RunTime for Update() call in ManageCenterOfMassScript.cs was " + elapsedTime);
    }

    //End: Debugging functions *********************************************************************************************************









}


