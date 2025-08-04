/* 
 * Main sources: ME281 materials and Collins et al 2009, ""
 * 
 * Notes for self: To do = 1.) complete definition of the segment coordinate frames and get transformation matrices from global, 
 * 2.) Get plantarflexion/dorsiflexion ankle angle from the Euler angles, 
 * 3.) determine if we need to ensure local coordinate frames are orthogonal (I think it may be already, consider more), 
 * 4.) Use a single frame for rigid body reconstruction instead of an average over startup frames (also do this in COM script).
 * 5.) Check to see if we somehow are using an old manageCOM script...I don't see some later dev I thought I had done...e.g. R.TibTub
 * 
 * Answers: 3.) Try using the cross-product of the z-axis and the vector between two ankle markers to get the A/P axis. 
 * This is more similar to ME281 and also ensures all axes are orthogonal, unlike the method described in Collins et al 2009.
 */

#define ENABLE_CUSTOM_WARNINGS_ERRORS //recommended that this define is always present so we can see user-defined warnings and errors
//#define ENABLE_LOGS //may want to comment out this define to suppress user-defined logging ("Debug Mode)")

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





public class AnkleAngleManagementScript : MonoBehaviour
{

    //debugging and testing 
    Stopwatch stopWatch = new Stopwatch();
    Stopwatch tempStopWatch = new Stopwatch();
    private const string logAMessageSpecifier = "LOG";
    private const string logAWarningSpecifier = "WARNING";
    private const string logAnErrorSpecifier = "ERROR";

    //key public game objects
    public GameObject markerDataDistributor; //the GameObject that reads marker data each frame and has getter functions to get marker data by name. Accessses data via the dataStream prefab from the Unity/Vicon plugin.
    public Camera sceneCamera; //the camera that visulizes the scene. Used for converting viewport coordinates to world coordinates.

    //key private game object scripts
    private UnityVicon.ReadViconDataStreamScript markerDataDistributorScript; //the callable script of the GameObject that makes marker data available.

    //logic flags that control program flow
    private bool storingFramesForSetupFlag; //if true, it means we're saving each marker frame's data. This data is used for setup.
    private bool setupComplete = false; //whether setup is complete (true) or not (false)

    //name of knee markers
    private const string rightKneeMarkerName = "RKNE";
    private const string leftKneeMarkerName = "LKNE";
    private const string rightKneeMedialMarkerName = "RKNEEMED";
    private const string leftKneeMedialMarkerName = "RKNEEMED";

    //name of shank markers
    private const string rightTibialTuberosityMarkerName = "R.TibTub";
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
    private const uint numberOfSetupMarkerFrames = 50; //how many marker frames to store for setup.
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


    // List of rigid bodies that we will attempt to reconstruct, if needed.
    private const string leftShankRigidBodyName = "LeftShank";
    private const string rightShankRigidBodyName = "RightShank";
    private const string leftFootRigidBodyName = "LeftFoot";
    private const string rightFootRigidBodyName = "RightFoot";

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
        //get reference to the marker data distributor, which distributes data from the Vicon data stream
        markerDataDistributorScript = markerDataDistributor.GetComponent<UnityVicon.ReadViconDataStreamScript>();

        // Get a reference to the General Data Recorder script, which will save marker data to file during periods of interest. 
        // Specifically, during ROM collection and during active sinusoid tracking.
        generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

        // List of the rigid bodies that we will attempt to reconstruct, if necessary.
        // The order must match the order of the List<string[]> array containing
        // string arrays of the marker names within the corresponding rigid body = "rigidBodiesToReconstructMarkerNames"
        rigidBodiesToReconstructByName = new string[] { leftShankRigidBodyName , rightShankRigidBodyName ,
            leftFootRigidBodyName , rightFootRigidBodyName};

        //Since in this script, all of our rigid bodies for reconstruction are also body segments,
        //we set the names of the body segments equal to the names of rigid bodies to be reconstructed
        segmentNames = rigidBodiesToReconstructByName;

        //Define the marker skeleton used for ankle angle collection on both sides
        namesOfAllMarkersInSkeleton = new string[] { rightKneeMarkerName, rightKneeMedialMarkerName, rightTibialTuberosityMarkerName,rightAnkleMarkerName , 
            rightAnkleMedialMarkerName, rightFirstMetatarsalMarkerName, rightFifthMetatarsalMarkerName, rightSecondMetatarsalMarkerName,
            leftKneeMarkerName, leftKneeMedialMarkerName, leftTibialTuberosityMarkerName, leftAnkleMarkerName ,
            leftAnkleMedialMarkerName, leftFirstMetatarsalMarkerName, leftFifthMetatarsalMarkerName, leftSecondMetatarsalMarkerName};

