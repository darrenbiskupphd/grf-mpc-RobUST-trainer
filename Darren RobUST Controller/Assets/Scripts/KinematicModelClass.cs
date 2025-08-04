using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Collections.Generic;
using System.IO;



// This interface is used by the classes 4RModelOfStance and 5RModelOfStance
public interface KinematicModelOfStance
{
    // Public Methods
    public double[] GetJointVariableValuesFromMarkerDataInverseKinematics();
    public double[] GetJointVariableValuesFromInverseKinematicsVirtualPelvicTracker(); // Probably only needed for squatting task.
    public double[] GetGravityTorqueAtEachModelJoint();
    public Matrix<double> GetChestForceVelocityJacobianTranspose();
    public Matrix<double> GetPelvisForceVelocityJacobianTranspose();
    public Matrix<double> GetKneeForceVelocityJacobianTranspose();
    public Matrix4x4 GetTransformFromViconFrameToFrameZeroOfStanceModel();
    public void UpdateModelVisualization();

    // Unit test function - implemented in 5R only
    public void TestModelForwardAndInverseKinematicsAndGravityTorques(TextAsset csvAsset);
}

// A small class to validate a model's forward kinematics (FK), inverse kinematics (IK), and computed gravity torques. 
public class FKIKGravityTestCase5RModel
{
    public float[] jointAngles = new float[5];           // theta1–5
    public Vector3 kneePos;                              // x, y, z
    public Vector3 pelvisPos;
    public Vector3 chestPos;
    public float[] gravityTorques = new float[5];        // gravTorque1–5
    public float[] substitutedValues = new float[14];    // a2, a3, a5, lc1–lc5, m1–m5, g
    public Matrix4x4 rotationMatrixFrame2ToZero;
    public Matrix4x4 rotationMatrixFrame3ToZero;
}


// Implement the 5R model class as an implementation of the stance model interface
public class FiveRevoluteModelWithKnees : KinematicModelOfStance
{
    //local class variables
    private ManageCenterOfMassScript centerOfMassManagerScript;
    private float subjectMassInKg;

    // The Vive data manager
    private ViveTrackerDataManager viveTrackerDataManagerScript;

    float thighFractionOfTotalBodyMass;
    float shankFractionOfTotalBodyMass;
    float trunkFractionOfTotalBodyMass;

    double massOfShank;
    double massOfThigh;
    double massOfTrunk;
    float zeroMass = 0.0f;

    double lengthAnkleToKnee;
    double lengthKneeToPelvis;
    double lengthPelvisToChest;

    float lengthAnkleToShankCable = 1.0f;

    double g = 9.807f; //in m/s^2

    double lcShank;
    double lcThigh;
    double lcTrunk;

    public double theta1;
    public double theta2;
    public double theta3;
    public double theta4;
    public double theta5;

    // The visualization List<LineRenderer> to draw the model in real time
    private List<LineRenderer> stanceModelLineRendererList = new List<LineRenderer>();
    private Material[] stanceModelRendererLineMaterial;


    // The selected data source specified in the Constructor: Vive, Vicon, or both
    private ModelDataSourceSelector dataSourceSelector;

    // An instance variable storing the current IK and gravity torques test case (if we're validating our model at startup with a "unit test")
    private FKIKGravityTestCase5RModel currentTestCase;

