using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;


public class GeneralDataRecorder : MonoBehaviour
{

    // instance variables
    // data storage - marker data
    private StreamWriter markerDataWriter;
    private bool markerDataFinalized = false; // whether to keep data on ApplicationQuit (true) or delete it (false = default).
    private int numberOfEntriesPerMarkerDataRow = 50; // Actual number of columns is specified when the LevelManager sets the column names
    private string[] namesOfMarkerDataHeadersForCsvFile;
    public List<float[]> markerDataStore = new List<float[]>(); //the object that stores all of the marker data
    private string outputMarkerDataSubdirectoryName = "SubdirectoryNotSpecified";
    private string outputMarkerDataFileName = "markerTestSaveFile.csv";
    private string markerDataFilePath;
    private bool markerHeaderWritten = false;

    // data storage - task-space data, recording data collected at Unity frame rate
    private int numberOfEntriesPerFrameDataRow = 10; // Actual number of columns is specified when the LevelManager sets the column names
    private StreamWriter frameDataWriter;
    private bool frameDataFinalized = false; // whether to keep data on ApplicationQuit (true) or delete it (false = default).
    private string[] namesOfFrameDataHeadersForCsvFile;
    public List<float[]> frameDataStore = new List<float[]>(); //the object that stores all of the frame-wise Unity data for the task
    private string outputFrameDataSubdirectoryName = "SubdirectoryNotSpecified";
    private string outputFrameDataFileName = "frameDataTestSaveFile.csv";
    private string frameDataFilePath;
    private bool frameHeaderWritten = false;

    // data storage - trial data (summarizing each trial in a block)
    private int numberOfEntriesPerTrialDataRow = 20; // Actual number of columns is specified when the LevelManager sets the column names
    private StreamWriter trialDataWriter;
    private bool trialDataFinalized = false; // whether to keep data on ApplicationQuit (true) or delete it (false = default).
    private string[] namesOfTrialDataHeadersForCsvFile;
    public List<float[]> trialDataStore = new List<float[]>(); //the object that stores all of the trial data for the task. Reset at start of each block?
    private string outputTrialDataSubdirectoryName = "SubdirectoryNotSpecified";
    private string outputTrialDataFileName = "trialDataTestSaveFile.csv";
    private string trialDataFilePath;
    private bool trialHeaderWritten = false;

    //data storage - excursion performance summary (summarizing the max distance the subject went along each excursion direction) 
    private StreamWriter excursionPerformanceDataWriter;
    private bool excursionPerformanceDataFinalized = false; // whether to keep data on ApplicationQuit (true) or delete it (false = default).
    private string[] namesOfExcursionPerformanceSummaryHeadersForCsvFile;
    public List<float[]> excursionPerformanceSummaryDataStore = new List<float[]>(); //the object that stores all of the trial data for the task. Reset at start of each block?
    private string outputExcursionPerformanceSummarySubdirectoryName = "SubdirectoryNotSpecified";
    private string outputExcursionPerformanceSummaryFileName = "excursionPerformanceSummaryTestSaveFile.csv";
    private string excursionPerformanceDataFilePath;
    private bool excursionPerformanceHeaderWritten = false;

    // data storage - EMG data (all 16 channels of EMG data, and hopefully the sync signal)
    private StreamWriter emgDataWriter;
    private bool emgDataFinalized = false; // whether to keep data on ApplicationQuit (true) or delete it (false = default).
    private int numberOfEntriesPerEmgDataRow = 18; // Actual number of columns is specified when the LevelManager sets the column names
    private string[] namesOfEmgDataHeadersForCsvFile;
    public List<float[]> emgDataStore = new List<float[]>(); //the object that stores all of the EMG data for the task. Reset at start of each block?
    private string outputEmgDataSubdirectoryName = "SubdirectoryNotSpecified";
    private string outputEmgDataFileName = "trialDataTestSaveFile.csv";
    private string emgDataFilePath;
    private bool emgHeaderWritten = false;

