using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Unity.Profiling;

using System;
using System.Threading;

/// <summary>
/// Main controller for the cable-driven robot.
/// This class centralizes the robot's state management, coordinating trackers,
/// force plates, physics calculations, and communication with LabVIEW.
/// </summary>
public class RobotController : MonoBehaviour
{
    static readonly ProfilerCounterValue<long> s_WorkloadNs = new(RobotProfiler.Workloads, "Controller Workload", ProfilerMarkerDataUnit.TimeNanoseconds);
    static readonly ProfilerCounterValue<long> s_IntervalNs = new(RobotProfiler.Intervals, "Controller Execution Interval", ProfilerMarkerDataUnit.TimeNanoseconds);

    [Header("Module References")]
    [Tooltip("The TrackerManager instance that provides tracker data.")]
    [SerializeField] private TrackerManager trackerManager;
    [Tooltip("The ForcePlateManager instance for reading force plate data.")]
    [SerializeField] private ForcePlateManager forcePlateManager;
    [Tooltip("The LabviewTcpCommunicator instance for sending motor commands.")]
    [SerializeField] private LabviewTcpCommunicator tcpCommunicator;

    [Header("Visualization")]
    [Tooltip("Handles tracker/camera visuals. Keeps RobotController logic-only.")]
    [SerializeField] private RobotVisualizer visualizer;

    [Header("Control Settings")]
    [Tooltip("Enable or disable sending commands to LabVIEW.")]
    [SerializeField] private bool isLabviewControlEnabled = true;
    private bool isForcePlateEnabled = true;

    public enum CONTROL_MODE { OFF, TRANSPARENT, MPC, IMPEDANCE }
    [SerializeField] private volatile CONTROL_MODE currentControlMode = CONTROL_MODE.OFF;

    [Header("Robot Geometry Configuration")]
    [Tooltip("Number of cables in the system. We currently support 8 or 4 cables")]
    [SerializeField] private int numCables = 8;
    [Tooltip("Measured thickness of the chest in the anterior-posterior direction [m].")]
    [SerializeField] private float chestAPDistance = 0.2f;
    [Tooltip("Measured width of the chest in the medial-lateral direction [m].")]
    [SerializeField] private float chestMLDistance = 0.3f;
    [Tooltip("User body mass [kg].")]
    [SerializeField] private float userMass = 70.0f;
    [Tooltip("User shoulder width [m].")]
    [SerializeField] private float userShoulderWidth = 0.4f;
    [Tooltip("User height (feet to head) [m].")]
    [SerializeField] private float userHeight = 1.5f;

    [Header("Data Logging")]
    [Tooltip("Check this to record data to RAM. It will be saved to disk on Stop.")]
    [SerializeField] private volatile bool isLogging = false;
    [Tooltip("Name of the experiment session for the log file.")]
    [SerializeField] private string sessionName = "Experiment";

    [Header("Trajectory Control")]
    [Tooltip("Change this during runtime to switch trajectory modes.")]
    [SerializeField] private TrajectoryMode TrajMode = TrajectoryMode.STABLE_STANDING;

    private RobUSTDescription robotDescription;
    private DataLogger dataLogger;

    // Controllers
    private MPCSolver mpcSolver;
    private ImpedanceController impedanceController;
    private CableTensionPlanner tensionPlanner;
    private TrajectoryPlanner trajectoryPlanner;
    private long trajectoryIndex = 0;
    

    // Static frame reference captured only at startup to prevent drift
    private TrackerData robot_frame_tracker;
    private Thread controllerThread;
    private volatile bool isRunning = false;
    private volatile bool isTrajectoryActive = false;

    // TEMP Perturbation Test
    private volatile bool testPulseRequested = false;
    private volatile bool testPulseActive = false;

