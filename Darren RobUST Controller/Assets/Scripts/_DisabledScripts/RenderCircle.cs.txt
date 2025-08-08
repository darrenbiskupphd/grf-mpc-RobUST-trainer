using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RenderCircle : MonoBehaviour
{

    private LineRenderer lineRenderer;  // the line renderer attached to this object, which renders the circle
    public int numberOfTrajectoryPointsLessOne;  //how many points to put in the circle
    private Vector3[] circlePointPositions;  // stores
    public float circleRadiusAsFractionOfAnteroposteriorBoundaryOfSupportDimension; //radius of circle as a fraction of the vertical height of the viewport
    private float circleRadius; //the actual radius, in World Units, of the circle
    public GameObject circleCenter; //the Game object located at the center of the circle
    private Vector3 circleCenterPosition = new Vector3(0.0f, 0.0f,  15.0f); //the position of the circle center 


    // Start is called before the first frame update
    void Start()
    {
        circleCenter.transform.position = circleCenterPosition; // Since our mapping from Vicon frame to Unity frame sets the center at (0,0)
                                                                // in Unity frame, ensure that the trajectory center object is at (0,0).
        circlePointPositions = new Vector3[numberOfTrajectoryPointsLessOne + 1];
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = numberOfTrajectoryPointsLessOne + 1;
    }

    // Update is called once per frame
    void Update()
    {

    }

    // The primary function for this script. Currently, the level manager determines the Unity coordinates for the 
    // desired tracing trajectory and calls this function to set them.
    public void setPointsOfTrajectory(Vector3[] pointsOfTrajectory)
    {
        lineRenderer.SetPositions(pointsOfTrajectory);
    }

    // Get the number of points 
    public int getNumberOfTrajectoryPoints()
    {
        return (numberOfTrajectoryPointsLessOne + 1);
    }


}
