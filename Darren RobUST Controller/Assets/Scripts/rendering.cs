using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics; 

public class rendering : MonoBehaviour
{
    public PartialSquatLevelManager levelManager; 
    private Stopwatch testStopwatch = new Stopwatch(); //start, stop , reset to 0, restart to 0 and start again

    public LineRenderer ankleToKneeTarget; 
    public LineRenderer kneeToHipTarget; 
    private float kneeAngleTargetRad;

    void Start()
    {
        //define angles
        kneeAngleTargetRad = levelManager.GetTargetKneeAngleRad(); // get target full squat depth knee angle. This sets the white squat representation.

        //set shank desired config
        // Positions[0] contains the leg starting point, i.e, the ankle
        // Positions[1] contains the knee
        Vector3[] positions1 = new Vector3[2]; 
        positions1[0] = new Vector3(.1f,0f,0f); //ankle point
        positions1[1] = new Vector3(.1f,1.5f,0f); //knee point - a fixed length directly along +y-axis from the ankle
        ankleToKneeTarget.positionCount = positions1.Length; 
        ankleToKneeTarget.SetPositions(positions1); // the first line renderer just plots the shank

        //set thigh desired config
        // Positions[2] contains the pelvis position
        Vector3[] positions2 = new Vector3[2]; 
        positions2[0] = new Vector3(.1f,1.5f,0f); //knee point
        float drawnThighLengthInMeters = 1.5f; // how long the thigh is in our rendering
        Vector3 kneeAngleTarget = drawnThighLengthInMeters * new Vector3(0f,Mathf.Cos(kneeAngleTargetRad),Mathf.Sin(kneeAngleTargetRad));
        positions2[1] = kneeAngleTarget + positions2[0]; //hip point
        kneeToHipTarget.positionCount = positions2.Length; 
        kneeToHipTarget.SetPositions(positions2);  

    }

    void Update()
    {     

    }
}
