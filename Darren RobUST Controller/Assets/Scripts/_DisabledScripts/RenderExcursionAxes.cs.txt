using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RenderExcursionAxes : MonoBehaviour
{
    public Camera sceneCamera; //the camera used for this scene
    public GameObject lateralAxisObject; //the object containing the line renderer for the lateral axis
    private LineRenderer lateralAxis;
    public GameObject verticalAxisObject; //the object containing the line renderer for the lateral axis
    private LineRenderer verticalAxis;
    public GameObject positiveSlopeDiagonalAxisObject; //the object containing the line renderer for the diagonal axis from bottom-left to top-right
    private LineRenderer positiveSlopeDiagonalAxis;
    public GameObject negativeSlopeDiagonalAxisObject; //the object containing the line renderer for the diagonal axis from top-left to bottom-right
    private LineRenderer negativeSlopeDiagonalAxis;

    // Axes program logic control flags
    private bool axesDrawn = false; //whether or not the axes have already been drawn. Once true, this script doesn't do much else.

    // The subject-specific drawn axes
    private uint numberOfExcursionDirections = 8; //hard-coded for now, but really should be retrieved from the level manager
    private float[] anglesOfAxesAsDrawnOnScreenInDegrees; //while the angles are the primary directions and diagonals separated by 
                                                         //45 degrees in Vicon space, on-screen they will differ based
                                                         //on the subject ratio of foot length (AP base of support) to stance
                                                         // width (ML base of support).
    //center of mass manager
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    private bool centerOfMassManagerReadyStatus = false; //if the center of mass manager is ready to distribute data (true) or not (false)

    // the excursion level manager
    public GameObject excursionLevelManager;
    private LevelManagerScriptAbstractClass levelManagerScript;


    //margins on-screen
    public float lateralMarginViewportCoords = 0.1f;
    public float verticalMarginViewportCoords = 0.1f;

    //axis centering (where on-screen is the center?)
    float lateralAxisApCoordinateInViewportCoords = 0.5f; //Where the horizontal (lateral) axis is on the screen. 0.5f puts it in the middle of the viewport.
    float verticalAxisMlCoordinateInViewportCoords = 0.5f; //where the vertical (AP) axis is on the screen. 0.5f puts in in the middle of the viewport.



    // Start is called before the first frame update
    void Start()
    {

        //marker data and center of mass manager
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();

        //level manager
        levelManagerScript = excursionLevelManager.GetComponent<ExcursionLevelManager>();

        //line renderers for each axis
        lateralAxis = lateralAxisObject.GetComponent<LineRenderer>();
        verticalAxis = verticalAxisObject.GetComponent<LineRenderer>();
        positiveSlopeDiagonalAxis = positiveSlopeDiagonalAxisObject.GetComponent<LineRenderer>();
        negativeSlopeDiagonalAxis = negativeSlopeDiagonalAxisObject.GetComponent<LineRenderer>();

        //initialize sizes of arrays
        anglesOfAxesAsDrawnOnScreenInDegrees = new float[numberOfExcursionDirections];

    }

    // Update is called once per frame
    void Update()
    {
        if (centerOfMassManagerReadyStatus == false) //if the center of mass manager object is not currently ready to serve COM data
        {
            centerOfMassManagerReadyStatus = centerOfMassManagerScript.getCenterOfMassManagerReadyStatus(); // see if it is ready now
        }
        else //if we can get COM data from the center of mass manager object
        {
            if (axesDrawn == false) //if the axes have not already been formatted and drawn
            {
                Debug.Log("Drawing excursion axes on-screen.");
                formatAxes();
                axesDrawn = true;

                //send the on-screen axes angles to the level manager so the task can begin
                // REPLACED: level manager now calls and asks for the angles
                //levelManagerScript.setOnScreenExcursionAxesAnglesInDegrees(anglesOfAxesAsDrawnOnScreenInDegrees);

            }
        }
    }






    private void formatAxes()
    {

        //compute lateral axis end points in viewport coordinates
        Vector3[] lateralAxisPoints = new Vector3[2]; //each axis will be a line and so only needs two points
        Vector3 pointOneLateralAxis = new Vector3(lateralMarginViewportCoords, lateralAxisApCoordinateInViewportCoords, transform.position.z);
        Vector3 pointTwoLateralAxis = new Vector3(1 - lateralMarginViewportCoords, lateralAxisApCoordinateInViewportCoords, transform.position.z);
        //convert from viewport coordinates to world coordinates
        pointOneLateralAxis = sceneCamera.ViewportToWorldPoint(pointOneLateralAxis);
        pointTwoLateralAxis = sceneCamera.ViewportToWorldPoint(pointTwoLateralAxis);
        lateralAxisPoints[0] = pointOneLateralAxis;
        lateralAxisPoints[1] = pointTwoLateralAxis;
        //set the line renderer positions to render the axis
        lateralAxis.SetPositions(lateralAxisPoints);


        //compute vertical axis end points in viewport coordinates
        Vector3[] verticalAxisPoints = new Vector3[2]; //each axis will be a line and so only needs two points
        Vector3 pointOneVerticalAxis = new Vector3(verticalAxisMlCoordinateInViewportCoords, verticalMarginViewportCoords, transform.position.z);
        Vector3 pointTwoVerticalAxis = new Vector3(verticalAxisMlCoordinateInViewportCoords, 1 - verticalMarginViewportCoords, transform.position.z);
        //convert from viewport coordinates to world coordinates
        pointOneVerticalAxis = sceneCamera.ViewportToWorldPoint(pointOneVerticalAxis);
        pointTwoVerticalAxis = sceneCamera.ViewportToWorldPoint(pointTwoVerticalAxis);
        verticalAxisPoints[0] = pointOneVerticalAxis;
        verticalAxisPoints[1] = pointTwoVerticalAxis;
        //set the line renderer positions to render the axis
        verticalAxis.SetPositions(verticalAxisPoints);

        // create the diagonals at 45 degree angles in the real, Vicon space, then draw them into the 
        // Unity space. Because of the mismatch between the screen dimensions and base of support dimensions,
        // they will not appear to be at 45 degree angles on-screen.
        drawDiagonalsBasedOnViconFortyFiveDegrees();

    }



    public Vector3 getAxesCenterPosition()
    {
        Vector3 axesCenterPositionViewportCoords = new Vector3(verticalAxisMlCoordinateInViewportCoords, lateralAxisApCoordinateInViewportCoords, transform.position.z);
        return sceneCamera.ViewportToWorldPoint(axesCenterPositionViewportCoords);
    }

    public float[] getAnglesOfExcursionAxesAsDrawnOnScreenInDegrees()
    {
        return anglesOfAxesAsDrawnOnScreenInDegrees;
    }

    public float[] GetOnScreenExcursionDirectionAnglesFromXAxis()
    {
        return anglesOfAxesAsDrawnOnScreenInDegrees;
    }

    public float[] GetExcursionDirectionAnglesCcwFromRightwardsViconFrame()
    {
        // This function assumes the +x-axis of the Vicon frame corresponds to rightwards movement. 
        // This is NOT usually the case, and is corrected for with the "rightwards sign" and "forwards sign" computations.
        return new float[8] { 0.0f, 45.0f, 90.0f, 135.0f, 180.0f, 225.0f, 270.0f, 315.0f }; // in degrees from +x-axis
    }


    private void drawDiagonalsBasedOnViconFortyFiveDegrees()
    {
        // Get the edges of the base of support from the center of mass manager, as the boundary of stability excursions are measured from its center.
        (float leftEdgeBaseOfSupportXPos, float rightEdgeBaseOfSupportXPos, float frontEdgeBaseOfSupportYPos,
         float backEdgeBaseOfSupportYPos) = centerOfMassManagerScript.getEdgesOfBaseOfSupportInViconCoordinates();

        //compute the center of the base of support
        Vector3 centerOfBaseOfSupportViconCoords = new Vector3((leftEdgeBaseOfSupportXPos + rightEdgeBaseOfSupportXPos) / 2.0f, (frontEdgeBaseOfSupportYPos + backEdgeBaseOfSupportYPos) / 2.0f, 5.0f);

        // 1.) Positive slope diagonal
        // Compute the endpoints of the positive slope diagonal in Vicon space
        Vector3[] positiveSlopeDiagonalAxisPoints = new Vector3[2]; //each axis will be a line and so only needs two points
        float distanceFromCenterAlongEachAxisInViconCoords = frontEdgeBaseOfSupportYPos - centerOfBaseOfSupportViconCoords.y;
        Vector3 upperRightPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x + distanceFromCenterAlongEachAxisInViconCoords, centerOfBaseOfSupportViconCoords.y + distanceFromCenterAlongEachAxisInViconCoords, 5.0f);
        Vector3 lowerLeftPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x - distanceFromCenterAlongEachAxisInViconCoords, centerOfBaseOfSupportViconCoords.y - distanceFromCenterAlongEachAxisInViconCoords, 5.0f);
        //Convert the Vicon coordinates to Unity coordinates by passing through viewport coordinates
        Vector3 upperRightPointUnityCoords = centerOfMassManagerScript.convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(upperRightPointViconCoords);
        Vector3 lowerLeftPointUnityCoords = centerOfMassManagerScript.convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(lowerLeftPointViconCoords);
        // Store the Unity points for the positive diagonal in an array of type Vector3
        positiveSlopeDiagonalAxisPoints[0] = lowerLeftPointUnityCoords;
        positiveSlopeDiagonalAxisPoints[1] = upperRightPointUnityCoords;
        // Set the points of the positive diagonal line renderer equal to the computed points
        positiveSlopeDiagonalAxis.positionCount = positiveSlopeDiagonalAxisPoints.Length;
        positiveSlopeDiagonalAxis.SetPositions(positiveSlopeDiagonalAxisPoints);

        // 2.) Negative slope diagonal
        // Compute the endpoints of the negative slope diagonal in Vicon space
        Vector3[] negativeSlopeDiagonalAxisPoints = new Vector3[2]; //each axis will be a line and so only needs two points
        Vector3 upperLeftPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x - distanceFromCenterAlongEachAxisInViconCoords, centerOfBaseOfSupportViconCoords.y + distanceFromCenterAlongEachAxisInViconCoords, 5.0f);
        Vector3 lowerRightPointViconCoords = new Vector3(centerOfBaseOfSupportViconCoords.x + distanceFromCenterAlongEachAxisInViconCoords, centerOfBaseOfSupportViconCoords.y - distanceFromCenterAlongEachAxisInViconCoords, 5.0f);
        //Convert the Vicon coordinates to Unity coordinates by passing through viewport coordinates
        Vector3 upperLeftPointUnityCoords = centerOfMassManagerScript.convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(upperLeftPointViconCoords);
        Vector3 lowerRightPointUnityCoords = centerOfMassManagerScript.convertViconCoordinatesToUnityCoordinatesByMappingIntoViewport(lowerRightPointViconCoords);
        // Store the Unity points for the negative diagonal in an array of type Vector3
        negativeSlopeDiagonalAxisPoints[0] = upperLeftPointUnityCoords;
        negativeSlopeDiagonalAxisPoints[1] = lowerRightPointUnityCoords;
        // Set the points of the negative diagonal line renderer equal to the computed points
        negativeSlopeDiagonalAxis.positionCount = negativeSlopeDiagonalAxisPoints.Length;
        negativeSlopeDiagonalAxis.SetPositions(negativeSlopeDiagonalAxisPoints);

        // Compute the angles of each axis (in degrees) as drawn on the screen
        float[] anglesOfAxesOnScreen = new float[8];
        // The primary axes are obvious
        anglesOfAxesOnScreen[0] = 0.0f;
        anglesOfAxesOnScreen[2] = 90.0f;
        anglesOfAxesOnScreen[4] = 180.0f;
        anglesOfAxesOnScreen[6] = 270.0f;
        // The diagonal axes will differ by subject and stance width, so must be computed 
        Vector3 axesCenterPositionUnityCoords = getAxesCenterPosition();
        anglesOfAxesOnScreen[1] = Mathf.Atan2((upperRightPointUnityCoords.y - axesCenterPositionUnityCoords.y),(upperRightPointUnityCoords.x - axesCenterPositionUnityCoords.x));
        anglesOfAxesOnScreen[1] = anglesOfAxesOnScreen[1] * (180.0f / Mathf.PI); //convert to degrees
        anglesOfAxesOnScreen[3] = Mathf.Atan2((upperLeftPointUnityCoords.y - axesCenterPositionUnityCoords.y) , (upperLeftPointUnityCoords.x - axesCenterPositionUnityCoords.x));
        anglesOfAxesOnScreen[3] = anglesOfAxesOnScreen[3] * (180.0f / Mathf.PI); //convert to degrees
        anglesOfAxesOnScreen[5] = Mathf.Atan2((lowerLeftPointUnityCoords.y - axesCenterPositionUnityCoords.y) , (lowerLeftPointUnityCoords.x - axesCenterPositionUnityCoords.x));
        anglesOfAxesOnScreen[5] = anglesOfAxesOnScreen[5] * (180.0f / Mathf.PI); //convert to degrees
        anglesOfAxesOnScreen[7] = Mathf.Atan2((lowerRightPointUnityCoords.y - axesCenterPositionUnityCoords.y) , (lowerRightPointUnityCoords.x - axesCenterPositionUnityCoords.x));
        anglesOfAxesOnScreen[7] = anglesOfAxesOnScreen[7] * (180.0f / Mathf.PI); //convert to degrees

        //Since atan2 returns values from -180 to 180, add 360 to each negative value
        for(uint angleIndex = 0; angleIndex < anglesOfAxesOnScreen.Length; angleIndex++)
        {
            if(anglesOfAxesOnScreen[angleIndex] < 0) //if negative angle
            {
                //then add 360 degrees
                anglesOfAxesOnScreen[angleIndex] = anglesOfAxesOnScreen[angleIndex] + 360.0f;
            }
        }

        //store the drawn angles in degrees as an instance variable
        anglesOfAxesAsDrawnOnScreenInDegrees = anglesOfAxesOnScreen;
    }


    private void drawDiagonalsAtFortyFiveDegrees()
    {
        //use the camera aspect ratio to render the diagonal axes at 45 degree angles from
        //the main axes (on any screen)
        float cameraAspectRatio = sceneCamera.aspect; //gives width/height
        Debug.Log("Aspect ratio: " + cameraAspectRatio);
        float lateralDistanceFromCenterToDiagEndpointScreenCoords = ((1.0f - verticalMarginViewportCoords) - 0.5f) * (1 / cameraAspectRatio);
        //positive slope diagonal axis
        Vector3[] positiveSlopeDiagonalAxisPoints = new Vector3[2]; //each axis will be a line and so only needs two points
        Vector3 lowerLeftPoint = new Vector3(0.5f - lateralDistanceFromCenterToDiagEndpointScreenCoords, verticalMarginViewportCoords, transform.position.z);
        Vector3 upperRightPoint = new Vector3(0.5f + lateralDistanceFromCenterToDiagEndpointScreenCoords, 1 - verticalMarginViewportCoords, transform.position.z);
        lowerLeftPoint = sceneCamera.ViewportToWorldPoint(lowerLeftPoint);
        upperRightPoint = sceneCamera.ViewportToWorldPoint(upperRightPoint);
        positiveSlopeDiagonalAxisPoints[0] = lowerLeftPoint;
        positiveSlopeDiagonalAxisPoints[1] = upperRightPoint;
        positiveSlopeDiagonalAxis.SetPositions(positiveSlopeDiagonalAxisPoints);

        //negative slope diagonal axis
        Vector3[] negativeSlopeDiagonalAxisPoints = new Vector3[2]; //each axis will be a line and so only needs two points
        Vector3 upperLeftPoint = new Vector3(0.5f - lateralDistanceFromCenterToDiagEndpointScreenCoords, 1 - verticalMarginViewportCoords, transform.position.z);
        Vector3 lowerRightPoint = new Vector3(0.5f + lateralDistanceFromCenterToDiagEndpointScreenCoords, verticalMarginViewportCoords, transform.position.z);
        upperLeftPoint = sceneCamera.ViewportToWorldPoint(upperLeftPoint);
        lowerRightPoint = sceneCamera.ViewportToWorldPoint(lowerRightPoint);
        negativeSlopeDiagonalAxisPoints[0] = upperLeftPoint;
        negativeSlopeDiagonalAxisPoints[1] = lowerRightPoint;
        negativeSlopeDiagonalAxis.SetPositions(negativeSlopeDiagonalAxisPoints);
    }
}