    // Constructor
    public FiveRevoluteModelWithKnees(float subjectMassInKgLocal, ManageCenterOfMassScript centerOfMassManagerScriptLocal,
        ViveTrackerDataManager viveTrackerDataManagerScriptLocal, ModelDataSourceSelector dataSourceSelectorInput, 
        Material[] lineRendererMaterial)
    {
        subjectMassInKg = subjectMassInKgLocal;

        // Store a reference to the Vicon marker data manager
        centerOfMassManagerScript = centerOfMassManagerScriptLocal;

        // Store a reference to the Vive tracker data manager
        viveTrackerDataManagerScript = viveTrackerDataManagerScriptLocal;

        // Store data source selector locally
        dataSourceSelector = dataSourceSelectorInput;

        // Store the line renderer material 
        stanceModelRendererLineMaterial = lineRendererMaterial;

        // Fill the line renderer list for visualization with 3 line renderers, since 
        // the 5R model has 3 links
        int numLinks = 3;
        for(int linkIndex = 0; linkIndex < numLinks; linkIndex++)
        {
            Material currentLineRendererMaterial;
            // If the array of materials is of the right/expected size for the number of links
            if(linkIndex < stanceModelRendererLineMaterial.Length)
            {
                // Set the link material equal to the material with the corresponding index
                currentLineRendererMaterial = stanceModelRendererLineMaterial[linkIndex];
            }
            // else if there are fewer materials than links
            else
            {
                // Set the link material equal to the last material in the list
                currentLineRendererMaterial = stanceModelRendererLineMaterial[stanceModelRendererLineMaterial.Length-1];
            }
            CreateLineRenderers(stanceModelLineRendererList, currentLineRendererMaterial, linkIndex);
        }

        // Compute subject specific measurements
        // Lengths must be computed from Vicon or Vive data. 
        // Segment fractional masses are from the literature (de Leva)
        // If we're using only Vicon
        if (dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {
            // Call functions in the Vicon marker manager ("COM manager") to compute needed values
            (trunkFractionOfTotalBodyMass, lengthPelvisToChest, lcTrunk) = centerOfMassManagerScript.GetSubjectSpecificSegmentMetrics("Trunk");
            (thighFractionOfTotalBodyMass, lengthKneeToPelvis, lcThigh) = centerOfMassManagerScript.GetSubjectSpecificSegmentMetrics("Thigh");
            (shankFractionOfTotalBodyMass, lengthAnkleToKnee, lcShank) = centerOfMassManagerScript.GetSubjectSpecificSegmentMetrics("Shank");

            // Get the length from the ankles to the shank belt using marker data
            Vector3 middleOfTwoShankBeltsPosViconFrame = centerOfMassManagerScript.GetCenterOfShankBeltPositionInViconFrame();
            Vector3 middleOfAnkleJointCentersViconFrame = centerOfMassManagerScript.GetAnkleJointCenterPositionViconFrame();
            Vector3 ankleToShankBeltsMidpointVector = middleOfTwoShankBeltsPosViconFrame - middleOfAnkleJointCentersViconFrame;
            lengthAnkleToShankCable = ankleToShankBeltsMidpointVector.magnitude / 1000.0f; // convert the Vicon frame units of mm to meters.

            // Compute the subject-specific segment masses
            massOfShank = shankFractionOfTotalBodyMass * subjectMassInKg;
            massOfThigh = thighFractionOfTotalBodyMass * subjectMassInKg;
            massOfTrunk = trunkFractionOfTotalBodyMass * subjectMassInKg;

            // Print the model link parameters
            Debug.Log("Trunk link parameters for stance model: (mass fraction, lengthPelvToChest, lc): (" + trunkFractionOfTotalBodyMass + ", "
                + lengthPelvisToChest + ", " + lcTrunk + ")");
            Debug.Log("Thigh link parameters for stance model: (mass fraction, lengthKneeToPelv, lc): (" + thighFractionOfTotalBodyMass + ", "
                + lengthKneeToPelvis + ", " + lcThigh + ")");
            Debug.Log("Shank link parameters for stance model: (mass fraction, lengthAnkleToKnee, lc): (" + shankFractionOfTotalBodyMass + ", "
                + lengthAnkleToKnee + ", " + lcShank + ")");
        }

        // Else if we're using only Vive
        else if(dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        {

            if (viveTrackerDataManagerScriptLocal== null)
            {
                Debug.Log("Vive tracker data manager is NULL");
            }
            
            // Call Vive tracker data manager to compute needed values
            (trunkFractionOfTotalBodyMass, lengthPelvisToChest, lcTrunk) = viveTrackerDataManagerScriptLocal.GetSubjectSpecificSegmentMetrics("Trunk");
            (thighFractionOfTotalBodyMass, lengthKneeToPelvis, lcThigh) = viveTrackerDataManagerScriptLocal.GetSubjectSpecificSegmentMetrics("Thigh");
            (shankFractionOfTotalBodyMass, lengthAnkleToKnee, lcShank) = viveTrackerDataManagerScriptLocal.GetSubjectSpecificSegmentMetrics("Shank");
            
            
            Debug.Log("trunkFractionOfTotalBodyMass:" + trunkFractionOfTotalBodyMass);
            Debug.Log("thighFractionOfTotalBodyMass:" + thighFractionOfTotalBodyMass);
            Debug.Log("shankFractionOfTotalBodyMass:" + shankFractionOfTotalBodyMass);
            
            Debug.Log("lengthPelvisToChest:" + lengthPelvisToChest);
            Debug.Log("lengthKneeToPelvis:" + lengthKneeToPelvis);
            // lengthKneePelvis is nan
            Debug.Log("lengthAnkleToKnee:" + lengthAnkleToKnee);
            // lengthAnkleToKnee is nan, lcShank is nan
            
            Debug.Log("lcTrunk:" + lcTrunk);
            Debug.Log("lcThigh:" + lcThigh);
            Debug.Log("lcShank:" + lcShank);
            
            
            // Get the distance from ankle to shank cable attachment
            (_, _, lengthAnkleToShankCable) = viveTrackerDataManagerScript.GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();

            // Compute the subject-specific segment masses
            massOfShank = shankFractionOfTotalBodyMass * subjectMassInKg;
            massOfThigh = thighFractionOfTotalBodyMass * subjectMassInKg;
            massOfTrunk = trunkFractionOfTotalBodyMass * subjectMassInKg;

            // Print the model link parameters
            Debug.Log("Trunk link parameters for stance model: (mass fraction, lengthPelvToChest, lc): (" + trunkFractionOfTotalBodyMass + ", "
                + lengthPelvisToChest + ", " + lcTrunk + ")");
            Debug.Log("Thigh link parameters for stance model: (mass fraction, lengthKneeToPelv, lc): (" + thighFractionOfTotalBodyMass + ", "
                + lengthKneeToPelvis + ", " + lcThigh + ")");
            Debug.Log("Shank link parameters for stance model: (mass fraction, lengthAnkleToKnee, lc): (" + shankFractionOfTotalBodyMass + ", "
                + lengthAnkleToKnee + ", " + lcShank + ")");
        }

        // Else if we're using both Vive and Vicon
        else if(dataSourceSelector == ModelDataSourceSelector.ViconAndVive)
        {
            // First, see if the Vicon data manager can supply the needed values

            // If not, call the Vive tracker manager 

            // If we failed to get needed data
                // Throw an error
        } else if (dataSourceSelector == ModelDataSourceSelector.DummyInputs)
        {
            // Do nothing in the constructor, as everything will be initialized when the test function is called.
        }
    }


    // 
    public void TestModelForwardAndInverseKinematicsAndGravityTorques(TextAsset csvAsset)
    {
        // Load the test cases for the 5R model
        List<FKIKGravityTestCase5RModel> tests = LoadTestCases(csvAsset);

        // Assign the robot parameters (like link length, link masses, lc values, etc.) from file
        AssignRobotParametersFromTestCase(tests[0]);

        // Call the function that runs the validation tests and reports how many cases successfully matched MATLAB-exported values.
        RunTestsOfModelForwardAndInverseKinematicsAndGravityTorques(tests);

    }

    void AssignRobotParametersFromTestCase(FKIKGravityTestCase5RModel testCase)
    {
        float[] vals = testCase.substitutedValues;

        // Link lengths
        lengthAnkleToKnee = vals[0]; // a2
        lengthKneeToPelvis = vals[1]; // a3
        lengthPelvisToChest = vals[2]; // a5

        // Lengths to mass centers (skipping unused lc1, lc4)
        lcShank = vals[4]; // lc2
        lcThigh = vals[5]; // lc3
        lcTrunk = vals[7]; // lc5

        // Set masses from the test case (skipping unused m1, m4)
        massOfShank = vals[9];
        massOfThigh = vals[10];
        massOfTrunk = vals[12];

        // Overwrite gravity with the test case value
        g = vals[13];
    }


    // 
    public List<FKIKGravityTestCase5RModel> LoadTestCases(TextAsset csvAsset)
    {
        List<FKIKGravityTestCase5RModel> testCases = new List<FKIKGravityTestCase5RModel>();
        using (StringReader reader = new StringReader(csvAsset.text))
        {
            string line;
            bool isHeader = true;

            while ((line = reader.ReadLine()) != null)
            {
                if (isHeader) { isHeader = false; continue; }
                var cols = line.Split(',');

                FKIKGravityTestCase5RModel testCase = new FKIKGravityTestCase5RModel();

                // 1–5: joint angles
                for (int i = 0; i < 5; i++)
                    testCase.jointAngles[i] = float.Parse(cols[i], CultureInfo.InvariantCulture);

                // 6–8: knee pos
                testCase.kneePos = new Vector3(
                    float.Parse(cols[5], CultureInfo.InvariantCulture),
                    float.Parse(cols[6], CultureInfo.InvariantCulture),
                    float.Parse(cols[7], CultureInfo.InvariantCulture));

                // 9–11: pelvis pos
                testCase.pelvisPos = new Vector3(
                    float.Parse(cols[8], CultureInfo.InvariantCulture),
                    float.Parse(cols[9], CultureInfo.InvariantCulture),
                    float.Parse(cols[10], CultureInfo.InvariantCulture));

                // 12–14: chest pos
                testCase.chestPos = new Vector3(
                    float.Parse(cols[11], CultureInfo.InvariantCulture),
                    float.Parse(cols[12], CultureInfo.InvariantCulture),
                    float.Parse(cols[13], CultureInfo.InvariantCulture));

                // 15–19: gravity torques
                for (int i = 0; i < 5; i++)
                    testCase.gravityTorques[i] = float.Parse(cols[14 + i], CultureInfo.InvariantCulture);

                // 20–32: substituted values (link lengths, lc, masses, gravity)
                for (int i = 0; i < 14; i++)
                    testCase.substitutedValues[i] = float.Parse(cols[19 + i], CultureInfo.InvariantCulture);

                // At column 33 onward (0-based index), parse 18 values
                int rotStart = 33;  // because MATLAB headers are 1-based, Unity 0-based

                float[] kneeRot = new float[9];
                float[] pelvisRot = new float[9];
                for (int i = 0; i < 9; i++)
                    kneeRot[i] = float.Parse(cols[rotStart + i], CultureInfo.InvariantCulture);
                for (int i = 0; i < 9; i++)
                    pelvisRot[i] = float.Parse(cols[rotStart + 9 + i], CultureInfo.InvariantCulture);

                // Build 4x4 matrices assuming identity translation
                Matrix4x4 kneeRotMatrix = Matrix4x4.identity;
                Matrix4x4 pelvisRotMatrix = Matrix4x4.identity;
                for (int r = 0; r < 3; r++)
                {
                    // Add rotation matrix part from the csv file
                    for (int c = 0; c < 3; c++)
                    {
                        kneeRotMatrix[r, c] = kneeRot[r + 3 * c];       // column-major
                        pelvisRotMatrix[r, c] = pelvisRot[r + 3 * c];
                    }

                    // Add position as last column
                    kneeRotMatrix[r, 3] = testCase.kneePos[r];
                    pelvisRotMatrix[r, 3] = testCase.pelvisPos[r];
                }

                // Note that these matrices are transformation from frame 2 to 0 and frame 3 to 0, respectively.
                testCase.rotationMatrixFrame2ToZero = kneeRotMatrix;
                testCase.rotationMatrixFrame3ToZero = pelvisRotMatrix;

                testCases.Add(testCase);
            }
        }

        return testCases;
    }


    public void RunTestsOfModelForwardAndInverseKinematicsAndGravityTorques(List<FKIKGravityTestCase5RModel> tests)
    {
        // FK - we don't currently use FK, so... we don't validate that. 

        // IK - validate the 5 joint variables computed with those from the test case. 
        // For each test case
        int numMatchingIkConfigs = 0;
        float toleranceForIkInRadians = 1e-3f; // radian tolerance for IK

        for (int testCaseIndex = 0; testCaseIndex < tests.Count; testCaseIndex++)
        {
            // Store the current test case in the 5R model's instance variable for it
            currentTestCase = tests[testCaseIndex];

            // Run IK on the test case 
            double[] jointVariableValues = GetJointVariableValuesFromMarkerDataInverseKinematics();

            // Compare computed joint variable values to expected values
            bool allMatch = true;
            for (int j = 0; j < 5; j++)
            {
                float predicted = (float)jointVariableValues[j];
                float expected = currentTestCase.jointAngles[j];

                float delta = Mathf.Abs(Mathf.DeltaAngle(predicted * Mathf.Rad2Deg, expected * Mathf.Rad2Deg)) * Mathf.Deg2Rad;

                if (delta > toleranceForIkInRadians)
                {
                    allMatch = false;

                    // Print mismatch info (in degrees for easier interpretation)
                    Debug.LogWarning($" IK mismatch at config {testCaseIndex}, joint {j + 1}:\n" +
                                     $"    Expected: {expected * Mathf.Rad2Deg:F4} degs\n" +
                                     $"    Predicted: {predicted * Mathf.Rad2Deg:F4} degs\n" +
                                     $"    Delta = {delta * Mathf.Rad2Deg:F4} degs");

                    break; // Optional: comment this out to report multiple joint mismatches per config
                }
                else
                {
                    Debug.Log($" IK matched at config {testCaseIndex}, joint {j + 1}:\n" +
                                     $"    Expected: {expected * Mathf.Rad2Deg:F4} degs\n" +
                                     $"    Predicted: {predicted * Mathf.Rad2Deg:F4} degs\n" +
                                     $"    Delta = {delta * Mathf.Rad2Deg:F4} degs");
                }
            }

            // If all 5 joint values matched the expected values, increment the counter of matches.
            if (allMatch)
            {
                numMatchingIkConfigs++;
            }

            // Gravity torques

        }

        // Print how many IK cases matched
        Debug.Log($"IK matched exactly for {numMatchingIkConfigs} of {tests.Count} configurations.");
    }



    // Inverse Kinematics function
    // Joint angle defination:
    // Theta 1 is forward
    // Theta 2 is left
    public double[] GetJointVariableValuesFromMarkerDataInverseKinematics()
    {
        // If we're using Vive data only
        if (dataSourceSelector == ModelDataSourceSelector.ViveOnly)

        {
            // Get the transform from the position data frame (Unity frame) to frame zero of the stance model, and vice versa
            Matrix4x4 transformationMatrixFromFrame0ToUnity = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnity.inverse;

            // Knee position is knee position
            // Shank position is tracker postion
            Vector3 RightKneeCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetRightKneeCenterPositionInUnityFrame();
            Vector3 LeftKneeCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetLeftKneeCenterPositionInUnityFrame();
            Vector3 middleKneeCenterPositionInUnityFrame = (RightKneeCenterPositionInUnityFrame + LeftKneeCenterPositionInUnityFrame) / 2.0f;
            Vector3 middleKneeCenterPositionInFrame0 =
                transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(middleKneeCenterPositionInUnityFrame);


            // Get the mid-ankle position
            (Vector3 middleAnklePositionInUnityFrame, _, float distanceFromAnkleCenterToShankCableAttachmentCenterInMeters) =
                viveTrackerDataManagerScript.GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();
            Vector3 avgVivePosAnkleMarkerFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(middleAnklePositionInUnityFrame);
            
            // Get the unit vector from the ankle to the knee in frame 0
            Vector3 ankleToKneeUnitVectorUnityFrame0 = (middleKneeCenterPositionInFrame0 - avgVivePosAnkleMarkerFrame0).normalized;

            // Joint angle defination:
            // Theta 1 is forward
            // Theta 2 is left
            // Compute theta1 and theta2 using body 3-2-1 rotation matrix approach (related to that matrix, anyway)
            theta2 = Mathf.Asin(ankleToKneeUnitVectorUnityFrame0.z);
            // Theta 2 rotates around negative y axis :(
            theta1 = Mathf.Atan2(ankleToKneeUnitVectorUnityFrame0.y, ankleToKneeUnitVectorUnityFrame0.x);

            // Build the rotation matrix from the pelvis frame to the knee frame (R^(pelvis)_(knee))
            // First build the unit vectors that define link frame 2(knee center), which is located at the knee.

            // x up
            // y back
            // z right

            // These values are not used!
/*            Vector3 linkTwoUnitVectorXAxisInUnityFrame = ankleToKneeUnitVectorUnityFrame;
            Vector3 linkTwoUnitVectorZAxisInUnityFrame = RightKneeCenterPositionInUnityFrame - middleKneeCenterPositionInUnityFrame;
            linkTwoUnitVectorZAxisInUnityFrame = linkTwoUnitVectorZAxisInUnityFrame / linkTwoUnitVectorZAxisInUnityFrame.magnitude;
            Vector3 linkTwoUnitVectorYAxisInUnityFrame = -Vector3.Cross(linkTwoUnitVectorZAxisInUnityFrame, linkTwoUnitVectorXAxisInUnityFrame);
            linkTwoUnitVectorYAxisInUnityFrame = linkTwoUnitVectorYAxisInUnityFrame / linkTwoUnitVectorYAxisInUnityFrame.magnitude;
            linkTwoUnitVectorZAxisInUnityFrame = -Vector3.Cross(linkTwoUnitVectorXAxisInUnityFrame, linkTwoUnitVectorYAxisInUnityFrame);
            linkTwoUnitVectorZAxisInUnityFrame = linkTwoUnitVectorZAxisInUnityFrame / linkTwoUnitVectorZAxisInUnityFrame.magnitude;*/

            // In order to call the variable in script viveTrackerDataManagerScript, is following method correct?
            // Rob said it is right

            // Get the pelvic center in frame 0
            Vector3 pelvisCenterInUnityFrame = viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();
            Vector3 pelvisCenterInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(pelvisCenterInUnityFrame); // Multiply point applies a rotation and translation by using the full transformation matrix
            Matrix4x4 TranformationMatrixFromFrame0ToFrame2 = viveTrackerDataManagerScript.TranformationMatrixFromFrame0ToFrame2();
            
            // Get the unit vector from the mid-knee to the pelvis in frame 2
            Vector3 vectorFromKneeToPelvisFrame2 = TranformationMatrixFromFrame0ToFrame2.MultiplyVector(pelvisCenterInFrame0 - middleKneeCenterPositionInFrame0); // MultiplyVector only uses the rotation matrix from the transformation matrix
            Vector3 unitVectorFromKneeToPelvisFrame2 = vectorFromKneeToPelvisFrame2.normalized;
           
            // Get theta 3 using basic rotation about z-axis matrix
            theta3 = Mathf.Atan2(unitVectorFromKneeToPelvisFrame2.y, unitVectorFromKneeToPelvisFrame2.x);
           
            // Get the chest center in frame 0
            Vector3 chestCenterInUnityFrame = viveTrackerDataManagerScript.GetChestCenterPositionInUnityFrame();
            Vector3 chestCenterInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(chestCenterInUnityFrame);

            // If desired, we can try an alternate version of the IK where we use the vector from the pelvic tracker to chest tracker, 
            // NOT the vector from pelvic center to chest center. Could be more robust.
            // To this end, get the pelvis and chest tracker positions in frame 0 
            Vector3 pelvisTrackerPositionUnityFrame = viveTrackerDataManagerScript.GetPelvicViveTrackerGameObject().transform.position;
            Vector3 chestTrackerPositionUnityFrame = viveTrackerDataManagerScript.GetChestViveTrackerGameObject().transform.position;
            // Transform tracker positions to frame 0
            Vector3 pelvisTrackerPositionFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(pelvisTrackerPositionUnityFrame);
            Vector3 chestTrackerPositionFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(chestTrackerPositionUnityFrame);


            // Get the vector from the pelvis to the chest expressed in frame 3
            // Currently we prefer the pelvic-to-chest TRACKER vector over the pelvic-to-chest CENTER positions to minimize error from the elliptical model of the segment profile.
            Matrix4x4 transformationMatrixFromFrame0ToFrame3 = viveTrackerDataManagerScript.TranformationMatrixFromFrame0ToFrame3();
            Vector3 vectorFromPelvisToChestFrame3 = transformationMatrixFromFrame0ToFrame3.MultiplyVector(chestTrackerPositionFrame0 - pelvisTrackerPositionFrame0);
            
            // Get theta4 and theta5 using the rotation matrix from frame 5 to frame 3 (related to the body 3-2-1 rotation matrix first column)
            Vector3 unitVectorFromPelvisToChestFrame3 = vectorFromPelvisToChestFrame3.normalized;
            theta4 = Mathf.Atan2(unitVectorFromPelvisToChestFrame3.y, unitVectorFromPelvisToChestFrame3.x);
            theta5 = Mathf.Asin(unitVectorFromPelvisToChestFrame3.z);
            //Debug.Log("vectorFromPelvisToChestFrame3"+unitVectorFromPelvisToChestFrame3);

        }
        else if (dataSourceSelector == ModelDataSourceSelector.ViconOnly)
        {

            // If we're using Vicon data only
            // Get the transform from the position data frame (Vicon frame) to frame zero of the stance model.
            // This requires defining frame 0.

            // Get ankle, knees (R,L, and mid-point), pelvis, and chest positions in the position data frame

            // Build link 3 frame (pelvis) axes
            Matrix4x4 transformationMatrixFromViconToFrame0 = GetTransformFromViconFrameToFrameZeroOfStanceModel();

            // Get knee markers in vicon frame:)
            (_, Vector3 rightKneeMarkerPosViconFrame) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName("RKNE");
            (_, Vector3 leftKneeMarkerPosViconFrame) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName("LKNE");
            // Get avg pos of right and left knee
            Vector3 avgPosKneeMarker = (rightKneeMarkerPosViconFrame + leftKneeMarkerPosViconFrame) / 2.0f;
            Vector3 avgPosKneeMarkerFrame0 = transformationMatrixFromViconToFrame0.MultiplyPoint3x4(avgPosKneeMarker);

            // Get outer ankle markers 
            (_, Vector3 rightAnkleMarkerPosViconFrame) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName("RANK");
            (_, Vector3 leftAnkleMarkerPosViconFrame) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName("LANK");
            // Avg to get ankle midpoint
            Vector3 avgPosAnkleMarker = (rightAnkleMarkerPosViconFrame + leftAnkleMarkerPosViconFrame) / 2.0f;
            Vector3 avgPosAnkleMarkerFrame0 = transformationMatrixFromViconToFrame0.MultiplyPoint3x4(avgPosAnkleMarker);

            // Get unit vector ankle to knee
            Vector3 ankleToKneeUnitVectorViconFrame = avgPosKneeMarker - avgPosAnkleMarker;
            ankleToKneeUnitVectorViconFrame = ankleToKneeUnitVectorViconFrame / ankleToKneeUnitVectorViconFrame.magnitude;
            Vector3 ankleToKneeUnitVectorFrame0 = avgPosKneeMarkerFrame0 - avgPosAnkleMarkerFrame0;
            ankleToKneeUnitVectorFrame0 = ankleToKneeUnitVectorFrame0 / ankleToKneeUnitVectorFrame0.magnitude;


            // joint angle defination:
            // theta 1 is forward
            // theta 2 is left

            // Compute theta1 and theta2 using body 3-2-1 rotation matrix approach
            theta2 = Mathf.Asin(ankleToKneeUnitVectorFrame0.z);
            // Theta 2 rotates around negative y axis :(
            theta1 = Mathf.Atan2(ankleToKneeUnitVectorFrame0.y, ankleToKneeUnitVectorFrame0.x);

            // Get unit vector knee to pelvis
            Vector3 pelvisCenterCoords = centerOfMassManagerScript.getCenterOfPelvisMarkerPositionsInViconFrame();
            Vector3 kneeToPelvisUnitVector = pelvisCenterCoords - avgPosKneeMarker;
            kneeToPelvisUnitVector = kneeToPelvisUnitVector / kneeToPelvisUnitVector.magnitude;

            // Build the rotation matrix from the pelvis frame to the knee frame (R^(pelvis)_(knee))
            // First build the unit vectors that define link frame 2(knee center), which is located at the knee.

            // x up
            // y back
            // z right
            Vector3 linkTwoUnitVectorXAxis = ankleToKneeUnitVectorViconFrame;
            Vector3 linkTwoUnitVectorZAxis = rightKneeMarkerPosViconFrame - avgPosKneeMarker;
            linkTwoUnitVectorZAxis = linkTwoUnitVectorZAxis / linkTwoUnitVectorZAxis.magnitude;
            Vector3 linkTwoUnitVectorYAxis = centerOfMassManagerScript.getRightHandedCrossProduct(linkTwoUnitVectorZAxis,
                                                                                                    linkTwoUnitVectorXAxis);
            linkTwoUnitVectorYAxis = linkTwoUnitVectorYAxis / linkTwoUnitVectorYAxis.magnitude;
            linkTwoUnitVectorZAxis = centerOfMassManagerScript.getRightHandedCrossProduct(linkTwoUnitVectorYAxis,
                                                                                              linkTwoUnitVectorXAxis);
            linkTwoUnitVectorZAxis = linkTwoUnitVectorZAxis / linkTwoUnitVectorZAxis.magnitude;

            // Vectors that define pelvic frame
            Vector3 linkThreeUnitVectorXAxis = kneeToPelvisUnitVector;
            (_, Vector3 mostRecentLeftAsisPositionViconFrame) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName("LASI");
            (_, Vector3 mostRecentRightAsisPositionViconFrame) = centerOfMassManagerScript.GetMostRecentMarkerPositionByName("RASI");
            // z become left:)
            Vector3 linkThreeUnitVectorZAxis = mostRecentLeftAsisPositionViconFrame -
                                                mostRecentRightAsisPositionViconFrame;

            linkThreeUnitVectorZAxis = linkThreeUnitVectorZAxis / linkThreeUnitVectorZAxis.magnitude;
            // y is forward
            Vector3 linkThreeUnitVectorYAxis = centerOfMassManagerScript.getRightHandedCrossProduct(linkThreeUnitVectorZAxis,
                                                                                                    linkThreeUnitVectorXAxis);

            linkThreeUnitVectorYAxis = linkThreeUnitVectorYAxis / linkThreeUnitVectorYAxis.magnitude;

            linkThreeUnitVectorZAxis = centerOfMassManagerScript.getRightHandedCrossProduct(linkThreeUnitVectorYAxis,
                                                                                                    linkThreeUnitVectorXAxis);
            // Edit the z axis perpendicular to the x axis
            linkThreeUnitVectorZAxis = linkThreeUnitVectorZAxis / linkThreeUnitVectorZAxis.magnitude;

            // Build the first column of rotation matrix from frame 3 to frame 2
            // Rotates by z-axis(3*3)
            Vector3 rotationMatrixPelvisToKneeCol1 = new Vector3(Vector3.Dot(linkThreeUnitVectorXAxis, linkTwoUnitVectorXAxis),
                                                        Vector3.Dot(linkThreeUnitVectorXAxis, linkTwoUnitVectorYAxis),
                                                        Vector3.Dot(linkThreeUnitVectorXAxis, linkTwoUnitVectorZAxis));

            Debug.Log("Link frame 3 x-axis: (" + linkThreeUnitVectorXAxis.x + ", " + linkThreeUnitVectorXAxis.y + ", " + linkThreeUnitVectorXAxis.z + ") and" +
                "Link frame 3 y-axis: (" + linkThreeUnitVectorYAxis.x + ", " + linkThreeUnitVectorYAxis.y + ", " + linkThreeUnitVectorYAxis.z + ") and" +
                "Link frame 3 z-axis: (" + linkThreeUnitVectorZAxis.x + ", " + linkThreeUnitVectorZAxis.y + ", " + linkThreeUnitVectorZAxis.z + ") and" +
                " link frame 2 x-axis: (" + linkTwoUnitVectorXAxis.x + ", " + linkTwoUnitVectorXAxis.y + ", " + linkTwoUnitVectorXAxis.z + ")");

            // Compute the knee angle theta3 from this rotation matrix (R^(pelvis)_(knee))
            // z_3 is left
            theta3 = Mathf.Atan2(rotationMatrixPelvisToKneeCol1.y, rotationMatrixPelvisToKneeCol1.x);

            // Get unit vector from pelvis to chest
            Vector3 chestCenterCoords = centerOfMassManagerScript.getCenterOfShoulderMarkerPositionsInViconFrame();
            Vector3 pelvisToChestUnitVector = chestCenterCoords - pelvisCenterCoords;
            Debug.Log("Chest center at: (" + chestCenterCoords.x + ", " + chestCenterCoords.y + ", " + chestCenterCoords.z + ") and" +
                " pelvic center at: (" + pelvisCenterCoords.x + ", " + pelvisCenterCoords.y + ", " + pelvisCenterCoords.z + ")");
            pelvisToChestUnitVector = pelvisToChestUnitVector / pelvisToChestUnitVector.magnitude;

            Matrix4x4 rotationMatrixLinkThreeFrameToViconFrame = centerOfMassManagerScript.getTransformationMatrix(linkThreeUnitVectorXAxis,
                                                                                                                   linkThreeUnitVectorYAxis,
                                                                                                                   linkThreeUnitVectorZAxis,
                                                                                                                   pelvisCenterCoords);
            rotationMatrixLinkThreeFrameToViconFrame = rotationMatrixLinkThreeFrameToViconFrame.inverse;
            Matrix4x4 rotationMatrixLinkThreeFrameToViconFrameInverse = rotationMatrixLinkThreeFrameToViconFrame.inverse;
            rotationMatrixLinkThreeFrameToViconFrameInverse.SetColumn(3, new Vector4(0, 0, 0, 1));

            // Express the unit vector from pelvis to chest in link3 (pelvis) frame
            Vector4 pelvisToChestUnitVectorInViconFrame = new Vector4(pelvisToChestUnitVector.x,
                                                            pelvisToChestUnitVector.y,
                                                            pelvisToChestUnitVector.z, 0);
            Vector4 pelvisToChestUnitVectorInLink3FrameVector4 = rotationMatrixLinkThreeFrameToViconFrameInverse * pelvisToChestUnitVectorInViconFrame;
            Vector3 pelvisToChestUnitVectorInLink3Frame = new Vector3(pelvisToChestUnitVectorInLink3FrameVector4.x,
                                                                        pelvisToChestUnitVectorInLink3FrameVector4.y,
                                                                        pelvisToChestUnitVectorInLink3FrameVector4.z);

            Debug.Log("Pelvis-to-chest vector Vicon frame: (" + pelvisToChestUnitVector.x + ", " + pelvisToChestUnitVector.y + ", " + pelvisToChestUnitVector.z + ") and" +
                " and in link 3 frame: (" + pelvisToChestUnitVectorInLink3Frame.x + ", " + pelvisToChestUnitVectorInLink3Frame.y + ", " + pelvisToChestUnitVectorInLink3Frame.z + ")");

            // Compute theta 4 and theta5 using body 3-2-1 rotation matrix approach
            // z_4 is backward
            theta4 = Mathf.Atan2(pelvisToChestUnitVectorInLink3Frame.y, pelvisToChestUnitVectorInLink3Frame.x);
            theta5 = Mathf.Asin(pelvisToChestUnitVectorInLink3Frame.z);

            if (double.IsNaN(theta4) || double.IsNaN(theta5))
            {
                Debug.Log("");
            }

            //return new float[] { theta1, theta2, theta3, theta4, theta5 };

        }  
        // Else if we're using both Vive and Vicon
        else if(dataSourceSelector == ModelDataSourceSelector.ViveOnly)
        // We removed the else if because we need to make sure the function returns a value
        // if (dataSourceSelector == ModelDataSourceSelector.ViconAndVive)
        {
            // If we're using both Vicon and Vive data
            // Implement later

            return new double[] {0.0f, 0.0f, 0.0f, 0.0f, 0.0f};
            
            
        }
        else if (dataSourceSelector == ModelDataSourceSelector.DummyInputs)
        {
            // Use rotation matrices and positions directly from the test case
            Matrix4x4 T_2_to_0_from_file = currentTestCase.rotationMatrixFrame2ToZero;
            Matrix4x4 T_3_to_0_from_file = currentTestCase.rotationMatrixFrame3ToZero;

            // Positions (already in frame 0)
            Vector3 anklePosInFrame0 = new Vector3(0.0f, 0.0f, 0.0f); // ankle at origin (defined for clarity)
            Vector3 kneePosInFrame0 = currentTestCase.kneePos;
            Vector3 pelvisPosInFrame0 = currentTestCase.pelvisPos;
            Vector3 chestPosInFrame0 = currentTestCase.chestPos;

            // ----------------------------
            // Theta1 and Theta2 (based on knee unit vector in frame 0)
            // ----------------------------
            Vector3 ankleToKnee = kneePosInFrame0 - anklePosInFrame0;  // assume anklePos is defined earlier
            Vector3 ankleToKneeUnit = ankleToKnee.normalized;

            theta2 = Mathf.Asin(ankleToKneeUnit.z); // Z is up in frame 0
            theta1 = Mathf.Atan2(ankleToKneeUnit.y, ankleToKneeUnit.x); // Y = forward, X = up

            // Get the transformation from frame 2 to frame 0 using the function defined in the Vive tracker data manager
            (Vector3 xUnitVectorFrame2InFrame0, Vector3 yUnitVectorFrame2InFrame0, Vector3 zUnitVectorFrame2InFrame0) = 
                viveTrackerDataManagerScript.GetColumnsOfRotationMatrixFromFrame2ToFrame0FiveRModel((float) theta1, (float) theta2);
            Matrix4x4 T_2_to_0 = new Matrix4x4();
            T_2_to_0.SetColumn(0, new Vector4(xUnitVectorFrame2InFrame0.x, xUnitVectorFrame2InFrame0.y, xUnitVectorFrame2InFrame0.z, 0));
            T_2_to_0.SetColumn(1, new Vector4(yUnitVectorFrame2InFrame0.x, yUnitVectorFrame2InFrame0.y, yUnitVectorFrame2InFrame0.z, 0));
            T_2_to_0.SetColumn(2, new Vector4(zUnitVectorFrame2InFrame0.x, zUnitVectorFrame2InFrame0.y, zUnitVectorFrame2InFrame0.z, 0));
            T_2_to_0.SetColumn(3, new Vector4(kneePosInFrame0.x, kneePosInFrame0.y, kneePosInFrame0.z, 1));

            Debug.Log("T_2_to_0: y vector, y component is: " + yUnitVectorFrame2InFrame0.y);
            Debug.Log($"After assignment: T_2_to_0[1,1] = {T_2_to_0[1, 1]}, y.y = {yUnitVectorFrame2InFrame0.y}");


            // Get the transformation from frame 0 to frame 2
            Matrix4x4 T_0_to_2 = T_2_to_0.inverse;

            // ----------------------------
            // Theta3 (pelvis relative to knee)
            // ----------------------------
            // Solve for theta3
            Vector3 vectorMidKneeToPelvisInFrame0 = pelvisPosInFrame0 - kneePosInFrame0;
            Vector3 vectorFromKneeToPelvisFrame2 = T_0_to_2.MultiplyVector(vectorMidKneeToPelvisInFrame0); // MultiplyVector only uses the rotation matrix from the transformation matrix
            Vector3 unitVectorFromKneeToPelvisFrame2 = vectorFromKneeToPelvisFrame2.normalized;
            Vector3 x3_in_frame2 = unitVectorFromKneeToPelvisFrame2;
            theta3 = Mathf.Atan2(x3_in_frame2.y, x3_in_frame2.x);

            // ----------------------------
            // Theta4 and Theta5 (chest relative to pelvis)
            // ----------------------------
            // Get the transformation from frame 3 to frame 0 using the function defined in the Vive tracker data manager
            (Vector3 xUnitVectorFrame3InFrame0, Vector3 yUnitVectorFrame3InFrame0, Vector3 zUnitVectorFrame3InFrame0) =
                viveTrackerDataManagerScript.GetColumnsOfRotationMatrixFromFrame3ToFrame0FiveRModel((float)theta1, (float) theta2, (float) theta3);
            Matrix4x4 T_3_to_0 = new Matrix4x4();
            T_3_to_0.SetColumn(0, new Vector4(xUnitVectorFrame3InFrame0.x, xUnitVectorFrame3InFrame0.y, xUnitVectorFrame3InFrame0.z, 0));
            T_3_to_0.SetColumn(1, new Vector4(yUnitVectorFrame3InFrame0.x, yUnitVectorFrame3InFrame0.y, yUnitVectorFrame3InFrame0.z, 0));
            T_3_to_0.SetColumn(2, new Vector4(zUnitVectorFrame3InFrame0.x, zUnitVectorFrame3InFrame0.y, zUnitVectorFrame3InFrame0.z, 0));
            T_3_to_0.SetColumn(3, new Vector4(pelvisPosInFrame0.x, pelvisPosInFrame0.y, pelvisPosInFrame0.z, 1));
            Matrix4x4 T_0_to_3 = T_3_to_0.inverse;
            Vector3 chestRelativeToPelvis = (chestPosInFrame0 - pelvisPosInFrame0);
            Vector3 chestVecInFrame3 = T_0_to_3.MultiplyVector(chestRelativeToPelvis).normalized;

            theta4 = Mathf.Atan2(chestVecInFrame3.y, chestVecInFrame3.x);
            theta5 = Mathf.Asin(chestVecInFrame3.z);

            // DEBUG ONLY - compare transformation matrices from file and those computed here in Unity
            // Compare T_2_to_0
            float matrixTolerance = 1e-4f;
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    float fromFile = T_2_to_0_from_file[row, col];
                    float computed = T_2_to_0[row, col];
                    float delta = Mathf.Abs(fromFile - computed);

                    if (delta > matrixTolerance)
                    {
                        Debug.LogWarning($" T_2_to_0 mismatch at ({row},{col}): File={fromFile:F6}, Computed={computed:R}, Delta={delta:F6}");
                    }
                }
            }

            // Compare T_3_to_0

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    float fromFile = T_3_to_0_from_file[row, col];
                    float computed = T_3_to_0[row, col];
                    float delta = Mathf.Abs(fromFile - computed);

                    if (delta > matrixTolerance)
                    {
                        Debug.LogWarning($" T_3_to_0 mismatch at ({row},{col}): File={fromFile:F6}, Computed={computed:F6}, Delta={delta:F6}");
                    }
                }
            }
        }
        else // undefined
        {
            return new double[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };
        }
            // all thetas are radius
            return new double[] { theta1, theta2, theta3, theta4, theta5 };
    }




    public double[] GetJointVariableValuesFromInverseKinematicsVirtualPelvicTracker()
    {

        double[] thetasWithVirtualPelvicTracker = new double[5]; // 5 since this is the 5-DOF model
        if (dataSourceSelector == ModelDataSourceSelector.ViveOnly)

        {
            // If we're using Vive data only
            // Get the transform from the position data frame (Unity frame) to frame zero of the stance model.
            // This requires defining frame 0.

            // Get ankle, knees (R,L, and mid-point), pelvis, and chest positions

            // Build link 3 frame (pelvis) axes 
            Matrix4x4 transformationMatrixFromFrame0ToUnity = viveTrackerDataManagerScript.GetTransformationMatrixFromFrame0ToUnityFrame();
            Matrix4x4 transformationMatrixFromUnityToFrame0 = transformationMatrixFromFrame0ToUnity.inverse;

            // Knee position is knee position
            // Shank position is tracker postion
            Vector3 RightKneeCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetRightKneeCenterPositionInUnityFrame();
            Vector3 LeftKneeCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetLeftKneeCenterPositionInUnityFrame();
            Vector3 middleKneeCenterPositionInUnityFrame = (RightKneeCenterPositionInUnityFrame + LeftKneeCenterPositionInUnityFrame) / 2.0f;
            Vector3 middleKneeCenterPositionInFrame0 =
                transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(middleKneeCenterPositionInUnityFrame);
            Vector3 avgVivePosKneeMarkerFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(middleKneeCenterPositionInUnityFrame);

            (Vector3 middleAnklePositionInUnityFrame, _, float distanceFromAnkleCenterToShankCableAttachmentCenterInMeters) =
                viveTrackerDataManagerScript.GetDistanceFromAnkleCenterToShankCableAttachmentCenterInMeters();
            Vector3 avgVivePosAnkleMarkerFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(middleAnklePositionInUnityFrame);


            // Get unit vector ankle to knee
            Vector3 ankleToKneeUnitVectorUnityFrame = (middleKneeCenterPositionInUnityFrame - middleAnklePositionInUnityFrame).normalized;

            //ankleToKneeUnitVectorUnityFrame = ankleToKneeUnitVectorUnityFrame / ankleToKneeUnitVectorUnityFrame.magnitude;
            Vector3 ankleToKneeUnitVectorUnityFrame0 = (avgVivePosKneeMarkerFrame0 - avgVivePosAnkleMarkerFrame0).normalized;
            //ankleToKneeUnitVectorUnityFrame0 = ankleToKneeUnitVectorUnityFrame0 / ankleToKneeUnitVectorUnityFrame0.magnitude;

            // Joint angle defination:
            // Theta 1 is forward
            // Theta 2 is left

            // Compute theta1 and theta2 using body 3-2-1 rotation matrix approach
            double theta2 = Mathf.Asin(ankleToKneeUnitVectorUnityFrame0.z);
            // Theta 2 rotates around negative y axis :(
            double theta1 = Mathf.Atan2(ankleToKneeUnitVectorUnityFrame0.y, ankleToKneeUnitVectorUnityFrame0.x);

            // Get unit vector knee to pelvis in unity frame
            Vector3 pelvisCenterPositionInUnityFrame = viveTrackerDataManagerScript.GetVirtualAdjustedPelvicCenterPositionInUnityFrame();
            Vector3 kneeToPelvisVectorInUnityFrame = pelvisCenterPositionInUnityFrame - middleKneeCenterPositionInUnityFrame;
            kneeToPelvisVectorInUnityFrame = kneeToPelvisVectorInUnityFrame / kneeToPelvisVectorInUnityFrame.magnitude;


            // Build the rotation matrix from the pelvis frame to the knee frame (R^(pelvis)_(knee))
            // First build the unit vectors that define link frame 2(knee center), which is located at the knee.

            // x up
            // y back
            // z right

            Vector3 linkTwoUnitVectorXAxisInUnityFrame = ankleToKneeUnitVectorUnityFrame;
            Vector3 linkTwoUnitVectorZAxisInUnityFrame = RightKneeCenterPositionInUnityFrame - middleKneeCenterPositionInUnityFrame;
            linkTwoUnitVectorZAxisInUnityFrame = linkTwoUnitVectorZAxisInUnityFrame / linkTwoUnitVectorZAxisInUnityFrame.magnitude;
            Vector3 linkTwoUnitVectorYAxisInUnityFrame = -Vector3.Cross(linkTwoUnitVectorZAxisInUnityFrame, linkTwoUnitVectorXAxisInUnityFrame);
            linkTwoUnitVectorYAxisInUnityFrame = linkTwoUnitVectorYAxisInUnityFrame / linkTwoUnitVectorYAxisInUnityFrame.magnitude;
            linkTwoUnitVectorZAxisInUnityFrame = -Vector3.Cross(linkTwoUnitVectorXAxisInUnityFrame, linkTwoUnitVectorYAxisInUnityFrame);
            linkTwoUnitVectorZAxisInUnityFrame = linkTwoUnitVectorZAxisInUnityFrame / linkTwoUnitVectorZAxisInUnityFrame.magnitude;

            // In order to call the variable in script viveTrackerDataManagerScript, is following method correct?
            // Rob said it is right

            // This is x(hat)^2_3(in note)
            Vector3 pelvisCenterInUnityFrame = viveTrackerDataManagerScript.GetPelvicCenterPositionInUnityFrame();
            Vector3 pelvisCenterInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(pelvisCenterInUnityFrame); // Multiply point applies a rotation and translation by using the full transformation matrix
            Matrix4x4 TranformationMatrixFromFrame0ToFrame2 = viveTrackerDataManagerScript.TranformationMatrixFromFrame0ToFrame2();
            Vector3 vectorFromKneeToPelvisFrame2 = TranformationMatrixFromFrame0ToFrame2.MultiplyVector(pelvisCenterInFrame0 - middleKneeCenterPositionInFrame0); // MultiplyVector only uses the rotation matrix from the transformation matrix

            Vector3 vectorFromKneeToPelvisFrame0 = pelvisCenterInFrame0 - middleKneeCenterPositionInFrame0;
            vectorFromKneeToPelvisFrame0 = vectorFromKneeToPelvisFrame0.normalized;

            Vector3 unitVectorFromKneeToPelvisFrame2 = vectorFromKneeToPelvisFrame2.normalized;
            // Get theta 3 using basic rotation about z-axis matrix
            double theta3 = Mathf.Atan2(unitVectorFromKneeToPelvisFrame2.y, unitVectorFromKneeToPelvisFrame2.x);

            // This is x(hat)^3_5
            Vector3 chestCenterInUnityFrame = viveTrackerDataManagerScript.GetChestCenterPositionInUnityFrame();
            Vector3 chestCenterInFrame0 = transformationMatrixFromUnityToFrame0.MultiplyPoint3x4(chestCenterInUnityFrame);
            Matrix4x4 TranformationMatrixFromFrame0ToFrame3 = viveTrackerDataManagerScript.TranformationMatrixFromFrame0ToFrame3();
            Vector3 vectorFromPelvisToChestFrame3 = TranformationMatrixFromFrame0ToFrame3.MultiplyVector(chestCenterInFrame0 - pelvisCenterInFrame0);

            /*
             * 5.28
             * The job today is to utilize the functions in ViveTrackerDataManger to calculate the dynamics in the
             * simulation. 
             * 
             * 
             */
            // Get theta4 and theta5 using the body 3-2-1 rotation matrix first column
            Vector3 unitVectorFromPelvisToChestFrame3 = vectorFromPelvisToChestFrame3.normalized;
            double theta4 = Mathf.Atan2(unitVectorFromPelvisToChestFrame3.y, unitVectorFromPelvisToChestFrame3.x);
            double theta5 = Mathf.Asin(unitVectorFromPelvisToChestFrame3.z);

            // Store computed thetas for return
            thetasWithVirtualPelvicTracker[0] = theta1;
            thetasWithVirtualPelvicTracker[1] = theta2;
            thetasWithVirtualPelvicTracker[2] = theta3;
            thetasWithVirtualPelvicTracker[3] = theta4;
            thetasWithVirtualPelvicTracker[4] = theta5;
        }
        // Else, throw an error because this function is intended for use only with Vive
        else
        {
            Debug.LogError("Called a Vive-only function with Vicon selected as the data input source.");
        }
        // all thetas are radius
        return thetasWithVirtualPelvicTracker;
    }


    public void UpdateModelVisualization()
    {
        // Get the mid-ankle position in frame 0 - should be (0,0,0)
        Vector3 ankleCenterPosFrame0 = viveTrackerDataManagerScript.GetAnkleCenterInFrame0();

        // Get the mid-knee position in frame 0
        Vector3 midKneeCenterPosFrame0 = viveTrackerDataManagerScript.GetKneeCenterInFrame0();

        // Get the mid-pelvis position in frame 0
        Vector3 pelvicCenterPosFrame0 = viveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0();

        // Get the mid-chest position in frame 0
        Vector3 chestCenterPosFrame0 = viveTrackerDataManagerScript.GetChestCenterPositionInFrame0();

        // Transform all points to a frame located at the Unity origin
        // with y-axis = up, z-axis = left, and x-axis = forward. 
        // FRAME 0: x = up, y = forward, z = left
        // Define the columns of the matrix
        Vector4 col0 = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
        Vector4 col1 = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
        Vector4 col2 = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
        Vector4 col3 = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        Matrix4x4 transformationFrame0ToRenderingFrame0 = new Matrix4x4(col0, col1, col2, col3);

        // Transform positions to the frame 0 rendering frame.
        Vector3 ankleCenterPosFrame0Rendering = transformationFrame0ToRenderingFrame0.MultiplyPoint(ankleCenterPosFrame0);
        Vector3 midKneeCenterPosFrame0Rendering = transformationFrame0ToRenderingFrame0.MultiplyPoint(midKneeCenterPosFrame0);
        Vector3 pelvicCenterPosFrame0Rendering = transformationFrame0ToRenderingFrame0.MultiplyPoint(pelvicCenterPosFrame0);
        Vector3 chestCenterPosFrame0Rendering = transformationFrame0ToRenderingFrame0.MultiplyPoint(chestCenterPosFrame0);

        // DEBUG
        Debug.Log("Rendering stance model: list of line renderers has length: " + stanceModelLineRendererList.Count);

        // Link 1: mid-ankle to mid-knee
        stanceModelLineRendererList[0].SetPositions(new Vector3[] { ankleCenterPosFrame0Rendering, midKneeCenterPosFrame0Rendering });

        // Link 2: mid-knee to mid-pelvis
        stanceModelLineRendererList[1].SetPositions(new Vector3[] { midKneeCenterPosFrame0Rendering, pelvicCenterPosFrame0Rendering });

        // Link3: mid-pelvis to mid-chest
        stanceModelLineRendererList[2].SetPositions(new Vector3[] { pelvicCenterPosFrame0Rendering, chestCenterPosFrame0Rendering });
    }


    // Create line renderers to draw the stance model
    private void CreateLineRenderers(List<LineRenderer> list, Material material, int linkIndex)
    {

        GameObject lineObj = new GameObject("LineRenderer_link" + linkIndex);
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();

        // Basic configuration
        lr.material = material;
        lr.widthMultiplier = 0.05f;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2; // Rounded ends, optional
        lr.startColor = lr.endColor = Color.Lerp(Color.red, Color.blue, linkIndex / 2f);

        list.Add(lr);
        
    }

    private void WriteGravityTorqueToCSV(float[] thetaValues)
    {
       /* string filePath = Path.Combine(Application.dataPath, "Gravity_Torque.csv");

        bool fileExists = File.Exists(filePath);

        using (StreamWriter sw = new StreamWriter(filePath, true))
        {
            if (!fileExists)
            {
                // Write headers if the file does not exist
                sw.WriteLine("GravityTorque1,GravityTorque2,GravityTorque3,GravityTorque4,GravityTorque5");
            }

            string line = string.Join(",", thetaValues);
            sw.WriteLine(line);
        }*/
    }

    private Matrix4x4 BuildTransformationMatrixFromPelvisViveTrackerFrameToPelvisFrame(
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


    public double[] GetGravityTorqueAtEachModelJoint()
    {
        
        
        Debug.Log("starting gravity torque calculations");
        double gravityTorqueJoint1;
        double gravityTorqueJoint2;
        double gravityTorqueJoint3;
        double gravityTorqueJoint4;
        double gravityTorqueJoint5;

        gravityTorqueJoint1 = g * massOfTrunk * (lcTrunk * System.Math.Cos(theta5) * (System.Math.Cos(theta4) *
            (System.Math.Cos(theta1) * System.Math.Sin(theta3) - System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1)) -
            System.Math.Sin(theta4) * (System.Math.Cos(theta1) * System.Math.Cos(theta3) + System.Math.Cos(theta2) * System.Math.Sin(theta1) *
            System.Math.Sin(theta3))) - lengthAnkleToKnee * System.Math.Cos(theta2) * System.Math.Sin(theta1) + lengthKneeToPelvis * System.Math.Cos(theta1) *
            System.Math.Sin(theta3) - lengthKneeToPelvis * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1) + lcTrunk *
            System.Math.Sin(theta1) * System.Math.Sin(theta2) * System.Math.Sin(theta5)) - g * massOfThigh *
            System.Math.Sin(theta1) * System.Math.Sin(theta2) * System.Math.Sin(theta5) - g * massOfThigh * // removed second parenthesis after Sin(theta5)... repaste
            (lengthAnkleToKnee * System.Math.Cos(theta2) * System.Math.Sin(theta1) - lcThigh * System.Math.Cos(theta1) * System.Math.Sin(theta3) + 
            lcThigh * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1)) - g * zeroMass * (lengthAnkleToKnee *
            System.Math.Cos(theta2) * System.Math.Sin(theta1) - lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Sin(theta3) + lengthKneeToPelvis *
            System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1)) - g * lcShank * massOfShank * System.Math.Cos(theta2) * System.Math.Sin(theta1);

        gravityTorqueJoint2 = -g * System.Math.Cos(theta1) * (lengthAnkleToKnee * massOfThigh * System.Math.Sin(theta2) +
            lengthAnkleToKnee * zeroMass * System.Math.Sin(theta2) + lengthAnkleToKnee * massOfTrunk * System.Math.Sin(theta2) +
            lcShank * massOfShank * System.Math.Sin(theta2) + lengthKneeToPelvis * zeroMass * System.Math.Cos(theta3) * System.Math.Sin(theta2) +
            lengthKneeToPelvis * massOfTrunk * System.Math.Cos(theta3) * System.Math.Sin(theta2) + lcThigh * massOfThigh * System.Math.Cos(theta3) *
            System.Math.Sin(theta2) + lcTrunk * massOfTrunk * System.Math.Cos(theta2) * System.Math.Sin(theta5) + lcTrunk * massOfTrunk * System.Math.Cos(theta3) *
            System.Math.Cos(theta4) * System.Math.Cos(theta5) * System.Math.Sin(theta2) + lcTrunk * massOfTrunk * System.Math.Cos(theta5) * System.Math.Sin(theta2) *
            System.Math.Sin(theta3) * System.Math.Sin(theta4));

        gravityTorqueJoint3 = g * massOfTrunk * (lcTrunk * System.Math.Cos(theta5) * (System.Math.Cos(theta4) * (System.Math.Cos(theta3) *
            System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3)) + System.Math.Sin(theta4) * (System.Math.Sin(theta1) *
            System.Math.Sin(theta3) + System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3))) + lengthKneeToPelvis * System.Math.Cos(theta3) *
            System.Math.Sin(theta1) - lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3)) + lengthKneeToPelvis * g *
            zeroMass * (System.Math.Cos(theta3) * System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3)) + g *
            lcThigh * massOfThigh * (System.Math.Cos(theta3) * System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3));

        gravityTorqueJoint4 = -g * lcTrunk * massOfTrunk * System.Math.Cos(theta5) * (System.Math.Cos(theta4) * (System.Math.Cos(theta3) *
            System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3)) + System.Math.Sin(theta4) * (System.Math.Sin(theta1) *
            System.Math.Sin(theta3) + System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3)));

        // Symbolic g4 from matlab - confirmed a match!