    // Data storage - stance model kinematics, gravity torques, and external forces/torques/cable tensions
    private StreamWriter stanceModelDataWriter;
    private bool stanceModelDataFinalized = false; // whether to keep data on ApplicationQuit (true) or delete it (false = default).
    private string[] namesOfStanceModelDataHeadersForCsvFile; // column names for output file
    public List<float[]> stanceModelDataStore = new List<float[]>(); //the object that stores all of the stance model and external force/torque data for the task.
    private string outputStanceModelDataSubdirectoryName = "SubdirectoryNotSpecified"; // output folder name - set by level manager or other scripts
    private string outputStanceModelDataFileName = "trialDataTestSaveFile.csv"; // output file name - set by level manager or other scripts. 
    private string stanceModelDataFilePath;
    private bool stanceModelHeaderWritten = false;

    void Start()
    {
        //instantiate data storage header name array sizes
        // These aren't really used, since sizes are set dynamically once the task starts. Delete?
        //namesOfMarkerDataHeadersForCsvFile = new string[numberOfEntriesPerMarkerDataRow];
        //namesOfFrameDataHeadersForCsvFile = new string[numberOfEntriesPerFrameDataRow];
        //namesOfTrialDataHeadersForCsvFile = new string[numberOfEntriesPerTrialDataRow];

    }

    //BEGIN: public methods - General purpose *****************************************************

    public bool DoesFileAlreadyExist(string folderPath, string fileName) {
        // Get the root path to the local /CSV folder
        string directoryName = getDirectoryPath();
        // Append the specific subject, date, task folder path
        directoryName = directoryName + folderPath;
        // See if the file already exists (true) or not (false), given the directory path (with ending /) and the file name
        Debug.Log("checking if file exists at: " + directoryName + fileName);
        return File.Exists(directoryName + fileName);
    }


    //END: public methods - General purpose *****************************************************



    //BEGIN: public methods - marker (and COM) data *****************************************************

    public void setCsvMarkerDataSubdirectoryName(string subdirectoryString)
    {
        outputMarkerDataSubdirectoryName = subdirectoryString;
    }



    public void setCsvMarkerDataFileName(string fileName)
    {
        outputMarkerDataFileName = fileName;
    }

