using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;

public class MeasureAnkleRomScript : MonoBehaviour
{

    public class ankleJointRangeOfMotionDataStorageObject
    {
        private bool anyFramesSeen = false;
        private float ankleAngleMaxAngle = -1.0f;
        private float ankleAngleMinAngle = -1.0f;


        // The constructor.
        public ankleJointRangeOfMotionDataStorageObject()
        {

        }


        //Function to see if the passed in ankle angle is a minimum or maximum observed ankle angle.
        public void checkIfAngleIsMaxOrMin(float currentAnkleAngle)
        {
            if (!this.anyFramesSeen) //if this is the first ankle angle this data storage object has been sent
            {
                //then the current ankle angle is both the minimum and maximum
                ankleAngleMaxAngle = currentAnkleAngle;
                ankleAngleMinAngle = currentAnkleAngle;

                //flip the first-frame-seen flag, to indicate that we have now already seen some data
                this.anyFramesSeen = true;

            }
            else //if we have previously seen this ankle's angle data (and thus have a reference for minimum and maximum)
            {
                //if the current ankle angle is a new minimum
                if (currentAnkleAngle < this.ankleAngleMinAngle)
                {
                    this.ankleAngleMinAngle = currentAnkleAngle; //set the minimum equal to the current ankle angle
                }

                //if the current ankle angle is a new maxmimum
                if (currentAnkleAngle > this.ankleAngleMaxAngle)
                {
                    this.ankleAngleMaxAngle = currentAnkleAngle; //set the maximum equal to the current ankle angle
                }
            }
        }

        public bool hasValidData()
        {
            return this.anyFramesSeen;
        }


        public float getAnkleMinimumAngle()
        {
            return this.ankleAngleMinAngle;
        }

        public float getAnkleMaximumAngle()
        {
            return this.ankleAngleMaxAngle;
        }

        //A setter function. Called if we load ankle ROM 
        //data from file.
        public void setAnkleMinimumAndMaximumAngles(float ankleMin, float ankleMax)
        {
            this.ankleAngleMinAngle = ankleMin;
            this.ankleAngleMaxAngle = ankleMax;
            this.anyFramesSeen = true; //set the frames seen flag to true, indicating that we have valid values to share.
        }

    }

    public GameObject ankleSceneLevelManager;
    public GameObject experimentalSettingsObject;
    private SinusoidExperimentalParametersScript experimentalSettingsScript; 
    private LevelManagerAnkleSinusoid ankleSceneLevelManagerScript;

    // A boolean to indicate if we should load the ankle ROM from file. Toggling this on during
    // runtime will load the ROM data from file, if and only if there is not ALREADY a valid 
    // ROM for both ankles.
    private bool loadAnkleRomFromDailyFilePath; 

    private const string leftAnkleActiveString = "Left";
    private const string rightAnkleActiveString = "Right";

    private string[] ankleRomSaveFileDataHeaders = new string[] { "rightAnkleMinimumAngle", "rightAnkleMaximumAngle", "leftAnkleMinimumAngle", "leftAnkleMaximumAngle" };

    // Store limits of the ankle ROM. 
    // Note: as defined, a more positive ankle angle is more dorsiflexion. 
    // The ankle angle as defined is always positive. An angle of 90 degrees 
    // corresponds to a right angle between the shank and foot main axis (from proximal to distal).
    ankleJointRangeOfMotionDataStorageObject leftAnkleRomData;
    ankleJointRangeOfMotionDataStorageObject rightAnkleRomData;

    // Define a minimum value for the ankle ROM to say we have a "valid" ROM. Just a low bar to have.
    private const float minimumAnkleAngleRangeForValidRomTest = 10.0f;
    
    // Define flags that state whether or not we have an ROM of a minimum size for each ankle. 
    // Just used as a minimum requirement for running the ankle sinusoid assessment.
    private bool leftAnkleHasValidRom = false;
    private bool rightAnkleHasValidRom = false;






