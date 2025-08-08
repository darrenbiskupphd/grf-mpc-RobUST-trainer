using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class ProvideOnscreenTextFeedbackScript : MonoBehaviour

{
    //total points earned feedback
    public Text totalPointsEarnedTextObjectOnCanvas; 
    private float totalPointsEarned;
    private string totalPointsEarnedString;
    private string totalPointsConstantStub = "Points: ";

    //trial-specific feedback
    public Text trialSpecificFeedbackTextObjectOnCanvas;
    private float durationToDisplayTrialSpecificFeedbackInSeconds = 2.5f;


    // Start is called before the first frame update
    void Start()
    {
        // Hide the trial-specific feedback Text object at the start
        trialSpecificFeedbackTextObjectOnCanvas.gameObject.SetActive(false);

        // The player starts the block with zero points earned
        SetTotalPointsEarned(0.0f);
    }



    // Update is called once per frame
    void Update()
    {
        
    }



    // Function to update the total points earned by the player
    public void SetTotalPointsEarned(float totalPointsEarnedToDisplay)
    {
        // Format a total points string
        totalPointsEarnedString = totalPointsConstantStub + totalPointsEarnedToDisplay.ToString("0.0");

        // Set the total points feedback text object's text to the string
        totalPointsEarnedTextObjectOnCanvas.text = totalPointsEarnedString;
    }



    // Flash a passed-in trial-specific feedback string to the user for some period of time.
    public void DisplayTrialSpecificTextFeedback(string trialSpecificFeedbackStringToDisplay)
    {
        // Ensure that the trial-specific feedback Text object is active
        trialSpecificFeedbackTextObjectOnCanvas.gameObject.SetActive(true);

        // Set the trial specific feedback text object's text to the passed-in string
        trialSpecificFeedbackTextObjectOnCanvas.text = trialSpecificFeedbackStringToDisplay;

        // Start the couroutine that will inactivate/hide the trial-specific feedback text object after a certain period of time
        StartCoroutine("DisplayTrialSpecificFeedbackForASpecifiedDuration", durationToDisplayTrialSpecificFeedbackInSeconds);

    }

    // The coroutine function that waits for a certain period, then inactivates/hides the trial-specific 
    // feedback text object. Called to display trial-specific feedback for a limited amount of time.
    public IEnumerator DisplayTrialSpecificFeedbackForASpecifiedDuration(float timeToDisplayTrialSpecificFeedbackInSeconds)
    {
        yield return new WaitForSeconds(timeToDisplayTrialSpecificFeedbackInSeconds);
        //after the post-collision period ends, hide the feedback text
        trialSpecificFeedbackTextObjectOnCanvas.gameObject.SetActive(false);

    }
}
