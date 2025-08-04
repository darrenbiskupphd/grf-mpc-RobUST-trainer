using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UniversalExperimentSettings : MonoBehaviour
{
    public controlPointEnum onscreenControlPoint;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // A public getter function for retrieving the control point settings. 
    // For example, called by COM manager to choose the control point.
    public controlPointEnum GetControlPointSettingsEnumObject()
    {
        return onscreenControlPoint;
    }
}

// Used across scripts (e.g., also used in COM manager), but defined here because the class relates to universal experiment settings.
public enum controlPointEnum
{
    COM,
    Pelvis,
    Chest
}
