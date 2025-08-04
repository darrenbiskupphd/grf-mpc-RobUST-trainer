using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System;
using System.Text.RegularExpressions;
using UnityEngine;

public class LoadReachingAndLeaningLimits : MonoBehaviour
{

    private float[] reachAndLeanLimitsFromFile;
    private string[] headersFromFile;
    private Dictionary<string, float> reachLimitByHeightDir = new Dictionary<string, float>();

    //the constant part of an excursion performancec summary file name
    private const string excursionPerformanceSummaryPrefix = "BestReachAndLeanDistances";

    // Start is called before the first frame update
    void Start()
    {


        // Store a reference to the line renderer
       // boundaryLineRenderer = GetComponent<LineRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
/*        if (toggleBoundaryOnOff != lastToggleBoundaryState)
        {
            // toggle the boundary of support line renderer from active to inactive or vice versa.
            boundaryLineRenderer.enabled = !boundaryLineRenderer.enabled;

            // Update the last toggle boundary state
            lastToggleBoundaryState = toggleBoundaryOnOff;
        }*/
    }


    //BEGIN: getter functions*************************************************************************************

    // Right now, returns the whole float[] loaded from file
    public Dictionary<string, float> GetReachingLimits()
    {
        return reachLimitByHeightDir;
    }




    //END: getter functions*************************************************************************************


    // This function tells this game object to load the boundary of stability and reach limits. It must be called
    // by the level manager, which knows the subject number and other details
    // specifying the path to save to/load from.
    public void loadBoundaryOfStability(string pathToDirectoryWithFile, string keyword = "")
    {
        Debug.Log("Loading reach and lean limits data from local path: " + pathToDirectoryWithFile);

        //load the excursion limits for the current subject, if available.
        (reachAndLeanLimitsFromFile, headersFromFile) = LoadReachAndLeanLimits(pathToDirectoryWithFile, keyword);

        // ---------------------------------------------------------------
        // Build lookup of reach-limit values keyed by "heightDir" strings.
        // Requires: using System.Text.RegularExpressions;
        // ---------------------------------------------------------------

        // Example header format: MAX_REACHING_DIRECTION_90_0_HEIGHT_waist
        Regex reachingHeaderRegex =
            new Regex(@"MAX_REACHING_DIRECTION_(\d+)_\d+_HEIGHT_(\w+)", RegexOptions.IgnoreCase);

        for (int i = 0; i < headersFromFile.Length && i < reachAndLeanLimitsFromFile.Length; i++)
        {
            string header = headersFromFile[i];

            // Skip columns that are not reach limits.
            Match m = reachingHeaderRegex.Match(header);
            if (!m.Success) continue;

            // --- Extract direction (first capture group) ---
            int directionDeg = int.Parse(m.Groups[1].Value);   // 0,45,90,135,180

            // --- Extract height string (second capture group) and map to int 0/1/2 ---
            string heightStr = m.Groups[2].Value.ToLowerInvariant();
            int heightId;
            switch (heightStr)
            {
                case "waist": heightId = 0; break;
                case "chest": heightId = 1; break;
                case "hmd": heightId = 2; break;
                default:
                    Debug.LogWarning($"[ReachLimitParser] Unrecognised height \"{heightStr}\" in header \"{header}\".");
                    continue;
            }

            // --- Compose key and store value ---
            string compositeKey = $"{heightId}_{directionDeg}";  // e.g., "0_90"
            float reachValue = reachAndLeanLimitsFromFile[i];

            reachLimitByHeightDir[compositeKey] = reachValue;
        }

        // (Optional) quick sanity-check print-out (keep).
        foreach (var kvp in reachLimitByHeightDir)
        {
            Debug.Log($"Loaded reach limits: reach limit key {kvp.Key} → {kvp.Value:F3} m");
        }
    }


    private (float[], string[]) LoadReachAndLeanLimits(string localPathToFolder, string keyword)
    {
        // Get all files in the directory
        string pathToFolder = getDirectoryPath() + localPathToFolder;
        Debug.Log("Trying to load reach and lean performance data from the path: " + pathToFolder);
        string[] allFiles = System.IO.Directory.GetFiles(pathToFolder);
        Debug.Log("Loaded the following number of files from the specified path: " + allFiles.Length);

        // Get the name of the most recent excursion performance summary file (with a keyword, such as "No_Stim", if desired)
        string fileToUseName = "";
        DateTime dateTimeOfFileToUse = new DateTime();
        for (uint fileIndex = 0; fileIndex < allFiles.Length; fileIndex++)
        {
            string fileName = allFiles[fileIndex];
            bool fileNameOfAnExcursionPerformanceSummaryFile = fileName.Contains(excursionPerformanceSummaryPrefix);
            bool hasKeyword = keyword == "" || fileName.Contains(keyword);
            bool isMetaFile = fileName.Contains("meta");

            if (fileNameOfAnExcursionPerformanceSummaryFile && hasKeyword && !isMetaFile)
            {
                DateTime currentFileTime = System.IO.File.GetCreationTime(fileName);
                if (fileToUseName == "" || DateTime.Compare(currentFileTime, dateTimeOfFileToUse) > 0)
                {
                    fileToUseName = fileName;
                    dateTimeOfFileToUse = currentFileTime;
                }
            }
        }

        Debug.Log("Loading from the following Reach and Lean peformance summary file path: " + fileToUseName);

        // Read the file text
        string allFileTextString = System.IO.File.ReadAllText(fileToUseName);

        // Split into lines
        string[] rowsFromFile = allFileTextString.Split(new char[] { '\n' }, 2);

        // Get headers from the first line
        string[] headers = rowsFromFile[0].Trim().Split(',');

        // Debug print all headers
        for (int i = 0; i < headers.Length; i++)
        {
            Debug.Log($"Header {i}: {headers[i]}");
        }

        // Split the second row (first data row) into values
        string[] firstDataRow = rowsFromFile[1].Trim().Split(',');

        float[] firstDataRowAsFloat = new float[firstDataRow.Length];
        for (int entryIndex = 0; entryIndex < firstDataRow.Length; entryIndex++)
        {
            Debug.Log("String to parse into a float is: " + firstDataRow[entryIndex]);
            firstDataRowAsFloat[entryIndex] = float.Parse(firstDataRow[entryIndex], CultureInfo.InvariantCulture.NumberFormat);
        }

        return (firstDataRowAsFloat, headers);
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
