using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;


public class StoreAndWriteDataToFileScript : MonoBehaviour
{

    //define the dataStorageObject class, which can be serialized by Unity
    [Serializable]
    public class dataStorageObject
    {
        [SerializeField]
        private List<float[]> dataStorage; //stores a session's dataStorage variable.
                                           //Useful because an instance of this class can be
                                           //serialized.

        //define the constructor
        public dataStorageObject(List<float[]> dataToStore)
        {
            dataStorage = dataToStore;
        }
    } //end dataStorageObject class definition



    //instance variables
    //data storage
    private int numberOfEntriesPerDataRow = 148;
    private string[] namesOfDataHeadersForCsvFile;
    public List<float[]> dataStore = new List<float[]>(); //the object that stores all of the data
    private string outputFileName = "testSaveFile.csv";






    void Start()
    {
        namesOfDataHeadersForCsvFile = new string[numberOfEntriesPerDataRow];
    }


    //BEGIN: public methods*****************************************************

    public void setCsvDataRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfDataHeadersForCsvFile = namesOfCsvDataHeaders;
    }



    public void storeRowOfData(float[] dataToStore)
    {
        dataStore.Add(dataToStore);
    }


    public void writeDataToCsv()
    {
        string[][] output = new string[dataStore.Count + 1][]; //output is an array of arrays

        output[0] = namesOfDataHeadersForCsvFile;

        //put each row of data (an array) as an element in an array of arrays
        for (int dataRowIndex = 1; dataRowIndex < output.Length; dataRowIndex++)
        {
            float[] currentDataRow = dataStore[dataRowIndex - 1]; //subtract one since the data struct has an extra row for headers

            //first convert the float[] to a string[]
            string[] rowOfDataAsString = new string[currentDataRow.Length];
            for (int entryInDataIndex = 0; entryInDataIndex < currentDataRow.Length; entryInDataIndex++)
            {
                //convert the float in the given index to a string
                rowOfDataAsString[entryInDataIndex] = currentDataRow[entryInDataIndex].ToString();
            }

            output[dataRowIndex] = rowOfDataAsString; //each recorded data row is an element in the output array of arrays
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




    public void writePassedInListOfFloatArraysToFile(string[] dataHeaders, List<float[]> dataToWrite, string filePath)
    {
        string[][] output = new string[dataToWrite.Count + 1][]; //output is an array of arrays

        output[0] = dataHeaders;

        //put each row of data (an array) as an element in an array of arrays
        for (int dataRowIndex = 1; dataRowIndex < output.Length; dataRowIndex++)
        {
            float[] currentDataRow = dataToWrite[dataRowIndex - 1]; //subtract one since the data struct has an extra row for headers

            //first convert the float[] to a string[]
            string[] rowOfDataAsString = new string[currentDataRow.Length];
            for (int entryInDataIndex = 0; entryInDataIndex < currentDataRow.Length; entryInDataIndex++)
            {
                //convert the float in the given index to a string
                rowOfDataAsString[entryInDataIndex] = currentDataRow[entryInDataIndex].ToString();
            }

            output[dataRowIndex] = rowOfDataAsString; //each recorded data row is an element in the output array of arrays
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

        //write the data to file
        StreamWriter outStream = System.IO.File.CreateText(filePath);
        outStream.WriteLine(dataFileStringBuilder);
        outStream.Close();
    }


    public void writeDataStoreToFileUsingSerializable()
    {
        //create an instance of the serializable data storage object and place our data into it
        dataStorageObject serializableDataStorer = new dataStorageObject(dataStore);

        //write the serializable data storage object to file

    }

    //END: public methods*******************************************************






    //BEGIN: private methods*****************************************************

    private string getPath()
    {
    #if UNITY_EDITOR
        return Application.dataPath + "/CSV/" + outputFileName;

    #elif UNITY_STANDALONE
        return Application.dataPath + "/" + outputFileName;
    #else
        return Application.dataPath + "/" + outputFileName;
    #endif
    }

    //END: private methods*******************************************************



}
