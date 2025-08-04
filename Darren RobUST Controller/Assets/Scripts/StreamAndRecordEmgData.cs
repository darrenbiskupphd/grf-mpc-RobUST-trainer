using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class StreamAndRecordEmgData : MonoBehaviour
{
	#region private members 	
	// TCPListener to listen for incoming TCP connection 	
	private TcpListener tcpListenerDataPort;
	private TcpListener tcpListenerCommandPort; 
	// Port number
	private int sendEmgCommandPortNumber = 50040;
	private int readEmgDataPortNumber = 50043;

	// Background thread for TcpServer workload. 	
	private Thread tcpListenerDataThread;
	private Thread tcpListenerCommandThread;
	// Create handle to connected tcp client. 	
	private TcpClient connectedTcpClientDataStream = new TcpClient();
	private TcpClient connectedTcpClientCommandStream = new TcpClient();
	// Create handle to connected client socket. This allows for live monitoring of connection.
	private Socket connectedDataStreamClientSocket;
	private Socket connectedCommandStreamClientSocket;

	// Message features
	private string delimiter = ",";
	private string terminator = "\r\n";
	private string delsysCommandTerminator = "\r\n\r\n";

	// Whether or not the Delsys base station is set up and ready for a hardware sync signal
	private bool emgBaseStationReadyForSyncSignal = false;

	// Messages expected from base station
	private const string delsysBaseStationOkMessage = "OK";

	// Base station settings tracking
	// Start trigger base station setting
	private bool baseStationStartTriggerSet = false;
	// Stop trigger base station setting
	private bool baseStationStopTriggerSet = false;
	// Whether or not we've sent the START command (to do after the triggers are set)
	private bool emgStartCommandSentAfterTriggersArmed = false;
	// Flag indicating if we're waiting for a base station response 
	private bool waitingOnBaseStationResponse = false;

	// Level manager 
	public LevelManagerScriptAbstractClass levelManager; 

	// The General Data Recorder
	public GameObject generalDataRecorderObject;
	private GeneralDataRecorder generalDataRecorderScript;

	// The time synchronizer object
	public GameObject timeKeeperObject;
	private TimeKeeperForDataSyncScript timeKeeperScript;

	// Tracking expected time of sample
	private float emgSampleRate = 2000.0f; // despite what the getStreamSampleRate command returns, this is the 
										   // sample rate (2000 Hz). Upsampling is on by default - see the Delsys Trigno sdk manual.
	private float timeStepInSeconds;
	private float thisSampleExpectedTimeInSeconds = 0.0f;

	// Command IDs
	private const string armBaseStationForHardwareStartSync = "B"; // Arm base station for a hardware start stream signal ("B"eginning)
	private const string armBaseStationForHardwareStopSync = "E"; // Arm base station for a hardware stop stream signal ("E"nd)
	private const string queryTriggerState = "Q";

	private const string sendStartDataStreamCommandIdString = "S";
	private const string getStreamSampleRateCommandIdString = "R";

	// pending command flags (like a cheap version of a queue for initialization messages
	private bool pendingExcursionCenterMessagetoSendOnTcpConnectFlag = false;
	private bool pendingExcursionLimitsMessagetoSendOnTcpConnectFlag = false;
	private bool pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag = false;

	// Most recent structure matrices (computed on a per-Vicon-frame basis)
	Vector3[] columnsOfForceStructureMatrix;
	Vector3[] columnsOfTorqueStructureMatrix;
	#endregion

	// Should we be streaming EMG data? Determined by level manager.
	private bool streamingEmgDataFlag;

	// Count packets at the start of EMG data streaming
	private int numEmgPacketsReceived = 0;
	private int minNumberOfPacketsReceivedForSuccess = 10;
	private bool streamingEmgSuccessFlag = false;
	public GameObject emgStreamingIndicatorObject;
	public Material emgStreamingSuccessColor;
	public bool streamingEmgSuccessColorChanged = false;

	// Use this for initialization
	void Start()
	{

		// Load the flag that indicates whether or not we're streaming EMG data. 
		// If we're not, this script should do nothing. 
		streamingEmgDataFlag = levelManager.GetEmgStreamingDesiredStatus();

		// If we are streaming EMG data
        if (streamingEmgDataFlag)
        {
			// Get a reference to the general data recorder script
			generalDataRecorderScript = generalDataRecorderObject.GetComponent<GeneralDataRecorder>();

			// Get a reference to the time keeper script
			timeKeeperScript = timeKeeperObject.GetComponent<TimeKeeperForDataSyncScript>();

			// Compute the EMG time step based on sample rate
			timeStepInSeconds = 1.0f / emgSampleRate;

			// Start Tcp client command thread 		
			tcpListenerCommandThread = new Thread(new ThreadStart(ConnectToEmgCommandStream));
			tcpListenerCommandThread.IsBackground = true;
			tcpListenerCommandThread.Start();

			// Start Tcp client data stream thread 		
			tcpListenerDataThread = new Thread(new ThreadStart(ConnectToEmgDataStreamAndRecordData));
			tcpListenerDataThread.IsBackground = true;
			tcpListenerDataThread.Start();



			// Set the EMG data .csv column header names
			// (LATER, include Unity time stamp, most recent Vicon frame #, and an EMG-based time stamp 
			// that we track based on sample rate).
			string[] csvEmgDataHeaderNames = new string[]{"SAMPLE_TIME_BASED_ON_RATE",
													  "MOST_RECENT_UNITY_FRAME_START_TIME_S",
													  "EMG_1", "EMG_2", "EMG_3" , "EMG_4",
													  "EMG_5", "EMG_6", "EMG_7" , "EMG_8",
													  "EMG_9", "EMG_10", "EMG_11" , "EMG_12",
													  "EMG_13", "EMG_14", "EMG_15" , "EMG_16"};
			generalDataRecorderScript.setCsvEmgDataRowHeaderNames(csvEmgDataHeaderNames);
		}
	}

	// Update is called once per frame
	void Update()
	{


		// If we haven't changed the indicator saying we've started the EMG stream successfully
		// but we have started the stream
		if (streamingEmgSuccessFlag == true)
		{
			// Change the color of the EMG data streaming indicator
			emgStreamingIndicatorObject.GetComponent<MeshRenderer>().material = emgStreamingSuccessColor;
			Debug.Log("EMG streaming data. Changing indicator color to green.");
			// Set the success flag to false so that we only change the material once.
			streamingEmgSuccessFlag = false;
			streamingEmgSuccessColorChanged = true;
		}



		if (streamingEmgDataFlag) 
		{
			// Set up the base station for a hardware trigger START and STOP signal, if we haven't yet done so.
			if (emgBaseStationReadyForSyncSignal == false)
			{
				if (connectedTcpClientDataStream != null)
				{
					if (connectedDataStreamClientSocket != null)
					{
						if (connectedDataStreamClientSocket.Connected == true)
						{
							// If the base station start trigger is not set (not acknowledged with OK) and 
							// we're not waiting on a response
							if (baseStationStartTriggerSet == false && waitingOnBaseStationResponse == false)
							{
								// Send the arm START trigger command
								SendCommandToDelsysEmgBaseStation(armBaseStationForHardwareStartSync);

								// Set the waiting for response flag 
								waitingOnBaseStationResponse = true;
							}

							// If the base station start trigger is set (acknowledged with OK) and 
							// the base station stop trigger is NOT set (not acknowledged with OK) and 
							// we're not waiting on a response
							if (baseStationStartTriggerSet == true && baseStationStopTriggerSet == false && waitingOnBaseStationResponse == false)
							{
								// Send the arm STOP trigger command
								SendCommandToDelsysEmgBaseStation(armBaseStationForHardwareStopSync);

								// Set the waiting for response flag 
								waitingOnBaseStationResponse = true;

							}

							// If both triggers are set to true 
							if (baseStationStartTriggerSet == true && baseStationStopTriggerSet == true &&
								emgStartCommandSentAfterTriggersArmed == false && waitingOnBaseStationResponse == false)
							{
								// Send a start command to the Delsys base station
								SendCommandToDelsysEmgBaseStation(sendStartDataStreamCommandIdString);

								// Set the waiting for response flag 
								waitingOnBaseStationResponse = true;
							}

							if (emgStartCommandSentAfterTriggersArmed == true && waitingOnBaseStationResponse == false)
							{
								// Then set the flag indicating setup is done and the base station is ready for a hardware sync
								emgBaseStationReadyForSyncSignal = true;

								Debug.Log("EMG streaming service is ready for sync start trigger.");
							}

							// If the "OK" received count is zero AND we aren't still waiting for a response
							// Else if the "OK" received count is one AND we aren't still waiting for a response
							// Send the arm stop trigger command
							// Else if the "OK" received count is two and the emgBaseStationReadyForSyncStartFlag is false
							// Set the flag indicating the base station is ready for a sync signal


							// Send the start command 
							//Debug.Log("Sending START command to Delsys EMG data stream");
							//SendCommandToDelsysEmgBaseStation(sendStartDataStreamCommandIdString);

							// Send the query sensor rate command 
							/*Debug.Log("Sending query sensor sample rate command to Delsys EMG data stream");
							SendCommandToDelsysEmgBaseStation(getStreamSampleRateCommandIdString);*/

							// For now, set the started flag to true. Could do a check for a return message.
							//emgDataStreamStartedFlag = true;
						}
					}
				}
            }
		} // End if statement on the emgStreamingFlag status
	}


	// START: public functions **************************************************************************


	public bool IsBaseStationReadyForSyncSignal()
    {
		return emgBaseStationReadyForSyncSignal;
    }


	// If a task has blocks, then we'd like to stop the EMG and Vicon at the end of a block, 
	// then restart them both for the next block. To do so, we need to reset the variables 
	// that govern message sending to the Delsys base station.
	public void ResetVariablesToEnableResettingBaseStation()
    {
		emgBaseStationReadyForSyncSignal = false;
		baseStationStartTriggerSet = false;
		baseStationStopTriggerSet = false;
		emgStartCommandSentAfterTriggersArmed = false;
	}

	// Note, we must send a start command to prepare the base station for a trigger (it won't 
	// start automatically streaming if the START trigger is on). 
	// This function can be used for the second, third, etc. block of a task.
	// NOTE: THIS FUNCTION IS NOT USED! Currently, the Update() flow of this script sends the sTART command
	// automatically. 
	public void SendStartCommandToBaseStation()
    {
		// Send a start command to the Delsys base station
		SendCommandToDelsysEmgBaseStation(sendStartDataStreamCommandIdString);

		// Set the flag indicating that the base station is not ready yet 
		emgBaseStationReadyForSyncSignal = false;

		// Set the flag indicating that we haven't yet received confirmation of our start command
		emgStartCommandSentAfterTriggersArmed = false;

		// Set the waiting for response flag 
		waitingOnBaseStationResponse = true;

	}

	// END: public functions **************************************************************************



	private void SendCommandToDelsysEmgBaseStation(string commandIdentifier)
	{
		if (connectedTcpClientCommandStream != null)
		{
			// Compose the message given tghe command identifier
			(byte[] composedMessage, bool validMessage) = ComposeCommandGivenIdentifier(commandIdentifier);

			// If the message is valid  
			if (validMessage)
			{
				// Sending message log
				Debug.Log("Sending message with identifer: " + commandIdentifier);

				// Send the command over the network
				SendComposedMessageToDelsysTrignoSystem(composedMessage);
			}
		}
	}


	private (byte[], bool) ComposeCommandGivenIdentifier(string commandIdentifier)
	{
		// Compose the complete command message based on the command identifier
		string serverMessage = ""; // initialize as empty string
		bool validMessage = false;
		if (commandIdentifier == sendStartDataStreamCommandIdString)
		{
			serverMessage = "START" + delsysCommandTerminator;
			validMessage = true;
		}
		else if(commandIdentifier == getStreamSampleRateCommandIdString)
		{
			serverMessage = "SENSOR 1 CHANNEL 1 RATE?" + delsysCommandTerminator;
			validMessage = true;
		}
		else if(commandIdentifier == armBaseStationForHardwareStartSync) // arm base station for hardware START signal
		{
			
			serverMessage = "TRIGGER START ON" + delsysCommandTerminator;
			validMessage = true;
		}
		else if (commandIdentifier == armBaseStationForHardwareStopSync) // arm base station for hardware STOP signal
		{
			serverMessage = "TRIGGER STOP ON" + delsysCommandTerminator;
			validMessage = true;
		}
		else if (commandIdentifier == queryTriggerState)
        {
			serverMessage = "TRIGGER?" + delsysCommandTerminator;
			validMessage = true;
		}
		else
		{
			// do nothing since command was invalid
		}

		// Convert string message to byte array.                 
		byte[] serverMessageAsByteArray = Encoding.ASCII.GetBytes(serverMessage);

		Debug.Log("Sending command to Delsys base station: " + serverMessage);
		// return byte[] message and whether or not the message is valid 
		return (serverMessageAsByteArray, validMessage);
	}

	private void SendComposedMessageToDelsysTrignoSystem(byte[] serverMessageAsByteArray)
	{
		if (connectedTcpClientCommandStream == null || connectedDataStreamClientSocket.Connected == false)
		{
			Debug.Log("No TCP client - not sending message.");
			return;
		}

		try
		{
			// Get a stream object for writing. 			
			NetworkStream stream = connectedTcpClientCommandStream.GetStream();
			if (stream.CanWrite) // if we are connected and ready to write to the stream
			{
				// Write byte array to socketConnection stream.
				int offset = 0;
				stream.Write(serverMessageAsByteArray, offset, serverMessageAsByteArray.Length);
			}
		}
		catch (SocketException socketException) // if we could not get the network stream
		{
			// throw an exception
			Debug.Log("Socket exception: " + socketException);
		}
	}

	/// <summary> 	
	/// Runs in background TcpServerThread; Handles incomming TcpClient requests 	
	/// </summary> 	
	private void ConnectToEmgDataStreamAndRecordData()
	{
		try
		{
			// Create listener on localhost port 8052. 			
			//tcpListenerDataPort = new TcpListener(IPAddress.Parse("127.0.0.1"), readEmgDataPortNumber);
			//tcpListenerDataPort.Start();
			Debug.Log("Emg data client will try connecting.");
			Byte[] incomingDataBuffer = new Byte[1664]; // each packet  has 64 bytes, buffer holds 26 packets
			Byte[] packetBuffer = new byte[64];
			int bytesInPacket = 64; // All 16 emgs send a 4-byte "Single" float. 
			int numEmgChannelsInPacket = 16;
			int numFloatsInPacket = bytesInPacket / numEmgChannelsInPacket;
			int offset = 0; // When reading packets, we don't need an "offset" since we don't need to skip any header bytes
			while (true)
			{
				// Get the local host IP address
				IPAddress ipAddress = IPAddress.Loopback;
				Debug.Log("Local host IP address is: " + ipAddress.ToString());
				int portNumber = readEmgDataPortNumber;
				IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, portNumber);

				//Uses a remote endpoint to establish a socket connection.
				connectedTcpClientDataStream.Connect(ipEndPoint); // blocks until connect or failure******
				Debug.Log("EMG data streamer is connected to Delsys TCP");
				// Get a stream object for reading 					
				using (NetworkStream stream = connectedTcpClientDataStream.GetStream())
				{
					// Store a reference to the client socket object
					connectedDataStreamClientSocket = connectedTcpClientDataStream.Client;
					while (connectedDataStreamClientSocket.Connected == true)
					{
						// Read incoming stream into the incoming data buffer. 			
						// The while loop argument copies the incoming bytes into the incomingDataBytesBuffer.
						// It blocks until the data is available, so will always return the requested bytes. 		
						int nextLength;
						int receivedLength = 0;
						while (receivedLength < bytesInPacket) // while we still haven't received a full packet
						{
							// Try to read the remaining bytes needed for a complete packet
							nextLength = stream.Read(packetBuffer, 0, bytesInPacket - receivedLength);

							if (nextLength == 0)
							{
								//Throw an exception? Something else?
								//The socket's never going to receive more data
							}
							receivedLength += nextLength;
						}

						// Convert the packet into the EMG float data. 
						// Note, in .NET, a float is a 4-byte value, i.e. a "single."
						List<float> emgDataPacketAscendingChannelOrder = new List<float>();

						// Add the time based on sample number and sample rate
						emgDataPacketAscendingChannelOrder.Add(thisSampleExpectedTimeInSeconds);
						thisSampleExpectedTimeInSeconds += timeStepInSeconds;

						// Add the Unity frame start time
						emgDataPacketAscendingChannelOrder.Add(timeKeeperScript.GetMostRecentUnityFrameTime());

						// Loop through each floats = the EMG indices and store.
						int startIndex = 0;
						for (int emgIndex = 0; emgIndex < numEmgChannelsInPacket; emgIndex++)
						{
							// Read the 4 bytes of data for that EMG channel into a float and store
							emgDataPacketAscendingChannelOrder.Add(System.BitConverter.ToSingle(packetBuffer, startIndex));

							// Increment the start index, so we're converting the next 4 bytes into a float
							startIndex += 4;
						}

						// Received an EMG packet 
						/*Debug.Log("Received an EMG packet of EMG data with values: " + emgDataPacketAscendingChannelOrder);
						for (int emgChannelIndex = 0; emgChannelIndex < emgDataPacketAscendingChannelOrder.Count; emgChannelIndex++)
						{
							Debug.Log("EMG Value: " + emgDataPacketAscendingChannelOrder[emgChannelIndex]);

						}*/

						// Send the float data to the General Data Recorder to store
						//Debug.Log("Storing packet of EMG data with " + numEmgChannelsInPacket + "channels of data.");
						generalDataRecorderScript.storeRowOfEmgData(emgDataPacketAscendingChannelOrder);

						// If we've received fewer than 10 packets of EMG data
						if(numEmgPacketsReceived < minNumberOfPacketsReceivedForSuccess)
                        {
							// Increment a counter of received packets
							numEmgPacketsReceived = numEmgPacketsReceived + 1;
							Debug.Log("Received a total of " + numEmgPacketsReceived + " packets");
						}
						//else if we've received 10 or more packets of EMG data
						else
						{
							if (streamingEmgSuccessColorChanged == false)
                            {
								// Set the flag indicating we've successfully started the EMG data stream
								streamingEmgSuccessFlag = true;
							}

						}





					}
				}
			}
		}
		catch (SocketException socketException)
		{
			Debug.Log("SocketException " + socketException.ToString());
		}
	}


	/// <summary> 	
	/// Runs in background TcpServerThread; Handles incomming TcpClient requests 	
	/// </summary> 	
	private void ConnectToEmgCommandStream()
	{
		try
		{
			Debug.Log("In command client thread");
			// Create listener on localhost port 8052. 			
			//tcpListenerCommandPort = new TcpListener(IPAddress.Parse("127.0.0.1"), sendEmgCommandPortNumber);
			//tcpListenerCommandPort.Start();
			Debug.Log("Emg command client will try connecting.");
			Byte[] incomingDataBuffer = new Byte[1664]; // each packet  has 64 bytes, buffer holds 26 packets
			Byte[] packetBuffer = new byte[64];
			int bytesInPacket = 64; // All 16 emgs send a 4-byte "Single" float. 
			int numEmgChannelsInPacket = 16;
			int numFloatsInPacket = bytesInPacket / numEmgChannelsInPacket;
			int offset = 0; // When reading packets, we don't need an "offset" since we don't need to skip any header bytes
			while (true)
			{
				// Get the local host IP address
				IPAddress ipAddress = IPAddress.Loopback;
				Debug.Log("Local host IP address is: " + ipAddress.ToString());
				int portNumber = sendEmgCommandPortNumber;
				IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, portNumber);

				//Uses a remote endpoint to establish a socket connection.
				Debug.Log("Command client is trying to connect to base station at IP/port: " + ipAddress + " / " + portNumber);
				connectedTcpClientCommandStream.Connect(ipEndPoint); // blocks until connect or failure******
				Debug.Log("EMG command TcpClient is connected to Delsys TCP");
				// Get a stream object for reading 					
				using (NetworkStream stream = connectedTcpClientCommandStream.GetStream())
				{
					// Store a reference to the client socket object
					connectedCommandStreamClientSocket = connectedTcpClientCommandStream.Client;

					int length;
					// Read incomming stream into byte arrary. 						
					while ((length = stream.Read(incomingDataBuffer, 0, incomingDataBuffer.Length)) != 0)
					{
						var incomingData = new byte[length];
						Array.Copy(incomingDataBuffer, 0, incomingData, 0, length);
						// Convert byte array to string message. 							
						string clientMessage = Encoding.ASCII.GetString(incomingData);
						// Check the received message to see if it's an "OK" (acknowledgement of command)
                        if (clientMessage.Contains(delsysBaseStationOkMessage))
                        {
							if(baseStationStartTriggerSet == false) // We assume that the first OK is for the start trigger set
                            {
								// Set the start trigger set to true
								baseStationStartTriggerSet = true;

								// Note we're no longer waiting on a response from the base station
								waitingOnBaseStationResponse = false;

							} else if(baseStationStopTriggerSet == false) // We assume that the second OK is for the stop trigger set
							{
								// Set the stop trigger set to true
								baseStationStopTriggerSet = true;

								// Note we're no longer waiting on a response from the base station
								waitingOnBaseStationResponse = false;
							}
							else if (emgStartCommandSentAfterTriggersArmed == false) // We assume that the third OK message is the
                                                                                     // START response 
							{
								// Note that we sent the START command successfully
								emgStartCommandSentAfterTriggersArmed = true;

								// Note we're no longer waiting on a response from the base station
								waitingOnBaseStationResponse = false;

							}
							else // We do nothing
                            {

                            }
                        }
						Debug.Log("client message received as: " + clientMessage);
					}

				}
			}
		}
		catch (SocketException socketException)
		{
			Debug.Log("SocketException " + socketException.ToString());
		}
	}

	/*	private IPAddress GetLocalHostIpAddress()
		{
			var ipAddresses = Dns.GetHostEntry(Dns.GetHostName());
			IPAddress localHostIpAddress = IPAddress.Loopback;
			foreach (var ip in ipAddresses.AddressList)
			{
				Debug.Log("Possible host address: " + ip.ToString());
				if (ip.AddressFamily == AddressFamily.InterNetwork)
				{
					// Then this is the local host IP. Store.
					localHostIpAddress = ip;
				}
				else
				{
					return null;
				}
			}
		}*/



	/// <summary> 	
	/// Send message to client using socket connection. 	
	/// </summary> 	
	public void SendControlPointCoordinate(Vector3 controlPointCoordinates)
	{
		if (connectedTcpClientCommandStream == null)
		{
			Debug.Log("No TCP client - not sending message.");
			return;
		}

		try
		{
			// Get a stream object for writing. 			
			NetworkStream stream = connectedTcpClientCommandStream.GetStream();
			if (stream.CanWrite)
			{
				string commandIdentifier = "C";
				string delimiter = ",";
				string terminator = "\r\n";
				string serverMessage = commandIdentifier + delimiter + controlPointCoordinates.x + delimiter + controlPointCoordinates.y + delimiter + controlPointCoordinates.z + terminator;
				// Convert string message to byte array.                 
				byte[] serverMessageAsByteArray = Encoding.ASCII.GetBytes(serverMessage);
				// Write byte array to socketConnection stream.
				int offset = 0;
				stream.Write(serverMessageAsByteArray, offset, serverMessageAsByteArray.Length);
				Debug.Log("Server sent control point coords: (" + controlPointCoordinates.x + ", " + controlPointCoordinates.y + ", " + controlPointCoordinates.z + ")");
			}
		}
		catch (SocketException socketException)
		{
			Debug.Log("Socket exception: " + socketException);
		}
	}


	void OnApplicationQuit()
	{
		Debug.Log("Application ending after " + Time.time + " seconds");

		// Close the connected TCP client EMG data stream and associated TCP listener
		if (connectedTcpClientCommandStream != null)
		{
			connectedTcpClientCommandStream.Close();
		}
		//tcpListenerDataPort.Stop();

		// Close the connected TCP client command stream and associated TCP listener
		if (connectedTcpClientDataStream != null)
		{
			connectedTcpClientDataStream.Close();
		}
		//tcpListenerCommandPort.Stop();
	}
}



