using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraSettingsController : MonoBehaviour
{

    public float aspectRatioNumerator; //the width. e.g. if we want an aspect ratio of 16:9, set this to 16
    public float aspectRatioDenominator; //the height. e.g. if we want an aspect ratio of 16:9, set this to 9
    private Camera sceneCamera; // the Camera GameObject to which this script is attached

    // Start is called before the first frame update
    void Start()
    {
        // set the desired aspect ratio (the values in this example are
        // hard-coded for 16:9, but you could make them into public
        // variables instead so you can set them at design time)
        float targetaspect = aspectRatioNumerator / aspectRatioDenominator;

        // determine the game window's current aspect ratio
        float windowaspect = (float)Screen.width / (float)Screen.height;

        // current viewport height should be scaled by this amount
        float scaleheight = windowaspect / targetaspect;

        // obtain camera component so we can modify its viewport
        sceneCamera = GetComponent<Camera>();

        // if scaled height is less than current height, add letterbox
        if (scaleheight < 1.0f)
        {
            Rect rect = sceneCamera.rect;

            rect.width = 1.0f;
            rect.height = scaleheight;
            rect.x = 0;
            rect.y = (1.0f - scaleheight) / 2.0f;

            sceneCamera.rect = rect;
        }
        else // add pillarbox
        {
            float scalewidth = 1.0f / scaleheight;

            Rect rect = sceneCamera.rect;

            rect.width = scalewidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scalewidth) / 2.0f;
            rect.y = 0;

            sceneCamera.rect = rect;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public float getAspectRatio()
    {
        return (aspectRatioNumerator/ aspectRatioDenominator);
    }

    public Vector3 ConvertViewportCoordsToWorldCoords(Vector3 vectorInViewportCoords)
    {
        return sceneCamera.ViewportToWorldPoint(vectorInViewportCoords);
    }
}
