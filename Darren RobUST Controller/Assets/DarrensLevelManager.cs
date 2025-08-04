/* 
 * The "LevelManager" runs a state machine. The states:
 * Setup: deals with system initalization and task-specific initialization (for rendering for example)
 *  - May need to wait for Vive tracker data to start streaming, Vicon data, EMG data, etc. 
 *  - Coordinates with LevelManagerStartupSupportScript. You must include that script's processes to work with RobUST and Vive and Vicon. 
 *  - You may want to remove some stuff like EMG startup.
 * EMG: made sure messages to EMG box and EMG data are streaming to its own thread/service/script before proceeding.
 * Further States: states and transitions that coordinate task-specific flow (e.g. waitingForSquatToStart, squatDescent, squatHold, squatAscent, Feedback, waitingForSAquatToStart)
 * TaskOver: do nothing, close file readers and stuff
 * 
 * 
 */ 





using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DarrensLevelManager : MonoBehaviour
{
    // Public instance variables - can drag references in editor

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
