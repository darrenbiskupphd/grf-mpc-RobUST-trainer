using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// A Cartesian Impedance Controller that outputs a desired Wrench (Force + Torque).
/// Implements a virtual spring-damper system: 
/// F = Kp(p_des - p) + Dp(v_des - v)
/// T = Ko(q_des * q_curr^-1) + Do(w_des - w)
/// </summary>
public class ImpedanceController : BaseController<Wrench>
{
    // Gains (Public for potential gain scheduling from main controller)
    public double3 K_pos;
    public double3 D_pos;
    public double3 K_ori;
    public double3 D_ori;

    // Safety Limits
    public double MaxForce = 300.0;
    public double MaxTorque = 30.0;

    // Internal State
    private double4x4 currentPose;
    private double3 currentLinVel;
    private double3 currentAngVel;
    private RBState targetState;
    private bool isStateValid = false;

    public ImpedanceController(float userMass)
    {
        // Default gains
        double K_pos_xy = 500.0 / 68.0 * userMass;
        double K_pos_z = 1100 / 68.0 * userMass;
        K_pos = new double3(K_pos_xy, K_pos_xy, K_pos_z);
        D_pos = new double3(.01, .01, .01);
        K_ori = new double3(1, 1, 1);
        D_ori = new double3(.01, .01, .01);
    }

    /// <summary>
    /// Updates the controller with the latest sensor data and target state.
    /// Call this immediately before computeNextControl().
    /// </summary>
    /// <param name="pose_RF">Current End-Effector pose in Robot Frame</param>
    /// <param name="linVel_RF">Current Linear Velocity in Robot Frame [m/s]</param>
    /// <param name="angVel_RF">Current Angular Velocity in Robot Frame [rad/s]</param>
    /// <param name="target">The desired state (Position, Euler ZYX, Velocities)</param>
    public void UpdateState(double4x4 pose_RF, double3 linVel_RF, double3 angVel_RF, RBState target)
    {
        currentPose = pose_RF;
        currentLinVel = linVel_RF;
        currentAngVel = angVel_RF;
        targetState = target;
        isStateValid = true;
    }

    public override Wrench computeNextControl()
    {
        if (!isStateValid) return new Wrench(double3.zero, double3.zero);

        // 1. Position Error (e_p = p_des - p_curr)
        double3 p_curr = currentPose.c3.xyz;
        double3 e_p = targetState.p - p_curr;
        double3 e_v = targetState.v - currentLinVel;

        double3 ForceCmd = (K_pos * e_p) + (D_pos * e_v);

        // 2. Orientation Error (Axis-Angle from Quaternion)
        // Construct Target Quaternion from Euler ZYX ( Matches MPCSolver convention )
        // R = Rz(psi) * Ry(theta) * Rx(phi)
        quaternion q_z = quaternion.AxisAngle((float3)new double3(0, 0, 1), (float)targetState.th.z);
        quaternion q_y = quaternion.AxisAngle((float3)new double3(0, 1, 0), (float)targetState.th.y);
        quaternion q_x = quaternion.AxisAngle((float3)new double3(1, 0, 0), (float)targetState.th.x);
        quaternion q_des = math.mul(q_z, math.mul(q_y, q_x));

        // Get Current Quaternion
        quaternion q_curr = new quaternion(new float3x3((float3)currentPose.c0.xyz, (float3)currentPose.c1.xyz, (float3)currentPose.c2.xyz));

        // Orientation Delta: q_diff = q_des * q_curr_inverse
        // This gives the rotation needed to go FROM current TO desired in global frame
        quaternion q_diff = math.mul(q_des, math.inverse(q_curr));

        // Convert to Rotation Vector (Axis * Angle)
        double3 e_o = double3.zero;
        if (math.abs(q_diff.value.w) < 0.999999)
        {
            // Extract angle: 2 * acos(w)
            // Extract axis: xyz / sin(theta/2)
            double theta = 2.0 * math.acos(math.clamp(q_diff.value.w, -1.0, 1.0));
            double sin_half_theta = math.sqrt(1.0 - q_diff.value.w * q_diff.value.w);
            
            if (sin_half_theta > 0.001)
                e_o = ((double3)q_diff.value.xyz / sin_half_theta) * theta;
        }

        double3 e_w = targetState.w - currentAngVel;
        double3 TorqueCmd = (K_ori * e_o) + (D_ori * e_w);

        // Saturation
        if (math.length(ForceCmd) > MaxForce) ForceCmd = math.normalize(ForceCmd) * MaxForce;
        if (math.length(TorqueCmd) > MaxTorque) TorqueCmd = math.normalize(TorqueCmd) * MaxTorque;

        return new Wrench(ForceCmd, TorqueCmd);
    }
}