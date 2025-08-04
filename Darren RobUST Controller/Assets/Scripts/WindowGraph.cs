using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WindowGraph : MonoBehaviour
{
    public RectTransform graphContainer; // a RectTransform type, which serves as the graph container
    public RectTransform labelTemplateX = new RectTransform(); // a RectTransform type, serves as a template for x-tick labels
    public RectTransform labelTemplateY; // a RectTransform type, serves as a template for y-tick labels

    [SerializeField] private Sprite circleSprite;
    [SerializeField] private Sprite squareSprite;


    private List<GameObject> gameObjectList = new List<GameObject>();

    // The General Data Recorder
/*    public GameObject generalDataRecorderObject;
    private GeneralDataRecorder generalDataRecorderScript;*/

    // Plotting
    private int howManyEmgSamplesToPlot = 2000;
    private int emgOfInterest = 0; // the EMG channel to plot
    private int fixedUpdateCounter = 0; // a counter so we can plot every so many counts of the main FixedUpdate() loop.

    // Start is called before the first frame update
    void Start()
    {

        // Get a reference to the general data recorder script
        //generalDataRecorderScript = generalDataRecorderObject.GetComponent<GeneralDataRecorder>();

        // Get a reference to the GraphContainer child's RectTransform component
        //graphContainer = new RectTransform();
        //graphContainer = GameObject.FindGameObjectsWithTag("GraphContainer")[0].GetComponent<RectTransform>();
        //labelTemplateX = graphContainer.Find("labelTemplateX").GetComponent<RectTransform>();
        //labelTemplateY = graphContainer.Find("labelTemplateY").GetComponent<RectTransform>();

        //List<float> valueList = new List<float> { 5.0f, 10.0f, 10.0f, 85.0f, 85.0f, 98.0f, 12.5f,  7.5f, 0.0f};
        //ShowGraph(valueList);

        //List<float> valueListTwo = new List<float> { 8.0f, 10.0f, 10.0f, 85.0f, 85.0f, 98.0f, 12.5f, 7.5f, 0.0f };
        //ShowGraph(valueListTwo);


    }

    // Update is called once per frame
    void FixedUpdate()
    {
/*        fixedUpdateCounter += 1; // increment fixed update loop counter 

        if (fixedUpdateCounter % 2 == 0)
        {
            // Get the EMG data
            (bool retrievedEmgData, List<float[]> mostRecentEmgData) =
                generalDataRecorderScript.GetMostRecentNumberOfDataRows(howManyEmgSamplesToPlot);

            //Debug.Log("Retrieved EMG data rows: number of rows = " + mostRecentEmgData.Count);

            if (retrievedEmgData)
            {
                // Construct a list of floats that contains only the EMG of interest
                List<float> emgOfInterestData = new List<float>();
                for (int sampleIndex = 0; sampleIndex < mostRecentEmgData.Count; sampleIndex++)
                {
                    emgOfInterestData.Add(mostRecentEmgData[sampleIndex][emgOfInterest]);
                }

                //Debug.Log("Graphing script retrieved EMG data.");
                //Debug.Log("Most recent EMG channel " + emgOfInterest + " value: " + emgOfInterestData[emgOfInterestData.Count-1]);

                // Plot the EMG data of interest
                //ShowGraph(emgOfInterestData);
            }
        }*/
    }


    // A public function to plot data. 
    // valueList contains y-axis values
    // timeStep determines the x-axis tick spacing
    // markerTypePerObservation will use circle data markers if = 1 or square otherwise, per observation. If null, circles are used.
    public void PlotDataPointsOnWindowGraph(List<float> valueList, float timeStep, List<int> markerTypePerObservation = null)
    {
        // Pass the data on to the plotting window
        ShowGraph(valueList, timeStep, markerTypePerObservation);
    }


    // Essentially, creating a data point
    private GameObject CreateCircle(Vector2 anchoredPosition)
    {
        GameObject gameObject = new GameObject("circle", typeof(Image));
        gameObject.transform.SetParent(graphContainer, false);
        gameObject.GetComponent<Image>().sprite = circleSprite;
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        // Set anchored position to passed-in position
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(5, 5);
        // Anchor to lower left corner
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        // Set marker size
        rectTransform.sizeDelta = new Vector2(15.0f, 15.0f);
        // Return the created game object
        return gameObject;
    }

    // Essentially, creating a data point, but square
    private GameObject CreateSquare(Vector2 anchoredPosition)
    {
        GameObject gameObject = new GameObject("square", typeof(Image));
        gameObject.transform.SetParent(graphContainer, false);
        gameObject.GetComponent<Image>().sprite = squareSprite;
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        // Set anchored position to passed-in position
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(5, 5);
        // Anchor to lower left corner
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        // Set marker size
        rectTransform.sizeDelta = new Vector2(15.0f, 15.0f);
        // Return the created game object
        return gameObject;
    }



    private void ShowGraph(List<float> valueList, float timeStep, List<int> markerTypePerObservation)
    {
        // Clear the graph
        foreach(GameObject gameObject in gameObjectList)
        {
            Destroy(gameObject);
        }
        gameObjectList.Clear();

        // Y-axis axis limits
        float yAxisMaximum = -Mathf.Infinity;
        float yAxisMinimum = Mathf.Infinity;
        foreach(float value in valueList)
        {
            if(value > yAxisMaximum)
            {
                yAxisMaximum = value;
            }

            if (value < yAxisMinimum)
            {
                yAxisMinimum = value;
            }
        }
        // Add a buffer by scaling the maximum
        Debug.Log("Y-axis max = " + yAxisMaximum);
        Debug.Log("Y-axis min = " + yAxisMinimum);
        yAxisMaximum = yAxisMaximum + (yAxisMaximum - yAxisMinimum) * 0.2f;
        yAxisMinimum = yAxisMinimum - (yAxisMaximum - yAxisMinimum) * 0.2f;
        Debug.Log("Y-axis max = " + yAxisMaximum);
        Debug.Log("Y-axis min = " + yAxisMinimum);

        // Set the time bound minimum and maximum
        float timeMinimum = 0.0f;
        float timeMaximum = timeStep * valueList.Count;

        // If we have valid data where the voltage maximum is greater than the minimum (not all zeros)
        if (yAxisMaximum > yAxisMinimum)
        {
            float graphHeight = graphContainer.sizeDelta.y;
            float graphWidth = graphContainer.sizeDelta.x;
            GameObject lastDataPoint = null;
            for (int pointIndex = 0; pointIndex < valueList.Count; pointIndex++)
            {
                // Time axis increments by 1
                float timeValue = pointIndex * timeStep;

                float normalizedTimeValue = ((timeValue - timeMinimum) / (timeMaximum - timeMinimum)) * graphWidth;

                // Normalize the value by the maximum expected reading and fit to graph
                float voltageValue = ((valueList[pointIndex] - yAxisMinimum) / (yAxisMaximum - yAxisMinimum)) * graphHeight;

                // Add the data point to the graph 
                GameObject dataPointObject = new GameObject();
                if (markerTypePerObservation == null)
                {
                    dataPointObject = CreateCircle(new Vector2(normalizedTimeValue, voltageValue));
                }
                else
                {
                    int currentMarkerType = markerTypePerObservation[pointIndex];
                    if(currentMarkerType == 1)
                    {
                        dataPointObject = CreateCircle(new Vector2(normalizedTimeValue, voltageValue));
                    }
                    else
                    {
                        dataPointObject = CreateSquare(new Vector2(normalizedTimeValue, voltageValue));
                    }
                }

                // Store a reference to the data point so it can be destroyed later when clearing graph
                gameObjectList.Add(dataPointObject);

                // If the last data point object was not null
                if (lastDataPoint != null)
                {
                    GameObject dataLineObject = CreateDataPointConnectionLines(lastDataPoint.GetComponent<RectTransform>().anchoredPosition,
                        dataPointObject.GetComponent<RectTransform>().anchoredPosition);
                    // Store a reference to the data line so it can be destroyed later when clearing graph
                    gameObjectList.Add(dataLineObject);
                }

                // Store last-created data point object
                lastDataPoint = dataPointObject;

                // Add an x-label (for every occasional sample)

                RectTransform labelX = Instantiate(labelTemplateX);
                labelX.SetParent(graphContainer);
                labelX.gameObject.SetActive(true);
                labelX.anchoredPosition = new Vector2(normalizedTimeValue, -10.0f);
                labelX.GetComponent<Text>().text = timeValue.ToString();
                // Store a reference to the x-axis tick label so it can be destroyed later when clearing graph
                gameObjectList.Add(labelX.gameObject);
                
            }

            // Add a fixed number of y-axis tick labels
            int numberOfYTickLabels = 20;
            for (int yTickIndex = 0; yTickIndex <= numberOfYTickLabels; yTickIndex++)
            {
                RectTransform labelY = Instantiate(labelTemplateY);
                labelY.SetParent(graphContainer);
                labelY.gameObject.SetActive(true);
                // Compute a normalized position for the y-axis tick label
                float normalizedYAxisValue = yTickIndex * (1.0f / numberOfYTickLabels);
                labelY.anchoredPosition = new Vector2(-7.0f, normalizedYAxisValue * graphHeight);
                labelY.GetComponent<Text>().text = (yAxisMinimum + normalizedYAxisValue * (yAxisMaximum - yAxisMinimum)).ToString();
                // Store a reference to the y-axis tick label so it can be destroyed later when clearing graph
                gameObjectList.Add(labelY.gameObject);
            }
        }
    }


    private GameObject CreateDataPointConnectionLines(Vector2 dotPositionA, Vector2 dotPositionB)
    {
        GameObject gameObject = new GameObject("dotConnection", typeof(Image));
        gameObject.transform.SetParent(graphContainer, false);
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        // Set color to translucent white
        gameObject.GetComponent<Image>().color = new Color(1, 1, 1, 0.5f);
        // Manage the rotation and length of the line segment
        Vector2 directionAdjacentDataPoints = (dotPositionB - dotPositionA).normalized;
        float distance = Vector2.Distance(dotPositionA, dotPositionB);
        rectTransform.localEulerAngles = new Vector3(0.0f, 0.0f,
            (180.0f / Mathf.PI) * Mathf.Atan2(directionAdjacentDataPoints.y, directionAdjacentDataPoints.x));
        // Set anchored position to passed-in position
        rectTransform.anchoredPosition = dotPositionA + 0.5f * distance * directionAdjacentDataPoints;
        rectTransform.sizeDelta = new Vector2(distance, 3f);
        // Anchor to lower left corner
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        // Return game object
        return gameObject;
    }
}
