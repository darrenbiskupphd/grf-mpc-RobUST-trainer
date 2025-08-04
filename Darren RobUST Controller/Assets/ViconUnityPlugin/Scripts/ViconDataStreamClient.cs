/*
 * NOTE: I'm modifying this class to access the data we need from the Vicon data stream.
 * Frustratingly, some Vicon SDK return parameters do not list their property names, so you cannot access
 * the data you need. To get property names for the different return values, check out the GitHub for the 
 * Vicon data stream SDK: https://github.com/KumarRobotics/vicon/blob/master/vicon_driver/vicon_sdk/Linux64/Client.h
 */




using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.ComponentModel;

using ViconDataStreamSDK.CSharp;


public class ViconDataStreamClient : MonoBehaviour
{
    [Tooltip("The hostname or ip address of the Datastream server.")]
    public string HostName = "localhost";

    [Tooltip("The Datastream port number. Typically 804 for the low latency stream and 801 if off-line review is required.")]
    public string Port = "801";

    [Tooltip("Enter a comma separated list of subjects that are required in the stream. If empty, all subjects will be transmitted.")]
    public string SubjectFilter;

    [Tooltip("Switches to the pre-fetch streaming mode, which will request new frames from the server as required while minimizing latency, rather than all frames being streamed. This can potentially help minimise the disruption of data delivery lags on the network. See the datastream documentation for more details of operation.")]
    public bool UsePreFetch = false;

    [Tooltip("Use retiming output mode. This can help to smooth out temporal artifacts due to differences between render and system frame rates.")]
    public bool IsRetimed = false;

    [Tooltip("Adds a fixed time offset to retimed output data. Only valid in retiming mode. Can be used to compensate for known render delays.")]
    public float Offset = 0;

    [Tooltip("Log timing information to a file.")]
    public bool Log = false;

    [Tooltip("Enable adapter settings to improve latency on wireless connections.")]
    public bool ConfigureWireless = true;

    private ViconDataStreamSDK.CSharp.Client m_Client;
    private ViconDataStreamSDK.CSharp.RetimingClient m_RetimingClient;

    private bool UseLightweightData = true;
    private bool GetFrameThread = true;
    private static bool bConnected = false;
    private bool bSubjectFilterSet = false;
    private bool bThreadRunning = false;
    Thread m_Thread;


    public delegate void ConnectionCallback(bool i_bConnected);
    public static void OnConnected(bool i_bConnected)
    {
        bConnected = i_bConnected;
    }

    ConnectionCallback ConnectionHandler = OnConnected;

    private void SetupLog()
    {
        String DateTime = System.DateTime.Now.ToString();
        DateTime = DateTime.Replace(" ", "_");
        DateTime = DateTime.Replace("/", "_");
        DateTime = DateTime.Replace(":", "_");
        String ClientPathName = Application.dataPath + "/../Logs/" + DateTime + "_ClientLog.csv";
        String StreamPathName = Application.dataPath + "/../Logs/" + DateTime + "_StreamLog.csv";

        bool bLogSuccess = false;
        if (IsRetimed)
        {
            bLogSuccess = m_RetimingClient.SetTimingLogFile(ClientPathName, StreamPathName).Result == Result.Success;
        }
        else
        {
            bLogSuccess = m_Client.SetTimingLogFile(ClientPathName, StreamPathName).Result == Result.Success;
        }

        if (bLogSuccess)
        {
            print("Writing log to " + ClientPathName + " and " + StreamPathName);
        }
        else
        {
            print("Failed to create logs: " + ClientPathName + ", " + StreamPathName);
        }
    }

