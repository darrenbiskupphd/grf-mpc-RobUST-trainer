using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeepTrackOfGameObjectVelocity : MonoBehaviour
{
    // keep track of last known position
    private Vector3 lastKnownPosition;
    private Vector3 currentPosition;
    private Vector3 deltaPosition;

    // last known velocity
    private Vector3 lastKnownVelocity;

    // Maximum velocity change between single update calls
    //private float maxVelocityChangeOnFrame = 0.1f;

    // velocity
    private Vector3 computedVelocity; // a desired velocity, but we should apply a "filter" of sorts by capping our actual velocity
    private Vector3 velocity;

    // Start is called before the first frame update
    void Start()
    {
        // Initialize last known position
        lastKnownPosition = transform.position;

        // Initialize last known velocity as zero
        lastKnownVelocity = new Vector3(0.0f, 0.0f, 0.0f);
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // Get current position
        currentPosition = transform.position;

        // Compute delta in position
        deltaPosition = currentPosition - lastKnownPosition;

        // Compute velocity as change in position divided by time
        computedVelocity = deltaPosition / Time.fixedDeltaTime;

        // Set actual velocity with MoveTowards
        velocity = computedVelocity;//Vector3.MoveTowards(lastKnownVelocity, computedVelocity, maxVelocityChangeOnFrame);

        // Update the last known position
        lastKnownPosition = currentPosition;
    }

    public Vector3 GetGameObjectVelocity()
    {
        return velocity;
    }
}
