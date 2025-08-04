using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

//Since the System.Diagnostics namespace AND the UnityEngine namespace both have a .Debug,
//specify which you'd like to use below 
using Debug = UnityEngine.Debug;

public class TimeKeeperForDataSyncScript : MonoBehaviour
{

    // The date time object that is set at startup 
    private DateTime programStartDateTime = DateTime.Now;
    private Stopwatch timeKeeperStopWatch = new Stopwatch();

    // The most recent frame time
    private float mostRecentUnityFrameTime = 0.0f;


    // Start is called before the first frame update
    void Start()
    {
        timeKeeperStopWatch.Start();
    }

    // Update is called once per frame
    void Update()
    {
        mostRecentUnityFrameTime = Time.time;
    }

    public float GetCurrentMillisecondsElapsedSinceProgramStart()
    {
        /*        // Get the current DateTime object
                DateTime currentDateTime = DateTime.Now;

                // Get the ticks elapsed since program start
                long elapsedTicks = currentDateTime.Ticks - programStartDateTime.Ticks;

                // Get the milliseconds elapsed since program start by dividing 
                // the number of ticks (1 tick = 100 nanoseconds) by 10000. 
                long elapsedMillseconds = elapsedTicks / (long) 10000.0;*/

        long ticksThisTime = 0;
        long nanosecPerTick = (1000L * 1000L * 1000L) / Stopwatch.Frequency;
        ticksThisTime = timeKeeperStopWatch.ElapsedTicks;
        float millisecondsElapsed = (float)(((double)nanosecPerTick * ticksThisTime) / (1000L * 1000L));

        // Convert from long to float and return 
        return millisecondsElapsed;
    }

    public float GetMostRecentUnityFrameTime()
    {
        return mostRecentUnityFrameTime;
    }
}
