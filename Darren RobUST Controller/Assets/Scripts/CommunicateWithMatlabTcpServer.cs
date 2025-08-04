using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class CommunicateWithMatlabTcpServer : MonoBehaviour
{
	#region private members 	
	// TCPListener to listen for incomming TCP connection 	
	private TcpListener tcpListener;
	// Port number
	private int portNumber = 8053;
	// Background thread for TcpServer workload. 	
	private Thread tcpListenerThread;
	// Create handle to connected tcp client. 	
	private TcpClient connectedTcpClient;

	// The task level manager
	public GameObject levelManager; //the level manager for the current task
	private LevelManagerScriptAbstractClass levelManagerScript; // the script of the level manager. Part of an abstract class.

	// Message features
	private string delimiter = ",";
	private string terminator = "\r\n";

	// Command IDs
	private const string sendTrunkBeltDesiredForcesAndStructureMatrixSpecifier = "T";
	private const string sendPelvicBeltStructureMatrixSpecifier = "P";


	// pending command flags (like a cheap version of a queue for initialization messages
	private bool pendingExcursionCenterMessagetoSendOnTcpConnectFlag = false;
	private bool pendingExcursionLimitsMessagetoSendOnTcpConnectFlag = false;
	private bool pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag = false;

	// Most recent desired forces and torques in Vicon frame
	private Vector3 mostRecentDesiredForcesViconFrame;
	private Vector3 mostRecentDesiredTorquesViconFrame;

	// Most recent structure matrices (computed on a per-Vicon-frame basis)
	private Vector3[] columnsOfForceTrunkStructureMatrix;
	private Vector3[] columnsOfTorqueTrunkStructureMatrix;
	private double[] structureMatrixTrunkAsDoubleArray;

	// Alglib test
	alglib.minqpstate state;
	
/*	alglib.minqpcreate(2, out state);
    alglib.minqpsetquadraticterm(state, a);
    alglib.minqpsetlinearterm(state, b);
    alglib.minqpsetstartingpoint(state, x0);
    alglib.minqpsetbc(state, bndl, bndu);*/
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
	}

	// Update is called once per frame
	void Update()
	{
		// Manage "pending" commands to send to RobUST
		if (pendingExcursionCenterMessagetoSendOnTcpConnectFlag == true)
		{
			pendingExcursionCenterMessagetoSendOnTcpConnectFlag = false;
			//SendExcursionLimitCenterPositionInViconFrameToRobot();
		}

		if (pendingExcursionLimitsMessagetoSendOnTcpConnectFlag == true)
		{
			pendingExcursionLimitsMessagetoSendOnTcpConnectFlag = false;
			//SendExcursionLimitsInViconFrameToRobot();
		}

		if (pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag == true)
		{
			pendingForceFieldSpeciferMessageToSendOnTcpConnectFlag = false;
			//SendCommandToRobust(sendForceFieldModeSpecifier);
		}
	}


	// START: public functions **************************************************************************

	public void SendDesiredTrunkForcesAndStructureMatrixToMatlab(Vector3 desiredForcesViconFrame,
		Vector3 desiredTorquesViconFrame, Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix)
	{
		Debug.Log("Sending S matrix to RobUST");
		// First, store the new columns of the structure matrix in instance variables.
		mostRecentDesiredForcesViconFrame = desiredForcesViconFrame;
		mostRecentDesiredTorquesViconFrame = desiredTorquesViconFrame;
		columnsOfForceTrunkStructureMatrix = columnsOfForceSMatrix;
		columnsOfTorqueTrunkStructureMatrix = columnsOfTorqueSMatrix;

		// Convert the structure matrix to a double array format

		double[,] structureMatrixTrunkAsDoubleArray = new double[6, columnsOfForceSMatrix.Length + columnsOfTorqueSMatrix.Length];
		double[] row1 = new double[columnsOfForceSMatrix.Length + columnsOfTorqueSMatrix.Length];
		double[] row2 = new double[columnsOfForceSMatrix.Length + columnsOfTorqueSMatrix.Length];
		
		

		// Then specify that we want to send a trunk structure matrix command
		SendCommandToMatlab(sendTrunkBeltDesiredForcesAndStructureMatrixSpecifier);

	}

	private void SendCommandToMatlab(string commandIdentifier)
	{
		if (connectedTcpClient != null)
		{
			// Compose the message given the command identifier
			(byte[] composedMessage, bool validMessage) = ComposeCommandGivenIdentifier(commandIdentifier);

			// If the message is valid  
			if (validMessage)
			{
				// Sending message log
				Debug.Log("Sending message with identifer: " + commandIdentifier);

				// Send the command over the network
				SendComposedMessageToMatlab(composedMessage);
			}
		}
	}

	// END: public functions **************************************************************************



	private (byte[], bool) ComposeCommandGivenIdentifier(string commandIdentifier)
	{
		// Compose the complete command message based on the command identifier
		string serverMessage = ""; // initialize as empty string
		bool validMessage = false;
		if (commandIdentifier == sendTrunkBeltDesiredForcesAndStructureMatrixSpecifier)
		{

			string dataString = ""; // note, this string will start with a delimiter!
									// For each column

			// First, add the desired end-effector/subject forces and torques in Vicon frame for this frame
			// The forces are the first Vector3 in the currentForcesAndTorques Vector3[], the torques are the second element.
			dataString = mostRecentDesiredForcesViconFrame.x + delimiter +
					 mostRecentDesiredForcesViconFrame.y + delimiter +
					mostRecentDesiredForcesViconFrame.z + delimiter +
					mostRecentDesiredTorquesViconFrame.x + delimiter +
					 mostRecentDesiredTorquesViconFrame.y + delimiter +
					mostRecentDesiredTorquesViconFrame.z;


			// Unpack the columns of the S matrix, separating each float by a delimiter. We send the message in columns, 
			// i.e. we send the first force column, then the first torque column, second force column, second torque column, etc.
			// 
			for (int columnIndex = 0; columnIndex < columnsOfForceTrunkStructureMatrix.Length; columnIndex++)
			{
				// Add the entire S matrix column to the data string,
				// which means force rows (3) and then torque rows (3)
				// for that column
				dataString = dataString + delimiter +
					columnsOfForceTrunkStructureMatrix[columnIndex].x + delimiter +
					 columnsOfForceTrunkStructureMatrix[columnIndex].y + delimiter +
					columnsOfForceTrunkStructureMatrix[columnIndex].z + delimiter +
					columnsOfTorqueTrunkStructureMatrix[columnIndex].x + delimiter +
					 columnsOfTorqueTrunkStructureMatrix[columnIndex].y + delimiter +
					columnsOfTorqueTrunkStructureMatrix[columnIndex].z;
			}

			int numberOfColumns = columnsOfForceTrunkStructureMatrix.Length;

			// Assemble the full message with command ID and data
			serverMessage = commandIdentifier + delimiter + numberOfColumns + dataString; // no delimiter needed between ID and data, since data starts with delimiter.

			// Note that we've created a valid message
			validMessage = true;
		}
		else
		{
			// do nothing since command was invalid
		}

		// Convert string message to byte array.                 
		byte[] serverMessageAsByteArray = Encoding.ASCII.GetBytes(serverMessage);

		// return byte[] message and whether or not the message is valid 
		return (serverMessageAsByteArray, validMessage);
	}

	private void SendComposedMessageToMatlab(byte[] serverMessageAsByteArray)
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