    // Start is called before the first frame update
    void Start()
    {
        // Get a reference to the Sinusoid level manager 
        ankleSceneLevelManagerScript = ankleSceneLevelManager.GetComponent<LevelManagerAnkleSinusoid>();

        // Get a reference to the Experimental Settings script
        experimentalSettingsScript = experimentalSettingsObject.GetComponent<SinusoidExperimentalParametersScript>();

        //initialize the ankle ROM data holder objects for each ankle
        leftAnkleRomData = new ankleJointRangeOfMotionDataStorageObject();
        rightAnkleRomData = new ankleJointRangeOfMotionDataStorageObject();

        // See if we're loading the ankle ROM data from file (or not)
        loadAnkleRomFromDailyFilePath = experimentalSettingsScript.getLoadAnkleRomFromDailyFileFlag();
    }



    // Update is called once per frame
    void Update()
    {
        //first, see if we're currently measuring range of motion (ROM) for either ankle
        bool currentlyMeasuringAnkleRom = ankleSceneLevelManagerScript.getMeasuringRangeOfMotionStatus();

        if (currentlyMeasuringAnkleRom && !loadAnkleRomFromDailyFilePath) //if we're measuring the ankle ROM live 
        {
            //update the ankle angle max and min values
            updateActiveAnkleRom();
        }
        else if (loadAnkleRomFromDailyFilePath) //else if we're loading ankle ROM values from file
        {
            if(!leftAnkleHasValidRom || !rightAnkleHasValidRom) //only if we don't already have a valid ROM data set
            {
                //load the ROM values
                bool successfullyLoadedAnkleRoms = loadAnkleRomDataFromFile();
            }
        }
    }



    private void updateActiveAnkleRom()
    {
        (bool successRetrievingAnkleAngle, float ankleAngle) = ankleSceneLevelManagerScript.getPlantarflexionAngleOfActiveAnkle();
        
        //if we successfully got the ankle angle
        if (successRetrievingAnkleAngle)
        {
            //update the minimum or maximum ankle angle, if the new ankle angle is a maximum or minimum
            if(ankleSceneLevelManagerScript.getActiveAnkle() == leftAnkleActiveString) //if the left ankle is active
            {
                leftAnkleRomData.checkIfAngleIsMaxOrMin(ankleAngle);
                //print the ROM to debug
                float minAngle = leftAnkleRomData.getAnkleMinimumAngle();
                float maxAngle = leftAnkleRomData.getAnkleMaximumAngle();

                float range = maxAngle - minAngle;

                // if the range of motion of the ankle is sufficient, we can say we have captured a valid 
                // data set. This is used so that we have a minimum bar for running the sinusoid assessment.
                if(range > minimumAnkleAngleRangeForValidRomTest)
                {
                    leftAnkleHasValidRom = true;
                }

                //Debug.Log("Updating left ROM: current ankle ROM (min, max, range) is: (" + minAngle + ", " + maxAngle + ", " + (maxAngle - minAngle) + " )");
            }
            else //assume that if the left ankle is not active, the right ankle is active
            {
                rightAnkleRomData.checkIfAngleIsMaxOrMin(ankleAngle);
                //print the ROM to debug
                float minAngle = rightAnkleRomData.getAnkleMinimumAngle();
                float maxAngle = rightAnkleRomData.getAnkleMaximumAngle();

                float range = maxAngle - minAngle;

                // if the range of motion of the ankle is sufficient, we can say we have captured a valid 
                // data set. This is used so that we have a minimum bar for running the sinusoid assessment.
                if (range > minimumAnkleAngleRangeForValidRomTest)
                {
                    rightAnkleHasValidRom = true;
                }

                //Debug.Log("Current ankle ROM (min, max, range) is: (" + minAngle + ", " + maxAngle + ", " + (maxAngle - minAngle) + " )");
            }
        }
    }