    private void Start()
    {
        using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
        {
            p.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
        }

        if (!ValidateModules())
        {
            enabled = false; 
            return;
        }

        if (chestAPDistance <= 0 || chestMLDistance <= 0)
        {
            Debug.LogError("Chest dimensions must be positive values.", this);
            enabled = false;
            return;
        }

        // Create RobUST description (single allocation at startup)
        robotDescription = RobUSTDescription.Create(numCables, chestAPDistance, chestMLDistance, 
                                                    userMass, userShoulderWidth, userHeight);
        Debug.Log($"RobUST description created: {numCables} cables, AP={chestAPDistance}m, ML={chestMLDistance}m");

        // Initialize Modules
        tcpCommunicator.Initialize();
        if (!visualizer.Initialize(robotDescription))
        {
            Debug.LogError("Failed to initialize RobotVisualizer.", this);
            enabled = false;
            return;
        }
        if (!trackerManager.Initialize())
        {
            Debug.LogError("Failed to initialize TrackerManager.", this);
            enabled = false;
            return;
        }
        if (!forcePlateManager.Initialize(robotDescription))
        {
            Debug.LogWarning("Failed to initialize ForcePlateManager.", this);
            isForcePlateEnabled = false;
        }
        
        if (isLabviewControlEnabled)
        {
            tcpCommunicator.ConnectToServer();
            tcpCommunicator.SetClosedLoopControl();
        }
        dataLogger = new DataLogger(60, 100);

        System.Threading.Thread.Sleep(500); // allow tracker thread to go live
        trackerManager.GetFrameTrackerData(out robot_frame_tracker); // import Capture static frame at startup 

        // Finally Initialize Controller modules and begin control thread
        mpcSolver = new MPCSolver(robotDescription, 0.05, 10);
        impedanceController = new ImpedanceController();
        tensionPlanner = new CableTensionPlanner(robotDescription);
        trajectoryPlanner = new TrajectoryPlanner();
        
        controllerThread = new Thread(controlLoop)
        {
            Name = "Robot Controller Main",
            IsBackground = true,
            Priority = System.Threading.ThreadPriority.AboveNormal
        };

        isRunning = true;
        controllerThread.Start();
    }

    private void Update()
    {
        if (trajectoryPlanner.CurrentMode != TrajMode)
            trajectoryPlanner.SetMode(TrajMode);

        if (Keyboard.current == null) return;
        if (Keyboard.current.spaceKey.wasPressedThisFrame) { isTrajectoryActive = !isTrajectoryActive; }
        if (Keyboard.current.oKey.wasPressedThisFrame) { currentControlMode = CONTROL_MODE.OFF; trajectoryIndex = 0; isTrajectoryActive = false; }
        if (Keyboard.current.tKey.wasPressedThisFrame) { currentControlMode = CONTROL_MODE.TRANSPARENT; }
        if (Keyboard.current.mKey.wasPressedThisFrame) { if (isForcePlateEnabled) currentControlMode = CONTROL_MODE.MPC; }
        if (Keyboard.current.iKey.wasPressedThisFrame) { if (robotDescription.NumCables == 8) currentControlMode = CONTROL_MODE.IMPEDANCE; }
        // Press 'P' to trigger a short tension pulse on motor 12 (hardcoded test)
        if (Keyboard.current.pKey.wasPressedThisFrame) { testPulseRequested = true; }
    }

