using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.XR;
//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below
using Debug = UnityEngine.Debug;

public class MovePlayerToPositionOnStartup : MonoBehaviour
{

    private bool playerPosSetupComplete = false; 

    //public GameObject punchingBagAnchor; // a reference to the punching bag anchor. Our starting position will be relative to its position.
    //public GameObject punchingBag; // a reference to the punching bag itself. 
    public GameObject vrCamera; // a reference to the VR camera (view of player)
    public bool reorientPlayerAtStartup; // whether to do the main function of this script (true) or not (false)
    private Valve.VR.InteractionSystem.Player playerScript;
    private float startingDistanceFromBag = 2.0f/3.0f; // How far from the bag the player starts [unity World units -> meters?]

    // The player's starting position and orientation
    private Quaternion playerStartingQuaternion; // the player's starting orientation as quaternion
    public Vector3 playerViewStartPosition; // where the HMD (vision of player) starts
    public Vector3 desiredGazeDirection; // a vector specifying the desired gaze direction when the player is looking straight ahead
    public Vector3 playersRightDirectionInGlobal; // a vector specifying the direction to the player's right when they're looking straight ahead

    // bool toggle to home the player position
    public bool playerToHomeToggle;
    private bool toggleHomeSetsOrientationFlag = false; // whether the toggle home button sets orientation (true) or not (false = the default case).
    private bool lastValuePlayerToHomeToggle;
    private bool toggleHomeHasOccurred = false; // this flag indicates if the player has been toggled home. We only allow a SINGLE toggle home.
    private Vector3 toggleHomeDistanceMoved; // how far the toggle home moved the player. This should be the player height,
                                             // if the headset starts on the floor and is toggled once on the head.
                                             // trial transition timer
    private Stopwatch waitToFlagToggleHomeTimer = new Stopwatch();
    private float waitToFlagToggleHomePeriodInMs = 15000.0f;

    // DEBUGGING ONLY!
    public bool useStartupDelayForSoloTesting = true;
    private Stopwatch startupDelayTimer = new Stopwatch();
    private float startupDelayTimeInMs = 15000.0f;

    // Start is called before the first frame update
    void Start()
    {
        // Get a reference to the Valve SteamVr "Player" script, which is just a script that extents MonoBehaviour.
        playerScript = gameObject.GetComponent<Valve.VR.InteractionSystem.Player>();

        // Set the last known value of the player to home toggle
        lastValuePlayerToHomeToggle = playerToHomeToggle;

        //startupDelayTimer.Start();
    }

    // Update is called once per frame
    void Update()
    {
        // If we haven't yet adjusted the player position and orientation and we'd like to do it automatically at startup.
        if (!playerPosSetupComplete && reorientPlayerAtStartup && 
            (useStartupDelayForSoloTesting == false || startupDelayTimer.ElapsedMilliseconds > startupDelayTimeInMs))
        {
            playerPosSetupComplete = adjustPlayerStartingOrientation();
            Debug.Log("Moving player HMD to starting position and gaze direction.");
        }

        // If the toggle home button has not previously been pressed
        if (lastValuePlayerToHomeToggle != playerToHomeToggle && (toggleHomeHasOccurred == false))
        {
            // If we did not request a toggle home delay
            if (useStartupDelayForSoloTesting == false)
            {
                // Immediately move the player HMD home
                if (toggleHomeSetsOrientationFlag == false)
                {
                    // translate the player to the home position (do not change orientation)
                    TranslatePlayerToHomeOnToggle();
                }
                else
                {
                    // translate the player to the home position (do not change orientation)
                    TranslateAndRotatePlayerToHomeOnToggle();
                }

                // update the last known toggle value
                lastValuePlayerToHomeToggle = playerToHomeToggle;

                // Mark that a toggle home has occurred so that further toggles home are not allowed.
                toggleHomeHasOccurred = true;
            }
            // Else if we did request a toggle home delay
            else
            {
                // If a toggle button press was not already registered and thus the delay timer is not yet running
                if(waitToFlagToggleHomeTimer.IsRunning == false)
                {
                    // Start the toggle home delay stopwatch from zero
                    waitToFlagToggleHomeTimer.Restart();
                }
            }
        }

        // If the toggle home delay timer is running and exceeded the wait time, then we 
        // should toggle the HMD to the home pos and mark that the toggle home occurred.
        if (waitToFlagToggleHomeTimer.ElapsedMilliseconds > waitToFlagToggleHomePeriodInMs)
        {
            // Reset the timer to zero
            waitToFlagToggleHomeTimer.Reset();

            // Immediately move the player HMD home
            if (toggleHomeSetsOrientationFlag == false)
            {
                // translate the player to the home position (do not change orientation)
                TranslatePlayerToHomeOnToggle();
            }
            else
            {
                // translate the player to the home position (do not change orientation)
                TranslateAndRotatePlayerToHomeOnToggle();
            }

            // Mark that a toggle home has occurred so that further toggles home are not allowed.
            toggleHomeHasOccurred = true;
        }
    }

    public void EnablePlayerReorientationFlag()
    {
        reorientPlayerAtStartup = true;
    }

