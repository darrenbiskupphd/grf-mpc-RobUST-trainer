using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DrawSinusoid : MonoBehaviour
{

    private LineRenderer lineRenderer;  // the line renderer attached to this object, which renders the sinusoid
    public int numberOfSinusoidPointsLessOne;  //how many points to put in the sinusoid
    private Vector3[] sinusoidPointPositions;  // stores
    bool hasValidSinusoidDataFlag; //has a valid sinusoid been drawn yet
    private float leftEdgeOfSinusoidInViewportCoords = 0.1f;
    private float rightEdgeOfSinusoidInViewportCoords = 0.9f;
    private float secondsOfDataToIncludeInWindow;
    public float frequencyOfSineWave; //frequency of sine wave in Hz
    private float startTime;
    private float endTime;
    private float verticalMidpointOfSinusoidInViewportCoords = 0.5f;  //vertical location of the midpoint of the sinusoid, 
                                                                      //in viewport coords. 0.5f would be halfway up screen.
    private float amplitudeOfSinWaveInViewportCoords = 0.4f; // how much of the vertical screen space the sinusoid will take up. 
                                                             // 0.4f would be 80% (plus and minus 40%), for example.
    private Camera sceneCamera; //the camera of this scene

    public GameObject sinusoidCurrentValueIndicatorObject;
    private SinusoidIndicatorController sinusoidCurrentValueIndicatorScript;

    public GameObject levelManager;
    private LevelManagerAnkleSinusoid levelManagerScript;

    public GameObject ankleRangeOfMotionFinder; //the GameObject containing the script that tracks ankle motion to find it's ROM
    private MeasureAnkleRomScript ankleRangeOfMotionFinderScript; //the GameObject containing the script that tracks ankle motion to find it's ROM

    private bool animateSinusoidFlag = false; // whether to animate the sinusoid (true) or not (false)

    // tracking the sinusoid's current valeu 
    private float currentPhaseOfSinWave; // the current phase of the sin wave animating the sinusoid. 
                                         // Values can take any value, but should consider this value's modulus with 2*pi. 
                                         // E.g. at 4*pi, sin wave will have value of 0. 
                                         // E.g. at 4.5*pi, sin wave will have value of 1. 

    // Start is called before the first frame update
    void Start()
    {
        // Get a reference to the indicator showing the current sinusoid value
        sinusoidCurrentValueIndicatorScript = sinusoidCurrentValueIndicatorObject.GetComponent<SinusoidIndicatorController>();

        // Get a reference to the level manager script
        levelManagerScript = levelManager.GetComponent<LevelManagerAnkleSinusoid>();

        // Get a reference to the script that computes and stores ankle ROM
        ankleRangeOfMotionFinderScript = ankleRangeOfMotionFinder.GetComponent<MeasureAnkleRomScript>();


        sinusoidPointPositions = new Vector3[numberOfSinusoidPointsLessOne + 1];
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = numberOfSinusoidPointsLessOne + 1;
        sceneCamera = FindObjectOfType<Camera>();
        //set the amount of data to display so that we see one full sinusoid
        secondsOfDataToIncludeInWindow = 1.0f / frequencyOfSineWave;
        
        // Draw the sinusoid for a single frame
        startTime = Time.time; //get the time at the beginning of the program
        endTime = startTime + 1.0f;
        animateSinusoidFlag = true;
        ComputeAndDrawSinusoid();

        // When the program begins, we are not animating the sinusoid.
        // We only animate it after the animate button is pressed.
        animateSinusoidFlag = false;

    }

    // Update is called once per frame
    void Update()
    {
            ComputeAndDrawSinusoid();
    }

    private void ComputeAndDrawSinusoid()
    {
        float currentTime = Time.time;
        if (animateSinusoidFlag && (currentTime < endTime))
        {
            float timeSinceStart = Time.time - startTime;
            float stepSize = (rightEdgeOfSinusoidInViewportCoords - leftEdgeOfSinusoidInViewportCoords) / numberOfSinusoidPointsLessOne;
            for (int index = 0; index <= numberOfSinusoidPointsLessOne; index++)
            {
                float indexAsFloat = (float)index;
                float lateralPosition = leftEdgeOfSinusoidInViewportCoords + indexAsFloat * stepSize;
                float timeOfPosition = timeSinceStart + secondsOfDataToIncludeInWindow * (indexAsFloat / (float)numberOfSinusoidPointsLessOne); //secondsOfDataToIncludeInWindow (seconds) will be displayed in the window
                currentPhaseOfSinWave = 2 * Mathf.PI * timeOfPosition * frequencyOfSineWave;
                float sinusoidValueAtThisTime = verticalMidpointOfSinusoidInViewportCoords + amplitudeOfSinWaveInViewportCoords * Mathf.Sin(2 * Mathf.PI * timeOfPosition * frequencyOfSineWave);
                Vector3 sinusoidPointInViewportCoords = new Vector3(lateralPosition, sinusoidValueAtThisTime, transform.position.z);
                Vector3 sinusoidPointPositionWorldCoords = sceneCamera.ViewportToWorldPoint(sinusoidPointInViewportCoords);
                sinusoidPointPositions[index] = sinusoidPointPositionWorldCoords;
            }

            //set the line renderer to draw the computed points
            lineRenderer.SetPositions(sinusoidPointPositions);

            //note that we now have valid sinusoid data 
            hasValidSinusoidDataFlag = true;

            // Set the sinusoid indicator value (sits on sinusoid in the middle of the screen, providing a target to track).
            sinusoidCurrentValueIndicatorScript.updateIndicatorPosition(GetMiddleSinusoidPosition());
        }
        else // then we should not be animating the sinusoid
        {
            if(animateSinusoidFlag == true) // if we just stopped animating it (desired # of cycles were displayed)
            {
                // Set the flag indicating we're no longer animating it
                animateSinusoidFlag = false;

                // Tell the level manager that the animation is over
                levelManagerScript.sinusoidWaveformHasFinishedCertainNumberOfCycles();

            }

        }

    }

    public Vector3 GetMiddleSinusoidPosition()
    {
        if (hasValidSinusoidDataFlag)
        {
            int middleIndex = numberOfSinusoidPointsLessOne / 2;
            return sinusoidPointPositions[middleIndex];
        }
        else //if we don't have valid data yet, return dummy values
        {
            return new Vector3(-1.0f, -1.0f, -1.0f);
        }

    }

    public (float, float, float) GetSinusoidBasicInformationInViewportCoords()
    {
        int middleIndex = numberOfSinusoidPointsLessOne / 2;
        float stepSize = (rightEdgeOfSinusoidInViewportCoords - leftEdgeOfSinusoidInViewportCoords) / numberOfSinusoidPointsLessOne;
        float middleLateralDirectionViewportCoords = leftEdgeOfSinusoidInViewportCoords + middleIndex * stepSize;

        return (middleLateralDirectionViewportCoords, verticalMidpointOfSinusoidInViewportCoords, amplitudeOfSinWaveInViewportCoords);
    }

    public void startSinusoidForAGivenNumberOfCycles(int numberOfCyclesToAnimate)
    {
        startTime = Time.time; //get the time when the sinusoid begins
        endTime = startTime + numberOfCyclesToAnimate * (1.0f / frequencyOfSineWave);
        animateSinusoidFlag = true;
    }

    public (bool, float, float) getCurrentPhaseOfTargetSinusoidAsTargetAnkleAngle()
    {
        (bool successfulFetchOfAnkleData, string activeAnkle, float minimumAnkleAngle,
            float maximumAnkleAngle) = ankleRangeOfMotionFinderScript.getActiveAnkleIdentifierAndRangeOfMotion();

        // return values
        float targetAnkleAnkle = Mathf.Infinity; // if no valid target ankle angle, return infinity
        float phaseModuloTwoPi = Mathf.Infinity; // if no valid phase is obtained, return infinity
        if (successfulFetchOfAnkleData)
        {
            float activeAnkleRom = maximumAnkleAngle - minimumAnkleAngle;
            phaseModuloTwoPi = currentPhaseOfSinWave % (2 * Mathf.PI);
            float ankleRomMidpoint = (maximumAnkleAngle + minimumAnkleAngle) / (2.0f);
            targetAnkleAnkle = ankleRomMidpoint + (activeAnkleRom/2.0f) * Mathf.Sin(phaseModuloTwoPi);
        }

        return (successfulFetchOfAnkleData, targetAnkleAnkle, phaseModuloTwoPi);
    }
}
