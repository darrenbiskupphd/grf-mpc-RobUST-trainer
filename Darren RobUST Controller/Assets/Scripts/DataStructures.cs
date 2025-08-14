using UnityEngine;

/// <summary>
/// A simple data structure to hold the position and rotation of a tracker.
/// NOTE: Data is stored in a RIGHT-HANDED coordinate system (OpenVR standard).
/// This is a non-MonoBehaviour class, used for organizing data efficiently.
/// </summary>
[System.Serializable]
public class TrackerData
{
    /// <summary>
    /// The full 4x4 homogeneous transformation matrix representing the tracker's pose.
    /// NOTE: This matrix is in the RIGHT-HANDED coordinate system (OpenVR standard).
    /// </summary>
    public Matrix4x4 PoseMatrix;

    public TrackerData()
    {
        PoseMatrix = Matrix4x4.identity;
    }
}

/// <summary>
/// A simple data structure to hold force and moment data from a force plate.
/// This is a non-MonoBehaviour class, used for organizing data.
/// </summary>
[System.Serializable]
public class ForcePlateData
{
    public Vector3 Force;
    public Vector3 Moment;

    public ForcePlateData(Vector3 force, Vector3 moment)
    {
        Force = force;
        Moment = moment;
    }
}
