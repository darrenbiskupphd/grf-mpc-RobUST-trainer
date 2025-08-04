using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadsetFllower : MonoBehaviour
{
    public Transform headset;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = headset.position + new Vector3(0f,0f,-5.0f);
        transform.position = headset.position + new Vector3(0f,0f,-5.0f);
    }
}
