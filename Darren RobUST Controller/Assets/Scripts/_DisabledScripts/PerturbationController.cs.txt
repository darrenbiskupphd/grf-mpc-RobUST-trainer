using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class PerturbationController : MonoBehaviour
{

    // The camera 
    public Camera camera;

    // The perturbation button on-screen
    public Button perturbationStartButton;

    // The level manager game object and script
    public GameObject levelManagerObject;
    private PerturbationLevelManager levelManagerScript;

    // The force field high-level controller. 
    // The perturbation controller has to "assert control" over the high-level controller
    // to set force field forces. 
    public GameObject highLevelControllerObject;
    private ForceFieldHighLevelControllerScript forceFieldHighLevelControllerScript;

    // The subject-specific info storage object.
    // Stores subject parameters like mass, height, subject number, gender.
    public GameObject subjectSpecificDataObject;
    private SubjectInfoStorageScript subjectSpecificDataScript;


    // A stopwatch to monitor time since the last perturbation began
    private Stopwatch perturbationStopwatch = new Stopwatch();

    // Perturbation-defining variables
    private float gravityConstant = 9.81f;
    private float perturbationForceRiseTimeInMs = 200.0f;
    private float perturbationForcePlateauTimeInMs = 400.0f;
    private float perturbationForceFallTimeInMs = 200.0f;
    public float desiredPercentBodyWeightForPerturbationForce; // the fraction of total subject body weight we want to apply as plateau pert. force
    private float perturbationForcePlateauMagnitudeInNewtons; // This is set in Start() based on body weight %

    // Derived perturbation variables. 
    // Computed in Start()
    private float totalPerturbationTimeInMilliseconds; // The total perturbation time = rising + plateau + falling
    private float risingTimeSlopeNewtonsPerMillisecond; // The slope of the rising period of the trapezoidal perturbation profile in Newtons/ms.
    private float fallingTimeSlopeNewtonsPerMillisecond; // The slope of the falling period of the trapezoidal perturbation profile in Newtons/ms.

    // Ongoing perturbation characteristics
    private Vector3 perturbationDirectionThisTrialAsUnitVectorViconFrame; // the desired perturbation force direction this trial,
                                                                          // expressed as a unit vector in Vicon frame
    private Vector3 currentDesiredPerturbationForceViconFrame; // the current desired force direction and magnitude for an ongoing perturbation


    // Start is called before the first frame update
    void Start()
    {
        //perturbationStartButton.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.8f, 0.0f));

        // Get a reference to the Perturbation level manager script
        levelManagerScript = levelManagerObject.GetComponent<PerturbationLevelManager>();

        // Get a reference to the force field high-level controller script
        forceFieldHighLevelControllerScript = highLevelControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();

        // Get the subject-specific data script
        subjectSpecificDataScript = subjectSpecificDataObject.GetComponent<SubjectInfoStorageScript>();

        // Get the total perturbation time
        totalPerturbationTimeInMilliseconds = perturbationForceRiseTimeInMs + perturbationForcePlateauTimeInMs + perturbationForceFallTimeInMs;

        // Get the perturbation plateau force, which is based on the subject's body weight.
        perturbationForcePlateauMagnitudeInNewtons = desiredPercentBodyWeightForPerturbationForce * 
            subjectSpecificDataScript.getSubjectMassInKilograms() * gravityConstant;

        // Compute the perturbation rising and falling time slopes
        ComputePerturbationRisePeriodAndFallPeriodSlopes();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // If the perturbation stopwatch is running
        if (perturbationStopwatch.IsRunning)
        {
            if(perturbationStopwatch.ElapsedMilliseconds < totalPerturbationTimeInMilliseconds) // If the perturbation should still be ongoing
            {
                // Compute the current desired force based on elapsed time since the perturbation started
                // and the desired perturbation direction. 
                // By storing desired force to an instance variable, it is made accessible to the force field
                // high-level controller.
                // First, get the desired force magnitude.
                float desiredForceMagnitudeForOngoingPerturbation = ComputeOngoingPerturbationDesiredForceMagnitude();
                // The desired force vector is the desired force magnitude multiplied by the perturbation direction unit vector. 
                currentDesiredPerturbationForceViconFrame = desiredForceMagnitudeForOngoingPerturbation *
                    perturbationDirectionThisTrialAsUnitVectorViconFrame;
            }
            else // if the perturbation has ended
            {
                // Tell the force field high-level controller that the perturbation is over
                forceFieldHighLevelControllerScript.ExitTrunkPerturbationMode();
                // Tell the level manager the perturbation is over
                levelManagerScript.PerturbationHasEndedEvent();
                // Reset the perturbation stopwatch
                perturbationStopwatch.Reset();
            }
        }
    }

    private void ComputePerturbationRisePeriodAndFallPeriodSlopes()
    {
        risingTimeSlopeNewtonsPerMillisecond = perturbationForcePlateauMagnitudeInNewtons / perturbationForceRiseTimeInMs;
        fallingTimeSlopeNewtonsPerMillisecond = -perturbationForcePlateauMagnitudeInNewtons / perturbationForceFallTimeInMs;
    }

    public void StartPerturbationResponseFunction()
    {
        // Only if the level manager is in the ready for perturbation state AND there is not an active perturbation
        Debug.Log("Perturbation requested and received by pertController.");

        if (levelManagerScript.IsLevelManagerInReadyForPerturbationState() && !perturbationStopwatch.IsRunning)
        {
            Debug.Log("Perturbation allowed by level manager.");

            // Tell the force field high-level controller that a perturbation has started, so desired forces should be retrieved from the 
            // perturbation controller (this script) instead of being computed. 
            // Get a response flag indicating if the high-level controller allowed this (deemed it safe).
            bool enteredPertModeSuccess = forceFieldHighLevelControllerScript.EnterTrunkPerturbationModeIfAllowed();

            // If we were able to assert control over the high-level controller (i.e. safe to apply perturbation)
            if (enteredPertModeSuccess)
            {
                Debug.Log("Perturbation allowed by force field high level controller. Entering pert mode");
                // Retrieve the current desired perturbation direction from the level manager.
                // The direction is specified as a unit vector in Vicon frame.
                perturbationDirectionThisTrialAsUnitVectorViconFrame =
                    levelManagerScript.GetCurrentDesiredPerturbationDirectionAsUnitVectorInViconFrame();
                Debug.Log("Desired pert. direction unit vector: (" + perturbationDirectionThisTrialAsUnitVectorViconFrame.x + "," +
                    perturbationDirectionThisTrialAsUnitVectorViconFrame.y + "," +
                    perturbationDirectionThisTrialAsUnitVectorViconFrame.z + ")");

                // Tell the level manager a perturbation has started
                levelManagerScript.PerturbationHasStartedEvent();

                // Start the perturbation stopwatch so we can track elapsed time
                perturbationStopwatch.Start();
            }

        }
    }


    // Called by the high-level controller when a perturbation is active. 
    public Vector3 GetCurrentPerturbationForceVectorViconFrame()
    {
        return currentDesiredPerturbationForceViconFrame;
    }


    private float ComputeOngoingPerturbationDesiredForceMagnitude()
    {
        // Get the current time elapsed since the perturbation start
        float timeElapsedSincePertStartInMs = perturbationStopwatch.ElapsedMilliseconds;

        // Initialize the computed perturbation force magnitude in Newtons
        float desiredPertForceMagnitudeInNewtons = 0.0f;

        // If the time is less than the end of the rise time
        if (timeElapsedSincePertStartInMs < perturbationForceRiseTimeInMs)
        {
            desiredPertForceMagnitudeInNewtons = risingTimeSlopeNewtonsPerMillisecond * timeElapsedSincePertStartInMs;
        }
        else if ((timeElapsedSincePertStartInMs >= perturbationForceRiseTimeInMs) &&
                  (timeElapsedSincePertStartInMs < perturbationForceRiseTimeInMs + perturbationForcePlateauTimeInMs))
        {
            desiredPertForceMagnitudeInNewtons = perturbationForcePlateauMagnitudeInNewtons;
        }else if (timeElapsedSincePertStartInMs >= perturbationForceRiseTimeInMs + perturbationForcePlateauTimeInMs &&
                  timeElapsedSincePertStartInMs < perturbationForceRiseTimeInMs + perturbationForcePlateauTimeInMs + perturbationForceFallTimeInMs)
        {
            float millisecondsSinceFallPeriodStart = timeElapsedSincePertStartInMs - (perturbationForceRiseTimeInMs + perturbationForcePlateauTimeInMs);
            desiredPertForceMagnitudeInNewtons = perturbationForcePlateauMagnitudeInNewtons + 
                millisecondsSinceFallPeriodStart * fallingTimeSlopeNewtonsPerMillisecond;
        }
        else
        {
            desiredPertForceMagnitudeInNewtons = 0.0f;
        }

        // Ensure the desired force magnitude is positive and less than the plateau force magnitude
        if(desiredPertForceMagnitudeInNewtons < 0.0f)
        {
            desiredPertForceMagnitudeInNewtons = 0.0f;
        }
        if(desiredPertForceMagnitudeInNewtons > perturbationForcePlateauMagnitudeInNewtons)
        {
            desiredPertForceMagnitudeInNewtons = perturbationForcePlateauMagnitudeInNewtons;
        }

        // Return the desired perturbation force magnitude
        return desiredPertForceMagnitudeInNewtons;
    }



}