    private bool loadAnkleRomDataFromFile()
    {
        Debug.LogWarning("Reading ankle ROM data from file!");
        string fileName = ankleSceneLevelManagerScript.getFilePathToAnkleRomSavedData();
        Debug.Log("Attempting to read ankle ROM data file from the following path: " + fileName);

        // if the file path exists
        if (System.IO.File.Exists(fileName))
        {
            // As the file path exists, read it in
            string allFileTextString = System.IO.File.ReadAllText(fileName);
            //split into lines, delimited by the newline character
            char[] separator = new char[] { '\n' };
            string[] rowsFromFile = allFileTextString.Split(separator, 2); // 2 limits lines returned to 2, as we only care about the first data row.
            //split first data row (second row, after the headers) into cells/entries, delimited by commas
            separator = new char[] { ',' };
            string[] firstDataRow = rowsFromFile[1].Split(separator, 100);
            //Convert each string in the data row to a float
            float[] firstDataRowAsFloat = new float[firstDataRow.Length];
            for (uint entryIndex = 0; entryIndex < firstDataRow.Length; entryIndex++)
            {
                Debug.Log("Parsing ankle ROM data. String to parse into a float is: " + firstDataRow[entryIndex]);
                firstDataRowAsFloat[entryIndex] = float.Parse(firstDataRow[entryIndex], CultureInfo.InvariantCulture.NumberFormat);
            }

            // Parse the ankle data 
            float rightAnkleAngleMin = firstDataRowAsFloat[0];
            float rightAnkleAngleMax = firstDataRowAsFloat[1];
            float leftAnkleAngleMin = firstDataRowAsFloat[2];
            float leftAnkleAngleMax = firstDataRowAsFloat[3];

            // Compute the ankle ranges of motion based on the loaded data
            float leftAnkleRange = leftAnkleAngleMax - leftAnkleAngleMin;
            float rightAnkleRange = rightAnkleAngleMax - rightAnkleAngleMin;

            //only allow the loaded data to be used if the ranges are reasonably large. 
            if ((leftAnkleRange > minimumAnkleAngleRangeForValidRomTest) && (rightAnkleRange > minimumAnkleAngleRangeForValidRomTest))
            {
                Debug.LogWarning("Successfully loaded ROM data from file!");
                leftAnkleRomData.setAnkleMinimumAndMaximumAngles(leftAnkleAngleMin, leftAnkleAngleMax);
                rightAnkleRomData.setAnkleMinimumAndMaximumAngles(rightAnkleAngleMin, rightAnkleAngleMax);

                //set the boolean flags indicating we have valid ROMs. This will stop us from trying to load data from file again.
                leftAnkleHasValidRom = true;
                rightAnkleHasValidRom = true;
            }
            else
            {
                Debug.LogError("Loaded ROM data is not valid (ROM is not large enough). Setting Load Rom Data from File bool to false.");
            }

            // return a tuple
            bool wereAnkleRomsSuccessfullyLoaded = true;
            return wereAnkleRomsSuccessfullyLoaded;
        }
        else
        {
            Debug.LogWarning("Attempted to load ankle ROM data file, but the foll0wing file path did not exist: " + fileName);
            return false;
        }


/*        var dataset = AssetDatabase.LoadAssetAtPath<TextAsset>(fileName);
        string[] lines = dataset.text.Split('\n');

        string[][] allData = new string[lines.Length][];
        int  numColumns = 0;
        Debug.Log("File has the following number of lines: " + lines.Length);
        Debug.Log("Broken on newlines, the file's first line is: " + lines[0]);
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string[] lineData = lines[lineIndex].Split(',');
            allData[lineIndex] = lineData;
            numColumns = Mathf.Max(numColumns, lineData.Length);
        }

        Debug.Log("File has the following number of columns = " + numColumns);

        float rightAnkleAngleMin = float.Parse(allData[1][0]);
        float rightAnkleAngleMax = float.Parse(allData[1][1]);
        float leftAnkleAngleMin = float.Parse(allData[1][2]);
        float leftAnkleAngleMax = float.Parse(allData[1][3]);

        float leftAnkleRange = leftAnkleAngleMax - leftAnkleAngleMin;
        float rightAnkleRange = rightAnkleAngleMax - rightAnkleAngleMin;

        //only allow the loaded data to be used if the ranges are reasonably large. 
        if ((leftAnkleRange > minimumAnkleAngleRangeForValidRomTest) && (rightAnkleRange > minimumAnkleAngleRangeForValidRomTest))
        {
            Debug.LogWarning("Successfully loaded ROM data from file!");
            leftAnkleRomData.setAnkleMinimumAndMaximumAngles(leftAnkleAngleMin, leftAnkleAngleMax);
            rightAnkleRomData.setAnkleMinimumAndMaximumAngles(rightAnkleAngleMin, rightAnkleAngleMax);

            //set the boolean flags indicating we have valid ROMs. This will stop us from trying to load data from file again.
            leftAnkleHasValidRom = true;
            rightAnkleHasValidRom = true;
}
        else
        {
            Debug.LogError("Loaded ROM data is not valid (ROM is not large enough). Setting Load Rom Data from File bool to false.");
            loadAnkleRomFromDailyFilePath = false;
        }

        for (int rowIndex = 0; rowIndex < lines.Length; rowIndex++)  
        {
            for (int colIndex = 0; colIndex < numColumns; colIndex++)
            {
                try
                {
                    Debug.Log("Read ankle ROM file data. Entry on line " + rowIndex + "and in column " + colIndex + "has value: " + allData[rowIndex][colIndex]); // we transpose them intentionally
                }
                catch
                { // with try/catch it won't explode if this particular column/row is out of range
                    Debug.Log("*");
                }
            }
        }*/
    }

