using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DarrenHighLevelController : MonoBehaviour
{
    // Add all references to other service's scripts
    // Public refs
    // E.g. structureMatrixBuilder, cableTensionComputer, ViveDataManager, etc. 

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // When conditions are met to compute new forces/torques (e.g. fresh position data)
            // Compute forces

            // Pass to cable tension solver

            // Pass tensions to TCP communication script
    }
}
