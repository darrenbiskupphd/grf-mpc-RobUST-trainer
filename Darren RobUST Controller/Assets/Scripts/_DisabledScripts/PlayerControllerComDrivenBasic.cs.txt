using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerControllerComDrivenBasic : MonoBehaviour
{
    //public variables
    public bool playerNeededForThisTask;
    public float playerSpeed = 5f; //units are...
    public GameObject centerOfMassManager; //the GameObject that updates the center of mass position (also has getter functions).
    private ManageCenterOfMassScript centerOfMassManagerScript; //the script with callable functions, belonging to the center of mass manager game object.
    public Camera sceneCamera; //the camera that visualizes this scene

    //private variables
    private float lateralMovement = 0f; // 
    private float verticalMovement = 0f; // 
    private BoxCollider2D collider; //this GameObject's collider
    private Rigidbody2D rigidBody; //this GameObject's rigid body 2d
    public Vector3 respawnPointInViewportCoords;
    private Vector3 respawnPointInWorldCoords; //stores the respawn point in world coords, which was converted from viewport coords using the scene camera
    //center of mass manager 
    private bool centerOfMassManagerReadyStatus = false; //set to true when the COM manager object is ready to serve up COM position data
    //private Animator playerAnimator;
    public LevelManagerScriptAbstractClass gameLevelManager;

    // Force plate data access
    public GameObject forcePlateDataAccessObject;
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;
    private bool isForcePlateDataReadyForAccess; // whether or not the force plate data script is ready to distribute data.

    // Bools to override the default COM position determined by the COM Manager. 
    // For example, we can let the subject control their onscreen position with the COP or 
    // just the center of pelvis. Note, if multiple are set to true, only one will be used as 
    // determined arbitrarily in this script. 
    public bool useCenterOfPressureControl; // If we want the user to interact with the tasks via force plate inputs
    public bool useCenterOfPelvisControl; // If we want the user to interact with their pelvis position, as opposed to the multi-segment COM. 


    //testing with keyboard input
    public bool usingKeyboardToControlPlayer; // if we want to control our "player" with keyboard inputs instead of with COM data, set to true. Great for testing.


    // Start is called before the first frame update
    void Start()
    {
        if(playerNeededForThisTask == true)
        {
            //find the player respawn point
            respawnPointInWorldCoords = convertViewportCoordinateToWorldCoordinate(respawnPointInViewportCoords);

            //initialize the player at the respawn point 
            transform.position = respawnPointInWorldCoords;

            //Get some needed components from this GameObject (the parent of this script)
            rigidBody = GetComponent<Rigidbody2D>();
            collider = GetComponent<BoxCollider2D>();

            //find other GameObjects and their components
            gameLevelManager = FindObjectOfType<LevelManagerScriptAbstractClass>();
            centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();
            scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();
        }

    }

    // Update is called once per frame
    void Update()
    {
        if (playerNeededForThisTask == true)
        {
            //if we're using keyboard input to control velocity (i.e. testing!).
            if (usingKeyboardToControlPlayer)
            {
                if (Input.GetKeyUp("right"))
                {
                    rigidBody.velocity = new Vector2(rigidBody.velocity.x + playerSpeed, rigidBody.velocity.y); //specify x speed for the player. Y-axis speed is unchanged.
                }
                if (Input.GetKeyUp("left"))
                {
                    rigidBody.velocity = new Vector2(rigidBody.velocity.x - playerSpeed, rigidBody.velocity.y); //specify x speed for the player. Y-axis speed is unchanged.
                }
                if (Input.GetKeyUp("up"))
                {
                    rigidBody.velocity = new Vector2(rigidBody.velocity.x, rigidBody.velocity.y + playerSpeed); //specify y speed for the player. X-axis speed is unchanged.
                }
                if (Input.GetKeyUp("down"))
                {
                    rigidBody.velocity = new Vector2(rigidBody.velocity.x, rigidBody.velocity.y - playerSpeed); //specify y speed for the player. X-axis speed is unchanged.
                }
            }
            else //if we are using COM data to control the player position (the standard case)
            {
                //Get the subject center of mass to drive the player position
                if (centerOfMassManagerReadyStatus == false) //if the center of mass manager object is not currently ready to serve COM data
                {
                    centerOfMassManagerReadyStatus = centerOfMassManagerScript.getCenterOfMassManagerReadyStatus(); // see if it is ready now
                }
                else //if we can get COM data from the center of mass manager object
                {
                    // Default case: use the multi-segment COM as determined by the COM manager
                    if (useCenterOfPressureControl || useCenterOfPelvisControl) // if we want to use COP data from the force plates for subject interaction
                    {
                        useAlternateVisualFeedback();
                    }
                    else // THE DEFAULT, PREFERRED CASE! Use COM computed from a multisegment model - see the COM manager script.
                    {
                        updatePlayerPositionBasedOnControlPointPosition(); // then let's use multi-segment COM data to drive our player position
                    }
                }
            }
        }
    }
    //update the player position so that it matches the center of mass (COM) position of the subject
    private void updatePlayerPositionBasedOnControlPointPosition()
    {

        Vector3 subjectComInViconCoordinates = centerOfMassManagerScript.GetSelectedControlPointPositionInViconCoordinates(); //centerOfMassManagerScript.GetCenterOfTrunkBeltPositionInViconFrame();
        // Debug.Log("COM position in Vicon Coords (x,y): (" + subjectComInViconCoordinates.x + ", " + subjectComInViconCoordinates.y + ")");

        // Map the COM position from Vicon frame to Unity frame by calling the task-specific mapping function in the level manager
        Vector3 subjectComInUnityCoordinates;
        subjectComInUnityCoordinates = gameLevelManager.mapPointFromViconFrameToUnityFrame(subjectComInViconCoordinates);

        // Update the player position to match the COM position mapped into Unity frame
        //Debug.Log("Updating player position to (x,y): (" + subjectComInUnityCoordinates.x + ", " + subjectComInUnityCoordinates.y + ")");
        transform.position = subjectComInUnityCoordinates;
    }



    private void useAlternateVisualFeedback()
    {
        if (useCenterOfPressureControl)
        {
            if (!isForcePlateDataReadyForAccess)
            {
                isForcePlateDataReadyForAccess = scriptToRetrieveForcePlateData.getForcePlateDataAvailableViaDataStreamStatus();
            }
            else
            {
                Vector3 CopPositionViconFrame = scriptToRetrieveForcePlateData.getMostRecentCenterOfPressureInViconFrame();
                Vector3 CopPositionInUnityFrame = gameLevelManager.mapPointFromViconFrameToUnityFrame(CopPositionViconFrame);
                transform.position = new Vector3(CopPositionInUnityFrame.x, CopPositionInUnityFrame.y, transform.position.z);
            }
        }
        else if (useCenterOfPelvisControl)
        {
            Vector3 subjectCenterOfPelvisMarkersInViconCoordinates = centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();

            // Map the center of pelvis position from Vicon frame to Unity frame by calling the task-specific mapping function in the level manager
            Vector3 subjectCenterOfPelvisMarkersInUnityCoordinates;
            subjectCenterOfPelvisMarkersInUnityCoordinates = 
                gameLevelManager.mapPointFromViconFrameToUnityFrame(subjectCenterOfPelvisMarkersInViconCoordinates);

            // Update the player position to match the center of pelvis markers position mapped into Unity frame
            transform.position = new Vector3(subjectCenterOfPelvisMarkersInUnityCoordinates.x,
                subjectCenterOfPelvisMarkersInUnityCoordinates.y, transform.position.z);
        }
        else
        {
            updatePlayerPositionBasedOnControlPointPosition(); // default to the standard case if there is some error.
        }
    }


    private Vector3 convertViewportCoordinateToWorldCoordinate(Vector3 positionInViewportCoordinates)
    {
        Vector3 positionInWorldCoords = sceneCamera.ViewportToWorldPoint(positionInViewportCoordinates);

        //keep the z-axis coordinate the same, as we want all of our 2D objects to remain in the same plane
        return new Vector3(positionInWorldCoords.x, positionInWorldCoords.y, transform.position.z);
    }


    public bool getPlayerBeingControlledByKeyboardStatus()
    {
        return usingKeyboardToControlPlayer;
    }


    // A placeholder for now
    void OnTriggerEnter2D(Collider2D other)
    {

    }



}

