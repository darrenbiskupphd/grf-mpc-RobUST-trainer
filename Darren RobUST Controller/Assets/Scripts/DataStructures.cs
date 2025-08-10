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
    /// Position in the right-handed coordinate system.
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// Rotation in the right-handed coordinate system.
    /// </summary>
    public Quaternion Rotation;
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
