using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class trackCenterOfPressure : MonoBehaviour
{

    public GameObject forcePlateDataAccessObject;
    private RetrieveForcePlateDataScript scriptToRetrieveForcePlateData;
    public GameObject LevelManager; 
    private LevelManagerScriptAbstractClass levelManagerScript;

    //
    private bool isForcePlateDataReadyForAccess; 


    // Start is called before the first frame update
    void Start()
    {
        scriptToRetrieveForcePlateData = forcePlateDataAccessObject.GetComponent<RetrieveForcePlateDataScript>();
        levelManagerScript = LevelManager.GetComponent<LevelManagerScriptAbstractClass>();
    }

    // Update is called once per frame
    void Update()
    {
        if (!isForcePlateDataReadyForAccess)
        {
            isForcePlateDataReadyForAccess = scriptToRetrieveForcePlateData.getForcePlateDataAvailableViaDataStreamStatus();
        }
        else
        {
            Vector3 CopPositionViconFrame = scriptToRetrieveForcePlateData.getMostRecentCenterOfPressureInViconFrame();
            Vector3 CopPositionInUnityFrame = levelManagerScript.mapPointFromViconFrameToUnityFrame(CopPositionViconFrame);
            transform.position = new Vector3(CopPositionInUnityFrame.x, CopPositionInUnityFrame.y, transform.position.z);
        }
    }
}
