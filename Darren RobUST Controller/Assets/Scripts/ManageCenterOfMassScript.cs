/* 
 * 
 * 
 * Notes for self: currently, Update()/FixedUpate() loop (the main functions of this script)
 * run incredibly fast averaging between 0.05 and 0.1 ms. So, shouldn't have to worry about 
 * performance/framerate issues here. However, as of 4/22/22, seem to see intermittent slower loops and I'm not sure why...
 */
#define ENABLE_CUSTOM_WARNINGS_ERRORS //recommended that this define is always present so we can see user-defined warnings and errors
//#define ENABLE_LOGS //may want to comment out this define to suppress user-defined logging

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using UnityEngine.UI;
using UnityEngine;

using ViconDataStreamSDK.CSharp;


//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;



// Used across scripts (e.g., also used in COM manager), but defined here because the class relates to universal experiment settings.
public enum skeletonNameEnum
{
    FiveSegmentComModelWithJointAngles,
    FeetLegsTrunkAndOnlyPelvicBelt
}


public class ManageCenterOfMassScript : MonoBehaviour
{


    //Start: nested classes *****************************************************************

    public class bodySegment
    {
        private string segmentType; // stores the segment type (e.g. "Thigh")
        private string segmentName; // stores the segment name (e.g. "LeftThigh");
        private string subjectSex; // the sex of the subject ("Man" or "Woman")
        private float fractionOfTotalBodyMass; //stores the fraction of the total body mass (e.g. 0.10 indicates 10%)
        private float distanceFromProximalMarkerToComAsFractionOfSegmentLength; // how far along the line from proximal to                     
                                                                                // distal marker the COM is located, as a fraction
                                                                                // of the total segment length
        private string nameOfProximalSegmentMarkerForFindingCom; // if the segment COM is located along a line between two markers/points,
                                                                 // this is the name of the "proximal" one
        private string nameOfDistalSegmentMarkerForFindingCom; // if the segment COM is located along a line between two markers/points,
                                                               // this is the name of the "distal" one
                                                               //The constructor 
        public bodySegment(string nameOfSegmentType, string nameOfSegment, float totalBodyMassFraction,
            string sexOfSubject, string proximalMarkerName, string distalMarkerName,
            Dictionary<string, float> maleComSegmentDistances, Dictionary<string, float> femaleComSegmentDistances)
        {
            //set private variables
            segmentType = nameOfSegmentType;
            segmentName = nameOfSegment;
            subjectSex = sexOfSubject;
            fractionOfTotalBodyMass = totalBodyMassFraction;
            nameOfProximalSegmentMarkerForFindingCom = proximalMarkerName;
            nameOfDistalSegmentMarkerForFindingCom = distalMarkerName;

            //use the subject's sex to determine the COM's location along the segment length
            if (sexOfSubject == "Man") //if the subject is male
            {
                distanceFromProximalMarkerToComAsFractionOfSegmentLength = maleComSegmentDistances[segmentName]; // get the COM distance along the segment
                                                                                                                 // using the male dictionary
            }
            else if (sexOfSubject == "Woman")
            {
                distanceFromProximalMarkerToComAsFractionOfSegmentLength = femaleComSegmentDistances[segmentName]; // get the COM distance along the segment
                                                                                                                   // using the female dictionary
            }
            else //if the subject has an incorrect sex specifier string
            {
                //raise an exception
            }
        }

        //Getter functions 

        public string getProximalMarkerName()

        {
            return nameOfProximalSegmentMarkerForFindingCom;
        }

        public string getDistalMarkerName()
        {
            return nameOfDistalSegmentMarkerForFindingCom;
        }

        public float getComLengthAlongSegmentFraction()
        {
            return distanceFromProximalMarkerToComAsFractionOfSegmentLength;
        }

        public float getFractionOfTotalBodyMass()
        {
            return fractionOfTotalBodyMass;
        }

    }

    //End: nested classes *****************************************************************



    //Start: Dictionaries, including the de Leva masses*******************************************

    private Dictionary<string, float> deLevaMaleSegmentMassesAsPercentOfTotalBodyMass = new Dictionary<string, float>() {
        {"Head", 6.94f / 100.0f},
        {"Trunk", 43.46f / 100.0f},
        {"UpperTrunk", 15.96f / 100.0f},
        {"MiddleTrunk", 16.33f / 100.0f},
        {"LowerTrunk", 11.17f / 100.0f},
        {"Arm", 2.71f / 100.0f},
        {"Forearm", 1.62f / 100.0f},
        {"Hand", 0.61f / 100.0f},
        {"Thigh", 14.16f / 100.0f},
        {"Shank", 4.33f / 100.0f},
        {"Foot", 1.37f / 100.0f},
    };

    private Dictionary<string, float> deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass = new Dictionary<string, float>() {
        {"Head", 6.68f / 100.0f},
        {"Trunk", 42.57f / 100.0f},
        {"UpperTrunk", 15.45f / 100.0f},
        {"MiddleTrunk", 14.65f / 100.0f},
        {"LowerTrunk", 12.47f / 100.0f},
        {"Arm", 2.55f / 100.0f},
        {"Forearm", 1.38f / 100.0f},
        {"Hand", 0.56f / 100.0f},
        {"Thigh", 14.78f / 100.0f},
        {"Shank", 4.81f / 100.0f},
        {"Foot", 1.29f / 100.0f},
    };

    private Dictionary<string, float> TisserandMaleDistancesFromProximalMarker = new Dictionary<string, float>()
    {
        { "Trunk", 0.3705f },
        { "Arm", 0.5437f },
        { "Forearm", 0.6364f },
        { "Thigh", 0.4260f },
        { "Shank", 0.5369f }
    };

    private Dictionary<string, float> TisserandFemaleDistancesFromProximalMarker = new Dictionary<string, float>()
    {
        { "Trunk", 0.3806f },
        { "Arm", 0.5664f },
        { "Forearm", 0.6377f },
        { "Thigh", 0.3812f },
        { "Shank", 0.5224f }
    };


    //End Dictionaries*******************************************


    //key public game objects
    public skeletonNameEnum markerSkeletonToUse; // An experimenter-selected enum that sets which marker skeleton is used.
    public GameObject markerDataDistributor; //the GameObject that reads marker data each frame and has getter functions to get marker data by name. Accessses data via the dataStream prefab from the Unity/Vicon plugin.
    public Camera sceneCamera; //the camera that visulizes the scene. Used for converting viewport coordinates to world coordinates.
    private string subjectSex; //the sex of the subject ("Man" or "Woman")
    public GameObject forcePlateDataAccessObject;
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;
    public bool pingStructureMatrixServiceWhenNewMarkerDataReady;

    // subject-specific data
    public GameObject subjectSpecificDataObject; //the game object that contains the subject-specific data like height, weight, subject number, etc. 
    private SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data
    private string maleSexName = "Man";
    private string femaleSexName = "Woman";

    //key private objects
    private UnityVicon.ReadViconDataStreamScript markerDataDistributorScript; //the callable script of the GameObject that makes marker data available.

    //logic flags that control program flow
    private bool storingFramesForSetupFlag; //if true, it means we're saving each marker frame's data. This data is used for setup.
    private bool setupComplete = false;
    private bool comManagerHasRunUpdateAtLeastOnceFlag = false; // a more stringent version of setupComplete. Other programs may need to call the comManager once 
                                                                // the Update() loop has run at least once.

    //debugging and testing 
    Stopwatch stopWatch = new Stopwatch();
    Stopwatch tempStopWatch = new Stopwatch();
    private const string logAMessageSpecifier = "LOG";
    private const string logAWarningSpecifier = "WARNING";
    private const string logAnErrorSpecifier = "ERROR";

    //logType values: "LOG" is logging text, "WARNING" is a warning, "ERROR" is an error

    //frame number for the most recent frame accessed
    private uint mostRecentlyAccessedViconFrameNumber;

    // Tracking rate of retrieval of Vicon data. 
    // Rate = 1/(mostRecentTime - lastTime)
    private float mostRecentFreshViconDataTimeStamp; // When Vicon data was last retrieved (and was fresh) from the data stream [s]
    private float previousViconDataTimeStamp = 0.0f; // The previous time Vicon data was retrieved (and was fresh) from the data stream [s]

    //marker frames stored from setup
    private uint numberOfSetupFramesAlreadyStored = 0; //keep track of how many frames we have stored for setup.
    private const uint numberOfSetupMarkerFrames = 1; //how many marker frames to store for setup.
    private List<bool[]> setupMarkerFramesOcclusionStatus = new List<bool[]>();
    private List<float[]> setupMarkerFramesXPos = new List<float[]>();
    private List<float[]> setupMarkerFramesYPos = new List<float[]>();
    private List<float[]> setupMarkerFramesZPos = new List<float[]>();
    Vector3[] averagePositionOfModelMarkersInStartupFrames; //stores the average position of all of the model markers across the startup frames
    //virtual markers stored from setup
    private List<Vector3[]> setupHipJointPositions = new List<Vector3[]>(); //stores the locations of the hip joint centers (HJCs) in all of the setup frames

    //model to use for computing COM
    public string multisegmentModelName;  // options = "trunk" (trunk only), "5_trunk_thighs_shanks", "best_5" (best of 5 segment model)
    private string[] markersInModel;  // a list of all the markers needed for the model. Initialized during setup.
    private bool[] markersInModelOcclusionStatus; // whether or not the markers in the model are occluded this frame
    private float[] markersInModelXPositions;  // x-axis positions of markers in model this frame (in Vicon coords)
    private float[] markersInModelYPositions;  // y-axis positions of markers in model this frame (in Vicon coords)
    private float[] markersInModelZPositions;  // z-axis positions of markers in model this frame (in Vicon coords)
    private string[] segmentsInModel; // a list of all the segments used in the model. Initialized during setup.
    private bodySegment[] segmentsInModelObjectArray; // an array containing representations of the segments listed
                                                      // in the string array of segment names, segmentsInModel
    private Vector3[] segmentComPositions; // an array of each segment in the model's COM position, as of their most recent computation
    // Rigid body reconstruction
    private string[] rigidBodiesToReconstructForModel; // a list of rigid bodies (segments or non-segments) for which we'll reconstruct any missing markers, if possible
    private List<string[]> rigidBodiesToReconstructForModelMarkerNames; //a list containing string arrays of the names of every marker in
                                                                        //rigid bodies with markers that must be reconstructed.

    // Array for saving reconstructed markers
    private float[] reconstructedMarkersInModelXPositions;
    private float[] reconstructedMarkersInModelYPositions;
    private float[] reconstructedMarkersInModelZPositions;

    // Joint angle computation
    private string[] rigidBodyNamesForJointAngleComputation; // a list of rigid bodies (segments) that will be involved in joint angle computations

    //Marker reconstruction variables 
    bool[] markerInModelWasReconstructedThisFrameFlags; //a bool array indicating whether or not the marker was reconstructed this frame
    List<string> namesOfAllReconstructedMarkers = new List<string>();
    List<Vector3> positionsOfAllReconstructedMarkers = new List<Vector3>();

    // Whether or not segment COM was calculated and contributed to whole-body COM estimation this frame
    private bool[] segmentUsedForComEstimationThisFrameFlags;

    // Whether or not the  segment COM had to be approximated with a simplified model. 
    // Currently, we only do this for the thigh and shank if the knee marker cannot be reconstructed. 
    private bool[] segmentComEstimatedWithSimplifiedModelThisFrameFlags;

    //Segment COM estimates
    private Vector3[] segmentEstimatedComPositions;
    
    //physical parameters measured during setup, including
    //locations of the boundary of stability, interASIS width, etc.
    //All reported in Vicon distance units [mm]
    private float interAsisWidth;
    private float rightThighLength;
    private float leftThighLength;
    private float rightShankLength;
    private float leftShankLength;
    private float bodyAsInvertedPendulumLegLength; //the length parameter used in the inverted pendulum model, implicit in XCOM calculations
    private float verticalDistanceMidAnkleToMidTrunkBeltInMeters; // the length parameter used for computing trunk belt-generated torques at the ankle
    //key parameter: the center of mass data!
    private float mostRecentComXPosition; //the most recent x-coordinate of the subject's COM in Vicon coords
    private float mostRecentComYPosition; //the most recent y-coordinate of the subject's COM in Vicon coords
    private Vector3 mostRecentTotalBodyComPosition; //the most recent coordinates of the subject's COM in Vicon coords

    //virtual markers = key positions computed from marker positions, but not themselves marked with physical markers
    private string[] virtualMarkerNames;
    private bool[] virtualMarkersCouldBeCalculatedThisFrameFlag; //a boolean array indicating whether or not the
                                                                 //corresponding virtual marker could be built.
    private Vector3[] virtualMarkerPositions; //stores the most recent position of the virtual markers (in Vicon coords)
    private const string recontructHipJointCentersString = "midpointOfHipJointCenters";
    private const string nameOfShoulderCenterVirtualMarker = "midpointOfShoulders";
    private string[] markerNamesForHipJointCenters; //names of the physical markers needed to compute this virtual marker
    private string[] markerNamesForShoulderCenter; //names of the physical markers needed to compute this virtual marker
    private string[] markerNamesForKneeJointCenter; // names of the physical markers needed to compute this virtual marker
    private string[] markerNamesForAnkleJointCenter; // names of the physical markers needed to compute this virtual marker

    //Hip joint centers. Since these are virtual markers computed along with the HJC midpoint,
    //we store the most recent calculation of their position in separate vectors
    private string[] hipJointMarkerNames; // the names of the VIRTUAL hip joint centers (in same order as hipJointMarkerPositions)
    private Vector3[] hipJointMarkerPositions; //stores the most recent position of the hip joint virtual markers (in Vicon coords)
    private const string nameOfLeftHjcVirtualMarker = "leftHipJointCenter";
    private const string nameOfRightHjcVirtualMarker = "rightHipJointCenter";
    private Vector3 leftHjcPosition;
    private Vector3 rightHjcPosition;

    //names of pelvis markers
    private const string frontLeftPelvisMarkerName = "LASI";
    private const string frontRightPelvisMarkerName = "RASI";
    private const string backLeftPelvisMarkerName = "LPSI";
    private const string backRightPelvisMarkerName = "RPSI";
    private const string backCenterPelvisMarkerName = "PelvisBackCenter";

    // names of trunk markers
    private const string trunkBeltBackMiddleMarkerName = "TrunkBeltMiddle";
    private const string trunkBeltBackRightMarkerName = "TrunkBeltRight";
    private const string trunkBeltBackLeftMarkerName = "TrunkBeltLeft";
    private const string trunkBeltFrontRightMarkerName = "TrunkBeltFrontRight";
    private const string trunkBeltFrontLeftMarkerName = "TrunkBeltFrontLeft";

    //names of shoulder markers
    private const string leftAcromionMarkerName = "L.Shoulder";
    private const string rightAcromionMarkerName = "R.Shoulder";
    private const string c7MarkerName = "C7";
    private const string sternalNotchMarkerName = "SternalNotch";

    //name of knee markers
    private const string rightKneeMarkerName = "RKNE";
    private const string rightKneeMedialMarkerName = "RKNEEM";
    private const string leftKneeMarkerName = "LKNE";
    private const string leftKneeMedialMarkerName = "LKNEEM";

    //name of ankle markers
    private const string rightAnkleMarkerName = "RANK";
    private const string rightAnkleMedialMarkerName = "RANKM";
    private const string leftAnkleMarkerName = "LANK";
    private const string leftAnkleMedialMarkerName = "LANKM";

    //name of thigh markers
    private const string rightThighFrontMarkerName = "RTHIFRONT";
    private const string leftThighFrontMarkerName = "LTHIFRONT";
    private const string rightThighMarkerName = "RTHI";
    private const string leftThighMarkerName = "LTHI";
    private const string leftThighSideBottomMarkerName = "LTHIBOTTOM";
    private const string rightThighSideBottomMarkerName = "RTHIBOTTOM";


    //name of arm markers
    private const string rightArmLateralNearMarkerName = "RightArmLateralNear";
    private const string rightArmLateralFarMarkerName = "RightArmLateralFar";
    private const string rightArmAnteriorMarkerName = "RightArmAnterior";
    private const string rightForearmMarkerName = "R.Forearm";
    private const string rightElbowMarkerName = "R.Elbow";
    private const string rightWristMarkerName = "R.Wrist";

    private const string leftArmLateralNearMarkerName = "LeftArmLateralNear";
    private const string leftArmLateralFarMarkerName = "LeftArmLateralFar";
    private const string leftArmAnteriorMarkerName = "LeftArmAnterior";
    private const string leftForearmMarkerName = "L.Forearm";
    private const string leftElbowMarkerName = "L.Elbow";
    private const string leftWristMarkerName = "L.Wrist";

   /* //name of foot markers
    private const string rightHeelMarkerName = "RHEE";
    private const string rightToeMarkerName = "RTOE";
    private const string rightFirstDistalPhalanxMarkerName = "R.1DP";
    private const string right5MTMarkerName = "R.5MT";

    private const string leftHeelMarkerName = "LHEE";
    private const string leftToeMarkerName = "LTOE";
    private const string leftFirstDistalPhalanxMarkerName = "L.1DP";
    private const string left5MTMarkerName = "L.5MT";
   */

    // name of shank markers
    private const string rightTibiaMarkerName = "RTIB";
    private const string rightTibialTuberosityMarkerName = "R.TibTubero";

    private const string leftTibiaMarkerName = "LTIB";
    private const string leftTibialTuberosityMarkerName = "L.TibTubero";



    // List of segment names (these are used for rigid body reconstruction, and may 
    // include rigid bodies that are NOT in the skeleton for COM estimation, i.e. they don't have masses, COM center positions, etc.)
    private const string trunkSegmentName = "Trunk";
    private const string pelvisSegmentName = "Pelvis";
    private const string leftThighSegmentName = "LeftThigh";
    private const string rightThighSegmentName = "RightThigh";
    private const string leftShankSegmentName = "LeftShank";
    private const string rightShankSegmentName = "RightShank";
    private const string leftArmSegmentName = "LeftArm";
    private const string rightArmSegmentName = "RightArm";
    private const string leftForearmSegmentName = "LeftForearm";
    private const string rightForearmSegmentName = "RightForearm";
    private const string shoulderSegmentName = "ShoulderGirdle"; 

    // Segment names used for segment frame computation for joint angles
    private const string leftThighKneeJointSegmentName = "LeftThighKneeJointFrame";
    private const string rightThighKneeJointSegmentName = "RightThighKneeJointFrame";
    private const string leftThighHipJointSegmentName = "LeftThighHipJointFrame";
    private const string rightThighHipJointSegmentName = "RightThighHipJointFrame";
    private const string leftShankKneeJointSegmentName = "LeftShankKneeJointFrame";
    private const string rightShankKneeJointSegmentName = "RightShankKneeJointFrame";
    private const string leftShankAnkleJointSegmentName = "LeftShankAnkleJointFrame";
    private const string rightShankAnkleJointSegmentName = "RightShankAnkleJointFrame";
    private const string leftFootSegmentName = "LeftFoot";
    private const string rightFootSegmentName = "RightFoot";

    //list of non-segment rigid body (e.g. the pelvis) names
    private const string pelvisName = "Pelvis";




    //lists of markers in rigid bodies (segments or non-segments).
    //Needed for reconstruction.
    string[] markersInPelvis; // names of all markers in the pelvis rigid body. Initialized in setup.
    string[] markersInTrunk; // names of all markers in the trunk (belt!) rigid body. Initialized in setup.
    string[] markersInRightFoot; // names of all markers in the right foot segment. Initialized in setup.
    string[] markersInLeftFoot; // names of all markers in the left foot segment. Initialized in setup.
    string[] markersInRightShank; // names of all markers in the right shank segment. Initialized in setup. ARIYADAV
    string[] markersInLeftShank; // names of all markers in the left shank segment. Initialized in setup. ARIYADAV
    string[] markersInLeftArm; // names of all markers in the left arm segment. Initialized in setup. ARIYADAV
    string[] markersInRightArm; // names of all markers in the right arm segment. Initialized in setup. ARIYADAV
    string[] markersInLeftThigh; // names of all markers in the left thigh segment. Initialized in setup. ARIYADAV
    string[] markersInRightThigh; // names of all markers in the right thigh segment. Initialized in setup. ARIYADAV
    string[] markersInShoulderSegment; // names of all merks in the shoulder girdle segment.

    //string armSegmentName = "Arm";
    //string forearmSegmentName = "Forearm";
    //string headSegmentName = "Head";

    //list of segment types 
    private const string trunkSegmentType = "Trunk";
    private const string thighSegmentType = "Thigh";
    private const string shankSegmentType = "Shank";
    private const string armSegmentType = "Arm";
    private const string forearmSegmentType = "Forearm";
    private const string handSegmentType = "Hand";
    private const string headSegmentType = "Head";
    private const string footSegmentType = "Foot";


    //define edges of base of support (let X be the lateral direction, reflecting current setup)
    //Normally, obtain these from foot markers in real time.
    private const string rightFifthMetatarsalMarkerName = "R.5MT";
    private const string leftFifthMetatarsalMarkerName = "L.5MT";
    private const string rightFirstDistalPhalanxMarkerName = "R.1DP";
    private const string leftFirstDistalPhalanxMarkerName = "L.1DP";
    private const string rightSecondMetatarsalMarkerName = "RTOE"; //call the right second metatarsal "RTOE" in the plugin gait model
    private const string leftSecondMetatarsalMarkerName = "LTOE"; //call the left second metatarsal "LTOE" in the plugin gait model
    private const string rightHeelMarkerName = "RHEE";
    private const string leftHeelMarkerName = "LHEE";
    private float rightEdgeBaseOfSupportXPos; //-1001.0f; //note, as set up, right is further along the negative x-axis
    private float rightEdgeBaseOfSupportYPos; //-1001.0f; //note, as set up, right is further along the negative x-axis
    private float leftEdgeBaseOfSupportXPos; // = -450.5f;
    private float leftEdgeBaseOfSupportYPos; // = -450.5f;
    private float frontEdgeBaseOfSupportYPos; // = -149.0f; //note, as set up, forwards is more negative along the y-axis
    private float frontEdgeBaseOfSupportXPos; // = -149.0f; //note, as set up, forwards is more negative along the y-axis
    private float backEdgeBaseOfSupportYPos; // = 84.0f;
    private float backEdgeBaseOfSupportXPos; // = 84.0f;

    //establish the bounds of the base of support in viewport coordinates
    private float leftEdgeOfBaseOfSupportInViewportCoords = 0.1f;
    private float rightEdgeOfBaseOfSupportInViewportCoords; //initialized in Start()
    private float backEdgeOfBaseOfSupportInViewportCoords = 0.1f;
    private float frontEdgeOfBaseOfSupportInViewportCoords; //initialized in Start()


    //object that contains all used marker names
    private string[] namesOfAllMarkersInComModel; //a string array containing the names of all markers used in our model to compute COM of the subject

    //most recent ankle marker positions (sometimes needed to estimate thigh and shank COM positions)
    Vector3 mostRecentRightAnkleMarkerPosition;
    Vector3 mostRecentLeftAnkleMarkerPosition;

    // Center of the pelvis markers position (failsafe, poor approximation to COM)
    private Vector3 centerOfPelvisPosition;

    // Center of the trunk belt (used to control trunk segment forces)
    private Vector3 mostRecentMidpointTrunkBelt; 


    //tracking COM velocity
    int howManyPositionsToUseComputingComVelocity = 5; //how many positions to use when averaging velocities. Note, this number - 1 = number of velocities averaged.
    private float timeBetweenUpdateCalls; //time between the last time Update() was called and the start of the current Update() call
    private int numberOfComPositionsToStoreForVelocity = 50;
    private List<Vector3> mostRecentComPositions;
    private List<float> mostRecentComTimes;

    //saving data to file
    public GameObject generalDataRecorder; //the GameObject that stores data and ultimately writes it to file (CSV)
    private GeneralDataRecorder generalDataRecorderScript; //the script with callable functions, part of the general data recorder object.
    private bool stillRecordingMarkerDataFlag = true; // Whether or not the marker data is sent to the General Data Recorder. 
                                                      // Starts true, and level manager sets to false when task is over.

    // Force plate data
    private bool isForcePlateDataReadyForAccess = false;
    private Vector3 CopPositionViconFrame;
    Vector3[] allForcePlateForces; // a Vector3[] array of force plate forces. First vector is one force plate, second is the other.
    Vector3[] allForcePlateTorques; // a Vector3[] array of force plate forces. First vector is one force plate, second is the other.


