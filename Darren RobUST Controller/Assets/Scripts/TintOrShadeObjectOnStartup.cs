// For now, this script only produces a shade, not a tint.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TintOrShadeObjectOnStartup : MonoBehaviour
{
    private float shadeScaler = 0.75f; 


    // Start is called before the first frame update
    void Start()
    {
        Color materialColor = gameObject.GetComponent<Renderer>().material.color;
        materialColor[0] = materialColor[0] * shadeScaler;
        materialColor[1] = materialColor[1] * shadeScaler;
        materialColor[2] = materialColor[2] * shadeScaler;
        gameObject.GetComponent<Renderer>().material.color = materialColor;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
