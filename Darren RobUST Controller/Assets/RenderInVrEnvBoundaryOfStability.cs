using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System;
using UnityEngine;

public class RenderInVrEnvBoundaryOfStability : MonoBehaviour
{
    // center of mass manager
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.

    // level manager
    private GameObject levelManager; //the level manager for the current task
    private LevelManagerScriptAbstractClass levelManagerScript; // the script of the level manager. Part of an abstract class.
    // mapping function from Vicon to Unity. This function is task-dependent (i.e. differnet for excursion, circle-trace, etc.)
    //public delegate Vector3 MappingFunctionFromViconFrameToUnityFrame(Vector3 coordinateInViconFrame); //accepts a Vector3 in Vicon frame, returns the Vector3 in Unity frame
    //MappingFunctionFromViconFrameToUnityFrame mappingFunctionViconToUnityFrameInstance; 

    //the constant part of an excursion performancec summary file name
    private const string excursionPerformanceSummaryPrefix = "Excursion_Performance_Summary";

    //store the Vicon-frame (real world) excursion distances of the control point (returned by this script's getter functions), 
    // and of the COM and chest.
    float[] controlPointExcursionLimitsPerDirection;
    float[] comExcursionLimitsPerDirection = new float[8]; // 8 = number of postural star directions
    float[] chestExcursionLimitsPerDirection = new float[8]; // 8 = number of postural star directions

    //store the Unity-frame excursion bounds so that other parts of the program can retrieve them
    private List<Vector3> maxExcursionsEachDirection; //ordered as +x-axis first, proceeding counterclockwise

    // The line renderer that stores the points visualizing the functional boundary of stability
    private LineRenderer boundaryLineRenderer;

    public bool toggleBoundaryOnOff;
    private bool lastToggleBoundaryState;

    // We store the excursion limits in Vicon frame coordinates.
    // We may need to send them to the force field robot.
    private List<Vector3> excursionDistancesInViconFrame;

    // Control point settings
    public UniversalExperimentSettings experimentSettingsScript;

    // Start is called before the first frame update
    void Start()
    {
        // Marker data and center of mass manager
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        // Get level manager via its tag. 
        GameObject[] levelManagerArray = GameObject.FindGameObjectsWithTag("LevelManager");
        levelManager = levelManagerArray[0]; // we assume there's only one level manager, in the first (0) index

        // Get the script inside of the level manager. All level managers should have a script 
        // with the abstract class LevelManagerScriptAbstractClass, from which we can call the mapping function
        levelManagerScript = levelManager.GetComponent<LevelManagerScriptAbstractClass>();

        // Store a reference to the line renderer
        boundaryLineRenderer = GetComponent<LineRenderer>();

        // Track the toggle on/off boolean state
        lastToggleBoundaryState = toggleBoundaryOnOff;
    }

    // Update is called once per frame
    void Update()
    {
        if (toggleBoundaryOnOff != lastToggleBoundaryState)
        {
            // toggle the boundary of support line renderer from active to inactive or vice versa.
            boundaryLineRenderer.enabled = !boundaryLineRenderer.enabled;

            // Update the last toggle boundary state
            lastToggleBoundaryState = toggleBoundaryOnOff;
        }
    }


    //BEGIN: getter functions*************************************************************************************

    public (Vector3, Vector3) getForwardAndBackwardExcursionEndpoints()
    {
        return (maxExcursionsEachDirection[2], maxExcursionsEachDirection[6]);
    }

    public float[] getExcursionDistancesInViconCoordinates()
    {
        return controlPointExcursionLimitsPerDirection;
    }

    // Returns excursion limits with units = mm
    public List<Vector3> getExcursionLimitsPerDirectionWithProperSign()
    {
        return computeExcursionLimitsPerDirectionWithProperSign();
    }

    private List<Vector3> computeExcursionLimitsPerDirectionWithProperSign()
    {
        //Assumptions: the x-axis is oriented along the person's "left/right" axis (mediolateral). 
        // The y-axis is oriented along the person's "front/back" axis (anteroposterior). 

        // get the edges of the base of support from the center of mass manager, as the boundary of stability excursions are measured from its center.
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
        float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();

        // Get the sign conventions (i.e. is "right" positive along the x-axis?)
        float rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportXPos - leftEdgeBaseOfSupportXPos);
        float forwardsSign = Mathf.Sign(frontEdgeBaseOfSupportYPos - backEdgeBaseOfSupportYPos);
        float fortyFiveDegreesInRadians = Mathf.PI / 4;