    /// <summary>
    ///  This control loop is the main "driver" of one or more `Controller` instances. 
    ///  It needs to grab the latest tracker data, update visuals, compute cable tensions, and send commands to LabVIEW.
    ///  a `Controller` functions more like a `Control Policy/Solver` here, with RobotController managing the data flow.
    /// </summary>
    private void controlLoop()
    {
        double ctrl_freq = 100.0;
        int mpcStride = (int)(mpcSolver.dt * ctrl_freq);

        Span<double> motor_tension_command = stackalloc double[14];
        double[] solver_tensions = new double[robotDescription.NumCables];
        
        double4x4 framePose = ToDouble4x4(robot_frame_tracker.PoseMatrix);
        double4x4 frameInv = math.fastinverse(framePose);
        SensorFilter filter_10Hz = new SensorFilter(ctrl_freq, 10.0);
        Span<RBState> Xref_horizon = stackalloc RBState[10];
        Span<RBState> mpc_results = stackalloc RBState[10];

        double system_frequency = System.Diagnostics.Stopwatch.Frequency;
        double ticksToNs = 1_000_000_000.0 / system_frequency;
        long intervalTicks = (long)(system_frequency / ctrl_freq);
        long nextTargetTime = System.Diagnostics.Stopwatch.GetTimestamp() + intervalTicks;
        long lastLoopTick = System.Diagnostics.Stopwatch.GetTimestamp();
        long testPulseEndTick = 0;
        while (isRunning)
        {
            long loopStartTick = System.Diagnostics.Stopwatch.GetTimestamp();
            s_IntervalNs.Value = (long)((loopStartTick - lastLoopTick) * ticksToNs);
            lastLoopTick = loopStartTick;

            // perturbation pulse logic
            if (testPulseRequested)
            {
                testPulseRequested = false;
                testPulseActive = true;
                testPulseEndTick = loopStartTick + (long)(0.3 * system_frequency);
            }

            trackerManager.GetEndEffectorTrackerData(out TrackerData rawEndEffectorData);
            trackerManager.GetCoMTrackerData(out TrackerData rawComData);
            double4x4 eePose_RF = math.mul(frameInv, ToDouble4x4(rawEndEffectorData.PoseMatrix));
            double4x4 comPose_RF = math.mul(frameInv, ToDouble4x4(rawComData.PoseMatrix));
            // FP already in robot frame
            forcePlateManager.GetForcePlateData(0, out ForcePlateData fp0);
            forcePlateManager.GetForcePlateData(1, out ForcePlateData fp1);
            double3 netForce = fp0.Force + fp1.Force;
            double3 netCoP = double3.zero;
            if (math.abs(netForce.z) > 1e-3) // Handle division by zero
                netCoP = (fp0.CoP * fp0.Force.z + fp1.CoP * fp1.Force.z) / netForce.z;
            ForcePlateData netFPData = new ForcePlateData(netForce, netCoP);
            
            filter_10Hz.Update(comPose_RF, eePose_RF, netFPData);

            int currentStride = (currentControlMode == CONTROL_MODE.MPC) ? mpcStride : 1;
            trajectoryPlanner.FillHorizon(trajectoryIndex, Xref_horizon, currentStride);

            switch (currentControlMode)
            {
                case CONTROL_MODE.OFF:
                    motor_tension_command.Clear();
                    Array.Clear(solver_tensions, 0, solver_tensions.Length);
                    break;
                case CONTROL_MODE.TRANSPARENT:
                    switch (robotDescription.NumCables)
                    {
                        case 8:
                            Wrench zeroWrench = new Wrench(double3.zero, double3.zero);
                            solver_tensions = tensionPlanner.CalculateTensions(eePose_RF, zeroWrench);
                            MapTensionsToMotors(solver_tensions, motor_tension_command);
                            break;
                        case 4:
                            solver_tensions[0] = 10.0; solver_tensions[1] = 10.0;
                            solver_tensions[2] = 10.0; solver_tensions[3] = 10.0;
                            MapTensionsToMotors(solver_tensions, motor_tension_command);
                            break;
                    }
                    visualizer.PushGoalTrajectory(Xref_horizon.Slice(0, 1));
                    break;
                case CONTROL_MODE.MPC:
                    ForcePlateData filteredFPData = new ForcePlateData(filter_10Hz.FilteredGRF, filter_10Hz.FilteredCoP);
                    
                    mpcSolver.UpdateReferenceTrajectory(Xref_horizon, 0); // Use lookahead buffer directly
                    mpcSolver.UpdateState(eePose_RF, comPose_RF, filter_10Hz.CoMLinearVelocity, filter_10Hz.CoMAngularVelocity, filteredFPData);
                    solver_tensions = mpcSolver.computeNextControl();
                    MapTensionsToMotors(solver_tensions, motor_tension_command);
                    
                    // --- Visualization: MPC Trajectory ---
                    mpcSolver.ComputeOptimalTrajectory(mpc_results);
                    visualizer.PushMpcTrajectory(mpc_results);
                    visualizer.PushGoalTrajectory(Xref_horizon);
                    break;
                case CONTROL_MODE.IMPEDANCE:
                    // Moved kinematics logic to TrajectoryPlanner
                    RBState target = TrajectoryPlanner.ComputeEERefFromCoMRef(Xref_horizon[0], comPose_RF, eePose_RF);

                    impedanceController.UpdateState(eePose_RF, filter_10Hz.EELinearVelocity, filter_10Hz.EEAngularVelocity, target);
                    Wrench goalWrench = impedanceController.computeNextControl();
                    solver_tensions = tensionPlanner.CalculateTensions(eePose_RF, goalWrench);
                    MapTensionsToMotors(solver_tensions, motor_tension_command);
                    visualizer.PushGoalTrajectory(Xref_horizon.Slice(0, 1));
                    break;
            }

            // perturbation logic
            int motorIndex = 11;
            long nowTick = System.Diagnostics.Stopwatch.GetTimestamp();
            if (testPulseActive && nowTick >= testPulseEndTick) testPulseActive = false;
            double setTension = testPulseActive ? 150 : 10;
            motor_tension_command[motorIndex] = setTension;

            if (isTrajectoryActive) trajectoryIndex++;

            tcpCommunicator.UpdateTensionSetpoint(motor_tension_command);
            visualizer.PushState(comPose_RF, eePose_RF, fp0, fp1);
            if (isLogging)
            {
                Wrench goalWrench = tensionPlanner.CalculateResultantWrench(eePose_RF, solver_tensions);
                dataLogger.Log(loopStartTick, comPose_RF, eePose_RF, fp0, fp1, goalWrench, Xref_horizon[0]);
            }

            s_WorkloadNs.Value = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - loopStartTick) * ticksToNs);
            while (System.Diagnostics.Stopwatch.GetTimestamp() < nextTargetTime) { } // BURN wait