    // In the case where we want the toggle home to move the headset to a home position and 
    // a defined orientation on toggle (e.g., we are tracking the headset position and orientation and moving the camera there)
    // then toggle the flag.
    public void EnableOrientationResetInToggleToHome()
    {
        toggleHomeSetsOrientationFlag = true;
    }

    // Set the desired player/HMD camera start position. 
    // NOTE: this won't actually cause a repositioning to occur - we have to manually toggle the player home in the editor.
    public void SetDesiredPlayerStartPosition(Vector3 desiredPlayerStartPosition)
    {
        // Set the desired player start position.
        // This may be called by the level manager in it's Start()
        playerViewStartPosition = desiredPlayerStartPosition;
    }

    // Set the desired player/HMD camera start orientation. 
    // NOTE: this won't actually cause a re-orientation to occur - we have to manually toggle the player home in the editor.
    // NOTE 2: toggling home will only affect orientation if the toggleHomeSetsOrientationFlag is set to true, which is not default behavior.
    public void SetDesiredPlayerOrientationInUnityFrame(Vector3 playerForwardDirectionInUnityFrame, Vector3 playerRightwardsDirectionInUnityFrame)
    {
        desiredGazeDirection = playerForwardDirectionInUnityFrame;
        playersRightDirectionInGlobal = playerRightwardsDirectionInUnityFrame;
    }

    // A function for other programs to programatically trigger a toggle home, instead of requiring a manual toggle. 
    // This can be useful if we want to toggle home from the level manager, which can simplify data flow.
    public void TriggerToggleToMoveCameraHome()
    {
        playerToHomeToggle = !playerToHomeToggle; // flip the value of the playerToHomeToggle variable, triggering a toggle home on the next Update() loop.
    }


    public bool IsCameraSetup()
    {
        return playerPosSetupComplete;
    }

    public (Vector3, Quaternion) GetNeutralPlayerOrientationAndStartingPosition()
    {
        return (playerViewStartPosition, playerStartingQuaternion);
    }




    public Vector3 GetToggleHmdToHomePositionOffsetVector()
    {
        return toggleHomeDistanceMoved;
    }

    public bool GetToggleHmdStatus()
    {
        return toggleHomeHasOccurred;
    }



    private bool adjustPlayerStartingOrientation()
    {

        bool setupSuccessBool = false; // the return value indicating whether or not we could correctly position the player

        // Get the punching bag anchor position
        //Vector3 punchingBagAnchorPosition = punchingBagAnchor.transform.position;

        // Set the desired player start position
        //playerStartPosition = punchingBag.transform.position;

        // Bag target swing directions are symmetrically arranged around the negative global z-axis. 
        // Therefore, the player should be displaced positively along the global z-axis from the anchor, 
        // facing along the negative z-axis. 
        //playerViewStartPosition = punchingBag.transform.position + new Vector3(0.0f, 0.0f, startingDistanceFromBag);
        Debug.Log("Player starting position x,y,z: (" + playerViewStartPosition.x + ", " + playerViewStartPosition.y + ", " + playerViewStartPosition.z + ")");

        // We must add (or subtract?) the offset from the head tracker to the XR "head" component (which is where the VR camera is)
        var headTrackingDevices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesAtXRNode(UnityEngine.XR.XRNode.Head, headTrackingDevices);
        Debug.Log("Head tracking device Count is: " + headTrackingDevices.Count);
        if (headTrackingDevices.Count > 0)
        {
            if (reorientPlayerAtStartup)
            {
                Debug.Log("Size of headTrackingDevices list = " + headTrackingDevices.Count);


                // First get the rotation from the orienting tracker to the headset. 
                // This will ultimately be used to preserve starting headset misalignment.
                //Quaternion rotationFromOrientingTrackerToHeadset = Quaternion.FromToRotation(playerScript.hmdTransform.TransformDirection(new Vector3(0.0f, 1.0f, 1.0f)), orientingTracker.transform.TransformDirection(new Vector3(0.0f, 1.0f, 1.0f)));

                // Rotate player to achieve a desired VR camera gaze orientation
                RotatePlayerToAchieveCameraGazeOrientation();

                // Translate player to achieve a desired VR camera gaze position
                bool recordTranslationAsToggleHome = false;
                TranslatePlayerToAchieveCameraPosition(recordTranslationAsToggleHome); // we don't record the start-up repositioning as the toggle home translation
            }

            setupSuccessBool = true;
        }
        else
        {// if we can't yet retrieve data from the Vive headset, controllers, or trackers
            setupSuccessBool = false;
        }
        
        return setupSuccessBool;
    }


