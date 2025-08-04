using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugModeSettings : MonoBehaviour
{

    public bool DEBUG_MODE_FLAG; // whether or not we'll print debug statements
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public bool GetDebugModeFlag()
    {
        return DEBUG_MODE_FLAG;
    }
}
