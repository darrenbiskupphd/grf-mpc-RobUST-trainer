using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System;
using UnityEngine;

public class DataRecorder : MonoBehaviour
{

    //private variables 
    public string outputFileName = "OutputData.csv";
    private List<string[]> recordedDataRows = new List<string[]>();
    private int sizeOfRowData = 23; 

    // Start is called before the first frame update
    void Start()
    {
        CreateFileHeadersDataRow(); // we can immediately add the row headers to our growing list of string arrays.
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void CreateFileHeadersDataRow()
    {
        // Creating First row of titles manually..
        string[] rowDataTemp = new string[sizeOfRowData];
        rowDataTemp[0] = "Block";
        rowDataTemp[1] = "Condition";
        rowDataTemp[2] = "Trial";
        //store collision positions
        rowDataTemp[3] = "CollisionPosX";
        rowDataTemp[4] = "CollisionPosY";
        //store collision velocities
        rowDataTemp[5] = "CollisionVelX";
        rowDataTemp[6] = "CollisionVelY";
        //store point-related collision parameters
        rowDataTemp[7] = "InStrikeZoneFlag";
        rowDataTemp[8] = "TargetPositionAtStrikeX";
        rowDataTemp[9] = "TargetPositionAtStrikeY";
        rowDataTemp[10] = "PhysicsUpdatedTargetPositionAtStrikeX";
        rowDataTemp[11] = "PhysicsUpdatedTargetPositionAtStrikeY";
        rowDataTemp[12] = "TargetFinalKnockbackPositionX";
        rowDataTemp[13] = "TargetFinalKnockbackPositionY";
        rowDataTemp[14] = "RespawnPointX";
        rowDataTemp[15] = "RespawnPointY";
        rowDataTemp[16] = "PointsEarned";
        rowDataTemp[17] = "StrikeLineXPosition";
        rowDataTemp[18] = "LeftEdgePenaltyZoneXPosition";
        rowDataTemp[19] = "LeftEdgeStrikeZoneXPosition";
        rowDataTemp[20] = "RightEdgeStrikeZoneXPosition";
        rowDataTemp[21] = "TargetKnockbackScaling";
        rowDataTemp[22] = "PenaltyValue";
        recordedDataRows.Add(rowDataTemp);
    }

    public void AddRowToRecordedData(int blockNumber, int experimentalCondition, int trialNumber, float collisionPosX,
        float collisionPosY, float collisionVelX, float collisionVelY, bool inStrikeZone, Vector2 targetLocationOnStrike,
        Vector3 updatedTargetLocationOnStrike, Vector3 targetFinalKnockbackPosition, Vector3 respawnPoint, float pointsEarned, 
        Vector3 targetStrikeLinePosition, float leftEdgeOfPenaltyZonePosition, float leftEdgeOfStrikeZone, float rightEdgeOfStrikeZone, 
        float targetKnockbackScaling, string penaltyValue)
    {
        string[] rowDataTemp = new string[sizeOfRowData];
        rowDataTemp[0] = blockNumber.ToString();
        rowDataTemp[1] = experimentalCondition.ToString();
        rowDataTemp[2] = trialNumber.ToString();
        rowDataTemp[3] = collisionPosX.ToString("#.0000");
        rowDataTemp[4] = collisionPosY.ToString("#.0000");
        rowDataTemp[5] = collisionVelX.ToString("#.0000");
        rowDataTemp[6] = collisionVelY.ToString("#.0000");
        rowDataTemp[7] = inStrikeZone.ToString();
        rowDataTemp[8] = targetLocationOnStrike.x.ToString();
        rowDataTemp[9] = targetLocationOnStrike.y.ToString();
        rowDataTemp[10] = updatedTargetLocationOnStrike.x.ToString();
        rowDataTemp[11] = updatedTargetLocationOnStrike.y.ToString();
        rowDataTemp[12] = targetFinalKnockbackPosition.x.ToString();
        rowDataTemp[13] = targetFinalKnockbackPosition.y.ToString();
        rowDataTemp[14] = respawnPoint.x.ToString();
        rowDataTemp[15] = respawnPoint.y.ToString();
        rowDataTemp[16] = pointsEarned.ToString("#.0000");
        rowDataTemp[17] = targetStrikeLinePosition.x.ToString();
        rowDataTemp[18] = leftEdgeOfPenaltyZonePosition.ToString();
        rowDataTemp[19] = leftEdgeOfStrikeZone.ToString();
        rowDataTemp[20] = rightEdgeOfStrikeZone.ToString();
        rowDataTemp[21] = targetKnockbackScaling.ToString();
        rowDataTemp[22] = penaltyValue;
        recordedDataRows.Add(rowDataTemp);
    }

    public void AddTargetMissRowToRecordedData(int blockNumber, int experimentalCondition, int trialNumber, Vector3 respawnPoint,
        Vector3 targetStrikeLinePosition, float leftEdgeOfPenaltyZonePosition, float leftEdgeOfStrikeZone, float rightEdgeOfStrikeZone,
        float targetKnockbackScaling, string penaltyValue)
    {
        string[] rowDataTemp = new string[sizeOfRowData];
        rowDataTemp[0] = blockNumber.ToString();
        rowDataTemp[1] = experimentalCondition.ToString();
        rowDataTemp[2] = trialNumber.ToString();
        rowDataTemp[3] = "-1";
        rowDataTemp[4] = "-1";
        rowDataTemp[5] = "-1";
        rowDataTemp[6] = "-1";
        rowDataTemp[7] = "MISS";
        rowDataTemp[8] = "-1";
        rowDataTemp[9] = "-1";
        rowDataTemp[10] = "-1";
        rowDataTemp[11] = "-1";
        rowDataTemp[12] = "-1";
        rowDataTemp[13] = "-1";
        rowDataTemp[14] = respawnPoint.x.ToString();
        rowDataTemp[15] = respawnPoint.y.ToString();
        rowDataTemp[16] = "0";
        rowDataTemp[17] = targetStrikeLinePosition.x.ToString();
        rowDataTemp[18] = leftEdgeOfPenaltyZonePosition.ToString();
        rowDataTemp[19] = leftEdgeOfStrikeZone.ToString();
        rowDataTemp[20] = rightEdgeOfStrikeZone.ToString();
        rowDataTemp[21] = targetKnockbackScaling.ToString();
        rowDataTemp[22] = penaltyValue;
        recordedDataRows.Add(rowDataTemp);
    }

    //Writes the recorded data to file
    public void SaveRecordedDataToFile()
    {
        string[][] output = new string[recordedDataRows.Count][]; //output is an array of arrays

        //put each row of data (an array) as an element in an array of arrays
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = recordedDataRows[i]; //each recorded data row is an element in the output array of arrays
        }

        //get the total number of rows
        int numRowsToWrite = output.GetLength(0);
        //choose a delimiter
        string delimiter = ",";

        //create a string builder to create a string representation of the data
        StringBuilder dataFileStringBuilder = new StringBuilder();

        //create a string representation of the data
        for (int index = 0; index < numRowsToWrite; index++)
        {
            dataFileStringBuilder.AppendLine(string.Join(delimiter, output[index])); //join simply concatenates the entries of a string array, separated by the delimiter
        }

        //get the output file path name
        string filePath = getPath();

        //write the data to file
        StreamWriter outStream = System.IO.File.CreateText(filePath);
        outStream.WriteLine(dataFileStringBuilder);
        outStream.Close();
    }

    private string getPath()
    {
        #if UNITY_EDITOR
            return Application.dataPath + "/CSV/"+ outputFileName;

        #elif UNITY_STANDALONE
            return Application.dataPath + "/" + outputFileName;
        #else
            return Application.dataPath + "/" + outputFileName;
        #endif
    }
}
