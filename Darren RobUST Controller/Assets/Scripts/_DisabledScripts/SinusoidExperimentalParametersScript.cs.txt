using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SinusoidExperimentalParametersScript : MonoBehaviour
{
    public bool loadAnkleRomFromDailyFilePathFlag;
    public int numberOfSinusoidCyclesToTrackPerBlock;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public bool getLoadAnkleRomFromDailyFileFlag()
    {
        return loadAnkleRomFromDailyFilePathFlag;
    }

    public int getNumberOfCyclesToTrackPerBlock()
    {
        return numberOfSinusoidCyclesToTrackPerBlock;
    }
}