    //getter functions for ROM data

    public (bool, string, float, float) getActiveAnkleIdentifierAndRangeOfMotion()
    {
        bool successfulFetchOfAnkleData = false;
        string activeAnkle = ankleSceneLevelManagerScript.getActiveAnkle();
        float minimumAnkleAngle = -1.0f;
        float maximumAnkleAngle = -1.0f;

        float minimumAnkleAngleRange = 1.0f;

        if ((activeAnkle == leftAnkleActiveString) && (leftAnkleRomData.hasValidData())) //if the left ankle is active
        {
            minimumAnkleAngle = leftAnkleRomData.getAnkleMinimumAngle();
            maximumAnkleAngle = leftAnkleRomData.getAnkleMaximumAngle();
            float ankleRange = maximumAnkleAngle - minimumAnkleAngle;
            
            // Only return the data if the ankle range is big enough. 
            // The range could be too small (or 0) at start-up.
            if (ankleRange > minimumAnkleAngleRange)
            {
                successfulFetchOfAnkleData = true;
            }
        }
        else if ((activeAnkle == rightAnkleActiveString) && (rightAnkleRomData.hasValidData()))
        {
            minimumAnkleAngle = rightAnkleRomData.getAnkleMinimumAngle();
            maximumAnkleAngle = rightAnkleRomData.getAnkleMaximumAngle();

            float ankleRange = maximumAnkleAngle - minimumAnkleAngle;

            // Only return the data if the ankle range is big enough. 
            // The range could be too small (or 0) at start-up.
            if (ankleRange > minimumAnkleAngleRange)
            {
                successfulFetchOfAnkleData = true;
            }
        }
        else //if we don't have valid data yet
        {

        }


        return (successfulFetchOfAnkleData, activeAnkle, minimumAnkleAngle, maximumAnkleAngle);
    }




    public (string[], float[]) getAnkleRomDataInFormatToSave()
    {
        float[] anklesRangeOfMotionRightLeft = new float[] { rightAnkleRomData.getAnkleMinimumAngle(), rightAnkleRomData.getAnkleMaximumAngle(),
            leftAnkleRomData.getAnkleMinimumAngle() , leftAnkleRomData.getAnkleMaximumAngle()};

        return (ankleRomSaveFileDataHeaders, anklesRangeOfMotionRightLeft);
    }



    public (bool, bool) getRomValidityFlagForEachAnkle()
    {
        return (rightAnkleHasValidRom, leftAnkleHasValidRom);
    }





}
