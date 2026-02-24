using UnityEngine;
using Unity.Mathematics;
using System.IO;
using System.Text;
using System;
using System.Globalization;

/// <summary>
/// High-performance logger that uses a pre-allocated array of structs 
/// to store data during the hot control loop, preventing GC spikes.
/// Writes to disk only when requested (e.g., on shutdown).
/// </summary>
public class DataLogger
{
    // A Blittable-ish struct to keep memory contiguous
    public struct DataFrame
    {
        public double Timestamp;
        public double4x4 ComPoseRF;
        public double4x4 EEPoseRF;
        public ForcePlateData FP1;
        public ForcePlateData FP2;
        public Wrench GoalWrench;
        public RBState GoalState;
    }

    private DataFrame[] _buffer;
    private int _cursor = 0;
    private int _maxFrames;
    private string _baseFolder;

    public int FrameCount => _cursor;

    /// <param name="maxMinutes">Maximum duration to buffer in RAM.</param>
    /// <param name="frequency">Control frequency in Hz.</param>
    public DataLogger(int maxMinutes, int frequency)
    {
        // Allocation: 60 sec * Freq * Minutes
        _maxFrames = 60 * frequency * maxMinutes; 
        
        // Eager allocation: Request all memory now. 
        // ~400 bytes per frame @ 100Hz = ~2.4MB per minute.
        _buffer = new DataFrame[_maxFrames];
        _cursor = 0;
        
        _baseFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ExperimentLogs");
        if (!Directory.Exists(_baseFolder)) Directory.CreateDirectory(_baseFolder);
    }

    /// <summary>
    /// Records a frame into the buffer. This is thread-safe for a single writer.
    /// </summary>
    public void Log(long timestamp_tick, in double4x4 comPose, in double4x4 eePose, 
                    in ForcePlateData fp1, in ForcePlateData fp2, 
                    in Wrench goalWrench, in RBState goalState)
    {
        if (_cursor >= _maxFrames) return; 

        // Direct array access by reference to avoid structure copy
        ref DataFrame frame = ref _buffer[_cursor];
        
        frame.Timestamp = (double)timestamp_tick / (double)System.Diagnostics.Stopwatch.Frequency;
        frame.ComPoseRF = comPose;
        frame.EEPoseRF = eePose;
        frame.FP1 = fp1;
        frame.FP2 = fp2;
        frame.GoalWrench = goalWrench;
        frame.GoalState = goalState;

        _cursor++;
    }

    /// <summary>
    /// Flushes the internal buffer to a CSV file.
    /// Warning: This allocates memory for string building and performs File I/O.
    /// Do not call during the control loop.
    /// </summary>
    public void WriteToDisk(string sessionName)
    {
        if (_cursor == 0) return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = $"{sessionName}_{timestamp}.csv";
        string fullPath = Path.Combine(_baseFolder, fileName);

        Debug.Log($"[DataLogger] Writing {_cursor} frames to {fileName}...");

        try 
        {
            using (StreamWriter writer = new StreamWriter(fullPath))
            {
                // CSV Header
                StringBuilder sb = new StringBuilder();
                sb.Append("Timestamp,");
                
                // Helper to expand headers
                AppendMatrixHeader(sb, "CoM");
                AppendMatrixHeader(sb, "EE");
                sb.Append("FP1_Fx,FP1_Fy,FP1_Fz,FP1_CoPx,FP1_CoPy,FP1_CoPz,");
                sb.Append("FP2_Fx,FP2_Fy,FP2_Fz,FP2_CoPx,FP2_CoPy,FP2_CoPz,");
                sb.Append("Goal_Fx,Goal_Fy,Goal_Fz,Goal_Tx,Goal_Ty,Goal_Tz,");
                // RBState fields: p (position), th (euler), v (linear vel), w (angular vel)
                sb.Append("Goal_Px,Goal_Py,Goal_Pz,Goal_Ex,Goal_Ey,Goal_Ez,Goal_Vx,Goal_Vy,Goal_Vz,Goal_Wx,Goal_Wy,Goal_Wz"); 
                
                writer.WriteLine(sb.ToString());

                // Write Data
                for (int i = 0; i < _cursor; i++)
                {
                    ref DataFrame f = ref _buffer[i];
                    sb.Clear();
                    
                    sb.Append(f.Timestamp).Append(',');
                    
                    AppendMatrix(sb, f.ComPoseRF);
                    AppendMatrix(sb, f.EEPoseRF);
                    
                    AppendVec3(sb, f.FP1.Force); AppendVec3(sb, f.FP1.CoP);
                    AppendVec3(sb, f.FP2.Force); AppendVec3(sb, f.FP2.CoP);
                    
                    AppendVec3(sb, f.GoalWrench.Force); AppendVec3(sb, f.GoalWrench.Torque);

                    // RBState unpacking 
                    AppendVec3(sb, f.GoalState.p);
                    AppendVec3(sb, f.GoalState.th);
                    AppendVec3(sb, f.GoalState.v);
                    AppendVec3(sb, f.GoalState.w);

                    writer.WriteLine(sb.ToString());
                }
            }
            Debug.Log($"[DataLogger] Saved successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataLogger] Failed to write log: {e.Message}");
        }
    }

    // Helpers to keep the loop clean
    private void AppendMatrixHeader(StringBuilder sb, string prefix)
    {
        for(int r=0; r<4; r++)
            for(int c=0; c<4; c++)
                sb.Append($"{prefix}_m{r}{c},");
    }

    private void AppendMatrix(StringBuilder sb, in double4x4 m)
    {
        sb.Append(m.c0.x).Append(',').Append(m.c1.x).Append(',').Append(m.c2.x).Append(',').Append(m.c3.x).Append(',');
        sb.Append(m.c0.y).Append(',').Append(m.c1.y).Append(',').Append(m.c2.y).Append(',').Append(m.c3.y).Append(',');
        sb.Append(m.c0.z).Append(',').Append(m.c1.z).Append(',').Append(m.c2.z).Append(',').Append(m.c3.z).Append(',');
        sb.Append(m.c0.w).Append(',').Append(m.c1.w).Append(',').Append(m.c2.w).Append(',').Append(m.c3.w).Append(',');
    }

    private void AppendVec3(StringBuilder sb, in double3 v)
    {
        sb.Append($"{v.x:F5},{v.y:F5},{v.z:F5},");
    }
}
