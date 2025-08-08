using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StructureMatrixSettingsScript : MonoBehaviour
{
    // This script's main function is to store toggles and enums to select how the 
    // BuildStructureMatrix scene is going to work. Put those public variables here.
    public StructureMatrixDataSourceEnum structureMatrixDataSourceSelector;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // Return whether or not we're using Vive data, based on the value of the main settings enum 
    public bool GetUsingViconFlag()
    {
        // If we're using Vicon alone or with Vive
        if (structureMatrixDataSourceSelector == StructureMatrixDataSourceEnum.ViconOnly ||
            structureMatrixDataSourceSelector == StructureMatrixDataSourceEnum.BothViveAndVicon)
        {
            return true; // we are using Vicon
        }
        else
        {
            return false; // we are not using Vicon
        }
    }


    // Return whether or not we're using Vive data, based on the value of the main settings enum 
    public bool GetUsingViveFlag()
    {
        // If we're using Vive alone or with Vicon
        if(structureMatrixDataSourceSelector == StructureMatrixDataSourceEnum.ViveOnly ||
            structureMatrixDataSourceSelector == StructureMatrixDataSourceEnum.BothViveAndVicon)
        {
            return true; // we are using Vive
        }
        else
        {
            return false; // we are not using Vive
        }
    }


    public enum StructureMatrixDataSourceEnum
    {
        ViconOnly, 
        ViveOnly, 
        BothViveAndVicon
    }
}