    public void setCsvMarkerDataRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfMarkerDataHeadersForCsvFile = namesOfCsvDataHeaders;
        markerHeaderWritten = false;
    }



    public void storeRowOfMarkerData(float[] dataToStore)
    {
        EnsureMarkerDataWriterInitialized();
        WriteRowToCsv(markerDataWriter, new List<float>(dataToStore), ref markerHeaderWritten, namesOfMarkerDataHeadersForCsvFile);
    }

    private void EnsureMarkerDataWriterInitialized()
    {
        if (markerDataWriter == null)
        {
            string dir = getDirectoryPath() + outputMarkerDataSubdirectoryName;
            Directory.CreateDirectory(dir);
            markerDataFilePath = dir + outputMarkerDataFileName;
            markerDataWriter = new StreamWriter(markerDataFilePath, append: false);
        }
    }



    public void writeMarkerDataToFile()
    {
        // Set flag indicating that we should retain and not delete the data file. 
        markerDataFinalized = true;

        // Close the data writer
        markerDataWriter?.Flush();
        markerDataWriter?.Close();
    }

    //END: public methods - marker (and COM) data *****************************************************




    //BEGIN: public methods - Unity frame-rate data *****************************************************

    public void setCsvFrameDataSubdirectoryName(string subdirectoryString)
    {
        outputFrameDataSubdirectoryName = subdirectoryString;
    }



    public void setCsvFrameDataFileName(string fileName)
    {
        outputFrameDataFileName = fileName;
    }


    public void setCsvFrameDataRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfFrameDataHeadersForCsvFile = namesOfCsvDataHeaders;
        frameHeaderWritten = false;
    }



    public void storeRowOfFrameData(List<float> dataToStore)
    {
        EnsureFrameDataWriterInitialized();
        WriteRowToCsv(frameDataWriter, dataToStore, ref frameHeaderWritten, namesOfFrameDataHeadersForCsvFile);
    }

    private void EnsureFrameDataWriterInitialized()
    {
        if (frameDataWriter == null)
        {
            string dir = getDirectoryPath() + outputFrameDataSubdirectoryName;
            Directory.CreateDirectory(dir);
            frameDataFilePath = dir + outputFrameDataFileName;
            frameDataWriter = new StreamWriter(frameDataFilePath, append: false);
        }
    }


    public void writeFrameDataToFile()
    {
        // Set flag indicating that we should retain and not delete the data file. 
        frameDataFinalized = true;

        // Close the data writer
        frameDataWriter?.Flush();
        frameDataWriter?.Close();
    }

    public string GetFrameDataFilePath()
    {
        return frameDataFilePath;
    }

    //END: public methods - frame data *****************************************************




    //BEGIN: public methods - trial data *****************************************************

    public void setCsvTrialDataSubdirectoryName(string subdirectoryString)
    {
        outputTrialDataSubdirectoryName = subdirectoryString;
    }

    public void setCsvTrialDataFileName(string fileName)
    {
        outputTrialDataFileName = fileName;
    }

    public void setCsvTrialDataRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfTrialDataHeadersForCsvFile = namesOfCsvDataHeaders;
        trialHeaderWritten = false;
    }


    public void storeRowOfTrialData(float[] dataToStore)
    {
        EnsureTrialDataWriterInitialized();
        WriteRowToCsv(trialDataWriter, dataToStore.ToList(), ref trialHeaderWritten, namesOfTrialDataHeadersForCsvFile);
    }

    private void EnsureTrialDataWriterInitialized()
    {
        if (trialDataWriter == null)
        {
            string dir = getDirectoryPath() + outputTrialDataSubdirectoryName;
            Directory.CreateDirectory(dir);
            trialDataFilePath = dir + outputTrialDataFileName;
            trialDataWriter = new StreamWriter(trialDataFilePath, append: false);
        }
    }



    public void writeTrialDataToFile()
    {
        // Set flag indicating that we should retain and not delete the data file. 
        trialDataFinalized = true;

        // Close the data writer
        trialDataWriter?.Flush();
        trialDataWriter?.Close();
    }

    //END: public methods - trial data *****************************************************




    //BEGIN: public methods - excursion performance summary (to build functional BoS) *****************************************************

    public void setCsvExcursionPerformanceSummarySubdirectoryName(string subdirectoryString)
    {
        outputExcursionPerformanceSummarySubdirectoryName = subdirectoryString;
    }

    public void setCsvExcursionPerformanceSummaryFileName(string fileName)
    {
        outputExcursionPerformanceSummaryFileName = fileName;
    }

    public void setCsvExcursionPerformanceSummaryRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfExcursionPerformanceSummaryHeadersForCsvFile = namesOfCsvDataHeaders;
        Debug.Log("Headers set for excursion summary: " + string.Join(",", namesOfCsvDataHeaders));
        excursionPerformanceHeaderWritten = false;
    }


    public void storeRowOfExcursionPerformanceSummaryData(float[] dataToStore)
    {
        EnsureExcursionPerformanceDataWriterInitialized();
        WriteRowToCsv(excursionPerformanceDataWriter, dataToStore.ToList(), ref excursionPerformanceHeaderWritten, namesOfExcursionPerformanceSummaryHeadersForCsvFile);
    }

    public string GetExcursionPerformanceDataFilePath()
    {
        return excursionPerformanceDataFilePath;
    }

    private void EnsureExcursionPerformanceDataWriterInitialized()
    {
        if (excursionPerformanceDataWriter == null)
        {
            string dir = getDirectoryPath() + outputExcursionPerformanceSummarySubdirectoryName;
            Directory.CreateDirectory(dir);
            excursionPerformanceDataFilePath = dir + outputExcursionPerformanceSummaryFileName;
            excursionPerformanceDataWriter = new StreamWriter(excursionPerformanceDataFilePath, append: false);
        }
    }



    public void writeExcursionPerformanceSummaryToFile()
    {
        // Set flag indicating that we should retain and not delete the data file. 
        excursionPerformanceDataFinalized = true;

        // Close the data writer
        excursionPerformanceDataWriter?.Flush();
        excursionPerformanceDataWriter?.Close();
    }

    //END: public methods - trial data *****************************************************


    //BEGIN: public methods - EMG data *****************************************************

    public void setCsvEmgDataSubdirectoryName(string subdirectoryString)
    {
        outputEmgDataSubdirectoryName = subdirectoryString;
    }



    public void setCsvEmgDataFileName(string fileName)
    {
        outputEmgDataFileName = fileName;
    }


    public void setCsvEmgDataRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfEmgDataHeadersForCsvFile = namesOfCsvDataHeaders;
        emgHeaderWritten = false;
    }



    public void storeRowOfEmgData(List<float> dataToStore)
    {
        EnsureEmgDataWriterInitialized();
        WriteRowToCsv(emgDataWriter, dataToStore.ToList(), ref emgHeaderWritten, namesOfEmgDataHeadersForCsvFile);
    }

    private void EnsureEmgDataWriterInitialized()
    {
        if (emgDataWriter == null)
        {
            string dir = getDirectoryPath() + outputEmgDataSubdirectoryName;
            Directory.CreateDirectory(dir);
            emgDataFilePath = dir + outputEmgDataFileName;
            emgDataWriter = new StreamWriter(emgDataFilePath, append: false);
        }
    }


    // This function could be used to plot the EMG data
    public (bool, List<float[]>) GetMostRecentNumberOfDataRows(int rowsToGet)
    {
        bool couldRetrieveRows = false;
        List<float[]> lastDataRows = new List<float[]>();
        if (emgDataStore.Count >= rowsToGet)
        {
            couldRetrieveRows = true;
            lastDataRows = emgDataStore.GetRange(emgDataStore.Count - rowsToGet - 1, rowsToGet);
        }

        return (couldRetrieveRows, lastDataRows);
    }

    public int GetNumberOfEmgDataRowsStored()
    {
        return emgDataStore.Count;
    }



    public void writeEmgDataToFile()
    {
        // Set flag indicating that we should retain and not delete the data file. 
        emgDataFinalized = true;

        // Close the data writer
        emgDataWriter?.Flush();
        emgDataWriter?.Close();
    }

    //END: public methods - EMG data *****************************************************
    
    
    
    //BEGIN: public methods - Stance model data *****************************************************

    public void setCsvStanceModelDataSubdirectoryName(string subdirectoryString)
    {
        outputStanceModelDataSubdirectoryName = subdirectoryString;
    }



    public void setCsvStanceModelDataFileName(string fileName)
    {
        outputStanceModelDataFileName = fileName;
    }


    public void setCsvStanceModelDataRowHeaderNames(string[] namesOfCsvDataHeaders)
    {
        namesOfStanceModelDataHeadersForCsvFile = namesOfCsvDataHeaders;
        stanceModelHeaderWritten = false;
    }
    
    public void storeRowOfStanceModelData(List<float> dataToStore)
    {
        EnsureStanceModelDataWriterInitialized();
        WriteRowToCsv(stanceModelDataWriter, dataToStore.ToList(), ref stanceModelHeaderWritten, namesOfStanceModelDataHeadersForCsvFile);
    }

    private void EnsureStanceModelDataWriterInitialized()
    {
        if (stanceModelDataWriter == null)
        {
            string dir = getDirectoryPath() + outputStanceModelDataSubdirectoryName;
            Directory.CreateDirectory(dir);
            stanceModelDataFilePath = dir + outputStanceModelDataFileName;
            stanceModelDataWriter = new StreamWriter(stanceModelDataFilePath, append: false);
        }
    }

    public void writeStanceModelDataToFile()
    {
        // Set flag indicating that we should retain and not delete the data file. 
        stanceModelDataFinalized = true;

        // Close the data writer
        stanceModelDataWriter?.Flush();
        stanceModelDataWriter?.Close();
    }

    public string GetStanceModelDataFilePath()
    {
        return stanceModelDataFilePath;
    }

    //END: public methods - Stance model data *****************************************************



    //BEGIN: public methods - Clearing data between blocks *****************************************************

    // Called to clear all of the stored marker, frame, and trial data. 
    // For example, called between blocks of trials if we're writing each block 
    // to its own file. 
    public void clearMarkerAndFrameAndTrialData()
    {
        // DEPRECATED! We don't store data in buffers anymore.
        markerDataStore.Clear();
        frameDataStore.Clear();
        trialDataStore.Clear();
        emgDataStore.Clear();
        stanceModelDataStore.Clear();
    }

    //END: public methods - Clearing data between blocks *****************************************************






    //BEGIN: private methods*****************************************************

    private void WriteRowToCsv(StreamWriter writer, List<float> row, ref bool headerWritten, string[] headers)
    {

        // Drop silently if the writer is closed or disposed
        if (writer == null || writer.BaseStream == null || !writer.BaseStream.CanWrite)
        {
            return;
        }

        // If the header row has not yet been written
        if (!headerWritten)
        {
            // Write the headers to file
            writer.WriteLine(string.Join(",", headers));
            headerWritten = true;
        }

        // Write the new line of data to file
        string line = string.Join(",", row.Select(x => x.ToString("G6"))); // or "F3" etc.
        writer.WriteLine(line);
        writer.Flush();
    }


    private void writeDataToCsv(List<float[]> dataToWrite, string[] namesOfDataHeadersForCsvFile, string filePath)
    {
        Debug.Log("Writing some data to CSV.");

        string[][] output = new string[dataToWrite.Count + 1][]; //output is an array of arrays

        output[0] = namesOfDataHeadersForCsvFile;

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
        Debug.Log("Storing data at " + filePath);
        StreamWriter outStream = System.IO.File.CreateText(filePath);
        outStream.WriteLine(dataFileStringBuilder);
        outStream.Close();
    }

    // Function to copy a CSV file - used if we want a time-stamped copy and a copy with a generic name for loading.
    public void CopyCsvFile(string sourcePath, string targetPath, bool overwrite = true)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                // Ensure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                File.Copy(sourcePath, targetPath, overwrite);
                Debug.Log($"Copied file from {sourcePath} to {targetPath}");
            }
            else
            {
                Debug.LogWarning($"Source file does not exist: {sourcePath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to copy file: {e}");
        }
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

    private void OnApplicationQuit()
    {
        // Close all the writers 
        CloseAllWriters();
        
        // Try to delete any written files, since the application terminated early.
        TryCleanupTempCsv(frameDataFinalized, frameDataFilePath);
        TryCleanupTempCsv(trialDataFinalized, trialDataFilePath);
        TryCleanupTempCsv(stanceModelDataFinalized, stanceModelDataFilePath);
        TryCleanupTempCsv(emgDataFinalized, emgDataFilePath);
        TryCleanupTempCsv(excursionPerformanceDataFinalized, excursionPerformanceDataFilePath);
        TryCleanupTempCsv(markerDataFinalized, markerDataFilePath);
    }

    private void CloseAllWriters()
    {
        if (markerDataWriter != null)
        {
            markerDataWriter.Flush();
            markerDataWriter.Close();
            markerDataWriter.Dispose();
            markerDataWriter = null;
        }

        if (frameDataWriter != null)
        {
            frameDataWriter.Flush();
            frameDataWriter.Close();
            frameDataWriter.Dispose();
            frameDataWriter = null;
        }

        if (trialDataWriter != null)
        {
            trialDataWriter.Flush();
            trialDataWriter.Close();
            trialDataWriter.Dispose();
            trialDataWriter = null;
        }

        if (emgDataWriter != null)
        {
            emgDataWriter.Flush();
            emgDataWriter.Close();
            emgDataWriter.Dispose();
            emgDataWriter = null;
        }

        if (stanceModelDataWriter != null)
        {
            stanceModelDataWriter.Flush();
            stanceModelDataWriter.Close();
            stanceModelDataWriter.Dispose();
            stanceModelDataWriter = null;
        }

        if (excursionPerformanceDataWriter != null)
        {
            excursionPerformanceDataWriter.Flush();
            excursionPerformanceDataWriter.Close();
            excursionPerformanceDataWriter.Dispose();
            excursionPerformanceDataWriter = null;
        }

        // Add more writers here if needed
    }

    private void TryCleanupTempCsv(bool isFinalized, string filePath)
    {
        if (!isFinalized && File.Exists(filePath))
        {
            File.Delete(filePath);
            Debug.Log("On application quit, deleted incomplete file: " + filePath);
        }
    }

    //END: private methods*******************************************************



}