            nextTargetTime += intervalTicks;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now > nextTargetTime) nextTargetTime = now + intervalTicks; // drift correction
        }
    }


    // Validates that all required module references are assigned in the Inspector.
    // returns True if all modules are assigned, false otherwise.
    private bool ValidateModules()
    {
        bool allValid = true;
        if (trackerManager == null) { Debug.LogError("Module not assigned in Inspector: trackerManager", this); allValid = false; }
        if (forcePlateManager == null) { Debug.LogError("Module not assigned in Inspector: forcePlateManager", this); allValid = false; }
        if (tcpCommunicator == null) { Debug.LogError("Module not assigned in Inspector: tcpCommunicator", this); allValid = false; }
        if (visualizer == null) { Debug.LogError("Visualizer not assigned in Inspector: visualizer", this); allValid = false; }

        return allValid;
    }

    private void OnDestroy()
    {
        // Clean shutdown of threaded components.
        // TrackerManager handles its own shutdown via its OnDestroy method.
        isRunning = false;
        tcpCommunicator?.Disconnect();
        if (dataLogger != null && dataLogger.FrameCount > 0)
            dataLogger.WriteToDisk(sessionName);
    }


    /// <summary>
    /// Maps solver tensions to the 14-motor driver array using the active configuration.
    /// </summary>
    private void MapTensionsToMotors(double[] solverResult, Span<double> output)
    {
        output.Clear();
        int count = robotDescription.SolverToMotorMap.Length;
        for (int i = 0; i < count; i++)
        {
            int motorIndex = robotDescription.SolverToMotorMap[i];
            output[motorIndex] = solverResult[i];
        }
    }

    // Local helper
    private static double4x4 ToDouble4x4(in Matrix4x4 m)
    {
        return new double4x4(
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33
        );
    }

}