        // Initialize boolean arrays dependent on the length of our skeleton marker array or rigid body array
        markerInSkeletonWasReconstructedThisFrameFlags = new bool[namesOfAllMarkersInSkeleton.Length];

        //initialize variables
        initializeVariables();

        // Set the variable naming for our marker .CSV file, which will store all of the marker data
        setMarkerDataNaming();

        //Since we still need to collect a few dozen marker frames for setup,
        //set the flag to true
        storingFramesForSetupFlag = true;
    }



    // Update is called once per frame
    void FixedUpdate()
    {
        if (setupComplete) //if setup has been completed, update the center of mass
        {
            //update the ankle angle for this frame, if new Vicon marker data is available
            updateAnkleAngle(); //every frame, update the ankle angle (degrees). May want to put this into a FixedUpdate() call.

            //reset variables needed for the next frame 
            resetVariablesNeededForNextFrame();
        } else //if we're still setting up, then keep trying to set up
        {
            printLogMessageToConsoleIfDebugModeIsDefined("Attempting setup");

            //call the setup function
            setupComplete = setupAnkleAngleManager();

            if (setupComplete) //if setup is complete
            {
                //print that setup is complete to console, if in debug mode
                Debug.Log("Setup complete");
                printLogMessageToConsoleIfDebugModeIsDefined("Setup completed.");
            }
        }
    }

    //END: Core Unity functions ( start(), update() )***********************************************************************************************






    //START: Functions called for setup only***********************************************************************************************


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
    //2.) Store startup frames that we can use later for rigid body reconstruction
    //Returns: a boolean indicating whether or not setup is complete
    private bool setupAnkleAngleManager()
    {
        //initialize the return value as false (setup is not complete)
        bool setupComplete = false;

        //set up the reconstruction parameters (which rigid bodies to reconstruct, rigid body marker names, etc.)
        setUpRigidBodyReconstructionParameters();

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
                setupComplete = true;
            }
        }

        return setupComplete; //replace this with an overall "setupSuccessful" bool return value
    }


    private void setUpRigidBodyReconstructionParameters()
    {
        // List of the marker names contained within each rigid body that we may wish to reconstruct
        markersInRightShank = new string[] { rightKneeMarkerName, rightKneeMedialMarkerName , rightTibialTuberosityMarkerName, rightAnkleMarkerName, rightAnkleMedialMarkerName};
        markersInLeftShank = new string[] { leftKneeMarkerName, leftKneeMedialMarkerName, leftTibialTuberosityMarkerName, leftAnkleMarkerName, leftAnkleMedialMarkerName };
        markersInRightFoot = new string[] { rightFifthMetatarsalMarkerName, rightSecondMetatarsalMarkerName,
            rightFirstMetatarsalMarkerName, rightAnkleMarkerName, rightAnkleMedialMarkerName}; // names of all markers in the left foot segment. Initialized in setup.
        markersInLeftFoot = new string[] { leftFifthMetatarsalMarkerName, leftSecondMetatarsalMarkerName, 
            leftFirstMetatarsalMarkerName, leftAnkleMarkerName, leftAnkleMedialMarkerName }; // names of all markers in the right foot segment. Initialized in setup.

        // Store the names of the rigid bodies that should be reconstructed using rigid body assumptions, if necessary.
        rigidBodiesToReconstructMarkerNames = new List<string[]>() { markersInLeftShank, markersInRightShank, markersInLeftFoot, markersInRightFoot };
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


    //The top-level function called in the Update() loop.
    private void updateAnkleAngle()
    {
        //first, get position and occlusion status of all the markers in our model
        bool isMarkerDataOld = getMarkerDataForAllMarkersNeededInSkeleton(namesOfAllMarkersInSkeleton);


        if (!isMarkerDataOld) // if there is new Vicon data to use for computing ankle angle
        {

            // Reconstruct missing markers that can be reconstructed and that are necessary. 
            // These markers are listed 
            List<string> rigidBodiesAvailableThisFrame = reconstructMissingMarkersOnListedRigidBodies(rigidBodiesToReconstructByName);

            // Construct transformations from the Vicon global frame to each segment's frame
            Matrix4x4[] transformationsViconFrameToSegmentFrameAllSegments = getTransformationsFromViconGlobalFrameToSegmentFrames(segmentNames);

            // Construct the transformation from the shank segment to the foot segment
            Matrix4x4 transformationFromRightShankToRightFoot = getTransformationFromRightShankToRightFoot(transformationsViconFrameToSegmentFrameAllSegments);
            Matrix4x4 transformationFromLeftShankToLeftFoot = getTransformationFromLeftShankToLeftFoot(transformationsViconFrameToSegmentFrameAllSegments);


            // Extract the ankle angle data from the shank to foot transformation.
            // Euler angles seem to be the standard (from ME281).
            // Euler angles are computed.
            // Also, consider writing your own function to verify these results!!!! 
            rightShankToRightFootEulerAngles = transformationFromRightShankToRightFoot.rotation.eulerAngles;
            leftShankToLeftFootEulerAngles = transformationFromLeftShankToLeftFoot.rotation.eulerAngles;

            // Set the flag indicating that some valid ankle angle data has been generated thus far to true
            hasScriptGeneratedAnkleAngleDataYetFlag = true;

            //For now, print the two ankle angles
/*            Debug.Log("Right ankle euler angles are (1,2,3): ( " + rightShankToRightFootEulerAngles.x + ", " + rightShankToRightFootEulerAngles.y +
                ", " + rightShankToRightFootEulerAngles.z + " )");
            Debug.Log("Left ankle euler angles are (1,2,3): ( " + leftShankToLeftFootEulerAngles.x + ", " + leftShankToLeftFootEulerAngles.y +
                ", " + leftShankToLeftFootEulerAngles.z + " )");*/

        }

        // If we should currently be saving marker data to file and if we have generated any ankle angle data so far, then 
        // record the marker data to file.
        if(recordMarkerDataToFile && hasScriptGeneratedAnkleAngleDataYetFlag)
        {
            storeViconMarkerDataInDataRecorderObject();
        }
    }



    // This function reconstructs all missing markers for each rigid body specified in the passed in string array. 
    // The result is stored in an instance variable. 
    private List<string> reconstructMissingMarkersOnListedRigidBodies(string[] rigidBodiesToReconstruct)
    {
        //instantiate the return parameter, a list of rigid bodies that were reconstructed this frame
        List<string> rigidBodiesThatCouldBeFullyReconstructedThisFrame = new List<string>();

        //for each rigid body on the reconstruction list 
        for (int rigidBodyIndex = 0; rigidBodyIndex < rigidBodiesToReconstruct.Length; rigidBodyIndex++)
        {
            //get the marker names on this rigid body
            string[] rigidBodyMarkerNames = rigidBodiesToReconstructMarkerNames[rigidBodyIndex];


            //pass the corresponding list of segment marker names on to the reconstruction functions
            var reconstructionResultTuple = reconstructMissingMarkersOnOneRigidBodyThisFrame(rigidBodyMarkerNames);

            if (reconstructionResultTuple.Item1 == true) //if reconstruction was successful
            {
                //note that the rigid body is fully available/reconstructed this frame 
                rigidBodiesThatCouldBeFullyReconstructedThisFrame.Add(rigidBodiesToReconstruct[rigidBodyIndex]);

                //store the reconstructed markers and their positions
                namesOfAllReconstructedMarkers.AddRange(reconstructionResultTuple.Item2);
                positionsOfAllReconstructedMarkers.AddRange(reconstructionResultTuple.Item3);
            }
            else //print a warning or error that the segment markers could not be reconstructed
            {
                string errorMessage = "Rigid body named " + rigidBodiesToReconstruct[rigidBodyIndex] +
                    " has occluded markers that could not be reconstructed.";
                printWarningOrErrorToConsoleIfDebugModeIsDefined(logAnErrorSpecifier, errorMessage);
            }

        }

        printLogMessageToConsoleIfDebugModeIsDefined("Number of reconstructed markers this frame = " +
            namesOfAllReconstructedMarkers.Count);

        //Now that we've finished reconstruction for all rigid bodies, mark which markers had to be reconstructed this frame
        for (int reconstructedMarkerIndex = 0; reconstructedMarkerIndex < namesOfAllReconstructedMarkers.Count; reconstructedMarkerIndex++)
        {
            int markerModelIndex = Array.IndexOf(namesOfAllMarkersInSkeleton, namesOfAllReconstructedMarkers[reconstructedMarkerIndex]);
            markerInSkeletonWasReconstructedThisFrameFlags[markerModelIndex] = true;

            //print reconstructed marker positions if debugging
            string logMessage = "Marker named " + namesOfAllReconstructedMarkers[reconstructedMarkerIndex] + " has reconstructed position (x,y,z): ( " +
                 positionsOfAllReconstructedMarkers[reconstructedMarkerIndex].x + ", " +
                 positionsOfAllReconstructedMarkers[reconstructedMarkerIndex].y + ", " +
                 positionsOfAllReconstructedMarkers[reconstructedMarkerIndex].z + " ) and actual position: (" +
                 markersInSkeletonXPositions[markerModelIndex] + ", " +
                 markersInSkeletonYPositions[markerModelIndex] + ", " +
                 markersInSkeletonZPositions[markerModelIndex] + " )";

            printLogMessageToConsoleIfDebugModeIsDefined(logMessage);
        }

        return rigidBodiesThatCouldBeFullyReconstructedThisFrame;

    }


    // For each segment (should be four: right and left shank, right and left foot), constructs a transformation from
    // the global Vicon frame to that segment's coordinate system. Each subfunction roughly follows Collin et al 2009,
    // with a minor adjustment to guarantee orthogonal axes (from ME281 lecture notes).
    private Matrix4x4[] getTransformationsFromViconGlobalFrameToSegmentFrames(string[] namesOfSegments) {

        //Instantiate return list, which will hold all of the transformations from Vicon frame to segment frame
        Matrix4x4[] transformationsFromViconToSegmentFramesArray = new Matrix4x4[namesOfSegments.Length];

        for(int segmentIndex = 0; segmentIndex < namesOfSegments.Length; segmentIndex++) //for each segment
        {
            string nameOfCurrentSegment = namesOfSegments[segmentIndex];
            Matrix4x4 transformationViconFrameToSegmentFrame = new Matrix4x4();

            if(nameOfCurrentSegment == rightShankRigidBodyName) //if we are getting the right shank transform
            {
                transformationViconFrameToSegmentFrame = constructTransformationMatrixFromViconFrameToRightShankLocalFrame();
            }
            else if (nameOfCurrentSegment == leftShankRigidBodyName) //if we are getting the left shank transform
            {
                transformationViconFrameToSegmentFrame = constructTransformationMatrixFromViconFrameToLeftShankLocalFrame();
            }
            else if (nameOfCurrentSegment == rightFootRigidBodyName) //if we are getting the right ankle transform
            {
                transformationViconFrameToSegmentFrame = constructTransformationMatrixFromViconFrameToRightFootLocalFrame();
            }
            else if (nameOfCurrentSegment == leftFootRigidBodyName) //if we are getting the left ankle transform
            {
                transformationViconFrameToSegmentFrame = constructTransformationMatrixFromViconFrameToLeftFootLocalFrame();
            }

            //store the transformation matrix
            transformationsFromViconToSegmentFramesArray[segmentIndex] = transformationViconFrameToSegmentFrame;
        }

        return transformationsFromViconToSegmentFramesArray;
    }



    // Function to construct right shank local frame.
    // Note, the y-axis is aligned with the two malleolus markers
    // (instead of the two knee markers) to adjust for tibial torsion,
    // since we're concerned with ankle angle and not knee angle. 
    private Matrix4x4 constructTransformationMatrixFromViconFrameToRightShankLocalFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 rightKneeMedial) = getMarkerPositionAsVectorByName(rightKneeMedialMarkerName);
        (_,Vector3 rightKneeLateral) = getMarkerPositionAsVectorByName(rightKneeMarkerName);
        (_, Vector3 rightAnkleMedial) = getMarkerPositionAsVectorByName(rightAnkleMedialMarkerName);
        (_, Vector3 rightAnkleLateral) = getMarkerPositionAsVectorByName(rightAnkleMarkerName);
        Vector3 rightKneeJointCenter = (rightKneeMedial + rightKneeLateral) / 2;
        Vector3 rightAnkleJointCenter = (rightAnkleMedial + rightAnkleLateral) / 2;

        // Define the coordinate system origin and axes
        // Note that since we're concerned with the ankle angle, the
        // Y-axis will be aligned with the axis between the two malleolus markers to
        // account for tibial torsion (torsion of the rigid body itself) along the length of the shank.
        Vector3 positionOfLocalFrameOriginInViconCoordinates = rightKneeJointCenter;
        Vector3 localFrameZAxis = rightKneeJointCenter - rightAnkleJointCenter; //positive superiorly
        Vector3 localFrameXAxis = getRightHandedCrossProduct(localFrameZAxis, (rightAnkleLateral - rightAnkleMedial)); //positive anteriorly. (rightKneeJointCenter - rightAnkleLateral) instead of Z-axis? Collins et al 2009 vs. ME281...
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



    // Function to construct left shank local frame.
    // Note, the y-axis is aligned with the two malleolus markers
    // (instead of the two knee markers) to adjust for tibial torsion,
    // since we're concerned with ankle angle and not knee angle. 
    private Matrix4x4 constructTransformationMatrixFromViconFrameToLeftShankLocalFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 leftKneeMedial) = getMarkerPositionAsVectorByName(leftKneeMedialMarkerName);
        (_, Vector3 leftKneeLateral) = getMarkerPositionAsVectorByName(leftKneeMarkerName);
        (_, Vector3 leftAnkleMedial) = getMarkerPositionAsVectorByName(leftAnkleMedialMarkerName);
        (_, Vector3 leftAnkleLateral) = getMarkerPositionAsVectorByName(leftAnkleMarkerName);
        Vector3 leftKneeJointCenter = (leftKneeMedial + leftKneeLateral) / 2;
        Vector3 leftAnkleJointCenter = (leftAnkleMedial + leftAnkleLateral) / 2;

        // Define the coordinate system origin and axes
        // Note that since we're concerned with the ankle angle, the
        // Y-axis will be aligned with the axis between the two malleolus markers to
        // account for tibial torsion (torsion of the rigid body itself) along the length of the shank.
        Vector3 positionOfLocalFrameOriginInViconCoordinates = leftKneeJointCenter;
        Vector3 localFrameZAxis = leftKneeJointCenter - leftAnkleJointCenter; //positive superiorly
        Vector3 localFrameXAxis = getRightHandedCrossProduct(localFrameZAxis, (leftAnkleMedial - leftAnkleLateral)); //positive anteriorly. should we use (rightKneeJointCenter - rightAnkleLateral) instead of crossing with z-axis? Collins et al 2009 vs. ME281...
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



    //Function to construct right foot local frame
    private Matrix4x4 constructTransformationMatrixFromViconFrameToRightFootLocalFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 rightAnkleMedial) = getMarkerPositionAsVectorByName(rightAnkleMedialMarkerName);
        (_, Vector3 rightAnkleLateral) = getMarkerPositionAsVectorByName(rightAnkleMarkerName);
        (_, Vector3 rightMT1) = getMarkerPositionAsVectorByName(rightFirstMetatarsalMarkerName);
        (_, Vector3 rightMT5) = getMarkerPositionAsVectorByName(rightFifthMetatarsalMarkerName);
        Vector3 rightAnkleJointCenter = (rightAnkleMedial + rightAnkleLateral) / 2;
        Vector3 rightMidForeFoot = (rightMT1 + rightMT5) / 2;

        // Define the coordinate system origin and axes
        Vector3 positionOfLocalFrameOriginInViconCoordinates = rightAnkleJointCenter;
        Vector3 localFrameZAxis = rightAnkleJointCenter - rightMidForeFoot; //positive posteriorly
        Vector3 localFrameXAxis = getRightHandedCrossProduct(localFrameZAxis, (rightMT5 - rightMT1)); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
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



    //Function to construct left foot local frame
    private Matrix4x4 constructTransformationMatrixFromViconFrameToLeftFootLocalFrame()
    {
        //Get positions for all the markers we will use to construct the local frame
        (_, Vector3 leftAnkleMedial) = getMarkerPositionAsVectorByName(leftAnkleMedialMarkerName);
        (_, Vector3 leftAnkleLateral) = getMarkerPositionAsVectorByName(leftAnkleMarkerName);
        (_, Vector3 leftMT1) = getMarkerPositionAsVectorByName(leftFirstMetatarsalMarkerName);
        (_, Vector3 leftMT5) = getMarkerPositionAsVectorByName(leftFifthMetatarsalMarkerName);
        Vector3 leftAnkleJointCenter = (leftAnkleMedial + leftAnkleLateral) / 2;
        Vector3 leftMidForeFoot = (leftMT1 + leftMT5) / 2;

        // Define the coordinate system origin and axes
        Vector3 positionOfLocalFrameOriginInViconCoordinates = leftAnkleJointCenter;
        Vector3 localFrameZAxis = leftAnkleJointCenter - leftMidForeFoot; //positive posteriorly
        Vector3 localFrameXAxis = getRightHandedCrossProduct(localFrameZAxis, (leftMT1 - leftMT5)); //orthogonal to transverse plane of foot, positive superiorly. (rightAnkleLateral - rightMT5) instead of Z-axis? Collins et al 2009 vs. ME281...
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



    private Matrix4x4 getTransformationFromRightShankToRightFoot(Matrix4x4[] allSegmentTransformsViconToSegment)
    {
        // Get transforms for the segments
        Matrix4x4 ViconToRightShankTransformation = allSegmentTransformsViconToSegment[Array.IndexOf(segmentNames, rightShankRigidBodyName)];
        Matrix4x4 ViconToRightFootTransformation = allSegmentTransformsViconToSegment[Array.IndexOf(segmentNames, rightFootRigidBodyName)];

        //Get transform from shank to foot = (Vicon global to foot) * (Shank to Vicon global)
        Matrix4x4 rightShankToRightFootTransform = ViconToRightFootTransformation * ViconToRightShankTransformation.inverse;

        return rightShankToRightFootTransform;
    }



    private Matrix4x4 getTransformationFromLeftShankToLeftFoot(Matrix4x4[] allSegmentTransformsViconToSegment)
    {
        // Get transforms for the segments
        Matrix4x4 ViconToLeftShankTransformation = allSegmentTransformsViconToSegment[Array.IndexOf(segmentNames, leftShankRigidBodyName)];
        Matrix4x4 ViconToLeftFootTransformation = allSegmentTransformsViconToSegment[Array.IndexOf(segmentNames, leftFootRigidBodyName)];

        //Get transform from shank to foot = (Vicon global to foot) * (Shank to Vicon global)
        Matrix4x4 leftShankToRightFootTransform = ViconToLeftFootTransformation * ViconToLeftShankTransformation.inverse;

        return leftShankToRightFootTransform;
    }



    private void resetVariablesNeededForNextFrame()
    {
        //Clear any lists that are filled dynamically on each update() call
        namesOfAllReconstructedMarkers.Clear();
        positionsOfAllReconstructedMarkers.Clear();

        // Clear any arrays that need to be reset
        Array.Clear(markersInSkeletonOcclusionStatus, 0, markersInSkeletonOcclusionStatus.Length);
        Array.Clear(markerInSkeletonWasReconstructedThisFrameFlags, 0, markerInSkeletonWasReconstructedThisFrameFlags.Length);
    }


    //END: Functions called by Update()***********************************************************************************************







    //START: Public access getter/setter functions***********************************************************************************************


    public bool getAnkleAngleManagerReadyStatus()
    {
        return hasScriptGeneratedAnkleAngleDataYetFlag;
    }

    //Get the ankle angles for the right, left ankles. 
    //Should only be called once data is ready (once setup is complete).
    public (Vector3, Vector3) getAnkleAngles()
    {
        return (rightShankToRightFootEulerAngles, leftShankToLeftFootEulerAngles);
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



    private float getAverageDistanceBetweenTwoMarkersInSetupFramesByName(string marker1Name, string marker2Name)
    {
        //get the position of the marker of interest within our setup frames array
        int marker1IndexInEachArray = Array.IndexOf(namesOfAllMarkersInSkeleton, marker1Name);
        int marker2IndexInEachArray = Array.IndexOf(namesOfAllMarkersInSkeleton, marker2Name);

        //for each setup frame, get the marker positions and find the distance between the two markers
        float distanceBetweenMarkers = 0;
        for (int setupFrameIndex = 0; setupFrameIndex < setupMarkerFramesXPos.Count; setupFrameIndex++)
        {
            Vector3 marker1Position;
            Vector3 marker2Position;

            //get marker 1 position for this frame
            marker1Position = new Vector3(setupMarkerFramesXPos[setupFrameIndex][marker1IndexInEachArray],
                setupMarkerFramesYPos[setupFrameIndex][marker1IndexInEachArray],
                setupMarkerFramesZPos[setupFrameIndex][marker1IndexInEachArray]);

            //get marker 2 position for this frame
            marker2Position = new Vector3(setupMarkerFramesXPos[setupFrameIndex][marker2IndexInEachArray],
                setupMarkerFramesYPos[setupFrameIndex][marker2IndexInEachArray],
                setupMarkerFramesZPos[setupFrameIndex][marker2IndexInEachArray]);

            //Get distance between markers and add it to running sum
            distanceBetweenMarkers = distanceBetweenMarkers + (marker2Position - marker1Position).magnitude;
        }

        //take the mean of the distances between the two markers
        distanceBetweenMarkers = distanceBetweenMarkers / setupMarkerFramesXPos.Count;
        return distanceBetweenMarkers;
    }




    private (bool, Vector3) getMarkerPositionAsVectorByName(string markerName)
    {
        bool isMarkerAvailable;
        int markerIndex = Array.IndexOf(namesOfAllMarkersInSkeleton, markerName);

        Vector3 requestedMarkerPosition = new Vector3(0.0f, 0.0f, 0.0f);

        if (markerIndex >= 0) //if the marker is a physical marker in the model-specific marker array
        {
            if (markersInSkeletonOcclusionStatus[markerIndex] != true) //if the marker is visible this frame
            {
                //find and return the marker position as a Vector3
                isMarkerAvailable = true;
                return (isMarkerAvailable, new Vector3(markersInSkeletonXPositions[markerIndex], markersInSkeletonYPositions[markerIndex],
                markersInSkeletonZPositions[markerIndex]));
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
                    return (isMarkerAvailable, requestedMarkerPosition);
                }
            }
        }
        else //if the marker is occluded and cannot be reconstructed
        {
            isMarkerAvailable = false; //set the flag indicating the marker is not available
            return (isMarkerAvailable, requestedMarkerPosition);
        }

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



    public void ankleManagerStartStoringMarkerDataToFile()
    {
        // Set the store marker data flag to true, if the ankle management script (this script)
        // is no longer in the Setup stage.
        if (setupComplete)
        {
            // Set flag to true to start storing marker data whenever it's fresh
            recordMarkerDataToFile = true;
        }

    }

    public void ankleManagerStopStoringMarkerDataToFile()
    {
        // Set the store marker data flag to false, if the ankle management script (this script)
        // is no longer in the Setup stage.
        if (setupComplete)
        {
            // Set flag to stop storing marker data
            recordMarkerDataToFile = false;
        }
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


    //The highest level function used for marker reconstruction. Accepts a list of markers on a rigid body and reconstructs any missing ones,
    //assuming there are enough visible markers for reconstruction.
    private (bool, string[], Vector3[]) reconstructMissingMarkersOnOneRigidBodyThisFrame(string[] markersOnRigidBodyNames)
    {

        List<string> markersVisibleNames = new List<string>();
        List<string> markersOccludedNames = new List<string>();
        //first, figure out which markers on the segment are visible and which are occluded.
        for (uint markerIndex = 0; markerIndex < markersOnRigidBodyNames.Length; markerIndex++) //for each marker on the segment in question
        {
            bool markerOccluded = markersInSkeletonOcclusionStatus[Array.IndexOf(namesOfAllMarkersInSkeleton, markersOnRigidBodyNames[markerIndex])];

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
            positionsOfReconstructedMarkers = reconstructionResultTuple.Item2;
        }
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
        Vector3[] positionsOfMissingMarkersInLocalFrameInSetup = transformPositionsToNewCoordinateFrame(markersOccludedNames, namesOfAllMarkersInSkeleton,
            averagePositionOfModelMarkersInStartupFrames, transformationMatrixViconToLocalInSetup);

        //find the rotation matrix from the local coordinate system to global in the current frame
        Matrix4x4 transformationMatrixLocalToViconThisFrame = constructRotationFromLocalToViconThisFrame(markersVisibleNames);

        //finally, transform the missing marker coordinates from local frame to global frame
        Vector3[] reconstructedMissingMarkerPositionsInViconFrame = transformPositionsToNewCoordinateFrame(markersOccludedNames, markersOccludedNames.ToArray(),
            positionsOfMissingMarkersInLocalFrameInSetup, transformationMatrixLocalToViconThisFrame);

        //DEBUG ONLY 
        //print reconstructed positions
        //Vector3 firstReconstructedMarker = reconstructedMissingMarkerPositionsInViconFrame[0];
        //Debug.Log("First reconstructed marker position with name " + markersOccludedNames[0] + " is (x,y,z): ( " + firstReconstructedMarker.x + ", " + firstReconstructedMarker.y + ", " + firstReconstructedMarker.z + " )");

        //Vector3 secondReconstructedMarker = reconstructedMissingMarkerPositionsInViconFrame[1];
        //Debug.Log("Second reconstructed marker position with name " + markersOccludedNames[1] + " is (x,y,z): ( " + secondReconstructedMarker.x + ", " + secondReconstructedMarker.y + ", " + secondReconstructedMarker.z + " )");

        //return the names of the reconstructed markers and their positions 
        return (markersOccludedNames.ToArray(), reconstructedMissingMarkerPositionsInViconFrame);

    }



    private Matrix4x4 constructRotationFromViconToLocalInSetupFrame(List<string> markersVisibleNames)
    {
        //get all needed marker positions FROM THE SETUP AVERAGED FRAMES!
        Vector3 marker1Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(namesOfAllMarkersInSkeleton, markersVisibleNames[0])]; ;
        Vector3 marker2Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(namesOfAllMarkersInSkeleton, markersVisibleNames[1])];
        Vector3 marker3Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(namesOfAllMarkersInSkeleton, markersVisibleNames[2])];

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
        }

        return transformedPositions;
    }

    //End reconstruction functions ********************************************************************************************




    // START: marker data storage functions ***********************************************************************************


    //The names of the headers (which will specify the names of the markers or segments in question) are 
    //specified here, called by the Start() function.
    private void setMarkerDataNaming()
    {
        // Marker occlusion status
        string[] markersInModelOcclusionStatusNames = appendStringToAllElementsOfStringArray(namesOfAllMarkersInSkeleton, "_OCCLUSION_STATUS");

        // Marker reconstructed with rigid body technique status
        string[] markersInModelReconstructionStatusNames = appendStringToAllElementsOfStringArray(namesOfAllMarkersInSkeleton, "_RECONSTRUCTED_STATUS");

        // Marker positions
        string[] markersInModelXNames = appendStringToAllElementsOfStringArray(namesOfAllMarkersInSkeleton, "_X");
        string[] markersInModelYNames = appendStringToAllElementsOfStringArray(namesOfAllMarkersInSkeleton, "_Y");
        string[] markersInModelZNames = appendStringToAllElementsOfStringArray(namesOfAllMarkersInSkeleton, "_Z");

        //Since we only do this once, we're not concerned about speed. Add all of the string arrays to a list of strings, 
        //then convert the finalized list back to an array of strings.
        List<string> csvHeaderNamesAsList = new List<string>();
        csvHeaderNamesAsList.Add("MOST_RECENTLY_ACCESSED_VICON_FRAME_NUMBER");
        csvHeaderNamesAsList.Add("TIME_AT_UNITY_FRAME_START");
        csvHeaderNamesAsList.AddRange(markersInModelOcclusionStatusNames.ToList());
        csvHeaderNamesAsList.AddRange(markersInModelReconstructionStatusNames.ToList());
        csvHeaderNamesAsList.AddRange(markersInModelXNames);
        csvHeaderNamesAsList.AddRange(markersInModelYNames);
        csvHeaderNamesAsList.AddRange(markersInModelZNames);

        //now convert back to a string array, as that's what we use in the General Data Recorder object.
        string[] csvHeaderNames = csvHeaderNamesAsList.ToArray();

        //send the .csv file column header names to the General Data Recorder object
        generalDataRecorderScript.setCsvMarkerDataRowHeaderNames(csvHeaderNames);

    }

        private void storeViconMarkerDataInDataRecorderObject()
    {
        //Note: the names of the headers (which will specify the names of the markers or segments in question) are 
        //specified during setup.

        //create a list to store the floats
        List<float> markerDataToStore = new List<float>();

        //Vicon frame number and Unity frame time 
        markerDataToStore.Add((float)mostRecentlyAccessedViconFrameNumber);
        markerDataToStore.Add(Time.time);

        //marker occlusion status 
        markerDataToStore.AddRange(markersInSkeletonOcclusionStatus.Select(x => Convert.ToSingle(x)));

        // Whether or not the individual markers were reconstructed with the rigid body approach this frame
        markerDataToStore.AddRange(markerInSkeletonWasReconstructedThisFrameFlags.Select(x => Convert.ToSingle(x)));

        // Marker positions
        markerDataToStore.AddRange(markersInSkeletonXPositions);
        markerDataToStore.AddRange(markersInSkeletonYPositions);
        markerDataToStore.AddRange(markersInSkeletonZPositions);

        //send this frame of data to the General Data Recorder object to be stored on dynamic memory until it is written to file
        generalDataRecorderScript.storeRowOfMarkerData(markerDataToStore.ToArray());
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

