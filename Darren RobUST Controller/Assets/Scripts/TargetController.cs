using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetController : MonoBehaviour
{
    public LevelManager gameLevelManager;
    public float targetSpeed; //how fast the target moves on approach
    private float lateralMovement = 0f; // 
    private float verticalMovement = 0f; // 
    private bool advancingForward = false; //if the target is moving left or "attacking"
    private bool travelingLeft; // the current direction of travel, left (true) or right (false). Only matters if we're currently advancing.
    //respawn
    public Vector3 respawnPoint;
    //impact and knockback
    private Vector3 targetKnockbackPositionAfterCollision; //if getting knocked back after a collision, where the target will be knocked back to
    private bool knockbackFlag = false; //indicates if the target is currently being knocked back after a collision
    public float knockbackInactivationTimeSeconds;
    public float successTargetKnockbackDistance;
    public float failTargetKnockbackDistance;
    private Rigidbody2D rigidBody;
    public float impactSmoothing;
    //Missed target 
    private float currentTargetMissLineXAxisPositionUnityFrame;
    private bool targetHasMissedFlag; // a flag indicating whether or not the target has been "missed" already by the player this trial

    // Start is called before the first frame update
    void Start()
    {
        //get references to needed GameObjects and other components of this GameObject
        gameLevelManager = FindObjectOfType<LevelManager>();
        rigidBody = GetComponent<Rigidbody2D>();
        //set the respawn point for this target as its initial position
        respawnPoint = transform.position;
        //set the X-position at which the target is deemed a "miss"

    }

    // Update is called once per frame
    void Update()
    {
        if (knockbackFlag) //if actively being knocked back after a collision
        {
            transform.position = Vector3.Lerp(transform.position, targetKnockbackPositionAfterCollision, impactSmoothing * Time.deltaTime);
        }

        //check if the target is past the strike zone and has been deemd a "miss"
        CheckIfTargetMissed();


    }

    // This function isn't doing anything right now, but might be a useful stub to keep.
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.tag == "Player") //if the player has collided with this target
        {
        }

    }

    public bool getActiveStatus()
    {
        return advancingForward; //the target is "active" when it is advancing forward
    }

    public void StartMovement(float targetSpeed, bool travelingLeftwards, float xAxisLocationAtWhichAMissIsDeterminedUnityFrame) //called by LevelManager
    {
        advancingForward = true; //indicate that the target is advancing forward /attacking
        currentTargetMissLineXAxisPositionUnityFrame = xAxisLocationAtWhichAMissIsDeterminedUnityFrame;
        travelingLeft = travelingLeftwards;

        if (travelingLeftwards) // If the target will be moving to the left
        {
            Debug.Log("Target received command to move left with speed: " + targetSpeed);

            rigidBody.velocity = new Vector2(-targetSpeed, 0); //the target moves left, i.e. has a negative x-axis velocity
        }
        else // If the target will be moving to the right
        {
            Debug.Log("Target received command to move right with speed: " + targetSpeed);
            rigidBody.velocity = new Vector2(targetSpeed, 0); //the target moves right, i.e. has a positive x-axis velocity
        }
    }

    public bool returnAdvancingStatus()
    {
        return advancingForward;
    }

    public bool returnWhetherOrNotTargetHasBeenMissedThisTrialFlag()
    {
        return targetHasMissedFlag;
    }

    public Vector3 updateXPositionAtImpact(float targetImpactXPositionBasedOnRigidBodies)
    {
        transform.position = new Vector3(targetImpactXPositionBasedOnRigidBodies, transform.position.y, transform.position.z);
        return transform.position;
    }

    public void RespondToCollisionWithPlayer(Vector3 postCollisionKnockbackPosition, bool inStrikeZone)
    {
        Debug.Log("Active target hit by player. Knockback.");

        // Set a flag indicating that the target is no longer active/advancing
        advancingForward = false;

        // Set the target's rigid body velocity to zero
        rigidBody.velocity = new Vector2(0, 0); //the target velocity drops to zero after impact                                         
          
        // Set the target's post-collision knockback position, which was computed by the level manager
        targetKnockbackPositionAfterCollision = postCollisionKnockbackPosition; 

        //set a flag indicating this target is currently being knocked back
        knockbackFlag = true;
    }

    //This function checks if the target has passed beyond the strike zone and has been deemed a "miss". 
    //If so, that trial should end.
    private void CheckIfTargetMissed()
    {
        // If the velocity is positive, we know the target started on the left side and is traveling right,
        // which implies leftward movements for the subject. Thus, use the leftwards boundary for detecting a miss.
        if (advancingForward && travelingLeft) // If this target is active and traveling left 
        {
            if ((transform.position.x < currentTargetMissLineXAxisPositionUnityFrame) && (targetHasMissedFlag == false)) //if the target is beyond the "miss" boundary
            {
                Debug.Log("Target missed.");

                targetHasMissedFlag = true;

                //tell the level manager that the target was missed. The level manager will manage the Player game object response and end the trial.
                gameLevelManager.TargetMissedResponse();
            }
        }
        else if (advancingForward && !travelingLeft) // If this target is active and traveling right 
        {
            if ((transform.position.x > currentTargetMissLineXAxisPositionUnityFrame) && (targetHasMissedFlag == false)) //if the target is beyond the "miss" boundary
            {
                Debug.Log("Target missed.");

                targetHasMissedFlag = true;

                //tell the level manager that the target was missed. The level manager will manage the Player game object response and end the trial.
                gameLevelManager.TargetMissedResponse();
            }
        }


    }

    public void StopTheEndOfTrialFeedbackPeriod()
    {
        //set the flag indicating that the post-collision knockback period is over
        knockbackFlag = false;
        //ensure that the target missed flag is set to false 
        targetHasMissedFlag = false;
        //ensure at this point that the target is not advancing forward and has velocity zero 
        advancingForward = false; //the target is no longer attacking after impact
        rigidBody.velocity = new Vector2(0, 0); //the target velocity drops to zero after impact 
    }
}
