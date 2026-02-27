using Unity.Mathematics;
using System;
using System.Collections.Generic;

public enum TrajectoryMode
{
    STABLE_STANDING,
    LUNGE,
    HOURGLASS
}

public class TrajectoryPlanner
{
    // We can keep different pre-calculated buffers for different modes
    private RBState[] Xref_stable;
    private RBState[] Xref_lunge;
    private RBState[] Xref_hourglass;
    
    // Pointer to the currently active trajectory
    private RBState[] curr_Xref;
    private TrajectoryMode currentMode;

    public TrajectoryMode CurrentMode => currentMode;

    public TrajectoryPlanner()
    {

        // 1. Initialize Stable Standing (Permanent Buffer)
        Xref_stable = new RBState[1]; // fill horizon handles 
        RBState staticPoint = new RBState(
            new double3(0.2, 0.825, 0.01),
            new double3(0, 0, -math.PI/2), 
            new double3(0, 0, 0), 
            new double3(0, 0, 0)
        );
        for (int i = 0; i < Xref_stable.Length; i++) Xref_stable[i] = staticPoint;

        // 2. Initialize lunge
        Span<RBState> waypoints_lunge = stackalloc RBState[]
        {
            staticPoint,
            staticPoint,
            new RBState(new double3(0.58, 0.68, staticPoint.p.z), staticPoint.th, double3.zero, double3.zero),
            new RBState(staticPoint.p + new double3(0,0,-0.2), staticPoint.th, double3.zero, double3.zero),
            new RBState(staticPoint.p + new double3(0,0,-0.2), staticPoint.th, double3.zero, double3.zero),
            new RBState(staticPoint.p + new double3(0,0,-0.2), staticPoint.th, double3.zero, double3.zero),
            new RBState(new double3(0.58, 0.68, staticPoint.p.z), staticPoint.th, double3.zero, double3.zero),
            staticPoint // Back to center
        };
        Xref_lunge = InitializeLinearTrajectory(waypoints_lunge, 2.0, 1.0, 100.0);
        
        // 3. Initialize Hourglass (Skeleton)
        Span<RBState> waypoints_hourglass = stackalloc RBState[]
        {
            staticPoint,
            staticPoint,
            new RBState(new double3(0.58, 0.68, staticPoint.p.z), staticPoint.th, double3.zero, double3.zero), // back left
            new RBState(new double3(0.58, 1.02, staticPoint.p.z), staticPoint.th, double3.zero, double3.zero), // back right
            new RBState(new double3(-0.07, 0.68, staticPoint.p.z), staticPoint.th, double3.zero, double3.zero), // front left
            new RBState(new double3(-0.07, 1.02, staticPoint.p.z), staticPoint.th, double3.zero, double3.zero), // front right
            staticPoint // Back to center
        };
        // Adjust moveDuration and pauseDuration as needed for the hourglass trajectory
        Xref_hourglass = InitializeLinearTrajectory(waypoints_hourglass, 2.0, 1.0, 100.0);

        // Default to stable
        SetMode(TrajectoryMode.STABLE_STANDING);
    }

    public void SetMode(TrajectoryMode mode)
    {
        currentMode = mode;
        switch (mode)
        {
            case TrajectoryMode.STABLE_STANDING:
                curr_Xref = Xref_stable;
                break;
            case TrajectoryMode.LUNGE:
                curr_Xref = Xref_lunge;
                break;
            case TrajectoryMode.HOURGLASS:
                curr_Xref = Xref_hourglass;
                break;
        }
    }

    /// <summary>
    /// Generates a trajectory array by linearly interpolating between waypoints.
    /// </summary>
    private RBState[] InitializeLinearTrajectory(ReadOnlySpan<RBState> waypoints, double moveDuration, double pauseDuration, double frequency)
    {
        int moveSteps = (int)(moveDuration * frequency);
        int pauseSteps = (int)(pauseDuration * frequency);
        
        // Calculate total size
        int totalSegments = waypoints.Length - 1;
        if (totalSegments < 1) return new RBState[] { waypoints[0] };

        int totalLength = totalSegments * (moveSteps + pauseSteps);
        RBState[] trajectory = new RBState[totalLength];
        
        int currentIndex = 0;

        for (int i = 0; i < totalSegments; i++)
        {
            RBState start = waypoints[i];
            RBState end = waypoints[i+1];

            // 1. Interpolate Move
            for (int s = 0; s < moveSteps; s++)
            {
                double t = (double)s / moveSteps;
                double3 p = math.lerp(start.p, end.p, t);
                double3 th = math.lerp(start.th, end.th, t);
                
                // Simple constant velocity during move
                double dt = 1.0 / frequency;
                double3 v = (end.p - start.p) / moveDuration; 
                double3 w = (end.th - start.th) / moveDuration; 

                trajectory[currentIndex++] = new RBState(p, th, v, w);
            }

            // 2. Pause at waypoint
            for (int p = 0; p < pauseSteps; p++)
                trajectory[currentIndex++] = new RBState(end.p, end.th, double3.zero, double3.zero);
        }
        
        return trajectory;
    }