    void Start()
    {
        m_Client = new Client();
        m_RetimingClient = new RetimingClient();

        // If we're using the retimer, we don't want to use our own thread for getting frames.
        GetFrameThread = !IsRetimed;

        if (ConfigureWireless)
        {
            Output_ConfigureWireless WifiConfig = m_Client.ConfigureWireless();
            if (WifiConfig.Result != Result.Success)
            {
                print("Failed to configure wireless: " + WifiConfig.ToString());
            }
            else
            {
                print("Configured adapter for wireless settings");
            }
        }

        print("Starting...");
        Output_GetVersion OGV = m_Client.GetVersion();
        print("Using Datastream version " + OGV.Major + "." + OGV.Minor + "." + OGV.Point + "." + OGV.Revision);

        if (Log)
        {
            SetupLog();
        }

        m_Thread = new Thread(ConnectClient);
        m_Thread.Start();
    }

    void OnValidate()
    {
        if (bConnected)
        {
            if (bThreadRunning)
            {
                bThreadRunning = false;
                m_Thread.Join();

                DisConnect();
                m_Thread = new Thread(ConnectClient);
                m_Thread.Start();
            }
        }
    }
    void DisConnect()
    {
        if (m_RetimingClient.IsConnected().Connected)
        {
            m_RetimingClient.Disconnect();
        }
        if (m_Client.IsConnected().Connected)
        {
            m_Client.Disconnect();
        }
    }

    private void ConnectClient()
    {
        bThreadRunning = true;

        // We have to handle the multi-route syntax, which is of the form HostName1:Port;Hostname2:Port
        String CombinedHostnameString = "";
        String[] Hosts = HostName.Split(';');
        foreach (String Host in Hosts)
        {
            String TrimmedString = Host.Trim();
            String HostWithPort = null;

            // Check whether the hostname already contains a port and add if it doesn't
            if (TrimmedString.Contains(":"))
            {
                HostWithPort = TrimmedString;
            }
            else
            {
                HostWithPort = TrimmedString + ":" + Port;
            }

            if (!String.IsNullOrEmpty(CombinedHostnameString))
            {
                CombinedHostnameString += ";";
            }

            CombinedHostnameString += HostWithPort;
        }

        print("Connecting to " + CombinedHostnameString + "...");

        if (IsRetimed)
        {
            while (bThreadRunning == true && !m_RetimingClient.IsConnected().Connected)
            {
                Output_Connect OC = m_RetimingClient.Connect(CombinedHostnameString);
                print("Connect result: " + OC.Result);

                System.Threading.Thread.Sleep(200);
            }

            print("Connected using retimed client.");

            if (UseLightweightData)
            {
                // Retiming client will have segment data enabled by default
                if (m_RetimingClient.EnableLightweightSegmentData().Result == Result.Success)
                {
                    print("Using lightweight segment data");
                }
                else
                {
                    print("Unable to use lightweight segment data: Using standard segment data");
                }
            }
            else
            {
                print("Using standard segment data");
            }

            // get a frame from the data stream so we can inspect the list of subjects
            SetAxisMapping(Direction.Forward, Direction.Left, Direction.Up);
            //SetAxisMapping(Direction.Right, Direction.Up, Direction.Backward);
            ConnectionHandler(true);

            bThreadRunning = false;
            return;
        }

        while (bThreadRunning == true && !m_Client.IsConnected().Connected)
        {
            Output_Connect OC = m_Client.Connect(CombinedHostnameString);
            print("Connect result: " + OC.Result);

            System.Threading.Thread.Sleep(200);
        }

        if (UsePreFetch)
        {
            m_Client.SetStreamMode(StreamMode.ClientPullPreFetch);
            print("Using pre-fetch streaming mode");
        }
        else
        {
            m_Client.SetStreamMode(StreamMode.ServerPush);
        }

        // Get a frame first, to ensure we have received supported type data from the server before
        // trying to determine whether lightweight data can be used.
        GetNewFrame();

        if (UseLightweightData)
        {
            if (m_Client.EnableLightweightSegmentData().Result != Result.Success)
            {
                print("Unable to use lightweight segment data: Using standard segment data");
                m_Client.EnableSegmentData();
            }
            else
            {
                print("Using lightweight segment data");
            }
        }
        else
        {
            print("Using standard segment data");
            m_Client.EnableSegmentData();
        }

        SetAxisMapping(Direction.Forward, Direction.Left, Direction.Up);
        //SetAxisMapping(Direction.Right, Direction.Up, Direction.Backward);
        ConnectionHandler(true);

        // Get frames in this separate thread if we've asked for it.
        while (GetFrameThread && bThreadRunning)
        {
            GetNewFrame();
        }

        bThreadRunning = false;
    }

