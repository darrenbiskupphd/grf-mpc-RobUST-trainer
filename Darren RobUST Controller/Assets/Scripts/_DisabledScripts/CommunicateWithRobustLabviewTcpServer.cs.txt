using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class CommunicateWithRobustLabviewTcpServer : MonoBehaviour
{
	#region private members 	
	// TCPListener to listen for incomming TCP connection 	
	private TcpListener tcpListener;
	// Port number
	private int portNumber = 8052;
	// Background thread for TcpServer workload. 	
	private Thread tcpListenerThread;
	// Create handle to connected tcp client. 	
	private TcpClient connectedTcpClient;
	// Create handle to connected client socket. This allows for live monitoring of connection.
	private Socket connectedClientSocket;

	// The excursion level manager
	public GameObject levelManager; //the level manager for the current task
	private LevelManagerScriptAbstractClass levelManagerScript; // the script of the level manager. Part of an abstract class.

	// The COM manager/Vicon data manager
	public GameObject ComManagerObject;
	private ManageCenterOfMassScript ComManagerScript;

	// The structure matrix builder object
	public GameObject structureMatrixBuilderObject;
	private BuildStructureMatricesForBeltsThisFrameScript structureMatrixBuilderScript;

	// The force field high-level controller object
	public GameObject forceFieldControllerObject;
	private ForceFieldHighLevelControllerScript forceFieldControllerScript;

	// Message features
	private string delimiter = ",";
	private string terminator = "\r\n";

	// Command IDs
	private const string sendControlPointCommandIdString = "C";
	private const string sendCenterPointDefiningExcursionDistanceStart = "O";
	private const string sendPosturalStarExcursionLimitsInViconFrameIdString = "E";
	private const string sendForceFieldModeSpecifier = "F";
	private const string sendTrunkBeltStructureMatrixSpecifier = "T";
	private const string sendTrunkBeltCorrespondingMotorsToSMatrixSpecifier = "TM";
	private const string sendPelvicBeltStructureMatrixSpecifier = "P";
	private const string sendPelvicBeltCorrespondingMotorsToSMatrixSpecifier = "PM";
	private const string sendMotorNumbersAndDesiredTensionsAndFrameNumberSpecifier = "K"; // the key command we use at this point
	private const string sendTaskInformationStringSpecifier = "I";
	private const string taskOverStringSpecifier = "S";

	// pending command flags (like a cheap version of a queue for initialization messages
	private bool pendingTaskInfoStringMessageFlag = false;
	private bool pendingExcursionCenterMessagetoSendOnTcpConnectFlag = false;
	private bool pendingExcursionLimitsMessagetoSendOnTcpConnectFlag = false;
	private bool pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag = false;

	// Most recent structure matrices (computed on a per-Vicon-frame basis)
	Vector3[] columnsOfForceStructureMatrix;
	Vector3[] columnsOfTorqueStructureMatrix;

	// The most recent task information string (subject number, date, time)
	private string subjectDateTimeInfoString = "taskInfoStringNotSet";
	#endregion


	// Use this for initialization
	void Start()
	{
		// Start TcpServer background thread 		
		tcpListenerThread = new Thread(new ThreadStart(ListenForIncomingRequests));
		tcpListenerThread.IsBackground = true;
		tcpListenerThread.Start();

		// Get a reference to the level manager script 
		levelManagerScript = levelManager.GetComponent<LevelManagerScriptAbstractClass>();

		// Get a reference to the COM manager
		ComManagerScript = ComManagerObject.GetComponent<ManageCenterOfMassScript>();

		// Get a reference to the structure matrix builder script
		structureMatrixBuilderScript = structureMatrixBuilderObject.GetComponent<BuildStructureMatricesForBeltsThisFrameScript>();

		// Get a reference to the force field high-level controller script
		forceFieldControllerScript = forceFieldControllerObject.GetComponent<ForceFieldHighLevelControllerScript>();
}

	// Update is called once per frame
	void Update()
	{
		// Disconnect from the TcpClient if the socket is no longer active
		if (connectedTcpClient != null)
        {
			if(connectedClientSocket.Connected == false)
            {
				Debug.Log("TCP client connection was lost. Closing that client socket.");
				connectedTcpClient.Close();
				// client.Close();
			}
		}

		// Manage "pending" commands to send to RobUST
		if (connectedTcpClient != null)
		{
			if (pendingExcursionCenterMessagetoSendOnTcpConnectFlag == true)
			{
				pendingExcursionCenterMessagetoSendOnTcpConnectFlag = false;
				SendExcursionLimitCenterPositionInViconFrameToRobot();
			}

			if (pendingExcursionLimitsMessagetoSendOnTcpConnectFlag == true)
			{
				pendingExcursionLimitsMessagetoSendOnTcpConnectFlag = false;
				SendExcursionLimitsInViconFrameToRobot();
			}

			if (pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag == true)
			{
				pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag = false;
				SendCommandToRobust(sendForceFieldModeSpecifier);
			}

			if (pendingTaskInfoStringMessageFlag == true)
			{
				Debug.Log("Sending pending task info string.");
				pendingTaskInfoStringMessageFlag = false; // note that there is not a pending message because we're sending it. 
				SendCommandToRobust(sendTaskInformationStringSpecifier);
			}
		}
	}


	// START: public functions **************************************************************************

	// AFTER UPDATE: this is the key function - the one that sends desired cable tensions to the PXI. Many of the other commands
	// will likely now be unnecessary. 
	public void SendMotorNumbersAndDesiredTensionsAndViconFrameNumberToRobot()
    {
		// Try to send the key data (cable tensions, motor numbers, and frame number) to the robot
		SendCommandToRobust(sendMotorNumbersAndDesiredTensionsAndFrameNumberSpecifier);
	}

	public void SendCommandWithCurrentTaskInfoString(string taskInfoString)
	{

		// Update the task information string with the passed-in value
		subjectDateTimeInfoString = taskInfoString;

		// Try to send the task information string (Subject number, date, time) to the robot. 
		// Note: making this a separate command from the main tensions command allows us to signal 
		// to Labview that a new task has started and a new tensions file should be created.
		if (connectedTcpClient != null)
		{
			SendCommandToRobust(sendTaskInformationStringSpecifier);
		}
		else // then note that we must send the message as soon as we get a TCP connection
		{
			pendingTaskInfoStringMessageFlag = true;
		}
	}

	public void SendCommandWithTaskOverSpecifier()
	{
		// Send a command that signifies the task is over, and so Labview can warite tensions to file.s
		Debug.Log("Sending task over message.");
		SendCommandToRobust(taskOverStringSpecifier);
	}

	public void SendCommandToSwitchToLiveForcesMode()
    {

    }

	public void SendCommandToSwitchToConstantTensionsMode()
	{

	}


	// Called by the LevelManager once it is in the ActiveBlock state. 
	public void SendUpdatedControlPointForForceFieldToRobot()
    {
		// Try to send the control point to the robot
			SendCommandToRobust(sendControlPointCommandIdString);
	}

	public void SendExcursionLimitCenterPositionInViconFrameToRobot()
	{
		// Try to send the excursion limits center (defining where to start each excursion)
		// in Vicon frame to the robot
		if (connectedTcpClient != null)
        {
			SendCommandToRobust(sendCenterPointDefiningExcursionDistanceStart);
		}
		else // then note that we must send the message as soon as we get a TCP connection
		{
			pendingExcursionCenterMessagetoSendOnTcpConnectFlag  = true;
		}
	}

	public void SendExcursionLimitsInViconFrameToRobot()
	{
		// Try to send the excursion limits in Vicon frame to the robot
		if (connectedTcpClient != null)
		{
			SendCommandToRobust(sendPosturalStarExcursionLimitsInViconFrameIdString);
		}
		else // then note that we must send the message as soon as we get a TCP connection
		{
			pendingExcursionLimitsMessagetoSendOnTcpConnectFlag = true;
		}
	}

	public void SendForceFieldModeSpecifierToRobot()
    {
		// Try to send the excursion limits in Vicon frame to the robot
		if (connectedTcpClient != null)
		{
			SendCommandToRobust(sendForceFieldModeSpecifier);
		}
		else // then note that we must send the message as soon as we get a TCP connection
		{
			pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag = true;
		}
	}

	public void SendTrunkStructureMatrixToRobot(Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix)
    {
		Debug.Log("Sending S matrix to RobUST");
		// First, store the new columns of the structure matrix in instance variables.
		columnsOfForceStructureMatrix = columnsOfForceSMatrix;
		columnsOfTorqueStructureMatrix = columnsOfTorqueSMatrix;

		// Then specify that we want to send a trunk structure matrix command
		SendCommandToRobust(sendTrunkBeltStructureMatrixSpecifier);

	}

	public void SendCommandToRobust(string commandIdentifier)
    {
		if (connectedTcpClient != null)
        {
			// Compose the message given tghe command identifier
			(byte[] composedMessage, bool validMessage) = ComposeCommandGivenIdentifier(commandIdentifier);

			// If the message is valid  
			if (validMessage)
			{
				// Sending message log
				Debug.Log("Sending message with identifer: " + commandIdentifier);

				// Send the command over the network
				SendComposedMessageToRobust(composedMessage);
			}
		}	
	}

	// END: public functions **************************************************************************



	private (byte[], bool) ComposeCommandGivenIdentifier(string commandIdentifier)
	{
		// Compose the complete command message based on the command identifier
		string serverMessage = ""; // initialize as empty string
		bool validMessage = false;
		if (commandIdentifier == sendMotorNumbersAndDesiredTensionsAndFrameNumberSpecifier)
		{
			// Get the current Vicon frame number, retrieved from the COM manager
			uint mostRecentlyAccessedViconFrameNumber = ComManagerScript.getMostRecentlyAccessedViconFrameNumber();
			Debug.Log("Most recently accessed Vicon frame # is: " + mostRecentlyAccessedViconFrameNumber);

			// Get the motor numbers for the trunk belt
			int[] trunkBeltMotorNumbers = structureMatrixBuilderScript.GetOrderedTrunkBeltPulleyMotorNumbers();
			string delimitedMotorNumbers = "";

			// Get the desired tensions for the trunk belt motors (corresponding to the motor numbers above - must be in same order)
			float[] trunkBeltDesiredCableTensions = forceFieldControllerScript.GetMostRecentlyComputedTrunkCableTensions();

			// Compose a string with alternating motor number and corresponding desired cable tension
			string delimitedTrunkBeltMotorNumbersAndDesiredTensionsString = "";
			for (int trunkBeltMotorIndex = 0; trunkBeltMotorIndex < trunkBeltMotorNumbers.Length; trunkBeltMotorIndex++)
			{
				if (trunkBeltMotorNumbers == null)
				{
					Debug.LogError("trunkBeltMotorNumbers is null");
				}

				if (trunkBeltDesiredCableTensions == null)
				{
					Debug.LogError("trunkBeltDesiredCableTensions is null");
				}

				if (trunkBeltMotorIndex < trunkBeltMotorNumbers.Length - 1) // if not the last motor, end with a delimiter
				{
					delimitedTrunkBeltMotorNumbersAndDesiredTensionsString = delimitedTrunkBeltMotorNumbersAndDesiredTensionsString +
						trunkBeltMotorNumbers[trunkBeltMotorIndex] + delimiter + trunkBeltDesiredCableTensions[trunkBeltMotorIndex] +
						delimiter;
				}
				else // if the last motor, don't end with a delimiter
				{
					delimitedTrunkBeltMotorNumbersAndDesiredTensionsString = delimitedTrunkBeltMotorNumbersAndDesiredTensionsString +
						trunkBeltMotorNumbers[trunkBeltMotorIndex] + delimiter + trunkBeltDesiredCableTensions[trunkBeltMotorIndex];
				}

			}

			// Get the motor numbers for the pelvic belt
			int[] pelvicBeltMotorNumbers = structureMatrixBuilderScript.GetOrderedPelvicBeltPulleyMotorNumbers();

			// Get the desired tensions for the pelvic belt motors (corresponding to the motor numbers above - must be in same order)
			float[] pelvicBeltDesiredCableTensions = forceFieldControllerScript.GetMostRecentlyComputedPelvicCableTensions();

			string delimitedPelvicBeltMotorNumbersAndDesiredTensionsString = "";
			for (int pelvicBeltMotorIndex = 0; pelvicBeltMotorIndex < pelvicBeltMotorNumbers.Length; pelvicBeltMotorIndex++)
			{
				if (pelvicBeltMotorIndex < pelvicBeltMotorNumbers.Length - 1) // if not the last motor, end with a delimiter
				{
					delimitedPelvicBeltMotorNumbersAndDesiredTensionsString = delimitedPelvicBeltMotorNumbersAndDesiredTensionsString +
						pelvicBeltMotorNumbers[pelvicBeltMotorIndex] + delimiter + pelvicBeltDesiredCableTensions[pelvicBeltMotorIndex] +
						delimiter;
				}
				else // if the last motor, don't end with a delimiter
				{
					delimitedPelvicBeltMotorNumbersAndDesiredTensionsString = delimitedPelvicBeltMotorNumbersAndDesiredTensionsString +
						pelvicBeltMotorNumbers[pelvicBeltMotorIndex] + delimiter + pelvicBeltDesiredCableTensions[pelvicBeltMotorIndex];
				}

			}

			// Get the motor numbers for the right shank belt
			int[] rightShankBeltMotorNumbers = structureMatrixBuilderScript.GetOrderedRightShankBeltPulleyMotorNumbers();

			// Get the desired tensions for the right shank belt motors (corresponding to the motor numbers above - must be in same order)
			float[] rightShankBeltDesiredCableTensions = forceFieldControllerScript.GetMostRecentlyComputedRightShankCableTensions();

			string delimitedRightShankBeltMotorNumbersAndDesiredTensionsString = "";
			for (int shankBeltMotorIndex = 0; shankBeltMotorIndex < rightShankBeltMotorNumbers.Length; shankBeltMotorIndex++)
			{
				if (shankBeltMotorIndex < rightShankBeltMotorNumbers.Length - 1) // if not the last motor, end with a delimiter
				{
					delimitedRightShankBeltMotorNumbersAndDesiredTensionsString = delimitedRightShankBeltMotorNumbersAndDesiredTensionsString +
						rightShankBeltMotorNumbers[shankBeltMotorIndex] + delimiter + rightShankBeltDesiredCableTensions[shankBeltMotorIndex] +
						delimiter;
				}
				else // if the last motor, don't end with a delimiter
				{
					delimitedRightShankBeltMotorNumbersAndDesiredTensionsString = delimitedRightShankBeltMotorNumbersAndDesiredTensionsString +
						rightShankBeltMotorNumbers[shankBeltMotorIndex] + delimiter + rightShankBeltDesiredCableTensions[shankBeltMotorIndex];
				}

			}

			// Get the motor numbers for the left shank belt
			int[] leftShankBeltMotorNumbers = structureMatrixBuilderScript.GetOrderedLeftShankBeltPulleyMotorNumbers();

			// Get the desired tensions for the left shank belt motors (corresponding to the motor numbers above - must be in same order)
			float[] leftShankBeltDesiredCableTensions = forceFieldControllerScript.GetMostRecentlyComputedLeftShankCableTensions();

			string delimitedLeftShankBeltMotorNumbersAndDesiredTensionsString = "";
			for (int shankBeltMotorIndex = 0; shankBeltMotorIndex < leftShankBeltMotorNumbers.Length; shankBeltMotorIndex++)
			{
				if (shankBeltMotorIndex < leftShankBeltMotorNumbers.Length - 1) // if not the last motor, end with a delimiter
				{
					delimitedLeftShankBeltMotorNumbersAndDesiredTensionsString = delimitedLeftShankBeltMotorNumbersAndDesiredTensionsString +
						leftShankBeltMotorNumbers[shankBeltMotorIndex] + delimiter + leftShankBeltDesiredCableTensions[shankBeltMotorIndex] +
						delimiter;
				}
				else // if the last motor, don't end with a delimiter
				{
					delimitedLeftShankBeltMotorNumbersAndDesiredTensionsString = delimitedLeftShankBeltMotorNumbersAndDesiredTensionsString +
						leftShankBeltMotorNumbers[shankBeltMotorIndex] + delimiter + leftShankBeltDesiredCableTensions[shankBeltMotorIndex];
				}

			}

			// Compose the message string
			// Note that the trunk belt motor number and tensions string ENDS with a delimiter
			serverMessage = commandIdentifier + delimiter + mostRecentlyAccessedViconFrameNumber +
			delimiter + delimitedTrunkBeltMotorNumbersAndDesiredTensionsString + delimiter + delimitedPelvicBeltMotorNumbersAndDesiredTensionsString +
			delimiter + delimitedRightShankBeltMotorNumbersAndDesiredTensionsString + delimiter +
			delimitedLeftShankBeltMotorNumbersAndDesiredTensionsString + terminator;
			Debug.Log("Fully composed cable tensions message for RobUST is: " + serverMessage);


			// Note that we composed a valid message
			validMessage = true;

		}
		else if (commandIdentifier == sendTaskInformationStringSpecifier)
		{

			Debug.Log("Sending task info string server message: " + serverMessage);

			// Compose the message with the only data token as the subject-date-time string
			serverMessage = commandIdentifier + delimiter + subjectDateTimeInfoString + terminator;

			// Note that we composed a valid message
			validMessage = true;
		}
		else if (commandIdentifier == taskOverStringSpecifier) {
			// Compose the message with the only data token as the subject-date-time string
			serverMessage = commandIdentifier + delimiter + terminator;

			Debug.Log("Sending task over message with body: " + serverMessage);

			// Note that we composed a valid message
			validMessage = true;
		}
		else if (commandIdentifier == sendControlPointCommandIdString)
		{
			// Get the control point position from the level manager
			Vector3 controlPointCoordinates = levelManagerScript.GetControlPointForRobustForceField();
			// Compose the message string
			serverMessage = commandIdentifier + delimiter + controlPointCoordinates.x +
				delimiter + controlPointCoordinates.y + delimiter +
				controlPointCoordinates.z + terminator;

			// DEBUG only: print
			/*Debug.Log("Sent the FF control point (x,y,z): (" +
				controlPointCoordinates.x + ", " + controlPointCoordinates.y +
				", " + controlPointCoordinates.z + ")");*/

			// Note that we created a valid message 
			validMessage = true;
		}
		else if (commandIdentifier == sendCenterPointDefiningExcursionDistanceStart)
		{
			Vector3 centerOfExcursionLimitsInViconFrame = levelManagerScript.GetCenterOfExcursionLimitsInViconFrame();
			// Compose the message string
			serverMessage = commandIdentifier + delimiter + centerOfExcursionLimitsInViconFrame.x +
				delimiter + centerOfExcursionLimitsInViconFrame.y + delimiter +
				centerOfExcursionLimitsInViconFrame.z + terminator;

			// DEBUG only: print
			Debug.Log("Sent a center of excursion limits coordinate (x,y,z): (" +
				centerOfExcursionLimitsInViconFrame.x + ", " + centerOfExcursionLimitsInViconFrame.y +
				", " + centerOfExcursionLimitsInViconFrame.z + ")");

			// Note that we created a valid message 
			validMessage = true;
		}
		else if (commandIdentifier == sendPosturalStarExcursionLimitsInViconFrameIdString)
		{
			List<Vector3> excursionDistancesXAndYWithSignsViconUnits = levelManagerScript.GetExcursionLimitsFromExcursionCenterInViconUnits();
			// Compose the message string
			serverMessage = commandIdentifier + delimiter + excursionDistancesXAndYWithSignsViconUnits[0].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[0].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[1].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[1].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[2].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[2].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[3].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[3].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[4].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[4].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[5].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[5].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[6].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[6].y + delimiter +
				excursionDistancesXAndYWithSignsViconUnits[7].x +
				delimiter + excursionDistancesXAndYWithSignsViconUnits[7].y + terminator;
			// Note that we created a valid message 
			validMessage = true;
		}
		else if (commandIdentifier == sendForceFieldModeSpecifier) {
			// Get the current desired force field type, as specified by the level manager
			string desiredForceFieldType = levelManagerScript.GetCurrentDesiredForceFieldTypeSpecifier();
			// Compose the message
			serverMessage = commandIdentifier + delimiter + desiredForceFieldType + terminator;
			// Note that we've created a valid message
			validMessage = true;
		}
		else if(commandIdentifier == sendTrunkBeltStructureMatrixSpecifier)
        {
			// Unpack the columns of the S matrix, separating each float by a delimiter. We send the message in columns, 
			// i.e. we send the first force column, then the first torque column, second force column, second torque column, etc.
			// 
			string dataString = ""; // note, this string will start with a delimiter!
			// For each column
			for(int columnIndex = 0; columnIndex < columnsOfForceStructureMatrix.Length; columnIndex++)
            {
				// Add the entire S matrix column to the data string,
                // which means force rows (3) and then torque rows (3)
				dataString = dataString + delimiter +
					columnsOfForceStructureMatrix[columnIndex].x + delimiter +
					 columnsOfForceStructureMatrix[columnIndex].y + delimiter +
					columnsOfForceStructureMatrix[columnIndex].z + delimiter +
					columnsOfTorqueStructureMatrix[columnIndex].x + delimiter +
					 columnsOfTorqueStructureMatrix[columnIndex].y + delimiter +
					columnsOfTorqueStructureMatrix[columnIndex].z;
			}

			int numberOfColumns = columnsOfForceStructureMatrix.Length;

			// Assemble the full message with command ID and data
			serverMessage = commandIdentifier + delimiter + numberOfColumns + dataString; // no delimiter needed between ID and data, since data starts with delimiter.

			// Note that we've created a valid message
			validMessage = true;
		}
		else {
			// do nothing since command was invalid
        }

		// Convert string message to byte array.                 
		byte[] serverMessageAsByteArray = Encoding.ASCII.GetBytes(serverMessage);

		// return byte[] message and whether or not the message is valid 
		return (serverMessageAsByteArray, validMessage);
	}

	private void SendComposedMessageToRobust(byte[] serverMessageAsByteArray)
    {
		if (connectedTcpClient == null || connectedClientSocket.Connected == false)
		{
			Debug.Log("No TCP client - not sending message.");
			return;
		}

		try
		{
			// Get a stream object for writing. 			
			NetworkStream stream = connectedTcpClient.GetStream();
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
	private void ListenForIncomingRequests()
	{
		try
		{
			// Create listener on localhost port 8052. 			
			tcpListener = new TcpListener(IPAddress.Parse("127.0.0.1"), portNumber);
			tcpListener.Start();
			Debug.Log("Server is listening");
			Byte[] bytes = new Byte[1024];
			while (true)
			{
				using (connectedTcpClient = tcpListener.AcceptTcpClient()) // AcceptTcpClient() blocks until a request is pending
				{
					// Get a stream object for reading 					
					using (NetworkStream stream = connectedTcpClient.GetStream())
					{

						// Store a reference to the client socket object
						connectedClientSocket = connectedTcpClient.Client;

						int length;
						// Read incomming stream into byte arrary. 						
						while ((length = stream.Read(bytes, 0, bytes.Length)) != 0)
						{
							var incomingData = new byte[length];
							Array.Copy(bytes, 0, incomingData, 0, length);
							// Convert byte array to string message. 							
							string clientMessage = Encoding.ASCII.GetString(incomingData);
							Debug.Log("client message received as: " + clientMessage);
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
	/// Send message to client using socket connection. 	
	/// </summary> 	
	public void SendControlPointCoordinate(Vector3 controlPointCoordinates)
	{
		if (connectedTcpClient == null)
		{
			Debug.Log("No TCP client - not sending message.");
			return;
		}

		try
		{
			// Get a stream object for writing. 			
			NetworkStream stream = connectedTcpClient.GetStream();
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


	public void CloseTcpConnectionToRobust()
    {
		connectedTcpClient.Close();
		tcpListener.Stop();
	}


	void OnApplicationQuit()
	{
		Debug.Log("Application ending after " + Time.time + " seconds");
		if (connectedTcpClient != null)
		{
			connectedTcpClient.Close();
		}
		tcpListener.Stop();
	}
}