    /// <summary>
    /// Safely fills the destination span with the lookahead trajectory.
    /// Supports striding for solvers running at different time horizons.
    /// </summary>
    public void FillHorizon(long trajectoryIndex, Span<RBState> destination, int stride = 1)
    {
        int sourceLen = curr_Xref.Length;
        int destinationLen = destination.Length;

        // Optimization for standard 1:1 playback
        if (stride == 1)
        {
            int startIndex = (int)trajectoryIndex;
            if (startIndex >= sourceLen)
            {
                destination.Fill(curr_Xref[sourceLen - 1]);
                return;
            }

            int copyCount = math.min(destinationLen, sourceLen - startIndex);
            curr_Xref.AsSpan(startIndex, copyCount).CopyTo(destination);

            if (copyCount < destinationLen)
                destination.Slice(copyCount).Fill(curr_Xref[sourceLen - 1]);
        }
        else
        {
            // Strided access (slow path, but necessary for MPC horizons)
            for (int i = 0; i < destinationLen; i++)
            {
                long lookupIndex = trajectoryIndex + (i * stride);
                if (lookupIndex < sourceLen)
                    destination[i] = curr_Xref[lookupIndex];
                else
                    destination[i] = curr_Xref[sourceLen - 1]; // Hold last point
            }
        }
    }

    /// <summary>
    /// Computes the corresponding End-Effector reference state from a CoM reference state.
    /// Uses the current body-frame offset r_local = R_curr^T * (p_ee - p_com)
    /// and rotates it by the reference orientation to support full rigid body kinematics.
    /// </summary>
    public static RBState ComputeEERefFromCoMRef(RBState comRefState, double4x4 curr_comPose, double4x4 curr_eePose)
    {
        // 1. Calculate constant offset in body frame: r_local = R_com^T * (p_ee - p_com)
        double3 p_diff_world = curr_eePose.c3.xyz - curr_comPose.c3.xyz;
        double3x3 R_curr = new double3x3(curr_comPose.c0.xyz, curr_comPose.c1.xyz, curr_comPose.c2.xyz);
        double3 r_local = math.mul(math.transpose(R_curr), p_diff_world);

        // 2. Compute Rotation Matrix for Reference State (Euler ZYX)
        double3 th = comRefState.th; // (x:phi, y:theta, z:psi)
        
        // Build R_ref = Rz(psi) * Ry(theta) * Rx(phi) manually (Column-Major)
        double cz = math.cos(th.z), sz = math.sin(th.z);
        double cy = math.cos(th.y), sy = math.sin(th.y);
        double cx = math.cos(th.x), sx = math.sin(th.x);
        double3x3 R_z = new double3x3(new double3(cz, sz, 0), new double3(-sz, cz, 0), new double3(0, 0, 1));
        double3x3 R_y = new double3x3(new double3(cy, 0, -sy), new double3(0, 1, 0), new double3(sy, 0, cy));
        double3x3 R_x = new double3x3(new double3(1, 0, 0), new double3(0, cx, sx), new double3(0, -sx, cx));
        double3x3 R_ref = math.mul(R_z, math.mul(R_y, R_x));

        // 3. Compute EE Reference terms
        double3 r_ref_global = math.mul(R_ref, r_local);
        
        return new RBState(
            comRefState.p + r_ref_global,                          // p_ee_ref
            comRefState.th,                                        // Orientation (Shared)
            comRefState.v + math.cross(comRefState.w, r_ref_global), // v_ee_ref = v_com + w x r
            comRefState.w                                          // Angular Velocity (Shared)
        );
    }
}