        //compute the center of the base of support
        Vector3 centerOfBaseOfSupportViconCoords = new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos) / 2.0f, (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f, 5.0f);

        // compute the eight excursion points
        // first, the cardinal directions, keeping in mind that Vicon x-axis can be flipped relative to 
        // Unity x-axis.
        Vector3 rightwardExcursionDistanceViconUnits = new Vector3(rightwardsSign * controlPointExcursionLimitsPerDirection[0], 0.0f, 0.0f);
        Vector3 forwardExcursionDistanceViconUnits = new Vector3(0.0f, forwardsSign * controlPointExcursionLimitsPerDirection[2], 0.0f);
        Vector3 leftwardExcursionDistanceViconUnits = new Vector3(-rightwardsSign * controlPointExcursionLimitsPerDirection[4], 0.0f, 0.0f);
        Vector3 backwardExcursionDistanceViconUnits = new Vector3(0.0f, -forwardsSign * controlPointExcursionLimitsPerDirection[6], 0.0f);
        //then the diagonal directions
        Vector3 forwardRightExcursionDistanceViconUnits = new Vector3(rightwardsSign * controlPointExcursionLimitsPerDirection[1] * Mathf.Cos(fortyFiveDegreesInRadians),
             forwardsSign * controlPointExcursionLimitsPerDirection[1] * Mathf.Sin(fortyFiveDegreesInRadians), 0.0f);
        Vector3 forwardLeftExcursionDistanceViconUnits = new Vector3(-rightwardsSign * controlPointExcursionLimitsPerDirection[3] * Mathf.Cos(fortyFiveDegreesInRadians),
            forwardsSign * controlPointExcursionLimitsPerDirection[3] * Mathf.Sin(fortyFiveDegreesInRadians), 0.0f);
        Vector3 backwardLeftExcursionDistanceViconUnits = new Vector3(-rightwardsSign * controlPointExcursionLimitsPerDirection[5] * Mathf.Cos(fortyFiveDegreesInRadians),
            -forwardsSign * controlPointExcursionLimitsPerDirection[5] * Mathf.Sin(fortyFiveDegreesInRadians), 0.0f);
        Vector3 backwardRightExcursionDistanceViconUnits = new Vector3(rightwardsSign * controlPointExcursionLimitsPerDirection[7] * Mathf.Cos(fortyFiveDegreesInRadians),
            -forwardsSign * controlPointExcursionLimitsPerDirection[7] * Mathf.Sin(fortyFiveDegreesInRadians), 5.0f);

        // Package them as desired
        List<Vector3> excursionDistancesXAndYWithSigns = new List<Vector3> { rightwardExcursionDistanceViconUnits,
            forwardRightExcursionDistanceViconUnits, forwardExcursionDistanceViconUnits,
        forwardLeftExcursionDistanceViconUnits, leftwardExcursionDistanceViconUnits,
            backwardLeftExcursionDistanceViconUnits, backwardExcursionDistanceViconUnits,
        backwardRightExcursionDistanceViconUnits};

        return excursionDistancesXAndYWithSigns;

    }



    //END: getter functions*************************************************************************************

    //public void setMappingFunctionFromViconToUnity(MappingFunctionFromViconFrameToUnityFrame mappingFunction)
    //{
    //mappingFunctionViconToUnityFrameInstance = mappingFunction;
    // uint test = 0;
    //}


    // This function tells this game object to load the boundary of stability. It must be called
    // by the level manager, which knows the subject number and other details
    // specifying the path to save to/load from.
    public void loadBoundaryOfStability(string pathToDirectoryWithFile, string keyword = "")
    {
        Debug.Log("Loading boundary of stability data from local path: " + pathToDirectoryWithFile);

        //load the excursion limits for the current subject, if available. 
        float[] allSegmentExcursionLimitsPerDirection = loadExcursionLimits(pathToDirectoryWithFile, keyword);

        // Store the by-segment excursion limits (e.g., COM and chest), and assign
        // the proper segment limits depending on the control point type being used. 
        StoreComAndChestExcursionLimits(allSegmentExcursionLimitsPerDirection);
    }



    // This function is called by the level manager and tells this object to render the 
    // functional boundary of stability. It must be called by the level manager
    // after the level manager has specified the mapping from Vicon frame to Unity frame.
    public void renderBoundaryOfStability()
    {
        Debug.Log("Render boundary of stability called.");
        //draw the boundary
        drawBoundaryOfStability();

    }


    private void StoreComAndChestExcursionLimits(float[] allSegmentExcursionLimitsPerDirection)
    {
        // The first 8 entries are the COM excursion limits
        for (int comIndex = 0; comIndex < 8; comIndex++)
        {
            comExcursionLimitsPerDirection[comIndex] = allSegmentExcursionLimitsPerDirection[comIndex];
        }
        // The entries 9-16 are the chest excursion limits
        for (int chestIndex = 0; chestIndex < 8; chestIndex++)
        {
            chestExcursionLimitsPerDirection[chestIndex] = allSegmentExcursionLimitsPerDirection[chestIndex + 8]; // chest excursions are stored in elements 8-15
        }

        // Get the current control point settings object
        controlPointEnum controlPointCurrentSetting = experimentSettingsScript.GetControlPointSettingsEnumObject();

        // Depending on the control point desired
        switch (controlPointCurrentSetting)
        {
            // If we're using the COM as the control point
            case controlPointEnum.COM:
                // Then store the COM excursion limits as the control point limits.
                controlPointExcursionLimitsPerDirection = comExcursionLimitsPerDirection;
                Debug.Log("Using COM excursion limits as control point limits: " + controlPointExcursionLimitsPerDirection);
                break;
            // Else if we're using the chest as the control point
            case controlPointEnum.Chest:
                // Then store the chest excursion limits as the control point limits.
                controlPointExcursionLimitsPerDirection = chestExcursionLimitsPerDirection;
                Debug.Log("Using chest excursion limits as control point limits: " + controlPointExcursionLimitsPerDirection);
                break;
        }
    }

    private float[] loadExcursionLimits(string localPathToFolder, string keyword)
    {
        // Get all files in the directory
        string pathToFolder = getDirectoryPath() + localPathToFolder;
        Debug.Log("Trying to load excursion performance data from the path: " + pathToFolder);
        string[] allFiles = System.IO.Directory.GetFiles(pathToFolder);
        Debug.Log("Loaded the following number of files from the specified path: " + allFiles.Length);

        // Get the name of the most recent excursion performance summary file (with a keyword, such as "No_Stim", if desired)
        string fileToUseName = "";
        DateTime dateTimeOfFileToUse = new DateTime();
        for (uint fileIndex = 0; fileIndex < allFiles.Length; fileIndex++)
        {
            //see if the file is an excursion performance summary file with the proper keywords
            string fileName = allFiles[fileIndex];
            bool fileNameOfAnExcursionPerformanceSummaryFile = fileName.Contains(excursionPerformanceSummaryPrefix);
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
            if (fileNameOfAnExcursionPerformanceSummaryFile && hasKeyword && !isMetaFile)
            {
                Debug.Log("Considering limits file name: " + fileName);
                if (fileName == (pathToFolder + "Excursion_Performance_Summary_Stim_Off.csv")) // if the file name is the default file name
                {
                    fileToUseName = fileName; // We only load limits from the "default" file name
                }
                /*                //if this is the first valid file we've found
                                if(fileToUseName == "")
                                {
                                    fileToUseName = fileName;
                                    dateTimeOfFileToUse = System.IO.File.GetCreationTime(fileName);
                                } else //if we have already found a file name
                                {
                                    DateTime dateTimeOfCurrentFile = System.IO.File.GetCreationTime(fileName);
                                    int isDateEarlierOrLater = DateTime.Compare(dateTimeOfCurrentFile, dateTimeOfFileToUse); 
                                    if(isDateEarlierOrLater > 0) //if the current file's date/time is the most recent one observed thus far
                                    {
                                        //store that one
                                        fileToUseName = fileName;
                                        dateTimeOfFileToUse = dateTimeOfCurrentFile;
                                    }
                                }*/
            }
        }

        Debug.Log("Loading from the following Excursion peformance summary file path: " + fileToUseName);

        // Now that we have the file to use, read it in
        string allFileTextString = System.IO.File.ReadAllText(fileToUseName);
        //split into lines, delimited by the newline character
        char[] separator = new char[] { '\n' };
        string[] rowsFromFile = allFileTextString.Split(separator, 2);
        //split first data row (second row) into cells/entries, delimited by commas
        separator = new char[] { ',' };
        string[] firstDataRow = rowsFromFile[1].Split(separator, 100);
        //Convert each string in the data row to a float
        float[] firstDataRowAsFloat = new float[firstDataRow.Length];
        for (uint entryIndex = 0; entryIndex < firstDataRow.Length; entryIndex++)
        {
            Debug.Log("String to parse into a float is: " + firstDataRow[entryIndex]);
            firstDataRowAsFloat[entryIndex] = float.Parse(firstDataRow[entryIndex], CultureInfo.InvariantCulture.NumberFormat);
        }

        //return the float array for the excursion performance summary
        return firstDataRowAsFloat;
    }


    private void drawBoundaryOfStability()
    {
        // get the edges of the base of support from the center of mass manager, as the boundary of stability excursions are measured from its center.
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
      float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();

        // compute the 8 points of maximum excursion in Unity coordinates.
        // Note, this requires some convention, so I've put everything into a function.
        maxExcursionsEachDirection = getPointsOfMaximumExcursionFromBosCenterAndExcursionDistances(leftEdgeBaseOfSupportXPos, rightEdgeBaseOfSupportXPos, frontEdgeBaseOfSupportYPos,
            backEdgeBaseOfSupportYPos, controlPointExcursionLimitsPerDirection);

        // Add a final point to the float array to "close" the boundary (so, last element is same as first element)
        maxExcursionsEachDirection.Add(maxExcursionsEachDirection[0]);


        //set the points of the boundary of stability as the points in our line renderer
        boundaryLineRenderer.positionCount = maxExcursionsEachDirection.Count;
        boundaryLineRenderer.SetPositions(maxExcursionsEachDirection.ToArray());

    }




    private List<Vector3> getPointsOfMaximumExcursionFromBosCenterAndExcursionDistances(float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
         float backEdgeBaseOfSupportYPos, float[] excursionDistancesPerDirection)
    {
        //Assumptions: the x-axis is oriented along the person's "left/right" axis (mediolateral). 
        // The y-axis is oriented along the person's "front/back" axis (anteroposterior). 

        // Get the sign conventions (i.e. is "right" positive along the x-axis?)
        float rightwardsSign = Mathf.Sign(rightEdgeBaseOfSupportXPos - leftEdgeBaseOfSupportXPos);
        float forwardsSign = Mathf.Sign(frontEdgeBaseOfSupportYPos - backEdgeBaseOfSupportYPos);
        float fortyFiveDegreesInRadians = Mathf.PI / 4;

        //compute the center of the base of support
        Vector3 centerOfBaseOfSupportViconCoords = new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos) / 2.0f, (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f, 5.0f);
        Debug.Log("Center of base of support, Vicon frame, in BoS renderer is (x,y): (" + centerOfBaseOfSupportViconCoords.x + ", " + centerOfBaseOfSupportViconCoords.y + ")");

        // compute the eight excursion points
        // first, the cardinal directions, keeping in mind that Vicon x-axis can be flipped relative to 
        // Unity x-axis.
        Vector3 rightwardMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x + rightwardsSign * excursionDistancesPerDirection[0], centerOfBaseOfSupportViconCoords.y, 5.0f);
        Debug.Log("Rightward max excursion point when drawing BoS in Vicon frame is (x,y): (" + rightwardMaxExcursionPointViconCoords.x + ", " + rightwardMaxExcursionPointViconCoords.y + ")");
        Debug.Log("Distance to rightward max excursion point from center, Vicon frame is: " + Vector3.Distance(rightwardMaxExcursionPointViconCoords, centerOfBaseOfSupportViconCoords) + " and should be: " + excursionDistancesPerDirection[0]);
        Vector3 forwardMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x, centerOfBaseOfSupportViconCoords.y + forwardsSign * excursionDistancesPerDirection[2], 5.0f);
        Vector3 leftwardMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x - rightwardsSign * excursionDistancesPerDirection[4], centerOfBaseOfSupportViconCoords.y, 5.0f);
        Vector3 backwardMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x, centerOfBaseOfSupportViconCoords.y - forwardsSign * excursionDistancesPerDirection[6], 5.0f);
        //then the diagonal directions
        Vector3 forwardRightDiagonalMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x + rightwardsSign * excursionDistancesPerDirection[1] * Mathf.Cos(fortyFiveDegreesInRadians),
            centerOfBaseOfSupportViconCoords.y + forwardsSign * excursionDistancesPerDirection[1] * Mathf.Sin(fortyFiveDegreesInRadians), 5.0f);
        Vector3 forwardLeftDiagonalMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x - rightwardsSign * excursionDistancesPerDirection[3] * Mathf.Cos(fortyFiveDegreesInRadians),
            centerOfBaseOfSupportViconCoords.y + forwardsSign * excursionDistancesPerDirection[3] * Mathf.Sin(fortyFiveDegreesInRadians), 5.0f);
        Vector3 backwardLeftDiagonalMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x - rightwardsSign * excursionDistancesPerDirection[5] * Mathf.Cos(fortyFiveDegreesInRadians),
            centerOfBaseOfSupportViconCoords.y - forwardsSign * excursionDistancesPerDirection[5] * Mathf.Sin(fortyFiveDegreesInRadians), 5.0f);
        Vector3 backwardRightDiagonalMaxExcursionPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x + rightwardsSign * excursionDistancesPerDirection[7] * Mathf.Cos(fortyFiveDegreesInRadians),
            centerOfBaseOfSupportViconCoords.y - forwardsSign * excursionDistancesPerDirection[7] * Mathf.Sin(fortyFiveDegreesInRadians), 5.0f);

        // Store the Vicon frame coordinates 
        excursionDistancesInViconFrame = new List<Vector3> { rightwardMaxExcursionPointViconCoords,
            forwardRightDiagonalMaxExcursionPointViconCoords, forwardMaxExcursionPointViconCoords,
        forwardLeftDiagonalMaxExcursionPointViconCoords, leftwardMaxExcursionPointViconCoords,
            backwardLeftDiagonalMaxExcursionPointViconCoords, backwardMaxExcursionPointViconCoords,
        backwardRightDiagonalMaxExcursionPointViconCoords};


        //convert all of the points in Vicon space to Unity space 
        Vector3 rightwardMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(rightwardMaxExcursionPointViconCoords);
        Vector3 forwardMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(forwardMaxExcursionPointViconCoords);
        Vector3 leftwardMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(leftwardMaxExcursionPointViconCoords);
        Vector3 backwardMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(backwardMaxExcursionPointViconCoords);

        Vector3 forwardRightDiagonalMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(forwardRightDiagonalMaxExcursionPointViconCoords);
        Vector3 forwardLeftDiagonalMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(forwardLeftDiagonalMaxExcursionPointViconCoords);
        Vector3 backwardLeftDiagonalMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(backwardLeftDiagonalMaxExcursionPointViconCoords);
        Vector3 backwardRightDiagonalMaxExcursionPointUnityCoords = levelManagerScript.mapPointFromViconFrameToUnityFrame(backwardRightDiagonalMaxExcursionPointViconCoords);

        //return the maximum excursion points as an array of type Vector3
        List<Vector3> maximumExcursionPoints = new List<Vector3> { rightwardMaxExcursionPointUnityCoords, forwardRightDiagonalMaxExcursionPointUnityCoords, forwardMaxExcursionPointUnityCoords,
        forwardLeftDiagonalMaxExcursionPointUnityCoords, leftwardMaxExcursionPointUnityCoords, backwardLeftDiagonalMaxExcursionPointUnityCoords, backwardMaxExcursionPointUnityCoords,
        backwardRightDiagonalMaxExcursionPointUnityCoords};

        return maximumExcursionPoints;

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




}

