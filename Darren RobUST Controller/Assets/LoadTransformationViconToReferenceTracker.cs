using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System;
using UnityEngine;

public class LoadTransformationViconToReferenceTracker : MonoBehaviour
{

    //the constant part of an excursion performancec summary file name
    private const string transformationFromViconToTrackerPrefix = "Vicon_Vive_Calibration_Data";

    // The key transformation this script reconstructs from file 
    private Matrix4x4 transformationViconToTrackerFrame;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LoadViconToReferenceTrackerTransformation(string pathToDirectoryWithFile, string keyword = "")
    {
        // Read the file 
        float[] transformationAsFloatArray = loadTransformationViconToReferenceTrackerFrame(pathToDirectoryWithFile, keyword);

        // Convert to a Matrix4x4 
        transformationViconToTrackerFrame = ReconstructTransformationAsMatrix(transformationAsFloatArray);
    }

    public Matrix4x4 GetTransformationReferenceTrackerToVicon()
    {
        return transformationViconToTrackerFrame;
    }


    private float[] loadTransformationViconToReferenceTrackerFrame(string localPathToFolder, string keyword)
    {
        // Get all files in the directory
        string pathToFolder = getDirectoryPath() + localPathToFolder;
        Debug.Log("Trying to load Vicon-tracker transformation data from the path: " + pathToFolder);
        string[] allFiles = System.IO.Directory.GetFiles(pathToFolder);
        Debug.Log("Loaded the following number of files from the specified path: " + allFiles.Length);

        // Get the name of the most recent excursion performance summary file (with a keyword, such as "No_Stim", if desired)
        string fileToUseName = "";
        DateTime dateTimeOfFileToUse = new DateTime();
        for (uint fileIndex = 0; fileIndex < allFiles.Length; fileIndex++)
        {
            //see if the file is an excursion performance summary file with the proper keywords
            string fileName = allFiles[fileIndex];
            bool fileNameOfAViconTrackerTransformationFile = fileName.Contains(transformationFromViconToTrackerPrefix);
            bool hasKeyword = false;
            if (keyword != "") //if a keyword was specified
            {
                //see if the keyword is in the string
                hasKeyword = fileName.Contains(keyword);
            }
            else //if there is no keyword
            {
                //proceed as if the keyword were present
                hasKeyword = true;
            }

            //ensure that it is not a meta file
            bool isMetaFile = fileName.Contains("meta");

            //if the file is an excursion performance summary file with the correct keyword
            if (fileNameOfAViconTrackerTransformationFile && hasKeyword && !isMetaFile)
            {
                Debug.Log("Considering limits file name: " + fileName);
                if (fileName == (pathToFolder + "Vicon_Vive_Calibration_Data.csv")) // if the file name is the default file name
                {
                    fileToUseName = fileName; // We only load limits from the "default" file name
                }
                /*                //if this is the first valid file we've found
                                if(fileToUseName == "")
                                {
                                    fileToUseName = fileName;
                                    dateTimeOfFileToUse = System.IO.File.GetCreationTime(fileName);
                                } else //if we have already found a file name
                                {
                                    DateTime dateTimeOfCurrentFile = System.IO.File.GetCreationTime(fileName);
                                    int isDateEarlierOrLater = DateTime.Compare(dateTimeOfCurrentFile, dateTimeOfFileToUse); 
                                    if(isDateEarlierOrLater > 0) //if the current file's date/time is the most recent one observed thus far
                                    {
                                        //store that one
                                        fileToUseName = fileName;
                                        dateTimeOfFileToUse = dateTimeOfCurrentFile;
                                    }
                                }*/
            }
        }

        Debug.Log("Loading from the following Vicon-tracker transformation file path: " + fileToUseName);

        // Now that we have the file to use, read it in
        string allFileTextString = System.IO.File.ReadAllText(fileToUseName);
        //split into lines, delimited by the newline character
        char[] separator = new char[] { '\n' };
        string[] rowsFromFile = allFileTextString.Split(separator, 2);
        //split first data row (second row) into cells/entries, delimited by commas
        separator = new char[] { ',' };
        string[] firstDataRow = rowsFromFile[1].Split(separator, 100);
        //Convert each string in the data row to a float
        float[] firstDataRowAsFloat = new float[firstDataRow.Length];
        for (uint entryIndex = 0; entryIndex < firstDataRow.Length; entryIndex++)
        {
            Debug.Log("String to parse into a float is: " + firstDataRow[entryIndex]);
            firstDataRowAsFloat[entryIndex] = float.Parse(firstDataRow[entryIndex], CultureInfo.InvariantCulture.NumberFormat);
        }

        //return the float array for the excursion performance summary
        return firstDataRowAsFloat;
    }


    private Matrix4x4 ReconstructTransformationAsMatrix(float[] transformationAsFloatArray)
    {
        Matrix4x4 transformationMatrix = new Matrix4x4();

        // Store the float[] in the matrix (we go left to right, top to bottom)
        transformationMatrix.SetRow(0, new Vector4(transformationAsFloatArray[0], transformationAsFloatArray[1],
            transformationAsFloatArray[2], transformationAsFloatArray[3]));

        transformationMatrix.SetRow(1, new Vector4(transformationAsFloatArray[4], transformationAsFloatArray[5],
            transformationAsFloatArray[6], transformationAsFloatArray[7]));

        transformationMatrix.SetRow(2, new Vector4(transformationAsFloatArray[8], transformationAsFloatArray[9],
            transformationAsFloatArray[10], transformationAsFloatArray[11]));

        transformationMatrix.SetRow(3, new Vector4(transformationAsFloatArray[12], transformationAsFloatArray[13],
            transformationAsFloatArray[14], transformationAsFloatArray[15]));

        return transformationMatrix;
    }

    private string getDirectoryPath()
    {
        #if UNITY_EDITOR
                return Application.dataPath + "/CSV/";

        #elif UNITY_STANDALONE
                return Application.dataPath + "/" ;
        #else
            return Application.dataPath + "/";
        #endif
    }
}
