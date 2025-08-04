using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TestReconstruction : MonoBehaviour
{

    private Vector3 marker1Pos = new Vector3 ( -10.0f, 0.0f, 0.0f ); //this is the marker position in local pelvis coords in this demo
    private Vector3 marker2Pos = new Vector3 ( -10.0f, 1.0f, 0.0f );
    private Vector3 marker3Pos = new Vector3 ( -10.0f, 0.0f, 1.0f );
    private Vector3 marker4Pos = new Vector3 ( -9.0f, 1.0f, 1.0f );
    private Vector3 marker5Pos = new Vector3 (-10.5f, -1.0f, 2.3f);

    private string[] rigidBodyMarkerNames = new string[] { "M1", "M2", "M3", "M4", "M5" };
    private string[] markerNames = new string[] {"M1", "M2", "M3", "M4" ,"M5"};
    private Vector3[] markerPositionsThisFrame;
    private bool[] markersOccludedStatusThisFrame = new bool[] { false, false, false, true, true };

    private Vector3[] averagePositionOfModelMarkersInStartupFrames;

    // Start is called before the first frame update
    void Start()
    {
        averagePositionOfModelMarkersInStartupFrames = new Vector3[] { marker1Pos, marker2Pos, marker3Pos, marker4Pos, marker5Pos };
        markerPositionsThisFrame = new Vector3[] { marker1Pos, marker2Pos, marker3Pos, marker4Pos, marker5Pos };
    }

    // Update is called once per frame
    void Update()
    {
        Debug.Log("Calling reconstruct script");
        reconstructMissingMarkersOnOneRigidBodyThisFrame(rigidBodyMarkerNames, markersOccludedStatusThisFrame);
    }


    //Pretend here that the segment marker names are not necessarily in the same order as markerNames. 
    private void reconstructMissingMarkersOnOneRigidBodyThisFrame(string[] markersOnRigidBodyNames, bool[] markersOccludedStatus)
    {

        bool reconstructionSuccessFlag;

        List<string> markersVisibleNames = new List<string>();
        List<string> markersOccludedNames = new List<string>();
        //first, figure out which markers on the segment are visible and which are occluded.
        for(uint markerIndex = 0; markerIndex < markersOnRigidBodyNames.Length; markerIndex++) //for each marker on the segment in question
        {
            bool markerOccluded = markersOccludedStatus[Array.IndexOf(markerNames, markersOnRigidBodyNames[markerIndex])];

            if (markerOccluded) //if the marker is occluded
            {
                //add the name of the marker to the occluded marker list
                markersOccludedNames.Add(markersOnRigidBodyNames[markerIndex]);
            }
            else
            {
                //add the name of the marker to the visible marker list
                markersVisibleNames.Add(markersOnRigidBodyNames[markerIndex]);
            }
        }

        if(markersVisibleNames.Count >= 3) //if there are 3 or more markers, we can reconstruct
        {
            constructLocalFrameAndFindOccludedMarkerLocalCoordinates(markersVisibleNames, markersOccludedNames);
            reconstructionSuccessFlag = true;
        }
        else //we can't reconstruct, return false
        {
            reconstructionSuccessFlag = false;
        }

    }


    //Should only be calling this function if markersVisibleNames has 3 or more markers in it
    private void constructLocalFrameAndFindOccludedMarkerLocalCoordinates(List<string> markersVisibleNames,
                                                                     List<string> markersOccludedNames)
    {
        //with setup data, construct a coordinate system using the first three markers visible in the current frame
        Matrix4x4 transformationMatrixViconToLocalInSetup = constructRotationFromViconToLocalInSetupFrame(markersVisibleNames);

        //find the coordinates of the missing markers in this (setup local frame) coordinate system
        Vector3[] positionsOfMissingMarkersInLocalFrameInSetup = transformPositionsToNewCoordinateFrame(markersOccludedNames, markerNames,
            averagePositionOfModelMarkersInStartupFrames, transformationMatrixViconToLocalInSetup);

        //find the rotation matrix from the local coordinate system to global in the current frame
        Matrix4x4 transformationMatrixLocalToViconThisFrame = constructRotationFromLocalToViconThisFrame(markersVisibleNames);

        //finally, transform the missing marker coordinates from local frame to global frame
        Vector3[] reconstructedMissingMarkerPositionsInViconFrame = transformPositionsToNewCoordinateFrame(markersOccludedNames, markersOccludedNames.ToArray(),
            positionsOfMissingMarkersInLocalFrameInSetup, transformationMatrixLocalToViconThisFrame);

        //print reconstructed positions
        Vector3 firstReconstructedMarker = reconstructedMissingMarkerPositionsInViconFrame[0];
        Debug.Log("First reconstructed marker position with name " + markersOccludedNames[0] + " is (x,y,z): ( " + firstReconstructedMarker.x + ", " + firstReconstructedMarker.y + ", " + firstReconstructedMarker.z + " )");

        Vector3 secondReconstructedMarker = reconstructedMissingMarkerPositionsInViconFrame[1];
        Debug.Log("Second reconstructed marker position with name " + markersOccludedNames[1] + " is (x,y,z): ( " + secondReconstructedMarker.x + ", " + secondReconstructedMarker.y + ", " + secondReconstructedMarker.z + " )");

    }



    private Matrix4x4 constructRotationFromViconToLocalInSetupFrame(List<string> markersVisibleNames)
    {
        //get all needed marker positions FROM THE SETUP AVERAGED FRAMES!
        Vector3 marker1Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(markerNames, markersVisibleNames[0])]; ;
        Vector3 marker2Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(markerNames, markersVisibleNames[1])];
        Vector3 marker3Position = averagePositionOfModelMarkersInStartupFrames[Array.IndexOf(markerNames, markersVisibleNames[2])];

        //pelvis origin in global frame is average position of the two ASIS and two PSIS markers
        Vector3 originOfLocalFrameInViconFrame = marker1Position;

        //X-axis will be pointing from marker 1 to marker 2
        Vector3 xAxisVector = marker2Position - marker1Position;

        //Y-axis will be pointing from marker 1 to marker 3.
        //Note that to ensure orthogonality, will be recomputed from Z-axis.
        Vector3 yAxisVector = marker3Position - marker1Position;

        //Z-axis will be along the right-handed cross product of the x- and y-axes
        Vector3 zAxisVector = getRightHandedCrossProduct(xAxisVector, yAxisVector);

        //recompute y-axis as orthogonal to z and x
        yAxisVector = getRightHandedCrossProduct(zAxisVector, xAxisVector);

        //normalize the axes
        xAxisVector.Normalize();
        yAxisVector.Normalize();
        zAxisVector.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(xAxisVector, yAxisVector, zAxisVector, originOfLocalFrameInViconFrame);

        Matrix4x4 transformationMatrixViconToLocal = transformationMatrixLocalToVicon.inverse;

        return transformationMatrixViconToLocal;

    }


    private Matrix4x4 constructRotationFromLocalToViconThisFrame(List<string> markersVisibleThisFrameNames)
    {
        //get all needed marker positions FROM THE CURRENT FRAME!
        Vector3 marker1Position = markerPositionsThisFrame[Array.IndexOf(markerNames, markersVisibleThisFrameNames[0])]; ;
        Vector3 marker2Position = markerPositionsThisFrame[Array.IndexOf(markerNames, markersVisibleThisFrameNames[1])];
        Vector3 marker3Position = markerPositionsThisFrame[Array.IndexOf(markerNames, markersVisibleThisFrameNames[2])];

        //pelvis origin in global frame is average position of the two ASIS and two PSIS markers
        Vector3 originOfLocalFrameInViconFrame = marker1Position;

        //X-axis will be pointing from marker 1 to marker 2
        Vector3 xAxisVector = marker2Position - marker1Position;

        //Y-axis will be pointing from marker 1 to marker 3.
        //Note that to ensure orthogonality, will be recomputed from Z-axis.
        Vector3 yAxisVector = marker3Position - marker1Position;

        //Z-axis will be along the right-handed cross product of the x- and y-axes
        Vector3 zAxisVector = getRightHandedCrossProduct(xAxisVector, yAxisVector);

        //recompute y-axis as orthogonal to z and x
        yAxisVector = getRightHandedCrossProduct(zAxisVector, xAxisVector);

        //normalize the axes
        xAxisVector.Normalize();
        yAxisVector.Normalize();
        zAxisVector.Normalize();

        //get rotation and translation from local frame to global Vicon frame
        Matrix4x4 transformationMatrixLocalToVicon = getTransformationMatrix(xAxisVector, yAxisVector, zAxisVector, originOfLocalFrameInViconFrame);

        return transformationMatrixLocalToVicon;
    }



    private Vector3[] transformPositionsToNewCoordinateFrame(List<string> namesOfMarkersToTransform, string[] arrayOfMarkerNames,
            Vector3[] arrayOfMarkerPositions, Matrix4x4 transformationMatrix)
    {
        //initialize the return parameter, which will store the transformed positions
        Vector3[] transformedPositions = new Vector3[namesOfMarkersToTransform.Count];

        for(int markerIndex = 0; markerIndex < namesOfMarkersToTransform.Count; markerIndex++) //for each marker position to transform
        {
            //get the position of that marker in the original frame, then multiply it by the transform to the target frame
            transformedPositions[markerIndex] = transformationMatrix.MultiplyPoint3x4(arrayOfMarkerPositions[Array.IndexOf(arrayOfMarkerNames, namesOfMarkersToTransform[markerIndex])]);
        }

        return transformedPositions;
    }



    //Given the three normalized/unit axes of a local coordinate system and the translation FROM the target coordinate system
    //TO the local coordinate system defined in the target coordinate system, construct a transformation matrix
    //that will transform points in the local coordinate system to the target coordinate system
    private Matrix4x4 getTransformationMatrix(Vector3 xAxisVector, Vector3 yAxisVector, Vector3 zAxisVector, Vector3 translationTargetToLocalInTargetFrame)
    {
        Matrix4x4 transformationMatrixLocalToTarget = new Matrix4x4();

        //fill the columns of the transformation matrix
        transformationMatrixLocalToTarget.SetColumn(0, new Vector4(xAxisVector.x, xAxisVector.y,
            xAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(1, new Vector4(yAxisVector.x, yAxisVector.y,
            yAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(2, new Vector4(zAxisVector.x, zAxisVector.y,
            zAxisVector.z, 0));
        transformationMatrixLocalToTarget.SetColumn(3, new Vector4(translationTargetToLocalInTargetFrame.x,
            translationTargetToLocalInTargetFrame.y, translationTargetToLocalInTargetFrame.z, 1)); //last element is one

        return transformationMatrixLocalToTarget;


    }



    //Computes a right-handed cross-product so that we can work in the Vicon coordinate system
    private Vector3 getRightHandedCrossProduct(Vector3 leftVector, Vector3 rightVector)
    {
        float newXValue = leftVector.y * rightVector.z - leftVector.z * rightVector.y;
        float newYValue = leftVector.z * rightVector.x - leftVector.x * rightVector.z;
        float newZValue = leftVector.x * rightVector.y - leftVector.y * rightVector.x;

        return new Vector3(newXValue, newYValue, newZValue);

    }


    //Function that takes a transformation matrix and returns the opposite transformation
    //Totally useless if using the Matrix4x4 type since it has a .inverse property...
    private Matrix4x4 getInverseOfTransformationMatrix(Matrix4x4 transformationMatrixToInvert)
    {
        Vector4 newRotationFirstColumn = new Vector4(transformationMatrixToInvert[0, 0], transformationMatrixToInvert[0, 1],
            transformationMatrixToInvert[0, 2], 0);

        Vector4 newRotationSecondColumn = new Vector4(transformationMatrixToInvert[1, 0], transformationMatrixToInvert[1, 1],
            transformationMatrixToInvert[1, 2], 0);

        Vector4 newRotationThirdColumn = new Vector4(transformationMatrixToInvert[2, 0], transformationMatrixToInvert[2, 1],
            transformationMatrixToInvert[2, 2], 0);

        Vector4 newTranslation = new Vector4(-transformationMatrixToInvert[0, 3], -transformationMatrixToInvert[1, 3],
            -transformationMatrixToInvert[2, 3], 1);

        //fill the new matrix, which will be the inverse of the passed in transformation
        Matrix4x4 invertedTransformation = new Matrix4x4();

        invertedTransformation.SetColumn(0, newRotationFirstColumn);
        invertedTransformation.SetColumn(1, newRotationSecondColumn);
        invertedTransformation.SetColumn(2, newRotationThirdColumn);
        invertedTransformation.SetColumn(3, newTranslation);

        //return the inverted transformation
        return invertedTransformation;
    }

}