/*        -gravity * lc5Sym * m5 * cos(theta5) * (cos(theta4) * (cos(theta3) * sin(theta1) - cos(theta1) *
    cos(theta2) * sin(theta3)) + sin(theta4) * (sin(theta1) * sin(theta3) + cos(theta1) * cos(theta2) * cos(theta3)))*/

        gravityTorqueJoint5 = -g * massOfTrunk * (lcTrunk * System.Math.Sin(theta5) * (System.Math.Cos(theta4) * (System.Math.Sin(theta1) *
            System.Math.Sin(theta3) + System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3)) - System.Math.Sin(theta4) * (System.Math.Cos(theta3) *
            System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3))) + lcTrunk * System.Math.Cos(theta1) *
            System.Math.Cos(theta5) * System.Math.Sin(theta2));

        // Symbolic g5 from matlab - confirmed a match!
        /*        -gravity * m5 * (lc5Sym * sin(theta5) * (cos(theta4) * (sin(theta1) * sin(theta3) + cos(theta1) * 
                    cos(theta2) * cos(theta3)) - sin(theta4) * (cos(theta3) * sin(theta1) - cos(theta1) * cos(theta2) 
                    * sin(theta3))) + lc5Sym * cos(theta1) * cos(theta5) * sin(theta2))*/





        Debug.Log("Computing gravity: (gravity, massTrunk, lcTrunk) are (" + g + ", " + massOfTrunk + ", " + lcTrunk + "), " + "thetas " + theta1 + " " + theta2 + " " + theta3 + " " + theta4 + " " + theta5 +
            " and gravity torques " + gravityTorqueJoint1 + " " + gravityTorqueJoint2 + " " + gravityTorqueJoint3 + " " +
            gravityTorqueJoint4 + " " + gravityTorqueJoint5);
        
        return new double[] { gravityTorqueJoint1, gravityTorqueJoint2, gravityTorqueJoint3, gravityTorqueJoint4, gravityTorqueJoint5 };
    }

    public Matrix<double> GetChestForceVelocityJacobianTranspose()
    {
        double row1_col1 = lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Cos(theta4) * (System.Math.Cos(theta1) * System.Math.Sin(theta3) - System.Math.Cos(theta2) * System.Math.Cos(theta3) *
            System.Math.Sin(theta1)) - System.Math.Sin(theta4) * (System.Math.Cos(theta1) * System.Math.Cos(theta3) + System.Math.Cos(theta2) * System.Math.Sin(theta1) * System.Math.Sin(theta3))) - lengthAnkleToKnee *
            System.Math.Cos(theta2) * System.Math.Sin(theta1) + lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Sin(theta3) - lengthKneeToPelvis * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1)
            + lengthPelvisToChest * System.Math.Sin(theta1) * System.Math.Sin(theta2) * System.Math.Sin(theta5);
        double row1_col2 = lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Cos(theta4) * (System.Math.Sin(theta1) * System.Math.Sin(theta3) + System.Math.Cos(theta1) * System.Math.Cos(theta2) *
            System.Math.Cos(theta3)) - System.Math.Sin(theta4) * (System.Math.Cos(theta3) * System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3))) + lengthAnkleToKnee *
            System.Math.Cos(theta1) * System.Math.Cos(theta2) + lengthKneeToPelvis * System.Math.Sin(theta1) * System.Math.Sin(theta3) + lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3)
            - lengthPelvisToChest * System.Math.Cos(theta1) * System.Math.Sin(theta2) * System.Math.Sin(theta5);
        double row1_col3 = 0.0;
        double row2_col1 = -System.Math.Cos(theta1) * (lengthAnkleToKnee * System.Math.Sin(theta2) + lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Sin(theta2) * System.Math.Sin(theta3) *
            System.Math.Sin(theta4) + System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Sin(theta2)) + lengthKneeToPelvis * System.Math.Cos(theta3) * System.Math.Sin(theta2) + lengthPelvisToChest *
            System.Math.Cos(theta2) * System.Math.Sin(theta5));
        double row2_col2 = -System.Math.Sin(theta1) * (lengthAnkleToKnee * System.Math.Sin(theta2) + lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Sin(theta2) * System.Math.Sin(theta3) *
            System.Math.Sin(theta4) + System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Sin(theta2)) + lengthKneeToPelvis * System.Math.Cos(theta3) * System.Math.Sin(theta2) + lengthPelvisToChest *
            System.Math.Cos(theta2) * System.Math.Sin(theta5));
        double row2_col3 = lengthAnkleToKnee * System.Math.Cos(theta2) + lengthKneeToPelvis * System.Math.Cos(theta2) * System.Math.Cos(theta3) - lengthPelvisToChest * System.Math.Sin(theta2) * System.Math.Sin(theta5) +
            lengthPelvisToChest * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Cos(theta5) + lengthPelvisToChest * System.Math.Cos(theta2) * System.Math.Cos(theta5) *
            System.Math.Sin(theta3) * System.Math.Sin(theta4);
        double row3_col1 = System.Math.Sin(theta1) * System.Math.Sin(theta2) * (lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Sin(theta2) * System.Math.Sin(theta3) * System.Math.Sin(theta4)
            + System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Sin(theta2)) + lengthKneeToPelvis * System.Math.Cos(theta3) * System.Math.Sin(theta2) + lengthPelvisToChest * System.Math.Cos(theta2) *
            System.Math.Sin(theta5)) - System.Math.Cos(theta2) * (lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Cos(theta4) * (System.Math.Cos(theta1) * System.Math.Sin(theta3) - System.Math.Cos(theta2) *
            System.Math.Cos(theta3) * System.Math.Sin(theta1)) - System.Math.Sin(theta4) * (System.Math.Cos(theta1) * System.Math.Cos(theta3) + System.Math.Cos(theta2) * System.Math.Sin(theta1) *
            System.Math.Sin(theta3))) + lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Sin(theta3) - lengthKneeToPelvis * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1) +
            lengthPelvisToChest * System.Math.Sin(theta1) * System.Math.Sin(theta2) * System.Math.Sin(theta5));
        double row3_col2 = lengthPelvisToChest * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Cos(theta5) * System.Math.Sin(theta1) * System.Math.Sin(theta4) - lengthKneeToPelvis *
            System.Math.Cos(theta2) * System.Math.Sin(theta1) * System.Math.Sin(theta3) - lengthPelvisToChest * System.Math.Cos(theta1) * System.Math.Cos(theta3) * System.Math.Cos(theta4) *
            System.Math.Cos(theta5) - lengthPelvisToChest * System.Math.Cos(theta1) * System.Math.Cos(theta5) * System.Math.Sin(theta3) * System.Math.Sin(theta4) - lengthKneeToPelvis *
            System.Math.Cos(theta1) * System.Math.Cos(theta3) - lengthPelvisToChest * System.Math.Cos(theta2) * System.Math.Cos(theta4) * System.Math.Cos(theta5) * System.Math.Sin(theta1) * System.Math.Sin(theta3);
        double row3_col3 = -System.Math.Sin(theta2) * (lengthKneeToPelvis * System.Math.Sin(theta3) - lengthPelvisToChest * System.Math.Cos(theta3) * System.Math.Cos(theta5) * System.Math.Sin(theta4) +
            lengthPelvisToChest * System.Math.Cos(theta4) * System.Math.Cos(theta5) * System.Math.Sin(theta3));
        double row4_col1 = -lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Sin(theta1) * System.Math.Sin(theta3) * System.Math.Sin(theta4) + System.Math.Cos(theta3) *
            System.Math.Cos(theta4) * System.Math.Sin(theta1) + System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta4) - System.Math.Cos(theta1) *
            System.Math.Cos(theta2) * System.Math.Cos(theta4) * System.Math.Sin(theta3));
        double row4_col2 = lengthPelvisToChest * System.Math.Cos(theta5) * (System.Math.Cos(theta1) * System.Math.Sin(theta3) * System.Math.Sin(theta4) + System.Math.Cos(theta1) * System.Math.Cos(theta3) *
            System.Math.Cos(theta4) - System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1) * System.Math.Sin(theta4) + System.Math.Cos(theta2) * System.Math.Cos(theta4) *
            System.Math.Sin(theta1) * System.Math.Sin(theta3));
        double row4_col3 = lengthPelvisToChest * System.Math.Sin(theta3 - theta4) * System.Math.Cos(theta5) * System.Math.Sin(theta2);
        double row5_col1 = lengthPelvisToChest * System.Math.Cos(theta3) * System.Math.Sin(theta1) * System.Math.Sin(theta4) * System.Math.Sin(theta5) - lengthPelvisToChest * System.Math.Cos(theta1) *
            System.Math.Cos(theta5) * System.Math.Sin(theta2) - lengthPelvisToChest * System.Math.Cos(theta4) * System.Math.Sin(theta1) * System.Math.Sin(theta3) * System.Math.Sin(theta5) - lengthPelvisToChest *
            System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Sin(theta5) - lengthPelvisToChest * System.Math.Cos(theta1) * System.Math.Cos(theta2) *
            System.Math.Sin(theta3) * System.Math.Sin(theta4) * System.Math.Sin(theta5);
        double row5_col2 = lengthPelvisToChest * System.Math.Cos(theta1) * System.Math.Cos(theta4) * System.Math.Sin(theta3) * System.Math.Sin(theta5) - lengthPelvisToChest * System.Math.Cos(theta1) *
            System.Math.Cos(theta3) * System.Math.Sin(theta4) * System.Math.Sin(theta5) - lengthPelvisToChest * System.Math.Cos(theta5) * System.Math.Sin(theta1) * System.Math.Sin(theta2) - lengthPelvisToChest *
            System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Sin(theta1) * System.Math.Sin(theta5) - lengthPelvisToChest * System.Math.Cos(theta2) * System.Math.Sin(theta1) *
            System.Math.Sin(theta3) * System.Math.Sin(theta4) * System.Math.Sin(theta5);
        double row5_col3 = -lengthPelvisToChest * (System.Math.Cos(theta3) * System.Math.Cos(theta4) * System.Math.Sin(theta2) * System.Math.Sin(theta5) - System.Math.Cos(theta2) * System.Math.Cos(theta5)
            + System.Math.Sin(theta2) * System.Math.Sin(theta3) * System.Math.Sin(theta4) * System.Math.Sin(theta5));

        Matrix<double> chestVelocityJacobianTranspose = DenseMatrix.OfArray(new double[,] {

        {row1_col1,row1_col2,row1_col3},
        {row2_col1,row2_col2,row2_col3},
        {row3_col1,row3_col2,row3_col3},
        {row4_col1,row4_col2,row4_col3},
        {row5_col1,row5_col2,row5_col3}});

        return chestVelocityJacobianTranspose;
    }

    public Matrix<double> GetPelvisForceVelocityJacobianTranspose()
    {
        double row1_col1 = lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Sin(theta3) - lengthAnkleToKnee * System.Math.Cos(theta2) *
            System.Math.Sin(theta1) - lengthKneeToPelvis * System.Math.Cos(theta2) * System.Math.Cos(theta3) * System.Math.Sin(theta1);
        double row1_col2 = lengthAnkleToKnee * System.Math.Cos(theta1) * System.Math.Cos(theta2) + lengthKneeToPelvis * System.Math.Sin(theta1) *
            System.Math.Sin(theta3) + lengthKneeToPelvis * System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Cos(theta3);
        double row1_col3 = 0;
        double row2_col1 = -System.Math.Cos(theta1) * System.Math.Sin(theta2) * (lengthAnkleToKnee + lengthKneeToPelvis * System.Math.Cos(theta3));
        double row2_col2 = -System.Math.Sin(theta1) * System.Math.Sin(theta2) * (lengthAnkleToKnee + lengthKneeToPelvis * System.Math.Cos(theta3));
        double row2_col3 = System.Math.Cos(theta2) * (lengthAnkleToKnee + lengthKneeToPelvis * System.Math.Cos(theta3));
        double row3_col1 = lengthKneeToPelvis * (System.Math.Cos(theta3) * System.Math.Sin(theta1) - System.Math.Cos(theta1) * System.Math.Cos(theta2) * System.Math.Sin(theta3));
        double row3_col2 = -lengthKneeToPelvis * (System.Math.Cos(theta1) * System.Math.Cos(theta3) + System.Math.Cos(theta2) * System.Math.Sin(theta1) * System.Math.Sin(theta3));
        double row3_col3 = -lengthKneeToPelvis * System.Math.Sin(theta2) * System.Math.Sin(theta3);

        Matrix<double> pelvisVelocityJacobianTranspose = DenseMatrix.OfArray(new double[,] {

        {row1_col1,row1_col2,row1_col3},
        {row2_col1,row2_col2,row2_col3},
        {row3_col1,row3_col2,row3_col3}
        });

        return pelvisVelocityJacobianTranspose;
    }

    public Matrix<double> GetKneeForceVelocityJacobianTranspose()
    {
        double row1_col1 = -lengthAnkleToShankCable * System.Math.Cos(theta2) * System.Math.Sin(theta1);
        double row1_col2 = lengthAnkleToShankCable * System.Math.Cos(theta1) * System.Math.Cos(theta2);
        double row1_col3 = 0;
        double row2_col1 = -lengthAnkleToShankCable * System.Math.Cos(theta1) * System.Math.Sin(theta2);
        double row2_col2 = -lengthAnkleToShankCable * System.Math.Sin(theta1) * System.Math.Sin(theta2);
        double row2_col3 = lengthAnkleToShankCable * System.Math.Cos(theta2);

        Matrix<double> kneeVelocityJacobianTranspose = DenseMatrix.OfArray(new double[,] {

        {row1_col1,row1_col2,row1_col3},
        {row2_col1,row2_col2,row2_col3},
        });

        return kneeVelocityJacobianTranspose;
    }


    public Matrix4x4 GetTransformFromViconFrameToFrameZeroOfStanceModel()
    {
        // Rotation from frame 0 of stance model to vicon frame
        Matrix4x4 transformationMatrixFrame0ToVicon = new Matrix4x4();

        Vector3 ankleJointCenterPositionViconFrame = centerOfMassManagerScript.GetAnkleJointCenterPositionViconFrame();

        Vector3 positionModelFrame0InViconFrame = ankleJointCenterPositionViconFrame;

        transformationMatrixFrame0ToVicon.SetColumn(0, new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
        transformationMatrixFrame0ToVicon.SetColumn(1, new Vector4(0.0f, -1.0f, 0.0f, 0.0f));
        transformationMatrixFrame0ToVicon.SetColumn(2, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        transformationMatrixFrame0ToVicon.SetColumn(3, new Vector4(positionModelFrame0InViconFrame.x, positionModelFrame0InViconFrame.y,
            positionModelFrame0InViconFrame.z, 1.0f));

        Matrix4x4 transformationMatrixViconToFrame0 = transformationMatrixFrame0ToVicon.inverse;
        return transformationMatrixViconToFrame0;

    }
}

/*public class FourRevoluteModel : KinematicModelOfStance
{
    // Constructor
    public FourRevoluteModel(float subjectMassInKgLocal, ManageCenterOfMassScript centerOfMassManagerScriptLocal)
    {
        subjectMassInKg = subjectMassInKgLocal;
        centerOfMassManagerScript = centerOfMassManagerScriptLocal;
    }
}*/



public enum StanceModelSelector
{
    FiveRModelWithKnees,
    FourRModel
};

public enum ModelDataSourceSelector
{
    ViveOnly, 
    ViconOnly,
    ViconAndVive, 
    DummyInputs
};




public class KinematicModelClass : MonoBehaviour
{
    // START: Instance variables**************************************************
    public StanceModelSelector stanceModelSelect;
    private KinematicModelOfStance stanceModel;

    // The subject-specific info storage script
    public SubjectInfoStorageScript subjectInfoStorageScript;

    // Select the data source
    public ModelDataSourceSelector dataSourceSelector;

    private ManageCenterOfMassScript markerDataManagerScript;
    private float subjectMass;

    public ViveTrackerDataManager viveDataManagerScript;

    // Specify if the stance model is ready to serve data
    private bool modelInstanceCreated = false;
    private bool stanceModelReadyToServeDataFlag = false; // starts as false, set in update loop

    // Whether or not this script should visualize the stance model.
    public bool visualizeStanceModel;
    public Material[] stanceModelRendererLineMaterial;

    // A reference to the CSV file that can be used to test the model FK, IK, and gravity torques
    public bool run5rModelUnitTestFlag;
    public TextAsset testIkAndFkAndGravTorquesCsvFile;

    // END: Instance variables**************************************************

    // This function creates an instance of a kinematic stance model. 
    // It is called by the ForceFieldHighLevelControllerScript.
    public KinematicModelOfStance CreateKinematicModelOfStance(ManageCenterOfMassScript centerOfMassManagerScript, ViveTrackerDataManager viveTrackerDataManagerScript)
    {
        if (viveTrackerDataManagerScript == null)
        {
            Debug.Log("Passed in vive data manager is null");
        }
        
        markerDataManagerScript = centerOfMassManagerScript; // store a reference to COM manager as instance variable
        viveDataManagerScript = viveTrackerDataManagerScript; // store a reference to the Vive data manager as instance variable
        // Choose a model type depending on the user selection: 4R or 5R
        switch (stanceModelSelect)
        {
            // In the 5R case (ankle is RR joint, knee is R, pelvis is RR joint)
            case StanceModelSelector.FiveRModelWithKnees:
                stanceModel = new FiveRevoluteModelWithKnees(subjectMass, markerDataManagerScript, viveDataManagerScript, 
                    dataSourceSelector, stanceModelRendererLineMaterial);
                break;
/*          case StanceModelSelector.FourRModel:
                      stanceModel = new FourRevoluteModel();
                      break;*/
        }

        // Record that a model instance has been created 
        modelInstanceCreated = true;

        // Return a reference to the stance model instance
        return stanceModel;
    }



    // Start is called before the first frame update
    void Start()
    {
        // Get the subject mass in kg 
        subjectMass = subjectInfoStorageScript.getSubjectMassInKilograms();

        // If we're doing a unit test on the 5R model
        if (run5rModelUnitTestFlag == true)
        {
            // Data source selector must be in DummyInputs mode - otherwise an error will occur.
            stanceModel = new FiveRevoluteModelWithKnees(subjectMass, markerDataManagerScript, viveDataManagerScript,
                    dataSourceSelector, stanceModelRendererLineMaterial);

            // Now run the unit tests on the IK and gravity torques
            stanceModel.TestModelForwardAndInverseKinematicsAndGravityTorques(testIkAndFkAndGravTorquesCsvFile);
        }
    }

    // Update is called once per frame
    void Update()
    {
        // If we have not already noted that we're ready to serve data
        if (stanceModelReadyToServeDataFlag == false)
        {
            // If an instance of the stance model has been initialized
            if(modelInstanceCreated == true)
            {
                // If we're using Vive as input
                if (dataSourceSelector == ModelDataSourceSelector.ViveOnly)
                {
                    // Vive data must be ready for the stance model to be ready
                    bool viveDataReady = viveDataManagerScript.GetViveTrackerDataHasBeenInitializedFlag();
                    if (viveDataReady == true)
                    {
                        stanceModelReadyToServeDataFlag = true;
                        Debug.Log("Stance model ready to serve data.");
                    }

                }
                else if (dataSourceSelector == ModelDataSourceSelector.ViconOnly)
                {
                    // Vicon data must be ready for the stance model to be ready
                    bool viconDataReady = markerDataManagerScript.getCenterOfMassManagerReadyStatus();
                    if (viconDataReady == true)
                    {
                        stanceModelReadyToServeDataFlag = true;
                        Debug.Log("Stance model ready to serve data.");
                    }
                }
                // Can add Vive + Vicon if we ever need it.
            }
        }
        // If the stance model is ready to serve data
        else
        {
            // if we're visualizing the stance model
            if(visualizeStanceModel == true)
            {
                stanceModel.UpdateModelVisualization();
            }
        }
    }

    // Get the stance model selector, which will tell us what type of stance model we're using.
    public StanceModelSelector GetStanceModelSelector()
    {
        return stanceModelSelect;
    }

    public ModelDataSourceSelector GetKinematicModelDataSourceSelector()
    {
        return dataSourceSelector;
    }

    public bool GetStanceModelReadyToServeDataFlag()
    {
        return stanceModelReadyToServeDataFlag;
    }
}

