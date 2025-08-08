using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisualizeForceFieldForceScript : MonoBehaviour
{

    public LevelManagerScriptAbstractClass levelManagerScript;

    public bool visualizeForceFieldToggle = false;
    private bool visualizeForceFieldToggleLastState = false;

    // The line renderer points
    private List<Vector3> lineRendererPoints = new List<Vector3>(); // representing the force vector

    // The player game object and script. NOTE: only needed to visualize force vector when using keyboard control. Otherwise, coudl remove. 
    public GameObject player; //  the player game object
    public ViveTrackerDataManager viveTrackerDataManagerScript;
    // Visualizing computed forces
    public LineRenderer chestForceVectorLineRenderer;
    public LineRenderer pelvisForceVectorLineRenderer;
    public LineRenderer shankForceVectorLineRenderer;

    // Line renderer width
    private float lineWidth = 0.03f;

    // Start is called before the first frame update
    void Start()
    {
        // Start the line renderer enable setting based on the current visualizeForceFieldFlag
        chestForceVectorLineRenderer.enabled = visualizeForceFieldToggle;
        pelvisForceVectorLineRenderer.enabled = visualizeForceFieldToggle;
        shankForceVectorLineRenderer.enabled = visualizeForceFieldToggle;

        // Set the line width
        chestForceVectorLineRenderer.startWidth = lineWidth;
        chestForceVectorLineRenderer.endWidth = lineWidth;
        pelvisForceVectorLineRenderer.startWidth = lineWidth;
        pelvisForceVectorLineRenderer.endWidth = lineWidth;
        shankForceVectorLineRenderer.startWidth = lineWidth;
        shankForceVectorLineRenderer.endWidth = lineWidth;
    }

    // Update is called once per frame
    void Update()
    {
        if (visualizeForceFieldToggle != visualizeForceFieldToggleLastState)
        {
            // toggle the line renderer from active to inactive or vice versa, 
            // to match the current state of the toggle boolean
            chestForceVectorLineRenderer.enabled = visualizeForceFieldToggle;
            pelvisForceVectorLineRenderer.enabled = visualizeForceFieldToggle;
            shankForceVectorLineRenderer.enabled = visualizeForceFieldToggle;

            // Update the last toggle boundary state
            visualizeForceFieldToggleLastState = visualizeForceFieldToggle;
        }
    }

    public void UpdateForceFieldVectorTrunk(Vector3 forceVectorScaledFrame0)
    {
        // Declare a list of the line renderer points
        List<Vector3> lineRendererPoints = new List<Vector3>(); // representing the force vector

        // Get the representation of the point of application of the pelvis force in Unity Frame.  
        Vector3 forceApplicationPointFrame0 = viveTrackerDataManagerScript.GetChestCenterPositionInFrame0();


        Matrix4x4 transformationFromFrame0ToUnityDisplayFrame =
            GetTransformationFromFrame0ToUnityDisplayFrame();

        // Rotate the force vector from frame 0 into the Unity display frame (task-specific)
        Vector3 forceVectorScaledInUnityDisplayFrame = transformationFromFrame0ToUnityDisplayFrame.MultiplyVector(forceVectorScaledFrame0);

        // Rotate the display point from frame 0 into the Unity display frame
        Vector3 forceApplicationPointUnityDisplayFrame = transformationFromFrame0ToUnityDisplayFrame.MultiplyVector(forceApplicationPointFrame0);

        // Set one end of the force vector at the current player position
        lineRendererPoints.Add(forceApplicationPointUnityDisplayFrame);

        // Set the other end of the force vector in the correct direction and with the passed-in magnitude
        Vector3 otherEndOfPelvicForceVectorUnityFrame = forceApplicationPointUnityDisplayFrame - forceVectorScaledInUnityDisplayFrame;
        lineRendererPoints.Add(otherEndOfPelvicForceVectorUnityFrame);

        Debug.Log("Chest force in frame 0: " + forceVectorScaledFrame0.x + ", " +
                  forceVectorScaledFrame0.y + ", " + forceVectorScaledFrame0.z + ")" +
            "Chest force in Unity frame: " + forceVectorScaledInUnityDisplayFrame.x + ", " +
                  forceVectorScaledInUnityDisplayFrame.y + ", " + forceVectorScaledInUnityDisplayFrame.z + ")");

        // Set the line renderer points to be the points in our LineRenderer
        chestForceVectorLineRenderer.SetPositions(lineRendererPoints.ToArray());
    }

    public Matrix4x4 GetTransformationFromFrame0ToUnityDisplayFrame()
    {
        Matrix4x4 transformationFrame0ToDisplay = new Matrix4x4();
        transformationFrame0ToDisplay.SetColumn(0,new Vector4 (0.0f, 1.0f, 0.0f, 0.0f));
        transformationFrame0ToDisplay.SetColumn(1,new Vector4 (1.0f, 0.0f, 0.0f, 0.0f));
        transformationFrame0ToDisplay.SetColumn(2,new Vector4 (0.0f, 0.0f, -1.0f, 0.0f));
        transformationFrame0ToDisplay.SetColumn(3,new Vector4 (0.0f,0.0f,0.0f,1.0f));

        return transformationFrame0ToDisplay;
    }
    public void UpdateForceFieldVectorPelvis(Vector3 forceVectorScaledFrame0)
    {
        // Declare a list of the line renderer points
        List<Vector3> lineRendererPoints = new List<Vector3>(); // representing the force vector

        // Get the representation of the point of application of the pelvis force in Unity Frame.  

        Vector3 forceApplicationPointFrame0 = viveTrackerDataManagerScript.GetPelvicCenterPositionInFrame0();
       
        
        Matrix4x4 transformationFromFrame0ToUnityDisplayFrame =
            GetTransformationFromFrame0ToUnityDisplayFrame();
        
        // Rotate the force vector from frame 0 into the Unity display frame (task-specific)
        Vector3 forceVectorScaledInUnityDisplayFrame = transformationFromFrame0ToUnityDisplayFrame.MultiplyVector(forceVectorScaledFrame0);

        // Rotate the display point from frame 0 into the Unity display frame
        Vector3 forceApplicationPointUnityDisplayFrame = transformationFromFrame0ToUnityDisplayFrame.MultiplyVector(forceApplicationPointFrame0);

        // Set one end of the force vector at the current player position
        lineRendererPoints.Add(forceApplicationPointUnityDisplayFrame);

        // Set the other end of the force vector in the correct direction and with the passed-in magnitude
        Vector3 otherEndOfPelvicForceVectorUnityFrame = forceApplicationPointUnityDisplayFrame - forceVectorScaledInUnityDisplayFrame;
        lineRendererPoints.Add(otherEndOfPelvicForceVectorUnityFrame);

        Debug.Log("Pelvic force in frame 0: " + forceVectorScaledFrame0.x + ", " +
                  forceVectorScaledFrame0.y + ", " + forceVectorScaledFrame0.z + ")" + 
            "Pelvic force in Unity frame: " + forceVectorScaledInUnityDisplayFrame.x + ", " +
                  forceVectorScaledInUnityDisplayFrame.y + ", " + forceVectorScaledInUnityDisplayFrame.z + ")");

        // Set the line renderer points to be the points in our LineRenderer
        pelvisForceVectorLineRenderer.SetPositions(lineRendererPoints.ToArray());
    }

    public void UpdateForceFieldVectorShank(Vector3 forceVectorScaledInFrame0)
    {
        // Declare a list of the line renderer points
        List<Vector3> lineRendererPoints = new List<Vector3>(); // representing the force vector

        // Get the representation of the point of application of the shank force in Unity Frame. 
        // We can either transform the passed in point, or call a function in the level manager. 
        Vector3 forceApplicationPointFrame0 = viveTrackerDataManagerScript.GetKneeCenterInFrame0();

        // Rotate the force vector from Vicon frame into the Unity display frame (task-specific)
        Matrix4x4 transformationFromFrame0ToUnityDisplayFrame =
            GetTransformationFromFrame0ToUnityDisplayFrame();
        Vector3 forceVectorScaledInUnityDisplayFrame = transformationFromFrame0ToUnityDisplayFrame.MultiplyPoint3x4(forceVectorScaledInFrame0);

        // Rotate the display point from frame 0 into the Unity display frame
        Vector3 forceApplicationPointUnityDisplayFrame = transformationFromFrame0ToUnityDisplayFrame.MultiplyVector(forceApplicationPointFrame0);

        // Set one end of the force vector at the current player position
        lineRendererPoints.Add(forceApplicationPointUnityDisplayFrame);

        // Set the other end of the force vector in the correct direction and with the passed-in magnitude
        lineRendererPoints.Add(forceApplicationPointUnityDisplayFrame - forceVectorScaledInUnityDisplayFrame);

        // Set the line renderer points to be the points in our LineRenderer
        shankForceVectorLineRenderer.SetPositions(lineRendererPoints.ToArray());
    }




}
