using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Diagnostics;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class PelvisChestTrajectoryGenerator : MonoBehaviour
{

    // public class Polynomial 
    // Stores the coefficients of the polynomial P(x) and can evaluate the polynomial for a value of x.
    [System.Serializable]
    public class Polynomial
    {
        public List<float> Coefficients;

        public float Evaluate(float x)
        {
            float result = 0f;

            for (int i = 0; i < Coefficients.Count; i++)
            {
                float coeff = Coefficients[i];
                // Skip if NaN coefficient (e.g. poly is quadratic so skip higher order NaN coefficients)
                if (float.IsNaN(coeff))
                {
                    continue;
                }
                result += coeff * Mathf.Pow(x, i);
            }

            return result;
        }
    }

    // Stores one Dictionary with varName keys (e.g. theta1, pelvisTilt) and 
    // the associated polynomial as a value. 
    // NOTE each instance stores polynomials for one reach height and direction.
    [System.Serializable]
    public class DirectionHeightPolynomialsOneReachHeightAndDir
    {
        public float ReachDirectionDeg; // e.g. 0, 22.5, 45, etc.
        public float ReachHeight;       // in meters
        public Dictionary<string, Polynomial> StateVariablePolynomials; // "pelvisTilt", "chestYaw", etc.
    }


    // A container class for estimated pose information. 
    // For example, this PoseEstimate class could store the estimated position/orientation for the pelvis or for the chest.
    // A container class for estimated pose information
    [System.Serializable]
    public class PoseEstimate
    {
        public Vector3 Position;
        public Dictionary<string, float> OrientationVariables;

        public PoseEstimate(Vector3 position, Dictionary<string, float> orientationVariables)
        {
            Position = position;
            OrientationVariables = orientationVariables ?? new Dictionary<string, float>();
        }
    }

    // FkTestCase - a class we use in our unit test validation of the polynomial predictor and the FK model. 
    // The class stores a single "test point" = a reach direction in degrees, a reach height specifier in the range [0,3], 
    // and a reach progress in the range [0,1]. 
    // The expected pelvis and chest positions are compared against the computed positions. 
    // Note that the class also stores dummy/mock "subject-specific" measurements for the test. 
    // NOTE: 1.) polynomial file and 2.) mean reach limits per height specifier/direction used by this and MATLAB must be the same!!!
    public class FKTestCase
    {
        public float reachDirDeg;
        public float reachHeightSpecifier;
        public float progression;
        public Vector3 pelvisExpected;
        public Vector3 chestExpected;

        // Subject-specific constants
        public float lAnklePelvis;
        public float lPelvisChest;
        public float hPelvis;
        public float hChest;
        public float hShoulder;
        public float hEye;
    }



    // Instance variables *********************************************************************************************************
    // Trajectory generator state machine states
    private string currentState;
    private const string setupStateName = "SETUP_STATE";
    private const string readyToServeDataStateName = "READY_TO_SERVE_DATA_STATE";
    // The file name for the polynomials for getting pelvis and chest position from current hand height, reach dir, and reach progression.
    private const string polynomialsFileName = "height_dir_reach_progress_polynomial_data.csv"; // no .csv needed
    public TextAsset polynomialsCsv;
    // Text file for the unit test of the polynomial prediction of chest and pelvis pos.
    public TextAsset polynomialAndForwardKinematicsUnitTestCsv;

    // Get a reference to the level manager startup support script
    public LevelManagerStartupSupportScript levelManagerStartupSupportScript;

    // Get a reference to the Vive Tracker Data Manager
    public ViveTrackerDataManager viveTrackerDataManagerScript;

    // The List of DirectionHeightPolynomialsOneReachHeightAndDir objects. 
    // Each DirectionHeightPolynomialsOneReachHeightAndDir object has ALL of the polynomials associated with that 
    // reach height/dir combination. For example, it would have all joint variable polynomials for the 6-DoF prediction model.
    private List<DirectionHeightPolynomialsOneReachHeightAndDir> polynomialDataDictionaries;

    // Subject-specific lengths. These should be retrieved from the sensing module (e.g., the Vive tracker data manager or the Vicon data manager)
    private float lengthAnkleToPelvisInMeters; // = d3 in model
    private float lengthPelvisToChestInMeters; // = d6 in model

    // A flag indicating whether or not the polynomials CSV has been loaded. 
    private bool loadedJointOrientationVarPolynomialsFlag = false;
    
    // Lists with all of the unique directions and heights loaded from file
    private List<float> uniqueDirs = new List<float>();
    private List<float> uniqueHeights = new List<float>();

    // List of joint variable names (for position kinematics)
    private string[] jointVariables = new string[] { "thetaOne", "thetaTwo", "dThree", "thetaFour", "thetaFive", "dSix" };

    // List of orientation variable names (for pelvis/chest orientation)
    private string[] orientationVariables = new string[]
    {
        "pelvicRotation", "pelvicObliquity", "pelvicTilt",
        "trunkFlexion", "trunkAxialRotation", "trunkLateralBending"
    };

    // The hard-coded mean reach limits per reach height and direction. 
    // NOTE: 22.5 and 67.5 are currently NOT the measured values! They are just means of their neighbors. UPDATE!!!!!
    // Keys start with the reach direction (0, 22.5, 45.0, 67.5, 90.0) and the height specifier
    // follows the underscore (0 = waist, 1 = chest, 2 = shoulder, 3 = eye)
    // ALSO CONFIRM NUMBERS AGAINST MATLAB SINCE THESE WERE GPT-TRANSCRIBED!
    private Dictionary<string, float> dirHeightLimitsDictionary = new Dictionary<string, float>()
    {
        { "0_0", 0.498f },
        { "0_1", 0.487f },
        { "0_2", 0.475f },
        { "0_3", 0.435f },
        { "22.5_0", 0.5075f },
        { "22.5_1", 0.497f },
        { "22.5_2", 0.485f },
        { "22.5_3", 0.465f },
        { "45_0", 0.517f },
        { "45_1", 0.507f },
        { "45_2", 0.495f },
        { "45_3", 0.495f },
        { "67.5_0", 0.520f },
        { "67.5_1", 0.506f },
        { "67.5_2", 0.497f },
        { "67.5_3", 0.468f },
        { "90_0", 0.523f },
        { "90_1", 0.505f },
        { "90_2", 0.499f },
        { "90_3", 0.441f }
    };

    // The four key height levels (waist, chest, shoulder, and eye)
    // for this specific subject in meters.
    private float[] heightLevelsInMeters;

    // A flag indicating if we should run a test reach and visualize the corresponding generated poses
    // All variables grouped under this header are only needed for this testing.
    public bool visualizeGeneratedReachPoseFlag = true;
    public Stopwatch testReachingPostureGenerationStopwatch = new Stopwatch();
    private float testReachDurationInSeconds = 5.0f;
    private float testReachDirectionDeg = 90.0f; // values in range [0,90]
    private float testReachHeightSpecifier = 0f; // values in range [0,3]
    private LineRenderer anklePelvisLineRenderer;
    private LineRenderer pelvisChestLineRenderer;
    public Material anklePelvis6RModelMaterial;
    public Material pelvisChest6RModelMaterial;

    void Start()
    {
        // Load the polynomials from the .CSV 
        LoadPolynomialsCsv();

        // After loading the polynomial data, get unique reach directions and heights
        IdentifyUniqueReachDirsAndHeights();

        // MOVE BELOW HEIGHT CODE TO SETUP STATE IN UPDATE LOOP!
        // Get the subject-specific heights for the 4 reaching levels in meters
        //pelvisHeightInMeters,
        ////   chestHeightInMeters,
        //  shoulderHeightInMeters,
        //  eyeHeightInMeters
        // We need to get the subject-specific height during setup, before we can use any other functions!
        heightLevelsInMeters = new float[] {
            1.0f, 1.5f, 1.7f, 1.9f
        };

        // Run the unit test on the polynomial interpolation and 6-DOF model prediction of pelvis/chest position
        RunPolynomialInterpolatorAndForwardKinematicsUnitTest();

        // If we're visualizing a generated reach (debug/test only)
        if (visualizeGeneratedReachPoseFlag == true)
        {
            // Start the stopwatch for simulating
            testReachingPostureGenerationStopwatch.Start();

            // Create and set settings for the line renderers of ankle-pelvis and pelvis-chest
            anklePelvisLineRenderer = CreateLineRenderer(anklePelvis6RModelMaterial, 1);
            pelvisChestLineRenderer = CreateLineRenderer(pelvisChest6RModelMaterial, 2);

        }

        // We start in setup state (we must wait for movement-sensing systems like Vive or Vicon to initialize)
        currentState = setupStateName;
    }

    // Update is called once per frame
    void Update()
    {

        if(visualizeGeneratedReachPoseFlag == true)
        {
            // Get the current reach progress in the range [0,1]
            float testCurrentReachProgress = 0.601f + 0.399f * (testReachingPostureGenerationStopwatch.ElapsedMilliseconds / 1000.0f) / (testReachDurationInSeconds);

            // If the reach progress is less than or equal to 1
            if(testCurrentReachProgress <= 1.0f)
            {
                // Compute the predicted posture for the current reach height, dir, and progression
                (PoseEstimate pelvisPose, PoseEstimate chestPose) = PredictPelvisAndChestPositionAndOrientation(
                    testReachDirectionDeg, testReachHeightSpecifier, testCurrentReachProgress);

                // Also compute the hand position based on the current ankle-pelvis length (which sets the waist height reach) and 
                // pelvis-chest length (which sets the eye height reach)?
                if(testReachHeightSpecifier >= 0 && testReachHeightSpecifier <= 1.0f)
                {

                    float handXPosFrame0 = lengthAnkleToPelvisInMeters + lengthPelvisToChestInMeters * testReachHeightSpecifier;

                }


                // Visualize the pelvis and chest pos
                VisualizePelvisAndChestPosInFrame0RotatedToUnity(pelvisPose.Position, chestPose.Position);
            }
            // Else it is too large, reset stopwatch
            else
            {
                // Restart to loop simulation
                testReachingPostureGenerationStopwatch.Restart();
            }

        }


        // The state machine states
        if(currentState == setupStateName)
        {
            // See if the Vive tracker data is ready 
            bool viveDataAndKinematicModelReadyFlag = levelManagerStartupSupportScript.GetServicesStartupCompleteStatusFlag();
            if(viveDataAndKinematicModelReadyFlag == true)
            {
                // Get the subject-specific ankle-to-pelvis and pelvis-to-chest lengths.
                (_, float lengthPelvisToChest, _) = viveTrackerDataManagerScript.GetSubjectSpecificSegmentMetrics("Trunk");
                (_, float lengthKneeToPelvis, _) = viveTrackerDataManagerScript.GetSubjectSpecificSegmentMetrics("Thigh");
                (_, float lengthAnkleToKnee, _) = viveTrackerDataManagerScript.GetSubjectSpecificSegmentMetrics("Shank");
                // Ankle-to-pelvis is sum of ankle-to-knee and knee-to-pelvis
                lengthAnkleToPelvisInMeters = lengthAnkleToKnee + lengthKneeToPelvis;
                // Pelvis-to-chest is unchanged
                lengthPelvisToChestInMeters = lengthPelvisToChest;

                // Transition to the ready to serve data state
                changeActiveState(readyToServeDataStateName);
            }
        }
        else if(currentState == readyToServeDataStateName)
        {
            // Do nothing for now. Updating the desired pelvis-chest position is done 
            // from the setter function for the desired hand position.
        }


    }


    // BEGIN: State machine state-transitioning functions *********************************************************************************

    public void SetDesiredHandPositionInFrame0AndRecomputeDesiredPelvisChestPos(float reachDirInDegFromRightCcw, float reachHeightSpecifier, 
        Vector3 desiredHandPositionInFrame0)
    {
        // Given the passed in desired hand position in frame 0, 
    }




    // END: State machine state-transitioning functions *********************************************************************************






    // BEGIN: State machine state-transitioning functions *********************************************************************************

    private void changeActiveState(string newState)
    {
        // if we're transitioning to a new state (which is why this function is called).
        if (newState != currentState)
        {
            Debug.Log("Transitioning states from " + currentState + " to " + newState);
            // call the exit function for the current state.
            // Note that we never exit the EndGame state.
            if (currentState == setupStateName)
            {
                ExitWaitingForSetupState();
            }

            //then call the entry function for the new state
            if (currentState == readyToServeDataStateName)
            {
                EnterReadyToServeDataState();
            }
            else
            {
                Debug.Log("Level manager cannot enter a non-existent state");
            }
        }
    }

    

    private void ExitWaitingForSetupState()
    {
        // Do nothing for now
    }



    private void EnterReadyToServeDataState()
    {
        // Change the current state to readyToServeData
        currentState = readyToServeDataStateName;
    }








    // START: VISUALIZING THE POSTURE PREDICTION PIPELINE - TESTING ONLY *********************************************************


    private void VisualizePelvisAndChestPosInFrame0RotatedToUnity(Vector3 pelvisPosFrame0, Vector3 chestPosFrame0)
    {
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
        Vector3 ankleCenterPosFrame0Rendering = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 pelvicCenterPosFrame0Rendering = transformationFrame0ToRenderingFrame0.MultiplyPoint(pelvisPosFrame0);
        Vector3 chestCenterPosFrame0Rendering = transformationFrame0ToRenderingFrame0.MultiplyPoint(chestPosFrame0);

        // Link 1: mid-ankle to mid-pelvis
        anklePelvisLineRenderer.SetPositions(new Vector3[] { ankleCenterPosFrame0Rendering, pelvicCenterPosFrame0Rendering });

        // Link 2: mid-pelvis to mid-chest
        pelvisChestLineRenderer.SetPositions(new Vector3[] { pelvicCenterPosFrame0Rendering, chestCenterPosFrame0Rendering });
    }

    private LineRenderer CreateLineRenderer(Material material, int linkIndex)
    {

        GameObject lineObj = new GameObject("6R_LineRenderer_link" + linkIndex);
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();

        // Basic configuration
        lr.material = material;
        lr.widthMultiplier = 0.05f;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2; // Rounded ends, optional
        lr.startColor = lr.endColor = Color.Lerp(Color.red, Color.blue, linkIndex / 2f);

        return lr;

    }

    // END: VISUALIZING THE POSTURE PREDICTION PIPELINE - TESTING ONLY *********************************************************


    // START: LOADING JOINT ANGLE POLYNOMIALS FROM FILE******************************************************************************
    // Loads the CSV file from the Resources folder and parses the polynomials
    public void LoadPolynomialsCsv()
    {
        if (polynomialsCsv != null)
        {
            LoadPolynomialsFromCSV(polynomialsCsv.text);
            Debug.Log($"Successfully loaded polynomial data from {polynomialsFileName}.csv.");
        }
        else
        {
            Debug.LogError($"CSV file {polynomialsFileName} not found in Resources folder.");
        }
    }

    // Parses CSV text and fills the polynomialData list
    public void LoadPolynomialsFromCSV(string csvText)
    {
        // Temporary dictionary to group polynomials by (reachDirection, reachHeight)
        // Each reach/direction height combination has an associated DirectionHeightPolynomialsOneReachHeightAndDir object.
        Dictionary<(float, float), DirectionHeightPolynomialsOneReachHeightAndDir> lookup = new Dictionary<(float, float), DirectionHeightPolynomialsOneReachHeightAndDir>();

        using (StringReader reader = new StringReader(csvText))
        {
            string line = reader.ReadLine(); // Read and ignore the header row

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue; // Skip any empty lines

                // Split the line by commas
                string[] parts = line.Split(',');

                // Parse the first three fields: direction (degrees), height (meters), and state variable name
                float dir = float.Parse(parts[0]);
                float height = float.Parse(parts[1]);
                string varName = parts[2];

                // Parse the remaining fields as polynomial coefficients
                List<float> coeffs = parts.Skip(3).Select(p => float.Parse(p)).ToList();

                // Key is a tuple of (float, float)
                (float, float) key = (dir, height);

                // If we haven't seen this direction/height combo yet, create a new entry
                if (!lookup.ContainsKey(key))
                {
                    lookup[key] = new DirectionHeightPolynomialsOneReachHeightAndDir
                    {
                        ReachDirectionDeg = dir,
                        ReachHeight = height,
                        StateVariablePolynomials = new Dictionary<string, Polynomial>()
                    };
                }

                // Add the polynomial for this state variable to the corresponding entry
                // For this key = dir/height combination, add the polynomial coefficients to the associated value
                // = the DirectionHeightPolynomialsOneReachHeightAndDir object associated with varName.
                lookup[key].StateVariablePolynomials[varName] = new Polynomial { Coefficients = coeffs };
            }
        }

        // Copy the dictionary values into the main list
        polynomialDataDictionaries = lookup.Values.ToList();
        Debug.Log($"Loaded {polynomialDataDictionaries.Count} direction/height entries from CSV.");
    }


    // AFTER loading the joint/orientation variable polynomials from file, 
    // get unique reach dir and reach height values and print them
    private void IdentifyUniqueReachDirsAndHeights()
    {
        // Build lists of unique directions and heights
        foreach (var entry in polynomialDataDictionaries)
        {
            if (!uniqueDirs.Contains(entry.ReachDirectionDeg))
                uniqueDirs.Add(entry.ReachDirectionDeg);

            if (!uniqueHeights.Contains(entry.ReachHeight))
                uniqueHeights.Add(entry.ReachHeight);
        }

        uniqueDirs.Sort();
        uniqueHeights.Sort();

        // Print sorted unique reach directions
        Debug.Log("Unique Reach Directions (deg): " + string.Join(", ", uniqueDirs));

        // Print sorted unique reach heights
        Debug.Log("Unique Reach Heights (m): " + string.Join(", ", uniqueHeights));
    }

    // END: LOADING JOINT ANGLE POLYNOMIALS FROM FILE******************************************************************************




    // START : Unit testing the polynomial prediction and 6-DOF model forward kinematics (confirm pelvis/chest pos predictions)**************


    public List<FKTestCase> LoadFKTestCases(TextAsset csvAsset)
    {
        var cases = new List<FKTestCase>();

        using (StringReader reader = new StringReader(csvAsset.text))
        {
            string line;
            bool isHeader = true;

            while ((line = reader.ReadLine()) != null)
            {
                if (isHeader)
                {
                    isHeader = false;
                    continue; // skip header
                }

                var cols = line.Split(',');

                FKTestCase testCase = new FKTestCase
                {
                    reachDirDeg = float.Parse(cols[0], CultureInfo.InvariantCulture),
                    reachHeightSpecifier = float.Parse(cols[1], CultureInfo.InvariantCulture),
                    progression = float.Parse(cols[2], CultureInfo.InvariantCulture),

                    pelvisExpected = new Vector3(
                        float.Parse(cols[3], CultureInfo.InvariantCulture),
                        float.Parse(cols[4], CultureInfo.InvariantCulture),
                        float.Parse(cols[5], CultureInfo.InvariantCulture)
                    ),

                    chestExpected = new Vector3(
                        float.Parse(cols[6], CultureInfo.InvariantCulture),
                        float.Parse(cols[7], CultureInfo.InvariantCulture),
                        float.Parse(cols[8], CultureInfo.InvariantCulture)
                    ),

                    lAnklePelvis = float.Parse(cols[9], CultureInfo.InvariantCulture),
                    lPelvisChest = float.Parse(cols[10], CultureInfo.InvariantCulture),
                    hPelvis = float.Parse(cols[11], CultureInfo.InvariantCulture),
                    hChest = float.Parse(cols[12], CultureInfo.InvariantCulture),
                    hShoulder = float.Parse(cols[13], CultureInfo.InvariantCulture),
                    hEye = float.Parse(cols[14], CultureInfo.InvariantCulture)
                };

                cases.Add(testCase);
            }
        }

        return cases;
    }


    void RunPolynomialInterpolatorAndForwardKinematicsUnitTest()
    {
        List<FKTestCase> testCases = LoadFKTestCases(polynomialAndForwardKinematicsUnitTestCsv);
        int countCorrectTests = 0;
        int totalTests = testCases.Count;

        foreach (var test in testCases)
        {
            // Set the subject-specific lengths
            SetSubjectKinematics(test.lAnklePelvis, test.lPelvisChest);

            // Call the function that does polynomial interpolation, 6-DOF FK, and outputs the predicted pelvis and chest positions.
            (Vector3 pelvisPosInFrame0, Vector3 chestPosInFrame0) = PredictPelvisAndChestPosInFrame0(
                test.reachDirDeg, test.reachHeightSpecifier, test.progression);

            // Compare to expected values from MATLAB
            bool pelvisMatch = ApproximatelyEqual(pelvisPosInFrame0, test.pelvisExpected);
            bool chestMatch = ApproximatelyEqual(chestPosInFrame0, test.chestExpected);

            if (!pelvisMatch || !chestMatch)
            {
                Debug.LogWarning($"❌ Mismatch at Dir={test.reachDirDeg}, Height={test.reachHeightSpecifier}, Prog={test.progression}");

                if (!pelvisMatch)
                {
                    Debug.Log($"  Pelvis mismatch:\n" +
                              $"    Expected: {test.pelvisExpected:F6}\n" +
                              $"    Actual:   {pelvisPosInFrame0:F6}\n" +
                              $"    Δ = {(pelvisPosInFrame0 - test.pelvisExpected).magnitude:F6}");
                }

                if (!chestMatch)
                {
                    Debug.Log($"  Chest mismatch:\n" +
                              $"    Expected: {test.chestExpected:F6}\n" +
                              $"    Actual:   {chestPosInFrame0:F6}\n" +
                              $"    Δ = {(chestPosInFrame0 - test.chestExpected).magnitude:F6}");
                }
            }
            else
            {
                countCorrectTests += 1;
            }
        }

        Debug.Log($"✅ FK Unit Test Results: {countCorrectTests}/{totalTests} matches with MATLAB.");
    }

    private bool ApproximatelyEqual(Vector3 a, Vector3 b, float tolerance = 1e-4f)
    {
        return Vector3.Distance(a, b) < tolerance;
    }

    // END : Unit testing the polynomial prediction and 6-DOF model forward kinematics (confirm pelvis/chest pos predictions)**************

    // START: Polynomial prediction of pelvis and chest position ****************************************************************************


    // Set the subject-specific parameters for the 6-DOF position prediction model. 
    private void SetSubjectKinematics(float lengthAnklePelvisInMeters, float lengthPelvisChestInMeters)
    {
        Debug.Log("Setting ankle-pelvis and pelvis-chest lengths to: (" + lengthAnklePelvisInMeters + ", " + lengthPelvisChestInMeters + ")");
        lengthAnkleToPelvisInMeters = lengthAnklePelvisInMeters;
        lengthPelvisToChestInMeters = lengthPelvisChestInMeters;
    }

    // Public top-level function to predict pelvis and chest states
    public (PoseEstimate pelvisPose, PoseEstimate chestPose) PredictPelvisAndChestPositionAndOrientation(
        float reachDirectionDeg, float reachHeight, float progression)
    {
        // Map continuous reachHeight (in meters) to a normalized height specifier in [0, 3]
        float reachHeightSpecifier = ConvertReachHeightToSpecifier(reachHeight);

        // Predict positions
        (Vector3 pelvisPos, Vector3 chestPos) = PredictPelvisAndChestPosInFrame0(reachDirectionDeg, reachHeight, progression);

        // Predict orientations
        (Dictionary<string, float> pelvisOrient, Dictionary<string, float> chestOrient) = PredictPelvisAndChestOrientation(
            reachDirectionDeg, reachHeight, progression);

        // Return both pelvis and chest "poses" (position, orientation vars) as a tuple of the PoseEstimate class
        return (new PoseEstimate(pelvisPos, pelvisOrient),
                new PoseEstimate(chestPos, chestOrient));
    }


    // This function converts a height of the hand (in meters) to a height specifier in [0, 3]. 
    // NOTE: relies on heightLevelsInMeters being set for the specific subject during startup.
    private float ConvertReachHeightToSpecifier(float reachHeight)
    {
        // Linear interpolation between bounding height levels
        for (int i = 0; i < heightLevelsInMeters.Length - 1; i++)
        {
            if (reachHeight <= heightLevelsInMeters[i + 1])
            {
                float hLow = heightLevelsInMeters[i];
                float hHigh = heightLevelsInMeters[i + 1];
                float t = Mathf.InverseLerp(hLow, hHigh, reachHeight);
                return i + t;  // result is a float ∈ [0, 3]
            }
        }

        // Clamp above eye level (we only reach this code if the height is above eye-level)
        return 3.0f;
    }


    // Predict pelvis and chest position using reach height, dir, progression to get expected joint variable values. 
    // After we know the expected joint variable values, we do FK on our 6-DOF model.
    private (Vector3 pelvisPosInFrame0, Vector3 chestPosInFrame0 ) PredictPelvisAndChestPosInFrame0(
        float reachDirectionDeg, float reachHeight, float progression)
    {
        // Init storage for the predicted 6R model joint variables
        float[] jointValues = new float[jointVariables.Length];

        for (int i = 0; i < jointVariables.Length; i++)
        {
            jointValues[i] = PredictStateVariable(jointVariables[i], reachDirectionDeg, reachHeight, progression);
        }

        // ✅ Print the raw joint predictions for debugging
        Debug.Log($"[Predict] Dir={reachDirectionDeg}, Height={reachHeight}, Prog={progression} → " +
                  $"JointVars: [{string.Join(", ", jointValues.Select(v => v.ToString("F6")))}]");

        // Modify the joint values that are passed into the FK! 
        // We need to add (3/2)* pi to theta2, theta4, and theta5 in the 6-DOF (RRP-RRP) model. 
        float[] jointValuesDhAdjusted = jointValues;
        jointValuesDhAdjusted[1] += 1.5f * Mathf.PI; // theta2
        jointValuesDhAdjusted[3] += 1.5f * Mathf.PI; // theta4
        jointValuesDhAdjusted[4] += 1.5f * Mathf.PI; // theta5

        // Multiply d3 and d6 by the subject-specific neutral/resting lengths 
        jointValuesDhAdjusted[2] = jointValuesDhAdjusted[2] * lengthAnkleToPelvisInMeters;
        jointValuesDhAdjusted[5] = jointValuesDhAdjusted[5] * lengthPelvisToChestInMeters;


        // Use forward kinematics 6-DOF model to get pelvis and chest positions
        return SixDofModelForwardKinematics(jointValuesDhAdjusted);
    }

    // Stub for your 6-DOF forward kinematics model
    private (Vector3 pelvisPos, Vector3 chestPos) SixDofModelForwardKinematics(float[] jointValuesDhAdjusted)
    {
        // Separate out joint variables for readability 
        float theta1 = jointValuesDhAdjusted[0];
        float theta2 = jointValuesDhAdjusted[1];
        float d3 = jointValuesDhAdjusted[2];
        float theta4 = jointValuesDhAdjusted[3];
        float theta5 = jointValuesDhAdjusted[4];
        float d6 = jointValuesDhAdjusted[5];

        // NOTE these are positions, not just containers for joint values. FIX.
        Vector3 pelvisPos = new Vector3(
            -d3 * Mathf.Cos(theta1) * Mathf.Sin(theta2),
            -d3 * Mathf.Sin(theta1) * Mathf.Sin(theta2),
             d3 * Mathf.Cos(theta2)
        );

        Vector3 chestPos = new Vector3(
            d6 * (Mathf.Sin(theta5) * (Mathf.Cos(theta4) * Mathf.Sin(theta1) - Mathf.Cos(theta1) * Mathf.Sin(theta2) * Mathf.Sin(theta4)) -
                  Mathf.Cos(theta1) * Mathf.Cos(theta2) * Mathf.Cos(theta5))
            - d3 * Mathf.Cos(theta1) * Mathf.Sin(theta2),

           -d6 * (Mathf.Sin(theta5) * (Mathf.Cos(theta1) * Mathf.Cos(theta4) + Mathf.Sin(theta1) * Mathf.Sin(theta2) * Mathf.Sin(theta4)) +
                  Mathf.Cos(theta2) * Mathf.Cos(theta5) * Mathf.Sin(theta1))
            - d3 * Mathf.Sin(theta1) * Mathf.Sin(theta2),

            d3 * Mathf.Cos(theta2) -
            d6 * (Mathf.Cos(theta5) * Mathf.Sin(theta2) - Mathf.Cos(theta2) * Mathf.Sin(theta4) * Mathf.Sin(theta5))
        );

        return (pelvisPos, chestPos);
    }

    // Predict pelvis and chest orientations based on reach progression, height, and direction
    private (Dictionary<string, float> pelvisDict, Dictionary<string, float> chestDict) PredictPelvisAndChestOrientation(
        float reachDirectionDeg, float reachHeight, float progression)
    {
        Dictionary<string, float> pelvisDict = new Dictionary<string, float>();
        Dictionary<string, float> chestDict = new Dictionary<string, float>();

        // For each orientation variable (specified in instance variable)
        foreach (var varName in orientationVariables)
        {
            // Do the polynomial interpolation given the current reach dir/height/progression
            float value = PredictStateVariable(varName, reachDirectionDeg, reachHeight, progression);

            // Assign variables into appropriate dictionary
            if (varName.StartsWith("pelvic"))
                pelvisDict[varName] = value;
            else if (varName.StartsWith("trunk"))
                chestDict[varName] = value;
            else
                Debug.LogWarning($"Unexpected orientation variable name: {varName}");
        }

        return (pelvisDict, chestDict);
    }


    private float PredictStateVariable(string stateVarName, float reachDirectionDeg, float reachHeight, float progression)
    {
        // 1. Get 4 surrounding/bounding entries
        DirectionHeightPolynomialsOneReachHeightAndDir[] neighborPolynomials = GetBoundingEntries(reachDirectionDeg, reachHeight);

        // 2. Evaluate polynomials
        float[] predictions = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (neighborPolynomials[i] == null || !neighborPolynomials[i].StateVariablePolynomials.TryGetValue(stateVarName, out Polynomial poly))
            {
                Debug.LogError($"Missing data for state variable {stateVarName} at index {i}");
                return float.NaN;
            }

            predictions[i] = poly.Evaluate(progression);
            if (float.IsNaN(predictions[i]))
            {
                Debug.LogError($"NaN result from poly.Evaluate at index {i}: Dir={neighborPolynomials[i].ReachDirectionDeg}, " +
                    $"Height={neighborPolynomials[i].ReachHeight}, Prog={progression}, Coeffs=[{string.Join(", ", poly.Coefficients.Select(c => c.ToString("F6")))}]");

            }
        }

        float hL = neighborPolynomials[0].ReachHeight;
        float hH = neighborPolynomials[1].ReachHeight;
        float dL = neighborPolynomials[0].ReachDirectionDeg;
        float dH = neighborPolynomials[2].ReachDirectionDeg;

        // ✅ Check bounds before calling InverseLerp
        if (Mathf.Approximately(hL, hH))
        {
            Debug.LogError($"❌ ReachHeight bounds equal (hL == hH == {hL}) — cannot interpolate");
            return float.NaN;
        }
        if (Mathf.Approximately(dL, dH))
        {
            Debug.LogError($"❌ ReachDirection bounds equal (dL == dH == {dL}) — cannot interpolate");
            return float.NaN;
        }

        // 3. Interpolate height-wise
        float tHeight = Mathf.InverseLerp(hL, hH, reachHeight);
        float interpHeightLow = Mathf.Lerp(predictions[0], predictions[1], tHeight);
        float interpHeightHigh = Mathf.Lerp(predictions[2], predictions[3], tHeight);

        // 4. Interpolate direction-wise
        float tDir = Mathf.InverseLerp(dL, dH, reachDirectionDeg);
        float finalValue = Mathf.Lerp(interpHeightLow, interpHeightHigh, tDir);

        if (float.IsNaN(finalValue))
        {
            Debug.LogError($"❌ Final interpolated value is NaN for {stateVarName}: tDir={tDir}, interpLow={interpHeightLow}, interpHigh={interpHeightHigh}");
        }

        return finalValue;
    }



   /* // This is the primary helper function for reaching-based control of chest/pelvis pos. using polynomial fits to get
    // joint/orientation variables. 
    // Predicts a "state variable", i.e., a joint variable or orientation variable, using the neighboring polynomial fits 
    // from the healthy subjects study. 
    // This function DOES do interpolation!
    private float PredictStateVariable(string stateVarName, float reachDirectionDeg, float reachHeight, float progression)
    {
        // 1. Get 4 surrounding/bounding entries
        // ORDER: 1 = dLow,hLow; 2 = dLow, hHigh; 3 = dHigh, hLow; 4 = dHigh, hHigh
        DirectionHeightPolynomialsOneReachHeightAndDir[] neighborPolynomials = GetBoundingEntries(reachDirectionDeg, reachHeight);

        // 2. Evaluate polynomials
        float[] predictions = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (neighborPolynomials[i] == null || !neighborPolynomials[i].StateVariablePolynomials.TryGetValue(stateVarName, out Polynomial poly))
            {
                Debug.LogError($"Missing data for state variable {stateVarName} at index {i}");
                return 0f;
            }
            predictions[i] = poly.Evaluate(progression);
        }

        // Get high and low values for the reach dir and height specifier
        float hL = neighborPolynomials[0].ReachHeight, hH = neighborPolynomials[1].ReachHeight; // height low and high values
        float dL = neighborPolynomials[0].ReachDirectionDeg, dH = neighborPolynomials[2].ReachDirectionDeg; // direction low and high values

        // Conduct height interpolation
        float interpHeightLow = Mathf.Lerp(predictions[0], predictions[1], Mathf.InverseLerp(hL, hH, reachHeight));
        float interpHeightHigh = Mathf.Lerp(predictions[2], predictions[3], Mathf.InverseLerp(hL, hH, reachHeight));

        // Conduct direction interpolation and return
        return Mathf.Lerp(interpHeightLow, interpHeightHigh, Mathf.InverseLerp(dL, dH, reachDirectionDeg));
    }*/

    // Find the 4 bounding entries for bilinear interpolation (dirLow-hLow, dirLow-hHigh, dirHigh-hLow, dirHigh-hHigh)
    private DirectionHeightPolynomialsOneReachHeightAndDir[] GetBoundingEntries(float dirDeg, float height)
    {
        // Find surrounding directions and heights
        float dirLow = GetLowerBound(uniqueDirs, dirDeg);
        float dirHigh = GetUpperBound(uniqueDirs, dirDeg);
        float heightLow = GetLowerBound(uniqueHeights, height);
        float heightHigh = GetUpperBound(uniqueHeights, height);

        // Retrieve the four surrounding polynomial entries
        DirectionHeightPolynomialsOneReachHeightAndDir[] neighbors = new DirectionHeightPolynomialsOneReachHeightAndDir[]
        {
        FindEntry(dirLow, heightLow),
        FindEntry(dirLow, heightHigh),
        FindEntry(dirHigh, heightLow),
        FindEntry(dirHigh, heightHigh),
        };

        // SAFEGUARD: Check for missing neighbors
        for (int i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i] == null)
            {
                Debug.LogWarning($"Missing neighbor polynomial for interpolation at (Direction: {dirDeg} deg, Height: {height} m). " +
                                 $"Missing neighbor {i}: " +
                                 $"dir={(i < 2 ? dirLow : dirHigh)}, height={(i % 2 == 0 ? heightLow : heightHigh)}.");
            }
        }

        return neighbors;
    }

    private float GetLowerBound(List<float> sortedList, float value)
    {
        // Add the largest value less than the current value
        float lower = sortedList[0];
        foreach (var val in sortedList)
            if (val < value)
            {
                lower = val;
            }
        return lower;
    }

    private float GetUpperBound(List<float> sortedList, float value)
    {
        float upper = sortedList[sortedList.Count - 1];
        // Find the first value in the sorted list greater than the value, then break out of the search loop
        foreach (var val in sortedList)
            if (val > value)
            {
                upper = val;
                break;
            }
        return upper;
    }

    private DirectionHeightPolynomialsOneReachHeightAndDir FindEntry(float dirDeg, float height)
    {
        // Note: the Approximately() function is a slightly less strict version of equality ==, allowing for some floating point error.
        return polynomialDataDictionaries.Find(entry => Mathf.Approximately(entry.ReachDirectionDeg, dirDeg) &&
                                            Mathf.Approximately(entry.ReachHeight, height));
    }

    // END: Polynomial prediction of pelvis and chest position ****************************************************************************





    // START: Reach-limit related code ****************************************************************************************************

    public float InterpolateReachLimitMeters(
    float reachDir, float reachHeightSpecifier,
    Dictionary<string, float> dirHeightLimitsDictionary,
    List<float> availableDirs, List<float> availableHeights,
    float subjectHeight)
    {
        // Find bounding values
        float dirLow = availableDirs.Where(d => d <= reachDir).Max(); // max value less than current reach direction
        float dirHigh = availableDirs.Where(d => d >= reachDir).Min(); // min value greater than current reach direction
        float hLow = availableHeights.Where(h => h <= reachHeightSpecifier).Max(); // max value less than current height specifier
        float hHigh = availableHeights.Where(h => h >= reachHeightSpecifier).Min(); // min value greater than current height specifier

        // Keys
        string[] keys = new string[]
        {
        $"{dirLow}_{(int)hLow}",
        $"{dirLow}_{(int)hHigh}",
        $"{dirHigh}_{(int)hLow}",
        $"{dirHigh}_{(int)hHigh}"
        };

        float[] normedLimits = keys.Select(k => dirHeightLimitsDictionary[k]).ToArray();

        // Bilinear interpolation weights
        float tH = (reachHeightSpecifier - hLow) / Mathf.Max((hHigh - hLow), 0.0001f); // height interpolation
        float tD = (reachDir - dirLow) / Mathf.Max((dirHigh - dirLow), 0.0001f); // direction interpolation

        float interpLowDir = (1 - tH) * normedLimits[0] + tH * normedLimits[1];
        float interpHighDir = (1 - tH) * normedLimits[2] + tH * normedLimits[3];
        float interpNormedLimit = (1 - tD) * interpLowDir + tD * interpHighDir;

        return interpNormedLimit * subjectHeight;
    }


    /// <summary>
    /// Computes the reach progression (0 to 1) of the current hand position
    /// along a straight-line trajectory from the hand start (vertically above frame 0 origin at height of reach)
    /// to desired end (estimated reach distance along direction vector at height of reach).
    /// </summary>
    public float ComputeReachProgress(Vector3 currentHandPos, Vector3 startHandPos, Vector3 endHandPos)
    {
        // Vector representing the total planned reach direction
        Vector3 direction = endHandPos - startHandPos;
        float desiredHandTravelLength = direction.magnitude;

        // Prevent division by zero for very short or zero-length vectors
        if (desiredHandTravelLength < 1e-5f)
            return 0f;

        // Vector from start to current hand position
        Vector3 toCurrent = currentHandPos - startHandPos;

        // Project the current hand vector onto the reach direction
        float progressionDistanceProjected = Vector3.Dot(toCurrent, direction.normalized);

        // Normalize the projected distance to get progression ∈ [0, 1]
        return Mathf.Clamp01(progressionDistanceProjected / desiredHandTravelLength);
    }


    // END: Reach-limit related code ****************************************************************************************************



}