    void LateUpdate()
    {
        // Get frame on late update if we've not got a separate frame acquisition thread
        if (!GetFrameThread)
        {
            if (!bConnected)
            {
                return;
            }
            GetNewFrame();
        }
    }

    public Output_GetSegmentLocalRotationQuaternion GetSegmentRotation(string SubjectName, string SegmentName)
    {
        if (IsRetimed)
        {
            return m_RetimingClient.GetSegmentLocalRotationQuaternion(SubjectName, SegmentName);
        }
        else
        {
            return m_Client.GetSegmentLocalRotationQuaternion(SubjectName, SegmentName);
        }

    }
    public Output_GetSegmentLocalTranslation GetSegmentTranslation(string SubjectName, string SegmentName)
    {
        if (IsRetimed)
        {
            return m_RetimingClient.GetSegmentLocalTranslation(SubjectName, SegmentName);
        }
        else
        {
            return m_Client.GetSegmentLocalTranslation(SubjectName, SegmentName);
        }

    }
    public Output_GetSegmentStaticScale GetSegmentScale(string SubjectName, string SegmentName)
    {
        if (IsRetimed)
        {
            return m_RetimingClient.GetSegmentStaticScale(SubjectName, SegmentName);
        }
        else
        {
            return m_Client.GetSegmentStaticScale(SubjectName, SegmentName);
        }

    }

    /// Returns the local translation for a bone, scaled according to its scale and the scale of the bones above it 
    /// in the heirarchy, apart from the root translation
    public Output_GetSegmentLocalTranslation GetScaledSegmentTranslation(string SubjectName, string SegmentName)
    {
        double[] OutputScale = new double[3];
        OutputScale[0] = OutputScale[1] = OutputScale[2] = 1.0;

        // Check first whether we have a parent, as we don't wish to scale the root node's position
        Output_GetSegmentParentName Parent = GetSegmentParentName(SubjectName, SegmentName);

        string CurrentSegmentName = SegmentName;
        if (Parent.Result == Result.Success)
        {

            do
            {
                // We have a parent. First get our scale, and then iterate through the nodes above us
                Output_GetSegmentStaticScale Scale = GetSegmentScale(SubjectName, CurrentSegmentName);
                if (Scale.Result == Result.Success)
                {
                    for (uint i = 0; i < 3; ++i)
                    {
                        if (Scale.Scale[i] != 0.0) OutputScale[i] = OutputScale[i] * Scale.Scale[i];
                    }
                }

                Parent = GetSegmentParentName(SubjectName, CurrentSegmentName);
                if (Parent.Result == Result.Success)
                {
                    CurrentSegmentName = Parent.SegmentName;
                }
            } while (Parent.Result == Result.Success);
        }

        Output_GetSegmentLocalTranslation Translation = GetSegmentTranslation(SubjectName, SegmentName);
        if (Translation.Result == Result.Success)
        {
            for (uint i = 0; i < 3; ++i)
            {
                Translation.Translation[i] = Translation.Translation[i] / OutputScale[i];
            }
        }
        return Translation;
    }