    private void RotatePlayerToAchieveCameraGazeOrientation()
    {
        // Get the rotation from player frame to camera frame and from camera frame to desired camera frame
        Debug.Log("VR Camera orientation before: (" + transform.rotation.eulerAngles.x + ", " + transform.rotation.eulerAngles.y + ", " + transform.rotation.eulerAngles.z + ")");
        Debug.Log("VR Camera desired forward gaze direction: (" + desiredGazeDirection.x + ", " + desiredGazeDirection.y + ", " + desiredGazeDirection.z + ")");
        //Quaternion rotationFromPlayerOrientationToCameraOrientation = Quaternion.FromToRotation(transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f)), playerScript.hmdTransform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f)));
        Quaternion rotationToDesiredCameraGazeDirection = Quaternion.FromToRotation(vrCamera.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f)), desiredGazeDirection);

        // First rotate the player to point the camera in the correct direction
        // Note that this doesn't fully constrain the system, e.g. we could be upside down.
        Quaternion intermediatePlayerOrientation = rotationToDesiredCameraGazeDirection * transform.rotation;
        transform.rotation = intermediatePlayerOrientation;

        // Next rotate the player to set the camera such that right is right, left is left, etc.
        // This can be accomplished by one more rotation
        Quaternion rotationToDesiredCameraOrientationAboutGazeDirection = Quaternion.FromToRotation(vrCamera.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f)), playersRightDirectionInGlobal);
        Quaternion playerStartingOrientationToOrientCameraProperly = rotationToDesiredCameraOrientationAboutGazeDirection * transform.rotation;
        transform.rotation = playerStartingOrientationToOrientCameraProperly;
        playerStartingQuaternion = transform.rotation;
        Debug.Log("VR Camera orientation after: (" + transform.rotation.eulerAngles.x + ", " + transform.rotation.eulerAngles.y + ", " + transform.rotation.eulerAngles.z + ")");

        // A final step (implement later) is to 
        //Quaternion finalOrientation = transform.rotation * rotationFromOrientingTrackerToHeadset;
        //transform.rotation = finalOrientation;
    }


    private void TranslatePlayerToAchieveCameraPosition(bool recordTranslationAsToggleHomeDistance)
    {
        // Get the relative position of the HMD camera to the player
        Vector3 relativePositionHmdCameraToPlayer = playerScript.hmdTransform.position - transform.position;
        //Debug.Log("Relative Position of HMD to player: (" + relativePositionHmdCameraToPlayer.x + ", " + relativePositionHmdCameraToPlayer.y + ", " + relativePositionHmdCameraToPlayer.z + ")");

        // Store the current position minus the home position. This offset vector is 
        // approximately the distance from the ground plane to the player's head (minus the starting offset of ground to headset frame)
        // assuming the game started with the headset on the ground between the player's feet. 
        if(recordTranslationAsToggleHomeDistance == true)
        {
            toggleHomeDistanceMoved = transform.position - (playerViewStartPosition - relativePositionHmdCameraToPlayer);
        }

        // Set the player position such that the camera is in the desired location
        //Debug.Log("Player position before: (" + transform.position.x + ", " + transform.position.y + ", " + transform.position.z + ")");
        transform.position = playerViewStartPosition - relativePositionHmdCameraToPlayer; // - new Vector3(0.0f, 0.0f, startingDistanceFromBag);
        //Debug.Log("Player position after: (" + transform.position.x + ", " + transform.position.y + ", " + transform.position.z + ")");


        // Put the VR camera in the same position as the player 
        //Debug.Log("Relative Position of HMD to player before: (" + playerScript.hmdTransform.position.x + ", " + playerScript.hmdTransform.position.y + ", " + playerScript.hmdTransform.position.z + ")");
        //Debug.Log("VR Camera orientation after: (" + transform.rotation.eulerAngles.x + ", " + transform.rotation.eulerAngles.y + ", " + transform.rotation.eulerAngles.z + ")");
    }


    private void TranslatePlayerToHomeOnToggle()
    {
/*        // Get the relative position of the HMD camera to the player
        Vector3 relativePositionHmdCameraToPlayer = playerScript.hmdTransform.position - transform.position;
        Debug.Log("Relative Position of HMD to player: (" + relativePositionHmdCameraToPlayer.x + ", " + relativePositionHmdCameraToPlayer.y + ", " + relativePositionHmdCameraToPlayer.z + ")");

        // Store the current position minus the home position. This offset vector is 
        // approximately the distance from the ground plane to the player's head (minus the starting offset of ground to headset frame)
        // assuming the game started with the headset on the ground between the player's feet. 
        toggleHomeDistanceMoved = transform.position - (playerViewStartPosition - relativePositionHmdCameraToPlayer);

        // Set the player position
        Debug.Log("Player position before: (" + transform.position.x + ", " + transform.position.y + ", " + transform.position.z + ")");
        transform.position = playerViewStartPosition - relativePositionHmdCameraToPlayer; // - new Vector3(0.0f, 0.0f, startingDistanceFromBag);
        Debug.Log("Player position after: (" + transform.position.x + ", " + transform.position.y + ", " + transform.position.z + ")");*/
    }


    private void TranslateAndRotatePlayerToHomeOnToggle()
    {
        Debug.Log("Request to toggle VR camera home received.");
        // Rotate player to achieve a desired VR camera gaze orientation
        RotatePlayerToAchieveCameraGazeOrientation();

        // Translate player to achieve a desired VR camera gaze position
        bool recordTranslationAsToggleHome = true;
        TranslatePlayerToAchieveCameraPosition(recordTranslationAsToggleHome);
    }
}
