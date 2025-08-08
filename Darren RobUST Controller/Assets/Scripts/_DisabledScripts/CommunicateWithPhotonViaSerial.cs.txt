using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using UnityEngine;

public class CommunicateWithPhotonViaSerial : MonoBehaviour
{
    private const string nameOfPhotonComPort = "COM4"; // The name of the Serial port connected to the Photon.
                                                       // We hardcode the name. Just set it for the given PC and 
                                                       // Photon.
    private const int photonBaudRate = 38400;
    private const int dataBitsSerialPortParameter = 8; // 8 is the default, but we set it to 8 for clarity.

    private SerialPort photonSerialPortObject; // the SerialPort class object that maintains the Serial 
                                               // communication with the Photon.

    // Commands to Photon variables
    private const string commandHeaderSetSyncHigh = "h";
    private const string commandHeaderSetSyncLow = "l";
    private const string commandBodyEmptyString = "";



    // Start is called before the first frame update
    void Start()
    {
        // See if the port is available
        bool portAvailable = false;
        foreach (string currentSerialPortName in SerialPort.GetPortNames())
        {
            if (currentSerialPortName == nameOfPhotonComPort)
            {
                portAvailable = true;
            }
        }

        // Try to connect to the serial port
        if (portAvailable) // if the Photon serial port is available
        {
            // Create serial port and specify settings
            photonSerialPortObject = new SerialPort();
            photonSerialPortObject.PortName = nameOfPhotonComPort;
            photonSerialPortObject.BaudRate = photonBaudRate;
            photonSerialPortObject.DataBits = dataBitsSerialPortParameter;
            photonSerialPortObject.StopBits = StopBits.One;
            photonSerialPortObject.Handshake = Handshake.None; // 0 = no handshake, the default

            // On the Argon, enabling DTR was critical to avoid timeouts (broken without this line). 
            // Confirm on the Photon.
            photonSerialPortObject.DtrEnable = true;

            // Set the read/write timeouts
            photonSerialPortObject.ReadTimeout = 500; // units: [ms]
            photonSerialPortObject.WriteTimeout = 500; // units: [ms]

            // Open the serial port 
            photonSerialPortObject.Open();

            // Print that the port is open
            Debug.Log("Photon Serial port is open.");
        }
        else
        {
            Debug.LogError("Cannot connect to the Photon Serial port.");
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void sendSerialMessageToPhoton(string commandIdentifier, string commandBody)
    {
        string messageWithoutNewlineEndMessageChar = commandIdentifier + commandBody;

        // By using WriteLine instead of Write, we append the newline character. 
        // The newline character is the character denoting the end of a message.
        photonSerialPortObject.WriteLine(messageWithoutNewlineEndMessageChar);
    }

    public void tellPhotonToPulseSyncStartPin()
    {
        // Send the message indicating the sync start pin should be pulsed(10 us pulse width)
        sendSerialMessageToPhoton(commandHeaderSetSyncHigh, commandBodyEmptyString);
    }


    public void tellPhotonToPulseSyncStopPin()
    {
        // Send the message indicating the sync stop pin should be pulsed (10 us pulse width)
        sendSerialMessageToPhoton(commandHeaderSetSyncLow, commandBodyEmptyString);
    }
}