    public Output_GetSubjectRootSegmentName GetSubjectRootSegmentName(string SubjectName)
    {
        if (IsRetimed)
        {
            return m_RetimingClient.GetSubjectRootSegmentName(SubjectName);
        }
        else
        {
            return m_Client.GetSubjectRootSegmentName(SubjectName);
        }

    }
    public Output_GetSegmentParentName GetSegmentParentName(string SubjectName, string SegmentName)
    {
        if (IsRetimed)
        {
            return m_RetimingClient.GetSegmentParentName(SubjectName, SegmentName);
        }
        else
        {
            return m_Client.GetSegmentParentName(SubjectName, SegmentName);
        }

    }
    public Output_SetAxisMapping SetAxisMapping(Direction X, Direction Y, Direction Z)
    {
        if (IsRetimed)
        {
            return m_RetimingClient.SetAxisMapping(X, Y, Z);
        }
        else
        {
            return m_Client.SetAxisMapping(X, Y, Z);
        }
    }
    public void GetNewFrame()
    {
        if (IsRetimed)
        {
            m_RetimingClient.UpdateFrame(Offset);
        }
        else
        {
            m_Client.GetFrame();
        }
        UpdateSubjectFilter();
    }
    public uint GetFrameNumber()
    {
        if (IsRetimed)
        {
            return 0;
        }
        else
        {
            return m_Client.GetFrameNumber().FrameNumber;
        }
    }

    //Added functions to get marker-specific data.
    //Started by Rob Carrera on 8/20/2021. 
    //___________________________________________________________________________
    // Date          Author          Note
    // 8/20/21       RMC             Started trying to add marker-specific getter functions
    // 4/14/22       RMC             Adding functions to access device data, specifically for the force plates
    //******************************************************************************************************



    public bool EnableMarkerData()
    {
        Debug.Log("m_Client has the following property names: ");
        Type t = m_Client.GetType(); // Where obj is object whose properties you need.
        PropertyInfo[] pi = t.GetProperties();
        foreach (PropertyInfo p in pi)
        {
            Debug.Log(p.Name + " : " + p.GetValue(m_Client));
        }

        //first, attempt to enable the marker data
        Output_EnableMarkerData enableResult = m_Client.EnableMarkerData();
        //next, check to see if the marker data is enabled
        Output_IsMarkerDataEnabled isEnabledFlagObject = m_Client.IsMarkerDataEnabled();
        return isEnabledFlagObject.Enabled; //return the bool indicating whether marker data is enabled (true) or not (false)
    }

    public bool EnableDeviceData()
    {
        //first, attempt to enable the device data
        Output_EnableDeviceData enableResult = m_Client.EnableDeviceData();
        
        //next, check to see if the device data is enabled
        Output_IsDeviceDataEnabled isEnabledFlagObject = m_Client.IsDeviceDataEnabled();
        return isEnabledFlagObject.Enabled; //return the bool indicating whether device data is enabled (true) or not (false)
    }


    // Note: the default data stream mode is "Client Pull." 
    // "Client pull pre-fetch" can offer higher performance (less latency), and 
    // then "ServerPush" can offer least latency but can fill up buffers.
    public bool SetDataStreamInClientPullPreFetchMode()
    {
        // Send a command telling the server that we want to use Client Pull Pre-fetch mode
        Output_SetStreamMode setStreamModeOutput = m_Client.SetStreamMode(ViconDataStreamSDK.CSharp.StreamMode.ClientPullPreFetch);

        // Try to get the boolean result which indicates if the operation was successful (true) or not (false)
        if(setStreamModeOutput.Result == Result.Success)
        {
            return true; // Success! Mode changed.
        }
        else
        {
            return false; // Failure. Mode not successfully changed.
        }
    }


    public uint  GetSubjectCount() {
        Output_GetSubjectCount outputObject = m_Client.GetSubjectCount();
        uint subjectCount = (uint) outputObject.SubjectCount;
        Debug.Log("Number of subjects is: " + subjectCount);
        return subjectCount;
    }



    public string GetFirstSubjectName()
    {
        uint subjectIndex = 0; //we're interested in the first subject name only, as in our setup we'll only ever have one
        Output_GetSubjectName subjectNameObject = m_Client.GetSubjectName(subjectIndex);
        return subjectNameObject.SubjectName;
    }



