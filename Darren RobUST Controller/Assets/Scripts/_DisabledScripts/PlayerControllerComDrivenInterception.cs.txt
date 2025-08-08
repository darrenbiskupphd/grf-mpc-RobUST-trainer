using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerControllerComDrivenInterception : MonoBehaviour
{
    //public variables
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
    private bool playerInactivatedForEndOfTrialFeedbackFlag = false;
    //center of mass manager 
    private bool centerOfMassManagerReadyStatus = false; //set to true when the COM manager object is ready to serve up COM position data
    //private Animator playerAnimator;
    public LevelManager gameLevelManager;
    //striking
    public float targetKnockbackScaling; //a key parameter. Multiplied by player impact velocity to determine target knockback distance.
    public float impactSmoothing;
    private Vector3 playerPostCollisionFinalKnockonPosition; //post-collision, the player will drift to this position to simulate momentum
    public float targetImpactInactivationTimeSeconds;
    public float minimumStrikeSpeed;
    public float maximumStrikeSpeed;
    public float playerKnockonDistance;
    //strike zone
    public GameObject strikeZone;

    //COM control type (depends on scene)
    public string mapFromComToUnityPositionSpecifierString; //"Standard" means full boundary of support maps to screen, "Interception" for interception game, 

    //testing with keyboard input
    public bool usingKeyboardToControlPlayer; // if we want to control our "player" with keyboard inputs instead of with COM data, set to true. Great for testing.


    // Start is called before the first frame update
    void Start()
    {
        //find the player respawn point
        respawnPointInWorldCoords = convertViewportCoordinateToWorldCoordinate(respawnPointInViewportCoords);

        //initialize the player at the respawn point 
        transform.position = respawnPointInWorldCoords;

        //Get some needed components from this GameObject (the parent of this script)
        rigidBody = GetComponent<Rigidbody2D>();
        collider = GetComponent<BoxCollider2D>();

        //find other GameObjects and their components
        gameLevelManager = FindObjectOfType<LevelManager>();
        centerOfMassManagerScript = centerOfMassManager.GetComponent<ManageCenterOfMassScript>();
    }

    // Update is called once per frame
    void Update()
    {
        if (!playerInactivatedForEndOfTrialFeedbackFlag) { //if the player is not inactivated for end-of-trial feedback, we can move

            //if we're using keyboard input to control velocity (i.e. testing!).
            if(usingKeyboardToControlPlayer)
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
                    Debug.Log("Player game object updating position based on COM.");
                    updatePlayerPositionBasedOnComPosition(); // then let's use COM data to drive our player position
                }
            }
           
            


        }
        else //if the player is currently "stunned" after a target collision and drifting to a knock-on position
        {
            //animate a slow drift to the final post-impact player position
            transform.position = Vector3.Lerp(transform.position, playerPostCollisionFinalKnockonPosition, impactSmoothing * Time.deltaTime);
        }
    }

    //update the player position so that it matches the center of mass (COM) position of the subject
    private void updatePlayerPositionBasedOnComPosition()
    {

        Vector3 subjectComInViconCoordinates = centerOfMassManagerScript.getSubjectCenterOfMassInViconCoordinates();

        // Map the COM position from Vicon frame to Unity frame by calling the task-specific mapping function in the level manager
        Vector3 subjectComInUnityCoordinates;
        subjectComInUnityCoordinates = gameLevelManager.mapPointFromViconFrameToUnityFrame(subjectComInViconCoordinates);

        // Update the player position to match the COM position mapped into Unity frame
        //Debug.Log("Updating player position to (x,y): (" + subjectComInUnityCoordinates.x + ", " + subjectComInUnityCoordinates.y + ")");
        transform.position = subjectComInUnityCoordinates;      
    }



    private Vector3 convertViewportCoordinateToWorldCoordinate(Vector3 positionInViewportCoordinates)
    {
        Vector3 positionInWorldCoords = sceneCamera.ViewportToWorldPoint(positionInViewportCoordinates);
        
        //keep the z-axis coordinate the same, as we want all of our 2D objects to remain in the same plane
        return new Vector3(positionInWorldCoords.x, positionInWorldCoords.y, transform.position.z);
    }



    public void setTypeOfCenterOfMassToUnityControlBySpecifierString(string controlType)
    {
        mapFromComToUnityPositionSpecifierString = controlType;
    }




    public void setTargetStrikeKnockbackScalingFactor(float knockbackScaling)
    {
        targetKnockbackScaling = knockbackScaling;
        Debug.Log("Setting target knockback scaler to: " + knockbackScaling);
    }



    public bool GetPlayerImpactStunnedStatus()
    {
        return playerInactivatedForEndOfTrialFeedbackFlag;
    }



    public Vector3 returnRespawnPoint()
    {
        return respawnPointInWorldCoords;
    }

    public bool getPlayerBeingControlledByKeyboardStatus()
    {
        return usingKeyboardToControlPlayer;
    }



    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.tag == "Target") //if the player has struck a target in the "Interception" task
        {
            // Let the level manager handle the interaction
            gameLevelManager.handlePlayerStrikingTargetEvent(other);
        }
    }


    public void BeginPostTargetCollisionResponse(Vector3 playerPostCollisionKnockonPosition)
    {
        // Set the flag indicating the player is "stunned" after the collision. This means it will not 
        // be repositioned based on the COM position (or keyboard commands) for some time so that we can 
        // provide visual feedback to the subject.
        playerInactivatedForEndOfTrialFeedbackFlag = true;

        // Set the post-collision knock-on position. Lerping to this position after collision provides the illusion of momentum after the collision.
        playerPostCollisionFinalKnockonPosition = playerPostCollisionKnockonPosition;

    }



    //This function is called by the level manager (after the target calls the level manager) when the player fails to strike the target (so the target is missed). 
    //Call the coroutine that allows for a feedback delay and then ends the trial.
    public void TargetMissedResponse()
    {
        // Set the flag indicating the player is inactivated after the target miss. This means it will not 
        // be repositioned based on the COM position (or keyboard commands) for some time so that we can 
        // provide visual feedback to the subject.
        playerInactivatedForEndOfTrialFeedbackFlag = true;
    }


    // This function is called by the level manager at the end of the end-of-trial feedback period. 
    // It activates the player (tracks COM or becomes responsive to keyboard).
    public void StopTheEndOfTrialFeedbackPeriod()
    {
        //set a flag re-enabling user input 
        playerInactivatedForEndOfTrialFeedbackFlag = false;

        //ensure at this point that the player velocity is zero 
        rigidBody.velocity = new Vector2(0, 0);
    }


}

