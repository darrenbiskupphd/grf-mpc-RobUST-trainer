// This script is supposed to be used to get the transformation from the Vicon frame to a Vive tracker that has been labeled with 
// Vicon markers. 
// We want to export the transformation from Vicon to Tracker frame. 

// KEY NOTE: STEAM VR does not have a guaranteed, fixed origin. 
// As a result, we have to define our games in Vicon frame, transform the game object coordinates to tracker frame (using the fixed transform computed here), 
// and then compute the tracker -> Unity transformation in real time to ensure that our real objects are always positioned properly in Unity.


//#define ENABLE_CUSTOM_WARNINGS_ERRORS //recommended that this define is always present so we can see user-defined warnings and errors
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

public class CreateAndSaveViconToUnityFrameTransformation : MonoBehaviour
{

    //key public game objects
    public GameObject generalDataRecorder; 
    public GameObject markerDataDistributor; //the GameObject that reads marker data each frame and has getter functions to get marker data by name. Accessses data via the dataStream prefab from the Unity/Vicon plugin.
    public SubjectInfoStorageScript subjectSpecificDataScript; //the script with getter functions for the subject-specific data
    public StructureMatrixSettingsScript structureMatrixSettingsScript;
    //public GameObject calibrationTracker; // the GameObject tethered to the Vive tracker position/orientation. Tethering is done with SteamVR TrackedObject Script.


    //key private game object scripts
    private bool usingViveFlag; // the settings enum that determines whether or not this script should run.
    private UnityVicon.ReadViconDataStreamScript markerDataDistributorScript; //the callable script of the GameObject that makes marker data available.
    private GeneralDataRecorder generalDataRecorderScript;

    // Set the name of this "task". This is just the name of the directory containing the data for the given subject. 
    // I.e. Subject# -> ThisTaskName -> Date -> OutputDataFile.csv
    private const string thisTaskName = "CalibrateViconAndVive";
    // Set the name of the output .csv file. 
    private const string outputFileName = "Vicon_Vive_Calibration_Data.csv";
    // Saving data naming
    private string[] csvHeaderNames;

    // States
    private string currentState;
    private const string setupStateString = "SETUP_STATE";
    private const string performComputationStateString = "COMPUTE_TRANSFORMATION_STATE";
    private const string gameOverStateString = "GAME_OVER";

    // Setup state 
    private bool storingFramesForSetupFlag; // whether or not we're still storing frames for setup.

    // Marker data access
    private uint mostRecentlyAccessedViconFrameNumber;

    // Markers
    private const string viveTrackerTopLeftMarkerName = "viveTrackerTopLeft";
    private const string viveTrackerTopRightMarkerName = "viveTrackerTopRight";
    private const string viveTrackerBottomCenterMarkerName = "viveTrackerBottomCenter";

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

    // Accessing transformation matrix
    private Matrix4x4 transformationTrackerToVicon; // the transformation matrix we're trying to compute with this script. 
                                                    // Transforms from the Vive reference tracker (mounted to RobUST's frame) 
                                                    // to the Vicon frame.
    private bool transformationTrackerToViconAvailableFlag; // a flag indicating whether or not the transformation ahs already been computed


    //debugging and testing 
    private const string logAMessageSpecifier = "LOG";
    private const string logAWarningSpecifier = "WARNING";
    private const string logAnErrorSpecifier = "ERROR";