    // Communicating with cable-driven robot (i.e. RobUST)
    private bool usingCableDrivenRobotFlag = false; // default is false - the level manager should flip this flag using the setter function
                                                    // if needed. It should only be flipped after the level manager has initialized the 
                                                    // BuildStructureMatrix service.
    public GameObject BuildStructureMatrixServiceObject; // the object that contains the script that computes structure matrices for the robot
    private BuildStructureMatricesForBeltsThisFrameScript buildStructureMatrixScript; // the script that computes structure matrices
                                                                                      // for the robot each frame. The reference is obtained
                                                                                      // when the public function is called that states we're 
                                                                                      // using a cable-driven robot.
    public GameObject forceFieldHighLevelControllerObject; // the object that contains the script that computes desired force field forces (the high-level controller)
    private ForceFieldHighLevelControllerScript forceFieldHighLevelControllerScript; // the script for the force field high level controller


    // Joint angles 
    // Hip angles
    private float rightHipFlexionAngle;
    private float rightHipAbductionAngle;
    private float rightHipInternalRotationAngle;
    private float leftHipFlexionAngle;
    private float leftHipAbductionAngle;
    private float leftHipInternalRotationAngle;
    // Knee angles
    private float rightKneeFlexionAngle;
    private float rightKneeAbductionAngle;
    private float rightKneeInternalRotationAngle;
    private float leftKneeFlexionAngle;
    private float leftKneeAbductionAngle;
    private float leftKneeInternalRotationAngle;
    // Ankle angles
    private float rightAnkleFlexionAngle;
    private float rightAnkleInversionAngle;
    private float rightAnkleInternalRotationAngle;
    private float leftAnkleFlexionAngle;
    private float leftAnkleInversionAngle;
    private float leftAnkleInternalRotationAngle;

    // Control point settings
    public UniversalExperimentSettings experimentSettingsScript;

    public bool testOccludeNamedMarker;

    // Stance model - needed to see if we're using Vive or Vicon for control
    public KinematicModelClass stanceModelScript;


    // Start is called before the first frame update
    void Start()
    {
        //initialize quantities that are computed from other variables
        markerDataDistributorScript = markerDataDistributor.GetComponent<UnityVicon.ReadViconDataStreamScript>();
        rightEdgeOfBaseOfSupportInViewportCoords = 1.0f - leftEdgeOfBaseOfSupportInViewportCoords;
        frontEdgeOfBaseOfSupportInViewportCoords = 1.0f - backEdgeOfBaseOfSupportInViewportCoords;
        mostRecentComPositions = new List<Vector3>(numberOfComPositionsToStoreForVelocity);
        mostRecentComTimes = new List<float>(numberOfComPositionsToStoreForVelocity);

        //saving data to file 
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // Initialize the subject sex based on the SubjectInfo script
        string subjectSexString = subjectSpecificDataScript.getSubjectGenderString();
        if (subjectSexString == "M" || subjectSexString == "m" || subjectSexString == "Man" || subjectSexString == "man")
        {
            subjectSex = maleSexName;
        }else if(subjectSexString == "W" || subjectSexString == "w" || subjectSexString == "Woman" || subjectSexString == "woman" ||
            subjectSexString == "F" || subjectSexString == "f")
        {
            subjectSex = femaleSexName;
        }
        else
        {
            Debug.LogError("Subject sex specifier string is incorrect. Use M for male or F for female");
        }

        //Since we still need to collect a few dozen marker frames for setup,
        //set the flag to true
        storingFramesForSetupFlag = true;

        // Get a reference to the force plate data access script 
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();

        // Get a reference to the force field high level controller
        if (usingCableDrivenRobotFlag)
        {
            forceFieldHighLevelControllerScript = forceFieldHighLevelControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();
        }
    }





    // Update is called once per frame
    void Update()
    {
        if (stanceModelScript.dataSourceSelector != ModelDataSourceSelector.ViveOnly)
        {
            timeBetweenUpdateCalls = Time.deltaTime;
            if (setupComplete) //if setup has been completed, update the center of mass
            {
                //[seconds]
                tempStopWatch.Start();

                // Compute the center of mass position for this frame
                (bool isMarkerDataOld, bool pelvisSegmentWasAvailable) =
                    updateCenterOfMass(); //every frame, update the center of mass position and velocity. May want to put this into a FixedUpdate() call.

                // Retrieve the force plate data

                // If the flag has been set (by the level manager) that indicates we're communicating with a cable-driven robot (RobUST), 
                // then ping the structure matrix computation service, if marker data is fresh.
                if (usingCableDrivenRobotFlag && !isMarkerDataOld)
                {
                    // Build the structure matrix
                    buildStructureMatrixScript.BuildStructureMatricesForThisFrame();

                    // Compute the forces needed to act on the subject, solve for appropriate calbe tensions, and send the 
                    // cable tensions to the robot.
                    forceFieldHighLevelControllerScript.ComputeDesiredForceFieldForcesAndTorquesOnSubject();

                    // Tell the high-level controller script to compute tensions and send them on to the cable robot
                    // via the TCP service
                    forceFieldHighLevelControllerScript.ComputeDesiredCableTensionsThisFrameAndSendToRobot();

                }

                // Store the data collected to file (even if marker data was old, since we want to store other data at a faster rate).
                if (stillRecordingMarkerDataFlag == true)
                {
                    //log the marker data used for computing the COM this frame (even if we could not compute it successfully)
                    storeViconAndComDataInDataRecorderObject(isMarkerDataOld);
                }

                //reset variables needed for the next frame 
                resetVariablesNeededForNextFrame();

                tempStopWatch.Stop();
                printStopwatchTimeElapsedToConsole(isMarkerDataOld);
                tempStopWatch.Reset();

                // Update a flag, if needed, saying that at least one frame of live data has been collected (one Update() has completed in this script). 
                if (comManagerHasRunUpdateAtLeastOnceFlag == false)
                {
                    comManagerHasRunUpdateAtLeastOnceFlag = true;
                }
            }
            else //if we're still setting up, then keep trying to set up
            {
                printLogMessageToConsoleIfDebugModeIsDefined("Attempting setup");
                //call the setup function (which includes finding base of support)
                setupComplete = setupCenterOfMassManager();

                if (setupComplete)
                {
                    Debug.Log("Setup complete");
                    printLogMessageToConsoleIfDebugModeIsDefined("Setup completed.");
                }
            }
        }
    }



