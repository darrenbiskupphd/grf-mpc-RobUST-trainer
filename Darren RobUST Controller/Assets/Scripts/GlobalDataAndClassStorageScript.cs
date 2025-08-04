using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class LevelManagerScriptAbstractClass : MonoBehaviour
{
    // Functions that MUST be implemented by child classes
    public abstract string GetCurrentTaskName(); // Get the name of the current task = the folder name data will be categorized to for this task (e.g., "SquattingTask")
    public abstract bool GetEmgStreamingDesiredStatus(); // Get the flag set by level manager that activates (true) or inactivates (false) the EMG streaming service
    public abstract Vector3 mapPointFromViconFrameToUnityFrame(Vector3 pointInViconFrame);
    public abstract Vector3 mapPointFromUnityFrameToViconFrame(Vector3 pointInUnityFrame);
    public abstract Vector3 GetControlPointForRobustForceField();
    public abstract List<Vector3> GetExcursionLimitsFromExcursionCenterInViconUnits();
    public abstract Vector3 GetCenterOfExcursionLimitsInViconFrame();
    public abstract string GetCurrentDesiredForceFieldTypeSpecifier(); // Retrieve a string specifying what type of RobUST FF is currently desired. OK to have idle as default.

    // Functions that MAY be implemented by child classes
    public virtual void StoreRowOfFrameData()
    {
        // Empty base class implementation
    }
}

public class GlobalDataAndClassStorageScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