    public uint GetSkeletonMarkerCount(string SubjectName)
    {
        Output_GetMarkerCount markerCountObject = m_Client.GetMarkerCount(SubjectName);
        Debug.Log("Number of markers on subject : " + markerCountObject.MarkerCount);
        return markerCountObject.MarkerCount;
    }



    public string[] GetNamesOfAllSubjectSkeletonMarkers(string subjectName, uint markersInSkeletonCount)
    {
        string[] listOfMarkerNames = new string[markersInSkeletonCount]; //initialize the string array to store all the marker names
        Output_GetMarkerName outputMarkerNameObject; //a container for the result object

        for (uint markerIndex = 0; markerIndex < markersInSkeletonCount; markerIndex++) //for each available labeled marker
        {
            outputMarkerNameObject = m_Client.GetMarkerName(subjectName, markerIndex); //get the name of the specified subjects marker at index specified by markerIndex.
            listOfMarkerNames[markerIndex] = outputMarkerNameObject.MarkerName;
            Debug.Log("Marker name in index " + markerIndex + "is " + outputMarkerNameObject.MarkerName);
        }

        return listOfMarkerNames;
    } 


    
    public (bool[], float[], float[], float[]) getAllMarkerPositionsAndOcclusionStatus(string subjectName, string[] markerNames)
    {
        //, float[] markerPositionsY, float[] markerPositionsZ

        uint markersInSkeletonCount = (uint) markerNames.Length;
        Output_GetMarkerGlobalTranslation markerGlobalTranslationObject; // a container for the output object, which in turn contains the marker position along with other parameters
        bool[] markerIsOccludedArray = new bool[markersInSkeletonCount];
        float[] markerPositionsX = new float[markersInSkeletonCount];
        float[] markerPositionsY = new float[markersInSkeletonCount];
        float[] markerPositionsZ = new float[markersInSkeletonCount];

        // for each available labeled marker
        for (uint markerIndex = 0; markerIndex < markersInSkeletonCount; markerIndex++)
        {
            markerGlobalTranslationObject = m_Client.GetMarkerGlobalTranslation(subjectName, markerNames[markerIndex]); //get the marker global translation 
            markerIsOccludedArray[markerIndex] = markerGlobalTranslationObject.Occluded; // mark whether or not the marker is occluded (not visible) in this frame
            // store the marker position, which is in millimeters
            markerPositionsX[markerIndex] = (float) markerGlobalTranslationObject.Translation[0]; // [mm]
            markerPositionsY[markerIndex] = (float)markerGlobalTranslationObject.Translation[1]; // [mm]
            markerPositionsZ[markerIndex] = (float)markerGlobalTranslationObject.Translation[2]; // [mm]

        }

        return (markerIsOccludedArray, markerPositionsX, markerPositionsY, markerPositionsZ);
    }

    public uint getFrameNumber()
    {

        Output_GetFrameNumber frameNumberObject; // a container for the output. Result = string marking if successful, .FrameNumber = the frame number.
        frameNumberObject = m_Client.GetFrameNumber();
        return (frameNumberObject.FrameNumber);
    }

    public (ViconDataStreamSDK.CSharp.Result, uint) getDeviceCount()
    {
        Output_GetDeviceCount deviceCountObject; // a container for the output. Result = string marking if successful, .DeviceCount = the number of external devices (Force Plates, EMGs, etc.).
        deviceCountObject = m_Client.GetDeviceCount();
        return (deviceCountObject.Result, deviceCountObject.DeviceCount);
    }

    public (string, ViconDataStreamSDK.CSharp.DeviceType) getDeviceName(uint deviceIndexToRetrieve)
    {
        Output_GetDeviceName deviceNameObject; // a container for the output. Result = string marking if successful, .DeviceCount = the number of external devices (Force Plates, EMGs, etc.).
        deviceNameObject = m_Client.GetDeviceName(deviceIndexToRetrieve);
        return (deviceNameObject.DeviceName, deviceNameObject.DeviceType);

    }

