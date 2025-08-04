using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SubjectInfoStorageScript : MonoBehaviour
{
    // START: Initialize custom classes to organize subject-related info into categories.*******************************
    // Subject basic info
    public basicSubjectPropertiesClass basicSubjectProperties;

    // Transcutaneous spinal stim info
    public transcutaneousSpinalCordStimClass spinalCordStimProperties;

    // Reaching task needed info 
    public subjectReachingPropertiesClass reachingTaskRelatedProperties;

    // Info needed to construct kinematic models of stance, used in RobUST control. 
    public kinematicModelPropertiesClass kinematicModelProperties;

    // Info related to Vive tracker hardware - custom or inherent to the devices 
    public viveTrackerHardwarePropertiesClass viveTrackerRelatedProperties;

    // Info needed to run RobUST with shank cables
    public shankCablePropertiesClass shankBeltProperties;
    // END: Initialize custom classes to organize subject-related info into categories.*******************************


    // START: Declare custom classes to organize subject-related info into categories.*******************************
    // Create a class for basic subject info, used in data storage and for a variety of other uses.
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class basicSubjectPropertiesClass
    {
        // Public properties
        // Basics about the subject
        public string subjectNumber;
        public string subjectGender;
        public float subjectMassInKg;
        public float subjectHeightInMeters;
    }


    // Create a class for TSCS status.
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class transcutaneousSpinalCordStimClass
    {
        // Stimulation status
        public bool stimulationStatus;

        // Constants for the stim status
        public static string stimulationOnStatusName = "Stim_On";
        public static string stimulationOffStatusName = "Stim_Off";
    }

    // Create a class to store reaching-specific quantities (like upper limb length)
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class subjectReachingPropertiesClass
    {
        // Public properties
        // Arm lengths
        public float upperArmLengthInMeters;
        public float forearmLengthInMeters;
        // Placing reaching targets
        public float verticalDistanceShouldersToEyeLevelInMeters; // [m]
        public float vertDistanceChestToShouldersInMeters; // used if we do NOT have Vicon markers as a reference...maybe!
    }

    // Create a class to store kinematic model quantities, such as pelvis and chest ellipse model major/minor axes lengths. 
    // Used for the 5R model currently, but likely will be used for 4R model as well.
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class kinematicModelPropertiesClass
    {
        // Pelvic ellipse modeling for RobUST control with Vive trackers only (no Vicon)
        public float pelvicEllipseMediolateralLengthMeters;
        public float pelvicEllipseAnteroposteriorLengthMeters;

        // Chest ellipse modeling for RobUST control with Vive trackers only (no Vicon)
        public float chestEllipseMediolateralLengthMeters;
        public float chestEllipseAnteroposteriorLengthMeters;

        // Distance ankle to knee. Needed to locate knee joint center in 5R kinematic model.
        public float distanceAnkleToKneeInMeters;

        // Distance between the two ASIS landmarks, measured straight (not curved) in meters.
        // Used to compute the hip joint centers with Vive tracker data (has this been checked?)
        public float interAsisWidthInMeters;

        // Distance from the pelvic tracker to the mid-shoulder point. 
        // Used to compute the torso length in the 4R/5R kinematic models.
        public float distancePelvicBeltTrackerToShouldersInMeters;

        // Distance from the pelvic tracker to the chest tracker. 
        // Used to render a more reliable theta3 angle on-screen for the 5R model, since the 
        // pelvic belt tends to slip up when doing squatting.
        public float distancePelvicBeltTrackerToChestBeltTrackerInMeters;
    }

    // Create a class to store Vive tracker mounting hardware data.
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class viveTrackerHardwarePropertiesClass
    {
        public float threeDPrintingOffsetInMeters;
    }


    // Create a class to store kinematic model quantities, such as pelvis and chest ellipse model major/minor axes lengths. 
    // Used for the 5R model currently, but likely will be used for 4R model as well.
    // Mark the class as serializable so it can appear in the inspector window.
    [System.Serializable]
    public class shankCablePropertiesClass
    {
        // Shank diameter
        public float rightShankPerimeterInMeters; // used to get shank belt center and attachment points in shank Vive tracker frame
        public float leftShankPerimeterInMeters;
    }
    // END: Declare custom classes to organize subject-related info into categories.*******************************



    // Start is called before the first frame update
    void Start()
    {
        
    }


    // Update is called once per frame
    void Update()
    {
        (float pelvisMlInMeters, float pelvisApInMeters) = GetPelvisEllipseMediolateralAndAnteroposteriorLengths();
        if (GetInterAsisWidthInMeters() >= pelvisMlInMeters)
        {
            Debug.LogError("InterASIS width is greater than pelvis width!!!! Impossible!");
        }

    }


    // START: Getters for basic subject info*******************************
    public string getSubjectNumberString()
    {
        return basicSubjectProperties.subjectNumber;
    }


    public string getSubjectGenderString()
    {
        return basicSubjectProperties.subjectGender;
    }

    public float getSubjectMassInKilograms()
    {
        return basicSubjectProperties.subjectMassInKg;
    }


    public float getSubjectHeightInMeters()
    {
        return basicSubjectProperties.subjectHeightInMeters;
    }

    public string getSubjectSummaryStringForFileNaming()
    {
        string delimiter = "_";
        return basicSubjectProperties.subjectNumber + delimiter + basicSubjectProperties.subjectGender;
    }
    // END: Getters for basic subject info*******************************



    // START: Getters for spinal stim-related info*******************************
    public string getStimulationStatusStringForFileNaming()
    {
        string stimStatusString; 
        if(spinalCordStimProperties.stimulationStatus == false) // if stimulation is off
        {
            // Return the string indicating stim is off
            stimStatusString = transcutaneousSpinalCordStimClass.stimulationOffStatusName;
        }
        else // else if stimulation is on
        {
            // Return the string indicating stim is on
            stimStatusString = transcutaneousSpinalCordStimClass.stimulationOnStatusName;
        }

        return stimStatusString;
    }
    // END: Getters for spinal stim-related info*******************************



    // START: Getters for reaching task-related info*******************************
    public float getUpperArmLengthInMeters()
    {
        return reachingTaskRelatedProperties.upperArmLengthInMeters;
    }

    public float getForearmLengthInMeters()
    {
        return reachingTaskRelatedProperties.forearmLengthInMeters;
    }

    // The distance from the chest tracker to the shoulders is a stand-in for distance to the shoulders, and 
    // can be measured more consistently.
    public float getVerticalDistanceShouldersToEyeLevel()
    {
        return reachingTaskRelatedProperties.verticalDistanceShouldersToEyeLevelInMeters;
    }

    // The distance from the chest tracker to the shoulders is a stand-in for distance to the shoulders, and 
    // can be measured more consistently.
    public float getVerticalDistanceChestToShouldersInMeters()
    {
        return reachingTaskRelatedProperties.vertDistanceChestToShouldersInMeters;
    }
    // END: Getters for reaching task-related info*******************************



    // START: Getters for kinematic model-related info*******************************
    public (float, float) GetPelvisEllipseMediolateralAndAnteroposteriorLengths()
    {
        return (kinematicModelProperties.pelvicEllipseMediolateralLengthMeters,
            kinematicModelProperties.pelvicEllipseAnteroposteriorLengthMeters);
    }

    public (float, float) GetChestEllipseMediolateralAndAnteroposteriorLengths()
    {
        return (kinematicModelProperties.chestEllipseMediolateralLengthMeters,
            kinematicModelProperties.chestEllipseAnteroposteriorLengthMeters);
    }

    public float GetDistanceAnkleToKneeInMeters()
    {
        return kinematicModelProperties.distanceAnkleToKneeInMeters;
    }

    public float GetInterAsisWidthInMeters()
    {
        return kinematicModelProperties.interAsisWidthInMeters;
    }

    public float GetLengthFromPelvisViveTrackerToShoulderInMeters()
    {
        return kinematicModelProperties.distancePelvicBeltTrackerToShouldersInMeters;
    }

    // Get the baseline distance from pelvic belt Vive tracker to chest belt Vive tracker. 
    public float GetDistanceFromPelvicBeltTrackerToChestBeltTracker()
    {
        return kinematicModelProperties.distancePelvicBeltTrackerToChestBeltTrackerInMeters;
    }
    // END: Getters for kinematic model-related info*******************************



    // START: Getters for Vive tracker hardware-related info*******************************
    public float GetOffsetForThreeDPrintedViveTrackerMount()
    {
        return viveTrackerRelatedProperties.threeDPrintingOffsetInMeters;
    }
    // END: Getters for Vive tracker hardware-related info*******************************



    // START: Getters for shank belt-related info*******************************
    public float GetRightShankPerimeterInMeters()
    {
        return shankBeltProperties.rightShankPerimeterInMeters;
    }
    
    public float GetLeftShankPerimeterInMeters()
    {
        return shankBeltProperties.leftShankPerimeterInMeters;
    }


    // END: Getters for shank belt-related info*******************************




}
