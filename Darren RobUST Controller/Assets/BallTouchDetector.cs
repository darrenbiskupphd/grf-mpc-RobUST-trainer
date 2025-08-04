using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallTouchDetector : MonoBehaviour
{
    
    // Flags indicating which hand, if any, is touching the ball
    private bool leftHandTouchingBallFlag = false;
    private bool rightHandTouchingBallFlag = false;
    
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    // Public functions to see if a hand is touching**************************************************************
    public bool GetRightHandTouchingBallFlag()
    {
        return rightHandTouchingBallFlag;
    }
    
    public bool GetLeftHandTouchingBallFlag()
    {
        return leftHandTouchingBallFlag;
    }

    // The Collision functions************************************************************************************
    // Collision Enter
    private void OnCollisionEnter(Collision other)
    {
        // If the other collider is the right hand
        if (other.gameObject.CompareTag("RightHand"))
        {
            rightHandTouchingBallFlag = true;
        }
        // If the other collider is the left hand
        if (other.gameObject.CompareTag("LeftHand"))
        {
            leftHandTouchingBallFlag = true;
        }
    }

    // Collision exit
    private void OnCollisionExit(Collision other)
    {
        // Set both the right hand and left hand touching ball flag to false
        leftHandTouchingBallFlag = false;
        rightHandTouchingBallFlag = false;
    }
}
