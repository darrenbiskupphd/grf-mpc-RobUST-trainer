using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CanvasToCamera : MonoBehaviour
{
    private Transform cameraTransform;

    void Start()
    {
        // Find the HMD camera (usually tagged as MainCamera)
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        else
        {
            Debug.LogError("Main camera not found. Please tag your HMD camera as 'MainCamera'.");
        }
    }

    void LateUpdate()
    {
        if (cameraTransform != null)
        {
            // Make the canvas face the camera
            transform.LookAt(cameraTransform);
            // Optionally, reverse the forward vector to face the camera correctly
            transform.rotation = Quaternion.LookRotation(transform.position - cameraTransform.position);
        }
    }
}
