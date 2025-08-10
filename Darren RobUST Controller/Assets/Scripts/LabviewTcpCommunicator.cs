using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// Handles threaded TCP communication with LabVIEW, continuously sending the latest tension data.
/// </summary>
public class LabviewTcpCommunicator : MonoBehaviour
{
    [Header("Network Settings")]
    public string serverAddress = "127.0.0.1";
    public int serverPort = 8052;

    // Network components
    private TcpClient tcpClient;
    private NetworkStream networkStream;
    
    // Threading
    private Thread sendThread;
    private volatile bool isRunning = false;
    private readonly object dataLock = new object();
    
    // Current data to send - pre-allocated during initialization
    private int[] motorNumbers;
    private float[] tensions;

    public bool IsConnected { get; private set; } = false;

    // Cache for send thread to avoid allocations (only tensions need copying)
    private float[] sendTensions;

    /// <summary>
    /// Initializes the TCP communicator with the cable configuration.
    /// Called by RobotController in the correct dependency order.
    /// </summary>
    /// <param name="motorConfig">Array of motor numbers from the tension planner</param>
    /// <returns>True if initialization succeeded, false otherwise</returns>
    public bool Initialize(int[] motorConfig)
    {
        if (motorConfig == null || motorConfig.Length == 0)
        {
            Debug.LogError("LabviewTcpCommunicator: Invalid motor configuration provided.", this);
            return false;
        }

        // Pre-allocate all arrays based on fixed configuration
        int motorCount = motorConfig.Length;
        motorNumbers = new int[motorCount];
        tensions = new float[motorCount];
        sendTensions = new float[motorCount];
        
        // Copy the fixed motor configuration once
        Array.Copy(motorConfig, motorNumbers, motorCount);
        
        Debug.Log($"TCP Communicator initialized for {motorCount} motors: [{string.Join(", ", motorNumbers)}]");
        return true;
    }

    /// <summary>
    /// Updates only the tension values (zero-allocation, zero-check real-time performance).
    /// </summary>
    public void UpdateTensionSetpoint(float[] newTensions)
    {
        // No checks - arrays are guaranteed to be the right size at startup
        lock (dataLock)
        {
            Array.Copy(newTensions, tensions, tensions.Length);
        }
    }

    public async void ConnectToServer()
    {
        try
        {
            tcpClient = new TcpClient();
            UnityEngine.Debug.Log($"Connecting to {serverAddress}:{serverPort}...");
            
            await tcpClient.ConnectAsync(serverAddress, serverPort);
            networkStream = tcpClient.GetStream();
            tcpClient.NoDelay = true; // Disable Nagle's algorithm
            
            IsConnected = true;
            isRunning = true;
            sendThread = new Thread(SendLoop) { IsBackground = true };
            sendThread.Start();
            
            UnityEngine.Debug.Log("Connected to LabVIEW server.");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Connection failed: {e.Message}");
        }
    }

    /// <summary>
    /// Background thread that continuously sends data at precise 1kHz.
    /// </summary>
    private void SendLoop()
    {
        // Use double precision and proper rounding to avoid truncation errors
        double exactIntervalTicks = (double)System.Diagnostics.Stopwatch.Frequency / 1000.0; // 1kHz = 1ms intervals
        long targetIntervalTicks = (long)Math.Round(exactIntervalTicks);
        long nextTargetTime = System.Diagnostics.Stopwatch.GetTimestamp() + targetIntervalTicks;
        
        while (isRunning && IsConnected)
        {
            if (networkStream != null)
            {
                // Get thread-safe copy of tension data
                lock (dataLock)
                {
                    Array.Copy(tensions, sendTensions, tensions.Length);
                }
                
                // Send data
                string packet = FormatPacket(motorNumbers, sendTensions);
                byte[] data = Encoding.ASCII.GetBytes(packet);
                networkStream.Write(data, 0, data.Length);
            }

            // Precise timing: wait until next target time
            long timeUntilNext = nextTargetTime - System.Diagnostics.Stopwatch.GetTimestamp();
            double sleepMs = (double)timeUntilNext * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            
            if (sleepMs > 0.1)
            {
                // Use SpinWait for sub-millisecond precision (0.1-1.0ms range)
                SpinWait.SpinUntil(() => System.Diagnostics.Stopwatch.GetTimestamp() >= nextTargetTime);
            } // else: For sleepMs <= 0.1, just continue


            // Advance to next target time
            nextTargetTime += targetIntervalTicks;
            
            // Drift compensation: if we're behind, reset to maintain frequency
            long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
            if (nextTargetTime <= currentTime)
            {
                // We're behind - skip ahead to maintain frequency
                nextTargetTime = currentTime + targetIntervalTicks;
            }
        }
    }

    /// <summary>
    /// Formats the tension data into the LabVIEW protocol.
    /// </summary>
    private string FormatPacket(int[] motors, float[] tensions)
    {
        var sb = new StringBuilder();
        sb.Append($"K,{motors.Length}");
        
        for (int i = 0; i < motors.Length; i++)
        {
            sb.Append($",{motors[i]},{tensions[i]:F6}");
        }
        
        // Use double precision for timestamp calculation
        double timestampMs = (double)System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / (double)System.Diagnostics.Stopwatch.Frequency;
        sb.Append($",{timestampMs}\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Manually disconnect from the server and clean up resources.
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected) return;
        
        isRunning = false;
        sendThread?.Join(500);
        networkStream?.Close();
        tcpClient?.Close();
        IsConnected = false;
        
        UnityEngine.Debug.Log("Disconnected from LabVIEW server.");
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }
}
