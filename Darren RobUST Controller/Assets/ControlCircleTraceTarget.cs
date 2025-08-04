using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class ControlCircleTraceTarget : MonoBehaviour
{
    // States
    private string currentState;
    private const string settingUpState = "settingUpState";
    private const string pushedByPlayerState = "pushedByPlayerState";
    private const string pacingState = "pacingState";
    private const string pacerIdlingState = "pacerIdlingState";

    // Level manager
    // NOTE: we only use this script in the paced cirlce trace task, 
    // so we don't need to use the abstractLevelManager class.
    public PacedCircleTraceLevelManager levelManagerScript;

    // Querying oval perimeter vectors
    private float[] indexingThetasInViconFrame;
    private float startTheta = 0.0f;
    private float endTheta = 2 * Mathf.PI;
    private int numThetaQueryPoints = (int)360 * 4;
    private float[] thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame;

    // Pacer velocity
    private float constantPacerVelocityMmPerSec = 0.0f; // Set via a public function (by the level manager)
    private float pacerDesiredLapTimeInSecondsThisTrial; // How many seconds it should take the pacer to move around the ellipse

    // Pacer starting angle 
    private float indicatorStartingAngleInRadiansViconFrameThisTrial = 0.0f;

    // Pacer stopwatch for pacing laps
    private Stopwatch pacerLapStopwatch = new Stopwatch();


    // Start is called before the first frame update
    void Start()
    {
        // Start in the pushed by player state
        currentState = pushedByPlayerState;

        // Build the two key perimeter indexing arrays: 
        // 1.) The set of angles at which we'll query the perimeter, measured from theta = 0 in the Vicon frame (leftwards, proceeding CCW). 
        // Get the current quadrant to determine the ellipse properties
        indexingThetasInViconFrame = CreateEvenlySpacedVector(startTheta, endTheta, numThetaQueryPoints);


    }

    // Update is called once per frame
    void Update()
    {
        switch (currentState)
        {
            // If the circle trace target is in the pushed by player state
            case pushedByPlayerState:
                // do nothing for now
                break;
            case pacingState:
                // Animate pacer moving around oval with constant velocity
                AnimateConstantVelocityPacer();
                if (pacerLapStopwatch.ElapsedMilliseconds >= (pacerDesiredLapTimeInSecondsThisTrial * 1000.0f)) // 1000 to convert s to ms
                {
                    // Change the active state
                    changeActiveState(pacerIdlingState);

                    // Reset (not restart!) the stopwatch 
                    pacerLapStopwatch.Reset();
                }

                break;
            case pacerIdlingState:
                // Do nothing
                break;
        }
    }

    private void AnimateConstantVelocityPacer()
    {
        // Compute the perimeter covered as constantVelocity * timeSinceLapStartedInSeconds
        float perimeterCoveredThisLapInMm = constantPacerVelocityMmPerSec * (pacerLapStopwatch.ElapsedMilliseconds / 1000.0f); // divide by 1000 to convert ms to s

        // Find the starting angle perimeter value and index
        (float perimeterCoveredAtStartingAngle, int indexOfNearestTheta) = GetPerimeterCoveredAtAngleInViconFrame(indicatorStartingAngleInRadiansViconFrameThisTrial);

        // Find the nearest perimeter covered value in the perimeter array. 
        // Note: the computation will depend on the direction of travel.
        (float nearestAngleInRadiansViconFrame, int nearestThetaIndex) = GetTargetPacerThetaInViconFrameGivenStartPointAndPerimeterCovered(indexOfNearestTheta, perimeterCoveredThisLapInMm);

        // Set the pacer position based on the angle in Vicon frame. 
        // The level manager already has functions for this, so we use them.
        levelManagerScript.setCircleTraceTargetAngle(nearestAngleInRadiansViconFrame * (180.0f / Mathf.PI));
    }

    // Returns the perimeter covered at a certain vicon frame angle, starting at Vicon frame theta  = 0 (leftwards) and proceeding CCW.
    private (float, int) GetPerimeterCoveredAtAngleInViconFrame(float viconFrameThetaInRadians)
    {
        // Find the nearest value in the query theta array
        float searchStepSize = (endTheta - startTheta) / numThetaQueryPoints;
        int indexOfNearestTheta = (int)(viconFrameThetaInRadians / searchStepSize);
        float remainder = viconFrameThetaInRadians % searchStepSize;
        // If the theta is closer to the next step value
        if (remainder > 0.5f * searchStepSize)
        {
            // Then the correct index is actually increased by 1
            indexOfNearestTheta = indexOfNearestTheta + 1;
        }

        // Get the corresponding perimeter value using the theta index
        return (thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[indexOfNearestTheta], indexOfNearestTheta);
    }

    // Returns the angle at a certain perimeter-covered covered value, starting at Vicon frame theta  = 0 (leftwards) and proceeding CCW.
    private (float, int) GetAngleInViconFrameAtPerimeterCoveredValue(float perimeterCovered)
    {
        // Lowest error and closest index
        float lowestErrorInSearch = Mathf.Infinity;
        int indexOfNearestPerimeter = -1;
        // Find the nearest value in the perimeter array with a brute force search
        // For each perimeter value
        for (int perimeterIndex = 0; perimeterIndex < thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame.Length; perimeterIndex++)
        {
            float errorThisIndex = Mathf.Abs(perimeterCovered - thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[perimeterIndex]);
            if (errorThisIndex < lowestErrorInSearch)
            {
                // Store the index of the element that was closest to the desired value
                lowestErrorInSearch = errorThisIndex;
                indexOfNearestPerimeter = perimeterIndex;
            }
        }

        // Get the theta value using the perimeter index
        return (indexingThetasInViconFrame[indexOfNearestPerimeter], indexOfNearestPerimeter);
    }

    private (float, int) GetTargetPacerThetaInViconFrameGivenStartPointAndPerimeterCovered(int indexOfStartPoint, float perimeterCoveredThisLapInMm)
    {

        Debug.Log("Pacer perimeter covered this lap [mm]: " + perimeterCoveredThisLapInMm);


        // Get the perimeter covered value at the start point (really 0, so we must make this value the new zero, effectively)
        float perimeterCoveredAtStart = thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[indexOfStartPoint];

        // Get the total perimeter value
        float totalPerimeter = thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame.Length - 1];

        // If we're moving counterclockwise (with increasing perimeter covered values)
        float currentPerimeterValue = -1.0f; // initialize
        if (levelManagerScript.GetIsThisTrialCounterClockwiseFlag() == true)
        {
            // If the perimeter covered this lap does not bring us past 2*pi radians
            if (perimeterCoveredAtStart + perimeterCoveredThisLapInMm > totalPerimeter)
            {
                // Then the perimeter we want is the sum of the perimeter covered this lap and the start point perimeter
                currentPerimeterValue = perimeterCoveredAtStart + perimeterCoveredThisLapInMm;
            }
            // Else if the perimeter covered in the lap puts us past 2*pi radians
            else
            {
                // Then the perimeter we want is the sum of the perimeter covered this lap and the starting perimeter minus the total perimeter.
                currentPerimeterValue = perimeterCoveredAtStart + perimeterCoveredThisLapInMm - totalPerimeter;
            }

        }
        //Else if we're moving clockwise
        else
        {
            // If the perimeter covered this lap does not bring us past 0 radians
            if (perimeterCoveredAtStart - perimeterCoveredThisLapInMm > 0)
            {
                // Then the perimeter we want is the start point perimeter minus the perimeter covered this lap. 
                currentPerimeterValue = perimeterCoveredAtStart - perimeterCoveredThisLapInMm;

            }
            // Else if the perimeter covered this lap brings us past 0 radians 
            else
            {
                // Then the perimeter we want is the start point perimeter minus the perimeter covered this lap plus the total perimeter.
                currentPerimeterValue = perimeterCoveredAtStart - perimeterCoveredThisLapInMm + totalPerimeter;
            }
        }

        // Given the perimeter we want, find the nearest theta value and index
        Debug.Log("Pacer perimeter value to index: " + currentPerimeterValue);
        (float nearestAngleInRadiansViconFrame, int nearestThetaIndex) = GetAngleInViconFrameAtPerimeterCoveredValue(currentPerimeterValue);

        Debug.Log("Pacer angle (radians, Vicon frame) this frame: " + nearestAngleInRadiansViconFrame);
        // Return the nearest theta value and index
        return (nearestAngleInRadiansViconFrame, nearestThetaIndex);

    }




    public void SetPacerConstantVelocityInViconFrameMmPerSec(float desiredConstantPacerVelocityMmPerSec)
    {
        constantPacerVelocityMmPerSec = desiredConstantPacerVelocityMmPerSec;
    }

    public bool GetPacerPushableStatus()
    {
        // If the tracer is in the pushable-by-player state
        if (currentState == pushedByPlayerState)
        {
            // Then the tracer is pushable
            return true;
        }
        else // in any other state
        {
            // The tracer is not pushable
            return false;
        }
    }

    public void StartPacerLap(float pacerDesiredLapTimeInSeconds)
    {
        Debug.Log("Start pacer lap called by level manager.");
        // If the pacer is in the pushable state( i.e, we just finished trial 1)
        // or if the pacer was idling 
        //  or if the pacer was still in a pacing state
        if (currentState == pushedByPlayerState || currentState == pacerIdlingState || currentState == pacingState)
        {
            // Compute the desired velocity of the pacer for this trial 
            float totalPerimeter = thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame.Length - 1];
            Debug.Log("When starting pacer lap, total perimeter was computed as: " + totalPerimeter);
            constantPacerVelocityMmPerSec = totalPerimeter / pacerDesiredLapTimeInSeconds;

            // Also store the lap time 
            pacerDesiredLapTimeInSecondsThisTrial = pacerDesiredLapTimeInSeconds;

            Debug.Log("Starting pacer lap with constant velocity [mm/s]: " + constantPacerVelocityMmPerSec);

            // Then rest the pacer position at neutral and start a paced lap
            // by entering (or re-entering) the pacing state
            changeActiveState(pacingState);
        }
    }

    public void SetPosition(Vector3 newTargetPosition)
    {
        transform.position = newTargetPosition;
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }


    private float[] CreateEvenlySpacedVector(float startVal, float endVal, int numSteps)
    {
        // Step size
        float stepSize = (endVal - startVal) / numSteps;

        // Initialize array
        float[] evenlySpacedVector = new float[numSteps + 1];
        evenlySpacedVector[0] = startVal;
        // For the number of steps
        for (int stepIndex = 1; stepIndex <= numSteps; stepIndex++)
        {
            evenlySpacedVector[stepIndex] = startVal + stepIndex * stepSize;
        }

        return evenlySpacedVector;

    }

    private void GetPerimeterAtThetaQueryPointsMeasuredFromThetaZeroViconFrame(float[] indexingThetasInViconFrame)
    {
        // For each theta query point
        float runningPerimeterTally = 0.0f;
        thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame = new float[indexingThetasInViconFrame.Length];
        thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[0] = runningPerimeterTally; // the perimeter covered at the start angle is 0
        Vector3 lastQueryAnglePositionViconFrame = new Vector3(0.0f, 0.0f, 0.0f);
        for (int angleQueryIndex = 0; angleQueryIndex < indexingThetasInViconFrame.Length; angleQueryIndex++)
        {
            // Get the current angle in radians in Vicon frame
            float currentAngleRadiansViconFrame = indexingThetasInViconFrame[angleQueryIndex];

            // Get the quadrant
            uint quadrant = levelManagerScript.getQuadrantFromAngle(currentAngleRadiansViconFrame);

            // Get the center of the base of support in Vicon frame
            Vector3 centerOfBaseOfSupportViconFrame = levelManagerScript.GetCenterOfExcursionLimitsInViconFrame();

            // Get the lateral and vertical axes of the ellipse in that quadrant
            (float ellipseWidthViconUnitsMm, float ellipseHeightViconUnitsMm) = levelManagerScript.getEllipseHeightAndWidthBasedOnQuadrant(quadrant);

            // Given the current angle, get the coordinate of the ellipse at the given angle
            // Given the passed-in angle, get the (x,y) coordinate of the tracing trajectory at that angle from the +x-axis
            float radiusAtCurrentAngleViconFrame = (ellipseWidthViconUnitsMm * ellipseHeightViconUnitsMm) /
                Mathf.Sqrt(Mathf.Pow(ellipseHeightViconUnitsMm * Mathf.Cos(currentAngleRadiansViconFrame), 2.0f)
                + Mathf.Pow(ellipseWidthViconUnitsMm * Mathf.Sin(currentAngleRadiansViconFrame), 2.0f));
            float xCoordinateOnTrajectory = centerOfBaseOfSupportViconFrame.x + radiusAtCurrentAngleViconFrame * Mathf.Cos(currentAngleRadiansViconFrame);
            float yCoordinateOnTrajectory = centerOfBaseOfSupportViconFrame.y + radiusAtCurrentAngleViconFrame * Mathf.Sin(currentAngleRadiansViconFrame);

            Vector3 currentQueryAngleEllipsePosition = new Vector3(xCoordinateOnTrajectory, yCoordinateOnTrajectory, 0.0f);

            // If the index is not zero, then add the distance between points to the running total of the perimeter
            // as an approximation to the perimeter at that theta
            if (angleQueryIndex != 0)
            {
                // Update the perimeter at this angle by adding the perimeter distance approximation
                float distanceBetweenThisAndLastQueryPoint = (currentQueryAngleEllipsePosition - lastQueryAnglePositionViconFrame).magnitude;
                runningPerimeterTally = runningPerimeterTally + distanceBetweenThisAndLastQueryPoint;

                // Store this value as the perimeter for the corresponding theta
                thetaCorrespondingPerimetersMeasuredFromThetaZeroViconFrame[angleQueryIndex] = runningPerimeterTally;
            }

            // Set the last query angle position equal to the position we computed
            lastQueryAnglePositionViconFrame = currentQueryAngleEllipsePosition;
        }


    }





    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        Debug.Log("Transitioning states from " + currentState + " to " + newState);
        // call the exit function for the current state.
        if (currentState == pushedByPlayerState)
        {
            exitPushedByPlayerState();
        }
        else if (currentState == pacingState)
        {
            exitPacingState();
        }
        else if (currentState == pacerIdlingState)
        {
            exitPacerIdlingState();
        }


        //then call the entry function for the new state
        if (newState == pushedByPlayerState)
        {
            enterPushedByPlayerState();
        }
        else if (newState == pacingState)
        {
            enterPacingState();
        }
        else if (newState == pacerIdlingState)
        {
            enterPacerIdlingState();
        }
    }

    private void enterPushedByPlayerState()
    {
        //set the current state
        currentState = pushedByPlayerState;
    }

    private void exitPushedByPlayerState()
    {
        // do nothing for now
    }


    private void enterPacingState()
    {
        //set the current state
        currentState = pacingState;

        // Reset the pacer position to neutral
        indicatorStartingAngleInRadiansViconFrameThisTrial = levelManagerScript.ResetTraceTargetToStartingPosition();

        // Start a paced lap by restarting the lap stopwatch
        pacerLapStopwatch.Restart();
    }

    private void exitPacingState()
    {
        // do nothing for now
    }


    private void enterPacerIdlingState()
    {
        //set the current state
        currentState = pacerIdlingState;
    }

    private void exitPacerIdlingState()
    {
        // do nothing for now
    }



}
