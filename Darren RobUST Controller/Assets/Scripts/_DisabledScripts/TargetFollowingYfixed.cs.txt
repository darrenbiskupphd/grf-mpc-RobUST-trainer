using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetFollowingYfixed : MonoBehaviour
{
    public GameObject target;
    public float HeightOfTheText=3f;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = target.transform.position + new Vector3(0f, HeightOfTheText, 0f);
    }

    public void SetTextOrientation(Quaternion desiredTextOrientation)
    {
        transform.rotation = desiredTextOrientation;
    }
}
