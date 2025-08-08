using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SinusoidIndicatorController : MonoBehaviour
{

    public GameObject sinusoidGameObject;
    private DrawSinusoid sinusoidController;
    // Start is called before the first frame update
    void Start()
    {
        sinusoidController = sinusoidGameObject.GetComponent<DrawSinusoid>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void updateIndicatorPosition(Vector3 currentMiddleOfSinusoidValue)
    {
        transform.position = currentMiddleOfSinusoidValue; // sinusoidController.GetMiddleSinusoidPosition();
    }
}
