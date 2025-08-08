using UnityEngine;

/// <summary>
/// A simple data structure to hold the position and rotation of a tracker.
/// This is a non-MonoBehaviour class, used for organizing data.
/// </summary>
[System.Serializable]
public class TrackerData
{
    public Vector3 Position;
    public Quaternion Rotation;

    public TrackerData(Vector3 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;
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
