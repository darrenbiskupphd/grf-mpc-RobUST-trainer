// CreateDataFileAndSMatrixViveBased.cs
// Note 1: The Vive tracker frame is as follows: 
//      - +x-axis: leftwards when looking at the face of the tracker, LED downwards. 
//      - +y-axis: outwards when looking at the face of the tracker, LED downwards. 
//      - +z-axis: upwards when looking at the face of the tracker, LED downwards.


using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateDataFileAndSMatrixViveBased : MonoBehaviour
{
    // Subject info manager script
    public SubjectInfoStorageScript subjectInfoScript;

    // The settings object for the structure matrix data computation. 
    // Can specify if we should create structure matrix files for 
    // 1.) Vicon-based control, 2.) Vive-based control, or 3.) both.
    public StructureMatrixSettingsScript structureMatrixSettingsScript;

    // The main script that computes the structure matrices. This script only needs a reference
    // if Vicon is also being used.
    public CreateDataFileForSMatrixAllBelts mainStructureMatrixDataCreationScript;

    // The script that computes the transformation matrix from Vive reference tracker to 
    // Vicon frame. This is only needed if we're using BOTH Vive and Vicon.
    // NOTE: NOT REALLY USED AT THIS POINT. VIVE-BASED RECONSTRUCTION OF VICON MARKERS COULD BE DONE IN REAL TIME.
    private CreateAndSaveViconToUnityFrameTransformation viveToViconTransformationComputationScript;

    // The Vive tracker manager


    // Key dimensions for the pelvic belt
    // The ellipse sizes are retrieved from the SubjectSpecificInfo script
    private float pelvicMediolateralAxisRadiusInMeters;
    private float pelvicAnteroposteriorAxisRadiusInMeters;
    public PelvisBeltSizeSelectEnum pelvicBeltSizeSelect;
    private float pelvicTrackerToBackLeftAttachmentLengthInMeters;
    private float pelvicTrackerToFrontLeftAttachmentLengthInMeters;
    private float pelvicTrackerToBackRightAttachmentLengthInMeters;
    private float pelvicTrackerToFrontRightAttachmentLengthInMeters;

    // Pelvic belt angle to tracker in standard ellipse frame. 
    // The ellipse is a simple model of the belt shape.
    // Standard ellipse frame for a belt = +x-axis is rightwards (the "major axis"), +y-axis is forwards (the "minor axis"). 
    // Angles are measured from the +x-axis CCW. 
    private float pelvicBeltViveTrackerAngleInStandardEllipseFrameRads = (3.0f / 2.0f) * Mathf.PI;

    // The pelvic belt cable attachments in belt Vive tracker frame
    private List<Vector3> pelvicBeltCableAttachmentsInBeltViveTrackerFrame; // Order is set in TryToComputeAllBeltCableAttachmentsInViveTrackerFrame()
                                                                            // as BL, FL, BR, FR.

    // The pelvic belt pulley positions in Vive reference tracker frame 
    private Vector3[] pelvisPulleyPositionsViveReferenceTrackerFrame = new Vector3[8]; // Computed in ComputePelvicBeltPulleyPositionsInViveTrackerFrame(), which is a 
                                                                                       // public function called by the main structure matrix script.

    // The chest belt pulley positions in Vive reference tracker frame
    private Vector3[] chestPulleyPositionsViveReferenceTrackerFrame = new Vector3[4]; // Computed in ComputeChestBeltPulleyPositionsInViveTrackerFrame(), which is a 
                                                                                       // public function called by the main structure matrix script.

    // Hard-coded positional data about the Vive reference tracker height from the ground. 
    // Also can include pelvis, chest, and shank pulley height from ground info, if needed.
    // These data are only used if we're using Vive-only robot control. 
    private float viveReferenceTrackerHeightFromGroundInMeters = 1.04f; // This is pretty much a fixed value, given that the Vive reference tracker should
                                                                         // always be mounted on Robust's lower side crossbar.
    public float upperPelvicPulleyHeightFromGroundInMeters; // the height of the upper pelvic pulley centers from the ground in meters
    public float lowerPelvicPulleyHeightFromGroundInMeters; // the height of the lower pelvic pulley centers from the ground in meters
    public float chestPulleyHeightFromGroundInMeters; // the height of the chest pulley centers from the ground in meters.
    public float shankPulleyHeightFromGroundInMeters; // the height of the shank pulley center from the ground in meters, when wings are level.


    // If we're only using Vive, then the pulley positions relative to the Vive reference tracker are hard-coded in Start(), using
    // only the user-input height of the pulleys from the ground.
    // Upper pelvis pulleys first.
    // The units are in meters and are approximate. They need to be updated if the pulley positions move.
    // Last edited: 5/16/2024.
    private Vector3 frontLeftUpperPulleyPositionInReferenceViveTrackerFrameInMeters; // ADD PULLEY POSITION IN VIVE REF FRAME
    private Vector3 frontRightUpperPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 backRightUpperPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 backLeftUpperPulleyPositionInReferenceViveTrackerFrameInMeters;
    // Lower pelvic pulleys
    private Vector3 frontLeftLowerPulleyPositionInReferenceViveTrackerFrameInMeters; // ADD PULLEY POSITION IN VIVE REF FRAME
    private Vector3 frontRightLowerPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 backRightLowerPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 backLeftLowerPulleyPositionInReferenceViveTrackerFrameInMeters;

    // Computed pelvic belt cable attachment points in belt Vive tracker frame
    private bool readyToReturnPelvicBeltAttachmentPositionsInBeltFrameFlag = false;
    private Vector3 pelvicBeltBackLeftAttachmentPositionBeltTrackerFrame;
    private Vector3 pelvicBeltFrontLeftAttachmentPositionBeltTrackerFrame;
    private Vector3 pelvicBeltBackRightAttachmentPositionBeltTrackerFrame;
    private Vector3 pelvicBeltFrontRightAttachmentPositionBeltTrackerFrame;


    // Key dimensions for the chest belt
    public ChestBeltSizeSelectEnum chestBeltSizeSelect;
    // The ellipse sizes are retrieved from the SubjectSpecificInfo script
    private float chestMediolateralAxisRadiusInMeters;
    private float chestAnteroposteriorAxisRadiusInMeters;

    // Distances chest belt Vive tracker to attachments
    private float chestBeltViveTrackerAngleInStandardEllipseFrameRads = (3.0f / 2.0f) * Mathf.PI; // where the chest belt tracker is located in ellipse frame
    private float chestTrackerToBackLeftAttachmentLengthInMeters;
    private float chestTrackerToFrontLeftAttachmentLengthInMeters;
    private float chestTrackerToBackRightAttachmentLengthInMeters;
    private float chestTrackerToFrontRightAttachmentLengthInMeters;

    // The chest belt cable attachments in belt Vive tracker frame
    private List<Vector3> chestBeltCableAttachmentsInBeltViveTrackerFrame; // Order is set in TryToComputeAllBeltCableAttachmentsInViveTrackerFrame()
                                                                           // as BL, FL, BR, FR.

    // Computed chest belt cable attachment points in belt Vive tracker frame
    private bool readyToReturnChestBeltAttachmentPositionsInBeltFrameFlag = false;
    private Vector3 chestBeltBackLeftAttachmentPositionBeltTrackerFrame;
    private Vector3 chestBeltFrontLeftAttachmentPositionBeltTrackerFrame;
    private Vector3 chestBeltBackRightAttachmentPositionBeltTrackerFrame;
    private Vector3 chestBeltFrontRightAttachmentPositionBeltTrackerFrame;

    // Chest belt pulleys
    private Vector3 frontLeftChestPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 frontRightChestPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 backRightChestPulleyPositionInReferenceViveTrackerFrameInMeters;
    private Vector3 backLeftChestPulleyPositionInReferenceViveTrackerFrameInMeters;
                

    // Key dimensions for the left shank belt
    private float circumferenceOfLeftShankInMeters; // shank perimeter is modeled as a circle

    // Key dimensions for the right shank belt
    private float circumferenceOfRightShankInMeters; // shank perimeter is modeled as a circle
    
    // Shank belt cable attachment points in shank Vive tracker frame
    private Vector3 leftShankCableAttachmentPointInTrackerLeftHandedFrame;
    private Vector3 rightShankCableAttachmentPointInTrackerLeftHandedFrame;
    private bool readyToReturnShankBeltAttachmentPositionsInBeltFrameFlag = false; // whether they've been computed already or not
    
    // If we're only using Vive, then the pulley positions relative to the Vive reference tracker are hard-coded in Start(), using
    // only the user-input height of the pulleys from the ground.
    // The units are in meters and are approximate. They need to be updated if the pulley positions move.
    // Last edited: 8/15/2024.
    private Vector3 leftShankPulleyPositionInReferenceViveTrackerFrameInMeters; // ADD PULLEY POSITION IN VIVE REF FRAME
    private Vector3 rightShankPulleyPositionInReferenceViveTrackerFrameInMeters; 

    // Start is called before the first frame update
    void Start()
    {
        // From the subject information, get the pelvic belt ellipse ML width and AP width. 
        // Divide by two to get the radii describing the pelvic belt ellipse.
        (float pelvisMediolateralLengthInMeters, float pelvisAnteroposteriorLengthInMeters) = 
            subjectInfoScript.GetPelvisEllipseMediolateralAndAnteroposteriorLengths();
        pelvicMediolateralAxisRadiusInMeters = pelvisMediolateralLengthInMeters / 2.0f;
        pelvicAnteroposteriorAxisRadiusInMeters = pelvisAnteroposteriorLengthInMeters / 2.0f;

        // From the subject information, get the chest belt ellipse ML width and AP width. 
        // Divide by two to get the radii describing the pelvic belt ellipse.
        (float chestMediolateralLengthInMeters, float chestAnteroposteriorLengthInMeters) =
            subjectInfoScript.GetChestEllipseMediolateralAndAnteroposteriorLengths();
        chestMediolateralAxisRadiusInMeters = chestMediolateralLengthInMeters / 2.0f;
        chestAnteroposteriorAxisRadiusInMeters = chestAnteroposteriorLengthInMeters / 2.0f;

        // Set the hard-coded pelvic pulley positions based on a user input height from the ground
        // These positions are in Vive reference tracker SOFTWARE frame, which is left-handed.
        // Step 1.) Specify the hard-coded positions that are at the vertical height of the Vive ref tracker and directly
        // underneath the pulleys.
        // These positions are in Vive reference tracker SOFTWARE frame, which is left-handed.
        Vector3 belowFrontLeftPulleyAtViveRefHeightInViveRefFrameMeters = new Vector3(0.728f, 0.019f, 0.002f);
        Vector3 belowFrontRightPulleyAtViveRefHeightInViveRefFrameMeters = new Vector3(0.751f, 0.112f, 1.638f);
        Vector3 belowBackLeftPulleyAtViveRefHeightInViveRefFrameMeters = new Vector3(-0.921f, -0.027f, 0.034f);
        Vector3 belowBackRightPulleyAtViveRefHeightInViveRefFrameMeters = new Vector3(-0.893f, 0.066f, 1.670f);
        // Step 2.) Convert the vertical offset to the upper pelvic pulley, which is input by the user, to an offset in Vive frame using 
        // the hard-coded transformation matrix from Vicon frame to Vive frame. 
        // The offset is expressed in Vicon frame, so it should be a displacement along the positive z-axis, upwards.
        // NOTE: The Vicon frame z-axis is the only meaningful axis, since the wand orientation on the floor is arbitrary. 
        // DO NOT ALLOW user to specify x or y Vicon offsets in the transformation function.
        // NOTE 2: this is necessary because the Vive tracker is not perfectly level, as the RobUST frame is not level due to
        // the uneven floor. IF RobUST MOVES, THIS TRANSFORMATION MATRIX HAS TO BE RECOMPUTED!!!
        float upperPelvicPulleyYAxisOffsetFromViveReferenceTrackerInMeters = upperPelvicPulleyHeightFromGroundInMeters - viveReferenceTrackerHeightFromGroundInMeters;
        Vector3 offsetToUpperPelvicPulleysInViveRefTrackerFrame = ConvertVerticalOffsetInLabFrameToViveRefTrackerFrameOffset(upperPelvicPulleyYAxisOffsetFromViveReferenceTrackerInMeters);

        // Repeat for offset to lower pelvic pulleys
        float lowerPelvicPulleyYAxisOffsetFromViveReferenceTrackerInMeters = lowerPelvicPulleyHeightFromGroundInMeters - viveReferenceTrackerHeightFromGroundInMeters;
        Vector3 offsetToLowerPelvicPulleysInViveRefTrackerFrame = ConvertVerticalOffsetInLabFrameToViveRefTrackerFrameOffset(lowerPelvicPulleyYAxisOffsetFromViveReferenceTrackerInMeters);

        // Repeat for offest to chest pulleys
        float chestPulleyYAxisOffsetFromViveReferenceTrackerInMeters = chestPulleyHeightFromGroundInMeters - viveReferenceTrackerHeightFromGroundInMeters;
        Vector3 offsetToChestPulleysInViveRefTrackerFrame = ConvertVerticalOffsetInLabFrameToViveRefTrackerFrameOffset(chestPulleyYAxisOffsetFromViveReferenceTrackerInMeters);

        // PELVIS UPPER PULLEYS
        // The pulley locations are the "below-pulley" locations level with the Vive ref tracker plus the offset vector in 
        // Vive ref tracker frame.
        frontLeftUpperPulleyPositionInReferenceViveTrackerFrameInMeters =
            belowFrontLeftPulleyAtViveRefHeightInViveRefFrameMeters + offsetToUpperPelvicPulleysInViveRefTrackerFrame; 
        frontRightUpperPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowFrontRightPulleyAtViveRefHeightInViveRefFrameMeters + offsetToUpperPelvicPulleysInViveRefTrackerFrame;
        backLeftUpperPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowBackLeftPulleyAtViveRefHeightInViveRefFrameMeters + offsetToUpperPelvicPulleysInViveRefTrackerFrame;
        backRightUpperPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowBackRightPulleyAtViveRefHeightInViveRefFrameMeters + offsetToUpperPelvicPulleysInViveRefTrackerFrame;

        // PELVIS LOWER PULLEYS
        // The pulley locations are the "below-pulley" locations level with the Vive ref tracker plus the offset vector in 
        // Vive ref tracker frame.
        frontLeftLowerPulleyPositionInReferenceViveTrackerFrameInMeters =
            belowFrontLeftPulleyAtViveRefHeightInViveRefFrameMeters + offsetToLowerPelvicPulleysInViveRefTrackerFrame;
        frontRightLowerPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowFrontRightPulleyAtViveRefHeightInViveRefFrameMeters + offsetToLowerPelvicPulleysInViveRefTrackerFrame;
        backLeftLowerPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowBackLeftPulleyAtViveRefHeightInViveRefFrameMeters + offsetToLowerPelvicPulleysInViveRefTrackerFrame;
        backRightLowerPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowBackRightPulleyAtViveRefHeightInViveRefFrameMeters + offsetToLowerPelvicPulleysInViveRefTrackerFrame;

        // Chest pulleys
        frontLeftChestPulleyPositionInReferenceViveTrackerFrameInMeters =
            belowFrontLeftPulleyAtViveRefHeightInViveRefFrameMeters + offsetToChestPulleysInViveRefTrackerFrame;
        frontRightChestPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowFrontRightPulleyAtViveRefHeightInViveRefFrameMeters + offsetToChestPulleysInViveRefTrackerFrame;
        backLeftChestPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowBackLeftPulleyAtViveRefHeightInViveRefFrameMeters + offsetToChestPulleysInViveRefTrackerFrame;
        backRightChestPulleyPositionInReferenceViveTrackerFrameInMeters =
                        belowBackRightPulleyAtViveRefHeightInViveRefFrameMeters + offsetToChestPulleysInViveRefTrackerFrame;

        // Set the shank pulley locations in Vive reference tracker frame. 
        // Step 1.) Specify the hard-coded positions that are at the vertical height of the Vive ref tracker and directly
        // above the shank pulleys. 
        // These positions are in Vive reference tracker SOFTWARE frame, which is left-handed.
        Vector3 aboveLeftShankPulleyAtViveRefHeightInViveRefFrameMeters = new Vector3(-0.783f, 0.013f, 0.684f);
        Vector3 aboveRightShankPulleyAtViveRefHeightInViveRefFrameMeters = new Vector3(-0.767f, 0.035f, 1.048f);

        // Step 2.) Convert the vertical offset in lab space to an offset vector in the left-handed Vive ref tracker frame.
        float shankPulleyYAxisOffsetFromViveReferenceTrackerInMeters = shankPulleyHeightFromGroundInMeters - viveReferenceTrackerHeightFromGroundInMeters;
        Vector3 offsetToShankPulleysInViveRefTrackerFrame = ConvertVerticalOffsetInLabFrameToViveRefTrackerFrameOffset(shankPulleyYAxisOffsetFromViveReferenceTrackerInMeters);

        leftShankPulleyPositionInReferenceViveTrackerFrameInMeters =
          aboveLeftShankPulleyAtViveRefHeightInViveRefFrameMeters + offsetToShankPulleysInViveRefTrackerFrame;
        rightShankPulleyPositionInReferenceViveTrackerFrameInMeters =
            aboveRightShankPulleyAtViveRefHeightInViveRefFrameMeters + offsetToShankPulleysInViveRefTrackerFrame;

        // Get shank perimeters from the subject info
        circumferenceOfLeftShankInMeters = subjectInfoScript.GetLeftShankPerimeterInMeters();
        circumferenceOfRightShankInMeters = subjectInfoScript.GetRightShankPerimeterInMeters();

        // Set distance from pelvic belt tracker to attachment points based on belt size
        SetDistancePelvicTrackerToAttachmentsUsingBeltSize();

        // Set distance from chest belt tracker to attachment points based on belt size
        SetDistanceChestTrackerToAttachmentsUsingBeltSize();
    }
    

    // Update is called once per frame
    void Update()
    {


    }



    // UNITS for belt attachments: meters. 
    // Measure distance from BACK of tracker to the attachment point when the belt is laid flat.
    private void SetDistancePelvicTrackerToAttachmentsUsingBeltSize()
    {
        // Extra-small
        if (pelvicBeltSizeSelect == PelvisBeltSizeSelectEnum.XS)
        {
            pelvicTrackerToBackLeftAttachmentLengthInMeters = 0.105f;
            pelvicTrackerToFrontLeftAttachmentLengthInMeters = 0.19f;
            pelvicTrackerToBackRightAttachmentLengthInMeters = 0.11f;
            pelvicTrackerToFrontRightAttachmentLengthInMeters = 0.23f;

        }
        // Small
        else if (pelvicBeltSizeSelect == PelvisBeltSizeSelectEnum.S)
        {
            pelvicTrackerToBackLeftAttachmentLengthInMeters = 0.09f;
            pelvicTrackerToFrontLeftAttachmentLengthInMeters = 0.20f;
            pelvicTrackerToBackRightAttachmentLengthInMeters = 0.12f;
            pelvicTrackerToFrontRightAttachmentLengthInMeters = 0.23f;
        }
        // Medium
        else if (pelvicBeltSizeSelect == PelvisBeltSizeSelectEnum.M)
        {
            pelvicTrackerToBackLeftAttachmentLengthInMeters = 0.13f;
            pelvicTrackerToFrontLeftAttachmentLengthInMeters = 0.24f;
            pelvicTrackerToBackRightAttachmentLengthInMeters = 0.08f;
            pelvicTrackerToFrontRightAttachmentLengthInMeters = 0.28f;
        }
        else
        {
            Debug.LogError("Pelvic belt size distance to attachments have not been specified!");
        }
    }



    // I BELIEVE VALUES ARE MADE UP AS OF 3/17
    private void SetDistanceChestTrackerToAttachmentsUsingBeltSize()
    {
        // Small 
        if (chestBeltSizeSelect == ChestBeltSizeSelectEnum.S)
        {
            chestTrackerToBackLeftAttachmentLengthInMeters = 0.0f;
            chestTrackerToFrontLeftAttachmentLengthInMeters = 0.0f;
            chestTrackerToBackRightAttachmentLengthInMeters = 0.0f;
            chestTrackerToFrontRightAttachmentLengthInMeters = 0.0f;

        }
        // Large = the first belt we've used (marked with L)
        else if (chestBeltSizeSelect == ChestBeltSizeSelectEnum.L)
        {
            chestTrackerToBackLeftAttachmentLengthInMeters = 0.14f;
            chestTrackerToFrontLeftAttachmentLengthInMeters = 0.37f;
            chestTrackerToBackRightAttachmentLengthInMeters = 0.13f;
            chestTrackerToFrontRightAttachmentLengthInMeters = 0.39f;
            // Medium
        }
        // Else if not specified
        else
        {
            Debug.LogError("Chest belt size distance to attachments have not been specified!");
        }
    }


    // START: PUBLIC FUNCTIONS********************************************************************************************************

    // Retrieves the already computed pelvic belt attachments in pelvic belt tracker frame.
    public (bool, Vector3, Vector3, Vector3, Vector3) GetPelvicBeltAttachmentsInBeltTrackerFrame()
    {
        // Return the already-computed belt attachment points in pelvic belt frame, if they're ready. 
        if(readyToReturnPelvicBeltAttachmentPositionsInBeltFrameFlag == true)
        {
            // Return a true flag, indicating success in retrieving values, and the cable attachment points in belt tracker frame.
            return (readyToReturnPelvicBeltAttachmentPositionsInBeltFrameFlag, pelvicBeltBackLeftAttachmentPositionBeltTrackerFrame,
                pelvicBeltFrontLeftAttachmentPositionBeltTrackerFrame, pelvicBeltBackRightAttachmentPositionBeltTrackerFrame,
                pelvicBeltFrontRightAttachmentPositionBeltTrackerFrame);
        }
        else
        {
            // Return a false flag, indicating failure to retrieve values, and Vector3 values filled with zeros.
            Vector3 zerosVector = new Vector3(0.0f, 0.0f, 0.0f);
            return (readyToReturnPelvicBeltAttachmentPositionsInBeltFrameFlag, zerosVector,
                    zerosVector, zerosVector, zerosVector);
        }
    }

    // Retrieves the already computed chest belt attachments in chest belt tracker frame.
    public (bool, Vector3, Vector3, Vector3, Vector3) GetChestBeltAttachmentsInBeltTrackerFrame()
    {
        // Return the already-computed belt attachment points in pelvic belt frame, if they're ready. 
        if (readyToReturnChestBeltAttachmentPositionsInBeltFrameFlag == true)
        {
            // Return a true flag, indicating success in retrieving values, and the cable attachment points in belt tracker frame.
            return (readyToReturnChestBeltAttachmentPositionsInBeltFrameFlag, chestBeltBackLeftAttachmentPositionBeltTrackerFrame,
                chestBeltFrontLeftAttachmentPositionBeltTrackerFrame, chestBeltBackRightAttachmentPositionBeltTrackerFrame,
                chestBeltFrontRightAttachmentPositionBeltTrackerFrame);
        }
        else
        {
            // Return a false flag, indicating failure to retrieve values, and Vector3 values filled with zeros.
            Vector3 zerosVector = new Vector3(0.0f, 0.0f, 0.0f);
            return (readyToReturnChestBeltAttachmentPositionsInBeltFrameFlag, zerosVector,
                    zerosVector, zerosVector, zerosVector);
        }
    }


    // Retrieves the already computed shank belt attachments in shank belt tracker frame.
    public (bool, Vector3, Vector3) GetLeftAndRightShankBeltAttachmentsInLeftHandedBeltTrackerFrame()
    {
        // Return the already-computed belt attachment points in pelvic belt frame, if they're ready. 
        if(readyToReturnShankBeltAttachmentPositionsInBeltFrameFlag == true)
        {
            // Return a true flag, indicating success in retrieving values, and the cable attachment points in belt tracker frame.
            return (readyToReturnShankBeltAttachmentPositionsInBeltFrameFlag, leftShankCableAttachmentPointInTrackerLeftHandedFrame,
                rightShankCableAttachmentPointInTrackerLeftHandedFrame);
        }
        else
        {
            // Return a false flag, indicating failure to retrieve values, and Vector3 values filled with zeros.
            Vector3 zerosVector = new Vector3(0.0f, 0.0f, 0.0f);
            return (readyToReturnShankBeltAttachmentPositionsInBeltFrameFlag, zerosVector,
                zerosVector);
        }
    }

    // This is a key function for obtaining the structure matrix data. 
    // Computes the cable attachement points expressed in the belt's Vive tracker frame.
    // NOTE that we don't actually need the Vive tracker to be present, since we're just making assumptions about the tracker orientation.
    // We only need estimated ellipse size.
    // We assume that the pelvic belt tracker is facing backwards, with it's LED downwards (downwards Gen 2.0, upwards Gen 3.0), w.r.t. the subject's orientation.
    // As a result, the LEFT-HANDED SOFTWARE FRAME for the tracker is
    // +x-axis is subject's rightwards, +y-axis is subject's downwards, and +z-axis is subject's backwards.
    // Note: this is the function that computes the coordinates in tracker SOFTWARE LEFT-HANDED frame. We will have another function that simply returns the stored values.
    public void ComputePelvicBeltCableAttachmentsInViveTrackerFrame()
    {
        // 1.) Pelvic belt ******************************************************************************************************
        // Compute the pelvic belt attachments in Vive tracker frame
        // For the pelvic belt, choose attachment order and indicate if they're CW or CCW from the tracker
        List<float> distancesFromPelvicBeltTrackerToAttachments = new List<float>()
            {
                pelvicTrackerToBackLeftAttachmentLengthInMeters,
                pelvicTrackerToFrontLeftAttachmentLengthInMeters,
                pelvicTrackerToBackRightAttachmentLengthInMeters,
                pelvicTrackerToFrontRightAttachmentLengthInMeters
            };
        // Specify if CW or CCW
        bool[] correspondingAttachmentsAreClockwiseFlag = new bool[] { true, true, false, false };

        // Compute the pelvic cable attachment points in belt frame (is this tracker software frame?)
        pelvicBeltCableAttachmentsInBeltViveTrackerFrame = ComputeBeltAttachmentsInBeltTrackerFrame(pelvicBeltViveTrackerAngleInStandardEllipseFrameRads, pelvicMediolateralAxisRadiusInMeters,
             pelvicAnteroposteriorAxisRadiusInMeters, distancesFromPelvicBeltTrackerToAttachments, correspondingAttachmentsAreClockwiseFlag);

        // Store the pelvic cable attachments as individual Vector3 values for each attachment point
        pelvicBeltBackLeftAttachmentPositionBeltTrackerFrame = pelvicBeltCableAttachmentsInBeltViveTrackerFrame[0];
        pelvicBeltFrontLeftAttachmentPositionBeltTrackerFrame = pelvicBeltCableAttachmentsInBeltViveTrackerFrame[1];
        pelvicBeltBackRightAttachmentPositionBeltTrackerFrame = pelvicBeltCableAttachmentsInBeltViveTrackerFrame[2];
        pelvicBeltFrontRightAttachmentPositionBeltTrackerFrame = pelvicBeltCableAttachmentsInBeltViveTrackerFrame[3];

        // Print if desired
        Debug.Log("Computed pelvic belt cable attachments in pelvic Vive tracker frame as: BL (x,y,z): " +
            "( " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[0].x + ", " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[0].y + ", "
            + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[0].z + "), " + 
            "FL (x, y, z): " +
            "( " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[1].x + ", " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[1].y + ", "
            + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[1].z + "), " + 
            "BR (x, y, z): " +
            "( " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[2].x + ", " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[2].y + ", "
            + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[2].z + "), " + 
            "FR (x, y, z): " +
            "( " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[3].x + ", " + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[3].y + ", "
            + pelvicBeltCableAttachmentsInBeltViveTrackerFrame[3].z + ")."
            );

        // Set teh flag indicating the pelvic cable attachment positions have been computed
        readyToReturnPelvicBeltAttachmentPositionsInBeltFrameFlag = true;
    }




    // This is a key function for obtaining the structure matrix data. 
    // Computes the cable attachement points expressed in the belt's Vive tracker frame.
    // NOTE that we don't actually need the Vive tracker to be present, since we're just making assumptions about the tracker orientation.
    // We only need estimated ellipse size.
    // We assume that the pelvic belt tracker is facing backwards, with it's LED downwards (downwards Gen 2.0, upwards Gen 3.0), w.r.t. the subject's orientation.
    // As a result, the LEFT-HANDED SOFTWARE FRAME for the tracker is
    // +x-axis is subject's rightwards, +y-axis is subject's downwards, and +z-axis is subject's backwards.
    // Note: this is the function that computes the coordinates in tracker SOFTWARE LEFT-HANDED frame. We will have another function that simply returns the stored values.
    public void ComputeChestBeltCableAttachmentsInViveTrackerFrame()
    {
        // 1.) Pelvic belt ******************************************************************************************************
        // Compute the pelvic belt attachments in Vive tracker frame
        // For the pelvic belt, choose attachment order and indicate if they're CW or CCW from the tracker
        List<float> distancesFromChestBeltTrackerToAttachments = new List<float>()
            {
                chestTrackerToBackLeftAttachmentLengthInMeters,
                chestTrackerToFrontLeftAttachmentLengthInMeters,
                chestTrackerToBackRightAttachmentLengthInMeters,
                chestTrackerToFrontRightAttachmentLengthInMeters
            };
        // Specify if CW or CCW
        bool[] correspondingAttachmentsAreClockwiseFlag = new bool[] { true, true, false, false };

        // Compute the pelvic cable attachment points in belt frame (is this tracker software frame?)
        chestBeltCableAttachmentsInBeltViveTrackerFrame = 
            ComputeBeltAttachmentsInBeltTrackerFrame(chestBeltViveTrackerAngleInStandardEllipseFrameRads, chestMediolateralAxisRadiusInMeters,
             chestAnteroposteriorAxisRadiusInMeters, distancesFromChestBeltTrackerToAttachments, correspondingAttachmentsAreClockwiseFlag);

        // Store the pelvic cable attachments as individual Vector3 values for each attachment point
        chestBeltBackLeftAttachmentPositionBeltTrackerFrame = chestBeltCableAttachmentsInBeltViveTrackerFrame[0];
        chestBeltFrontLeftAttachmentPositionBeltTrackerFrame = chestBeltCableAttachmentsInBeltViveTrackerFrame[1];
        chestBeltBackRightAttachmentPositionBeltTrackerFrame = chestBeltCableAttachmentsInBeltViveTrackerFrame[2];
        chestBeltFrontRightAttachmentPositionBeltTrackerFrame = chestBeltCableAttachmentsInBeltViveTrackerFrame[3];

        // Print if desired
        Debug.Log("Computed chest belt cable attachments in chest Vive tracker frame as: BL (x,y,z): " +
            "( " + chestBeltBackLeftAttachmentPositionBeltTrackerFrame.x + ", " + chestBeltBackLeftAttachmentPositionBeltTrackerFrame.y + ", "
            + chestBeltBackLeftAttachmentPositionBeltTrackerFrame.z + "), " +
            "FL (x, y, z): " +
            "( " + chestBeltFrontLeftAttachmentPositionBeltTrackerFrame.x + ", " + chestBeltFrontLeftAttachmentPositionBeltTrackerFrame.y + ", "
            + chestBeltFrontLeftAttachmentPositionBeltTrackerFrame.z + "), " +
            "BR (x, y, z): " +
            "( " + chestBeltBackRightAttachmentPositionBeltTrackerFrame.x + ", " + chestBeltBackRightAttachmentPositionBeltTrackerFrame.y + ", "
            + chestBeltBackRightAttachmentPositionBeltTrackerFrame.z + "), " +
            "FR (x, y, z): " +
            "( " + chestBeltFrontRightAttachmentPositionBeltTrackerFrame.x + ", " + chestBeltFrontRightAttachmentPositionBeltTrackerFrame.y + ", "
            + chestBeltFrontRightAttachmentPositionBeltTrackerFrame.z + ")."
            );

        // Set teh flag indicating the pelvic cable attachment positions have been computed
        readyToReturnChestBeltAttachmentPositionsInBeltFrameFlag = true;
    }

    // This is a key function for obtaining the structure matrix data. 
    // Computes the cable attachment points expressed in the shank belt's Vive tracker frame.
    // NOTE that we don't actually need the Vive tracker to be present, since we're just making assumptions about the tracker orientation.
    // We only need estimated shank circle size. The shank perimeter is modeled as a circle.
    // We assume that the shank belt tracker is mounted on the lateral shank, with it's LED downwards (downwards Gen 2.0, upwards Gen 3.0), w.r.t. the subject's orientation.
    // As a result, the tracker's LEFT-HANDED SOFTWARE FRAME has
    // LEFT SHANK: +x-axis is subject's backwards, +y-axis is subject's downwards, and +z-axis is pointed laterally.
    // RIGHT SHANK: +x-axis is subject's forwards, +y-axis is subject's downwards, and +z-axis is pointed laterally.
    // Note: this is the function that computes the coordinates in tracker frame. We will have another function that simply returns the stored values.
    public void ComputeLeftAndRightShankBeltsCableAttachmentsInViveTrackerFrame()
    {
        // Left shank
        // Attachment is along +x-axis (backwards) and along -z-axis (medial)
        float leftShankRadiusInMeters = circumferenceOfLeftShankInMeters / (2 * Mathf.PI); // get radius
        leftShankCableAttachmentPointInTrackerLeftHandedFrame =
            new Vector3(leftShankRadiusInMeters, 0.0f, -leftShankRadiusInMeters);
        
        // Right shank
        // Attachment is along -x-axis (backwards) and along -z-axis (medial)
        float rightShankRadiusInMeters = circumferenceOfRightShankInMeters / (2 * Mathf.PI); // get radius
        rightShankCableAttachmentPointInTrackerLeftHandedFrame =
            new Vector3(-rightShankRadiusInMeters, 0.0f, -rightShankRadiusInMeters);
        
        // Print if desired
        Debug.Log("Computed shank belt cable attachments in shank Vive tracker LEFT-HANDED frame as: Left shank (x,y,z): " +
                  "( " + leftShankCableAttachmentPointInTrackerLeftHandedFrame.x + ", " + leftShankCableAttachmentPointInTrackerLeftHandedFrame.y + ", "
                  + leftShankCableAttachmentPointInTrackerLeftHandedFrame.z + "), " + 
                  "Right shank (x, y, z): " +
                  "( " + rightShankCableAttachmentPointInTrackerLeftHandedFrame.x + ", " + rightShankCableAttachmentPointInTrackerLeftHandedFrame.y + ", "
                  + rightShankCableAttachmentPointInTrackerLeftHandedFrame.z + ")"
        );

        // Set teh flag indicating the pelvic cable attachment positions have been computed
        readyToReturnShankBeltAttachmentPositionsInBeltFrameFlag = true;
    }
    


    // A public function called by the main structure matrix builder script. 
    // Gets the pelvis pulley positions in Vive ref tracker frame for storage in the CSV.
    public (bool, Vector3[]) ComputePelvicBeltPulleyPositionsInViveTrackerFrame()
    {
        bool successfullyComputedPulleyPositionsFlag = false; // starts as false
        bool usingVive = structureMatrixSettingsScript.GetUsingViveFlag();
        bool usingVicon = structureMatrixSettingsScript.GetUsingViconFlag();

        // If we're using Vive for structure matrix computation
        if (usingVive)
        {
            // Store the hard-coded values in the proper order. 
            // These hard-coded values must be updated whenever the pulleys are moved by changing the user-input height of the pelvic pulleys.
            pelvisPulleyPositionsViveReferenceTrackerFrame = new Vector3[] { frontLeftUpperPulleyPositionInReferenceViveTrackerFrameInMeters,
                frontRightUpperPulleyPositionInReferenceViveTrackerFrameInMeters,
                backRightUpperPulleyPositionInReferenceViveTrackerFrameInMeters, 
                backLeftUpperPulleyPositionInReferenceViveTrackerFrameInMeters,
                frontLeftLowerPulleyPositionInReferenceViveTrackerFrameInMeters,
                frontRightLowerPulleyPositionInReferenceViveTrackerFrameInMeters,
                backRightLowerPulleyPositionInReferenceViveTrackerFrameInMeters,
                backLeftLowerPulleyPositionInReferenceViveTrackerFrameInMeters,
            };

            // Since we have computed the pulley positions in Vive ref frame, set the flag to true
            successfullyComputedPulleyPositionsFlag = true;
        }
        else
        // Else, we're only using Vicon, so throw an error. This function should only be called if Vive is being used. 
        {
            Debug.LogError("The function to compute pelvic pulley positions in Vive frame was called, but the structureMatrixSettingsScript says Vive " +
                "is not being used!");
        }

        // Return the flag indicating if we successfully found the pulley positions in Vive reference frame (true) or not (false)
        return (successfullyComputedPulleyPositionsFlag, pelvisPulleyPositionsViveReferenceTrackerFrame);
    }


    // A public function called by the main structure matrix builder script. 
    // Gets the chest pulley positions in Vive ref tracker frame for storage in the CSV.
    public (bool, Vector3[]) ComputeChestBeltPulleyPositionsInViveTrackerFrame()
    {
        bool successfullyComputedPulleyPositionsFlag = false; // starts as false
        bool usingVive = structureMatrixSettingsScript.GetUsingViveFlag();
        bool usingVicon = structureMatrixSettingsScript.GetUsingViconFlag();

        // If we're using Vive for structure matrix computation
        if (usingVive)
        {
            // Store the hard-coded values in the proper order. 
            // These hard-coded values must be updated whenever the pulleys are moved by changing the user-input height of the pelvic pulleys.
            chestPulleyPositionsViveReferenceTrackerFrame = new Vector3[] { frontLeftChestPulleyPositionInReferenceViveTrackerFrameInMeters,
                frontRightChestPulleyPositionInReferenceViveTrackerFrameInMeters,
                backRightChestPulleyPositionInReferenceViveTrackerFrameInMeters,
                backLeftChestPulleyPositionInReferenceViveTrackerFrameInMeters
            };

            // Since we have computed the pulley positions in Vive ref frame, set the flag to true
            successfullyComputedPulleyPositionsFlag = true;
        }
        else
        // Else, we're only using Vicon, so throw an error. This function should only be called if Vive is being used. 
        {
            Debug.LogError("The function to compute pelvic pulley positions in Vive frame was called, but the structureMatrixSettingsScript says Vive " +
                "is not being used!");
        }

        // Return the flag indicating if we successfully found the pulley positions in Vive reference frame (true) or not (false)
        return (successfullyComputedPulleyPositionsFlag, chestPulleyPositionsViveReferenceTrackerFrame);
    }


    public (bool, Vector3, Vector3) ComputeShankBeltPulleyPositionsInViveTrackerFrame()
    {
        bool successfullyComputedPulleyPositionsFlag = false; // starts as false
        bool usingVive = structureMatrixSettingsScript.GetUsingViveFlag();
        bool usingVicon = structureMatrixSettingsScript.GetUsingViconFlag();

        // If we're using Vive for structure matrix computation
        if (usingVive)
        {
            // Since we have computed the pulley positions in Vive ref frame, set the flag to true
            successfullyComputedPulleyPositionsFlag = true;
        }
        else
            // Else, we're only using Vicon, so throw an error. This function should only be called if Vive is being used. 
        {
            Debug.LogError("The function to compute shank pulley positions in Vive frame was called, but the structureMatrixSettingsScript says Vive " +
                           "is not being used!");
        }

        // Return the flag indicating if we successfully found the pulley positions in Vive reference frame (true) or not (false)
        return (successfullyComputedPulleyPositionsFlag, leftShankPulleyPositionInReferenceViveTrackerFrameInMeters, rightShankPulleyPositionInReferenceViveTrackerFrameInMeters);
    }



    // END: PUBLIC FUNCTIONS********************************************************************************************************



    // START: Pelvic belt attachment computation functions. Includes ellipse functions **********************************************************************************************

    private Vector3 ConvertVerticalOffsetInLabFrameToViveRefTrackerFrameOffset(float verticalOffsetInLabFrameInMeters)
    {
        // We use the 3rd column of the rotation matrix from Vicon frame to Vive reference frame,
        // as the third column corresponds to vertical offsets in the Vicon space. 
        // This essentially only allows for conversions of vertical offsets in the lab space to the Vive ref tracker frame. 
        Vector3 thirdColumnOfViveToViconRotationMatrix = new Vector3(0.0292f, -0.9980f, 0.0565f);

        // Multiply each element of the column by the offset to get the offset vector in Vive ref frame
        Vector3 offsetVectorInViveRefTrackerFrame = thirdColumnOfViveToViconRotationMatrix * verticalOffsetInLabFrameInMeters;

        // Return the offset vector in Vive ref frame
        return offsetVectorInViveRefTrackerFrame;

    }

    private List<Vector3> ComputeBeltAttachmentsInBeltTrackerFrame(float beltStartAngleEllipseFrame, float ellipseMediolateralRadius, 
        float ellipseAnteroposteriorRadius, List<float> distancesFromBeltTrackerToAttachments, bool[] attachmentsAreClockwiseFlags)
    {
        // We start by creating the list of angles we'll be probing in standard ellipse frame. 
        // The tracker is at (3/2) * pi. We want to integrate from there around the full ellipse, so we add 2*pi 
        // to get the final angle. 
        float startAngle = beltStartAngleEllipseFrame; // the angle to the belt tracker in standard ellipse frame. It is at the back of the ellipse.
        float endAngle = startAngle + 2.0f * Mathf.PI; // the end angle we'll integrate over, a full revolution from where the tracker is. 
        int numAnglesToProbe = 2000; // the number of steps we make when integrating around the ellipse.
        float stepSize = (endAngle - startAngle) / numAnglesToProbe;

        // Build the list of angles to explore
        List<float> anglesToExploreStandardEllipseFrame = new List<float>();
        for (int angleIndex = 0; angleIndex < numAnglesToProbe; angleIndex++)
        {
            // Angle this step
            anglesToExploreStandardEllipseFrame.Add(startAngle + angleIndex * stepSize);
        }

        // Now that we have our angles, pass these angles and the ellipse dimensions to the ellipse perimeter integrator. 
        List<float> perimeterAtGivenAngles = IntegrateEllipsePerimeterAndReportPerimeterAtGivenAngles(anglesToExploreStandardEllipseFrame,
            ellipseMediolateralRadius, ellipseAnteroposteriorRadius);

        Debug.Log("Angle/distance pairs at index: 10 is " + anglesToExploreStandardEllipseFrame[10] + ", " + perimeterAtGivenAngles[10]);

        // Get the angles to the 4 attachment points using their distance from the tracker. 
        // The angles should be in standard ellipse frame (measured CCW from righwards = +x-axis).
        // Note: left attachments are CCW from tracker, whereas right attachments are CW. 
        // Get the attachment angles
        List<float> beltAttachmentAnglesInStandardEllipseFrame = GetBeltAttachmentAnglesInBeltStandardEllipseFrame(perimeterAtGivenAngles, anglesToExploreStandardEllipseFrame,
            distancesFromBeltTrackerToAttachments, attachmentsAreClockwiseFlags, startAngle);

        Debug.Log("Pelvic belt attachment angles in standard ellipse frame: BL: " + beltAttachmentAnglesInStandardEllipseFrame[0] +
            ", FL: " + beltAttachmentAnglesInStandardEllipseFrame[1] +
            ", BR: " + beltAttachmentAnglesInStandardEllipseFrame[2] +
            ", FR: " + beltAttachmentAnglesInStandardEllipseFrame[3]);

        // Get the attachment positions in standard ellipse frame.
        // This is simply using the angle and axis dimensions in the polar-coordinate form of the ellipse.
        List <Vector3> beltAttachmentPositionsInStandardEllipseFrame =
            GetBeltAttachmentPositionsInBeltStandardEllipseFrame(beltAttachmentAnglesInStandardEllipseFrame,
            ellipseMediolateralRadius, ellipseAnteroposteriorRadius);

        // Construct the transformation matrix (a Matrix4x4 type) from standard ellipse frame to belt frame.
        // Note, this is general for all belts (or, at least, the chest and pelvic belts).
        Matrix4x4 transformationEllipseToViveTrackerFrame = 
            BuildTransformationMatrixFromEllipseFrameToViveTrackerFrameGivenEllipseSize(ellipseMediolateralRadius,
            ellipseAnteroposteriorRadius);

        // Compute and store all of the attachment points in belt tracker frame. 
        // Order of the attachments was defined just above as: BL, FL, BR, FR
        List<Vector3> cableAttachmentsInBeltViveTrackerFrame = new List<Vector3>();
        cableAttachmentsInBeltViveTrackerFrame.Add(
            transformationEllipseToViveTrackerFrame.MultiplyPoint3x4(beltAttachmentPositionsInStandardEllipseFrame[0]));
        cableAttachmentsInBeltViveTrackerFrame.Add(
            transformationEllipseToViveTrackerFrame.MultiplyPoint3x4(beltAttachmentPositionsInStandardEllipseFrame[1]));
        cableAttachmentsInBeltViveTrackerFrame.Add(
            transformationEllipseToViveTrackerFrame.MultiplyPoint3x4(beltAttachmentPositionsInStandardEllipseFrame[2]));
        cableAttachmentsInBeltViveTrackerFrame.Add(
            transformationEllipseToViveTrackerFrame.MultiplyPoint3x4(beltAttachmentPositionsInStandardEllipseFrame[3]));

        // Return the cable attachment positions in belt Vive tracker frame 
        return cableAttachmentsInBeltViveTrackerFrame;  
    }


    private List<float> IntegrateEllipsePerimeterAndReportPerimeterAtGivenAngles(List<float> anglesToExploreStandardEllipseFrame,
            float ellipseXAxisRadius, float ellipseYAxisRadius)
    {
        Debug.Log("ellipse x-axis radius: " + ellipseXAxisRadius);
        Debug.Log("ellipse y-axis radius: " + ellipseYAxisRadius);

        // Create a list to store the integrated perimeter at each angle
        List<float> integratedPerimeterAtEachAngle = new List<float>();

        // The first integrated perimeter value is zero, since we start at the tracker and there hasn't been any integrated perimeter yet. 
        integratedPerimeterAtEachAngle.Add(0.0f);

        // For each adjacent pair of angles i and i-1 (so, starting at Unity index 1, not 0)
        int testIndex = 10;
        for (int anglePairIndex = 1; anglePairIndex < anglesToExploreStandardEllipseFrame.Count; anglePairIndex++)
        {
            // Get the two angles of interest
            float upperAngle = anglesToExploreStandardEllipseFrame[anglePairIndex];
            float lowerAngle = anglesToExploreStandardEllipseFrame[anglePairIndex-1];
            float changeInAngle = upperAngle - lowerAngle;

            // Get the radius at the upper and lower angles using polar coordinates
            float upperAngleRadius = GetEllipseRadiusGivenAngleInStandardEllipseFrame(upperAngle, ellipseXAxisRadius, ellipseYAxisRadius);
            float lowerAngleRadius = GetEllipseRadiusGivenAngleInStandardEllipseFrame(lowerAngle, ellipseXAxisRadius, ellipseYAxisRadius);

            if(anglePairIndex == testIndex)
            {
                Debug.Log("Ellipse radius at index " + anglePairIndex + " is " + upperAngleRadius);
            }

            // Get the ellipse slope at the upper and lower angles
            float upperAngleSlope = GetEllipseSlopeGivenAngleInStandardEllipseFrame(upperAngle, ellipseXAxisRadius, ellipseYAxisRadius);
            float lowerAngleSlope = GetEllipseSlopeGivenAngleInStandardEllipseFrame(lowerAngle, ellipseXAxisRadius, ellipseYAxisRadius);

            if (anglePairIndex == testIndex)
            {
                Debug.Log("Ellipse slope at index " + anglePairIndex + " is " + upperAngleSlope);
            }

            // Compute the integrands at the upper and lower angles
            float upperAngleIntegrand = Mathf.Sqrt(Mathf.Pow(upperAngleRadius, 2.0f) + Mathf.Pow(upperAngleSlope, 2.0f));
            float lowerAngleIntegrand = Mathf.Sqrt(Mathf.Pow(lowerAngleRadius, 2.0f) + Mathf.Pow(lowerAngleSlope, 2.0f));

            // The integrated perimeter this step is computed from the trapezoidal rule
            float integratedPerimeter = ((upperAngleIntegrand + lowerAngleIntegrand) / 2.0f) * changeInAngle;

            // Add the integrated perimeter to the running total (in the previous index) integrated perimeter/arc length
            integratedPerimeterAtEachAngle.Add(integratedPerimeterAtEachAngle[anglePairIndex-1] + integratedPerimeter);
        }

        // Return the list that has total integrated perimeter at each proble angle
        return integratedPerimeterAtEachAngle;
    }


    private float GetEllipseRadiusGivenAngleInStandardEllipseFrame(float angleInStandardEllipseFrameInRads,
        float ellipseXAxisRadius, float ellipseYAxisRadius)
    {
        // Compute radius using the polar coordinates description of an ellipse
        float radius = (ellipseXAxisRadius * ellipseYAxisRadius) / Mathf.Sqrt(Mathf.Pow(ellipseXAxisRadius * Mathf.Sin(angleInStandardEllipseFrameInRads), 2.0f)
            + Mathf.Pow(ellipseYAxisRadius * Mathf.Cos(angleInStandardEllipseFrameInRads), 2.0f));

        // Return the radius
        return radius;
    }

    private float GetEllipseSlopeGivenAngleInStandardEllipseFrame(float angleInStandardEllipseFrameInRads,
    float ellipseXAxisRadius, float ellipseYAxisRadius)
    {
        // Compute slope with the derivative of the polar coordinates description of an ellipse
        float dr_dtheta = -(ellipseXAxisRadius * ellipseYAxisRadius) * (-2.0f * Mathf.Cos(angleInStandardEllipseFrameInRads) *
            Mathf.Sin(angleInStandardEllipseFrameInRads) * Mathf.Pow(ellipseYAxisRadius, 2.0f) + 2.0f * Mathf.Cos(angleInStandardEllipseFrameInRads) *
            Mathf.Sin(angleInStandardEllipseFrameInRads) * Mathf.Pow(ellipseXAxisRadius, 2.0f))       
            / ( 2.0f * Mathf.Pow(Mathf.Pow(ellipseXAxisRadius * Mathf.Sin(angleInStandardEllipseFrameInRads), 2.0f)
            + Mathf.Pow(ellipseYAxisRadius * Mathf.Cos(angleInStandardEllipseFrameInRads), 2.0f), (3.0f / 2.0f)));

        float slopeNumerator = -(ellipseXAxisRadius * ellipseYAxisRadius) * (-2.0f * Mathf.Cos(angleInStandardEllipseFrameInRads) *
            Mathf.Sin(angleInStandardEllipseFrameInRads) * Mathf.Pow(ellipseYAxisRadius, 2.0f) + 2.0f * Mathf.Cos(angleInStandardEllipseFrameInRads) *
            Mathf.Sin(angleInStandardEllipseFrameInRads) * Mathf.Pow(ellipseXAxisRadius, 2.0f));
        float slopeDenominator = (2.0f * Mathf.Pow(Mathf.Pow(ellipseXAxisRadius * Mathf.Sin(angleInStandardEllipseFrameInRads), 2.0f)
            + Mathf.Pow(ellipseYAxisRadius * Mathf.Cos(angleInStandardEllipseFrameInRads), 2.0f), (3.0f / 2.0f)));

        Debug.Log("slope Numerator : " + slopeNumerator);
        Debug.Log("slope Denominator : " + slopeDenominator);


        // Return the polar slope
        return dr_dtheta;
    }


    private List<float> GetBeltAttachmentAnglesInBeltStandardEllipseFrame(List<float> perimeterAtGivenAngles, List<float> anglesToExploreStandardEllipseFrame,
            List<float> distancesFromBeltTrackerToAttachments, bool[] attachmentsAreClockwiseFlags, float angleToTrackerInStandardEllipseFrameRads)
    {
        // For each belt attachment point
        List<float> angleToAttachmentsInStandardEllipseFrame = new List<float>();
        for (int beltAttachmentIndex = 0; beltAttachmentIndex < distancesFromBeltTrackerToAttachments.Count; beltAttachmentIndex++)
        {
            // We find the index of the integrated perimeter that is closest to the measured distance from tracker to the attachment point
            int nearestIndex = GetIndexOfNearestValueInListOfFloats(perimeterAtGivenAngles, distancesFromBeltTrackerToAttachments[beltAttachmentIndex]);

            // Get the angle to tanglesToExploreStandardEllipseFramehe attachment. In this step, we assume the attachment is CCW from the tracker. 
            // This is just the angle in the corresponding index.
            float angleToAttachment = anglesToExploreStandardEllipseFrame[nearestIndex];

            // If the attachment is actually CW from the tracker, then "mirror" the angle to the attachment about the tracker angle. 
            // This is just trackerAngle - (angleToAttachment - trackerAngle)
            if(attachmentsAreClockwiseFlags[beltAttachmentIndex] == true)
            {
                angleToAttachment = angleToTrackerInStandardEllipseFrameRads - (angleToAttachment - angleToTrackerInStandardEllipseFrameRads);
            }

            // Store the attachment angle
            angleToAttachmentsInStandardEllipseFrame.Add(angleToAttachment);
        }

        // Return the attachment angles
        return angleToAttachmentsInStandardEllipseFrame;
    }


    private List<Vector3> GetBeltAttachmentPositionsInBeltStandardEllipseFrame(List<float> beltAttachmentAnglesInStandardEllipseFrame,
            float beltMediolateralRadius, float beltAnteroposteriorRadius)
    {
        // For each belt attachment 
        List<Vector3> beltCableAttachmentsInStandardEllipseFrame = new List<Vector3>();
        for (int beltAttachmentIndex = 0; beltAttachmentIndex < beltAttachmentAnglesInStandardEllipseFrame.Count; beltAttachmentIndex++)
        {
            // Angle to current belt attachment in ellipse standard frame 
            float angleToAttachment = beltAttachmentAnglesInStandardEllipseFrame[beltAttachmentIndex];
            
            // Get the ellipse radius at the given angle
            float ellipseRadius = GetEllipseRadiusGivenAngleInStandardEllipseFrame(beltAttachmentAnglesInStandardEllipseFrame[beltAttachmentIndex], beltMediolateralRadius,
                beltAnteroposteriorRadius);

            // Compute the (x,y) position on the ellipse in standard ellipse frame. Note that the z-axis component is 0.
            Vector3 cableAttachmentInEllipseFrame = new Vector3(
                ellipseRadius * Mathf.Cos(angleToAttachment), ellipseRadius * Mathf.Sin(angleToAttachment), 0.0f);

            // Store the position
            beltCableAttachmentsInStandardEllipseFrame.Add(cableAttachmentInEllipseFrame);
        }

        // Return the belt attachment positions in ellipse frame
        return beltCableAttachmentsInStandardEllipseFrame;
    }



    private Matrix4x4 BuildTransformationMatrixFromEllipseFrameToViveTrackerFrameGivenEllipseSize(
        float beltMediolateralRadius, float beltAnteroposteriorRadius)
    {
        // The rotation matrix is straightforward. It transforms from ellipse frame to LEFT-HANDED SOFTWARE Vive tracker frame.
        // X-axis: the tracker left-handed software frame x-axis is right, the ellipse x-axis is right, so first column is [1, 0, 0].
        // Y-axis: the tracker left-handed software frame y-axis is downwards and z-axis is backwards, the ellipse y-axis is forwards, so second column is [0, 0, -1].
        // Z-axis: the tracker left-handed software frame y-axis is downwards, the ellipse z-axis is upwards, so third column is [0, -1, 0].
        Matrix4x4 transformationEllipseToBeltFrame = new Matrix4x4();
        transformationEllipseToBeltFrame.SetColumn(0, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        transformationEllipseToBeltFrame.SetColumn(1, new Vector4(0.0f, 0.0f, -1.0f, 0.0f));
        transformationEllipseToBeltFrame.SetColumn(2, new Vector4(0.0f, -1.0f, 0.0f, 0.0f));

        // The position vector is the position of the ellipse frame origin in tracker frame. 
        // The ellipse origin is in the center of the ellipse. 
        // The ellipse origin is at 1 minor axis (y-axis) radius along the -y-axis in the tracker frame. 
        // So the vector is [0, -minorAxis, 0]
        transformationEllipseToBeltFrame.SetColumn(3, new Vector4(0.0f, 0.0f, -beltAnteroposteriorRadius - 
            subjectInfoScript.GetOffsetForThreeDPrintedViveTrackerMount(), 1.0f));

        // Return the assembled transformation matrix
        return transformationEllipseToBeltFrame;
    }


    private int GetIndexOfNearestValueInListOfFloats(List<float> listOfFloats, float searchValue)
    {
        // For each float in the list
        float minimumAbsoluteDiff = Mathf.Infinity;
        int minimumIndex = -1;
        // For each float in the list
        for (int listIndex = 0; listIndex < listOfFloats.Count; listIndex++)
        {
            // Get the current difference from the desired value
            float currentDiff = Mathf.Abs(listOfFloats[listIndex] - searchValue);
            // If the absolute value of the difference is the lowest seen so far
            if(currentDiff < minimumAbsoluteDiff)
            {
                // Store the new minimum diff and update the closest element index
                minimumAbsoluteDiff = currentDiff;
                minimumIndex = listIndex;
            }
        }

        return minimumIndex;
    }



}

public enum PelvisBeltSizeSelectEnum
{
    XS, S, M, L, XL
}

public enum ChestBeltSizeSelectEnum
{
    S, L
}
