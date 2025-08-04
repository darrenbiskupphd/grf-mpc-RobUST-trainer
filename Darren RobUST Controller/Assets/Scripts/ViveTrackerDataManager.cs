// Vive tracker left-handed frame, looking at front face: x is right, y is down, z is forwards or towards viewer. 
// Vive tracker right-handed frame, looking at front face: x is left, y is forwards or towards viewer, z is up.


using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ViveTrackerDataManager : MonoBehaviour
{

    // Selections by experimenter!
    public FrameZeroComputationSettingsSelect frameZeroComputationSettingsSelect;
    public FrameZeroOrientationSettingsSelect frameZeroOrientationSettingsSelect;
    public ApplyCorrectionToAlignWithForcePlateSelect applyCorrectionToAlignWithForcePlateSelect;

    // Variables to "lock"/fix frame 0
    private bool lockFrame0TransformationCommandReceivedFlag = false; // Whether or not we've received the "lock now" command from another script.
    private bool transformationFrameZeroToUnityFrameIsLockedFlag = false; // start with the transformation not locked/fixed
    private Matrix4x4 transformationMatrixFrameZeroToUnityFrame; // Stored the transformation after we "lock"/fix it 

    // Reference to the Vive tracker pairing manager
    public ManageViveTrackedObjects viveTrackerPairingManagerScript;
    
    // Reference to the "reference" Vive tracker that is static and mounted to the robot frame
    public GameObject viveFixedReferenceTracker;
    List<Vector3> defaultPositionForBodyViveTracker = new List<Vector3>();
    private uint validViveDataCounter = 0; // keeps track of how many times the vive data has been updated, at least until we reach the minimum number of updates
    private uint numberOfValidViveDataUpdatesBeforeUse = 100; 
    
    
    // References to the moving game objects with positions set by the Vive trackers.
    public GameObject pelvicViveTracker;
    public GameObject chestViveTracker;
    public GameObject leftAnkleViveTracker;
    public GameObject rightAnkleViveTracker;
    public GameObject leftShankViveTracker;
    public GameObject rightShankViveTracker;
    public GameObject rightHandViveTracker; // FINISH****
    public GameObject BuildStructureMatrixServiceObject; // the object that contains the script that computes structure matrices for the robot
    private BuildStructureMatricesForBeltsThisFrameScript buildStructureMatrixScript; // the script that computes structure matrices

    // Create a "virtual" pelvic tracker position that is computed as a fixed distance from the chest tracker, 
    // along a vector pointing towards the pelvic tracker. 
    // We display this position to the user in the squatting task to adjust for pelvic belt upward movement. 
    public GameObject pelvicViveTrackerVirtual;


    // for the robot each frame. The reference is obtained
    // when the public function is called that states we're 
    // using a cable-driven robot.
    public GameObject forceFieldHighLevelControllerObject; // the object that contains the script that computes desired force field forces (the high-level controller)
    public ForceFieldHighLevelControllerScript forceFieldHighLevelControllerScript; 
    // Reference to the Vicon marker data manager ("COM manager")
    public ManageCenterOfMassScript centerOfMassManagerScript;
    // public SubjectInfoStorageScript SubjectInfoStorageScriptlocal;

    // Subject-specific info (local storage)
    public SubjectInfoStorageScript subjectSpecificDataScript;
    private string subjectSexString;
    private float pelvicMediolateralAxisRadiusInMeters;
    private float pelvicAnteroposteriorAxisRadiusInMeters;
    private float chestAnteroposteriorAxisRadiusInMeters;
    private float chestMediolateralAxisRadiusInMeters;
    private bool usingCableDrivenRobotFlag = false;

    //list of segment types 
    private const string trunkSegmentType = "Trunk";
    private const string thighSegmentType = "Thigh";
    private const string shankSegmentType = "Shank";
    private const string armSegmentType = "Arm";
    private const string forearmSegmentType = "Forearm";
    private const string handSegmentType = "Hand";
    private const string headSegmentType = "Head";
    private const string footSegmentType = "Foot";
    
    // Visualizing kinematic skeleton
    public bool visualizeKinematicSkeletonFlag;
    public GameObject midKneeCenterVisualizerObject; 
    public GameObject pelvicCenterVisualizerObject; 
    public GameObject chestCenterVisualizerObject;
    // Visualizing the cable attachments = part of kinematic skeleton
    public GameObject cableAttachmentMarkerObject;
    private List<GameObject> shankCableAttachmentVisualizerList = new List<GameObject>(); // we store right and left shank together
    private List<GameObject> pelvisCableAttachmentVisualizerList = new List<GameObject>();
    private List<GameObject> chestCableAttachmentVisualizerList = new List<GameObject>();
    // Visualizing the cable pulleys
    public GameObject pulleyMarkerObject;
    private List<GameObject> shankPulleyVisualizerList = new List<GameObject>(); // we store right and left shank together
    private List<GameObject> pelvisPulleyVisualizerList = new List<GameObject>();
    private List<GameObject> chestPulleyVisualizerList = new List<GameObject>();

    // Using vive trackers to get some length parameters
    public bool computedStartupSubjectLengthParametersFlag = false; // whether we've computed subject-specific params at startup (true) or not (false)
    private float lengthAnkleToChestInMeters; // the length from the ankle tracker midpoint to the chest tracker at startup

    // Flag for indicating that Vive trackers are initialized and can be read
    private bool viveTrackerDataHasBeenInitialized = false;

    // Get a reference to the level manager script
    public LevelManagerScriptAbstractClass levelManagerScript;

    // Which belts are actually being used. Retrieved from the force field high level controller.
    private bool usingChestBelt;
    private bool usingPelvicBelt;
    private bool usingShankBelts;

    private bool pulleysAlreadyVisualizedFlag = false ;


    // Start is called before the first frame update
    void Start()
    {

        /*if (SubjectInfoStorageScriptlocal == null)
        {
            // Try to find the script on an object in the scene
            SubjectInfoStorageScriptlocal = FindObjectOfType<SubjectInfoStorageScript>();

            // If still null, log an error
            if (SubjectInfoStorageScriptlocal == null)
            {
                Debug.LogError("SubjectInfoStorageScriptlocal is not assigned and could not be found in the scene.");
                
            }
        }
*/
        // 
        ManageViveTrackedObjects.TrackedDeviceProperties trackedDeviceProperties= new ManageViveTrackedObjects.TrackedDeviceProperties();
        trackedDeviceProperties.SetDeviceName(pelvicViveTracker.name);

        trackedDeviceProperties.SetLatestDevicePosition(pelvicViveTracker.transform.position);
        subjectSexString = subjectSpecificDataScript.getSubjectGenderString();
        
        // From the subject information, get the pelvic belt ellipse ML width and AP width. 
        // Divide by two to get the radii describing the pelvic belt ellipse.
        (float pelvisMediolateralLengthInMeters, float pelvisAnteroposteriorLengthInMeters) =
            subjectSpecificDataScript.GetPelvisEllipseMediolateralAndAnteroposteriorLengths();
        pelvicMediolateralAxisRadiusInMeters = pelvisMediolateralLengthInMeters / 2.0f;
        pelvicAnteroposteriorAxisRadiusInMeters = pelvisAnteroposteriorLengthInMeters / 2.0f;
        Debug.Log("pelvicMediolateralAxisRadiusInMeters" + pelvicMediolateralAxisRadiusInMeters);
        Debug.Log("pelvicAnteroposteriorAxisRadiusInMeters" + pelvicAnteroposteriorAxisRadiusInMeters);

        // Assign the chest belt ellipse lengths
        (float chestMediolateralLengthInMeters, float chestAnteroposteriorLengthInMeters) =
        subjectSpecificDataScript.GetChestEllipseMediolateralAndAnteroposteriorLengths();
        chestMediolateralAxisRadiusInMeters = chestMediolateralLengthInMeters / 2.0f;
        chestAnteroposteriorAxisRadiusInMeters = chestAnteroposteriorLengthInMeters / 2.0f;

        // Call the function that determines which belts are being used and does visualizer setup
        SetUpVisualizersForAllBelts();

    }

    // Update is called once per frame
void Update()
{
    
   // ManageViveTrackedObjects.TrackedDeviceProperties trackedDeviceProperties= new ManageViveTrackedObjects.TrackedDeviceProperties();
    //trackedDeviceProperties.SetDeviceName(pelvicViveTracker.name);

    //trackedDeviceProperties.SetLatestDevicePosition(pelvicViveTracker.transform.position);
    subjectSexString = subjectSpecificDataScript.getSubjectGenderString();
    //viveTrackerPairingManagerScript.GetCurrentDeviceStatesAllDevices();
   
    
    ForceFieldHighLevelControllerScript forceControlScript = forceFieldHighLevelControllerScript;
    (List<Vector3> gameObjectPositionListThisFrame, List<Vector3>gameObjectPositionListWithoutViveFixedReferenceTracker) = GameObjectPositionList();
    // Maybe we don't need to check it's available in every single frame, we can put it in start.
    if (viveTrackerPairingManagerScript.IfAllViveTrackerDataIsAvailableFlag())
    {
        if (IfAllViveTrackerDataIsVaildFlag(gameObjectPositionListThisFrame)) // valid = all non-zero (includes Vive ref tracker on frame)
        {
            if (IfAllViveTrackerDataIsUpdatedFlag(gameObjectPositionListWithoutViveFixedReferenceTracker)) 
                // updated = list of subject-mounted
                // tracker positions has changed (all changed)
            {
                // Then we have fresh Vive tracker position/orientation data for all trackers.
                // Compute force field forces.
                //forceControlScript.ComputeDesiredForceFieldForcesAndTorquesOnSubject();
                
                // should uncomment after the buildStructureMatrixScript is work
                // buildStructureMatrixScript.BuildStructureMatricesForThisFrame();
                
                // Debug
                /*Debug.Log("Vive tracker data should be valid. First tracker pos: (" + gameObjectPositionListThisFrame[0].x + ", "
                    + gameObjectPositionListThisFrame[0].y + ", " + gameObjectPositionListThisFrame[0].z + ") and second tracker pos: " + gameObjectPositionListThisFrame[1].x + ", "
                        + gameObjectPositionListThisFrame[1].y + ", " + gameObjectPositionListThisFrame[1].z);*/

                // If the flag that indicates whether Vive data has initialized is set to false, 
                // then set it to true, because Vive data has been shown to be valid. 
                if(viveTrackerDataHasBeenInitialized == false)
                {
                    viveTrackerDataHasBeenInitialized = true;
                    Debug.Log("All Vive trackers connected successfully!");
                }

                if (computedStartupSubjectLengthParametersFlag == false)
                {
                    ComputeSubjectSpecificLengthParams();
                }

                // Compute the "virtual", adjusted pelvic tracker position that we'll use for the squatting task
                // visualization on-screen.
                Vector3 chestTrackerToPelvicTrackerUnitVector = pelvicViveTracker.transform.position - chestViveTracker.transform.position;
                chestTrackerToPelvicTrackerUnitVector = chestTrackerToPelvicTrackerUnitVector / chestTrackerToPelvicTrackerUnitVector.magnitude;
                pelvicViveTrackerVirtual.transform.position = chestViveTracker.transform.position +
                    chestTrackerToPelvicTrackerUnitVector * subjectSpecificDataScript.GetDistanceFromPelvicBeltTrackerToChestBeltTracker();

                // Compute the forces needed to act on the subject, solve for appropriate calbe tensions, and send the 
                // cable tensions to the robot.
                    buildStructureMatrixScript.BuildStructureMatricesForThisFrame();
                forceFieldHighLevelControllerScript.ComputeDesiredForceFieldForcesAndTorquesOnSubject();

                // Tell the high-level controller script to compute tensions and send them on to the cable robot
                // via the TCP service
                forceFieldHighLevelControllerScript.ComputeDesiredCableTensionsThisFrameAndSendToRobot();
                
                // Since the Vive tracker data is "fresh", tell the level manager to store a row of frame data, 
                // which stores all of the Vive tracker pos/orientation data.
                levelManagerScript.StoreRowOfFrameData();


            }
            else
            {
                //Debug.Log("The data are not correctly up dated.");
            }
        }
        else
        {
            //Debug.Log("The data that are read are not correct. They are not non-zero.");
        }
    }
    else
    {
        //Debug.Log("Please turn on you tracker.");
    }
    // if all vive tracker data is available (how to see? Maybe all 6 trackers found their serial id on a device)
        // If th data seems valid (non-zeros?)
            // If all tracker positions have been updated by Steam (not the same vs previous)
                // Then new data for tracker pos is available. Call HL FF function.
}


    public bool GetViveTrackerDataHasBeenInitializedFlag()
    {
        return viveTrackerDataHasBeenInitialized;
    }

    private void SetUpVisualizersForAllBelts()
    {
        // Get which belts are actually being used
        ForceFieldHighLevelControllerScript.ChestForceControlPropertiesClass chestForceFieldSettings =
            forceFieldHighLevelControllerScript.GetChestBeltSettingsSelector();
        ForceFieldHighLevelControllerScript.PelvisForceControlPropertiesClass pelvisForceFieldSettings =
            forceFieldHighLevelControllerScript.GetPelvicBeltSettingsSelector();
        ForceFieldHighLevelControllerScript.ShankForceControlPropertiesClass shankForceFieldSettings =
            forceFieldHighLevelControllerScript.GetShankBeltSettingsSelector();

        if(chestForceFieldSettings.chestForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            usingChestBelt = true;
        }

        if (pelvisForceFieldSettings.pelvisForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            usingPelvicBelt = true;
        }

        if (shankForceFieldSettings.shankForceFieldType != ForceFieldTypeEnum.Disabled)
        {
            usingShankBelts = true;
        }

        // Create cable attachment visualization game objects - one small red sphere per attachment point
        // One cable attachment per shank
        if (usingShankBelts == true)
        {
            int numShankCableAttachments = 2;
            for (int i = 0; i < numShankCableAttachments; i++)
            {
                // Attachments
                GameObject marker = Instantiate(cableAttachmentMarkerObject);
                marker.name = "ShankAttachmentMarker_" + i;

                // Optional: Set active if the original is inactive
                marker.SetActive(true);

                // Optional: Set position/parent if needed
                // marker.transform.position = somePosition;
                // marker.transform.SetParent(transform);

                shankCableAttachmentVisualizerList.Add(marker);

                Debug.Log("Created shank attachment marker: " + marker.name);

                // Pulleys
                GameObject pulleyMarker = Instantiate(pulleyMarkerObject);
                pulleyMarker.name = "ShankPulleyMarker_" + i;

                // Optional: Set active if the original is inactive
                pulleyMarker.SetActive(true);

                // Optional: Set position/parent if needed
                // marker.transform.position = somePosition;
                // marker.transform.SetParent(transform);

                shankPulleyVisualizerList.Add(pulleyMarker);

                Debug.Log("Created shank pulley marker: " + pulleyMarker.name);

            }
        }

        // Four cable attachments for pelvis and chest (if using)
        for (uint cableAttachmentIndex = 0; cableAttachmentIndex < 4; cableAttachmentIndex++)
        {
            if (usingPelvicBelt == true)
            {
                GameObject marker = Instantiate(cableAttachmentMarkerObject);
                marker.name = "PelvisAttachmentMarker_" + cableAttachmentIndex;

                // Optional: Set active if the original is inactive
                marker.SetActive(true);

                // Optional: Set position/parent if needed
                // marker.transform.position = somePosition;
                // marker.transform.SetParent(transform);

                pelvisCableAttachmentVisualizerList.Add(marker);

                Debug.Log("Created pelvis attachment marker: " + marker.name);
            }

            if (usingChestBelt == true)
            {
                GameObject marker = Instantiate(cableAttachmentMarkerObject);
                marker.name = "ChestAttachmentMarker_" + cableAttachmentIndex;

                // Optional: Set active if the original is inactive
                marker.SetActive(true);

                // Optional: Set position/parent if needed
                // marker.transform.position = somePosition;
                // marker.transform.SetParent(transform);

                chestCableAttachmentVisualizerList.Add(marker);

                Debug.Log("Created chest attachment marker: " + marker.name);
            }
        }

        // Four or eight pulley visualizers for pelvis (if using the pelvic belt)
        if (usingPelvicBelt == true)
        {
            uint numberOfPelvicPulleys = 0;
            if (forceFieldHighLevelControllerScript.GetPelvicBeltNumberOfCablesSelector() == PelvicBeltCableNumberSelector.Four)
            {
                numberOfPelvicPulleys = 4;
            }
            else if (forceFieldHighLevelControllerScript.GetPelvicBeltNumberOfCablesSelector() == PelvicBeltCableNumberSelector.Eight)
            {
                numberOfPelvicPulleys = 8;
            }

            // Instantiate pelvis pulley visualizer game objects
            for (uint cableAttachmentIndex = 0; cableAttachmentIndex < numberOfPelvicPulleys; cableAttachmentIndex++)
            {
                // Pulley visualizers
                GameObject pulleyMarker = Instantiate(pulleyMarkerObject);
                pulleyMarker.name = "PelvisPulleyMarker_" + cableAttachmentIndex;

                // Optional: Set active if the original is inactive
                pulleyMarker.SetActive(true);

                // Optional: Set position/parent if needed
                // marker.transform.position = somePosition;
                // marker.transform.SetParent(transform);

                pelvisPulleyVisualizerList.Add(pulleyMarker);

                Debug.Log("Created pelvis pulley marker: " + pulleyMarker.name);
            }
        }

        // The chest belt always has four pulley visualizers (if using the chest belt)
        if (usingChestBelt == true)
        {
            // Four pulleys for chest
            for (uint cableAttachmentIndex = 0; cableAttachmentIndex < 4; cableAttachmentIndex++)
            {
                // Pulley visualizers
                GameObject pulleyMarker = Instantiate(pulleyMarkerObject);
                pulleyMarker.name = "ChestPulleyMarker_" + cableAttachmentIndex;

                // Optional: Set active if the original is inactive
                pulleyMarker.SetActive(true);

                // Optional: Set position/parent if needed
                // marker.transform.position = somePosition;
                // marker.transform.SetParent(transform);

                chestPulleyVisualizerList.Add(pulleyMarker);

                Debug.Log("Created chest pulley marker: " + pulleyMarker.name);
            }
        }
    }
private bool IfAllViveTrackerDataIsVaildFlag(List<Vector3> position)
{
    Vector3 zeroVector = new Vector3(0.0f, 0.0f, 0.0f);
    foreach (Vector3 p in position)
    {
        if (p == zeroVector || p == null || float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
        {
            //Debug.Log("One of the data is invaild. The position is 0.");
            return false;
        }
    }
    //Debug.Log("The position data is vaild.");
    return true;
}

private bool IfAllViveTrackerDataIsUpdatedFlag(List<Vector3> positionWithoutViveFixedReferenceTracker)
{
    // If we don't have a "default" list of vive tracker positions
    if (defaultPositionForBodyViveTracker == null)
    {
        // Store all Vive tracker positions as default positions
        defaultPositionForBodyViveTracker = positionWithoutViveFixedReferenceTracker;
        return false;
    }
    else
    {
        // Now, we make the "default" position list be a previous position list. 
        // If the positions have changed, then we have updated the positions.
        if (positionWithoutViveFixedReferenceTracker.SequenceEqual(defaultPositionForBodyViveTracker))
        {
            defaultPositionForBodyViveTracker = positionWithoutViveFixedReferenceTracker;
            return false;
        }
        else // this is the valid case, where vive tracker data has been updated
        {
            defaultPositionForBodyViveTracker = positionWithoutViveFixedReferenceTracker;
            
            if (validViveDataCounter < numberOfValidViveDataUpdatesBeforeUse)
            {
                validViveDataCounter++;
            }
            
            if (validViveDataCounter >= numberOfValidViveDataUpdatesBeforeUse)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}


private void ComputeSubjectSpecificLengthParams()
{
    // Get the length from the ankle to the chest based on Vive tracker data
    Vector3 ankleMidpointUnityFrame =
        (leftAnkleViveTracker.transform.position + rightAnkleViveTracker.transform.position) / 2.0f;
    lengthAnkleToChestInMeters = (chestViveTracker.transform.position - ankleMidpointUnityFrame).magnitude;
    
}

    private (List<Vector3>, List<Vector3>) GameObjectPositionList()
    {   
        List<Vector3> gameObjectPositionList = new List<Vector3>();
        gameObjectPositionList.Add(viveFixedReferenceTracker.transform.position);
        gameObjectPositionList.Add(pelvicViveTracker.transform.position);
        gameObjectPositionList.Add(chestViveTracker.transform.position);
        gameObjectPositionList.Add(leftAnkleViveTracker.transform.position);
        gameObjectPositionList.Add(rightAnkleViveTracker.transform.position);
        gameObjectPositionList.Add(leftShankViveTracker.transform.position);
        gameObjectPositionList.Add(rightShankViveTracker.transform.position);
        gameObjectPositionList.Add(rightHandViveTracker.transform.position);
   
        List<Vector3> gameObjectPositionListWithoutviveFixedReferenceTracker = new List<Vector3>();
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(pelvicViveTracker.transform.position);
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(chestViveTracker.transform.position);
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(leftAnkleViveTracker.transform.position);
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(rightAnkleViveTracker.transform.position);
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(leftShankViveTracker.transform.position);
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(rightShankViveTracker.transform.position);
        gameObjectPositionListWithoutviveFixedReferenceTracker.Add(rightHandViveTracker.transform.position);
    
        return (gameObjectPositionList, gameObjectPositionListWithoutviveFixedReferenceTracker);
    }


    // START: public functions ****************************************************************************************************

    public float GetLengthAnkleToChestInMeters()
    {
        return lengthAnkleToChestInMeters;
    }
    public void SetUsingCableDrivenRobotFlagToTrue()
    {
        // Get the reference to the structure matrix script, since we are using a cable-driven robot
        buildStructureMatrixScript = BuildStructureMatrixServiceObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

        // Also get the reference to the high-level controller script for computing force field forces/torques
        forceFieldHighLevelControllerScript = forceFieldHighLevelControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();

        // Then set the flag indicating we're using a cable-driven robot
        usingCableDrivenRobotFlag = true;
    }

    public Vector3 TransformPositionToViveRefTrackerFrame(Vector3 currentPositionTrackerFrame, GameObject viveTracker)
    {
        // Transform the pelvic center position from pelvic tracker frame to Unity world frame
        Vector3 currentPositionInUnityFrame = viveTracker.transform.TransformPoint(currentPositionTrackerFrame);

        // Transform the pelvic center to the left-handed fixed Vive reference tracker frame
        Vector3 currentPositionInLeftHandedFixedViveFrame = viveFixedReferenceTracker.transform.InverseTransformPoint(currentPositionInUnityFrame);

        // Get the transformation matrix from the left-handed (Unity) to the right-handed (hardware)
        // fixed Vive reference tracker frame 
        Matrix4x4 transformUnityLeftHandedViveRefFrameToRightHandedViveRefFrame =
            GetTransformationMatrixUnityLeftHandedViveRefToRightHandedViveRefFrame();

        // Transfrom the pelvic center from left-handed Unity to right-handed hardware Vive ref tracker frame.
        // Note that moving from Unity fixed Vive tracker frame to 
        // hardware fixed Vive tracker frame is just a rotation.
        Vector3 currentPositionInRightHandedFixedViveTrackerFrame =
            transformUnityLeftHandedViveRefFrameToRightHandedViveRefFrame.MultiplyPoint3x4(currentPositionInLeftHandedFixedViveFrame);

        return currentPositionInRightHandedFixedViveTrackerFrame;
    }


    public Matrix4x4 GetTransformationMatrixPelvicViveTrackerToRefTrackerLeftHandedFrame()
    {
        // Get the transformation from pelvic tracker to Vive ref tracker left-handed frame
        return GetTransformationFromViveTrackerToViveRefTrackerLeftHandedFrame(pelvicViveTracker);
    }

    public Matrix4x4 GetTransformationMatrixChestBeltCenterToRefTrackerLeftHandedFrame()
    {
        // Get the transformation from pelvic tracker to Vive ref tracker left-handed frame
        return GetTransformationFromViveTrackerToViveRefTrackerLeftHandedFrame(chestViveTracker);
    }

    public Matrix4x4 GetTransformationMatrixLeftShankViveTrackerToRefTrackerLeftHandedFrame()
    {

        return GetTransformationFromViveTrackerToViveRefTrackerLeftHandedFrame(leftShankViveTracker);
    }

    public Matrix4x4 GetTransformationMatrixRightShankViveTrackerToRefTrackerLeftHandedFrame()
    {
        return GetTransformationFromViveTrackerToViveRefTrackerLeftHandedFrame(rightAnkleViveTracker);
    }
    // Build Transformation(from any Vive tracker frame TO Vive reference tracker left-handed frame)
    private Matrix4x4 GetTransformationFromViveTrackerToViveRefTrackerLeftHandedFrame(GameObject viveTrackerOfInterest)
    {
        // Get the unit vectors of the Vive tracker of interest expressed in Vive ref tracker frame (the rotation matrix)
        Vector3 trackerOfInterestXAxisUnityFrame = viveTrackerOfInterest.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));
        Vector3 trackerOfInterestYAxisUnityFrame = viveTrackerOfInterest.transform.TransformDirection(new Vector3(0.0f, 1.0f, 0.0f));
        Vector3 trackerOfInterestZAxisUnityFrame = viveTrackerOfInterest.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f));
        
        // Get the transformation from Unity frame to Vive ref tracker left-handed frame
        // InverseTransformDirection goes from Unity frame to local frame.
        Vector3 trackerOfInterestXAxisViveRefFrame = viveFixedReferenceTracker.transform.InverseTransformDirection(trackerOfInterestXAxisUnityFrame);
        Vector3 trackerOfInterestYAxisViveRefFrame = viveFixedReferenceTracker.transform.InverseTransformDirection(trackerOfInterestYAxisUnityFrame);
        Vector3 trackerOfInterestZAxisViveRefFrame = viveFixedReferenceTracker.transform.InverseTransformDirection(trackerOfInterestZAxisUnityFrame);
        
        // Get the position of the Vive tracker of interest in Vive ref tracker frame (the translation column)
        Vector3 trackerOfInterestPositionInRefTrackerFrame = viveFixedReferenceTracker.transform.InverseTransformPoint(viveTrackerOfInterest.transform.position);
        
        // Build the transformation matrix from Vive tracker of interest frame to Vive ref tracker frame
        Matrix4x4 transformationFromViveTrackerToViveRefTrackerLeftHandedFrame = new Matrix4x4();
        transformationFromViveTrackerToViveRefTrackerLeftHandedFrame.SetColumn(0,new Vector4(trackerOfInterestXAxisUnityFrame.x, trackerOfInterestXAxisUnityFrame.y, trackerOfInterestXAxisUnityFrame.z, 0.0f));
        transformationFromViveTrackerToViveRefTrackerLeftHandedFrame.SetColumn(1, new Vector4(trackerOfInterestYAxisUnityFrame.x, trackerOfInterestYAxisUnityFrame.y, trackerOfInterestYAxisUnityFrame.z, 0.0f));
        transformationFromViveTrackerToViveRefTrackerLeftHandedFrame.SetColumn(2, new Vector4(trackerOfInterestZAxisUnityFrame.x, trackerOfInterestZAxisUnityFrame.y, trackerOfInterestZAxisUnityFrame.z, 0.0f));
        transformationFromViveTrackerToViveRefTrackerLeftHandedFrame.SetColumn(3, new Vector4(trackerOfInterestPositionInRefTrackerFrame.x, trackerOfInterestPositionInRefTrackerFrame.y, trackerOfInterestPositionInRefTrackerFrame.z, 1.0f));
        
        // Return 
        return transformationFromViveTrackerToViveRefTrackerLeftHandedFrame;
    }

    public Matrix4x4 GetTransformationFromViveReferenceTrackerLeftHandedToUnityFrame()
    {
        /*// Get transformation from Unity frame to Vive reference tracker RIGHT-HANDED frame
        Matrix4x4 transformationMatrixUnityToRightHandedViveRefTrackerFrame = GetTransformationMatrixUnityLeftHandedViveRefToRightHandedViveRefFrame();
        
        // Get transformation from Vive reference tracker RIGHT-HANDED frame to Vive ref tracker LEFT-handed frame
        Matrix4x4 transformationMatrixUnityLeftHandedViveRefToRightHandedViveRefFrame =
            GetTransformationMatrixUnityLeftHandedViveRefToRightHandedViveRefFrame();
        Matrix4x4 transformationMatrixViveRightHandedToViveLeftHandedFrame = new Matrix4x4();
        Matrix4x4.Inverse3DAffine(transformationMatrixUnityLeftHandedViveRefToRightHandedViveRefFrame,
            ref transformationMatrixViveRightHandedToViveLeftHandedFrame);
        
        // Multiply transformations to go from Unity to left-handed vive ref frame
        Matrix4x4 transformationMatrixUnityToLeftHandedViveRefTrackerFrame =
                transformationMatrixViveRightHandedToViveLeftHandedFrame * transformationMatrixUnityToRightHandedViveRefTrackerFrame;*/
        
        // Get the unit axes of the Vive reference tracker (left-handed) frame in Unity frame 
        // to build the rotation matrix from Vive reference tracker (left-handed) to Unity frame.
        Vector3 xAxisViveRefInUnityFrame = viveFixedReferenceTracker.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));
        Vector3 yAxisViveRefInUnityFrame = viveFixedReferenceTracker.transform.TransformDirection(new Vector3(0.0f, 1.0f, 0.0f));
        Vector3 zAxisViveRefInUnityFrame = viveFixedReferenceTracker.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f));
        
        // The translation is the position of the tracker in Unity frame
        Vector3 viveRefTrackerPosUnityFrame = viveFixedReferenceTracker.transform.position;
        
        // Compose the full transformation matrix from Vive reference tracker (left-handed) to Unity frame.
        Matrix4x4 transformationFromViveReferenceTrackerLeftHandedToUnityFrame = new Matrix4x4();
        transformationFromViveReferenceTrackerLeftHandedToUnityFrame.SetColumn(0, new Vector4(xAxisViveRefInUnityFrame.x, xAxisViveRefInUnityFrame.y, xAxisViveRefInUnityFrame.z, 0.0f));
        transformationFromViveReferenceTrackerLeftHandedToUnityFrame.SetColumn(1, new Vector4(yAxisViveRefInUnityFrame.x, yAxisViveRefInUnityFrame.y, yAxisViveRefInUnityFrame.z, 0.0f));
        transformationFromViveReferenceTrackerLeftHandedToUnityFrame.SetColumn(2, new Vector4(zAxisViveRefInUnityFrame.x, zAxisViveRefInUnityFrame.y, zAxisViveRefInUnityFrame.z, 0.0f));
        transformationFromViveReferenceTrackerLeftHandedToUnityFrame.SetColumn(3, new Vector4(viveRefTrackerPosUnityFrame.x, viveRefTrackerPosUnityFrame.y, viveRefTrackerPosUnityFrame.z, 1.0f));
        
        // Return
        return transformationFromViveReferenceTrackerLeftHandedToUnityFrame;
    }


    public void FixFrame0OriginAndOrientation()
    {
        // Lock/fix frame 0. This only has an effect if the settings allow for locking frame 0.
        lockFrame0TransformationCommandReceivedFlag = true;
    }

    public Matrix4x4 GetTransformationMatrixFromFrame0ToUnityFrame()
    {
        if(frameZeroComputationSettingsSelect == FrameZeroComputationSettingsSelect.ComputeEachFrame)
        {
            // Compute the transformation from frame 0 to Unity frame at the current time
            transformationMatrixFrameZeroToUnityFrame = ComputeTransformationMatrixFromFrame0ToUnityFrame();            // Return the current transformation
            return transformationMatrixFrameZeroToUnityFrame;
        }
        else if(frameZeroComputationSettingsSelect == FrameZeroComputationSettingsSelect.LockOnToggleHome)
        {
            // If we haven't received the lock command
            if(lockFrame0TransformationCommandReceivedFlag == false)
            {
                // Compute the transformation from frame 0 to Unity frame at the current time
                transformationMatrixFrameZeroToUnityFrame = ComputeTransformationMatrixFromFrame0ToUnityFrame();            // Return the current transformation
            }
            // If we have received the lock command
            else
            {
                // If we haven't yet "locked" or fixed the transformation matrix
                if (transformationFrameZeroToUnityFrameIsLockedFlag == false)
                {
                    // Compute the current transformation and store it as the locked value
                    transformationMatrixFrameZeroToUnityFrame = ComputeTransformationMatrixFromFrame0ToUnityFrame();

                    // Note that the transformation is now locked
                    transformationFrameZeroToUnityFrameIsLockedFlag = true;

                    // Print the Matrix4x4() 
                    string matrixString =
                        $"[{transformationMatrixFrameZeroToUnityFrame.m00:F3}, {transformationMatrixFrameZeroToUnityFrame.m01:F3}, " +
                        $"{transformationMatrixFrameZeroToUnityFrame.m02:F3}, {transformationMatrixFrameZeroToUnityFrame.m03:F3}]\n" +
                        $"[{transformationMatrixFrameZeroToUnityFrame.m10:F3}, {transformationMatrixFrameZeroToUnityFrame.m11:F3}, " +
                        $"{transformationMatrixFrameZeroToUnityFrame.m12:F3}, {transformationMatrixFrameZeroToUnityFrame.m13:F3}]\n" +
                        $"[{transformationMatrixFrameZeroToUnityFrame.m20:F3}, {transformationMatrixFrameZeroToUnityFrame.m21:F3}," +
                        $" {transformationMatrixFrameZeroToUnityFrame.m22:F3}, {transformationMatrixFrameZeroToUnityFrame.m23:F3}]\n" +
                        $"[{transformationMatrixFrameZeroToUnityFrame.m30:F3}, {transformationMatrixFrameZeroToUnityFrame.m31:F3}, " +
                        $"{transformationMatrixFrameZeroToUnityFrame.m32:F3}, {transformationMatrixFrameZeroToUnityFrame.m33:F3}]";
                    Debug.Log($"Transformation frame 0 to Unity locked as:\n{matrixString}");
                }
            }
        }
        else
        {
            Debug.LogError("frameZeroComputationSettingsSelect value has not been programmed!");
            transformationMatrixFrameZeroToUnityFrame = Matrix4x4.identity;
        }

        // Return the transformation matrix
        return transformationMatrixFrameZeroToUnityFrame;
    }


    private Matrix4x4 ComputeTransformationMatrixFromFrame0ToUnityFrame()
    {
        // Read the position of both ankle tracker(in the unity frame)
        Vector3 leftAnklePositionInUnityFrame = leftAnkleViveTracker.transform.position;
        Vector3 rightAnklePositionInUnityFrame = rightAnkleViveTracker.transform.position;

        // Then get the middle point of both ankle trackers (in the unity frame). 
        // This is the origin of frame 0.
        Vector3 middlePointOfAnklesInUnityFrame = (leftAnklePositionInUnityFrame + rightAnklePositionInUnityFrame) / 2.0f;

        // Initialize the unit vectors of frame 0 expressed in Unity frame
        Vector3 xDirectionUnitVectorInUnityFrame = new Vector3();
        Vector3 yDirectionUnitVectorInUnityFrame = new Vector3();
        Vector3 zDirectionUnitVectorInUnityFrame = new Vector3();

        if (frameZeroOrientationSettingsSelect == FrameZeroOrientationSettingsSelect.ComputeFromViveTrackers)
        {
            // axis z0 equals to a unit vector that is a normalized vector from right ankle to left ankle
            // axis x0 equals to a unit vector that parallel to x-axis of viveFixedReferenceTracker
            zDirectionUnitVectorInUnityFrame = leftAnklePositionInUnityFrame - rightAnklePositionInUnityFrame;
            zDirectionUnitVectorInUnityFrame = zDirectionUnitVectorInUnityFrame / zDirectionUnitVectorInUnityFrame.magnitude;

            // Define the upwards direction in the fixed Vive tracker left-handed frame
            Vector3 upwardsInFixedViveTracker = new Vector3(0.0f, -1.0f, 0.0f);

            // Transform the "upwards" direction from the fixed vive tracker left-handed local frame to Unity world frame.
            xDirectionUnitVectorInUnityFrame =
                viveFixedReferenceTracker.transform.TransformDirection(upwardsInFixedViveTracker);

            // The negative one that we multiply in the yDirectionUnitVectorInUnityFrame indicate the shaft from right-handed
            // frame to a left-handed frame. We need to notice that we only should do it once since we only shift from 
            // right-handed frame to left-handed frame once. 
            yDirectionUnitVectorInUnityFrame =
                -Vector3.Cross(zDirectionUnitVectorInUnityFrame, xDirectionUnitVectorInUnityFrame);
            yDirectionUnitVectorInUnityFrame = yDirectionUnitVectorInUnityFrame / yDirectionUnitVectorInUnityFrame.magnitude;

            // Since it is possible that the x,y and z are not perpendicular to each other,
            // therefore we use cross product to calculate a new z to replace the initial z axis to make sure
            // the z is orthogonal to x and y
            // The reason we pick z because the measured z axis may not necessary on the ground

            zDirectionUnitVectorInUnityFrame =
                -Vector3.Cross(xDirectionUnitVectorInUnityFrame, yDirectionUnitVectorInUnityFrame);

            zDirectionUnitVectorInUnityFrame = zDirectionUnitVectorInUnityFrame / zDirectionUnitVectorInUnityFrame.magnitude;
            // Print z-axis after re-computation



            //  X cross Y should be negative Z because of left-to-right hand flip embedded in R
            Vector3 xCrossY = Vector3.Cross(xDirectionUnitVectorInUnityFrame, yDirectionUnitVectorInUnityFrame);
            /*        Debug.Log("Frame 0 z-axis in Unity frame is: (" + newZAxisDirectionUnitVectorInUnityFrame.x + ", " + newZAxisDirectionUnitVectorInUnityFrame.y + ", " + newZAxisDirectionUnitVectorInUnityFrame.z + ") " +
                              "and x cross y is: ("  + xCrossY.x + ", " + xCrossY.y + ", " + xCrossY.z + ")");*/
        }
        else if (frameZeroOrientationSettingsSelect == FrameZeroOrientationSettingsSelect.ComputeFromViveTrackers)
        {
            // Use the Vive reference tracker axes. 
            // The x-axis of frame 0 (upwards) is the negative y-axis of the reference tracker. 
            xDirectionUnitVectorInUnityFrame = viveFixedReferenceTracker.transform.TransformDirection(new Vector3(0.0f, -1.0f, 0.0f));

            // The y-axis of frame 0 (forwards) is the x-axis of the reference tracker
            yDirectionUnitVectorInUnityFrame = viveFixedReferenceTracker.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));

            // The z-axis of frame 0 (leftwards) is the negative z-axis of the reference tracker
            zDirectionUnitVectorInUnityFrame = viveFixedReferenceTracker.transform.TransformDirection(new Vector3(0.0f, 0.0f, -1.0f));
        }

        Matrix4x4 transformationMatrixFromFrame0ToUnityFrame = new Matrix4x4();

        transformationMatrixFromFrame0ToUnityFrame.SetColumn(0, new Vector4(
                                                                            xDirectionUnitVectorInUnityFrame.x,
                                                                            xDirectionUnitVectorInUnityFrame.y,
                                                                            xDirectionUnitVectorInUnityFrame.z,
                                                                            0.0f));
        transformationMatrixFromFrame0ToUnityFrame.SetColumn(1, new Vector4(
                                                                            yDirectionUnitVectorInUnityFrame.x,
                                                                            yDirectionUnitVectorInUnityFrame.y,
                                                                            yDirectionUnitVectorInUnityFrame.z,
                                                                            0.0f));
        transformationMatrixFromFrame0ToUnityFrame.SetColumn(2, new Vector4(
                                                                            zDirectionUnitVectorInUnityFrame.x,
                                                                            zDirectionUnitVectorInUnityFrame.y,
                                                                            zDirectionUnitVectorInUnityFrame.z,
                                                                            0.0f));
        transformationMatrixFromFrame0ToUnityFrame.SetColumn(3, new Vector4(
                                                                            middlePointOfAnklesInUnityFrame.x,
                                                                            middlePointOfAnklesInUnityFrame.y,
                                                                            middlePointOfAnklesInUnityFrame.z,
                                                                            1.0f));

        // Print transformation from Unity to frame 0. We should see one negative column.

        return (transformationMatrixFromFrame0ToUnityFrame);
    }

    //Computes a right-handed cross-product so that we can work in the Vicon coordinate system
    public Vector3 getRightHandedCrossProduct(Vector3 leftVector, Vector3 rightVector)
    {
        float newXValue = leftVector.y * rightVector.z - leftVector.z * rightVector.y;
        float newYValue = leftVector.z * rightVector.x - leftVector.x * rightVector.z;
        float newZValue = leftVector.x * rightVector.y - leftVector.y * rightVector.x;
        
        return new  Vector3(newXValue, newYValue, newZValue);

    }

    public Vector3 GetRightHandedCrossProductAndRightDirection(Vector3 leftVector, Vector3 rightVector)
    {
        Vector3 newVector = Vector3.Cross(rightVector, leftVector);
        return newVector;
    }
  
    
    
    public Matrix4x4 GetTransformationMatrixUnityLeftHandedViveRefToRightHandedViveRefFrame()
    {
        // hardware frame light facing people and down z up y out of paper x left
        // software frame z out y down x right left fram34e
        // Initialize a matrix instance
        Matrix4x4 transformationMatrix = new Matrix4x4();

        // 1.) Initialize the transformation frmo Vicon tracker frame to Unity tracker frame
        // Flip x-axis
        transformationMatrix.SetColumn(0, new Vector4(-1.0f, 0.0f, 0.0f, 0.0f));
        // New y-axis is the old negative z-axis
        transformationMatrix.SetColumn(1, new Vector4(0.0f, 0.0f, -1.0f, 0.0f));
        // New z-axis is the old y-axis
        transformationMatrix.SetColumn(2, new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
        // The translation is zero
        transformationMatrix.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

        // Return the transformation from the Unity-representation of the Vive fixed refernece tracker frame
        // to the "hardware" represenatation of the Vive tracker frame.
        return transformationMatrix;
    }

    public Matrix4x4 TranformationMatrixFromFrame0ToFrame2()
    {
        // Get the transformation matrix from frame 0 to Unity frame, and vice versa
        Matrix4x4 transformationMatrixFromFrame0ToUnity = GetTransformationMatrixFromFrame0ToUnityFrame();
        Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnity.inverse;

        // Compute the knee center position in frame 0
        Vector3 rightKneeCenterPositionInUnityFrame = GetRightKneeCenterPositionInUnityFrame();
        Vector3 leftKneeCenterPositionInUnityFrame = GetLeftKneeCenterPositionInUnityFrame();
        Vector3 middleKneeCenterPositionInUnityFrame = (rightKneeCenterPositionInUnityFrame + leftKneeCenterPositionInUnityFrame) / 2.0f;
        Vector3 middleKneeCenterPositionInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(middleKneeCenterPositionInUnityFrame);

        // Get the ankle position in Unity frame
        (Vector3 middleAnklePositionInUnityFrame, _, _) = GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();
    
        // Get the vector from the ankle to the knee in Unity frame
        Vector3 frame2XAxisInUnityFrame = middleKneeCenterPositionInUnityFrame - middleAnklePositionInUnityFrame; // Note: GetPelvicCenterPositionInUnityFrame()

        // Get the vector from the ankle to the knee center in frame 0, which is equal to the link 2 x-axis expressed in frame 0.
        // We will not use 'MultiplyPoint3x4' but 'MultiplyVector' because we only want to consider the rotation not
        // the translation.
        Vector3 frame2XAxisInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyVector(frame2XAxisInUnityFrame);
        frame2XAxisInFrame0 = frame2XAxisInFrame0 / frame2XAxisInFrame0.magnitude;

        // From the frame 2 x-axis, we can solve for theta1 and theta2 
        float theta2 = Mathf.Asin(frame2XAxisInFrame0.z);
        float theta1 = Mathf.Atan2(frame2XAxisInFrame0.y, frame2XAxisInFrame0.x);

        // Derive the frame 2 Y and Z-axes from the appropriate rotation matrix built from theta1 and theta2. 
        (_, Vector3 frame2YAxisInFrame0, Vector3 frame2ZAxisInFrame0) = GetColumnsOfRotationMatrixFromFrame2ToFrame0FiveRModel(theta1, theta2);


        // This old method, commented out, is WRONG!
        /*        Vector3 frame2ZAxisInUnityFrame = rightKneeCenterPositionInUnityFrame - middleKneeCenterPositionInUnityFrame;
                frame2ZAxisInUnityFrame = frame2ZAxisInUnityFrame / frame2ZAxisInUnityFrame.magnitude;
                Vector3 frame2ZAxisInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyVector(frame2ZAxisInUnityFrame);

                // We might need this negative sign, but we do not understand why. 
                Vector3 frame2YAxisInFrame0 = Vector3.Cross(frame2ZAxisInFrame0, frame2XAxisInFrame0);

                frame2YAxisInFrame0 = frame2YAxisInFrame0 / frame2YAxisInFrame0.magnitude;

                frame2ZAxisInFrame0 = Vector3.Cross(frame2XAxisInFrame0, frame2YAxisInFrame0);
                frame2ZAxisInFrame0 = frame2ZAxisInFrame0 / frame2ZAxisInFrame0.magnitude;*/

        // Assemble the transformation matrix from frame 2 to frame 0
        Matrix4x4 tranformationMatrixFromFrame2ToFrame0 = new Matrix4x4();
        tranformationMatrixFromFrame2ToFrame0.SetColumn(0, new Vector4(
                                                                    frame2XAxisInFrame0.x,
                                                                    frame2XAxisInFrame0.y,
                                                                    frame2XAxisInFrame0.z,
                                                                    0.0f));
        tranformationMatrixFromFrame2ToFrame0.SetColumn(1, new Vector4(
                                                                    frame2YAxisInFrame0.x,
                                                                    frame2YAxisInFrame0.y,
                                                                    frame2YAxisInFrame0.z,
                                                                    0.0f));
        tranformationMatrixFromFrame2ToFrame0.SetColumn(2, new Vector4(
                                                                    frame2ZAxisInFrame0.x,
                                                                    frame2ZAxisInFrame0.y,
                                                                    frame2ZAxisInFrame0.z,
                                                                    0.0f));
        tranformationMatrixFromFrame2ToFrame0.SetColumn(3, new Vector4(
                                                                    middleKneeCenterPositionInFrame0.x,
                                                                    middleKneeCenterPositionInFrame0.y,
                                                                    middleKneeCenterPositionInFrame0.z,
                                                                    1.0f));

        // Take the inverse to get the transformation from frame 0 to frame 2
        Matrix4x4 tranformationMatrixFromFrame0ToFrame2 = tranformationMatrixFromFrame2ToFrame0.inverse;
        
        // Columns of the rotation matrix are the frame 2 unit axes expressed in frame 0.
        // X-axis of frame 2 in frame 0 should more or less "make sense" when printed out live.
        // Points up in netural stance, points a bit forward (along y0) when squatting
        return tranformationMatrixFromFrame0ToFrame2;
    }


    // This is for the 5R model as defined in our paper and in the squatting task.
    public (Vector3, Vector3, Vector3) GetColumnsOfRotationMatrixFromFrame2ToFrame0FiveRModel(float theta1, float theta2)
    {
        // Column 1 = frame 2 x-axis expressed in frame 0
        Vector3 x2InFrame0 = new Vector3(Mathf.Cos(theta1) * Mathf.Cos(theta2),
                                         Mathf.Sin(theta1) * Mathf.Cos(theta2),
                                         Mathf.Sin(theta2));

        // Column 2 = frame 2 y-axis expressed in frame 0
        Vector3 y2InFrame0 = new Vector3(Mathf.Sin(theta1), 
                                         -Mathf.Cos(theta1),
                                         0);

        // Column 3 = frame 2 z-axis expressed in frame 0
        Vector3 z2InFrame0 = new Vector3(Mathf.Cos(theta1) * Mathf.Sin(theta2),
                                         Mathf.Sin(theta1) * Mathf.Sin(theta2),
                                         -Mathf.Cos(theta2));


        // Return 3 vectors in a tuple
        return (x2InFrame0, y2InFrame0, z2InFrame0);
    }

    public Matrix4x4 TranformationMatrixFromFrame0ToFrame3()
    {
        // Get the transformation matrix from frame 0 to Unity frame, and vice versa
        Matrix4x4 transformationMatrixFromFrame0ToUnity = GetTransformationMatrixFromFrame0ToUnityFrame();
        Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnity.inverse;

        // Get the pelvic position in Unity frame and in frame 0
        Vector3 pelvicPositionInUnityFrame = GetPelvicCenterPositionInUnityFrame();
        Vector3 pelvicPositionInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(pelvicPositionInUnityFrame);

        // Get the knee center position in Unity frame
        Vector3 leftKneeCenterPositionInUnityFrame = GetLeftKneeCenterPositionInUnityFrame();
        Vector3 rightKneeCenterPositionInUnityFrame = GetRightKneeCenterPositionInUnityFrame();
        Vector3 middleKneeCenterPositionInUnityFrame = (leftKneeCenterPositionInUnityFrame + rightKneeCenterPositionInUnityFrame) / 2.0f;

        // The x3 axis in Unity frame is the vector from the knee to the pelvis
        Vector3 frame3XAxisInUnityFrame = pelvicPositionInUnityFrame - middleKneeCenterPositionInUnityFrame;

        // Transform x3 from Unity frame to frame 0.
        // We will not use 'MultiplyPoint3x4' but 'MultiplyVector' because we only want to consider the rotation not
        // the translation.
        Vector3 frame3XAxisInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyVector(frame3XAxisInUnityFrame);

        // Compute theta1 and theta2, which we'll need to build the rotation matrix from frame 3 to frame 1***********************
        // Get the ankle position in Unity frame
        (Vector3 middleAnklePositionInUnityFrame, _, _) = GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();
        // Get the vector from the ankle to the knee in Unity frame
        Vector3 frame2XAxisInUnityFrame = middleKneeCenterPositionInUnityFrame - middleAnklePositionInUnityFrame; // Note: GetPelvicCenterPositionInUnityFrame()
        // Get the vector from the ankle to the knee center in frame 0, which is equal to the link 2 x-axis expressed in frame 0.
        // We will not use 'MultiplyPoint3x4' but 'MultiplyVector' because we only want to consider the rotation not
        // the translation.
        Vector3 frame2XAxisInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyVector(frame2XAxisInUnityFrame);
        frame2XAxisInFrame0 = frame2XAxisInFrame0 / frame2XAxisInFrame0.magnitude;
        // From the frame 2 x-axis, we can solve for theta1 and theta2 
        float theta2 = Mathf.Asin(frame2XAxisInFrame0.z);
        float theta1 = Mathf.Atan2(frame2XAxisInFrame0.y, frame2XAxisInFrame0.x);
        // END theta1, theta 2 computation ******************************************************************************************

        // Compute theta3 ***************************************************************************
        // Get rotation from frame 0 to frame 2
        Matrix4x4 transformationFrame0ToFrame2 = TranformationMatrixFromFrame0ToFrame2();
        // Transform the vector from knee to pelvis into frame 2
        Vector3 frame3XAxisInFrame2 = transformationFrame0ToFrame2.MultiplyVector(frame3XAxisInFrame0);
        // Get theta3
        float theta3 = Mathf.Atan2(frame3XAxisInFrame2.y, frame3XAxisInFrame2.x);
        // END theta3 computation ******************************************************************************************

        // Get the transformation matrix from frame 3 to frame 0
        (_, Vector3 frame3YAxisInFrame0, Vector3 frame3ZAxisInFrame0) =
            GetColumnsOfRotationMatrixFromFrame3ToFrame0FiveRModel(theta1, theta2, theta3);

        // The commented out method below is WRONG!
        /*        // Define the leftward direction in the fixed Vive tracker left-handed frame
                Vector3 leftwardsInPelvicViveTracker = new Vector3(-1.0f, 0.0f, 0.0f);

                // Transform the "leftward" direction from the fixed vive tracker left-handed local frame to Unity world frame.
                Vector3 frame3ZAxisInUnityFrame = pelvicViveTracker.transform.TransformDirection(leftwardsInPelvicViveTracker);
                frame3ZAxisInUnityFrame = frame3ZAxisInUnityFrame / frame3ZAxisInUnityFrame.magnitude;
                Vector3 frame3ZAxisInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyVector(frame3ZAxisInUnityFrame);

                Vector3 frame3YAxisInFrame0 = Vector3.Cross(frame3ZAxisInFrame0, frame3XAxisInFrame0);
                frame3YAxisInFrame0 = frame3YAxisInFrame0 / frame3YAxisInFrame0.magnitude;

                frame3ZAxisInFrame0 = Vector3.Cross(frame3XAxisInFrame0, frame3YAxisInFrame0);
                frame3ZAxisInFrame0 = frame3ZAxisInFrame0 / frame3ZAxisInFrame0.magnitude;*/

        Matrix4x4 tranformationMatrixFromFrame3ToFrame0 = new Matrix4x4();
        tranformationMatrixFromFrame3ToFrame0.SetColumn(0, new Vector4(frame3XAxisInFrame0.x,
                                                                    frame3XAxisInFrame0.y,
                                                                    frame3XAxisInFrame0.z,
                                                                    0.0f));
        tranformationMatrixFromFrame3ToFrame0.SetColumn(1, new Vector4(frame3YAxisInFrame0.x,
                                                                    frame3YAxisInFrame0.y,
                                                                    frame3YAxisInFrame0.z,
                                                                    0.0f));
        tranformationMatrixFromFrame3ToFrame0.SetColumn(2, new Vector4(frame3ZAxisInFrame0.x,
                                                                    frame3ZAxisInFrame0.y,
                                                                    frame3ZAxisInFrame0.z,
                                                                    0.0f));
        tranformationMatrixFromFrame3ToFrame0.SetColumn(3, new Vector4(pelvicPositionInFrame0.x,
                                                                    pelvicPositionInFrame0.y,
                                                                    pelvicPositionInFrame0.z,
                                                                    1.0f));
        
        Matrix4x4 tranformationMatrixFromFrame0ToFrame3 = tranformationMatrixFromFrame3ToFrame0.inverse;
        
        return tranformationMatrixFromFrame0ToFrame3;
    }



    // This is for the 5R model as defined in our paper and in the squatting task.
    public (Vector3, Vector3, Vector3) GetColumnsOfRotationMatrixFromFrame3ToFrame0FiveRModel(float theta1, float theta2, float theta3)
    {
        // Column 1 = frame 2 x-axis expressed in frame 0
        Vector3 x2InFrame0 = new Vector3(Mathf.Sin(theta3) * Mathf.Sin(theta1) + Mathf.Cos(theta3) * Mathf.Cos(theta2) * Mathf.Cos(theta1),
                                   Mathf.Cos(theta3) * Mathf.Cos(theta2) * Mathf.Sin(theta1) - Mathf.Cos(theta1) * Mathf.Sin(theta3),
                                   Mathf.Cos(theta3) * Mathf.Sin(theta2));

        // Column 2 = frame 2 y-axis expressed in frame 0
        Vector3 y2InFrame0 = new Vector3(Mathf.Cos(theta2) * Mathf.Cos(theta1) * Mathf.Sin(theta3) - Mathf.Cos(theta3) * Mathf.Sin(theta1),
                                   Mathf.Cos(theta3) * Mathf.Cos(theta1) + Mathf.Cos(theta2) * Mathf.Sin(theta3) * Mathf.Sin(theta1),
                                   Mathf.Sin(theta3) * Mathf.Sin(theta2));

        // Column 3 = frame 2 z-axis expressed in frame 0
        Vector3 z2InFrame0 = new Vector3(-Mathf.Cos(theta1) * Mathf.Sin(theta2),
                                  -Mathf.Sin(theta2) * Mathf.Sin(theta1),
                                   Mathf.Cos(theta2));

        // Return 3 vectors in a tuple
        return (x2InFrame0, y2InFrame0, z2InFrame0);
    }

    public (Vector3, Vector3, float) GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters()
    {
        // Get ankle midpoint
        Vector3 leftAnklePositionInUnityFrame = leftAnkleViveTracker.transform.position;
        Vector3 rightAnklePositionInUnityFrame = rightAnkleViveTracker.transform.position;
        Vector3 middleAnklePositionInUnityFrame = (leftAnklePositionInUnityFrame + rightAnklePositionInUnityFrame) * 0.5f;
        // Get shank belt midpoint
        Vector3 leftShankPositionInUnityFrame = leftShankViveTracker.transform.position;
        Vector3 rightShankPositionInUnityFrame = rightShankViveTracker.transform.position;
        Vector3 middleShankPositionInUnityFrame = (leftShankPositionInUnityFrame + rightShankPositionInUnityFrame) * 0.5f;
        // Compute distance between ankle and shank belt midpoints and return
        Vector3 vectorMiddleAnkleToMiddleShank = middleShankPositionInUnityFrame - middleAnklePositionInUnityFrame;
        float distanceFromAnkleCenterToShankCableAttachmentCenterInMeters = vectorMiddleAnkleToMiddleShank.magnitude;
        return (middleAnklePositionInUnityFrame, middleShankPositionInUnityFrame, distanceFromAnkleCenterToShankCableAttachmentCenterInMeters);
    }


    public (float, float, float) GetSubjectSpecificSegmentMetrics(string segmentName)
    {
        // Init return values
        float segmentFractionOfTotalBodyMass = 0.0f;
        float segmentLength = 0.0f;
        float lengthToJointMassCenter = 0.0f;
        float distanceFromProximalMarkerToComAsFractionOfSegmentLength = 0.0f;
        

        // 1.) Compute segment mass **************************************************************************

        if (subjectSexString == "Man") //if the subject is male
        {
            // Get the fractional distance from the joint to the center of mass of the segment
            Dictionary<string, float> TisserandMaleDistancesFromProximalMarker = centerOfMassManagerScript.GetTisserandMaleDistancesFromProximalMarker();
            Dictionary<string, float> deLevaMaleSegmentMassesAsPercentOfTotalBodyMass = centerOfMassManagerScript.GetDeLevaMaleSegmentMassesAsPercentOfTotalBodyMass();
            distanceFromProximalMarkerToComAsFractionOfSegmentLength = 1.0f - TisserandMaleDistancesFromProximalMarker[segmentName]; // get the COM distance along the segment
            // If the segment is the trunk
            if (segmentName == trunkSegmentType)
            {
                // Incorporate the upper limbs and head into the segment mass
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[trunkSegmentType] + deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[headSegmentType] +
                    2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[armSegmentType] + 2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[forearmSegmentType] +
                    2 * deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[handSegmentType];
            }
            else
            {
                segmentFractionOfTotalBodyMass = deLevaMaleSegmentMassesAsPercentOfTotalBodyMass[segmentName];
            }
        }
        else if (subjectSexString == "Woman")
        {
            Dictionary<string, float> TisserandFemaleDistancesFromProximalMarker = centerOfMassManagerScript.GetTisserandFemaleDistancesFromProximalMarker();
            Dictionary<string, float> deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass = centerOfMassManagerScript.GetDeLevaFemaleSegmentMassesAsPercentOfTotalBodyMass();
            distanceFromProximalMarkerToComAsFractionOfSegmentLength = 1.0f - TisserandFemaleDistancesFromProximalMarker[segmentName]; // get the COM distance along the segment
            if (segmentName == trunkSegmentType)
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[trunkSegmentType] + deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[headSegmentType] +
                    2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[armSegmentType] + 2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[forearmSegmentType] +
                    2 * deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[handSegmentType];
            }
            else
            {
                segmentFractionOfTotalBodyMass = deLevaFemaleSegmentMassesAsPercentOfTotalBodyMass[segmentName];
            }
        }


        // 2.) Compute segment length **************************************************************************

        if (segmentName == thighSegmentType) // mid knee - hip joint center
        {

            
            // get right knee center position
            Vector3 rightKneePosUnityFrame = GetRightKneeCenterPositionInUnityFrame();
            while (float.IsNaN(rightKneePosUnityFrame.x) || float.IsNaN(rightKneePosUnityFrame.y) || float.IsNaN(rightKneePosUnityFrame.z))
            {
                rightKneePosUnityFrame = GetRightKneeCenterPositionInUnityFrame();
                break;
            }

            // get left knee center position
            Vector3 leftKneePosUnityFrame = GetLeftKneeCenterPositionInUnityFrame();

            // Get the mid-knee position
            Vector3 middleOfKneePosUnityFrame = (rightKneePosUnityFrame + leftKneePosUnityFrame) / 2.0f;

            // get pelvis center position
            Vector3 pelvisCenterCoords = GetPelvicCenterPositionInUnityFrame();

            // Segment length is mid-knee to mid-HJC
            Vector3 kneeToPelvisVector = pelvisCenterCoords - middleOfKneePosUnityFrame;
            segmentLength = kneeToPelvisVector.magnitude;

            // Compute the segment lc (length from knee to thigh COM) as fraction of right knee center to right HJC 
            (Vector3 rightHJCPosUnityFrame, _, _) = GetHjcAndHjcMidpointPositionsInUnityFrame();
            Vector3 rightKneeToRightHJC = rightHJCPosUnityFrame - rightKneePosUnityFrame;
            lengthToJointMassCenter = rightKneeToRightHJC.magnitude * distanceFromProximalMarkerToComAsFractionOfSegmentLength;
        }
        else if (segmentName == shankSegmentType) //mid ankle - mid knee
        {
            
            // get right knee center position
            Vector3 rightKneePosUnityFrame = GetRightKneeCenterPositionInUnityFrame();

            // get left knee center position
            Vector3 leftKneePosUnityFrame = GetLeftKneeCenterPositionInUnityFrame();
          
            // Get the mid-knee position
            Vector3 middleOfKneePosUnityFrame = (rightKneePosUnityFrame + leftKneePosUnityFrame) / 2.0f;

            // get outer ankle markers 
            Vector3 leftAnkleTrackerPosUnityFrame =  leftAnkleViveTracker.transform.position;
            Vector3 rightAnkleTrackerPosUnityFrame = rightAnkleViveTracker.transform.position;

            // avg to get ankle midpoint
            Vector3 avgPosAnkleMarker = (rightAnkleTrackerPosUnityFrame + leftAnkleTrackerPosUnityFrame) / 2.0f;

            // Compute the vector from ankle to knee and get its length
            Vector3 shankSegmentVector = middleOfKneePosUnityFrame - avgPosAnkleMarker;
            segmentLength = shankSegmentVector.magnitude;
            lengthToJointMassCenter = segmentLength * distanceFromProximalMarkerToComAsFractionOfSegmentLength;

        }
        else if (segmentName == trunkSegmentType) //supersternal - hip joint center
        {

            // Get the pelvic center in Unity frame
            Vector3 pelvisCenterInUnityFrame = GetPelvicCenterPositionInUnityFrame();

            // Get the chest center in Unity frame
            Vector3 chestCenterInUnityFrame = GetChestCenterPositionInUnityFrame();
            Debug.Log("Emergency debug: pelvisCenterInUnityFrame = " + pelvisCenterInUnityFrame);
            // Compute the mid-shoulder point by extending a vector from mid-pelvis to mid-chest
            // by the measured length from pelvis to shoulders
            // The segment length is the distance from pelvic center to shoulders


            Vector3 vectorChestToPelvis = chestCenterInUnityFrame - pelvisCenterInUnityFrame;
            Vector3 unitVectorChestToPelvis = vectorChestToPelvis.normalized;
            float lengthFromPelvicBeltTrackerToShoulderMidpointInMeters = subjectSpecificDataScript.GetLengthFromPelvisViveTrackerToShoulderInMeters();
            Vector3 vectorNeckToPelvis = unitVectorChestToPelvis * lengthFromPelvicBeltTrackerToShoulderMidpointInMeters;
            
            Vector3 positionOfMidShoulderInUnityFrame = vectorNeckToPelvis + pelvisCenterInUnityFrame;

            Vector3 vectorPelvisCenterToMidShoulder = positionOfMidShoulderInUnityFrame - pelvisCenterInUnityFrame;

            //Vector3 vectorPelvisCenterToMidShoulder = new Vector3(1.0f, 1.0f, 1.0f);
            segmentLength = vectorPelvisCenterToMidShoulder.magnitude;
            Debug.Log("Emergency debug: trunk segmentLength = " + segmentLength);



            // The length from the pelvic revolute joints to trunk mass center is 
            // The fraction from Tisserand times the distance from HJC to shoulder center
            // minus the difference between this length and the segment length
            (_, _, Vector3 midPointOfHJC) = GetHjcAndHjcMidpointPositionsInUnityFrame();
            Vector3 vectorMidShoulderToHJCCenter = positionOfMidShoulderInUnityFrame - midPointOfHJC;
            Vector3 vectorCOMToHJC = distanceFromProximalMarkerToComAsFractionOfSegmentLength * vectorMidShoulderToHJCCenter;
            Vector3 vectorCOMToPelvisCenter = vectorCOMToHJC - (vectorMidShoulderToHJCCenter - vectorPelvisCenterToMidShoulder);
            lengthToJointMassCenter = vectorCOMToPelvisCenter.magnitude;
            Debug.Log("Emergency debug: vectorMidShoulderToHJCCenter = " + vectorMidShoulderToHJCCenter);
            Debug.Log("Emergency debug: midPointOfHJC = " + vectorMidShoulderToHJCCenter);
            Debug.Log("Emergency debug: lengthToJointMassCenter = " + lengthToJointMassCenter);

        }

        float segmentLengthInMeters = segmentLength;
        float lengthToJointMassCenterInMeters = lengthToJointMassCenter;

        return (segmentFractionOfTotalBodyMass, segmentLengthInMeters, lengthToJointMassCenterInMeters);
    }

    // END: public functions ****************************************************************************************************




    // Get the right knee center by extending a vector from the 
    // right ankle Vive tracker through right shank tracker, with 
    // a measured ankle-knee distance.
    public Vector3 GetRightKneeCenterPositionInUnityFrame()
    {
        // Get the ankle and shank tracker positions in Unity frame
        Vector3 rightShankPositionUnityFrame = rightShankViveTracker.transform.position;
        Vector3 rightAnklePositionUnityFrame = rightAnkleViveTracker.transform.position;
        
        // Create vector from ankle to shank in Unity frame
        Vector3 unitVectorAnkleTowardsKnee = rightShankPositionUnityFrame - rightAnklePositionUnityFrame;
        
        // Normalize vector from ankle to shank 
        unitVectorAnkleTowardsKnee = unitVectorAnkleTowardsKnee / unitVectorAnkleTowardsKnee.magnitude;

        // Multiply this vector by the measured ankle-knee distance
        Vector3 distanceVectorAnkleToKneeInUnityInMeters = unitVectorAnkleTowardsKnee * subjectSpecificDataScript.GetDistanceAnkleToKneeInMeters();
     
        Vector3 rightKneeCenterPositionInUnityFrame = 
            distanceVectorAnkleToKneeInUnityInMeters + rightAnklePositionUnityFrame;
        //  Add distance vector to 
        // ankle position to get knee position in Unity frame. Return the position
        return rightKneeCenterPositionInUnityFrame;

    }

    public Vector3 GetLeftKneeCenterPositionInUnityFrame()
    {
        // Get the ankle and shank tracker positions in Unity frame
        Vector3 leftShankPositionUnityFrame = leftShankViveTracker.transform.position;
        Vector3 leftAnklePositionUnityFrame = leftAnkleViveTracker.transform.position;

        // Create vector from ankle to shank in Unity frame
        Vector3 unitVectorAnkleTowardsKnee = leftShankPositionUnityFrame - leftAnklePositionUnityFrame;

        // Normalize vector from ankle to shank 
        unitVectorAnkleTowardsKnee = unitVectorAnkleTowardsKnee.normalized;

        // Multiply this vector by the measured ankle-knee distance
        Vector3 distanceVectorAnkleToKneeInUnityInMeters = unitVectorAnkleTowardsKnee * subjectSpecificDataScript.GetDistanceAnkleToKneeInMeters();

        //  Add distance vector to 
        // ankle position to get knee position in Unity frame. Return the position
        return (distanceVectorAnkleToKneeInUnityInMeters + leftAnklePositionUnityFrame);

    }





    // START: Pelvic bel functions. Includes ellipse functions **********************************************************************************************


    public Vector3 GetPelvicCenterPositionInUnityFrame()
    {    float threeDPrintingOffset = subjectSpecificDataScript.GetOffsetForThreeDPrintedViveTrackerMount();
        // Get the position of the pelvic center in pelvis tracker left-handed frame. 
        Vector3 pelvisCenterPosPelvicTrackerLefthandedFrame =
            new Vector3(0.0f, 0.0f, (-threeDPrintingOffset-pelvicAnteroposteriorAxisRadiusInMeters) / pelvicViveTracker.transform.localScale.z);

        // Transform from pelvic tracker left-handed local frame to Unity (world) frame
        Vector3 pelvisCenterPosUnityFrame = pelvicViveTracker.transform.TransformPoint(pelvisCenterPosPelvicTrackerLefthandedFrame);

        if (visualizeKinematicSkeletonFlag == true)
        {
            pelvicCenterVisualizerObject.transform.position = pelvisCenterPosUnityFrame;
        }

        // Return 
        return pelvisCenterPosUnityFrame;
    }


    public Vector3 GetVirtualAdjustedPelvicCenterPositionInUnityFrame()
    {
        float threeDPrintingOffset = subjectSpecificDataScript.GetOffsetForThreeDPrintedViveTrackerMount();
        // Get the position of the pelvic center in pelvis tracker left-handed frame. 
        Vector3 pelvisCenterPosPelvicTrackerLefthandedFrame =
            new Vector3(0.0f, 0.0f, (-threeDPrintingOffset - pelvicAnteroposteriorAxisRadiusInMeters) / pelvicViveTracker.transform.localScale.z);

        // Transform from pelvic tracker left-handed local frame to Unity (world) frame
        Vector3 pelvisCenterPosUnityFrame = pelvicViveTrackerVirtual.transform.TransformPoint(pelvisCenterPosPelvicTrackerLefthandedFrame);

        // Return 
        return pelvisCenterPosUnityFrame;
    }

    public Vector3 GetRightShankBeltCenterPositionInUnityFrame()
    {
        float threeDPrintOffest = subjectSpecificDataScript.GetOffsetForThreeDPrintedViveTrackerMount();
        // Get the position of the pelvic center in pelvis tracker left-handed frame. 
        Vector3 rightShankCenterPosTrackerLefthandedFrame =
            new Vector3(0.0f, 0.0f, (-subjectSpecificDataScript.GetRightShankPerimeterInMeters()/(2*Mathf.PI) - threeDPrintOffest )/ rightShankViveTracker.transform.localScale.z);

        // Transform from pelvic tracker left-handed local frame to Unity (world) frame
        Vector3 rightShankCenterPosUnityFrame = rightShankViveTracker.transform.TransformPoint(rightShankCenterPosTrackerLefthandedFrame);

        /*if (visualizeKinematicSkeletonFlag == true)
        {
            pelvicCenterVisualizerObject.transform.position = pelvisCenterPosUnityFrame;
        }*/

        // Return 
        return rightShankCenterPosUnityFrame;
    }
    
    public Vector3 GetLeftShankBeltCenterPositionInUnityFrame()
    {
        float threeDPrintOffest = subjectSpecificDataScript.GetOffsetForThreeDPrintedViveTrackerMount();
        // Get the position of the pelvic center in pelvis tracker left-handed frame. 
        Vector3 leftShankCenterPosTrackerLefthandedFrame =
            new Vector3(0.0f, 0.0f, (-subjectSpecificDataScript.GetLeftShankPerimeterInMeters()/(2*Mathf.PI) - threeDPrintOffest ) / leftShankViveTracker.transform.localScale.z);

        // Transform from pelvic tracker left-handed local frame to Unity (world) frame
        Vector3 leftShankCenterPosUnityFrame = leftShankViveTracker.transform.TransformPoint(leftShankCenterPosTrackerLefthandedFrame);

        /*if (visualizeKinematicSkeletonFlag == true)
        {
            pelvicCenterVisualizerObject.transform.position = pelvisCenterPosUnityFrame;
        }*/

        // Return 
        return leftShankCenterPosUnityFrame;
    }
    
    public Vector3 GetPelvicCenterPositionInFrame0()
    {
        Matrix4x4 transformationFromUnitytoFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;

        Vector3 pelvisCenterPosFrame0 =
            transformationFromUnitytoFrame0.MultiplyPoint3x4(GetPelvicCenterPositionInUnityFrame());
        // Return 
        return pelvisCenterPosFrame0;
    }
    
    public Vector3 GetRightShankBeltCenterPositionInFrame0()
    {
        Matrix4x4 transformationFromUnitytoFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;

        Vector3 rightShankCenterPosFrame0 =
            transformationFromUnitytoFrame0.MultiplyPoint3x4(GetRightShankBeltCenterPositionInUnityFrame());
        // Return 
        return rightShankCenterPosFrame0;
    }
    
    public Vector3 GetLeftShankBeltCenterPositionInFrame0()
    {
        Matrix4x4 transformationFromUnitytoFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;

        Vector3 leftShankCenterPosFrame0 =
            transformationFromUnitytoFrame0.MultiplyPoint3x4(GetLeftShankBeltCenterPositionInUnityFrame());
        // Return 
        return leftShankCenterPosFrame0;
    }

    public Vector3 GetChestCenterPositionInFrame0()
    {
        Matrix4x4 transformationFromUnitytoFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;

        Vector3 chestCenterPosFrame0 =
            transformationFromUnitytoFrame0.MultiplyPoint3x4(GetChestCenterPositionInUnityFrame());
        // Return 
        return chestCenterPosFrame0;
    }

    public Vector3 GetKneeCenterInFrame0()
    {
        Matrix4x4 transformationFromUnitytoFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;
        Vector3 kneeCenterInUnityFrame =
            (GetLeftKneeCenterPositionInUnityFrame() + GetRightKneeCenterPositionInUnityFrame()) / 2.0f;
        Vector3 kneeCenterInFrame0 = transformationFromUnitytoFrame0.MultiplyPoint3x4(kneeCenterInUnityFrame);
        return kneeCenterInFrame0;
    }
    
    public Vector3 GetAnkleCenterInFrame0()
    {
        Matrix4x4 transformationFromUnitytoFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;
        Vector3 ankleCenterInUnityFrame =
            (leftAnkleViveTracker.transform.position + rightAnkleViveTracker.transform.position) / 2.0f;
        Vector3 ankleCenterInFrame0 = transformationFromUnitytoFrame0.MultiplyPoint3x4(ankleCenterInUnityFrame);
        return ankleCenterInFrame0;
    }

    public Vector3 GetAnkleCenterInUnityFrame()
    {
        Vector3 ankleCenterInUnityFrame =
            (leftAnkleViveTracker.transform.position + rightAnkleViveTracker.transform.position) / 2.0f;
        return ankleCenterInUnityFrame;
    }

    public GameObject GetPelvicViveTrackerGameObject()
    {
        return pelvicViveTracker;
        
    }


    public GameObject GetChestViveTrackerGameObject()
    {
        return chestViveTracker;
    }
    public Vector3 GetChestCenterPositionInUnityFrame()
    {
        float threeDPrintingOffset = subjectSpecificDataScript.GetOffsetForThreeDPrintedViveTrackerMount();
        Vector3 chestCenterPosPelvicTrackerLefthandedFrame =
            new Vector3(0.0f, 0.0f, (-chestAnteroposteriorAxisRadiusInMeters-threeDPrintingOffset) / chestViveTracker.transform.localScale.z);

        // Transform from pelvic tracker left-handed local frame to Unity (world) frame
        Vector3 chestCenterPosUnityFrame = chestViveTracker.transform.TransformPoint(chestCenterPosPelvicTrackerLefthandedFrame);
        
        if (visualizeKinematicSkeletonFlag == true)
        {
            chestCenterVisualizerObject.transform.position = chestCenterPosUnityFrame;
        }

        // Return 
        return chestCenterPosUnityFrame;
    }




    // Since the belt attachments are closely connected with the Vive trackers, we
    // visualize belt attachments in this script. 
    // Called by the BuildStructureMatricesThisFrame script after all structure matrices have been computed.
    public void VisualizeAllBeltAttachmentsInRenderingFrame0()
    {
        // Visualize pulleys
        if (visualizeKinematicSkeletonFlag == true)
        {
            // Define the columns of the matrix from frame 0 to new unity frame
            Vector4 col0 = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            Vector4 col1 = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            Vector4 col2 = new Vector4(0.0f, 0.0f, -1.0f, 0.0f);
            Vector4 col3 = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            Matrix4x4 transformationFrame0ToRenderingFrame0 = new Matrix4x4(col0, col1, col2, col3);

            if (pulleysAlreadyVisualizedFlag == false)
            {
                // Visualize the pulleys
                if (usingChestBelt == true)
                {
                    // Iterating through the pulleys, set the visualizer positions
                    List<Vector3> chestPulleyPositionsFrame0 = buildStructureMatrixScript.GetChestBeltPulleyPositionInFrame0();
                    for (int pulleyIndex = 0; pulleyIndex < chestPulleyPositionsFrame0.Count; pulleyIndex++)
                    {
                        // Set the position of the visualizer game object for the current pulley to the correct position
                        chestPulleyVisualizerList[pulleyIndex].transform.position =
                            transformationFrame0ToRenderingFrame0.MultiplyPoint3x4(chestPulleyPositionsFrame0[pulleyIndex]);
                    }
                }

                // If we're using the chest belt
                if (usingPelvicBelt == true)
                {

                    // Iterating through the pulleys, set the visualizer positions
                    List<Vector3> pelvisPulleyPositionsFrame0 = buildStructureMatrixScript.GetPelvicBeltPulleyPositionInFrame0();
                    for (int pulleyIndex = 0; pulleyIndex < pelvisPulleyPositionsFrame0.Count; pulleyIndex++)
                    {
                        pelvisPulleyVisualizerList[pulleyIndex].transform.position =
                            transformationFrame0ToRenderingFrame0.MultiplyPoint3x4(pelvisPulleyPositionsFrame0[pulleyIndex]);
                    }
                }

                // If we're using the shank belts
                if (usingShankBelts == true)
                {
                    // Iterating through the pulleys, set the visualizer positions
                    List<Vector3> shankPulleyPositionsFrame0 = buildStructureMatrixScript.GetLeftAndRightShankBeltPulleyPositionInFrame0();
                    for (int pulleyIndex = 0; pulleyIndex < shankPulleyPositionsFrame0.Count; pulleyIndex++)
                    {
                        shankPulleyVisualizerList[pulleyIndex].transform.position =
                            transformationFrame0ToRenderingFrame0.MultiplyPoint3x4(shankPulleyPositionsFrame0[pulleyIndex]);
                    }

                }

                // Set flag to true. Could not set if we want pulleys to move with subject.
                pulleysAlreadyVisualizedFlag = true;
            }
        }
           
        // Visualize cable attachments after pulleys have been visualized
        if(pulleysAlreadyVisualizedFlag == true) // 
        {
            // If we're visualizing the kinematic skeleton
            if (visualizeKinematicSkeletonFlag == true)
            {

                // Define the columns of the matrix from frame 0 to new unity frame
                Vector4 col0 = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
                Vector4 col1 = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                Vector4 col2 = new Vector4(0.0f, 0.0f, -1.0f, 0.0f);
                Vector4 col3 = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                Matrix4x4 transformationFrame0ToRenderingFrame0 = new Matrix4x4(col0, col1, col2, col3);

                // If we're using the chest belt
                if (usingChestBelt == true)
                {
                    // Get the chest belt attachment positions in frame 0
                    List<Vector3> chestBeltAttachmentPoints =
                        buildStructureMatrixScript.GetChestBeltAttachmentPointInFrame0();

                    // Iterating through the attachment positions, set the attachment visualizer positions
                    for (int attachmentIndex = 0; attachmentIndex < chestBeltAttachmentPoints.Count; attachmentIndex++)
                    {
                        // Set the position of the visualizer game object for the current cable attachment to the correct position
                        chestCableAttachmentVisualizerList[attachmentIndex].transform.position =
                            transformationFrame0ToRenderingFrame0.MultiplyPoint3x4(chestBeltAttachmentPoints[attachmentIndex]);
                    }

                }

                // If we're using the pelvic belt
                if (usingPelvicBelt == true)
                {
                    // Get the pelvic belt attachment positions in frame 0.
                    // NOTE: the pelvic belt can have 8 cables but only 4 attachments. 
                    // Instead of calling GetPelvisBeltAttachmentPointInFrame0(), we'll call
                    // GetUniquePelvisBeltAttachmentPointInFrame0(), which will only return unique attachments.
                    List<Vector3> pelvicBeltAttachmentPoints =
                        buildStructureMatrixScript.GetUniquePelvisBeltAttachmentPointInFrame0();

                    // Iterating through the attachment positions, set the attachment visualizer positions
                    for (int attachmentIndex = 0; attachmentIndex < pelvicBeltAttachmentPoints.Count; attachmentIndex++)
                    {
                        pelvisCableAttachmentVisualizerList[attachmentIndex].transform.position =
                            transformationFrame0ToRenderingFrame0.MultiplyPoint3x4(pelvicBeltAttachmentPoints[attachmentIndex]);
                    }

                }

                // If we're using the shank belts
                if (usingShankBelts == true)
                {
                    // Get the right shank belt attachment positions in frame 0
                    List<Vector3> shankBeltAttachmentPoints =
                        buildStructureMatrixScript.GetLeftAndRightShankAttachmentPositionInFrame0();

                    // Iterating through the attachment positions, set the attachment visualizer positions
                    for (int attachmentIndex = 0; attachmentIndex < shankBeltAttachmentPoints.Count; attachmentIndex++)
                    {
                        shankCableAttachmentVisualizerList[attachmentIndex].transform.position =
                            transformationFrame0ToRenderingFrame0.MultiplyPoint3x4(shankBeltAttachmentPoints[attachmentIndex]);
                    }
                }
            }

        }
    }

    private (Vector3, Vector3, Vector3) GetHjcAndHjcMidpointPositionsInUnityFrame()
    {
        // Get the interASIS width, which was measured manually


        // Assuming the x-coordinate is + (1/2) * interASIS width for the right ASIS
        float ellipseFramex = 0.5f * subjectSpecificDataScript.GetInterAsisWidthInMeters();
        // or - (1/2) * interASIS width for the left ASIS, get the y-coordinates for both ASIS positions
        // in "ellipse frame"
        float ellipseFramey = pelvicAnteroposteriorAxisRadiusInMeters / pelvicMediolateralAxisRadiusInMeters
            * Mathf.Sqrt(pelvicMediolateralAxisRadiusInMeters * pelvicMediolateralAxisRadiusInMeters -
            (ellipseFramex * ellipseFramex));
        Debug.Log("Emergency debug: ellipseFramex = " + ellipseFramex);
        Debug.Log("Emergency debug: ellipseFramey = " + ellipseFramey);
        Debug.Log("Emergency debug: pelvicAnteroposteriorAxisRadiusInMeters = " + pelvicAnteroposteriorAxisRadiusInMeters);
        Debug.Log("Emergency debug: pelvicMediolateralAxisRadiusInMeters = " + pelvicMediolateralAxisRadiusInMeters);

        Vector3 rightASISInellipseFrame = new Vector3(ellipseFramex, ellipseFramey, 0.0f);
        Vector3 leftASISInellipseFrame = new Vector3(-ellipseFramex, ellipseFramey, 0.0f);
        // Ellipse frame = rightwards (major axis) is x-axis, forwards (minor axis) is y-axis, z-axis is upwards.

        // Add offsets to get HJC positions in "ellipse frame"
        float interAsisWidthInMeters = subjectSpecificDataScript.GetInterAsisWidthInMeters();
        Vector3 offsetRightAsisToHjcInPelvisCoords = new Vector3(-0.141f * interAsisWidthInMeters, 
                                                                 -0.193f * interAsisWidthInMeters,
                                                                 -0.304f * interAsisWidthInMeters);

        Vector3 offsetLeftAsisToHjcInPelvisCoords = new Vector3(0.141f * interAsisWidthInMeters, 
                                                               -0.193f * interAsisWidthInMeters,
                                                               -0.304f * interAsisWidthInMeters);
        Vector4 rightHjcInEllipseCoords = new Vector4(rightASISInellipseFrame.x + offsetRightAsisToHjcInPelvisCoords.x,
                                                        rightASISInellipseFrame.y + offsetRightAsisToHjcInPelvisCoords.y,
                                                        rightASISInellipseFrame.z + offsetRightAsisToHjcInPelvisCoords.z,
                                                        1.0f);
        Vector4 leftHjcInEllipseCoords = new Vector4(leftASISInellipseFrame.x + offsetLeftAsisToHjcInPelvisCoords.x,
                                                        leftASISInellipseFrame.y + offsetLeftAsisToHjcInPelvisCoords.y,
                                                        leftASISInellipseFrame.z + offsetLeftAsisToHjcInPelvisCoords.z,
                                                        1.0f);

        // Transform the HJC positions to pelvic belt tracker frame
        Vector4 rightHjcInEllipseCoordsInPelvicBeltTrackerFrame = 
            BuildTransformationMatrixFromEllipseFrameToViveTrackerFrameGivenEllipseSize(pelvicMediolateralAxisRadiusInMeters,
            pelvicAnteroposteriorAxisRadiusInMeters) * rightHjcInEllipseCoords;
        Vector4 leftHjcInEllipseCoordsInPelvicBeltTrackerFrame = 
            BuildTransformationMatrixFromEllipseFrameToViveTrackerFrameGivenEllipseSize(pelvicMediolateralAxisRadiusInMeters,
            pelvicAnteroposteriorAxisRadiusInMeters) * leftHjcInEllipseCoords;
        Vector4 middleHjcInEllipseCoordsInPelvicBeltTrackerFrame = (rightHjcInEllipseCoordsInPelvicBeltTrackerFrame +
           leftHjcInEllipseCoordsInPelvicBeltTrackerFrame) * 0.5f;

        // Transform the HJC positions to Unity frame
        // We have to express the point in local frame but divide by local scale, as TransformPoint considers local scale
        Vector4 rightHjcInEllipseCoordsInUnityFrame4 = pelvicViveTracker.transform.TransformPoint
            (rightHjcInEllipseCoordsInPelvicBeltTrackerFrame / pelvicViveTracker.transform.localScale.x);
        Vector4 leftHjcInEllipseCoordsInUnityFrame4 = pelvicViveTracker.transform.TransformPoint
            (leftHjcInEllipseCoordsInPelvicBeltTrackerFrame / pelvicViveTracker.transform.localScale.x);
        Vector4 middleHjcInEllipseCoordsInUnityFrame4 = pelvicViveTracker.transform.TransformPoint
            (middleHjcInEllipseCoordsInPelvicBeltTrackerFrame / pelvicViveTracker.transform.localScale.x);

        Vector3 rightHjcInEllipseCoordsInUnityFrame = rightHjcInEllipseCoordsInUnityFrame4;
        Vector3 leftHjcInEllipseCoordsInUnityFrame = leftHjcInEllipseCoordsInUnityFrame4;
        Vector3 middleHjcInEllipseCoordsInUnityFrame = middleHjcInEllipseCoordsInUnityFrame4;
        return (rightHjcInEllipseCoordsInUnityFrame,
                leftHjcInEllipseCoordsInUnityFrame,
                middleHjcInEllipseCoordsInUnityFrame);
    }

    private Matrix4x4 BuildTransformationMatrixFromEllipseFrameToViveTrackerFrameGivenEllipseSize(
        float beltMediolateralRadius, float beltAnteroposteriorRadius)
    {
        // The rotation matrix is straightforward. 
        // X-axis: the tracker x-axis is left, the ellipse x-axis is right, so first column is [-1, 0, 0].
        // Y-axis: the tracker y-axis is backwards, the ellipse y-axis is forwards, so second column is [0, -1, 0].
        // Z-axis: the tracker z-axis is upwards, the ellipse z-axis is upwards, so third column is [0, 0, 1].
        Matrix4x4 transformationEllipseToBeltFrame = new Matrix4x4();
        transformationEllipseToBeltFrame.SetColumn(0, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        transformationEllipseToBeltFrame.SetColumn(1, new Vector4(0.0f, 0.0f, -1.0f, 0.0f));
        transformationEllipseToBeltFrame.SetColumn(2, new Vector4(0.0f, -1.0f, 0.0f, 0.0f));

        // The position vector is the position of the ellipse frame origin in tracker frame. 
        // The ellipse origin is in the center of the ellipse. 
        // The ellipse origin is at 1 minor axis (y-axis) radius along the -y-axis in the tracker frame. 
        // So the vector is [0, -minorAxis, 0]
        transformationEllipseToBeltFrame.SetColumn(3, new Vector4(0.0f, -beltAnteroposteriorRadius, 0.0f, 1.0f));

        // Return the assembled transformation matrix
        return transformationEllipseToBeltFrame;
    }

    public Vector3 GetTrackerPositionInFrame0(GameObject viveTracker)
    {
        Vector3 viveTrackerInUnityFrame = viveTracker.transform.position;
        Matrix4x4 transformationMatrixFromUnityFrameToFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;
        Vector3 viveFrackerPositionInFrame0 = transformationMatrixFromUnityFrameToFrame0.MultiplyPoint3x4(viveTrackerInUnityFrame);
        return viveFrackerPositionInFrame0;
    }
    public (Vector3, Vector3, Vector3, Vector3, Vector3, Vector3) GetAllTrackerPositionInFrame0()
    {
        Vector3 leftAnkleViveTrackerInFrame0 = GetTrackerPositionInFrame0(leftAnkleViveTracker);
        Vector3 rightAnkleViveTrackerInFrame0 = GetTrackerPositionInFrame0(rightAnkleViveTracker);
        Vector3 leftShankViveTrackerInFrame0 = GetTrackerPositionInFrame0(leftShankViveTracker);
        Vector3 rightShankViveTrackerInFrame0 = GetTrackerPositionInFrame0(rightShankViveTracker);
        Vector3 pelvicViveTrackerInFrame0 = GetTrackerPositionInFrame0(pelvicViveTracker);
        Vector3 chestViveTrackerInFrame0 = GetTrackerPositionInFrame0(chestViveTracker);
        
        return (leftAnkleViveTrackerInFrame0, rightAnkleViveTrackerInFrame0, leftShankViveTrackerInFrame0, rightShankViveTrackerInFrame0, 
            pelvicViveTrackerInFrame0, chestViveTrackerInFrame0);
    }

    public (Vector3, Vector3, Vector3, Vector3, Vector3, Vector3) GetAllTrackerPositionInUnityFrame()
    {
        // Return the Unity-frame Vive tracker positions
        return (leftAnkleViveTracker.transform.position, rightAnkleViveTracker.transform.position,
            leftShankViveTracker.transform.position, rightShankViveTracker.transform.position,
            pelvicViveTracker.transform.position, chestViveTracker.transform.position);
    }




    public (Vector3, Vector3, Vector3) GetTrackerOrientationInFrame0(GameObject viveTracker)
    {
        Vector3 viveTrackerxDirectionInUnityFrame = viveTracker.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));
        Vector3 viveTrackeryDirectionInUnityFrame = viveTracker.transform.TransformDirection(new Vector3(0.0f, 1.0f, 0.0f));
        Vector3 viveTrackerzDirectionInUnityFrame = viveTracker.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f));

        Matrix4x4 transformationMatrixFromUnityFrameToFrame0 = GetTransformationMatrixFromFrame0ToUnityFrame().inverse;

        Vector3 viveTrackerxDirectionInFrame0 = transformationMatrixFromUnityFrameToFrame0.MultiplyVector(viveTrackerxDirectionInUnityFrame);
        Vector3 viveTrackeryDirectionInFrame0 = transformationMatrixFromUnityFrameToFrame0.MultiplyVector(viveTrackeryDirectionInUnityFrame);
        Vector3 viveTrackerzDirectionInFrame0 = transformationMatrixFromUnityFrameToFrame0.MultiplyVector(viveTrackerzDirectionInUnityFrame);

        return (viveTrackerxDirectionInFrame0, viveTrackeryDirectionInFrame0, viveTrackerzDirectionInFrame0);
    }


    // Get Vive tracker frame unit vectors in Unity frame
    public (Vector3, Vector3, Vector3) GetTrackerOrientationInUnityFrame(GameObject viveTracker)
    {
        Vector3 viveTrackerXDirectionInUnityFrame = viveTracker.transform.TransformDirection(new Vector3(1.0f, 0.0f, 0.0f));
        Vector3 viveTrackerYDirectionInUnityFrame = viveTracker.transform.TransformDirection(new Vector3(0.0f, 1.0f, 0.0f));
        Vector3 viveTrackerZDirectionInUnityFrame = viveTracker.transform.TransformDirection(new Vector3(0.0f, 0.0f, 1.0f));

        return (viveTrackerXDirectionInUnityFrame, viveTrackerYDirectionInUnityFrame, viveTrackerZDirectionInUnityFrame);
    }



    public List<Vector3> GetAllTrackerOrientationsInFrame0()
    {
        // Get tracker orientations for each tracker
        (Vector3 pelvicViveTrackerxDirectionInFrame0, Vector3 pelvicViveTrackeryDirectionInFrame0,
            Vector3 pelvicViveTrackerzDirectionInFrame0) = GetTrackerOrientationInFrame0(pelvicViveTracker);
        (Vector3 chestViveTrackerxDirectionInFrame0, Vector3 chestViveTrackeryDirectionInFrame0,
            Vector3 chestViveTrackerzDirectionInFrame0) = GetTrackerOrientationInFrame0(chestViveTracker);
        (Vector3 leftAnkleViveTrackerxDirectionInFrame0, Vector3 leftAnkleViveTrackeryDirectionInFrame0,
            Vector3 leftAnkleViveTrackerzDirectionInFrame0) = GetTrackerOrientationInFrame0(leftAnkleViveTracker);
        (Vector3 rightAnkleViveTrackerxDirectionInFrame0, Vector3 rightAnkleViveTrackeryDirectionInFrame0,
            Vector3 rightAnkleViveTrackerzDirectionInFrame0) = GetTrackerOrientationInFrame0(rightAnkleViveTracker);
        (Vector3 leftShankViveTrackerxDirectionInFrame0, Vector3 leftShankViveTrackeryDirectionInFrame0,
            Vector3 leftShankViveTrackerzDirectionInFrame0) = GetTrackerOrientationInFrame0(rightAnkleViveTracker);
        (Vector3 rightShankViveTrackerxDirectionInFrame0, Vector3 rightShankViveTrackeryDirectionInFrame0,
            Vector3 rightShankViveTrackerzDirectionInFrame0) = GetTrackerOrientationInFrame0(rightAnkleViveTracker);

        // Create a list to hold all Vector3 values
        List<Vector3> allTrackerOrientationsInFrame0 = new List<Vector3>
    {
        leftAnkleViveTrackerxDirectionInFrame0,
        leftAnkleViveTrackeryDirectionInFrame0,
        leftAnkleViveTrackerzDirectionInFrame0,
        rightAnkleViveTrackerxDirectionInFrame0,
        rightAnkleViveTrackeryDirectionInFrame0,
        rightAnkleViveTrackerzDirectionInFrame0,
        leftShankViveTrackerxDirectionInFrame0,
        leftShankViveTrackeryDirectionInFrame0,
        leftShankViveTrackerzDirectionInFrame0,
        rightShankViveTrackerxDirectionInFrame0,
        rightShankViveTrackeryDirectionInFrame0,
        rightShankViveTrackerzDirectionInFrame0,
        pelvicViveTrackerxDirectionInFrame0,
        pelvicViveTrackeryDirectionInFrame0,
        pelvicViveTrackerzDirectionInFrame0,
        chestViveTrackerxDirectionInFrame0,
        chestViveTrackeryDirectionInFrame0,
        chestViveTrackerzDirectionInFrame0,
        
        
    };

        // Return the list containing all Vector3 directions
        return allTrackerOrientationsInFrame0;
    }


    public List<Vector3> GetAllTrackerOrientationsInUnityFrame()
    {
        // Get tracker orientations for each tracker
        (Vector3 pelvicViveTrackerxDirectionInUnityFrame, Vector3 pelvicViveTrackeryDirectionInUnityFrame,
            Vector3 pelvicViveTrackerzDirectionInUnityFrame) = GetTrackerOrientationInUnityFrame(pelvicViveTracker);
        (Vector3 chestViveTrackerxDirectionInUnityFrame, Vector3 chestViveTrackeryDirectionInUnityFrame,
            Vector3 chestViveTrackerzDirectionInUnityFrame) = GetTrackerOrientationInUnityFrame(chestViveTracker);
        (Vector3 leftAnkleViveTrackerxDirectionInUnityFrame, Vector3 leftAnkleViveTrackeryDirectionInUnityFrame,
            Vector3 leftAnkleViveTrackerzDirectionInUnityFrame) = GetTrackerOrientationInUnityFrame(leftAnkleViveTracker);
        (Vector3 rightAnkleViveTrackerxDirectionInUnityFrame, Vector3 rightAnkleViveTrackeryDirectionInUnityFrame,
            Vector3 rightAnkleViveTrackerzDirectionInUnityFrame) = GetTrackerOrientationInUnityFrame(rightAnkleViveTracker);
        (Vector3 leftShankViveTrackerxDirectionInUnityFrame, Vector3 leftShankViveTrackeryDirectionInUnityFrame,
            Vector3 leftShankViveTrackerzDirectionInUnityFrame) = GetTrackerOrientationInUnityFrame(rightAnkleViveTracker);
        (Vector3 rightShankViveTrackerxDirectionInUnityFrame, Vector3 rightShankViveTrackeryDirectionInUnityFrame,
            Vector3 rightShankViveTrackerzDirectionInUnityFrame) = GetTrackerOrientationInUnityFrame(rightAnkleViveTracker);

        // Create a list to hold all Vector3 values
        List<Vector3> allTrackerOrientationsInUnityFrame = new List<Vector3>
    {
        leftAnkleViveTrackerxDirectionInUnityFrame,
        leftAnkleViveTrackeryDirectionInUnityFrame,
        leftAnkleViveTrackerzDirectionInUnityFrame,
        rightAnkleViveTrackerxDirectionInUnityFrame,
        rightAnkleViveTrackeryDirectionInUnityFrame,
        rightAnkleViveTrackerzDirectionInUnityFrame,
        leftShankViveTrackerxDirectionInUnityFrame,
        leftShankViveTrackeryDirectionInUnityFrame,
        leftShankViveTrackerzDirectionInUnityFrame,
        rightShankViveTrackerxDirectionInUnityFrame,
        rightShankViveTrackeryDirectionInUnityFrame,
        rightShankViveTrackerzDirectionInUnityFrame,
        pelvicViveTrackerxDirectionInUnityFrame,
        pelvicViveTrackeryDirectionInUnityFrame,
        pelvicViveTrackerzDirectionInUnityFrame,
        chestViveTrackerxDirectionInUnityFrame,
        chestViveTrackeryDirectionInUnityFrame,
        chestViveTrackerzDirectionInUnityFrame,


    };

        // Return the list containing all Vector3 directions
        return allTrackerOrientationsInUnityFrame;
    }


    public enum FrameZeroComputationSettingsSelect
    {
        LockOnToggleHome, // e.g., after the VR camera is toggled home
        ComputeEachFrame
    }

    public enum FrameZeroOrientationSettingsSelect
    {
        ComputeFromViveTrackers,
        AlignWithReferenceTracker
    }

    public enum ApplyCorrectionToAlignWithForcePlateSelect
    {
        Disabled,
        Enabled
    }
}
