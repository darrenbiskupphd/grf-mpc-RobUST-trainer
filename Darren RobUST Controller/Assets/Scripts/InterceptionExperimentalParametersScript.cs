/* This script has getter functions for each parameter needed by the 
 * task's level manager (experimental control parameters), the player control script (for testing),
 * and possibly more. The experimenter should only need to change parameters within this script and this 
 * subject information script.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InterceptionExperimentalParametersScript : MonoBehaviour
{
    // Player object
    public bool useKeyboardToControlPlayer; // if we should use arrow keys to move player (true) or COM movements/COP/center of pelvis/etc. (false)

    // Interception level manager (key experimental parameters)
    public bool stimUsedThisBlock; // if stimulation was applied this block (true) or not (false)
    public int[] blockConditionOrders; // The condition: no penalty (1), low penalty (2), high penalty (3). The length of this array 
                                       // also determines the total number of blocks.
    public int numberOfTrialsPerBlock;
    public bool firstBlockIsRightwardStrikesFlag; // Whether or not the person strikes targets on the right (true) or left (false) this block
    public bool strikeDirectionAlternatesEachBlock; // Whether or not the strike direction (right or left) alternates each block (true) or stays the same (false)
    public bool targetIsForwardFromCenterOfBaseOfSupport;

    public float fractionOfMarginOfStabilityTraversedAtStrikeOnAverage; //the fraction of the margin of stability travelled in the given lateral direction
                                                                        //to reach the strike zone at the correct velocity. Note that this considers both 
                                                                        //position and velocity and incorporates the inverted pendulum model built into XCOM.
                                                                        //For example, 0.6 would position the strike zone center at 60% along the margin of stability 
                                                                        // measured from the center of the base of support to the edge (left, right) in question.
    public float proportionOfAnteroposteriorExcursionDistanceToPositionTarget = 0.4f; // The distance from the center of the base of support 
                                                                                    // to the target, expressed as a proportion of the distance
                                                                                    // from the center of base of support to the relevant A/P border.

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