    //Start: Getter functions *********************************************
    public (float, float, float) GetSubjectSpecificSegmentMetrics(String segmentName)
    {
        // Init return values
        float segmentFractionOfTotalBodyMass = 0.0f;
        float segmentLength = 0.0f;
        float lengthToJointMassCenter = 0.0f;
        float distanceFromProximalMarkerToComAsFractionOfSegmentLength = 0.0f; 

        // 1.) Compute segment mass **************************************************************************

        if (subjectSex == "Man") //if the subject is male
        {
            // Get the fractional distance from the joint to the center of mass of the segment
            distanceFromProximalMarkerToComAsFractionOfSegmentLength = 1.0f - TisserandMaleDistancesFromProximalMarker[segmentName]; // get the COM distance along the segment
            // If the segment is the trunk
            if (segmentName == trunkSegmentType)
            {
                // Incorporate the upper limbs and head into the segment mass
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[trunkSegmentType] + deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[headSegmentType] +
                    2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[armSegmentType] + 2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[forearmSegmentType] +
                    2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[handSegmentType];
            }
            else
            {
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[segmentName];
            }
        }
        else if (subjectSex == "Woman")
        {
            distanceFromProximalMarkerToComAsFractionOfSegmentLength = 1.0f - TisserandFemaleDistancesFromProximalMarker[segmentName]; // get the COM distance along the segment
            if (segmentName == trunkSegmentType)
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[trunkSegmentType] + deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[headSegmentType] +
                    2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[armSegmentType] + 2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[forearmSegmentType] +
                    2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[handSegmentType];
            }
            else
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[segmentName];
            }
        }


        // 2.) Compute segment length **************************************************************************

        if (segmentName == thighSegmentType) // mid knee - hip joint center
        {
            // get knee markers
            (_, Vector3 rightKneeMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("RKNE");
            (_, Vector3 leftKneeMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("LKNE");
            // get avg pos of right and left knee
            Vector3 avgPosKneeMarker = (rightKneeMarkerPosThisFrame + leftKneeMarkerPosThisFrame) / 2.0f;

            //get pelvis markers
            Vector3 pelvisCenterCoords = getCenterOfPelvisMarkerPositionsInViconFrame();

            Vector3 kneeToPelvisVector = pelvisCenterCoords - avgPosKneeMarker;
            segmentLength = kneeToPelvisVector.magnitude;

            (Vector3 rightHJCPos, _, _) = getHjcAndHjcMidpointPositions();
            Vector3 rightKneeToRightHJC = rightHJCPos - rightKneeMarkerPosThisFrame;
            lengthToJointMassCenter = rightKneeToRightHJC.magnitude * distanceFromProximalMarkerToComAsFractionOfSegmentLength;
        }
        else if (segmentName == shankSegmentType) //mid ankle - mid knee
        {
            // get knee markers
            (_, Vector3 rightKneeMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("RKNE");
            (_, Vector3 leftKneeMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("LKNE");
            // get avg pos of right and left knee
            Vector3 avgPosKneeMarker = (rightKneeMarkerPosThisFrame + leftKneeMarkerPosThisFrame) / 2.0f;

            // get outer ankle markers 
            (_, Vector3 rightAnkleMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("RANK");
            (_, Vector3 leftAnkleMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("LANK");
            // avg to get ankle midpoint
            Vector3 avgPosAnkleMarker = (rightAnkleMarkerPosThisFrame + leftAnkleMarkerPosThisFrame) / 2.0f;

            Vector3 shankSegmentVector = avgPosKneeMarker - avgPosAnkleMarker;
            segmentLength = shankSegmentVector.magnitude;
            lengthToJointMassCenter = segmentLength * distanceFromProximalMarkerToComAsFractionOfSegmentLength;

        }
        else if (segmentName == trunkSegmentType) //supersternal - hip joint center
        {
            (_, Vector3 SupersternalNotchMarkerPosThisFrame) = GetMostRecentMarkerPositionByName("SternalNotch");
            Vector3 pelvisCenterCoords = getCenterOfPelvisMarkerPositionsInViconFrame();
            Vector3 trunkSegmentVector = SupersternalNotchMarkerPosThisFrame - pelvisCenterCoords;
            segmentLength = trunkSegmentVector.magnitude;

            (Vector3 rightHJCPos, _, _) = getHjcAndHjcMidpointPositions();
            Vector3 supersternalNotchToHJC = SupersternalNotchMarkerPosThisFrame - rightHJCPos;

            lengthToJointMassCenter = (supersternalNotchToHJC.magnitude * distanceFromProximalMarkerToComAsFractionOfSegmentLength) -
                (supersternalNotchToHJC.magnitude - segmentLength);
        }

        float segmentLengthInMeters = segmentLength / 1000.0f;
        float lengthToJointMassCenterInMeters = lengthToJointMassCenter / 1000.0f;  

        return (segmentFractionOfTotalBodyMass, segmentLengthInMeters, lengthToJointMassCenterInMeters);
    }



    public Dictionary<string,float> GetTisserandMaleDistancesFromProximalMarker()
    {
        return TisserandMaleDistancesFromProximalMarker;
    }

    public Dictionary<string, float> GetTisserandFemaleDistancesFromProximalMarker()
    {
        return TisserandFemaleDistancesFromProximalMarker;
    }

    public Dictionary<string, float> GetDeLevaMaleSegmentMassesAsPercentOfTotalBodyMass()
    {
        return deLevaMaleSegmentMassesAsPercentOfTotalBodyMass;
    }

    public Dictionary<string, float> GetDeLevaFemaleSegmentMassesAsPercentOfTotalBodyMass()
    {
        return deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass;
    }

    public Vector3 GetSelectedControlPointPositionInViconCoordinates()
    {
        // Get the current control point settings object
        controlPointEnum controlPointCurrentSetting = experimentSettingsScript.GetControlPointSettingsEnumObject();
        // Initialize control point position
        Vector3 controlPointPosition = new Vector3();
        // Depending on the control point desired
        switch (controlPointCurrentSetting)
        {
            case controlPointEnum.COM:
                controlPointPosition = getSubjectCenterOfMassInViconCoordinates();
                break;
            case controlPointEnum.Pelvis:
                controlPointPosition = getCenterOfPelvisMarkerPositionsInViconFrame();
                break;
            case controlPointEnum.Chest:
                controlPointPosition = GetCenterOfTrunkBeltPositionInViconFrame();
                break;
        }
        // Return control point position
        return controlPointPosition;
    }

    //The key getter function, which returns the coordinates of the COM in Vicon frame in millimeters. 
    public Vector3 getSubjectCenterOfMassInViconCoordinates()
    {
        return mostRecentTotalBodyComPosition;
    }

    public (bool, Vector3) GetMostRecentMarkerPositionByName(string markerName)
    {
        (bool success, Vector3 markerPositionThisFrame) = getMarkerPositionAsVectorByName(markerName);

        return (success, markerPositionThisFrame);
    }


    //Returns the edges of the base of support (boundaries of the feet) as defined in Vicon coordinates 
    public (float, float, float, float) getEdgesOfBaseOfSupportInViconCoordinates()
    {
        return (leftEdgeBaseOfSupportXPos, rightEdgeBaseOfSupportXPos, frontEdgeBaseOfSupportYPos, backEdgeBaseOfSupportYPos);
    }

    //Returns the edges of the base of support (boundaries of the feet) as defined in Vicon coordinates 
    public (float, float, float, float) getEdgesOfBaseOfSupportRotatedInViconCoordinates()
    {
        return (leftEdgeBaseOfSupportYPos, rightEdgeBaseOfSupportYPos, frontEdgeBaseOfSupportXPos, backEdgeBaseOfSupportXPos);
    }


    public Vector3 GetAnkleJointCenterPositionViconFrame()
    {
        // Get the left and right outer ankle marker positions (lateral malleolus)
        (_, Vector3 leftAnkleMarkerPositionViconFrame) = getMarkerPositionAsVectorByName(leftAnkleMarkerName);
        (_, Vector3 rightAnkleMarkerPositionViconFrame) = getMarkerPositionAsVectorByName(rightAnkleMarkerName);

        // Take the average of these two markers to get the ankle joint center
        return (leftAnkleMarkerPositionViconFrame + rightAnkleMarkerPositionViconFrame) / (2.0f);
    }

    public Vector3 GetCenterOfChestBeltPositionInStartupFramesViconFrame()
    {

        // Compute the midpoint of the two front markers
        Vector3 frontLeftTrunkBeltStartupPosition = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontLeftMarkerName);
        Vector3 frontRightTrunkBeltStartupPosition = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontRightMarkerName);
        Vector3 backCenterTrunkBeltStartupPosition = getMarkerAveragePositionInStartupFramesByName(trunkBeltBackMiddleMarkerName);


        Vector3 midpointFrontTrunkBelt = (frontLeftTrunkBeltStartupPosition + frontRightTrunkBeltStartupPosition) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        Vector3 startupAveragePositionTrunkBelt = (backCenterTrunkBeltStartupPosition + midpointFrontTrunkBelt) / 2.0f;

        // Return the trunk belt midpoint for the startup frames
        return startupAveragePositionTrunkBelt;
    }

    public Vector3 GetCenterOfBaseOfSupportPositionViconFrame()
    {
        //get right boundary for base of support
        Vector3 rightFifthMtPosition = getMarkerAveragePositionInStartupFramesByName(rightFifthMetatarsalMarkerName);
        rightEdgeBaseOfSupportXPos = rightFifthMtPosition.x; //Item 1 of the tuple is the X-position

        //get left boundary for base of support
        Vector3 leftFifthMtPosition = getMarkerAveragePositionInStartupFramesByName(leftFifthMetatarsalMarkerName);
        leftEdgeBaseOfSupportXPos = leftFifthMtPosition.x; //Item 1 of the tuple is the X-position

        //get forward boundary for base of support as mean of the two big toenail (1st distal phalanx) positions
        Vector3 rightToePosition = getMarkerAveragePositionInStartupFramesByName(rightFirstDistalPhalanxMarkerName);
        Vector3 leftToePosition = getMarkerAveragePositionInStartupFramesByName(leftFirstDistalPhalanxMarkerName);
        Vector3 frontEdgeOfBaseOfSupportMarker = ((rightToePosition + leftToePosition) / 2.0f);

        //get backward boundary for base of support as mean of the two heel positions
        Vector3 rightHeelPosition = getMarkerAveragePositionInStartupFramesByName(rightHeelMarkerName);
        Vector3 leftHeelPosition = getMarkerAveragePositionInStartupFramesByName(leftHeelMarkerName);
        Vector3 backEdgeOfBaseOfSupportMarker = ((rightHeelPosition + leftHeelPosition) / 2.0f);

        // Center of base of support componennts
        float centerOfBaseOfSupportXPos = (rightFifthMtPosition.x + leftFifthMtPosition.x) / 2.0f;
        float centerOfBaseOfSupportYPos = (frontEdgeOfBaseOfSupportMarker.y + backEdgeOfBaseOfSupportMarker.y) / 2.0f;
        float centerOfBaseOfSupportZPos = (rightFifthMtPosition.x + leftFifthMtPosition.x) / 2.0f;

        return new Vector3(centerOfBaseOfSupportXPos, centerOfBaseOfSupportYPos, centerOfBaseOfSupportZPos);
    }

    public Vector3 GetLeftHandPositionViconFrame()
    {
        (_, Vector3 leftWristMarkerPosition) = getMarkerPositionAsVectorByName(leftWristMarkerName);
        return leftWristMarkerPosition;
    }

    public Vector3 GetRightHandPositionViconFrame()
    {
        (_, Vector3 rightWristMarkerPosition) = getMarkerPositionAsVectorByName(rightWristMarkerName);
        return rightWristMarkerPosition;
    }

    public Vector3 GetLeftFootCenterPositionViconFrame()
    {
        (_, Vector3 leftFifthMtPosition) = getMarkerPositionAsVectorByName(leftFifthMetatarsalMarkerName);
        (_, Vector3 leftFirstMtPosition) = getMarkerPositionAsVectorByName(leftSecondMetatarsalMarkerName);
        (_, Vector3 leftHeelPosition) = getMarkerPositionAsVectorByName(leftHeelMarkerName);

        // Front center 
        Vector3 leftFootFrontCenter = (leftFifthMtPosition + leftFirstMtPosition) / 2.0f;

        // Return the mean of the left foot front center and heel
        return ((leftFootFrontCenter + leftHeelPosition) / 2.0f);
    }

    public Vector3 GetRightFootCenterPositionViconFrame()
    {
        (_, Vector3 rightFifthMtPosition) = getMarkerPositionAsVectorByName(rightFifthMetatarsalMarkerName);
        (_, Vector3 rightFirstMtPosition) = getMarkerPositionAsVectorByName(rightSecondMetatarsalMarkerName);
        (_, Vector3 rightHeelPosition) = getMarkerPositionAsVectorByName(rightHeelMarkerName);

        // Front center 
        Vector3 rightFootFrontCenter = (rightFifthMtPosition + rightFirstMtPosition) / 2.0f;

        // Return the mean of the left foot front center and heel
        return ((rightFootFrontCenter + rightHeelPosition) / 2.0f);
    }


    public bool getCenterOfMassManagerReadyStatus()
    {
        return setupComplete; //if setup is complete, then this script (the center of mass manager) is ready to serve up COM data
    }

    public bool GetCenterOfMassManagerHasRunUpdateLoopOnceFlag()
    {
        return comManagerHasRunUpdateAtLeastOnceFlag; // if the Update() loop has run once, then all marker data should be ready for retrieval.
    }

    public float getLengthParameterOfBodyInvertedPendulumModel()
    {
        return bodyAsInvertedPendulumLegLength;
    }

    public float GetVerticalDistanceMidAnkleToMidTrunkBeltInMeters()
    {
        return verticalDistanceMidAnkleToMidTrunkBeltInMeters;
    }


    public uint getMostRecentlyAccessedViconFrameNumber()
    {
        return mostRecentlyAccessedViconFrameNumber;
    }

    public Vector3 getCenterOfPelvisMarkerPositionsInViconFrame()
    {
        return new Vector3(centerOfPelvisPosition.x, centerOfPelvisPosition.y, centerOfPelvisPosition.z);
    }

    public Vector3 getCenterOfPelvisMarkerViconFramePositionsInStartupFrames()
    {
        //get the positions and occlusion status of the four pelvic markers
        Vector3 frontLeftPelvisPos = getMarkerAveragePositionInStartupFramesByName(frontLeftPelvisMarkerName);
        Vector3 frontRightPelvisPos = getMarkerAveragePositionInStartupFramesByName(frontRightPelvisMarkerName);
        Vector3 backRightPelvisPos = getMarkerAveragePositionInStartupFramesByName(backRightPelvisMarkerName);
        Vector3 backLeftPelvisPos = getMarkerAveragePositionInStartupFramesByName(backLeftPelvisMarkerName);

        //average the x- and y-positions of the four markers to get a shitey guess at COM 
        Vector3 centerOfPelvisPositionInStartup = (frontLeftPelvisPos + frontRightPelvisPos + backRightPelvisPos + backLeftPelvisPos) / 4;

        return new Vector3(centerOfPelvisPositionInStartup.x, centerOfPelvisPositionInStartup.y, centerOfPelvisPositionInStartup.z);
    }


    // Currently, buildStructureMatrixScript is only available when we've set the usingCableDrivenRobotFlag to true. 
    // Assume, for now, that this has been set up before this function is called.
    public Vector3 GetCenterOfShankBeltPositionInViconFrame()
    {
        // Compute the center of each shank belt
        Vector3 leftShankBeltCenterPosViconFrame = buildStructureMatrixScript.GetMostRecentLeftShankBeltCenterPositionInViconFrame();
        Vector3 rightShankBeltCenterPosViconFrame = buildStructureMatrixScript.GetMostRecentRightShankBeltCenterPositionInViconFrame();

        // Take the mean as the center of both shank belts.
        Vector3 midpointOfShankBeltCenters = (leftShankBeltCenterPosViconFrame + rightShankBeltCenterPosViconFrame) / 2.0f;
        
        // Return
        return midpointOfShankBeltCenters;
    }


    public Vector3 getCenterOfShoulderMarkerPositionsInViconFrame()
    {
        // Get the shoulder positions
        (_, Vector3 rightShoulderPosition) = getMarkerPositionAsVectorByName(rightAcromionMarkerName);
        (_, Vector3 leftShoulderPosition) = getMarkerPositionAsVectorByName(leftAcromionMarkerName);

        // Return the average shoulder position
        return ((rightShoulderPosition + leftShoulderPosition) / 2.0f);

    }

    public Vector3 GetCenterOfTrunkBeltPositionInViconFrame()
    {
        return mostRecentMidpointTrunkBelt;
    }


    public float GetRightKneeAngleInDegrees()
    {
        return rightKneeFlexionAngle;
    }

    public float GetLeftKneeAngleInDegrees()
    {
        return ((180.0f / Mathf.PI) * leftKneeFlexionAngle);
    }





    //Convert the COM to world-coordinates, first by passing through viewport coordinates. Let the center of the base of support be (0,0), and let the edges of the BoS be a certain percentage of the way to the edges of the screen. 
    //For example, let the frontward edge of the BoS be 0.4 and the backward edge be -0.4 in viewport coordinates, then convert.
    public Vector3 getMostRecentCenterOfMassPositionInViewportFittedWorldCoordinates()
    {
        //first, convert subject center of mass coordinates from Vicon coordinates to Viewport coordinates
        float ComXPositionInViewport = leftEdgeOfBaseOfSupportInViewportCoords + ((mostRecentTotalBodyComPosition.x - leftEdgeBaseOfSupportXPos) / (rightEdgeBaseOfSupportXPos - leftEdgeBaseOfSupportXPos)) * (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords);
        float ComYPositionInViewport = backEdgeOfBaseOfSupportInViewportCoords + ((mostRecentTotalBodyComPosition.y - backEdgeBaseOfSupportYPos) / (frontEdgeBaseOfSupportYPos - backEdgeBaseOfSupportYPos)) * (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords);

        printLogMessageToConsoleIfDebugModeIsDefined("COM position in viewport coordinates is (x,y): ( " + ComXPositionInViewport + ", " + ComYPositionInViewport + " )");
        //then convert viewport coordinates to Unity world coordinates
        Vector3 ComPositionInUnityWorldCoords = sceneCamera.ViewportToWorldPoint(new Vector3(ComXPositionInViewport, ComYPositionInViewport, 5.0f));

        return ComPositionInUnityWorldCoords;

    }


    public (bool, Vector3) getEstimateOfMostRecentCenterOfMassVelocity()
    {

        // We only access Vicon COM velocity if we have collected enough frames,
        // as specified in the variable numberOfComPositionsToStoreForVelocity.
        if(mostRecentComPositions.Count >= numberOfComPositionsToStoreForVelocity)
        {
            int finalIndexForRecentComArray = mostRecentComPositions.Count - 1;
            int startIndexToUseForRecentComArray = finalIndexForRecentComArray - howManyPositionsToUseComputingComVelocity + 1;

            Vector3 approximateVelocity = new Vector3(0.0f, 0.0f, 0.0f);
            //Note that this loop will execute (howManyPositionsToUseComputingComVelocity - 1) times.
            for (int positionIndex = startIndexToUseForRecentComArray; positionIndex < finalIndexForRecentComArray; positionIndex++)
            {
                float deltaTime = mostRecentComTimes[positionIndex + 1];
                approximateVelocity += (mostRecentComPositions[positionIndex + 1] - mostRecentComPositions[positionIndex]) / ((float)(deltaTime));
                /*Debug.Log("When computing COM velocity, deltaTime was " + deltaTime);
                Debug.Log("When computing COM velocity, two COM positions in consideration are 2, 1: " + mostRecentComPositions[positionIndex + 1]
                    + ", " + mostRecentComPositions[positionIndex]);
                Debug.Log("One frame used to compute COM velocity had velocity of " + (mostRecentComPositions[positionIndex + 1] - mostRecentComPositions[positionIndex]) / ((float)(deltaTime)));*/
            }

            //divide by the number of observations used in computing the velocity to get an average velocity over the frames of interest
            approximateVelocity = approximateVelocity / (howManyPositionsToUseComputingComVelocity - 1);

            //return a flag indicating that velocity is available and the velocity
            return (true,approximateVelocity);
        }
        else
        {
            // return null values and a bool indicating that the velocity is not available
            return (false, new Vector3(0.0f, 0.0f, 0.0f));
        }


        
    }

    public Vector3 convertUnityWorldCoordinatesToViconCoordinates(Vector3 pointInUnityWorldCoordinates)
    {
        // convert Unity world coordinates to Viewport coordinates
        Vector3 pointInViewportCoordinates = sceneCamera.WorldToViewportPoint(pointInUnityWorldCoordinates);

        // convert Viewport coordinates to Vicon coordinates (defined relative to the base of support)
        float comXPositionInViconCoords = leftEdgeBaseOfSupportXPos + ((pointInViewportCoordinates.x - leftEdgeOfBaseOfSupportInViewportCoords) / (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords)) * (rightEdgeBaseOfSupportXPos - leftEdgeBaseOfSupportXPos);
        float comYPositionInViconCoords = backEdgeBaseOfSupportYPos + ((pointInViewportCoordinates.y - backEdgeOfBaseOfSupportInViewportCoords) / (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords)) * (frontEdgeBaseOfSupportYPos - backEdgeBaseOfSupportYPos);

        // return the point in Vicon coordinates
        return new Vector3(comXPositionInViconCoords, comYPositionInViconCoords, 0.0f);

    }



    public Vector3 convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(Vector3 pointInViconCoordinates)
    {
        //first, convert subject center of mass coordinates from Vicon coordinates to Viewport coordinates
        float pointInViewportCoordsX = leftEdgeOfBaseOfSupportInViewportCoords + ((pointInViconCoordinates.x - leftEdgeBaseOfSupportXPos) / (rightEdgeBaseOfSupportXPos - leftEdgeBaseOfSupportXPos)) * (rightEdgeOfBaseOfSupportInViewportCoords - leftEdgeOfBaseOfSupportInViewportCoords);
        float pointInViewportCoordsY = backEdgeOfBaseOfSupportInViewportCoords + ((pointInViconCoordinates.y - backEdgeBaseOfSupportYPos) / (frontEdgeBaseOfSupportYPos - backEdgeBaseOfSupportYPos)) * (frontEdgeOfBaseOfSupportInViewportCoords - backEdgeOfBaseOfSupportInViewportCoords);
        //Debug.Log("Vicon coordinates for the point are (x,y): (" + pointInViconCoordinates.x + ", " + pointInViconCoordinates.y + ")");
        //Debug.Log("Viewport coordinates for the point in Vicon space are (x,y):  (" + pointInViewportCoordsX + ", " + pointInViewportCoordsY + ")");

        //then convert viewport coordinates to Unity world coordinates
        Vector3 pointInUnityWorldCoords = sceneCamera.ViewportToWorldPoint(new Vector3(pointInViewportCoordsX, pointInViewportCoordsY, 5.0f));

        return pointInUnityWorldCoords;
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



    //End: Getter functions *********************************************






    // Start: Setter functions *********************************************

    public void SetUsingCableDrivenRobotFlagToTrue()
    {
        // Get the reference to the structure matrix script, since we are using a cable-driven robot
        buildStructureMatrixScript = BuildStructureMatrixServiceObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

        // Also get the reference to the high-level controller for computing force field forces/torques
        forceFieldHighLevelControllerScript = forceFieldHighLevelControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();

        // Then set the flag indicating we're using a cable-driven robot
        usingCableDrivenRobotFlag = true;
    }

    // Called by the level manager when the task is over, so that recording stops. 
    public void SetStillRecordMarkerDataFlagToFalse()
    {
        // Set the flag to false, indicating we'll no longer store the marker data (or force plate data) to file
        stillRecordingMarkerDataFlag = false;
    }

    //End: Setter functions *********************************************







    //Start: Functions called for setup only*******************************



    //The names of the headers (which will specify the names of the markers or segments in question) are 
    //specified here, called by the Start() function.
    private void setFrameDataNaming()
    {
        // Marker occlusion status
        string[] markersInModelOcclusionStatusNames = appendStringToAllElementsOfStringArray(markersInModel, "_OCCLUSION_STATUS");

        // Marker reconstructed with rigid body technique status
        string[] markersInModelReconstructionStatusNames = appendStringToAllElementsOfStringArray(markersInModel, "_RECONSTRUCTED_STATUS");

        // Marker positions
        string[] markersInModelXNames = appendStringToAllElementsOfStringArray(markersInModel, "_X");
        string[] markersInModelYNames = appendStringToAllElementsOfStringArray(markersInModel, "_Y");
        string[] markersInModelZNames = appendStringToAllElementsOfStringArray(markersInModel, "_Z");

        // Whether or not the segment COM could be computed and contributed to the whole-body COM estimation this frame
        string[] segmentUsedForComEstimationThisFrameFlagNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_USED_IN_COM_ESTIMATE_THIS_FRAME");

        // Whether or not the segment COM had to be estimated with a simplified model this frame
        // Note, we may do this for the thigh and shank if the knee marker is missing - connect a line from 
        // HJC to ankle and estimate the segment COM locations.
        string[] segmentUsedEstimatedWithSimplifedModelThisFrameFlagNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_COM_ESTIMATED_WITH_SIMPLIFIED_MODEL_THIS_FRAME");

        // The COM positions of the segment COMs this frame
        string[] segmentsInModelComXNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_X");
        string[] segmentsInModelComYNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_Y");
        string[] segmentsInModelComZNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_Z");

        string[] segmentsInModelEstimatedComXNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_ESTIMATED_X");
        string[] segmentsInModelEstimatedComYNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_ESTIMATED_Y");
        string[] segmentsInModelEstimatedComZNames = appendStringToAllElementsOfStringArray(segmentsInModel, "_ESTIMATED_Z");

        // Whether or not the virtual marker could be computed this frame
        string[] virtualMarkerComputedThisFrameFlagNames = appendStringToAllElementsOfStringArray(virtualMarkerNames, "_COULD_BE_CALCULATED_THIS_FRAME");


        // string[] virtualMarkerCalculableStatusNames = appendStringToAllElementsOfStringArray(virtualMarkerNames, "_CALCULABLE_STATUS"); //currently not implemented
        string[] virtualMarkerXNames = appendStringToAllElementsOfStringArray(virtualMarkerNames, "_X");
        string[] virtualMarkerYNames = appendStringToAllElementsOfStringArray(virtualMarkerNames, "_Y");
        string[] virtualMarkerZNames = appendStringToAllElementsOfStringArray(virtualMarkerNames, "_Z");

        string[] hipJointCenterXNames = appendStringToAllElementsOfStringArray(hipJointMarkerNames, "_X");
        string[] hipJointCenterYNames = appendStringToAllElementsOfStringArray(hipJointMarkerNames, "_Y");
        string[] hipJointCenterZNames = appendStringToAllElementsOfStringArray(hipJointMarkerNames, "_Z");

        //Since we only do this once, we're not concerned about speed. Add all of the string arrays to a list of strings, 
        //then convert the finalized list back to an array of strings.
        List<string> csvHeaderNamesAsList = new List<string>();
        csvHeaderNamesAsList.Add("MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER");
        csvHeaderNamesAsList.Add("TIME_AT_UNITY_FRAME_START");
        csvHeaderNamesAsList.Add("IS_MARKER_DATA_OLD_FLAG");
        csvHeaderNamesAsList.Add("SYNC_PIN_ANALOG_VOLTAGE");
        csvHeaderNamesAsList.AddRange(markersInModelOcclusionStatusNames.ToList());
        csvHeaderNamesAsList.AddRange(markersInModelReconstructionStatusNames.ToList());
        csvHeaderNamesAsList.AddRange(markersInModelXNames);
        csvHeaderNamesAsList.AddRange(markersInModelYNames);
        csvHeaderNamesAsList.AddRange(markersInModelZNames);
        csvHeaderNamesAsList.AddRange(segmentUsedForComEstimationThisFrameFlagNames); 
        csvHeaderNamesAsList.AddRange(segmentUsedEstimatedWithSimplifedModelThisFrameFlagNames);
        csvHeaderNamesAsList.AddRange(segmentsInModelComXNames);
        csvHeaderNamesAsList.AddRange(segmentsInModelComYNames);
        csvHeaderNamesAsList.AddRange(segmentsInModelComZNames);
        csvHeaderNamesAsList.AddRange(segmentsInModelEstimatedComXNames);
        csvHeaderNamesAsList.AddRange(segmentsInModelEstimatedComYNames);
        csvHeaderNamesAsList.AddRange(segmentsInModelEstimatedComZNames);
        csvHeaderNamesAsList.AddRange(virtualMarkerComputedThisFrameFlagNames); 
        csvHeaderNamesAsList.AddRange(virtualMarkerXNames);
        csvHeaderNamesAsList.AddRange(virtualMarkerYNames);
        csvHeaderNamesAsList.AddRange(virtualMarkerZNames);
        csvHeaderNamesAsList.AddRange(hipJointCenterXNames);
        csvHeaderNamesAsList.AddRange(hipJointCenterYNames);
        csvHeaderNamesAsList.AddRange(hipJointCenterZNames);
        csvHeaderNamesAsList.Add("COMPUTED_COM_X");
        csvHeaderNamesAsList.Add("COMPUTED_COM_Y");
        csvHeaderNamesAsList.Add("COMPUTED_COM_Z");
        csvHeaderNamesAsList.Add("PELVIS_CENTER_X");
        csvHeaderNamesAsList.Add("PELVIS_CENTER_Y");
        csvHeaderNamesAsList.Add("PELVIS_CENTER_Z");
        csvHeaderNamesAsList.Add("COP_X");
        csvHeaderNamesAsList.Add("COP_Y");
        csvHeaderNamesAsList.Add("COP_Z");
        csvHeaderNamesAsList.Add("FP_1_FX");
        csvHeaderNamesAsList.Add("FP_1_FY");
        csvHeaderNamesAsList.Add("FP_1_FZ");
        csvHeaderNamesAsList.Add("FP_1_TX");
        csvHeaderNamesAsList.Add("FP_1_TY");
        csvHeaderNamesAsList.Add("FP_1_TZ");
        csvHeaderNamesAsList.Add("FP_2_FX");
        csvHeaderNamesAsList.Add("FP_2_FY");
        csvHeaderNamesAsList.Add("FP_2_FZ");
        csvHeaderNamesAsList.Add("FP_2_TX");
        csvHeaderNamesAsList.Add("FP_2_TY");
        csvHeaderNamesAsList.Add("FP_2_TZ");
        // Joint angle columns
        csvHeaderNamesAsList.Add("RIGHT_HIP_FLEXION");
        csvHeaderNamesAsList.Add("RIGHT_HIP_ABDUCTION");
        csvHeaderNamesAsList.Add("RIGHT_HIP_INTERNAL_ROTATION");
        csvHeaderNamesAsList.Add("LEFT_HIP_FLEXION");
        csvHeaderNamesAsList.Add("LEFT_HIP_ABDUCTION");
        csvHeaderNamesAsList.Add("LEFT_HIP_INTERNAL_ROTATION");
        // Knee
        csvHeaderNamesAsList.Add("RIGHT_KNEE_FLEXION");
        csvHeaderNamesAsList.Add("RIGHT_KNEE_ABDUCTION");
        csvHeaderNamesAsList.Add("RIGHT_KNEE_INTERNAL_ROTATION");
        csvHeaderNamesAsList.Add("LEFT_KNEE_FLEXION");
        csvHeaderNamesAsList.Add("LEFT_KNEE_ABDUCTION");
        csvHeaderNamesAsList.Add("LEFT_KNEE_INTERNAL_ROTATION");
        // Ankle
        csvHeaderNamesAsList.Add("RIGHT_ANKLE_FLEXION");
        csvHeaderNamesAsList.Add("RIGHT_ANKLE_INVERSION");
        csvHeaderNamesAsList.Add("RIGHT_ANKLE_INTERNAL_ROTATION");
        csvHeaderNamesAsList.Add("LEFT_ANKLE_FLEXION");
        csvHeaderNamesAsList.Add("LEFT_ANKLE_INVERSION");
        csvHeaderNamesAsList.Add("LEFT_ANKLE_INTERNAL_ROTATION");

        //now convert back to a string array, as that's what we use in the General Data Recorder object.
        string[] csvHeaderNames = csvHeaderNamesAsList.ToArray();

        //send the .csv file column header names to the General Data Recorder object
        generalDataRecorderScript.setCsvMarkerDataRowHeaderNames(csvHeaderNames);

    }


    private string[] appendStringToAllElementsOfStringArray(string[] stringArray, string stringToAppend)
    {

        //we must append to a clone of the string array, or else we'd be modifying the original string array!
        string[] stringArrayClone = (string[]) stringArray.Clone();

        //for each element in the string array
        for (uint index = 0; index < stringArray.Length; index++)
        {
            //append the string to the element
            stringArrayClone[index] = stringArrayClone[index] + stringToAppend;
        }

        //return the modified string array
        return stringArrayClone;
    }



    //A setup function.
    //Goals:
    //1.) Set up variables needed for the center of mass multisegment model
    //2.) Establish the boundary of support edges
    //Returns: a boolean indicating whether or not setup is complete
    private bool setupCenterOfMassManager()
    {

        bool setupComplete = false;

        //Given a choice of model, define the model variables
        bool validModelSelected = selectMarkersForCurrentModel(multisegmentModelName);

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

                //establish the boundaries of the base of support
                bool boundariesFound = defineBoundaryOfStabilityWithMarkerPositions();

                //measure subject parameters from the stored frames, e.g. interASIS distance, etc.
                bool subjectParametersMeasured = measureSubjectParametersFromMarkerData();

                // Now that a model has been selected and the data stream is ready, we can specify the .csv header names for the marker data saved to file.
                setFrameDataNaming();

                if (boundariesFound && subjectParametersMeasured) //if all the setup steps have been completed
                {
                    setupComplete = true;
                }
            }
        }

        return setupComplete; //replace this with an overall "setupSuccessful" bool return value
    }




    //Based on our selection of model, fills in the list of required marker names
    private bool selectMarkersForCurrentModel(string modelName)
    {
        bool validModelSelected = false;
       
        if (markerSkeletonToUse == skeletonNameEnum.FiveSegmentComModelWithJointAngles) //the 5 segment model including trunk, both thighs, and both shanks
        {
            //specify which markers are in the model (whether used for COM estimation or not!)
            markersInModel = new string[]{ frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                        backRightPelvisMarkerName, backCenterPelvisMarkerName, leftAcromionMarkerName, rightAcromionMarkerName,
                                        c7MarkerName,
                                        rightFifthMetatarsalMarkerName, leftFifthMetatarsalMarkerName, rightSecondMetatarsalMarkerName,
                                        leftSecondMetatarsalMarkerName, rightFirstDistalPhalanxMarkerName,
                                        leftFirstDistalPhalanxMarkerName, rightHeelMarkerName, leftHeelMarkerName,
                                        rightKneeMarkerName, rightKneeMedialMarkerName, leftKneeMarkerName, leftKneeMedialMarkerName,
                                        leftAnkleMarkerName, leftAnkleMedialMarkerName, rightAnkleMarkerName, rightAnkleMedialMarkerName,
                                        trunkBeltBackMiddleMarkerName , trunkBeltBackRightMarkerName, trunkBeltBackLeftMarkerName,
                                        trunkBeltFrontRightMarkerName, trunkBeltFrontLeftMarkerName,
                                        rightThighFrontMarkerName, leftThighFrontMarkerName, rightThighMarkerName, leftThighMarkerName,
                                        rightArmLateralNearMarkerName, rightArmLateralFarMarkerName, rightArmAnteriorMarkerName,
                                        rightForearmMarkerName, rightElbowMarkerName, rightWristMarkerName,
                                        leftArmLateralNearMarkerName, leftArmLateralFarMarkerName, leftArmAnteriorMarkerName,
                                        leftForearmMarkerName, leftElbowMarkerName, leftWristMarkerName,
                                        rightTibiaMarkerName, rightTibialTuberosityMarkerName,
                                        leftTibiaMarkerName, leftTibialTuberosityMarkerName};

/*            markersInModel = new string[]{ frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                        backRightPelvisMarkerName, backCenterPelvisMarkerName, leftAcromionMarkerName, rightAcromionMarkerName,
                                        rightFifthMetatarsalMarkerName, leftFifthMetatarsalMarkerName, rightSecondMetatarsalMarkerName,
                                        leftSecondMetatarsalMarkerName, rightFirstDistalPhalanxMarkerName,
                                        leftFirstDistalPhalanxMarkerName, rightHeelMarkerName, leftHeelMarkerName, rightKneeMarkerName,
                                        leftKneeMarkerName, leftAnkleMarkerName, rightAnkleMarkerName,
                                        trunkBeltBackMiddleMarkerName , trunkBeltBackRightMarkerName, trunkBeltBackLeftMarkerName,
                                            trunkBeltFrontRightMarkerName, trunkBeltFrontLeftMarkerName};*/

            //specify the segments used in the COM-estimation model 
            segmentsInModel = new string[] { trunkSegmentName, leftThighSegmentName, rightThighSegmentName, leftShankSegmentName, rightShankSegmentName };

            // Initialize variables dependent on the number of segments
            segmentUsedForComEstimationThisFrameFlags = new bool[segmentsInModel.Length];
            segmentComEstimatedWithSimplifiedModelThisFrameFlags = new bool[segmentsInModel.Length];


            //create bodySegment objects to represent all segments in the model and store them in the segment array
            //trunk
            float trunkMassAsFractionOfBodyMass = getSegmentFractionOfTotalBodyMassFiveSegmentModel(trunkSegmentType, subjectSex);
            bodySegment trunkSegment = new bodySegment(trunkSegmentName, trunkSegmentName, trunkMassAsFractionOfBodyMass, subjectSex,
                                                       nameOfShoulderCenterVirtualMarker, recontructHipJointCentersString,
                                                       TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);

            //left thigh 
            float thighMassAsFractionOfBodyMass = getSegmentFractionOfTotalBodyMassFiveSegmentModel(thighSegmentType, subjectSex);
            bodySegment leftThighSegment = new bodySegment(leftThighSegmentName, thighSegmentType, thighMassAsFractionOfBodyMass, subjectSex,
                                                       nameOfLeftHjcVirtualMarker, leftKneeMarkerName,
                                                       TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);
            //right thigh 
            bodySegment rightThighSegment = new bodySegment(rightThighSegmentName, thighSegmentType, thighMassAsFractionOfBodyMass, subjectSex,
                                           nameOfRightHjcVirtualMarker, rightKneeMarkerName,
                                           TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);
            //left shank
            float shankMassAsFractionOfBodyMass = getSegmentFractionOfTotalBodyMassFiveSegmentModel(shankSegmentType, subjectSex);
            bodySegment leftShankSegment = new bodySegment(leftShankSegmentName, shankSegmentType, shankMassAsFractionOfBodyMass, subjectSex,
                                                       leftKneeMarkerName, leftAnkleMarkerName,
                                                       TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);
            //right shank
            bodySegment rightShankSegment = new bodySegment(rightShankSegmentName, shankSegmentType, shankMassAsFractionOfBodyMass, subjectSex,
                                           rightKneeMarkerName, rightAnkleMarkerName,
                                           TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);

            //store all segment models in an array
            segmentsInModelObjectArray = new bodySegment[] { trunkSegment, leftThighSegment, rightThighSegment, leftShankSegment, rightShankSegment };

            //do a "unit test" of the total model fractional body mass, ensuring that it's sufficiently close to 1.0
            checkIfSegmentModelHasTotalFractionalBodyMassSummingToOne(segmentsInModelObjectArray);

            //instantiate the segment COM position array to the correct size
            segmentComPositions = new Vector3[segmentsInModelObjectArray.Length];
            segmentEstimatedComPositions = new Vector3[segmentsInModelObjectArray.Length]; //instantiate the segment COM position estimates array to the correct size

            // Specify rigid body names needed for joint angle computations (may not be all rigid bodies in the model)
            rigidBodyNamesForJointAngleComputation = new string[] { pelvisSegmentName, leftThighKneeJointSegmentName, rightThighKneeJointSegmentName,
                leftThighHipJointSegmentName, rightThighHipJointSegmentName, leftShankKneeJointSegmentName, rightShankKneeJointSegmentName,
                leftShankAnkleJointSegmentName, rightShankAnkleJointSegmentName, leftFootSegmentName, rightFootSegmentName };

            // RIGID BODY RECONSTRUCTION SECTION: START*********************************************************************************************
            markersInPelvis = new string[] { frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                        backRightPelvisMarkerName, backCenterPelvisMarkerName }; // names of all markers in the pelvis rigid body. Initialized in setup.
            markersInTrunk = new string[] { trunkBeltBackMiddleMarkerName , trunkBeltBackRightMarkerName, trunkBeltBackLeftMarkerName, 
                                            trunkBeltFrontRightMarkerName, trunkBeltFrontLeftMarkerName, c7MarkerName}; // ARIYADAV changed to add C7 marker in Trunk
            markersInRightFoot = 
                new string[] { rightFirstDistalPhalanxMarkerName, rightFifthMetatarsalMarkerName, 
                    rightSecondMetatarsalMarkerName, rightHeelMarkerName, rightAnkleMarkerName, 
                    rightAnkleMedialMarkerName }; // names of all markers in the left foot segment. Initialized in setup.
            markersInLeftFoot = 
                new string[] { leftFirstDistalPhalanxMarkerName, leftFifthMetatarsalMarkerName, 
                    leftSecondMetatarsalMarkerName, leftHeelMarkerName, leftAnkleMarkerName, 
                    leftAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup.

            markersInLeftShank =
                new string[] {leftTibiaMarkerName, leftTibialTuberosityMarkerName, leftKneeMarkerName, 
                    leftKneeMedialMarkerName, leftAnkleMarkerName, leftAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInRightShank =
                new string[] { rightTibiaMarkerName, rightTibialTuberosityMarkerName, rightKneeMarkerName, 
                    rightKneeMedialMarkerName, rightAnkleMarkerName, rightAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInLeftThigh =
                new string[] { leftThighFrontMarkerName, leftThighMarkerName, leftKneeMedialMarkerName, leftKneeMarkerName}; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInRightThigh =
                new string[] { rightThighFrontMarkerName, rightThighMarkerName, rightKneeMedialMarkerName, rightKneeMarkerName}; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInLeftArm =
                new string[] { leftArmAnteriorMarkerName, leftArmLateralFarMarkerName, leftArmLateralNearMarkerName,
                leftElbowMarkerName, leftAcromionMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInRightArm =
                new string[] { rightArmAnteriorMarkerName, rightArmLateralFarMarkerName, rightArmLateralNearMarkerName,
                rightElbowMarkerName, rightAcromionMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            // Specify which segments should have their markers reconstructed if occluded this frame. 
            // KEY NOTE: the rigid body names and the list of string[] with corresponding marker names must be in the same order!!!!!!!
            // I.E.: if rigidBodiesToReconstructForModel has order (pelvis, trunk, left foot, right foot, ...), then the 
            // rigidBodiesToReconstructForModelMarkerNames must have order (pelvisMarkerNames, trunkMarkerNames, leftFootMarkerNames, rightFootMarkerNames, ...).
            rigidBodiesToReconstructForModel = new string[] { pelvisName, trunkSegmentName, leftFootSegmentName, rightFootSegmentName, leftShankSegmentName,
                rightShankSegmentName, leftThighSegmentName, rightThighSegmentName, rightArmSegmentName, leftArmSegmentName};

            rigidBodiesToReconstructForModelMarkerNames = new List<string[]>() { markersInPelvis, markersInTrunk, markersInLeftFoot, 
                markersInRightFoot, markersInLeftShank, 
                markersInRightShank, markersInLeftThigh, markersInRightThigh, markersInRightArm, markersInLeftArm }; // ARIYADAV

            // The reconstructed marker flags will be the length of the number of markers in the skeleton.
            markerInModelWasReconstructedThisFrameFlags = new bool[markersInModel.Length]; // a boolean array will keep track of if each marker in the model was 
                                                                                           //reconstructed this frame. Note, default value for bool array is false.

            //specify which virtual markers must be computed for the model
            virtualMarkerNames = new string[] { recontructHipJointCentersString, nameOfShoulderCenterVirtualMarker };
            virtualMarkerPositions = new Vector3[virtualMarkerNames.Length];

            // Initialize variables dependent on the number of virtual markers in the model 
            virtualMarkersCouldBeCalculatedThisFrameFlag = new bool[virtualMarkerNames.Length];

            //specify the hip joint marker names 
            hipJointMarkerNames = new string[] { nameOfLeftHjcVirtualMarker, nameOfRightHjcVirtualMarker };
            hipJointMarkerPositions = new Vector3[hipJointMarkerNames.Length];

            //specify the physical markers required to compute each virtual marker.
            //Note that it may be possible to compute some virtual markers using only a subset of
            //the listed markers.
            markerNamesForHipJointCenters = new string[] {frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                               backRightPelvisMarkerName };

            markerNamesForShoulderCenter = new string[] { leftAcromionMarkerName, rightAcromionMarkerName, c7MarkerName };

            validModelSelected = true;
        }
        else if (markerSkeletonToUse == skeletonNameEnum.FeetLegsTrunkAndOnlyPelvicBelt) // this skeleton has foot and shoulder markers, ankle and knee angle computation,
                                                        // and pelvic belt control (NO chest belt markers). 
        {
            //specify which markers are in the model (whether used for COM estimation or not!)
            markersInModel = new string[]{ frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                        backRightPelvisMarkerName, backCenterPelvisMarkerName, leftAcromionMarkerName, rightAcromionMarkerName,
                                        c7MarkerName, sternalNotchMarkerName, 
                                        rightFifthMetatarsalMarkerName, leftFifthMetatarsalMarkerName, rightSecondMetatarsalMarkerName,
                                        leftSecondMetatarsalMarkerName, rightFirstDistalPhalanxMarkerName,
                                        leftFirstDistalPhalanxMarkerName, rightHeelMarkerName, leftHeelMarkerName,
                                        rightKneeMarkerName, rightKneeMedialMarkerName, leftKneeMarkerName, leftKneeMedialMarkerName,
                                        leftAnkleMarkerName, leftAnkleMedialMarkerName, rightAnkleMarkerName, rightAnkleMedialMarkerName,
                                        rightThighFrontMarkerName, leftThighFrontMarkerName, rightThighMarkerName, leftThighMarkerName,
                                        leftThighSideBottomMarkerName, rightThighSideBottomMarkerName, 
                                        rightTibiaMarkerName, rightTibialTuberosityMarkerName,
                                        leftTibiaMarkerName, leftTibialTuberosityMarkerName};

            //specify the segments used in the COM-estimation model 
            segmentsInModel = new string[] { trunkSegmentName, leftThighSegmentName, rightThighSegmentName, leftShankSegmentName, rightShankSegmentName };

            // Initialize variables dependent on the number of segments
            segmentUsedForComEstimationThisFrameFlags = new bool[segmentsInModel.Length];
            segmentComEstimatedWithSimplifiedModelThisFrameFlags = new bool[segmentsInModel.Length];


            //create bodySegment objects to represent all segments in the model and store them in the segment array
            //trunk
            float trunkMassAsFractionOfBodyMass = getSegmentFractionOfTotalBodyMassFiveSegmentModel(trunkSegmentType, subjectSex);
            bodySegment trunkSegment = new bodySegment(trunkSegmentName, trunkSegmentName, trunkMassAsFractionOfBodyMass, subjectSex,
                                                       nameOfShoulderCenterVirtualMarker, recontructHipJointCentersString,
                                                       TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);

            //left thigh 
            float thighMassAsFractionOfBodyMass = getSegmentFractionOfTotalBodyMassFiveSegmentModel(thighSegmentType, subjectSex);
            bodySegment leftThighSegment = new bodySegment(leftThighSegmentName, thighSegmentType, thighMassAsFractionOfBodyMass, subjectSex,
                                                       nameOfLeftHjcVirtualMarker, leftKneeMarkerName,
                                                       TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);
            //right thigh 
            bodySegment rightThighSegment = new bodySegment(rightThighSegmentName, thighSegmentType, thighMassAsFractionOfBodyMass, subjectSex,
                                           nameOfRightHjcVirtualMarker, rightKneeMarkerName,
                                           TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);
            //left shank
            float shankMassAsFractionOfBodyMass = getSegmentFractionOfTotalBodyMassFiveSegmentModel(shankSegmentType, subjectSex);
            bodySegment leftShankSegment = new bodySegment(leftShankSegmentName, shankSegmentType, shankMassAsFractionOfBodyMass, subjectSex,
                                                       leftKneeMarkerName, leftAnkleMarkerName,
                                                       TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);
            //right shank
            bodySegment rightShankSegment = new bodySegment(rightShankSegmentName, shankSegmentType, shankMassAsFractionOfBodyMass, subjectSex,
                                           rightKneeMarkerName, rightAnkleMarkerName,
                                           TisserandMaleDistancesFromProximalMarker, TisserandFemaleDistancesFromProximalMarker);

            //store all segment models in an array
            segmentsInModelObjectArray = new bodySegment[] { trunkSegment, leftThighSegment, rightThighSegment, leftShankSegment, rightShankSegment };

            //do a "unit test" of the total model fractional body mass, ensuring that it's sufficiently close to 1.0
            checkIfSegmentModelHasTotalFractionalBodyMassSummingToOne(segmentsInModelObjectArray);

            //instantiate the segment COM position array to the correct size
            segmentComPositions = new Vector3[segmentsInModelObjectArray.Length];
            segmentEstimatedComPositions = new Vector3[segmentsInModelObjectArray.Length]; //instantiate the segment COM position estimates array to the correct size

            // Specify rigid body names needed for joint angle computations (may not be all rigid bodies in the model)
            rigidBodyNamesForJointAngleComputation = new string[] { pelvisSegmentName, leftThighKneeJointSegmentName, rightThighKneeJointSegmentName,
                leftThighHipJointSegmentName, rightThighHipJointSegmentName, leftShankKneeJointSegmentName, rightShankKneeJointSegmentName,
                leftShankAnkleJointSegmentName, rightShankAnkleJointSegmentName, leftFootSegmentName, rightFootSegmentName };

            // RIGID BODY RECONSTRUCTION SECTION: START*********************************************************************************************
            markersInPelvis = new string[] { frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                        backRightPelvisMarkerName, backCenterPelvisMarkerName }; // names of all markers in the pelvis rigid body. Initialized in setup.
            markersInRightFoot =
                new string[] { rightFirstDistalPhalanxMarkerName, rightFifthMetatarsalMarkerName,
                    rightSecondMetatarsalMarkerName, rightHeelMarkerName, rightAnkleMarkerName,
                    rightAnkleMedialMarkerName }; // names of all markers in the left foot segment. Initialized in setup.
            markersInLeftFoot =
                new string[] { leftFirstDistalPhalanxMarkerName, leftFifthMetatarsalMarkerName,
                    leftSecondMetatarsalMarkerName, leftHeelMarkerName, leftAnkleMarkerName,
                    leftAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup.

            markersInLeftShank =
                new string[] {leftTibiaMarkerName, leftTibialTuberosityMarkerName, leftKneeMarkerName,
                    leftKneeMedialMarkerName, leftAnkleMarkerName, leftAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInRightShank =
                new string[] { rightTibiaMarkerName, rightTibialTuberosityMarkerName, rightKneeMarkerName,
                    rightKneeMedialMarkerName, rightAnkleMarkerName, rightAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInLeftThigh =
                new string[] { leftThighFrontMarkerName, leftThighMarkerName, leftThighSideBottomMarkerName, leftKneeMedialMarkerName, leftKneeMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInRightThigh =
                new string[] { rightThighFrontMarkerName, rightThighMarkerName, rightThighSideBottomMarkerName, rightKneeMedialMarkerName, rightKneeMarkerName }; // names of all markers in the right foot segment. Initialized in setup. ARIYADAV

            markersInShoulderSegment =
                new string[] { rightAcromionMarkerName, leftAcromionMarkerName, sternalNotchMarkerName, c7MarkerName };

            // Specify which segments should have their markers reconstructed if occluded this frame. 
            // KEY NOTE: the rigid body names and the list of string[] with corresponding marker names must be in the same order!!!!!!!
            // I.E.: if rigidBodiesToReconstructForModel has order (pelvis, trunk, left foot, right foot, ...), then the 
            // rigidBodiesToReconstructForModelMarkerNames must have order (pelvisMarkerNames, trunkMarkerNames, leftFootMarkerNames, rightFootMarkerNames, ...).
            rigidBodiesToReconstructForModel = new string[] { pelvisName, leftFootSegmentName, rightFootSegmentName, leftShankSegmentName,
                rightShankSegmentName, leftThighSegmentName, rightThighSegmentName, shoulderSegmentName};

            rigidBodiesToReconstructForModelMarkerNames = new List<string[]>() { markersInPelvis, markersInLeftFoot,
                markersInRightFoot, markersInLeftShank,
                markersInRightShank, markersInLeftThigh, markersInRightThigh, markersInShoulderSegment}; // ARIYADAV

            // The reconstructed marker flags will be the length of the number of markers in the skeleton.
            markerInModelWasReconstructedThisFrameFlags = new bool[markersInModel.Length]; // a boolean array will keep track of if each marker in the model was 
                                                                                           //reconstructed this frame. Note, default value for bool array is false.

            //specify which virtual markers must be computed for the model
            virtualMarkerNames = new string[] { recontructHipJointCentersString, nameOfShoulderCenterVirtualMarker };
            virtualMarkerPositions = new Vector3[virtualMarkerNames.Length];

            // Initialize variables dependent on the number of virtual markers in the model 
            virtualMarkersCouldBeCalculatedThisFrameFlag = new bool[virtualMarkerNames.Length];

            //specify the hip joint marker names 
            hipJointMarkerNames = new string[] { nameOfLeftHjcVirtualMarker, nameOfRightHjcVirtualMarker };
            hipJointMarkerPositions = new Vector3[hipJointMarkerNames.Length];

            //specify the physical markers required to compute each virtual marker.
            //Note that it may be possible to compute some virtual markers using only a subset of
            //the listed markers.
            markerNamesForHipJointCenters = new string[] {frontLeftPelvisMarkerName, frontRightPelvisMarkerName, backLeftPelvisMarkerName,
                                               backRightPelvisMarkerName };

            markerNamesForShoulderCenter = new string[] { leftAcromionMarkerName, rightAcromionMarkerName, c7MarkerName };

            validModelSelected = true;
        }
        else
        {
            //raise an exception since no valid model was specified
            string errorMessage = "Marker skeleton enum is (somehow) invalid. Enter a valid COM model name.";
            printWarningOrErrorToConsoleIfDebugModeIsDefined(logAWarningSpecifier, errorMessage);
        }

        //initialize the marker storage instance variables to the correct size
        int numberOfMarkersInModel = markersInModel.Length;
        markersInModelOcclusionStatus = new bool[numberOfMarkersInModel];
        markersInModelXPositions = new float[numberOfMarkersInModel];
        markersInModelYPositions = new float[numberOfMarkersInModel];
        markersInModelZPositions = new float[numberOfMarkersInModel];

        //for reconstructed markers ISI
        reconstructedMarkersInModelXPositions = new float[numberOfMarkersInModel];
        reconstructedMarkersInModelYPositions = new float[numberOfMarkersInModel];
        reconstructedMarkersInModelZPositions = new float[numberOfMarkersInModel];

        return validModelSelected;
    }



    private float getSegmentFractionOfTotalBodyMassFiveSegmentModel(string segmentType, string subjectSex)
    {
        float segmentFractionOfTotalBodyMass = 0.0f;

        //depending on segment type
        if (segmentType == trunkSegmentType)  //if segment is the trunk
        {
            if (subjectSex == maleSexName) //if subject is a man
            {
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[trunkSegmentType] + deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[headSegmentType] +
                    2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[armSegmentType] + 2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[forearmSegmentType] +
                    2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[handSegmentType];
            }
            else //if subject is a woman
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[trunkSegmentType] + deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[headSegmentType] +
                    2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[armSegmentType] + 2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[forearmSegmentType] +
                    2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[handSegmentType];
            }
        } else if (segmentType == thighSegmentType) //if segment is a thigh
        {
            if (subjectSex == maleSexName) //if subject is a man
            {
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[thighSegmentType];
            }
            else //if subject is a woman
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[thighSegmentType];
            }
        } else if (segmentType == shankSegmentType) //if segment is a shank
        {
            if (subjectSex == maleSexName) //if subject is a man
            {
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[shankSegmentType] +
                    deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[footSegmentType];
            }
            else //if subject is a woman
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[shankSegmentType] +
                    deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[footSegmentType];
            }
        }

        return segmentFractionOfTotalBodyMass;
    }



    //Stores the first n frames in which none of the model markers are occluded.
    //
    private bool storeMarkerFramesForSetup()
    {
        Debug.Log("Trying to store frames for setup.");
        bool framesStored = false;
        if (storingFramesForSetupFlag) //if we're still storing frames, see if the most recent frame can be stored
        {
            bool isMarkerDataOld = getMarkerDataForAllMarkersNeededInComModel(markersInModel);

            if (isMarkerDataOld)
            {
                Debug.Log("Trying to store frames for setup, but marker data is old.");
            }

            if (markersInModelOcclusionStatus.All(x => !x) && !isMarkerDataOld) //if no markers in the model are occluded this frame
            {
                //store the marker occlusion status and positions as elements in a list
                setupMarkerFramesOcclusionStatus.Add(markersInModelOcclusionStatus);
                setupMarkerFramesXPos.Add(markersInModelXPositions);
                setupMarkerFramesYPos.Add(markersInModelYPositions);
                setupMarkerFramesZPos.Add(markersInModelZPositions);

                //compute and store needed virtual marker locations in the frame as well
                computeVirtualMarkerLocations();
                setupHipJointPositions.Add(hipJointMarkerPositions); //store the Vector3 array containing the two hip joint center locations for this frame
                //increment the counter which keeps track of how many frames we've stored
                numberOfSetupFramesAlreadyStored = numberOfSetupFramesAlreadyStored + 1;

                Debug.Log("For setup, have stored the following number of frames: " + numberOfSetupFramesAlreadyStored);
            }
            else //if some markers are missing this frame
            {
                printLogMessageToConsoleIfDebugModeIsDefined("storeMarkerFramesForSetup(): markers missing from current frame");

                //print out which markers are missing
                for (uint markerInModelIndex = 0; markerInModelIndex < markersInModelOcclusionStatus.Length; markerInModelIndex++)
                {
                    if (markersInModelOcclusionStatus[markerInModelIndex] == true)
                    {
                        string logMessage = "storeMarkerFramesForSetup(): Marker with name " + markersInModel[markerInModelIndex] + "is occluded or missing from the model.";
                        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
                    }
                }
            }
        }

        //manage the return boolean, which should be true if we've collected enough frames
        bool enoughFramesStored = false;
        if (numberOfSetupFramesAlreadyStored >= numberOfSetupMarkerFrames) //if we've collected enough frames
        {
            enoughFramesStored = true;
        }

        return enoughFramesStored;

    }


    //This function is used to get the average position of all the model markers across the stored startup frames. 
    private Vector3[] getAveragePositionOfAllMarkersInStartupFrames()
    {
        Vector3[] averagePositionOfAllMarkersInStartupFrames = new Vector3[markersInModel.Length];
        for (int markerInModelIndex = 0; markerInModelIndex < markersInModel.Length; markerInModelIndex++)
        {
            averagePositionOfAllMarkersInStartupFrames[markerInModelIndex] = getMarkerAveragePositionInStartupFramesByName(markersInModel[markerInModelIndex]);
        }

        return averagePositionOfAllMarkersInStartupFrames;
    }


    //This function finds the boundaries of the base of support using the stored
    //marker frames collected in setup.
    //Assumptions:
    //          - x-axis is in person's mediolateral direction.
    //          - y-axis is person's AP direction
    private bool defineBoundaryOfStabilityWithMarkerPositions()
    {
        //get right boundary for base of support
        Vector3 rightFifthMtPosition = getMarkerAveragePositionInStartupFramesByName(rightFifthMetatarsalMarkerName);
        rightEdgeBaseOfSupportXPos = rightFifthMtPosition.x; //Item 1 of the tuple is the X-position
        rightEdgeBaseOfSupportYPos = rightFifthMtPosition.y; //Item 1 of the tuple is the X-position

        //get left boundary for base of support
        Vector3 leftFifthMtPosition = getMarkerAveragePositionInStartupFramesByName(leftFifthMetatarsalMarkerName);
        leftEdgeBaseOfSupportXPos = leftFifthMtPosition.x; //Item 1 of the tuple is the X-position
        leftEdgeBaseOfSupportYPos = leftFifthMtPosition.y; //Item 1 of the tuple is the X-position

        //get forward boundary for base of support as mean of the two big toenail (1st distal phalanx) positions
        Vector3 rightToePosition = getMarkerAveragePositionInStartupFramesByName(rightFirstDistalPhalanxMarkerName);
        Vector3 leftToePosition = getMarkerAveragePositionInStartupFramesByName(leftFirstDistalPhalanxMarkerName);
        frontEdgeBaseOfSupportYPos = ((rightToePosition.y + leftToePosition.y) / 2);
        frontEdgeBaseOfSupportXPos = ((rightToePosition.x + leftToePosition.x) / 2);

        //get backward boundary for base of support as mean of the two heel positions
        Vector3 rightHeelPosition = getMarkerAveragePositionInStartupFramesByName(rightHeelMarkerName);
        Vector3 leftHeelPosition = getMarkerAveragePositionInStartupFramesByName(leftHeelMarkerName);
        backEdgeBaseOfSupportYPos = ((rightHeelPosition.y + leftHeelPosition.y) / 2);
        backEdgeBaseOfSupportXPos = ((rightHeelPosition.x + leftHeelPosition.x) / 2);

        string logMessage = "Left, right, front, back (x,x,y,y) boundaries for BoS are: (" + leftEdgeBaseOfSupportXPos + ", " +
            rightEdgeBaseOfSupportXPos + ", " + frontEdgeBaseOfSupportYPos + ", " + backEdgeBaseOfSupportYPos + ")";
        printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        //for now we just assume this process was successful. Could implement a meaningful check.
        return true;
    }


    //Using the stored setup marker frames, measure some key parameters like
    //interASIS width.
    private bool measureSubjectParametersFromMarkerData()
    {
        //get distance between the two ASIS markers. Used in HJC computation.
        Vector3 leftAsisPosition = getMarkerAveragePositionInStartupFramesByName(frontLeftPelvisMarkerName);
        Vector3 rightAsisPosition = getMarkerAveragePositionInStartupFramesByName(frontRightPelvisMarkerName);
        interAsisWidth = Vector3.Distance(leftAsisPosition, rightAsisPosition);

        Debug.Log("Inter-ASIS width is " + interAsisWidth);

        //get thigh length, if needed
        leftThighLength = getAverageDistanceBetweenTwoMarkersInSetupFramesByName(nameOfLeftHjcVirtualMarker, leftKneeMarkerName);
        rightThighLength = getAverageDistanceBetweenTwoMarkersInSetupFramesByName(nameOfRightHjcVirtualMarker, rightKneeMarkerName);
        Debug.Log("Left and right thigh lengths from startup frames are (L, R): ( " + leftThighLength + ", " + rightThighLength + " ).");

        //get shank length, if needed
        leftShankLength = getAverageDistanceBetweenTwoMarkersInSetupFramesByName(leftKneeMarkerName, leftAnkleMarkerName);
        rightShankLength = getAverageDistanceBetweenTwoMarkersInSetupFramesByName(rightKneeMarkerName, rightAnkleMarkerName);
        Debug.Log("Left and right shank lengths from startup frames are (L, R): ( " + leftShankLength + ", " + rightShankLength + " ).");

        // Get the length of the inverted pendulum used to model the body.
        // Let's use the distance between the average of the two lateral malleoli (so, center of the two ankles)
        // and the center of mass. 
        //Vector3 bodyAsInvertedPendulumBase = (getMarkerAveragePositionInStartupFramesByName(leftAnkleMarkerName) + getMarkerAveragePositionInStartupFramesByName(rightAnkleMarkerName)) / (2.0f);
        //Vector3 bodyAsInvertedPendulumCenterOfMass = 


        float bodyAsInvertedPendulumRightLegLength = getAverageDistanceBetweenTwoMarkersInSetupFramesByName(nameOfRightHjcVirtualMarker, rightAnkleMarkerName);
        float bodyAsInvertedPendulumLeftLegLength = getAverageDistanceBetweenTwoMarkersInSetupFramesByName(nameOfLeftHjcVirtualMarker, leftAnkleMarkerName);
        bodyAsInvertedPendulumLegLength = (bodyAsInvertedPendulumRightLegLength + bodyAsInvertedPendulumLeftLegLength) / 2.0f;


        // Only compute trunk-belt related items if the marker skeleton has trunk belt markers.
        if (rigidBodiesToReconstructForModel.Contains(trunkSegmentName) == true)
        {
            // Also compute the vertical distance from the mid-ankle positiion to the center of the trunk belt, 
            // using averages across the startup frames
            Vector3 leftAnklePositionAverage = getMarkerAveragePositionInStartupFramesByName(leftAnkleMarkerName);
            Vector3 rightAnklePositionAverage = getMarkerAveragePositionInStartupFramesByName(rightAnkleMarkerName);
            Vector3 midAnklePositionAverage = (leftAnklePositionAverage + rightAnklePositionAverage) / 2.0f;
            Vector3 trunkBeltFrontLeftPositionAverage = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontLeftMarkerName);
            Vector3 trunkBeltFrontRightPositionAverage = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontLeftMarkerName);
            Vector3 trunkBeltBackCenterPositionAverage = getMarkerAveragePositionInStartupFramesByName(trunkBeltFrontLeftMarkerName);
            // Compute the trunk belt midpoint position average in startup frames
            Vector3 midpointTrunkBeltAveragePositionStartupFrames = GetTrunkBeltMidpointFromTrunkBeltMarkers(trunkBeltBackCenterPositionAverage,
                 trunkBeltFrontLeftPositionAverage,
                 trunkBeltFrontRightPositionAverage);
            // Compute distance mid-ankles to mid-trunk belt
            float convertMillimetersToMeters = 1000.0f;
            verticalDistanceMidAnkleToMidTrunkBeltInMeters =
                (midpointTrunkBeltAveragePositionStartupFrames.z - midAnklePositionAverage.z) / convertMillimetersToMeters;
        }

        //for now we just assume this process was successful. Could implement a meaningful check.
        return true;

    }



    //During setup, we store a few dozen marker frames from which we compute some key
    // parameters, like base of support boundaries and interASIS width.
    //This function allows us to compute the average position of a marker across these "setup frames,"
    //by name.
    public Vector3 getMarkerAveragePositionInStartupFramesByName(string markerName)
    {

        int markerIndexInEachArray = Array.IndexOf(markersInModel, markerName);
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
        //printLogMessageToConsoleIfDebugModeIsDefined(logMessage);

        //create and return a Vector3 representing the position
        return new Vector3(markerXPos, markerYPos, markerZPos);
    }



    private float getAverageDistanceBetweenTwoMarkersInSetupFramesByName(string marker1Name, string marker2Name)
    {
        int marker1IndexInEachArray = Array.IndexOf(markersInModel, marker1Name);
        bool marker1IsHipJointCenter = false;

        //figure out whether marker 1 is a typical marker or a hip joint center
        if (marker1IndexInEachArray < 0) //if the marker name is not an available physical marker
        {
            marker1IndexInEachArray = Array.IndexOf(hipJointMarkerNames, marker1Name); //check if the marker is a hip joint center
            if (marker1IndexInEachArray >= 0) //if the marker is a hip joint center
            {
                marker1IsHipJointCenter = true; //then this marker is a hip joint center, so access it's position through the HJC array
            }
            else //then the marker cannot be found
            {
                printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, "Marker named " + marker1Name + " is not available in setup frames.");
            }
        }


        int marker2IndexInEachArray = Array.IndexOf(markersInModel, marker2Name);
        bool marker2IsHipJointCenter = false;

        //figure out whether marker 2 is a typical marker or a hip joint center
        if (marker2IndexInEachArray < 0) //if the marker name is not an available physical marker
        {
            marker2IndexInEachArray = Array.IndexOf(hipJointMarkerNames, marker2Name); //check if the marker is a hip joint center
            if (marker2IndexInEachArray >= 0) //if the marker is a hip joint center
            {
                marker2IsHipJointCenter = true; //then this marker is a hip joint center, so access it's position through the HJC array
            }
            else //then the marker cannot be found
            {
                printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, "Marker named " + marker2Name + " is not available in setup frames.");
            }
        }

        //for each setup frame, get the marker positions and find the distance between the two markers
        float distanceBetweenMarkers = 0;
        for (int setupFrameIndex = 0; setupFrameIndex < setupMarkerFramesXPos.Count; setupFrameIndex++)
        {
            Vector3 marker1Position;
            Vector3 marker2Position;

            //get marker 1 position for this frame
            if (!marker1IsHipJointCenter)
            {
                marker1Position = new Vector3(setupMarkerFramesXPos[setupFrameIndex][marker1IndexInEachArray],
                    setupMarkerFramesYPos[setupFrameIndex][marker1IndexInEachArray],
                    setupMarkerFramesZPos[setupFrameIndex][marker1IndexInEachArray]);
            }
            else
            {
                marker1Position = setupHipJointPositions[setupFrameIndex][marker1IndexInEachArray];
            }

            //get marker 2 position for this frame
            if (!marker2IsHipJointCenter)
            {
                marker2Position = new Vector3(setupMarkerFramesXPos[setupFrameIndex][marker2IndexInEachArray],
                    setupMarkerFramesYPos[setupFrameIndex][marker2IndexInEachArray],
                    setupMarkerFramesZPos[setupFrameIndex][marker2IndexInEachArray]);
            }
            else
            {
                marker2Position = setupHipJointPositions[setupFrameIndex][marker2IndexInEachArray];
            }

            //Get distance between markers and add it to running sum
            distanceBetweenMarkers = distanceBetweenMarkers + (marker2Position - marker1Position).magnitude;
        }

        //take the mean of the distances between the two markers
        distanceBetweenMarkers = distanceBetweenMarkers / setupMarkerFramesXPos.Count;
        return distanceBetweenMarkers;

    }


    //End: Functions called for setup only********************************************************








    //A simple test function that computes a very simple COM estimate. It 
    //takes the average position of 4 pelvis markers and uses the average as the COM position. 
    private void updateCenterOfMassTest()
    {
        //get the positions and occlusion status of the four pelvic markers
        var frontLeftResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(frontLeftPelvisMarkerName);
        var frontRightResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(frontRightPelvisMarkerName);
        var backRightResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(backRightPelvisMarkerName);
        var backLeftResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(backLeftPelvisMarkerName);

        //average the x- and y-positions of the four markers to get a shitey guess at COM 
        mostRecentComXPosition = (frontLeftResultTuple.Item2 + frontRightResultTuple.Item2 + backRightResultTuple.Item2 + backLeftResultTuple.Item2) / 4;
        mostRecentComYPosition = (frontLeftResultTuple.Item3 + frontRightResultTuple.Item3 + backRightResultTuple.Item3 + backLeftResultTuple.Item3) / 4;
    }



    private (bool, bool) updateCenterOfMass()
    {
        int test = 0;

        //zero: figure out how complex you want to go. Perhaps a pelvis + trunk model would be sufficient. Probably a good place to start!!!!

        //first, get position and occlusion status of all the markers in our model
        bool isMarkerDataOld = getMarkerDataForAllMarkersNeededInComModel(markersInModel);

        // Initialize the other return flag, whether or not the pelvis segment is available this frame. 
        // For now, it is a substitute for a flag of whether or not we could compute the desired skeleton COM.
        bool pelvisAvailableThisFrame = false;

        //MANUALLY SET SOME MARKERS OCCLUSION STATUS TO TRUE TO TEST RECONSTRUCTION 
        if(testOccludeNamedMarker == true)
        {
            markersInModelOcclusionStatus[Array.IndexOf(markersInModel, frontLeftPelvisMarkerName)] = true;
        }

        ////markersInModelOcclusionStatus[Array.IndexOf(markersInModel, rightAnkleMarkerName)] = true;
        ////markersInModelOcclusionStatus[Array.IndexOf(markersInModel, leftAnkleMarkerName)] = true;
        float millisecondsSinceLastFreshMarkerData = -1.0f;
        if (!isMarkerDataOld) //if the marker positions have changed since the last time we read it
        {
            // Mark the time at which we got fresh data. This will allow us to Debug print an estimate of the frequency at which we 
            // read Vicon Data Stream data (using the data stream SDK).
            mostRecentFreshViconDataTimeStamp = Time.time;
            float viconDataFetchFrequency = 1.0f / (mostRecentFreshViconDataTimeStamp - previousViconDataTimeStamp); // Rate of data fetch from Vicon data stream SDK. 
            printLogMessageToConsoleIfDebugModeIsDefined("Rate of Vicon data stream fetch: " +
            viconDataFetchFrequency);
            previousViconDataTimeStamp = mostRecentFreshViconDataTimeStamp;


            if (!tempStopWatch.IsRunning)
            {
                tempStopWatch.Start();
            } else
            {
                tempStopWatch.Stop();
                // Get the elapsed time as a TimeSpan value.
                long ticksThisTime = 0;
                long nanosecPerTick = (1000L * 1000L * 1000L) / Stopwatch.Frequency;
                ticksThisTime = tempStopWatch.ElapsedTicks;
                millisecondsSinceLastFreshMarkerData = (float)(((double)nanosecPerTick * ticksThisTime) / (1000L * 1000L));
                // Format and display the TimeSpan value.

                //Debug.Log("Milliseconds between new marker data is " + millisecondsSinceLastFreshMarkerData);
                tempStopWatch.Reset();
                tempStopWatch.Start();
            }
            //Reconstruct missing markers that can be reconstructed and that are necessary. 
            //These markers are listed 
            List<string> rigidBodiesAvailableThisFrame = reconstructMissingMarkersOnListedRigidBodies(rigidBodiesToReconstructForModel);
            pelvisAvailableThisFrame = rigidBodiesAvailableThisFrame.Contains(pelvisName);

            //Note, currently if pelvis segment is missing then we have no recourse
            //and just render the last COM location.
            if (pelvisAvailableThisFrame)
            {
                //compute all virtual marker locations. Note, the virtual markers are the locations
                //that can be calculated using different algorithms, depending on which markers are available
                //Note: each virtual marker location is currently stored in the array virtualMarkerPositions
                //as a Vector3. 
                computeVirtualMarkerLocations();

                //compute the COM location of each segment
                //Note: positions of each segment COM stored in an array of type Vector3.
                List<string> segmentsWithoutComAvailable = computeModelSegmentCentersOfMass();

                //Reconstruct segment COM positions using our custom method, if needed. 

                //reconstruct the thighs and shanks if needed
                List<string> segmentsWithEstimatedComPositions = estimateThighAndShankComPositionsIfNeededLineFromHjcToAnkle(segmentsWithoutComAvailable);

                //take a weighted average of the segment COMs to get total body COM
                computeTotalBodyCenterOfMassFromSegments(segmentsWithoutComAvailable, segmentsWithEstimatedComPositions);

                // Compute key joint angles (ankle, knee, hip)
                ComputeJointAngles();

                //add the COM position to the list of recent COM positions, so that we can compute velocity
                storeMostRecentComPosition((float)(millisecondsSinceLastFreshMarkerData / 1000));

                // EXTRA: we always compute the center-of-pelvis-markers position as a rough estimate of the COM, as a good failsafe.
                computeAndStoreCenterOfPelvisMarkersPosition();

                // We will always compute the center-of-trunk position as the control point for trunk forces
                if (rigidBodiesToReconstructForModel.Contains(trunkSegmentName) == true)
                {
                    computeAndStoreCenterOfTrunkPosition();
                }

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
            }
            else //if the pelvis is unavailable, then the COM cannot be meaningfully computed this frame
            {
                printWarningOrErrorToConsoleIfDebugModeIsDefined(logAWarningSpecifier, "Pelvis not available this frame. COM will be rendered in same position as previous frame.");
            }
        }

        // Return the flags indicating 1.) if the marker data is old (true) or fresh (false) and 2.) could we successfully compute our COM, 
        // as approximated by pelvis availability
        return (isMarkerDataOld, pelvisAvailableThisFrame);
    }


    private void resetVariablesNeededForNextFrame()
    {
        //Clear any lists that are filled dynamically on each update() call
        namesOfAllReconstructedMarkers.Clear();
        positionsOfAllReconstructedMarkers.Clear();

        // Clear any arrays that need to be reset
        Array.Clear(segmentEstimatedComPositions, 0, segmentEstimatedComPositions.Length);
        Array.Clear(markersInModelOcclusionStatus, 0, markersInModelOcclusionStatus.Length);
        Array.Clear(markerInModelWasReconstructedThisFrameFlags, 0, markerInModelWasReconstructedThisFrameFlags.Length);
        Array.Clear(segmentUsedForComEstimationThisFrameFlags, 0, segmentUsedForComEstimationThisFrameFlags.Length);
        Array.Clear(segmentComEstimatedWithSimplifiedModelThisFrameFlags, 0, segmentComEstimatedWithSimplifiedModelThisFrameFlags.Length);
        Array.Clear(virtualMarkersCouldBeCalculatedThisFrameFlag, 0, virtualMarkersCouldBeCalculatedThisFrameFlag.Length);
    }



    //Gets occlusion status and position for all markers needed in our current COM model.
    //Results are stored in instance variables.
    private bool getMarkerDataForAllMarkersNeededInComModel(string[] listOfMarkerNamesInModel)
    {
        float[] copyMarkersInModelXPositions = (float[])markersInModelXPositions.Clone();
        float[] copyMarkersInModelYPositions = (float[])markersInModelYPositions.Clone();

        bool markerDataIsOld = false; //assume marker data is fresh, and update after checking

        //Get the frame number that was most recently accessed
        uint frameNumber = markerDataDistributorScript.getLastRetrievedViconFrameNumber();
        mostRecentlyAccessedViconFrameNumber = frameNumber;

        //Debug.Log("Most recent frame number was " + mostRecentlyAccessedViconFrameNumber);

        for (uint index = 0; index < listOfMarkerNamesInModel.Length; index++) //for each marker in the model
        {
            string markerName = listOfMarkerNamesInModel[index];
            var markerResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(markerName);
            markersInModelOcclusionStatus[index] = markerResultTuple.Item1;
            markersInModelXPositions[index] = markerResultTuple.Item2;
            markersInModelYPositions[index] = markerResultTuple.Item3;
            markersInModelZPositions[index] = markerResultTuple.Item4;
        }

        //if the marker data is the same, then new data is not ready, so don't run the rest of the pipeline
        if (Enumerable.SequenceEqual(copyMarkersInModelXPositions, markersInModelXPositions) &&
            Enumerable.SequenceEqual(copyMarkersInModelYPositions, markersInModelYPositions))
        {
            markerDataIsOld = true;
        }

        //update the most recent ankle (left and right) positions, if those markers are available
        updateMostRecentAnklePositions();

        return markerDataIsOld;
    }



    private void updateMostRecentAnklePositions()
    {
        //try to get the ankle positions as a vector
        (bool leftAnkleMarkerAvailable, Vector3 leftAnklePosition) = getMarkerPositionAsVectorByName(leftAnkleMarkerName);
        (bool rightAnkleMarkerAvailable, Vector3 rightAnklePosition) = getMarkerPositionAsVectorByName(rightAnkleMarkerName);

        //if the ankle marker is available, update the most recent ankle marker position
        if (leftAnkleMarkerAvailable)
        {
            mostRecentLeftAnkleMarkerPosition = leftAnklePosition;
        }

        if (rightAnkleMarkerAvailable)
        {
            mostRecentRightAnkleMarkerPosition = rightAnklePosition;
        }

    }


    // This function reconstructs all missing markers for each rigid body specified in the passed in string array. 
    // The result is stored in an instance variable. 
    private List<string> reconstructMissingMarkersOnListedRigidBodies(string[] rigidBodiesToReconstructForModel)
    {
        List<string> rigidBodiesThatCouldBeFullyReconstructedThisFrame = new List<string>();
        //Debug.Log("Rigid bodies list: " + string.Join(", ", rigidBodiesToReconstructForModel.Select(v => v.ToString())));
        //for each rigid body on the reconstruction list 
        for (int rigidBodyIndex = 0; rigidBodyIndex < rigidBodiesToReconstructForModel.Length; rigidBodyIndex++)
        {
            //get the marker names on this rigid body
            string[] rigidBodyMarkerNames = rigidBodiesToReconstructForModelMarkerNames[rigidBodyIndex];

            //for each marker name, get the occlusion status and store in an array
            //bool[] occlusionStatusOfMarkers = new bool[rigidBodyMarkerNames.Length];
            //for(int markerIndex = 0; markerIndex < rigidBodyMarkerNames.Length; markerIndex++)
            //{
            //   occlusionStatusOfMarkers[markerIndex] = markersInModelOcclusionStatus[Array.IndexOf(markersInModel, rigidBodyMarkerNames[markerIndex])];
            //}

            //pass the corresponding list of segment marker names on to the reconstruction functions
            var reconstructionResultTuple = reconstructMissingMarkersOnOneRigidBodyThisFrame(rigidBodyMarkerNames);

            if (reconstructionResultTuple.Item1 == true) //if reconstruction was successful
            {
                //note that the rigid body is fully available/reconstructed this frame 
                rigidBodiesThatCouldBeFullyReconstructedThisFrame.Add(rigidBodiesToReconstructForModel[rigidBodyIndex]);

                //store the reconstructed markers and their positions
                namesOfAllReconstructedMarkers.AddRange(reconstructionResultTuple.Item2);
                positionsOfAllReconstructedMarkers.AddRange(reconstructionResultTuple.Item3);

                Vector3[] testPositions = reconstructionResultTuple.Item3;
                //Debug.Log("Reconstruction " + string.Join(", ", rigidBodyMarkerNames.Select(v => v.ToString())) + "Positions: " +
                    //testPositions[1].x);
                    //string.Join(", ", testPositions[0].Select(v=>v.ToString())));
            } else //print a warning or error that the segment markers could not be reconstructed
            {
                string errorMessage = "Rigid body named " + rigidBodiesToReconstructForModel[rigidBodyIndex] +
                    " has occluded markers that could not be reconstructed.";
                printWarningOrErrorToConsoleIfDebugModeIsDefined(logAWarningSpecifier, errorMessage);
            }

        }

        printLogMessageToConsoleIfDebugModeIsDefined("Number of reconstructed markers this frame = " +
            namesOfAllReconstructedMarkers.Count);

        //Now that we've finished reconstruction for all rigid bodies, mark which markers had to be reconstructed this frame
        for (int reconstructedMarkerIndex = 0; reconstructedMarkerIndex < namesOfAllReconstructedMarkers.Count; reconstructedMarkerIndex++)
        {
            int markerModelIndex = Array.IndexOf(markersInModel, namesOfAllReconstructedMarkers[reconstructedMarkerIndex]);
            markerInModelWasReconstructedThisFrameFlags[markerModelIndex] = true;

            //print reconstructed marker positions if debugging
            string logMessage = "Frame: " + mostRecentlyAccessedViconFrameNumber + "Marker named " + namesOfAllReconstructedMarkers[reconstructedMarkerIndex] + " has reconstructed position (x,y,z): ( " +
                 positionsOfAllReconstructedMarkers[reconstructedMarkerIndex].x + ", " +
                 positionsOfAllReconstructedMarkers[reconstructedMarkerIndex].y + ", " +
                 positionsOfAllReconstructedMarkers[reconstructedMarkerIndex].z + " ) and actual position: (" +
                 markersInModelXPositions[markerModelIndex] + ", " +
                 markersInModelYPositions[markerModelIndex] + ", " +
                 markersInModelZPositions[markerModelIndex] + " )";

            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
        }

        return rigidBodiesThatCouldBeFullyReconstructedThisFrame;

    }


    private List<string> computeModelSegmentCentersOfMass()
    {
        //keep track of which segments COMs cannot be computed
        List<string> namesOfMissingSegments = new List<string>();
        for (uint segmentIndex = 0; segmentIndex < segmentsInModel.Length; segmentIndex++) //for each segment in the model
        {
            bool segmentCOMComputedSuccessfully = computeSegmentComPosition(segmentIndex, segmentsInModel[segmentIndex], segmentsInModelObjectArray[segmentIndex]);

            //if the segment COM could not be computed due to missing markers or virtual markers, 
            //note this
            if (!segmentCOMComputedSuccessfully)
            {
                namesOfMissingSegments.Add(segmentsInModel[segmentIndex]);
            }
        }

        //if there are any missing segments
        if (namesOfMissingSegments.Count != 0)
        {
            //construct a string with the names of all missing segments
            string missingSegmentsWarning = "In this frame, the COMs of the following " + namesOfMissingSegments.Count +
                " segments could not be computed: ";
            for (int indexOfMissingSegment = 0; indexOfMissingSegment < namesOfMissingSegments.Count; indexOfMissingSegment++)
            {
                //add the name of the missing segment to the warning message
                missingSegmentsWarning = missingSegmentsWarning + namesOfMissingSegments[indexOfMissingSegment] + ", ";
            }
            //print a warning to the console
            printWarningOrErrorToConsoleIfDebugModeIsDefined(logAWarningSpecifier, missingSegmentsWarning);
        }

        return namesOfMissingSegments;
    }



    private bool computeSegmentComPosition(uint segmentIndex, string segmentName, bodySegment segmentObject)
    {

        bool couldComputeSegmentCom; //return value that indicates whether or not the segment COM could be computed

        string proximalMarkerName = segmentObject.getProximalMarkerName();
        string distalMarkerName = segmentObject.getDistalMarkerName();
        float distanceFromProximalMarkerToComAsSegmentLengthFraction = segmentObject.getComLengthAlongSegmentFraction();

        //get the marker positions
        (bool proximalMarkerPositionAvailable, Vector3 proximalMarkerPosition) = getMarkerPositionAsVectorByName(proximalMarkerName);
        (bool distalMarkerPositionAvailable, Vector3 distalMarkerPosition) = getMarkerPositionAsVectorByName(distalMarkerName);

        if (proximalMarkerPositionAvailable && distalMarkerPositionAvailable) //if the segment endpoints are available this frame
        {
            couldComputeSegmentCom = true; //note that we could compute the segment COM

            //compute and store the segment COM
            segmentComPositions[segmentIndex] = proximalMarkerPosition + distanceFromProximalMarkerToComAsSegmentLengthFraction * (distalMarkerPosition - proximalMarkerPosition);
        }
        else //if the segment endpoints are not available this frame
        {
            couldComputeSegmentCom = false; //note that we cannot compute the segment COM
        }

        //DEBUG ONLY 
        //Debug.Log("Segment with name " + segmentName + " has proximal marker " + proximalMarkerName + "at (x, y, z): (" + proximalMarkerPosition.x + ", " + proximalMarkerPosition.y + ", " + proximalMarkerPosition.z + ")");
        //Debug.Log("Segment with name " + segmentName + " has distal marker " + distalMarkerName + "at (x, y, z): (" + distalMarkerPosition.x + ", " + distalMarkerPosition.y + ", " + distalMarkerPosition.z + ")");
        //Debug.Log("Segment with name " + segmentName + " has COM at (x,y,z): (" + segmentComPositions[segmentIndex].x + ", " + segmentComPositions[segmentIndex].y + ", " + segmentComPositions[segmentIndex].z + ")");

        return couldComputeSegmentCom;



    }




    //This function is used to reconstruct the segment COM locations of the shank and thigh, if they are missing. 
    //It does so by drawing a line from the HJC to the ipsilateral ankle, placing the segment COMs on this line 
    //at a distance determined by the relative lengths of the thigh and shank, as well as the COM locations 
    //reported by deLeva. 
    private List<string> estimateThighAndShankComPositionsIfNeededLineFromHjcToAnkle(List<string> namesOfMissingSegments)
    {
        List<string> segmentsWithEstimatedComsThisFrame = new List<string>();
        if (namesOfMissingSegments.Count > 0) //if at least one segment requires an estimated COM 
        {
            for (int missingSegmentIndex = 0; missingSegmentIndex < namesOfMissingSegments.Count; missingSegmentIndex++)
            {
                //get an estimate of the COM center, if possible
                string segmentName = namesOfMissingSegments[missingSegmentIndex];
                (bool segmentComEstimatedSuccessfullyFlag, Vector3 segmentComPositionInViconCoords) = estimateSpecificMissingSegmentCom(segmentName);

                //store the estimated COM position in a dedicated array, in the index corresponding to the specific model segment
                if (segmentComEstimatedSuccessfullyFlag)
                {
                    segmentsWithEstimatedComsThisFrame.Add(segmentName);
                    segmentEstimatedComPositions[Array.IndexOf(segmentsInModel, segmentName)] = segmentComPositionInViconCoords;
                }

            }
        }

        return segmentsWithEstimatedComsThisFrame;

    }


    //This function estimates a segment's COM, if it's not more directly available from marker data (e.g. segment
    //start or end point markers are not avaiable).
    //This function assumes that the hip joint centers are available, which requires that the pelvic segment was reconstructed. 
    //Only call this function if that is the case.
    private (bool, Vector3) estimateSpecificMissingSegmentCom(string segmentName)
    {
        Vector3 positionOfSegmentComInViconCoordinates = new Vector3(0.0f, 0.0f, 0.0f);
        bool estimateOfComSuccessful = false;
        if (segmentName == rightThighSegmentName)
        {
            estimateOfComSuccessful = true;
            bodySegment rightThigh = segmentsInModelObjectArray[Array.IndexOf(segmentsInModel, segmentName)];
            Vector3 rightHjcPosition = hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfRightHjcVirtualMarker)];
            Vector3 unitVectorHjcToAnkle = (mostRecentRightAnkleMarkerPosition - rightHjcPosition).normalized;
            Vector3 offsetVectorHjcToSegmentCom = rightThighLength *
                rightThigh.getComLengthAlongSegmentFraction() * unitVectorHjcToAnkle;
            positionOfSegmentComInViconCoordinates = rightHjcPosition + offsetVectorHjcToSegmentCom;

            /*Debug.Log("Reconstructed right thigh COM pos as (x,y,z): ( " + positionOfSegmentComInViconCoordinates.x + ", "
                + positionOfSegmentComInViconCoordinates.y + ", "
                + positionOfSegmentComInViconCoordinates.z + " )");
            Debug.Log("Reconstructing right thigh, COM length along segment as a fraction is: " + rightThigh.getComLengthAlongSegmentFraction());
            Debug.Log("Reconstructing right thigh, segment length is: " + rightThighLength);
            Debug.Log("Reconstructing right thigh, offset vector HJC to segment COM is (x,y,z): ( " + offsetVectorHjcToSegmentCom.x + ", "
                + offsetVectorHjcToSegmentCom.y + ", "
                + offsetVectorHjcToSegmentCom.z + " )");
            Debug.Log("Reconstructing right thigh, most recent right HJC pos (x,y,z): ( " + rightHjcPosition.x + ", "
                + rightHjcPosition.y + ", "
                + rightHjcPosition.z + " )");*/

        }
        else if (segmentName == rightShankSegmentName)
        {
            estimateOfComSuccessful = true;
            bodySegment rightShank = segmentsInModelObjectArray[Array.IndexOf(segmentsInModel, segmentName)];
            Vector3 rightHjcPosition = hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfRightHjcVirtualMarker)];
            Vector3 unitVectorHjcToAnkle = (mostRecentRightAnkleMarkerPosition - rightHjcPosition).normalized;
            Vector3 offsetVectorHjcToSegmentCom = (rightThighLength +
                rightShankLength * rightShank.getComLengthAlongSegmentFraction()) * unitVectorHjcToAnkle;
            positionOfSegmentComInViconCoordinates = rightHjcPosition + offsetVectorHjcToSegmentCom;

            /*Debug.Log("Reconstructed right shank COM pos as (x,y,z): ( " + positionOfSegmentComInViconCoordinates.x + ", "
                + positionOfSegmentComInViconCoordinates.y + ", "
                + positionOfSegmentComInViconCoordinates.z + " )");*/
        } else if (segmentName == leftThighSegmentName)
        {
            estimateOfComSuccessful = true;
            bodySegment leftThigh = segmentsInModelObjectArray[Array.IndexOf(segmentsInModel, segmentName)];
            Vector3 leftHjcPosition = hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfLeftHjcVirtualMarker)];
            Vector3 unitVectorHjcToAnkle = (mostRecentLeftAnkleMarkerPosition - leftHjcPosition).normalized;
            Vector3 offsetVectorHjcToSegmentCom = leftThighLength *
                leftThigh.getComLengthAlongSegmentFraction() * unitVectorHjcToAnkle;
            positionOfSegmentComInViconCoordinates = leftHjcPosition + offsetVectorHjcToSegmentCom;
        }
        else if (segmentName == leftShankSegmentName)
        {
            estimateOfComSuccessful = true;
            bodySegment leftShank = segmentsInModelObjectArray[Array.IndexOf(segmentsInModel, segmentName)];
            Vector3 leftHjcPosition = hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfLeftHjcVirtualMarker)];
            Vector3 unitVectorHjcToAnkle = (mostRecentLeftAnkleMarkerPosition - leftHjcPosition).normalized;
            Vector3 offsetVectorHjcToSegmentCom = (leftThighLength +
                leftShankLength * leftShank.getComLengthAlongSegmentFraction()) * unitVectorHjcToAnkle;
            positionOfSegmentComInViconCoordinates = leftHjcPosition + offsetVectorHjcToSegmentCom;
        }
        else //if we can't estimate the segment's COM
        {
            printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, "Segment named " + segmentName + " is missing and it's COM location could not be reconstructed.");
        }

        return (estimateOfComSuccessful, positionOfSegmentComInViconCoordinates);
    }




    private void computeTotalBodyCenterOfMassFromSegments(List<string> segmentsWithoutCom, List<string> segmentsWithEstimatedCom)
    {
        Vector3 totalBodyComPosition = new Vector3(0.0f, 0.0f, 0.0f); //initialize the total body COM as a Vector3 filled with zeros

        for (uint segmentIndex = 0; segmentIndex < segmentsInModel.Length; segmentIndex++) //for each segment in the model
        {
            //get the segment name 
            string segmentName = segmentsInModel[segmentIndex];

            // Track whether or not the segment COM had to be estimated with a simplified model this frame
            bool segmentComEstimatedWithSimplifiedModelThisFrame = false;

            //get the segment COM position 
            Vector3 segmentComPosition;
            bool thisSegmentIndexUsedForComEstimationThisFrame = false;
            if (segmentsWithoutCom.Contains(segmentName)) //if the segment COM could not be calculated from markers
            {
                if (segmentsWithEstimatedCom.Contains(segmentName)) //if there is an estimate of the COM available
                {
                    //use the segment COM position estimate
                    segmentComPosition = segmentEstimatedComPositions[segmentIndex];

                    // Note that we can use the segment for the whole-body COM estimation
                    thisSegmentIndexUsedForComEstimationThisFrame = true;

                    // Note that the segment COM had to be estimated with a simplifed model this frame
                    segmentComEstimatedWithSimplifiedModelThisFrame = true;
                }
                else //if the segment COM cannot be approximated
                {
                    // Note that we cannot (!) use the segment for the whole-body COM estimation
                    thisSegmentIndexUsedForComEstimationThisFrame = false;

                    printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, "Missing segment named " + segmentName + " is being excluded from the COM estimate this frame.");
                    break;
                }
            }
            else //if the segment COM was calculated directly from markers
            {
                //use the segment COM as computed by our current model
                segmentComPosition = segmentComPositions[segmentIndex];

                // Note that we can use the segment for the whole-body COM estimation
                thisSegmentIndexUsedForComEstimationThisFrame = true;
            }

            //add the weighted segment mass position to the total body mass position
            totalBodyComPosition = totalBodyComPosition + segmentsInModelObjectArray[segmentIndex].getFractionOfTotalBodyMass() * segmentComPositions[segmentIndex];

            // Store the flag indicating whether or not the segment was used for COM estimation this frame
            segmentUsedForComEstimationThisFrameFlags[segmentIndex] = thisSegmentIndexUsedForComEstimationThisFrame;

            // Store the flag indicating if the segment COM had to be estimated with a simplified model this frame
            segmentComEstimatedWithSimplifiedModelThisFrameFlags[segmentIndex] = segmentComEstimatedWithSimplifiedModelThisFrame;
        }

        //store the computed total body center of mass
        //Debug.Log("Computed COM this frame is located at (x,y,z): ( " + totalBodyComPosition.x + ", " + totalBodyComPosition.y + ", " + totalBodyComPosition.z + " )");
        mostRecentTotalBodyComPosition = totalBodyComPosition;

        // Print: 
        //Debug.Log("COM Position X: " + mostRecentTotalBodyComPosition.x);
        //Debug.Log("COM Position Y: " + mostRecentTotalBodyComPosition.y);
    }



    // START: Joint angle computation functions***********************************************************************************************

    // The top-level function called by Update() when computing joint angles
    private void ComputeJointAngles()
    {
        // Construct transformations from the Vicon global frame to the foot, shank, thigh segment frames
        (bool[] transformationsRetrievedFlags, Matrix4x4[] transformationsViconFrameToSegmentFrameKeySegments)
            = getTransformationsFromViconGlobalFrameToSegmentFrames(rigidBodyNamesForJointAngleComputation);
        
        // BELOW TO DO: we don't check if the transformations were successfully retrieved. If not, we just leave the joint angle
        // as the last computed, and store the flags indicating that the joint angle computation was a failure.

        // Contruct the transformations from the pelvis segment to each thigh
        Matrix4x4 transformationFromPelvisToRightThigh =
            GetTransformationFromSegmentToSegment(transformationsViconFrameToSegmentFrameKeySegments, pelvisSegmentName, rightThighHipJointSegmentName);
        Matrix4x4 transformationFromPelvisToLeftThigh =
                        GetTransformationFromSegmentToSegment(transformationsViconFrameToSegmentFrameKeySegments, pelvisSegmentName, leftThighHipJointSegmentName);

        // Construct the transformations from the thigh segments to the shank segments
        Matrix4x4 transformationFromRightThighToRightShank =
            GetTransformationFromSegmentToSegment(transformationsViconFrameToSegmentFrameKeySegments, rightThighKneeJointSegmentName, rightShankKneeJointSegmentName);
        Matrix4x4 transformationFromLeftThighToLeftShank =
            GetTransformationFromSegmentToSegment(transformationsViconFrameToSegmentFrameKeySegments, leftThighKneeJointSegmentName, leftShankKneeJointSegmentName);

        // Construct the transformation from the shank segment to the foot segment
        Matrix4x4 transformationFromRightShankToRightFoot = 
            GetTransformationFromSegmentToSegment(transformationsViconFrameToSegmentFrameKeySegments, rightShankAnkleJointSegmentName, rightFootSegmentName);
        Matrix4x4 transformationFromLeftShankToLeftFoot = 
            GetTransformationFromSegmentToSegment(transformationsViconFrameToSegmentFrameKeySegments, leftShankAnkleJointSegmentName, leftFootSegmentName);

        // Extract the hip angle data from the pelvis-to-thigh transformations
        (rightHipFlexionAngle, rightHipAbductionAngle, rightHipInternalRotationAngle) =
            getEulerAnglesHipJoint(transformationFromPelvisToRightThigh);
        (leftHipFlexionAngle, leftHipAbductionAngle, leftHipInternalRotationAngle) =
            getEulerAnglesHipJoint(transformationFromPelvisToLeftThigh);

        // Extract the knee angle data from the thigh to shank transformation.
        (rightKneeFlexionAngle, rightKneeAbductionAngle, rightKneeInternalRotationAngle) =
            getEulerAnglesKneeJoint(transformationFromRightThighToRightShank);
        (leftKneeFlexionAngle, leftKneeAbductionAngle, leftKneeInternalRotationAngle) =
            getEulerAnglesKneeJoint(transformationFromLeftThighToLeftShank);

        // Extract the ankle angle data from the shank to foot transformation.
        // Euler angles seem to be the standard (from ME281).
        // Euler angles are computed.
        // Also, consider writing your own function to verify these results!!!! 
        (rightAnkleFlexionAngle, rightAnkleInversionAngle, rightAnkleInternalRotationAngle) =
            getEulerAnglesAnkleJoint(transformationFromRightShankToRightFoot);
        (leftAnkleFlexionAngle, leftAnkleInversionAngle, leftAnkleInternalRotationAngle) =
            getEulerAnglesAnkleJoint(transformationFromLeftShankToLeftFoot);

        // Sort the ankle angle data into flexion/extension, inversion/eversion, and medial/lateral rotation

        // Compute our own value for the right knee angle


        // DEBUGGING: print the joint angles
        //Debug.Log("Right ankle flexion:" + rightAnkleFlexionAngle * 180/(3.14) + '\n' 
        //  + " and right ankle adduction: " + rightAnkleInversionAngle * 180 / (3.14)
        //   + " and right ankle rotation: " + rightAnkleInternalRotationAngle * 180/3.14);
        /*Debug.Log("Ankle abc:" + Math.Round(rightAnkleFlexionAngle * 180 / (3.14), 2) + " b=" + Math.Round(rightAnkleInversionAngle * 180 / (3.14), 2)
            + " c=" + Math.Round(rightAnkleInternalRotationAngle * 180 / 3.14, 2) + "\n" +
            "Knee abc:" + Math.Round(rightKneeFlexionAngle * 180 / 3.14, 2) + " b=" + Math.Round(rightKneeAbductionAngle * 180 / 3.14, 2) +
            " c=" + Math.Round(rightKneeInternalRotationAngle * 180 / 3.14, 2) +
            "Hip abc:" + Math.Round(rightHipFlexionAngle * 180 / 3.14, 2) + " b=" + Math.Round(rightHipAbductionAngle * 180 / 3.14, 2) +
            " c=" + Math.Round(rightHipInternalRotationAngle * 180 / 3.14, 2));*/
        
        // Set the flag indicating that some valid ankle angle data has been generated thus far to true
        //hasScriptGeneratedAnkleAngleDataYetFlag = true;
    }

    // For each segment (should be seven: right and left shank, right and left foot, right and left thigh, pelvis), constructs a transformation from
    // the global Vicon frame to that segment's coordinate system. Each subfunction roughly follows Collin et al 2009,
    // with a minor adjustment to guarantee orthogonal axes (from ME281 lecture notes).
    private (bool[], Matrix4x4[]) getTransformationsFromViconGlobalFrameToSegmentFrames(string[] namesOfSegments)
    {
        //Instantiate return list, which will hold all of the transformations from Vicon frame to segment frame
        Matrix4x4[] transformationsFromViconToSegmentFramesArray = new Matrix4x4[namesOfSegments.Length];
        
        // Instantiate the success flag list, indicating if we successfully got the transform for each segment
        bool[] transformComputedFlags = new bool[namesOfSegments.Length];


        for (int segmentIndex = 0; segmentIndex < namesOfSegments.Length; segmentIndex++) //for each segment
        {
            string nameOfCurrentSegment = namesOfSegments[segmentIndex];
            Matrix4x4 transformationViconFrameToSegmentFrame = new Matrix4x4();
            // Instantiate the success flag, indicating if we successfully got the transform
            bool transformComputedFlag = false;

            if (nameOfCurrentSegment == pelvisSegmentName) //if we are getting the pelvis transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame) = 
                    constructTransformationMatrixFromViconFrameToPelvisLocalFrame();
            }
            else if (nameOfCurrentSegment == rightThighKneeJointSegmentName || nameOfCurrentSegment == rightThighHipJointSegmentName) 
                //if we are getting the right thigh transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame) =
                    constructTransformationMatrixFromViconFrameToRightThighLocalFrame(nameOfCurrentSegment);
            }
            else if (nameOfCurrentSegment == leftThighKneeJointSegmentName || nameOfCurrentSegment == leftThighHipJointSegmentName) 
                //if we are getting the left thigh transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame)
                    = constructTransformationMatrixFromViconFrameToLeftThighLocalFrame(nameOfCurrentSegment);
            }
            else if (nameOfCurrentSegment == rightShankKneeJointSegmentName || nameOfCurrentSegment == rightShankAnkleJointSegmentName) 
                //if we are getting the right shank transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame)
                    = constructTransformationMatrixFromViconFrameToRightShankLocalFrame(nameOfCurrentSegment);
            }
            else if (nameOfCurrentSegment == leftShankKneeJointSegmentName || nameOfCurrentSegment == leftShankAnkleJointSegmentName) //if we are getting the left shank transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame)
                    = constructTransformationMatrixFromViconFrameToLeftShankLocalFrame(nameOfCurrentSegment);
            }
            else if (nameOfCurrentSegment == rightFootSegmentName) //if we are getting the right foot transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame)
                    = constructTransformationMatrixFromViconFrameToRightFootLocalFrame();
            }
            else if (nameOfCurrentSegment == leftFootSegmentName) //if we are getting the left foot transform
            {
                (transformComputedFlag, transformationViconFrameToSegmentFrame)
                    = constructTransformationMatrixFromViconFrameToLeftFootLocalFrame();
            }

            //store the transformation matrix
            transformationsFromViconToSegmentFramesArray[segmentIndex] = transformationViconFrameToSegmentFrame;
            // Store the success flag
            transformComputedFlags[segmentIndex] = transformComputedFlag;
        }

        return (transformComputedFlags, transformationsFromViconToSegmentFramesArray);
    }


    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToPelvisLocalFrame()  // no orthogonality check ?????
    {
        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool leftPsisAvailableFlag, Vector3 l_psis) = getMarkerPositionAsVectorByName(backLeftPelvisMarkerName);
        (bool rightPsisAvailableFlag, Vector3 r_psis) = getMarkerPositionAsVectorByName(backRightPelvisMarkerName);
        (bool leftAsisAvailableFlag, Vector3 l_asis) = getMarkerPositionAsVectorByName(frontLeftPelvisMarkerName);
        (bool rightAsisAvailableFlag, Vector3 r_asis) = getMarkerPositionAsVectorByName(frontRightPelvisMarkerName);

        if(leftPsisAvailableFlag && rightPsisAvailableFlag && leftAsisAvailableFlag && rightAsisAvailableFlag)
        {
            Vector3 pelvisCenter = (l_psis + r_psis) / 2;

            // Define the coordinate system origin and axes
            Vector3 positionOfLocalFrameOriginInViconCoordinates = pelvisCenter;
            Vector3 localFrameZAxis = r_asis - l_asis; //positive posteriorly
                                                       // Vector3 localFrameXDash = leftKneeMedial - leftKneeLateral
            Vector3 localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, ((-pelvisCenter) + l_asis)); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
            Vector3 localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, localFrameZAxis); //positive to subject's left

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }


    // Function to construct right shank local frame.
    // Note, the y-axis is aligned with the two malleolus markers !!!!!!!!!!!!!!!!!!! IMPORTANT !!!!!!!!!!!!!!!!!!!!!!!!
    // (instead of the two knee markers) to adjust for tibial torsion,
    // since we're concerned with ankle angle and not knee angle. 
    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToRightShankLocalFrame(string jointOfInterest)
    {
        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool rightKneeMedialAvailableFlag, Vector3 rightKneeMedial) = getMarkerPositionAsVectorByName(rightKneeMedialMarkerName);
        (bool rightKneeLateralAvailableFlag, Vector3 rightKneeLateral) = getMarkerPositionAsVectorByName(rightKneeMarkerName);
        (bool rightAnkleMedialAvailableFlag, Vector3 rightAnkleMedial) = getMarkerPositionAsVectorByName(rightAnkleMedialMarkerName);
        (bool rightAnkleLateralAvailableFlag, Vector3 rightAnkleLateral) = getMarkerPositionAsVectorByName(rightAnkleMarkerName);
        if (rightKneeMedialAvailableFlag && rightKneeLateralAvailableFlag && rightAnkleMedialAvailableFlag && rightAnkleLateralAvailableFlag)
        {
            Vector3 rightKneeJointCenter = (rightKneeMedial + rightKneeLateral) / 2;
            Vector3 rightAnkleJointCenter = (rightAnkleMedial + rightAnkleLateral) / 2;

            // Define the coordinate system origin and axes
            // Note that since we're concerned with the ankle angle, the
            // Y-axis will be aligned with the axis between the two malleolus markers to
            // account for tibial torsion (torsion of the rigid body itself) along the length of the shank.
            Vector3 localFrameXAxis = new Vector3();
            Vector3 localFrameYAxis = new Vector3();
            Vector3 localFrameZAxis = new Vector3();
            Vector3 positionOfLocalFrameOriginInViconCoordinates = new Vector3();
            if (jointOfInterest == rightShankAnkleJointSegmentName)
            {
                positionOfLocalFrameOriginInViconCoordinates = rightKneeJointCenter;
                localFrameZAxis = (rightAnkleLateral - rightAnkleMedial); //positive right
                localFrameXAxis = getRightHandedCrossProduct((rightKneeJointCenter - rightAnkleJointCenter), localFrameZAxis); //positive anteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
                localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXAxis); //positive upwards (pointing from ankle to knee) 
            }
            else if (jointOfInterest == rightShankKneeJointSegmentName)
            {
                positionOfLocalFrameOriginInViconCoordinates = rightKneeJointCenter;
                localFrameZAxis = (rightKneeJointCenter - rightAnkleJointCenter); //positive right
                localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, (rightKneeLateral - rightKneeMedial)); //positive upwards (pointing from ankle to knee)
                localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, localFrameZAxis); //positive anteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
            }

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }



    // Function to construct left shank local frame.
    // Note, the y-axis is aligned with the two malleolus markers
    // (instead of the two knee markers) to adjust for tibial torsion,
    // since we're concerned with ankle angle and not knee angle. 
    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToLeftShankLocalFrame(string jointOfInterest)
    {

        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool leftKneeMedialAvailableFlag, Vector3 leftKneeMedial) = getMarkerPositionAsVectorByName(leftKneeMedialMarkerName);
        (bool leftKneeLateralAvailableFlag, Vector3 leftKneeLateral) = getMarkerPositionAsVectorByName(leftKneeMarkerName);
        (bool leftAnkleMedialAvailableFlag, Vector3 leftAnkleMedial) = getMarkerPositionAsVectorByName(leftAnkleMedialMarkerName);
        (bool leftAnkleLateralAvailableFlag, Vector3 leftAnkleLateral) = getMarkerPositionAsVectorByName(leftAnkleMarkerName);

        if (leftKneeMedialAvailableFlag && leftKneeLateralAvailableFlag && leftAnkleMedialAvailableFlag && leftAnkleLateralAvailableFlag)
        {
            Vector3 leftKneeJointCenter = (leftKneeMedial + leftKneeLateral) / 2;
            Vector3 leftAnkleJointCenter = (leftAnkleMedial + leftAnkleLateral) / 2;

            // Define the coordinate system origin and axes
            // Note that since we're concerned with the ankle angle, the
            // Y-axis will be aligned with the axis between the two malleolus markers to
            // account for tibial torsion (torsion of the rigid body itself) along the length of the shank.
            Vector3 localFrameXAxis = new Vector3();
            Vector3 localFrameYAxis = new Vector3();
            Vector3 localFrameZAxis = new Vector3();
            Vector3 positionOfLocalFrameOriginInViconCoordinates = new Vector3();
            if (jointOfInterest == leftShankAnkleJointSegmentName)
            {
                positionOfLocalFrameOriginInViconCoordinates = leftKneeJointCenter;
                localFrameZAxis = (leftAnkleMedial - leftAnkleLateral); //positive rightwards
                localFrameXAxis = getRightHandedCrossProduct(leftKneeJointCenter - leftAnkleJointCenter, localFrameZAxis); //positive anteriorly. should we use (rightKneeJointCenter - rightAnkleLateral) instead of crossing with z-axis? Collins et al 2009 vs. ME281...
                localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXAxis); //positive to subject's left
            }
            else if (jointOfInterest == leftShankKneeJointSegmentName)
            {
                positionOfLocalFrameOriginInViconCoordinates = leftKneeJointCenter;
                localFrameZAxis = (leftKneeJointCenter - leftAnkleJointCenter); //positive right
                localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, (leftKneeMedial - leftKneeLateral)); //positive upwards (pointing from ankle to knee)
                localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, localFrameZAxis); //positive anteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
            }

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }



    //Function to construct right thigh local frame
    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToRightThighLocalFrame(string jointOfInterest)
    {
        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool rightKneeMedialAvailableFlag, Vector3 rightKneeMedial) = getMarkerPositionAsVectorByName(rightKneeMedialMarkerName);
        (bool rightKneeLateralAvailableFlag, Vector3 rightKneeLateral) = getMarkerPositionAsVectorByName(rightKneeMarkerName);
        // (_, Vector3 rightMT1) = getMarkerPositionAsVectorByName(rightFirstMetatarsalMarkerName); // dimension of the matrix ??????????
        // (_, Vector3 rightMT5) = getMarkerPositionAsVectorByName(rightFifthMetatarsalMarkerName);

        // Determine if the hip joint centers could be computed this frame
        bool hipJointCentersAvailableThisFrame = 
            virtualMarkersCouldBeCalculatedThisFrameFlag[Array.IndexOf(virtualMarkerNames, recontructHipJointCentersString)];

        if(rightKneeMedialAvailableFlag && rightKneeLateralAvailableFlag && hipJointCentersAvailableThisFrame) // if the key markers are available
        {
            // Get the right hip joint center position
            Vector3 hipJointCenterRight = hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfRightHjcVirtualMarker)];

            // Try to reconstruct the knee joint center. 
            // If markers are occluded, we note that we can't compute the knee angle
            Vector3 rightKneeJointCenter = (rightKneeMedial + rightKneeLateral) / 2.0f;
            Vector3 localFrameXAxis = new Vector3();
            Vector3 localFrameYAxis = new Vector3();
            Vector3 localFrameZAxis = new Vector3();
            Vector3 positionOfLocalFrameOriginInViconCoordinates = new Vector3();
            if (jointOfInterest == rightThighKneeJointSegmentName)
            {
                // Define the coordinate system origin and axes
                positionOfLocalFrameOriginInViconCoordinates = rightKneeJointCenter;
                localFrameZAxis = -rightKneeJointCenter + hipJointCenterRight; //positive posteriorly
                Vector3 localFrameXDash = rightKneeLateral - rightKneeMedial;
                localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXDash); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
                localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, localFrameZAxis); //positive to subject's left
            }
            else if (jointOfInterest == rightThighHipJointSegmentName)
            {
                // Define the coordinate system origin and axes
                positionOfLocalFrameOriginInViconCoordinates = rightKneeJointCenter;
                localFrameYAxis = -rightKneeJointCenter + hipJointCenterRight; //positive posteriorly
                localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, rightKneeLateral - rightKneeMedial);
                localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, localFrameYAxis); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
            }

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            // Compute the transform from Vicon to segment local frame
            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }

    //Function to construct left thigh local frame
    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToLeftThighLocalFrame(string jointOfInterest)
    {
        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool leftKneeMedialAvailableFlag, Vector3 leftKneeMedial) = getMarkerPositionAsVectorByName(leftKneeMedialMarkerName);
        (bool leftKneeLateralAvailableFlag, Vector3 leftKneeLateral) = getMarkerPositionAsVectorByName(leftKneeMarkerName);
        // (_, Vector3 rightMT1) = getMarkerPositionAsVectorByName(rightFirstMetatarsalMarkerName); // dimension of the matrix ??????????
        // (_, Vector3 rightMT5) = getMarkerPositionAsVectorByName(rightFifthMetatarsalMarkerName);

        // Determine if the hip joint centers could be computed this frame
        bool hipJointCentersAvailableThisFrame =
            virtualMarkersCouldBeCalculatedThisFrameFlag[Array.IndexOf(virtualMarkerNames, recontructHipJointCentersString)];

        if (leftKneeMedialAvailableFlag && leftKneeLateralAvailableFlag && hipJointCentersAvailableThisFrame) // if the key markers are available
        {
            // Get the right hip joint center position
            Vector3 hipJointCenterLeft = hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfLeftHjcVirtualMarker)];

            // Try to reconstruct the knee joint center. 
            // If markers are occluded, we note that we can't compute the knee angle
            Vector3 leftKneeJointCenter = (leftKneeMedial + leftKneeLateral) / 2.0f;

            Vector3 localFrameXAxis = new Vector3();
            Vector3 localFrameYAxis = new Vector3();
            Vector3 localFrameZAxis = new Vector3();
            Vector3 positionOfLocalFrameOriginInViconCoordinates = new Vector3();
            if (jointOfInterest == leftThighKneeJointSegmentName)
            {
                // Define the coordinate system origin and axes
                positionOfLocalFrameOriginInViconCoordinates = leftKneeJointCenter;
                localFrameZAxis = -leftKneeJointCenter + hipJointCenterLeft; //positive posteriorly
                Vector3 localFrameXDash = leftKneeMedial - leftKneeLateral;
                localFrameYAxis = getRightHandedCrossProduct(localFrameZAxis, localFrameXDash); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
                localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, localFrameZAxis); //positive to subject's left
            }
            else if (jointOfInterest == leftThighHipJointSegmentName)
            {
                // Define the coordinate system origin and axes
                positionOfLocalFrameOriginInViconCoordinates = leftKneeJointCenter;
                localFrameYAxis = -leftKneeJointCenter + hipJointCenterLeft; //positive posteriorly
                localFrameXAxis = getRightHandedCrossProduct(localFrameYAxis, leftKneeMedial - leftKneeLateral);
                localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, localFrameYAxis); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
            }

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }


    //Function to construct right foot local frame
    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToRightFootLocalFrame()
    {
        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool rightAnkleMedialAvailableFlag, Vector3 rightAnkleMedial) = getMarkerPositionAsVectorByName(rightAnkleMedialMarkerName);
        (bool rightAnkleLateralAvailableFlag, Vector3 rightAnkleLateral) = getMarkerPositionAsVectorByName(rightAnkleMarkerName);
        (bool rightMt1AvailableFlag, Vector3 rightMT1) = getMarkerPositionAsVectorByName(rightSecondMetatarsalMarkerName); // actually first MT!
        (bool rightMt5AvailableFlag, Vector3 rightMT5) = getMarkerPositionAsVectorByName(rightFifthMetatarsalMarkerName);

        if(rightAnkleMedialAvailableFlag && rightAnkleLateralAvailableFlag && rightMt1AvailableFlag && rightMt5AvailableFlag)
        {
            Vector3 rightAnkleJointCenter = (rightAnkleMedial + rightAnkleLateral) / 2;
            Vector3 rightMidForeFoot = (rightMT1 + rightMT5) / 2;

            // Define the coordinate system origin and axes
            Vector3 positionOfLocalFrameOriginInViconCoordinates = rightAnkleJointCenter;
            Vector3 localFrameXAxis = rightMidForeFoot - rightAnkleJointCenter; //positive anteriorly
            Vector3 localFrameYAxis = getRightHandedCrossProduct(localFrameXAxis, (rightMT1 - rightMT5)); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
            Vector3 localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, localFrameYAxis); //positive to subject's right

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }



    //Function to construct left foot local frame
    private (bool, Matrix4x4) constructTransformationMatrixFromViconFrameToLeftFootLocalFrame()
    {
        // Success flag
        bool transformationComputedFlag = true; // assume success at the start 

        // Initialize the return tranform
        Matrix4x4 transformationMatrixViconToLocal = new Matrix4x4();
        Matrix4x4 transformationMatrixLocalToVicon = new Matrix4x4();

        //Get positions for all the markers we will use to construct the local frame
        (bool leftAnkleMedialAvailableFlag, Vector3 leftAnkleMedial) = getMarkerPositionAsVectorByName(leftAnkleMedialMarkerName);
        (bool leftAnkleLateralAvailableFlag, Vector3 leftAnkleLateral) = getMarkerPositionAsVectorByName(leftAnkleMarkerName);
        (bool leftMt1AvailableFlag, Vector3 leftMT1) = getMarkerPositionAsVectorByName(leftSecondMetatarsalMarkerName); // actually first MT!
        (bool leftMt5AvailableFlag, Vector3 leftMT5) = getMarkerPositionAsVectorByName(leftFifthMetatarsalMarkerName);

        if (leftAnkleMedialAvailableFlag && leftAnkleLateralAvailableFlag && leftMt1AvailableFlag && leftMt5AvailableFlag)
        {
            Vector3 leftAnkleJointCenter = (leftAnkleMedial + leftAnkleLateral) / 2;
            Vector3 leftMidForeFoot = (leftMT1 + leftMT5) / 2;

            // Define the coordinate system origin and axes
            Vector3 positionOfLocalFrameOriginInViconCoordinates = leftAnkleJointCenter;
            Vector3 localFrameXAxis = leftMidForeFoot - leftAnkleJointCenter; //positive posteriorly
            Vector3 localFrameYAxis = getRightHandedCrossProduct(localFrameXAxis, (leftMT5 - leftMT1)); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
            Vector3 localFrameZAxis = getRightHandedCrossProduct(localFrameXAxis, localFrameYAxis); //positive to subject's left

            //normalize the axes
            localFrameXAxis.Normalize();
            localFrameYAxis.Normalize();
            localFrameZAxis.Normalize();

            //get rotation and translation from local frame to global Vicon frame
            transformationMatrixLocalToVicon = getTransformationMatrix(localFrameXAxis, localFrameYAxis, localFrameZAxis, positionOfLocalFrameOriginInViconCoordinates);

            transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;
        }
        else // if we're missing the key markers
        {
            transformationComputedFlag = false; // We can't compute the transformation
        }
        // Return the success flag and transform
        return (transformationComputedFlag, transformationMatrixLocalToVicon);
    }


    // A general function for getting the transform from the "from" segment to the "to" segment coordinate frame
    private Matrix4x4 GetTransformationFromSegmentToSegment(Matrix4x4[] allSegmentTransformsViconToSegment,
    string fromSegmentName, string toSegmentName)
    {
        // Get transforms for the segments
        Matrix4x4 ViconToFromSegmentTransformation =
            allSegmentTransformsViconToSegment[Array.IndexOf(rigidBodyNamesForJointAngleComputation, fromSegmentName)];
        Matrix4x4 ViconToToSegmentTransformation =
            allSegmentTransformsViconToSegment[Array.IndexOf(rigidBodyNamesForJointAngleComputation, toSegmentName)];

        Matrix4x4 rotationMatrix = ViconToFromSegmentTransformation;
        rotationMatrix.m03 = rotationMatrix.m13 = rotationMatrix.m23 = 0f;
        rotationMatrix.m30 = rotationMatrix.m31 = rotationMatrix.m32 = 0f;
        rotationMatrix.m33 = 1f;

        Matrix4x4 inverseRotationMatrix = rotationMatrix.inverse;

        Vector4 lastColumn = new Vector4(-ViconToFromSegmentTransformation.m03, -ViconToFromSegmentTransformation.m13, -ViconToFromSegmentTransformation.m23, 1f);

        // Get the transform from the "from" segment to the "to" segment, = (Vicon global to "to" segment) * ("From" segment to Vicon global)

        Vector4 transformedFinalColumn = inverseRotationMatrix * lastColumn;

        inverseRotationMatrix.m03 = lastColumn[0];
        inverseRotationMatrix.m13 = lastColumn[1];
        inverseRotationMatrix.m23 = lastColumn[2];

        Matrix4x4 fromToToSegmentTransform = inverseRotationMatrix * ViconToToSegmentTransformation;


        // Return the transform 
        return fromToToSegmentTransform;
    }




    // knee body3 123; hip body3 312; ankle body3 312;
    // takes in matrices (transformation matrices)

    static (float, float, float) getEulerAnglesKneeJoint(Matrix4x4 matrix) //knee joint angle calculation 
    { // euler angle for Body3 123
        float alpha = Mathf.Atan2(-matrix.m12, matrix.m22); // Alpha = flexion/extension. Alpha, beta, gamma referenced through WikiPedia

        float beta = Mathf.Asin(matrix.m02); // Beta ~ abduction/adduction. https://en.wikipedia.org/wiki/Euler_angles

        float gamma = Mathf.Atan2(-matrix.m01, matrix.m00); // Gamma ~ internal/external rotation.

        return (alpha, beta, gamma); // returns 1x3 vector

    }

    static (float, float, float) getEulerAnglesHipJoint(Matrix4x4 matrix) //hip joint angle calculation 
    { //euler angle for Body3 312
        float alpha = Mathf.Atan2(-matrix.m01, matrix.m11); // Alpha = flexion/extension. alpha, beta, gamma referenced through WikiPedia
        float beta = Mathf.Asin(matrix.m21); // Beta ~ abduction/adduction. https://en.wikipedia.org/wiki/Euler_angles
        float gamma = Mathf.Atan2(-matrix.m20, matrix.m22); // Gamma ~ internal/external rotation.

        return (alpha, beta, gamma); // returns 1x3 vector

    }

    static (float, float, float) getEulerAnglesAnkleJoint(Matrix4x4 matrix) //ankle joint angle calculation
    { // euler angle for Body3 312
        float alpha = Mathf.Atan(-matrix.m01 / matrix.m11); // Alpha = dorsiflexion/plantarflexion. Alpha, beta, gamma referenced through WikiPedia
        float beta = Mathf.Asin(matrix.m21); // Beta = inversion/eversion. https://en.wikipedia.org/wiki/Euler_angles
        float gamma = Mathf.Atan(-matrix.m20 / matrix.m22); // Gamma = medial/lateral rotation.

        return (alpha, beta, gamma); // returns 1x3 vector

    }


    // END: Joint angle computation functions***********************************************************************************************




    private void storeMostRecentComPosition(float secondsSinceLastFreshMarkerData)
    {
        if (mostRecentComPositions.Count < numberOfComPositionsToStoreForVelocity) //if the list still has space
        {
            mostRecentComPositions.Add(mostRecentTotalBodyComPosition);

            //add the corresponding time interval from the last Update() call to this one.
            mostRecentComTimes.Add(secondsSinceLastFreshMarkerData);
        }
        else //if the list is full
        {
            mostRecentComPositions.RemoveAt(0); //remove the element at index 0, i.e. the oldest COM position in the array
            mostRecentComPositions.Add(mostRecentTotalBodyComPosition); //add the new COM position, which will be stored in the last index of the list

            //add the corresponding time interval from the last Update() call to this one.
            mostRecentComTimes.RemoveAt(0);
            mostRecentComTimes.Add(secondsSinceLastFreshMarkerData);

        }

    }




    //Gets the location of necessary virtual markers.
    //Results are stored in instance variables.
    private void computeVirtualMarkerLocations()
    {
        for (uint index = 0; index < virtualMarkerNames.Length; index++) //for each virtual marker needed for the model
        {
            string currentVirtualMarkerName = virtualMarkerNames[index];

            //get specific marker location
            bool virtualMarkerLocationFound = computeSpecificVirtualMarkerLocation(currentVirtualMarkerName, index);

            // Record whether or not the virtual marker could be computed this frame
            virtualMarkersCouldBeCalculatedThisFrameFlag[index] = virtualMarkerLocationFound;


        }
    }




    //A helper function, called by computeVirtualMarkerLocations()
    //to get the location of a specific virtual marker by name
    private bool computeSpecificVirtualMarkerLocation(string virtualMarkersToBuildSpecifier, uint indexInVirtualMarkerList)
    {
        //call the correct function to get the virtual marker location, depending
        //on the name
        bool virtualMarkerLocationFound = false; // a bool indicating whether or not the virtual marker could be computed

        if (virtualMarkersToBuildSpecifier == recontructHipJointCentersString)
        {
            virtualMarkerLocationFound = constructHipJointCenters(indexInVirtualMarkerList);
        }
        else if (virtualMarkersToBuildSpecifier == nameOfShoulderCenterVirtualMarker)
        {
            virtualMarkerLocationFound = constructCenterOfShouldersVirtualMarker(indexInVirtualMarkerList);
        }
        else //if the name does not match any valid virtual marker name
        {
            //raise an exception because the virtual marker name is not valid
        }

        return virtualMarkerLocationFound;
    }




    // In this function, we compute the center of the pelvis markers as a rough COM approximation. 
    // It can serve as a failsafe for computing COM. Of note, this is NOT equal to a segment COM position - there 
    // is no "pelvis" segment. The multisegment model only contains a combined Pelvis/Trunk segment.
    private void computeAndStoreCenterOfPelvisMarkersPosition()
    {
        //get the positions and occlusion status of the four pelvic markers
        (bool frontLeftPelvisOcclusionStatus, Vector3 frontLeftPelvisPos) = getMarkerPositionAsVectorByName(frontLeftPelvisMarkerName);
        (bool frontRightPelvisOcclusionStatus, Vector3 frontRightPelvisPos) = getMarkerPositionAsVectorByName(frontRightPelvisMarkerName);
        (bool backRightPelvisOcclusionStatus, Vector3 backRightPelvisPos) = getMarkerPositionAsVectorByName(backRightPelvisMarkerName);
        (bool backLeftPelvisOcclusionStatus, Vector3 backLeftPelvisPos) = getMarkerPositionAsVectorByName(backLeftPelvisMarkerName);

        var frontLeftResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(frontLeftPelvisMarkerName);
        var frontRightResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(frontRightPelvisMarkerName);
        var backRightResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(backRightPelvisMarkerName);
        var backLeftResultTuple = markerDataDistributorScript.getMarkerOcclusionStatusAndPositionByName(backLeftPelvisMarkerName);

        //average the x- and y-positions of the four markers to get a shitey guess at COM 
        centerOfPelvisPosition = (frontLeftPelvisPos + frontRightPelvisPos + backRightPelvisPos + backLeftPelvisPos) / 4;
    }

    private void computeAndStoreCenterOfTrunkPosition()
    {
        // Get the position of the two front markers on the trunk belt and the back center marker
        (bool trunkBackCenterAvailable, Vector3 trunkBeltBackCenterMarkerPositionViconFrame) =
            GetMostRecentMarkerPositionByName(trunkBeltBackMiddleMarkerName);
        (bool trunkFrontLeftAvailable, Vector3 trunkBeltFrontLeftMarkerPositionViconFrame) =
            GetMostRecentMarkerPositionByName(trunkBeltFrontLeftMarkerName);
        (bool trunkFrontRightAvailable, Vector3 trunkBeltFrontRightMarkerPositionViconFrame) =
            GetMostRecentMarkerPositionByName(trunkBeltFrontRightMarkerName);

        // Only if all needed markers are available (either not occluded OR reconstructed in this script!)
        if(trunkBackCenterAvailable & trunkFrontLeftAvailable & trunkFrontRightAvailable)
        {
            // Compute the trunk belt midpoint position and store in instance variable
            mostRecentMidpointTrunkBelt = GetTrunkBeltMidpointFromTrunkBeltMarkers(trunkBeltBackCenterMarkerPositionViconFrame,
                 trunkBeltFrontLeftMarkerPositionViconFrame,
                 trunkBeltFrontRightMarkerPositionViconFrame);
        }
    }

    private Vector3 GetTrunkBeltMidpointFromTrunkBeltMarkers(Vector3 trunkBeltBackCenterMarkerPositionViconFrame,
        Vector3 trunkBeltFrontLeftMarkerPositionViconFrame, Vector3 trunkBeltFrontRightMarkerPositionViconFrame)
    {
        // Compute the midpoint of the two front markers
        Vector3 midpointFrontTrunkBelt = (trunkBeltFrontLeftMarkerPositionViconFrame + trunkBeltFrontRightMarkerPositionViconFrame) / 2.0f;

        // Compute the midpoint of the front midpoint and the back center marker
        mostRecentMidpointTrunkBelt = (trunkBeltBackCenterMarkerPositionViconFrame + midpointFrontTrunkBelt) / 2.0f;

        // Return the trunk belt midpoint
        return mostRecentMidpointTrunkBelt;
    }



    private void storeViconAndComDataInDataRecorderObject(bool isMarkerDataOld)
    {
        //Note: the names of the headers (which will specify the names of the markers or segments in question) are 
        //specified during setup.

        //create a list to store the floats
        List<float> markerAndComDataToStore = new List<float>();

        //Vicon frame number and Unity frame time 
        markerAndComDataToStore.Add((float) mostRecentlyAccessedViconFrameNumber);
        markerAndComDataToStore.Add(Time.time);

        // Store whether or not the marker data was old (true) or not (false)
        markerAndComDataToStore.Add(Convert.ToSingle(isMarkerDataOld));

        // The analog sync pin voltage (high = EMG streaming, low = EMG stopped)
        float analogSyncPinVoltage = scriptToRetrieveForcePlateData.GetMostRecentSyncPinVoltageValue();
        markerAndComDataToStore.Add(analogSyncPinVoltage);

        //marker occlusion status 
        markerAndComDataToStore.AddRange(markersInModelOcclusionStatus.Select(x => Convert.ToSingle(x)));

        // Whether or not the individual markers were reconstructed with the rigid body approach this frame
        markerAndComDataToStore.AddRange(markerInModelWasReconstructedThisFrameFlags.Select(x => Convert.ToSingle(x)));

        // Replacing markersInModelXPositions and others with an array that also has the reconstructed markers 
        for(uint index = 0; index < markersInModel.Length; index++)
        {
            (bool success, Vector3 presentMarkerPosition) = getMarkerPositionAsVectorByName(markersInModel[index]);
            reconstructedMarkersInModelXPositions[index] = presentMarkerPosition.x;
            reconstructedMarkersInModelYPositions[index] = presentMarkerPosition.y;
            reconstructedMarkersInModelZPositions[index] = presentMarkerPosition.z;
        } //ISI

        // Marker positions
        /*markerAndComDataToStore.AddRange(markersInModelXPositions);
        markerAndComDataToStore.AddRange(markersInModelYPositions);
        markerAndComDataToStore.AddRange(markersInModelZPositions);*/
        markerAndComDataToStore.AddRange(reconstructedMarkersInModelXPositions);
        markerAndComDataToStore.AddRange(reconstructedMarkersInModelYPositions);
        markerAndComDataToStore.AddRange(reconstructedMarkersInModelZPositions);
        //ISI

            //"Name of reconstructed markers:" + string.Join(", ", namesOfAllReconstructedMarkers.Select(v => v.ToString())) +
            //"occlusion status: " + string.Join(",", markersInModelOcclusionStatus.Select(v=>v.ToString())));

        // Segment used for COM estimation this frame flag?
        markerAndComDataToStore.AddRange(segmentUsedForComEstimationThisFrameFlags.Select(x => Convert.ToSingle(x)));

        // Segment COM approximated with a simple model flag? segmentComEstimatedWithSimplifiedModelThisFrameFlags
        markerAndComDataToStore.AddRange(segmentComEstimatedWithSimplifiedModelThisFrameFlags.Select(x => Convert.ToSingle(x)));

        //Segment COM positions
        (float[] segmentComPositionsX, float[] segmentComPositionsY, float[] segmentComPositionsZ) = extractComponentFloatArraysFromVector3Array(segmentComPositions);
        markerAndComDataToStore.AddRange(segmentComPositionsX);
        markerAndComDataToStore.AddRange(segmentComPositionsY);
        markerAndComDataToStore.AddRange(segmentComPositionsZ);

        // Segment "estimated" COM positions computed with a simplified model. Only computed if segments are missing, i.e. the full model
        // can't be used to compute the segment COMs due to missing markers.
        (float[] segmentEstimatedComPositionsX, float[] segmentEstimatedComPositionsY, float[] segmentEstimatedComPositionsZ) = extractComponentFloatArraysFromVector3Array(segmentEstimatedComPositions);
        markerAndComDataToStore.AddRange(segmentEstimatedComPositionsX);
        markerAndComDataToStore.AddRange(segmentEstimatedComPositionsY);
        markerAndComDataToStore.AddRange(segmentEstimatedComPositionsZ);

        // virtual marker status (could it be calculated or not) 
        markerAndComDataToStore.AddRange(virtualMarkersCouldBeCalculatedThisFrameFlag.Select(x => Convert.ToSingle(x)));

        // Virtual marker  positions
        (float[] virtualMarkerPositionsX, float[] virtualMarkerPositionsY, float[] virtualMarkerPositionsZ) = extractComponentFloatArraysFromVector3Array(virtualMarkerPositions);
        markerAndComDataToStore.AddRange(virtualMarkerPositionsX);
        markerAndComDataToStore.AddRange(virtualMarkerPositionsY);
        markerAndComDataToStore.AddRange(virtualMarkerPositionsZ);

        // Hip joint virtual marker positions
        (float[] hipJointMarkerPositionsX, float[] hipJointMarkerPositionsY, float[] hipJointMarkerPositionsZ) = extractComponentFloatArraysFromVector3Array(hipJointMarkerPositions);
        markerAndComDataToStore.AddRange(hipJointMarkerPositionsX);
        markerAndComDataToStore.AddRange(hipJointMarkerPositionsY);
        markerAndComDataToStore.AddRange(hipJointMarkerPositionsZ);

        // Total body COM position
        markerAndComDataToStore.Add(mostRecentTotalBodyComPosition.x);
        markerAndComDataToStore.Add(mostRecentTotalBodyComPosition.y);
        markerAndComDataToStore.Add(mostRecentTotalBodyComPosition.z);

        // Center of pelvis position
        markerAndComDataToStore.Add(centerOfPelvisPosition.x);
        markerAndComDataToStore.Add(centerOfPelvisPosition.y);
        markerAndComDataToStore.Add(centerOfPelvisPosition.z);

        // COP position
        markerAndComDataToStore.Add(CopPositionViconFrame.x);
        markerAndComDataToStore.Add(CopPositionViconFrame.y);
        markerAndComDataToStore.Add(CopPositionViconFrame.z);

        // Store the first force plate forces and torques
        if (allForcePlateForces != null) // If the force plates have been initialized
        {
            if (allForcePlateForces.Length >= 1 && allForcePlateTorques.Length >= 1)
            {
                markerAndComDataToStore.Add(allForcePlateForces[0].x);
                markerAndComDataToStore.Add(allForcePlateForces[0].y);
                markerAndComDataToStore.Add(allForcePlateForces[0].z);
                markerAndComDataToStore.Add(allForcePlateTorques[0].x);
                markerAndComDataToStore.Add(allForcePlateTorques[0].y);
                markerAndComDataToStore.Add(allForcePlateTorques[0].z);
            }

            // Store the second force plate forces and torques
            if (allForcePlateForces.Length >= 2 && allForcePlateTorques.Length >= 2)
            {
                markerAndComDataToStore.Add(allForcePlateForces[1].x);
                markerAndComDataToStore.Add(allForcePlateForces[1].y);
                markerAndComDataToStore.Add(allForcePlateForces[1].z);
                markerAndComDataToStore.Add(allForcePlateTorques[1].x);
                markerAndComDataToStore.Add(allForcePlateTorques[1].y);
                markerAndComDataToStore.Add(allForcePlateTorques[1].z);
            }
        }

        // Right hip angles
        markerAndComDataToStore.Add(rightHipFlexionAngle);
        markerAndComDataToStore.Add(rightHipAbductionAngle);
        markerAndComDataToStore.Add(rightHipInternalRotationAngle);

        // Left hip angles
        markerAndComDataToStore.Add(leftHipFlexionAngle);
        markerAndComDataToStore.Add(leftHipAbductionAngle);
        markerAndComDataToStore.Add(leftHipInternalRotationAngle);

        // Right knee angles
        markerAndComDataToStore.Add(rightKneeFlexionAngle);
        markerAndComDataToStore.Add(rightKneeAbductionAngle);
        markerAndComDataToStore.Add(rightKneeInternalRotationAngle);
        // Left knee angles
        markerAndComDataToStore.Add(leftKneeFlexionAngle);
        markerAndComDataToStore.Add(leftKneeAbductionAngle);
        markerAndComDataToStore.Add(leftKneeInternalRotationAngle);
        // Right ankle angles
        markerAndComDataToStore.Add(rightAnkleFlexionAngle);
        markerAndComDataToStore.Add(rightAnkleInversionAngle);
        markerAndComDataToStore.Add(rightAnkleInternalRotationAngle);
        // Left ankle angles
        markerAndComDataToStore.Add(leftAnkleFlexionAngle);
        markerAndComDataToStore.Add(leftAnkleInversionAngle);
        markerAndComDataToStore.Add(leftAnkleInternalRotationAngle);

        //send this frame of data to the General Data Recorder object to be stored on dynamic memory until it is written to file
        generalDataRecorderScript.storeRowOfMarkerData(markerAndComDataToStore.ToArray());
    }


    // This function takes a Vector3 array input, extracts the x,y,z components from each element, 
    // and returns them in 3 float arrays.
    private (float[], float[], float[]) extractComponentFloatArraysFromVector3Array(Vector3[] vectorInput)
    {
        float[] xComponent = new float[vectorInput.Length];
        float[] yComponent = new float[vectorInput.Length];
        float[] zComponent = new float[vectorInput.Length];

        for(uint index = 0; index < vectorInput.Length; index++)
        {
            Vector3 currentVector = vectorInput[index]; 
            xComponent[index] = currentVector.x;
            yComponent[index] = currentVector.y;
            zComponent[index] = currentVector.z;
        }

        //return the component float vectors as a tuple
        return (xComponent, yComponent, zComponent);
    }



    //Start: Functions for 3D reconstruction of missing markers**********************


    //The highest level function used for marker reconstruction. Accepts a list of markers on a rigid body and reconstructs any missing ones,
    //assuming there are enough visible markers for reconstruction.
    private (bool, string[], Vector3[]) reconstructMissingMarkersOnOneRigidBodyThisFrame(string[] markersOnRigidBodyNames)
    {

        List<string> markersVisibleNames = new List<string>();
        List<string> markersOccludedNames = new List<string>();
        //first, figure out which markers on the segment are visible and which are occluded.
        for (uint markerIndex = 0; markerIndex < markersOnRigidBodyNames.Length; markerIndex++) //for each marker on the segment in question
        {
            //Debug.Log(string.Join(", ", markersInModel.Select(v=>v.ToString())) + "\n");
            bool markerOccluded = markersInModelOcclusionStatus[Array.IndexOf(markersInModel, markersOnRigidBodyNames[markerIndex])];

            if (markerOccluded) //if the marker is occluded
            {
                //add the name of the marker to the occluded marker list
                markersOccludedNames.Add(markersOnRigidBodyNames[markerIndex]);
            }
            else
            {
                //add the name of the marker to the visible marker list
                markersVisibleNames.Add(markersOnRigidBodyNames[markerIndex]);
            }
        }

        //define return parameters 
        bool reconstructionSuccessFlag;
        string[] namesOfReconstructedMarkers = new string[markersOccludedNames.Count];
        Vector3[] positionsOfReconstructedMarkers = new Vector3[markersOccludedNames.Count];
        
        //reconstruct, if possible 
        if (markersVisibleNames.Count >= 3) //if there are 3 or more markers, we can reconstruct
        {
            var reconstructionResultTuple = constructLocalFrameAndFindOccludedMarkerLocalCoordinates(markersVisibleNames, markersOccludedNames);
            reconstructionSuccessFlag = true;
            namesOfReconstructedMarkers = reconstructionResultTuple.Item1;
            positionsOfReconstructedMarkers = reconstructionResultTuple.Item2;        }
        else //we can't reconstruct, return false
        {
            reconstructionSuccessFlag = false;
        }

        //return a boolean indicating whether reconstruction was successful (true) or not (false), along with names of reconstructed markers
        //and their positions, if computed
        return (reconstructionSuccessFlag, namesOfReconstructedMarkers, positionsOfReconstructedMarkers);
    }


    //Should only be calling this function if markersVisibleNames has 3 or more markers in it
    private (string[], Vector3[]) constructLocalFrameAndFindOccludedMarkerLocalCoordinates(List<string> markersVisibleNames,
                                                                     List<string> markersOccludedNames)
    {
        //with setup data, construct a coordinate system using the first three markers visible in the current frame
        Matrix4x4 transformationMatrixViconToLocalInSetup = constructRotationFromViconToLocalInSetupFrame(markersVisibleNames);

        //find the coordinates of the missing markers in this (setup local frame) coordinate system
        Vector3[] positionsOfMissingMarkersInLocalFrameInSetup = transformPositionsToNewCoordinateFrame(markersOccludedNames, markersInModel,
            averagePositionOfModelMarkersInStartupFrames, transformationMatrixViconToLocalInSetup);

        //find the rotation matrix from the local coordinate system to global in the current frame
        Matrix4x4 transformationMatrixLocalToViconThisFrame = constructRotationFromLocalToViconThisFrame(markersVisibleNames);

        //finally, transform the missing marker coordinates from local frame to global frame
        Vector3[] reconstructedMissingMarkerPositionsInViconFrame = transformPositionsToNewCoordinateFrame(markersOccludedNames, markersOccludedNames.ToArray(),
            positionsOfMissingMarkersInLocalFrameInSetup, transformationMatrixLocalToViconThisFrame);

        //DEBUG ONLY 
        /*Debug.Log("Frame: "  + "MarkerOccludedNames =" + string.Join(", ", markersOccludedNames.Select(v => v.ToString())) + 
    "In array form: " + string.Join(", ", markersOccludedNames.ToArray().Select(v => v.ToString())));*/
        //print reconstructed positions
        /*if (markersOccludedNames.Count > 0)
        {
            Vector3 firstReconstructedMarker = reconstructedMissingMarkerPositionsInViconFrame[0];
            Debug.Log("First reconstructed marker position with name " + markersOccludedNames[0] + " is (x,y,z): ( " + firstReconstructedMarker.x + ", " + firstReconstructedMarker.y + ", " + firstReconstructedMarker.z + " )");

            Vector3 secondReconstructedMarker = reconstructedMissingMarkerPositionsInViconFrame[1];
            Debug.Log("Second reconstructed marker position with name " + markersOccludedNames[1] + " is (x,y,z): ( " + secondReconstructedMarker.x + ", " + secondReconstructedMarker.y + ", " + secondReconstructedMarker.z + " )");
        }*/
        //return the names of the reconstructed markers and their positions 
        return (markersOccludedNames.ToArray(), reconstructedMissingMarkerPositionsInViconFrame);

    }



    private Matrix4x4 constructRotationFromViconToLocalInSetupFrame(List<string> markersVisibleNames)
    {
        //get all needed marker positions FROM THE SETUP AVERAGED FRAMES!
        Vector3 marker1Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(markersInModel, markersVisibleNames[0])]; ;
        Vector3 marker2Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(markersInModel, markersVisibleNames[1])];
        Vector3 marker3Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(markersInModel, markersVisibleNames[2])];

        //pelvis origin in global frame is average position of the two ASIS and two PSIS markers
        Vector3 originOfLocalFrameInViconFrame = marker1Position;

        //X-axis will be pointing from marker 1 to marker 2
        Vector3 xAxisVector = marker2Position - marker1Position;

        //Y-axis will be pointing from marker 1 to marker 3.
        //Note that to ensure orthogonality, will be recomputed from Z-axis.
        Vector3 yAxisVector = marker3Position - marker1Position;

        //Z-axis will be along the right-handed cross product of the x- and y-axes
        Vector3 zAxisVector = getRightHandedCrossProduct(xAxisVector, yAxisVector);

        //recompute y-axis as orthogonal to z and x
        yAxisVector = getRightHandedCrossProduct(zAxisVector, xAxisVector);

        //normalize the axes
        xAxisVector.Normalize();
        yAxisVector.Normalize();
        zAxisVector.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(xAxisVector, yAxisVector, zAxisVector, originOfLocalFrameInViconFrame);

        Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        return transformationMatrixViconToLocal;

    }


    private Matrix4x4 constructRotationFromLocalToViconThisFrame(List<string> markersVisibleThisFrameNames)
    {
        //get all needed marker positions FROM THE CURRENT FRAME!
        (_, Vector3 marker1Position) = getMarkerPositionAsVectorByName(markersVisibleThisFrameNames[0]);
        (_, Vector3 marker2Position) = getMarkerPositionAsVectorByName(markersVisibleThisFrameNames[1]);
        (_, Vector3 marker3Position) = getMarkerPositionAsVectorByName(markersVisibleThisFrameNames[2]);

        //pelvis origin in global frame is average position of the two ASIS and two PSIS markers
        Vector3 originOfLocalFrameInViconFrame = marker1Position;

        //X-axis will be pointing from marker 1 to marker 2
        Vector3 xAxisVector = marker2Position - marker1Position;

        //Y-axis will be pointing from marker 1 to marker 3.
        //Note that to ensure orthogonality, will be recomputed from Z-axis.
        Vector3 yAxisVector = marker3Position - marker1Position;

        //Z-axis will be along the right-handed cross product of the x- and y-axes
        Vector3 zAxisVector = getRightHandedCrossProduct(xAxisVector, yAxisVector);

        //recompute y-axis as orthogonal to z and x
        yAxisVector = getRightHandedCrossProduct(zAxisVector, xAxisVector);

        //normalize the axes
        xAxisVector.Normalize();
        yAxisVector.Normalize();
        zAxisVector.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(xAxisVector, yAxisVector, zAxisVector, originOfLocalFrameInViconFrame);

        return transformationMatrixLocalToVicon;
    }



    private Vector3[] transformPositionsToNewCoordinateFrame(List<string> namesOfMarkersToTransform, string[] arrayOfMarkerNames,
            Vector3[] arrayOfMarkerPositions, Matrix4x4 transformationMatrix)
    {
        //initialize the return parameter, which will store the transformed positions
        Vector3[] transformedPositions = new Vector3[namesOfMarkersToTransform.Count];

        for (int markerIndex = 0; markerIndex < namesOfMarkersToTransform.Count; markerIndex++) //for each marker position to transform
        {
            //get the position of that marker in the original frame, then multiply it by the transform to the target frame
            transformedPositions[markerIndex] = transformationMatrix.MultiplyPoint3x4(arrayOfMarkerPositions[Array.IndexOf(arrayOfMarkerNames, namesOfMarkersToTransform[markerIndex])]);
            Vector3 temp_transformed_positions = transformationMatrix.MultiplyPoint3x4(arrayOfMarkerPositions[Array.IndexOf(arrayOfMarkerNames, namesOfMarkersToTransform[markerIndex])]);
        }

        return transformedPositions;
    }



    //End reconstruction functions ****************************************************


    //Start: Functions for hip joint center computations*******************************

    // Construct the virtual marker corresponding to either the hip joint center (HJC) or,
    // if fewer than three pelvic markers are available, to the center of the pelvis.
    private bool constructHipJointCenters(uint indexInVirtualMarkerList)
    {
        //return parameter
        bool ableToReconstructVirtualMarker = false;

        //figure out which markers are occluded
        bool[] markersOccluded = new bool[markerNamesForHipJointCenters.Length];
        for(int index = 0; index < markerNamesForHipJointCenters.Length; index++)
        {
            //the marker is considered occluded/unavailable if it is occluded and was not reconstructed
            int markerIndexInModelMarkerList = Array.IndexOf(markersInModel, markerNamesForHipJointCenters[index]);
            markersOccluded[index] = ( markersInModelOcclusionStatus[markerIndexInModelMarkerList] && 
                !markerInModelWasReconstructedThisFrameFlags[markerIndexInModelMarkerList] );
        }
        int numberOfOccludedMarkers = markersOccluded.Count(element => element == true);
        int numberOfVisibleMarkers = (markerNamesForHipJointCenters.Length - numberOfOccludedMarkers);

        //based on number of missing markers, either reconstruct all necessary markers (>=3 present)
        //or reconstruct the planar position of the Hjc midpoint based on the last relative position (2 markers)
        if (numberOfOccludedMarkers == 0) //if no markers are missing (could be occluded this frame, but all were reconstructed)
        {
            //call the standard HJC function
            var hjcTuple = getHjcAndHjcMidpointPositions();

            //Store the HJC positions and their midpoint
            hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfRightHjcVirtualMarker)] = hjcTuple.Item1;
            hipJointMarkerPositions[Array.IndexOf(hipJointMarkerNames, nameOfLeftHjcVirtualMarker)] = hjcTuple.Item2;
            virtualMarkerPositions[indexInVirtualMarkerList] = hjcTuple.Item3; //store the HJC midpoint position, the desired virtual marker

            //set the return boolean to true, as we could reconstruct the virtual marker
            ableToReconstructVirtualMarker = true;
        } else if (numberOfVisibleMarkers == 2) //if there are only two markers, we can use a simplified planar reconstruction
        {
            //do simple planar reconstruction

            //set the return boolean to true, as we could reconstruct the virtual marker
            ableToReconstructVirtualMarker = false; // since not implemented yet
        }
        else //if there are 1 or fewer visible markers
        {
            //allow the reconstruct boolean to remain false, indicating that this virtual marker cannot be reconstructed
        }

        return ableToReconstructVirtualMarker;
    }



    private (Vector3, Vector3, Vector3) getHjcAndHjcMidpointPositions()
    {
        //get all needed marker positions
        (_, Vector3 frontLeftPelvisMarkerPosition) = getMarkerPositionAsVectorByName(frontLeftPelvisMarkerName);
        (_, Vector3 frontRightPelvisMarkerPosition) = getMarkerPositionAsVectorByName(frontRightPelvisMarkerName);
        (_, Vector3 backRightPelvisMarkerPosition) = getMarkerPositionAsVectorByName(backRightPelvisMarkerName);
        (_, Vector3 backLeftPelvisMarkerPosition) = getMarkerPositionAsVectorByName(backLeftPelvisMarkerName);

        //pelvis origin in global frame is average position of the two ASIS and two PSIS markers
        Vector3 pelvisOriginInViconFrame = (frontLeftPelvisMarkerPosition + frontRightPelvisMarkerPosition +
                                backRightPelvisMarkerPosition + backLeftPelvisMarkerPosition) / 4;

        //X-axis will be pointing from the front left marker to the front right marker
        Vector3 xAxisVector = frontRightPelvisMarkerPosition - frontLeftPelvisMarkerPosition;

        //Y-axis will point from the midpoint of the rear pelvis markers to the midpoint of the
        //front pelvis markers. Note that to ensure orthogonality, will be recomputed from Z-axis.
        Vector3 yAxisVector = ((frontLeftPelvisMarkerPosition + frontRightPelvisMarkerPosition) / 2) -
            ((backRightPelvisMarkerPosition + backLeftPelvisMarkerPosition) / 2); //

        Vector3 zAxisVector = getRightHandedCrossProduct(xAxisVector, yAxisVector);

        //recompute y-axis as orthogonal to x and z
        yAxisVector = getRightHandedCrossProduct(zAxisVector, xAxisVector);

        //normalize the axes
        xAxisVector.Normalize();
        yAxisVector.Normalize();
        zAxisVector.Normalize();

        //get rotation and translation from pelvis frame to global Vicon frame
        Matrix4x4 transformationMatrixPelvisToVicon = getTransformationMatrix(xAxisVector, yAxisVector, zAxisVector, pelvisOriginInViconFrame);

        //get HJC positions in global coordinates
        var HjcTuple = getHjcsAndMidpointFromAsis(frontRightPelvisMarkerPosition,
                                                                         frontLeftPelvisMarkerPosition,
                                                                         transformationMatrixPelvisToVicon);

        return HjcTuple;

    }

    private (Vector3, Vector3, Vector3) getHjcsAndMidpointFromAsis(Vector3 rightAsisPositionInViconCoords,
                                                             Vector3 leftAsisPositionInViconCoords,
                                                             Matrix4x4 transformationPelvisToVicon)
    {
        // Get the offsets fro  m the ASIS to the HJCs
        Vector3 offsetRightAsisToHjcInPelvisCoords = new Vector3(-0.141f * interAsisWidth, -0.193f * interAsisWidth,
                                                                 -0.304f * interAsisWidth);

        Vector3 offsetLeftAsisToHjcInPelvisCoords = new Vector3(0.141f * interAsisWidth, -0.193f * interAsisWidth,
                                                                 -0.304f * interAsisWidth);

        //Add the offset to the ASIS positions in Vicon coordinates. Note that Matrix4x4.MultiplyVector
        //applies only the rotation of the transformation matrix and excludes the translation.
        Vector3 rightHjcInViconCoords = rightAsisPositionInViconCoords +
                                        transformationPelvisToVicon.MultiplyVector(offsetRightAsisToHjcInPelvisCoords);

        Vector3 leftHjcInViconCoords = leftAsisPositionInViconCoords +
                                transformationPelvisToVicon.MultiplyVector(offsetLeftAsisToHjcInPelvisCoords);

        //The midpoint of the HJC is critical, as it is the "distal" virtual marker of the trunk segment
        Vector3 HjcMidpoint = (rightHjcInViconCoords + leftHjcInViconCoords) / 2;

        return (rightHjcInViconCoords, leftHjcInViconCoords, HjcMidpoint);


    }

    //End: Functions for hip joint center computations*******************************



    //Start: Functions for shoulder center computations*******************************

    private bool constructCenterOfShouldersVirtualMarker(uint indexInVirtualMarkerList)
    {
        //intialize return parameter
        bool ableToReconstructVirtualMarker = false;

        //check occlusion status of each marker manually
        bool leftShoulderOcclusionStatus = markersInModelOcclusionStatus[Array.IndexOf(markersInModel, leftAcromionMarkerName)];
        bool rightShoulderOcclusionStatus = markersInModelOcclusionStatus[Array.IndexOf(markersInModel, leftAcromionMarkerName)];
        bool c7OcclusionStatus = true; // markersInModelOcclusionStatus[Array.IndexOf(markersInModel, c7MarkerName)];

        //compute the center of the shoulders virtual marker based on which Vicon markers we have available
        Vector3 centerOfShouldersPosition; 
        if ((leftShoulderOcclusionStatus == false) && (rightShoulderOcclusionStatus == false)) //if both shoulder/acromion markers are visible
        {
            //get the acromion/shoulder positions
            (_, Vector3 leftShoulderPosition) = getMarkerPositionAsVectorByName(leftAcromionMarkerName);
            (_, Vector3 rightShoulderPosition) = getMarkerPositionAsVectorByName(rightAcromionMarkerName);

            //compute the midpoint as the average of the two acromion positions
            centerOfShouldersPosition = (leftShoulderPosition + rightShoulderPosition) / 2;
            virtualMarkerPositions[indexInVirtualMarkerList] = centerOfShouldersPosition; //store the shoulders midpoint position, the desired virtual marker

            //set the flag indicating we could reconstruct the midpoint shoulder position
            ableToReconstructVirtualMarker = true;
        }
        else if (c7OcclusionStatus == false) //if either shoulder is missing, use the c7 marker
        {
            (_, centerOfShouldersPosition) = getMarkerPositionAsVectorByName(c7MarkerName);
            virtualMarkerPositions[indexInVirtualMarkerList] = centerOfShouldersPosition; //store the shoulders midpoint position, the desired virtual marker

            //set the flag indicating we could reconstruct the midpoint shoulder position
            ableToReconstructVirtualMarker = true;
        }
        else //if one or more shoulder markers and the c7 marker are missing, then we can't compute the shoulder midpoint!
        {
            //allow the return boolean to remain false, indicating we could not get this virtual marker position
        }

        //return a bool indicating if we were successful at reconstructing the shoulder midpoint position
        return ableToReconstructVirtualMarker;

    }

    //End: Functions for shoulder center computations*******************************



    //Start: General purpose functions for working with Vicon marker data *******************************


    // NOTE: this function returns EITHER the most recent marker data from the Vicon data stream OR the 
    // reconstructed marker position (if it was occluded on the last frame in which Vicon data was sent).
    private (bool, Vector3) getMarkerPositionAsVectorByName(string markerName)
    {
        bool isMarkerAvailable;
        int markerIndex = Array.IndexOf(markersInModel, markerName);

        if (markerIndex >= 0) //if the marker is a physical marker in the model-specific marker array
        {
            if (markersInModelOcclusionStatus[markerIndex] != true) //if the marker is visible this frame
            {
                //find and return the marker position as a Vector3
                isMarkerAvailable = true;
                return (isMarkerAvailable, new Vector3(markersInModelXPositions[markerIndex], markersInModelYPositions[markerIndex],
                markersInModelZPositions[markerIndex]));
            }
            else //if the marker is occluded
            {
                //check to see if the marker was reconstructed 
                markerIndex = namesOfAllReconstructedMarkers.IndexOf(markerName);
                if (markerIndex >= 0) //if the marker is occluded but has been reconstructed
                {
                    isMarkerAvailable = true;
                    return (isMarkerAvailable, positionsOfAllReconstructedMarkers[markerIndex]);
                }
                else //if the marker is occluded and could not be reconstructed
                {
                    isMarkerAvailable = false;
                    return (isMarkerAvailable, new Vector3(0.0f, 0.0f, 0.0f));
                }
            }
               
        } else //check if the marker is in the virtual marker list
        {
            markerIndex = Array.IndexOf(virtualMarkerNames, markerName);
            if (markerIndex >= 0) //if the marker is in the virtual marker array
            {
                //find and return the new (virtual) marker position as a Vector3
                return (true,virtualMarkerPositions[markerIndex]); //virtual marker positions are already stored as type Vector3 
            } else //finally, check if the marker is a hip joint center. Eventually, these markers should be folded into the virtual markers array!
            {
                markerIndex = Array.IndexOf(hipJointMarkerNames, markerName);
                if (markerIndex >= 0) //if the marker is in the hip joint marker array
                {
                    return (true,hipJointMarkerPositions[markerIndex]); //hip joint virtual marker positions are already stored as type Vector3
                } else //if there is no matching marker
                {
                    //the requested marker is not available, so print an error
                    string errorMessage = "No marker could be found with marker name " + markerName;
                    printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, errorMessage);

                    //also, add a return value so the compiler doesn't get mad
                    return (false,new Vector3(0.0f, 0.0f, 0.0f));
                }     
            }
        }
    }



    //Computes a right-handed cross-product so that we can work in the Vicon coordinate system
    public Vector3 getRightHandedCrossProduct(Vector3 leftVector, Vector3 rightVector)
    {
        float newXValue = leftVector.y * rightVector.z - leftVector.z * rightVector.y;
        float newYValue = leftVector.z * rightVector.x - leftVector.x * rightVector.z;
        float newZValue = leftVector.x * rightVector.y - leftVector.y * rightVector.x;

        return new Vector3(newXValue, newYValue, newZValue);

    }


    //Given the three normalized/unit axes of a local coordinate system and the translation FROM the target coordinate system
    //TO the local coordinate system defined in the target coordinate system, construct a transformation matrix
    //that will transform points in the local coordinate system to the target coordinate system
    public Matrix4x4 getTransformationMatrix(Vector3 xAxisVector, Vector3 yAxisVector, Vector3 zAxisVector, Vector3 translationTargetToLocalInTargetFrame)
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


    //End: General purpose functions for working with Vicon marker data *******************************


    //Start: Debugging functions ************************************************************************

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
        }else if (logType == logAnErrorSpecifier) //if an error is being logged
        {
            Debug.LogError(logMessage);
        }
    }


    [Conditional("ENABLE_LOGS")]
    private void printStopwatchTimeElapsedToConsole(bool isMarkerDataOld)
    {
        // Get the elapsed time as a TimeSpan value.
        //TimeSpan ts = stopWatch.Elapsed;

        long ticksThisTime = 0;
        long nanosecPerTick = (1000L * 1000L * 1000L) / Stopwatch.Frequency;
        ticksThisTime = tempStopWatch.ElapsedTicks;
        float millisecondsElapsed = (float)(((double)nanosecPerTick * ticksThisTime) / (1000L * 1000L));

        // Format and display the TimeSpan value.
        /*string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:0000}",
            ts.Hours, ts.Minutes, ts.Seconds,
            ts.Milliseconds / 10);*/
        //Debug.Log("RunTime for FixedUpdate() call in ManageCenterOfMassScript.cs was " + millisecondsElapsed + " [ms].");
        Debug.Log("RunTime for FixedUpdate() call in ManageCenterOfMassScript.cs was " + millisecondsElapsed + 
            " [ms]" + " and marker data was old? = " + isMarkerDataOld);
    }

    //End: Debugging functions ***************************************************************************

    //Start: Unit testing functions ************************************************************************

    private void checkIfSegmentModelHasTotalFractionalBodyMassSummingToOne(bodySegment[] multiSegmentModel)
    {
        float totalBodyMass = 0.0f; //keeps track of the total fractional body mass. All segment masses should sum to 1.0
        float tolerance = 0.001f; //a tolerance for the error of the total fractional body mass
        
        for(int segmentIndex = 0; segmentIndex < multiSegmentModel.Length; segmentIndex++) //for each segment in the model
        {
            //add the segment's fractional mass to the running sum of segment masses
            totalBodyMass = totalBodyMass + multiSegmentModel[segmentIndex].getFractionOfTotalBodyMass();
        }

        //if the sum of the fractional segment masses is sufficiently close to 1.0
        if(Mathf.Abs(totalBodyMass - 1.0f) < tolerance)
        {
            printLogMessageToConsoleIfDebugModeIsDefined("Multisegment model for COM has total fractional mass of " +
                "(should be 1.0): " + totalBodyMass + ", which is within tolerance.");
        } else //else if the model fails to have a mass close to 1.0
        {
            //log an error
            string errorMessage = "Multisegment model used to find COM has a total fractional mass equal to " +
                 totalBodyMass + ", which is too far from 1.0. Check model segments.";
            printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, errorMessage);
        }
        
    }



    //End: Unit testing functions ************************************************************************


} //end class "ManageCenterOfMassScript"