    public uint GetDeviceOutputCount(string deviceName)

    {
        // Initialize an object to hold the return value 
        Output_GetDeviceOutputCount deviceOutputCountObject;

        // Get the device output count for the specified device 
        deviceOutputCountObject = m_Client.GetDeviceOutputCount(deviceName);

        // Return the device output count 
        return (deviceOutputCountObject.DeviceOutputCount);
    }



    public (string, ViconDataStreamSDK.CSharp.Unit) GetDeviceOutputName(string deviceName, uint deviceOutputIndex)
    {
        // Initialize an object to hold the return value 
        Output_GetDeviceOutputName deviceOutputNameObject;

        // Get the device output name for the specified device and device output index 
        deviceOutputNameObject = m_Client.GetDeviceOutputName(deviceName, deviceOutputIndex);

        // Return the device output name and SI unit 
        return (deviceOutputNameObject.DeviceOutputName, deviceOutputNameObject.DeviceOutputUnit);
    }



    public (string, string, ViconDataStreamSDK.CSharp.Unit) GetDeviceOutputNameWithComponentName(string deviceName, uint deviceOutputIndex)
    {
        // Initialize an object to hold the return value 
        Output_GetDeviceOutputComponentName deviceOutputNameObject;

        // Get the device output name with component name for the specified device and device output index 
        deviceOutputNameObject = m_Client.GetDeviceOutputComponentName(deviceName, deviceOutputIndex);

        // Return the device output name, component name, and SI unit 
        return (deviceOutputNameObject.DeviceOutputName, deviceOutputNameObject.DeviceOutputComponentName, deviceOutputNameObject.DeviceOutputUnit);
    }


    // This function gets the output value for a given output component of a given device.
    // If multiple subsamples are available for this output component, the first one is returned.
    // To get specific subsamples, use the GetDeviceOutputSubsampleValue() function. 
    public float GetDeviceOutputValue(string deviceName, string deviceOutputName, string deviceOutputComponentName)
    {
        // Initialize an object to hold the return value 
        Output_GetDeviceOutputValue deviceOutputValueObject;

        // Get the device output value 
        deviceOutputValueObject = m_Client.GetDeviceOutputValue(deviceName, deviceOutputName, deviceOutputComponentName);

        // Return the device output component value. If multiple subsamples are available this frame, this function returns the value for the first subsample of the frame 
        return ((float) deviceOutputValueObject.Value);
    }


    public uint GetDeviceOutputSubsamplesAvailablePerFrame(string deviceName, string deviceOutputName, string deviceOutputComponentName)
    {
        // Initialize an object to hold the return value 
        Output_GetDeviceOutputSubsamples deviceOutputSubsamplesAvailableObject;

        // Get the device output value 
        deviceOutputSubsamplesAvailableObject = m_Client.GetDeviceOutputSubsamples(deviceName, deviceOutputName, deviceOutputComponentName);

        // Return the number of subsamples available for the given output component each frame 
        return (deviceOutputSubsamplesAvailableObject.DeviceOutputSubsamples);
    }



    public float GetDeviceOutputSubsampleValue(string deviceName, string deviceOutputName, string deviceOutputComponentName, uint subsampleIndex)
    {
        // Initialize an object to hold the return value 
        Output_GetDeviceOutputValue deviceOutputValueObject;

        // Get the device output value 
        deviceOutputValueObject = m_Client.GetDeviceOutputValue(deviceName, deviceOutputName, deviceOutputComponentName, subsampleIndex);

        // Return the device output component value for the requested subframe 
        return ((float)deviceOutputValueObject.Value);
    }

