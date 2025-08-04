using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisualFeedbackForAnkleScript : MonoBehaviour
{

    public GameObject sinusoidGameObject;
    private DrawSinusoid sinusoidController;
    public GameObject ankleSceneLevelManager;
    private LevelManagerAnkleSinusoid ankleSceneLevelManagerScript;
    public GameObject ankleRangeOfMotionCalculator;
    private MeasureAnkleRomScript ankleRangeOfMotionCalculatorScript;
    private Camera sceneCamera; //the camera of this scene
    private float ankleMovementPercentOfRom = 0.90f; 


    // Start is called before the first frame update
    void Start()
    {
        sinusoidController = sinusoidGameObject.GetComponent<DrawSinusoid>();
        ankleSceneLevelManagerScript = ankleSceneLevelManager.GetComponent<LevelManagerAnkleSinusoid>();
        ankleRangeOfMotionCalculatorScript = ankleRangeOfMotionCalculator.GetComponent<MeasureAnkleRomScript>();
        sceneCamera = FindObjectOfType<Camera>();
    }

    // Update is called once per frame
    void Update()
    {
        findFeedbackIndicatorPosition();
    }


    private void findFeedbackIndicatorPosition()
    {
        //first, get the location of the middle of the sinusoid in viewport coords
        (float xAxisMiddlePosInViewportCoords, float yAxisMiddlePosInViewportCoords, float amplitudeOfSinWaveInViewportCoords) = sinusoidController.GetSinusoidBasicInformationInViewportCoords();

        //next, figure out the current ankle angle and map it onto the range of motion
        (bool successRetrievingAnkleAngle, float ankleAngle) = ankleSceneLevelManagerScript.getPlantarflexionAngleOfActiveAnkle();
        (bool validRomAvailable, string activeAnkle, float ankleRomMin, float ankleRomMax) = ankleRangeOfMotionCalculatorScript.getActiveAnkleIdentifierAndRangeOfMotion();

        //if possible, update the visual feedback for the current ankle angle
        //Debug.Log("updating feedback indicator. Boolean guards (ankle angle, valid ROM) are: ( " + successRetrievingAnkleAngle + ", " + validRomAvailable + " )");
        //Debug.Log("Visual feedback script: current ankle ROM (min, max, range) is: (" + ankleRomMin + ", " + ankleRomMax + ", " + (ankleRomMax - ankleRomMin) + " )");

        if (successRetrievingAnkleAngle && validRomAvailable) //if we can render the current ankle angle (have an ROM and a current ankle angle)
        {
            float currentAnkleAngleAsFractionOfAnkleRange = (ankleAngle - ankleRomMin) / (ankleRomMax - ankleRomMin);
            float fractionOfSinusoidAmplitudeFromCenter = 2 * (currentAnkleAngleAsFractionOfAnkleRange - 0.5f);

            // Scale so that the person doesn't have to move through their full ROM to track the sinusoid. 
            // Instead, they have to move through some fraction of their ROM.
            float fractionOfSinusoidAmplitudeFromCenterScaled = (1.0f / ankleMovementPercentOfRom) * fractionOfSinusoidAmplitudeFromCenter;
            float viewportYCoordForCurrentAnkleAngleIndicator = yAxisMiddlePosInViewportCoords + fractionOfSinusoidAmplitudeFromCenterScaled * amplitudeOfSinWaveInViewportCoords;

            Vector3 feedbackIndicatorPositionInViewportCoords = new Vector3(xAxisMiddlePosInViewportCoords, viewportYCoordForCurrentAnkleAngleIndicator, transform.position.z);
            Vector3 worldPositionOfFeedbackIndicator = sceneCamera.ViewportToWorldPoint(feedbackIndicatorPositionInViewportCoords);
            worldPositionOfFeedbackIndicator.z = transform.position.z;
            transform.position = worldPositionOfFeedbackIndicator;
        }
        
    }
}