    // Start is called before the first frame update
    void Start()
    {
        usingViveFlag = structureMatrixSettingsScript.GetUsingViveFlag();

        // This script only executes if we are using Vive
        if (usingViveFlag)
        {
            string verFramework = AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName;
            Debug.Log(".NET framework is: " + verFramework);

            // Get reference to the marker data distributor, which distributes data from the Vicon data stream
            markerDataDistributorScript = markerDataDistributor.GetComponent<UnityVicon.ReadViconDataStreamScript>();

            // Get a reference to the General Data Recorder script, which will save data to file. 
            // Specifically, it will store the file needed to construct the S matrix for each belt.
            generalDataRecorderScript = generalDataRecorder.GetComponent<GeneralDataRecorder>();

            // Choose all markers in the skeleton
            namesOfAllMarkersInSkeleton = new string[] { viveTrackerTopLeftMarkerName, viveTrackerTopRightMarkerName,
                viveTrackerBottomCenterMarkerName };

            // Initialize variables (Including arrays to store the skeleton marker occlusion status, position, etc.)
            initializeVariables();

            // Set the directory and file name for the output .CSV file, which will store all of the computed data
            SetOutputDataDirectoryAndFileName();

            // Set the variable naming (column names) for our output .CSV file, which will store all of the computed data
            SetOutputDataColumnNaming();

            // Since we still need to collect a few dozen marker frames for setup,
            //set the flag to true
            storingFramesForSetupFlag = true;

            // We start by setting up
            currentState = setupStateString;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // This script only executes if we are using Vive
        if (!usingViveFlag)
        {
            switch (currentState)
            {
                case setupStateString: // if we're still setting up
                    bool setupComplete = AttemptSetupAndStoreViconFrames();
                    if (setupComplete == true)
                    {
                        Debug.Log("Transitioning from setup state to the main perform computation state.");
                        // Switch to the perform computation state
                        currentState = performComputationStateString;
                    }
                    break;
                case performComputationStateString: // if setup is complete and we're ready to compute the transformation
                                                    // Find the transformation matrix from the tracker to Vicon frame
                    Matrix4x4 transformationTrackerToVicon = GetTransformationMatrixFromTrackerFrameToViconFrame();

                    // Write the transformation matrix to file
                    StoreDataForTrackerToViconTransformationInDataRecorderObject(transformationTrackerToVicon);

                    // Set the flag indicating the transformation is available
                    transformationTrackerToViconAvailableFlag = true;

                    // Then, tell the General Data Recorder to write the data to file
                    generalDataRecorderScript.writeExcursionPerformanceSummaryToFile();

                    // Switch to a dead, game over state
                    currentState = gameOverStateString;
                    break;
            }
        }
    }

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
    //2.) Store startup frames so we can use averaged data for our computations.
    //Returns: a boolean indicating whether or not setup is complete
    private bool AttemptSetupAndStoreViconFrames()
    {
        //initialize the return value as false (setup is not complete)
        bool setupComplete = false;

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
        generalDataRecorderScript.setCsvTrialDataSubdirectoryName(subdirectoryString);

        // 4.) Set the output file name. We'll always use the same one (each day) for the structure matrix data we have computed
        // Note: we use the "Excursion Performance Summary" functions of the GeneralDataRecorder object to store our data in this script.
        generalDataRecorderScript.setCsvTrialDataFileName(outputFileName);
    }


    //The names of the headers (which will specify the names of the markers or segments in question) are 
    //specified here, called by the Start() function.
    private void SetOutputDataColumnNaming()
    {
        // Store column names for transformation matrix elements
        csvHeaderNames = new string[] { "T11", "T12", "T13", "T14",
                                                       "T21", "T22", "T23", "T24",
                                                       "T31", "T32", "T33", "T34",
                                                       "T41", "T42", "T43", "T44"};

        //send the .csv file column header names to the General Data Recorder object
        // Note: we use the GeneralDataRecorder object's "Excursion Performance Summary" functions to output our data, even though the
        // name no longer matches the current objective.
        generalDataRecorderScript.setCsvTrialDataRowHeaderNames(csvHeaderNames);

    }

    // END: marker data storage functions ***********************************************************************************



    //START: Functions called to access marker data************************************************************************************


    //Gets occlusion status and position for all markers needed in our current COM model.
    //Results are stored in instance variables.
    private bool getMarkerDataForAllMarkersNeededInSkeleton(string[] listOfMarkerNamesInModel)
    {
        //make a copy, in memory (not by reference), of the current marker positions. 
        // We use these as a way to see if the marker data is fresh.
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


    //END: Functions called to access marker data************************************************************************************




    //START: Update() loop functions************************************************************************************

    private Matrix4x4 GetTransformationMatrixFromTrackerFrameToViconFrame()
    {
        // 1.) Get the unit vectors defining the rotation matrix
        // Get x-axis vector 
        Vector3 trackerXAxisInViconFrame = getMarkerAveragePositionInStartupFramesByName(viveTrackerTopLeftMarkerName) -
            getMarkerAveragePositionInStartupFramesByName(viveTrackerTopRightMarkerName);

        // Get z-axis vector (TEMP, since we need to make sure it's orthogonal later)
        Vector3 topMarkersMidPoint = (getMarkerAveragePositionInStartupFramesByName(viveTrackerTopLeftMarkerName) +
            getMarkerAveragePositionInStartupFramesByName(viveTrackerTopRightMarkerName)) / 2.0f;
        Vector3 trackerZAxisInViconFrame = topMarkersMidPoint -
            getMarkerAveragePositionInStartupFramesByName(viveTrackerBottomCenterMarkerName);

        // Get y-axis vector 
        Vector3 trackerYAxisInViconFrame = getRightHandedCrossProduct(trackerZAxisInViconFrame, trackerXAxisInViconFrame);

        // Recompute an orthogonal z-vector
        trackerZAxisInViconFrame = getRightHandedCrossProduct(trackerXAxisInViconFrame, trackerYAxisInViconFrame);

        // Normalize all 3 unit vectors 
        trackerXAxisInViconFrame = trackerXAxisInViconFrame / trackerXAxisInViconFrame.magnitude;
        trackerYAxisInViconFrame = trackerYAxisInViconFrame / trackerYAxisInViconFrame.magnitude;
        trackerZAxisInViconFrame = trackerZAxisInViconFrame / trackerZAxisInViconFrame.magnitude;

        // 2.) Compute the translation vector from Vicon to the tracker frame
        // Get the approximate origin
        Vector3 trackerOriginInViconFrame = (topMarkersMidPoint + getMarkerAveragePositionInStartupFramesByName(viveTrackerBottomCenterMarkerName)) / 2.0f;
        // Compute the offset vector from the computed approximate origin to the true origin in the tracker frame
        Vector3 offsetToTrueOriginTrackerFrame = new Vector3(0.0f, -42.3f, 0.0f); // units = millimeters (the units of Vicon)
        // Get a transform that represents ONLY THE ROTATION between Vicon and tracker frame 
        Matrix4x4 rotationTrackerToVicon = getRotationOnlyTransformationMatrix(trackerXAxisInViconFrame, trackerYAxisInViconFrame, trackerZAxisInViconFrame);
        // Apply the offset by rotating it into Vicon frame and adding it to the Vicon frame origin approximation
        // NOTE: Matrix4x4.MultiplyVector only applies the rotation anyway.
        Vector3 offsetToTrueOriginViconFrame = rotationTrackerToVicon.MultiplyVector(offsetToTrueOriginTrackerFrame);
        // Adjust the origin in Vicon frame
        trackerOriginInViconFrame = trackerOriginInViconFrame + offsetToTrueOriginViconFrame;
        Debug.Log("Computed translation: (x,y,z): (" + trackerOriginInViconFrame.x + ", " +
            trackerOriginInViconFrame.y + ", " + trackerOriginInViconFrame.z + ")");

        // 3.) Store the rotation matrix and translation as a transformation matrix (matrix4x4)
        Matrix4x4 transformationTrackerToVicon = 
            getTransformationMatrix(trackerXAxisInViconFrame, trackerYAxisInViconFrame, trackerZAxisInViconFrame, trackerOriginInViconFrame);

        // Return 
        return transformationTrackerToVicon;
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


    //Given the three normalized/unit axes of a local coordinate system and the translation FROM the target coordinate system
    //TO the local coordinate system defined in the target coordinate system, construct a transformation matrix
    //that will transform points in the local coordinate system to the target coordinate system
    private Matrix4x4 getRotationOnlyTransformationMatrix(Vector3 xAxisVector, Vector3 yAxisVector, Vector3 zAxisVector)
    {
        Matrix4x4 transformationMatrixLocalToTarget = new Matrix4x4();


        //fill the columns of the transformation matrix
        transformationMatrixLocalToTarget.SetColumn(0, new Vector4(xAxisVector.x, xAxisVector.y,
            xAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(1, new Vector4(yAxisVector.x, yAxisVector.y,
            yAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(2, new Vector4(zAxisVector.x, zAxisVector.y,
            zAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(3, new Vector4(0,
            0, 0, 1)); //last element is one

        return transformationMatrixLocalToTarget;
    }



    private void StoreDataForTrackerToViconTransformationInDataRecorderObject(Matrix4x4 transformationMatrix)
    {
        //Note: the names of the headers (which will specify the names of the markers or segments in question) are 
        //specified during setup.

        //create a list to store the floats
        float[] computedDataToStore = new float[]{
            transformationMatrix[0, 0], transformationMatrix[0, 1], transformationMatrix[0, 2], transformationMatrix[0, 3], 
            transformationMatrix[1, 0], transformationMatrix[1, 1], transformationMatrix[1, 2], transformationMatrix[1, 3], 
            transformationMatrix[2, 0], transformationMatrix[2, 1], transformationMatrix[2, 2], transformationMatrix[2, 3], 
            transformationMatrix[3, 0], transformationMatrix[3, 1], transformationMatrix[3, 2], transformationMatrix[3, 3], 
        };

        //send this frame of data to the General Data Recorder object to be stored on dynamic memory until it is written to file
        generalDataRecorderScript.storeRowOfTrialData(computedDataToStore);
    }



    //END: Update() loop functions************************************************************************************


    // START: public functions***********************************************************************************************************


    public (bool, Matrix4x4) GetTransformationViveRefTrackerToViconFrame()
    {
        // If the transformation matrix is available
        if(transformationTrackerToViconAvailableFlag == true)
        {
            // Return a true indicating the transformation matrix is available, 
            // and return the transformation matrix
            return (true, transformationTrackerToVicon);
        }
        else
        {
            // Return a false indicating the transformation matrix is not available yet
            return (false, new Matrix4x4());
        }
    }






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

    //END: Debugging functions ********************************************************************************************************

}