    public (ViconDataStreamSDK.CSharp.Result, uint) GetForcePlateCount()
    {
        // Initialize an object to hold the return value 
        Output_GetForcePlateCount forcePlateCountObject;

        // Get the force plate count object
        forcePlateCountObject = m_Client.GetForcePlateCount();

        return (forcePlateCountObject.Result, forcePlateCountObject.ForcePlateCount);
    }

    public (ViconDataStreamSDK.CSharp.Result, Vector3) GetForcePlateGlobalCentreOfPressure(uint forcePlateIndex)
    {
        // Initialize an object to hold the return value 
        Output_GetGlobalCentreOfPressure globalCenterOfPressureObject;

        // Get the center of pressure data object
        globalCenterOfPressureObject = m_Client.GetGlobalCentreOfPressure(forcePlateIndex);

        // Store the COP components
        Vector3 globalCenterOfPressureForThisPlate = new Vector3((float)globalCenterOfPressureObject.CentreOfPressure[0],
            (float)globalCenterOfPressureObject.CentreOfPressure[1],
            (float)globalCenterOfPressureObject.CentreOfPressure[2]);

        return (globalCenterOfPressureObject.Result,globalCenterOfPressureForThisPlate);
    }


    public (ViconDataStreamSDK.CSharp.Result, Vector3) GetForcePlateForceInViconGlobalCoords(uint forcePlateIndex)
    {
        // Initialize an object to hold the return value 
        Output_GetGlobalForceVector globalForceObject;

        // Get the center of pressure data object
        globalForceObject = m_Client.GetGlobalForceVector(forcePlateIndex);

        // Store the force components
        Vector3 globalForceComponents = new Vector3((float)globalForceObject.ForceVector[0],
            (float)globalForceObject.ForceVector[1],
            (float)globalForceObject.ForceVector[2]);

        return (globalForceObject.Result, globalForceComponents);
    }


    public (ViconDataStreamSDK.CSharp.Result, Vector3) GetForcePlateMomentInViconGlobalCoords(uint forcePlateIndex)
    {
        // Initialize an object to hold the return value 
        Output_GetGlobalMomentVector globalMomentObject;

        // Get the center of pressure data object
        globalMomentObject = m_Client.GetGlobalMomentVector(forcePlateIndex);

        // Store the force components
        Vector3 globalMomentComponents = new Vector3((float)globalMomentObject.MomentVector[0],
            (float)globalMomentObject.MomentVector[1],
            (float)globalMomentObject.MomentVector[2]);

        return (globalMomentObject.Result, globalMomentComponents);
    }


    public (ViconDataStreamSDK.CSharp.Result,float) GetAnalogPinVoltageGivenDeviceAndComponentNames(string deviceName, string pinComponentName)
    {
        // Call the SDK function to get the component value given device and component name
        Output_GetDeviceOutputValue pinValueResult = m_Client.GetDeviceOutputValue(deviceName, pinComponentName);

        // Extract the pin voltage value 
        float pinVoltageValue = (float) pinValueResult.Value;

        return (pinValueResult.Result, pinVoltageValue);
    }

    //******************************************************************************************************


    private void OnDisable()
  {
    if (bThreadRunning)
    {
      bThreadRunning = false;
      m_Thread.Join();
    }

  }
  private void UpdateSubjectFilter()
  {
    if (!String.IsNullOrEmpty( SubjectFilter ) && !bSubjectFilterSet)
    {
      string[] Subjects = SubjectFilter.Split(',');
      foreach (string Subject in Subjects)
      {
        if (IsRetimed)
        {
          if( m_RetimingClient.AddToSubjectFilter(Subject.Trim()).Result == Result.Success )
          {
            bSubjectFilterSet = true;
          }
        }
        else
        {
          if( m_Client.AddToSubjectFilter(Subject.Trim()).Result == Result.Success )
          {
            bSubjectFilterSet = true;
          }
        }
      }
    }
  }
  void OnDestroy()
  {
    DisConnect();

    m_Client = null;
    m_RetimingClient = null;
  }
}

