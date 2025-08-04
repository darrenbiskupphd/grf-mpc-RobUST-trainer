using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using UnityEngine;

public class CableTensionPlannerScript : MonoBehaviour
{

	// Select if we're controlling the pelvic torques closely or not
	public EnforcePelvicTorqueBoundsControl enforcePelvicTorqueBoundsControl;

	// The level manager. We only need a reference to see if Debug mode is active
	public LevelManagerScriptAbstractClass levelManagerScript;
	private bool debugModeFlag; // set by Level Manager, calls local function SetDebugModeFlag()

	// Most recent desired trunk forces and torques in Vicon frame
	private Vector3 mostRecentTrunkDesiredForcesViconFrame;
	private Vector3 mostRecentTrunkDesiredTorquesViconFrame;
	private Vector3 mostRecentTrunkDesiredForcesViveFrame;
	private Vector3 mostRecentTrunkDesiredTorquesViveFrame;

	// Most recent desired pelvis forces and torques in Vicon frame
	private Vector3 mostRecentPelvisDesiredForcesViconFrame;
	private Vector3 mostRecentPelvisDesiredTorquesViconFrame;
	private Vector3 mostRecentPelvisDesiredForcesViveFrame;
	private Vector3 mostRecentPelvisDesiredTorquesViveFrame;
	
	// Most recent desired pelvis forces and torques in frame 0
	private Vector3 mostRecentPelvisDesiredForcesFrame0;
	private Vector3 mostRecentPelvisDesiredTorquesFrame0;

	private Vector3 forceOnPelvisFromCable;
	// Most recent trunk structure matrices (computed on a per-Vicon-frame basis)
	private Vector3[] columnsOfForceTrunkStructureMatrix;
	private Vector3[] columnsOfTorqueTrunkStructureMatrix;
	private double[] structureMatrixTrunkAsDoubleArray;

	// Most recent pelvis structure matrices (computed on a per-Vicon-frame basis)
	private Vector3[] columnsOfForcePelvisStructureMatrix;
	private Vector3[] columnsOfTorquePelvisStructureMatrix;
	private double[] structureMatrixPelvisAsDoubleArray;

	// Alglib - quadratic programmer/optimizer quantities
	private alglib.minqpstate stateSolverObject; // the "state" or solver object

	// The most recent cable tensions output by the solver, per belt
	double[] mostRecentTrunkCableSolutionFromSolver;
	double[] mostRecentPelvisCableSolutionFromSolver;
	public Vector3 attachPointLeftFront;


	// Start is called before the first frame update
	void Start()
    {


	}

    // Update is called once per frame
    void Update()
    {
        
    }




	public float[] ComputeCableTensionsForDesiredTrunkForces(Vector3 desiredForcesViconFrame,
	Vector3 desiredTorquesViconFrame, Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix, 
	int[] indicesOfStructureMatrixControlledRows, float minimumCableTension, float[] bestGuessInitialCableTensions)
	{
		//Debug.Log("Computing needed cable tensions");

		// First, store the new columns of the structure matrix in instance variables.
		mostRecentTrunkDesiredForcesViconFrame = desiredForcesViconFrame;
		mostRecentTrunkDesiredTorquesViconFrame = desiredTorquesViconFrame;
		columnsOfForceTrunkStructureMatrix = columnsOfForceSMatrix;
		columnsOfTorqueTrunkStructureMatrix = columnsOfTorqueSMatrix;

		// For clarity, retrieve the number of cables
		int numCables = columnsOfForceSMatrix.Length;

		// Convert the controlled rows of the structure matrix to a double array format
		double[,] structureMatrixTrunkRowsOfInterestAsDoubleArray = new double[indicesOfStructureMatrixControlledRows.Length,
			columnsOfForceSMatrix.Length];

		// Retrieve the desired rows of the structure matrix as double arrays, i.e. double[]
		List<double[]> allRowsOfInterest = new List<double[]>();
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
        {
			// Row of interest
			int rowOfInterestIndex = indicesOfStructureMatrixControlledRows[controlledRowIndex];
			// Get the row given the row index. 
			// Note, row index 1 means Fx, 2 means Fy, 3 means Fz, 4 means Tx, 5 means Ty, 6 means Tz
			double[] rowOfInterest = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrix, columnsOfTorqueSMatrix, rowOfInterestIndex);
			// Store 
			allRowsOfInterest.Add(rowOfInterest);
		}

		// Fill the 2D array representing the S matrix rows of interest
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
        {
			double[] rowOfInterest = allRowsOfInterest[controlledRowIndex];
			for (uint columnIndex = 0; columnIndex < columnsOfForceSMatrix.Length; columnIndex++)
            {
				structureMatrixTrunkRowsOfInterestAsDoubleArray[controlledRowIndex, columnIndex] = rowOfInterest[columnIndex];
			}
		}

		// START: quadratic programmer equation setup and solution**********************************************************************************
		// Get a (mxm) diagonal matrix with each diagonal element set to 2, where m is the # of cables. This is our quadratic term.
		double diagonalValue = 2.0f;
		double[,] quadraticTermMatrix = GetDiagonalMatrixWithValueOnDiagonal(numCables, diagonalValue);

		// Get the controlled row force/torque values, which are equal to the equality constraints
		double[] equalityConstraintForceTorqueValues = 
			GetEqualityConstraintForceTorqueValuesForControlledRows(desiredForcesViconFrame, desiredTorquesViconFrame, 
			indicesOfStructureMatrixControlledRows);

		// Declare the quadratic term, linear term, initial guess, bounds, and scale of the quadratic solver 
		double[] bLinearTerm = new double[numCables];
		double[] initialGuessTensions = new double[numCables];
		double[] lowerTensionBoundariesPerCable = new double[numCables];
		double[] upperTensionBoundariesPerCable = new double[numCables];
		double[] scaleOfEachTension = new double[numCables]; // the scale gives the solver an order of magnitude for the expected cable tension.
		// Set a very high upper bound for the cable tensions. We will manually scale tensions to 
		// limit tensions after the quadratic optimizer step.
		double maxTensionInCable = 1000; // [Newtons]
		// Set the expected scale/order of magnitude for the cable tension solutions. 
		// Our minimum tension is 15 N, and our maximum tension should be less than 100.
		// So, let's use a scale of 50.
		double cableTensionScaleInNewtons = 50; 

		// Set the "scale" of the tensions. 

		// Fill the linear terms, bounds, and initial guess at cable tensions
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
        {
			// Set the linear term equal to the negative of the last known tension values. 
			// This allows us to create a function that minimizes tensions and difference from previous tensions. 
			// See note at top of function.
			bLinearTerm[cableIndex] = (double) -bestGuessInitialCableTensions[cableIndex];
			// Use the passed-in best guess
			initialGuessTensions[cableIndex] = (double)bestGuessInitialCableTensions[cableIndex];
			// Set the lower bounds on cable tensions equal to the minimum cable tension
			lowerTensionBoundariesPerCable[cableIndex] = (double)minimumCableTension;
			upperTensionBoundariesPerCable[cableIndex] = maxTensionInCable;
			// Set the scale. Our minimum tension is 15 N, and our maximum tension should be less than 100. So, 
			// let's use a scale of 50.
			scaleOfEachTension[cableIndex] = cableTensionScaleInNewtons;

		}

		// Now, create the solver 
		// Assign the solver to the instance variable
		alglib.minqpcreate(4, out stateSolverObject);
		// Set quadratic term
		alglib.minqpsetquadraticterm(stateSolverObject, quadraticTermMatrix);
		// Set linear tearm
		alglib.minqpsetlinearterm(stateSolverObject, bLinearTerm);
		// Set equality constraints. By specifying upper and lower bounds that are equal, we create equality constraints. 
		// (See documentation for alglib.minqpsetlc2dense)
		alglib.minqpsetlc2dense(stateSolverObject, structureMatrixTrunkRowsOfInterestAsDoubleArray,
			equalityConstraintForceTorqueValues, equalityConstraintForceTorqueValues);
		// Set initial tensions guess
		alglib.minqpsetstartingpoint(stateSolverObject, initialGuessTensions);
		// Set lower and upper bounds for each cable tension
		alglib.minqpsetbc(stateSolverObject, lowerTensionBoundariesPerCable, upperTensionBoundariesPerCable);
		// Set expected scale for the solutions
		alglib.minqpsetscale(stateSolverObject, scaleOfEachTension);

		// Choose the mode as BLEIC-based QP solver.
		alglib.minqpsetalgobleic(stateSolverObject, 0.0, 0.0, 0.0, 0);
		// Solve
		alglib.minqpoptimize(stateSolverObject);
		// Save the tensions and the reporter object
		alglib.minqpreport quadraticSolverReporterObject;
		alglib.minqpresults(stateSolverObject, out mostRecentTrunkCableSolutionFromSolver, out quadraticSolverReporterObject);

		// END: quadratic programmer equation setup and solution**********************************************************************************


		// Convert to float[] for return (my preference is working with floats)
		float[] computedCableTensions = new float[numCables];
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
        {
			computedCableTensions[cableIndex] = (float) mostRecentTrunkCableSolutionFromSolver[cableIndex];
		}
		//Debug.Log("Computed cable tensions by QP solver are:" + computedCableTensions);
		return computedCableTensions;
	}

	public float[] ComputeCableTensionsForDesiredTrunkForcesVive(Vector3 desiredForcesFrame0,
	Vector3 desiredTorquesViveFrame, Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix, 
	int[] indicesOfStructureMatrixControlledRows, float minimumCableTension, float[] bestGuessInitialCableTensions)
	{
		//Debug.Log("Computing needed cable tensions");

		// First, store the new columns of the structure matrix in instance variables.
		mostRecentTrunkDesiredForcesViveFrame = desiredForcesFrame0;
		mostRecentTrunkDesiredTorquesViveFrame = desiredTorquesViveFrame;
		columnsOfForceTrunkStructureMatrix = columnsOfForceSMatrix;
		columnsOfTorqueTrunkStructureMatrix = columnsOfTorqueSMatrix;

		// If the best guess for initial cable tensions (typically, the previous solution) contains NAN, 
		// replace all values with the minimum cable tension.
		bool containsNanFlag = false;
		// First check for any NANs
		for (int cableIndex = 0; cableIndex < bestGuessInitialCableTensions.Length; cableIndex++)
		{
			if (float.IsNaN(bestGuessInitialCableTensions[cableIndex]))
			{
				containsNanFlag = true;
			}
		}
		// If NANs were present, replace with tension minimum
		if (containsNanFlag == true)
		{
			for (int cableIndex = 0; cableIndex < bestGuessInitialCableTensions.Length; cableIndex++)
			{
				bestGuessInitialCableTensions[cableIndex] = minimumCableTension;
			}
		}

		// For clarity, retrieve the number of cables
		int numCables = columnsOfForceSMatrix.Length;

		// Convert the controlled rows of the structure matrix to a double array format
		double[,] structureMatrixTrunkRowsOfInterestAsDoubleArray = new double[indicesOfStructureMatrixControlledRows.Length,
			columnsOfForceSMatrix.Length];

		// Retrieve the desired rows of the structure matrix as double arrays, i.e. double[]
		List<double[]> allRowsOfInterest = new List<double[]>();
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
        {
			// Row of interest
			int rowOfInterestIndex = indicesOfStructureMatrixControlledRows[controlledRowIndex];
			// Get the row given the row index. 
			// Note, row index 1 means Fx, 2 means Fy, 3 means Fz, 4 means Tx, 5 means Ty, 6 means Tz
			double[] rowOfInterest = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrix, columnsOfTorqueSMatrix, rowOfInterestIndex);
			// Store 
			allRowsOfInterest.Add(rowOfInterest);
		}

		// Fill the 2D array representing the S matrix rows of interest
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
        {
			double[] rowOfInterest = allRowsOfInterest[controlledRowIndex];
			for (uint columnIndex = 0; columnIndex < columnsOfForceSMatrix.Length; columnIndex++)
            {
				structureMatrixTrunkRowsOfInterestAsDoubleArray[controlledRowIndex, columnIndex] = rowOfInterest[columnIndex];
			}
		}

		// START: quadratic programmer equation setup and solution**********************************************************************************
		// Get a (mxm) diagonal matrix with each diagonal element set to 2, where m is the # of cables. This is our quadratic term.
		double diagonalValue = 2.0f;
		double[,] quadraticTermMatrix = GetDiagonalMatrixWithValueOnDiagonal(numCables, diagonalValue);

		// Get the controlled row force/torque values, which are equal to the equality constraints
		double[] equalityConstraintForceTorqueValues = 
			GetEqualityConstraintForceTorqueValuesForControlledRows(desiredForcesFrame0, desiredTorquesViveFrame, 
			indicesOfStructureMatrixControlledRows);

		// Declare the quadratic term, linear term, initial guess, bounds, and scale of the quadratic solver 
		double[] bLinearTerm = new double[numCables];
		double[] initialGuessTensions = new double[numCables];
		double[] lowerTensionBoundariesPerCable = new double[numCables];
		double[] upperTensionBoundariesPerCable = new double[numCables];
		double[] scaleOfEachTension = new double[numCables]; // the scale gives the solver an order of magnitude for the expected cable tension.
		// Set a very high upper bound for the cable tensions. We will manually scale tensions to 
		// limit tensions after the quadratic optimizer step.
		double maxTensionInCable = 1000; // [Newtons]
		// Set the expected scale/order of magnitude for the cable tension solutions. 
		// Our minimum tension is 15 N, and our maximum tension should be less than 100.
		// So, let's use a scale of 50.
		double cableTensionScaleInNewtons = 50; 

		// Set the "scale" of the tensions. 

		// Fill the linear terms, bounds, and initial guess at cable tensions
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
        {
			// Set the linear term equal to the negative of the last known tension values. 
			// This allows us to create a function that minimizes tensions and difference from previous tensions. 
			// See note at top of function.
			bLinearTerm[cableIndex] = (double) -bestGuessInitialCableTensions[cableIndex];
			// Use the passed-in best guess
			initialGuessTensions[cableIndex] = (double)bestGuessInitialCableTensions[cableIndex];
			// Set the lower bounds on cable tensions equal to the minimum cable tension
			lowerTensionBoundariesPerCable[cableIndex] = (double)minimumCableTension;
			upperTensionBoundariesPerCable[cableIndex] = maxTensionInCable;
			// Set the scale. Our minimum tension is 15 N, and our maximum tension should be less than 100. So, 
			// let's use a scale of 50.
			scaleOfEachTension[cableIndex] = cableTensionScaleInNewtons;

		}

		// Now, create the solver 
		// Assign the solver to the instance variable
		alglib.minqpcreate(4, out stateSolverObject);
		// Set quadratic term
		alglib.minqpsetquadraticterm(stateSolverObject, quadraticTermMatrix);
		// Set linear tearm
		alglib.minqpsetlinearterm(stateSolverObject, bLinearTerm);
		// Set equality constraints. By specifying upper and lower bounds that are equal, we create equality constraints. 
		// (See documentation for alglib.minqpsetlc2dense)
		alglib.minqpsetlc2dense(stateSolverObject, structureMatrixTrunkRowsOfInterestAsDoubleArray,
			equalityConstraintForceTorqueValues, equalityConstraintForceTorqueValues);
		// Set initial tensions guess
		alglib.minqpsetstartingpoint(stateSolverObject, initialGuessTensions);
		// Set lower and upper bounds for each cable tension
		alglib.minqpsetbc(stateSolverObject, lowerTensionBoundariesPerCable, upperTensionBoundariesPerCable);
		// Set expected scale for the solutions
		alglib.minqpsetscale(stateSolverObject, scaleOfEachTension);

		// Choose the mode as BLEIC-based QP solver.
		alglib.minqpsetalgobleic(stateSolverObject, 0.0, 0.0, 0.0, 0);
		// Solve
		alglib.minqpoptimize(stateSolverObject);
		// Save the tensions and the reporter object
		alglib.minqpreport quadraticSolverReporterObject;
		alglib.minqpresults(stateSolverObject, out mostRecentTrunkCableSolutionFromSolver, out quadraticSolverReporterObject);

		// END: quadratic programmer equation setup and solution**********************************************************************************


		// Convert to float[] for return (my preference is working with floats)
		float[] computedCableTensions = new float[numCables];
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
        {
			computedCableTensions[cableIndex] = (float) mostRecentTrunkCableSolutionFromSolver[cableIndex];
		}

		// Convert to float[] for return (my preference is working with floats)

		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
		{
			DebugLogIfLevelManagerDebugIsSet("Cable tension planner: Computed chest cable tensions for chest force: (" + desiredForcesFrame0.x + ", " + desiredForcesFrame0.y
				+ ", " + desiredForcesFrame0.z + "), tension computed by QP solver for cable " + cableIndex + " is:" + computedCableTensions[cableIndex]);

		}

		//Debug.Log("Computed cable tensions by QP solver are:" + computedCableTensions);
		return computedCableTensions;
	}

	public float[] ComputeCableTensionsForDesiredPelvisForces(Vector3 desiredForcesViconFrame,
Vector3 desiredTorquesViconFrame, Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix,
int[] indicesOfStructureMatrixControlledRows, float minimumCableTension, float[] bestGuessInitialCableTensions)
	{
		//Debug.Log("Computing needed cable tensions");

		// First, store the new columns of the structure matrix in instance variables.
		mostRecentPelvisDesiredForcesViconFrame = desiredForcesViconFrame;
		mostRecentPelvisDesiredTorquesViconFrame = desiredTorquesViconFrame;
		columnsOfForcePelvisStructureMatrix = columnsOfForceSMatrix;
		columnsOfTorquePelvisStructureMatrix = columnsOfTorqueSMatrix;

		// For clarity, retrieve the number of cables
		int numCables = columnsOfForceSMatrix.Length;
		DebugLogIfLevelManagerDebugIsSet("Test the Matrix of TensionForPelvisForce "+columnsOfForceSMatrix);
		// Retrieve the desired rows of the structure matrix as double arrays, i.e. double[]
		List<double[]> allRowsOfInterest = new List<double[]>();
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
		{
			// Row of interest
			int rowOfInterestIndex = indicesOfStructureMatrixControlledRows[controlledRowIndex];
			// Get the row given the row index. 
			// Note, row index 1 means Fx, 2 means Fy, 3 means Fz, 4 means Tx, 5 means Ty, 6 means Tz
			double[] rowOfInterest = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrix, columnsOfTorqueSMatrix, rowOfInterestIndex);
			// Store 
			allRowsOfInterest.Add(rowOfInterest);
		}

		// Convert the controlled rows of the structure matrix to a double array format
		double[,] structureMatrixPelvisRowsOfInterestAsDoubleArray = AddArraysInListAsRowsOf2DArrayFormat(allRowsOfInterest);

		/*double[,] structureMatrixPelvisRowsOfInterestAsDoubleArray = new double[indicesOfStructureMatrixControlledRows.Length,
			columnsOfForceSMatrix.Length];
		// Fill the 2D array representing the S matrix rows of interest
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
		{
			double[] rowOfInterest = allRowsOfInterest[controlledRowIndex];
			for (uint columnIndex = 0; columnIndex < columnsOfForceSMatrix.Length; columnIndex++)
			{
				structureMatrixPelvisRowsOfInterestAsDoubleArray[controlledRowIndex, columnIndex] = rowOfInterest[columnIndex];
			}
		}*/

		// START: quadratic programmer equation setup and solution**********************************************************************************
		// Get a (mxm) diagonal matrix with each diagonal element set to 2, where m is the # of cables. This is our quadratic term.
		double diagonalValue = 2.0f;
		double[,] quadraticTermMatrix = GetDiagonalMatrixWithValueOnDiagonal(numCables, diagonalValue);

		// Get the controlled row force/torque values, which are equal to the equality constraints
		double[] equalityConstraintForceTorqueValues =
			GetEqualityConstraintForceTorqueValuesForControlledRows(desiredForcesViconFrame, desiredTorquesViconFrame,
			indicesOfStructureMatrixControlledRows);

		// Declare the quadratic term, linear term, initial guess, bounds, and scale of the quadratic solver 
		double[] bLinearTerm = new double[numCables];
		double[] initialGuessTensions = new double[numCables];
		double[] lowerTensionBoundariesPerCable = new double[numCables];
		double[] upperTensionBoundariesPerCable = new double[numCables];
		double[] scaleOfEachTension = new double[numCables]; // the scale gives the solver an order of magnitude for the expected cable tension.
															 // Set a very high upper bound for the cable tensions. We will manually scale tensions to 
															 // limit tensions after the quadratic optimizer step.
		double maxTensionInCable = 1000; // [Newtons]
										 // Set the expected scale/order of magnitude for the cable tension solutions. 
										 // Our minimum tension is 15 N, and our maximum tension should be less than 100.
										 // So, let's use a scale of 50.
		double cableTensionScaleInNewtons = 50;

		// Set the "scale" of the tensions. 

		// Fill the linear terms, bounds, and initial guess at cable tensions
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
		{
			// Set the linear term equal to the negative of the last known tension values. 
			// This allows us to create a function that minimizes tensions and difference from previous tensions. 
			// See note at top of function.
			bLinearTerm[cableIndex] = (double)-bestGuessInitialCableTensions[cableIndex];
			// Use the passed-in best guess
			initialGuessTensions[cableIndex] = (double)bestGuessInitialCableTensions[cableIndex];
			// Set the lower bounds on cable tensions equal to the minimum cable tension
			lowerTensionBoundariesPerCable[cableIndex] = (double)minimumCableTension;
			upperTensionBoundariesPerCable[cableIndex] = maxTensionInCable;
			// Set the scale. Our minimum tension is 15 N, and our maximum tension should be less than 100. So, 
			// let's use a scale of 50.
			scaleOfEachTension[cableIndex] = cableTensionScaleInNewtons;

		}


		// Set linear tearm
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: blinearterm: (" + bLinearTerm[0] + ", " + bLinearTerm[1] + ", " + bLinearTerm[2] + ")");
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: quadratic term matrix diags: " + quadraticTermMatrix[0, 0] + ", " + quadraticTermMatrix[1, 1] + ", " + quadraticTermMatrix[2, 2]);
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: equalityConstraintForceTorqueValues " + equalityConstraintForceTorqueValues[0]
			+ equalityConstraintForceTorqueValues[1]
			+ equalityConstraintForceTorqueValues[2]);
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: desired pelvic forces are (Fx, Fy, Fz): (" + desiredForcesViconFrame.x + ", " + desiredForcesViconFrame.y + ", " +
			desiredForcesViconFrame.z + ")");
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: desired pelvic torques are (Tx, Ty, Tz): (" + desiredTorquesViconFrame.x + ", " + desiredTorquesViconFrame.y + ", " +
			desiredTorquesViconFrame.z + ")");

		for (int structureColIndex = 0; structureColIndex < columnsOfForceSMatrix.Length; structureColIndex++)
		{
			Vector3 structureMatrixCol = columnsOfForceSMatrix[structureColIndex];
			DebugLogIfLevelManagerDebugIsSet("Cable tension planner: structure matrix column " + structureColIndex + " has elements: (" + structureMatrixCol.x + ", " + structureMatrixCol.y + ", " +
				structureMatrixCol.z + ")");
		}

		// To first set equality constraint only on Fy, we have to modify the Jacobian and the equality constraint vector
		// to be all zero except on the Fy axis.
		List<double[]> zerosMatrixWithFyRowOfSMatrix = new List<double[]>();
		// Create zeros array (a row)
		double[] zerosArrayForStructureMatrix = new double[numCables];
		for(int elementIndex = 0; elementIndex < numCables; elementIndex++)
        {
			zerosArrayForStructureMatrix[elementIndex] = 0.0;
		}
		// Create the Fy-only matrix for the equality constraints
		zerosMatrixWithFyRowOfSMatrix.Add(zerosArrayForStructureMatrix);
		double[] fyRowOfStructureMatrix = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrix, columnsOfTorqueSMatrix, 2);
		zerosMatrixWithFyRowOfSMatrix.Add(fyRowOfStructureMatrix);
		zerosMatrixWithFyRowOfSMatrix.Add(zerosArrayForStructureMatrix);
		double[,] zerosMatrixWithFyRowOfSMatrixDoubleArray = AddArraysInListAsRowsOf2DArrayFormat(zerosMatrixWithFyRowOfSMatrix);

		// Create the equality constraints vector, with zeros except for the Fy value
		double[] equalityConstraintsFyOnly = new double[] { 0.0f, desiredForcesViconFrame.y, 0.0f};

		// Solve for tensions that satisfy the desired Fy
		int problemSizeNumCables = 4;
		(double[] minimizedTensionsToAchieveFy, _) = SolveQuadProgrammingProblem(problemSizeNumCables, quadraticTermMatrix, bLinearTerm,
			zerosMatrixWithFyRowOfSMatrixDoubleArray,
			equalityConstraintsFyOnly, initialGuessTensions, lowerTensionBoundariesPerCable, upperTensionBoundariesPerCable,
			scaleOfEachTension);

		// Get the Fz row of the structure matrix
		double[] fzRowOfStructureMatrix = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrix, columnsOfTorqueSMatrix, 3);

		// Using the minimized tensions that satisfied Fy, compute the minimum Fz by multiplying these tensions
		// by the structure matrix Fz row. 
		// This is the same as element-wise multiplication followead by summing all elements.
		float minimumFz = 0.0f;
		for (int elementIndex = 0; elementIndex < fzRowOfStructureMatrix.Length; elementIndex++)
		{
			DebugLogIfLevelManagerDebugIsSet("Minimum Fz computation, element " + elementIndex + ": fz row element " + fzRowOfStructureMatrix[elementIndex] + " times tension " + minimizedTensionsToAchieveFy[elementIndex]);
			minimumFz = minimumFz + (float) (fzRowOfStructureMatrix[elementIndex] * minimizedTensionsToAchieveFy[elementIndex]);
		}
		DebugLogIfLevelManagerDebugIsSet("Minimum Fz is: " + minimumFz);

		// If the desired Fz is less than the minimum, adjust the Fz to be the minimum Fz
		float minimumFzBufferInNewtons = 5.0f; // Newtons. We add a small buffer to the minimum Fz to make the equations numerically solvable.
		Vector3 originalDesiredForcesViconFrame = desiredForcesViconFrame;
		if (desiredForcesViconFrame[2] < minimumFz + minimumFzBufferInNewtons)
        {
			DebugLogIfLevelManagerDebugIsSet("Desired Fz, " + desiredForcesViconFrame[2] +
				", was smaller than minimum Fz of " + minimumFz + ". Increasing Fz to " + minimumFz + minimumFzBufferInNewtons);
			desiredForcesViconFrame[2] = minimumFz + minimumFzBufferInNewtons;

			// Recompute the equality constraints after replacing Fz 
			equalityConstraintForceTorqueValues =
				GetEqualityConstraintForceTorqueValuesForControlledRows(desiredForcesViconFrame, desiredTorquesViconFrame,
				indicesOfStructureMatrixControlledRows);
		}

		// Last, solve for the minimized tensions that both satisfy Fy and Fz (either Fz desired or Fz,min)
		alglib.minqpreport solverReport;
		(mostRecentPelvisCableSolutionFromSolver, solverReport) = SolveQuadProgrammingProblem(problemSizeNumCables, quadraticTermMatrix, bLinearTerm,
			structureMatrixPelvisRowsOfInterestAsDoubleArray,
			equalityConstraintForceTorqueValues, initialGuessTensions, lowerTensionBoundariesPerCable, upperTensionBoundariesPerCable,
			scaleOfEachTension);

		// END: quadratic programmer equation setup and solution**********************************************************************************


		// Convert to float[] for return (my preference is working with floats)
		float[] computedCableTensions = new float[numCables];
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
		{
			computedCableTensions[cableIndex] = (float)mostRecentPelvisCableSolutionFromSolver[cableIndex];
			DebugLogIfLevelManagerDebugIsSet("Computed pelvic cable tensions for initial desired pelvic force: (" + originalDesiredForcesViconFrame.x + ", " + originalDesiredForcesViconFrame.y
				+ ", " + originalDesiredForcesViconFrame.z + ") and modified pelvic force: (" + desiredForcesViconFrame.x + ", " + desiredForcesViconFrame.y 
				+ ", " + desiredForcesViconFrame .z + ") by QP solver for cable " + cableIndex + " are:" + computedCableTensions[cableIndex]);

		}
		return computedCableTensions;
	}


public (float[], Vector3) ComputeCableTensionsForDesiredPelvisForcesInFrame0(Vector3 desiredForcesFrame0,
Vector3 desiredTorquesFrame0, Vector3[] columnsOfForceSMatrix, Vector3[] columnsOfTorqueSMatrix,
int[] indicesOfStructureMatrixControlledRows, float minimumCableTension, float[] bestGuessInitialCableTensions)
	{
		//DebugLogIfLevelManagerDebugIsSet("Computing needed cable tensions");
		
		// Set desired force in z equal to 0 FOR SQUATTING TASK ONLY!!!
		//desiredForcesFrame0.z = 0.0f;
		
		// If the best guess for initial cable tensions (typically, the previous solution) contains NAN, 
		// replace all values with the minimum cable tension.
		bool containsNanFlag = false;
		// First check for any NANs
		for (int cableIndex = 0; cableIndex < bestGuessInitialCableTensions.Length; cableIndex++)
		{
			if (float.IsNaN(bestGuessInitialCableTensions[cableIndex]))
			{
				containsNanFlag = true;
			}
		}
		// If NANs were present, replace with tension minimum
		if (containsNanFlag == true)
		{
			for (int cableIndex = 0; cableIndex < bestGuessInitialCableTensions.Length; cableIndex++)
			{
				bestGuessInitialCableTensions[cableIndex] = minimumCableTension;
			}
		}

		// First, store the new columns of the structure matrix in instance variables.
		mostRecentPelvisDesiredForcesFrame0 = desiredForcesFrame0;
		mostRecentPelvisDesiredTorquesFrame0 = desiredTorquesFrame0;
		columnsOfForcePelvisStructureMatrix = columnsOfForceSMatrix;
		columnsOfTorquePelvisStructureMatrix = columnsOfTorqueSMatrix;
		// For clarity, retrieve the number of cables
		int numCables = columnsOfForceSMatrix.Length;

		// Retrieve the desired rows of the structure matrix as double arrays, i.e. double[]
		List<double[]> allRowsOfInterest = new List<double[]>();
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
		{
			// Row of interest
			int rowOfInterestIndex = indicesOfStructureMatrixControlledRows[controlledRowIndex];
			// Get the row given the row index. 
			// Note, row index 1 means Fx, 2 means Fy, 3 means Fz, 4 means Tx, 5 means Ty, 6 means Tz
			double[] rowOfInterest = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrix, columnsOfTorqueSMatrix, rowOfInterestIndex);
			// Store 
			allRowsOfInterest.Add(rowOfInterest);
		}

		// Convert the controlled rows of the structure matrix to a double array format
		double[,] structureMatrixPelvisRowsOfInterestAsDoubleArray = AddArraysInListAsRowsOf2DArrayFormat(allRowsOfInterest);

		/*double[,] structureMatrixPelvisRowsOfInterestAsDoubleArray = new double[indicesOfStructureMatrixControlledRows.Length,
			columnsOfForceSMatrix.Length];
		// Fill the 2D array representing the S matrix rows of interest
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
		{
			double[] rowOfInterest = allRowsOfInterest[controlledRowIndex];
			for (uint columnIndex = 0; columnIndex < columnsOfForceSMatrix.Length; columnIndex++)
			{
				structureMatrixPelvisRowsOfInterestAsDoubleArray[controlledRowIndex, columnIndex] = rowOfInterest[columnIndex];
			}
		}*/

		// START: quadratic programmer equation setup and solution**********************************************************************************
		// Get a (mxm) diagonal matrix with each diagonal element set to 2, where m is the # of cables. This is our quadratic term.
		double diagonalValue = 2.0f;
		double[,] quadraticTermMatrix = GetDiagonalMatrixWithValueOnDiagonal(numCables, diagonalValue);

		// Get the controlled row force/torque values, which are equal to the equality constraints

		double[] equalityConstraintForceTorqueValues =
			GetEqualityConstraintForceTorqueValuesForControlledRows(desiredForcesFrame0, desiredTorquesFrame0,
			indicesOfStructureMatrixControlledRows);

		// Declare the quadratic term, linear term, initial guess, bounds, and scale of the quadratic solver 
		double[] bLinearTerm = new double[numCables];
		double[] initialGuessTensions = new double[numCables];
		double[] lowerTensionBoundariesPerCable = new double[numCables];
		double[] upperTensionBoundariesPerCable = new double[numCables];
		double[] scaleOfEachTension = new double[numCables]; // the scale gives the solver an order of magnitude for the expected cable tension.
															 // Set a very high upper bound for the cable tensions. We will manually scale tensions to 
															 // limit tensions after the quadratic optimizer step.
		double maxTensionInCable = 1000; // [Newtons]
										 // Set the expected scale/order of magnitude for the cable tension solutions. 
										 // Our minimum tension is 15 N, and our maximum tension should be less than 100.
										 // So, let's use a scale of 50.
		double cableTensionScaleInNewtons = 50;

		// Set the "scale" of the tensions. 

		// Fill the linear terms, bounds, and initial guess at cable tensions
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
		{
			// Set the linear term equal to the negative of the last known tension values. 
			// This allows us to create a function that minimizes tensions and difference from previous tensions. 
			// See note at top of function.
			bLinearTerm[cableIndex] = (double)-bestGuessInitialCableTensions[cableIndex];
			// Use the passed-in best guess
			initialGuessTensions[cableIndex] = (double)bestGuessInitialCableTensions[cableIndex];
			// Set the lower bounds on cable tensions equal to the minimum cable tension
			lowerTensionBoundariesPerCable[cableIndex] = (double)minimumCableTension;
			upperTensionBoundariesPerCable[cableIndex] = maxTensionInCable;
			// Set the scale. Our minimum tension is 15 N, and our maximum tension should be less than 100 (or so). So, 
			// let's use a scale of 50.
			scaleOfEachTension[cableIndex] = cableTensionScaleInNewtons;

		}


		// Set linear tearm
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: blinearterm: (" + bLinearTerm[0] + ", " + bLinearTerm[1] + ", " + bLinearTerm[2] + ")");
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: quadratic term matrix diags: " + quadraticTermMatrix[0, 0] + ", " + quadraticTermMatrix[1, 1] + ", " + quadraticTermMatrix[2, 2]);
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: equalityConstraintForceTorqueValues " + equalityConstraintForceTorqueValues[0]
			+ equalityConstraintForceTorqueValues[1]
			+ equalityConstraintForceTorqueValues[2]);
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: desired pelvic forces are (Fx, Fy, Fz): (" + desiredForcesFrame0.x + ", " + desiredForcesFrame0.y + ", " +
			desiredForcesFrame0.z + ")");
		DebugLogIfLevelManagerDebugIsSet("Cable tension planner: desired pelvic torques are (Tx, Ty, Tz): (" + desiredTorquesFrame0.x + ", " + desiredTorquesFrame0.y + ", " +
			desiredTorquesFrame0.z + ")");

		for (int structureColIndex = 0; structureColIndex < columnsOfForceSMatrix.Length; structureColIndex++)
		{
			Vector3 structureMatrixCol = columnsOfForceSMatrix[structureColIndex];
			DebugLogIfLevelManagerDebugIsSet("Cable tension planner: structure matrix column " + structureColIndex + " has elements: (" + structureMatrixCol.x + ", " + structureMatrixCol.y + ", " +
				structureMatrixCol.z + ")");
		}


		// Last, solve for the minimized tensions that both satisfy Fy and Fx (either Fx desired or Fx,min)
		int problemSizeNumCables = initialGuessTensions.Length; // num. cables is number of cable tensions passed in
		alglib.minqpreport solverReport;
		(mostRecentPelvisCableSolutionFromSolver, solverReport) = SolveQuadProgrammingProblem(problemSizeNumCables, quadraticTermMatrix, bLinearTerm,
			structureMatrixPelvisRowsOfInterestAsDoubleArray,
			equalityConstraintForceTorqueValues, initialGuessTensions, lowerTensionBoundariesPerCable, upperTensionBoundariesPerCable,
			scaleOfEachTension);

		// END: quadratic programmer equation setup and solution**********************************************************************************

		// Convert to float[] for return (my preference is working with floats)
		float[] computedCableTensions = new float[numCables];
		for (int cableIndex = 0; cableIndex < numCables; cableIndex++)
		{
			computedCableTensions[cableIndex] = (float)mostRecentPelvisCableSolutionFromSolver[cableIndex];
			DebugLogIfLevelManagerDebugIsSet("Cable tension planner: Computed pelvic cable tensions for pelvic force: (" + desiredForcesFrame0.x + ", " + desiredForcesFrame0.y 
				+ ", " + desiredForcesFrame0.z + "), tension computed by QP solver for cable " + cableIndex + " is:" + computedCableTensions[cableIndex]);

		}
		
		// Return the computed cable tensions AND the possibly modified desired forces in frame 0
		return (computedCableTensions, desiredForcesFrame0);
	}

	public float[] ComputeCableTensionForDesiredLeftKneeForceInFrame0()
	{
		float[] computedCableTensionsOnLeftKnee = new float[] { };
		return computedCableTensionsOnLeftKnee;
	}
	public float[] ComputeCableTensionForDesiredRightKneeForceInFrame0()
	{
		float[] computedCableTensionsOnRightKnee = new float[] { };
		return computedCableTensionsOnRightKnee;
	}
	private double[,] AddArraysInListAsRowsOf2DArrayFormat(List<double[]> listOfArrays)
    {
		// Instantiate return 2D array
		double[,] twoDArray = new double[listOfArrays.Count,
				listOfArrays[0].Length];
		// Fill the 2D array representing the S matrix rows of interest
		for (int rowIndex = 0; rowIndex < listOfArrays.Count; rowIndex++)
		{
			double[] currentRow = listOfArrays[rowIndex];
			for (uint columnIndex = 0; columnIndex < listOfArrays[0].Length; columnIndex++)
			{
				twoDArray[rowIndex, columnIndex] = currentRow[columnIndex];
			}
		}

		// Return the 2D array which has each double[] in the list as a row.
		return twoDArray;
}


	private (double[], alglib.minqpreport) SolveQuadProgrammingProblem(int problemSizeNumCables, double [,] quadraticTermMatrix, double[] bLinearTerm, 
			double[,] structureMatrixPelvisRowsOfInterestAsDoubleArray,
			double[] equalityConstraintForceTorqueValues, double[] initialGuessTensions, double[] lowerTensionBoundariesPerCable,
			double[] upperTensionBoundariesPerCable, double[] scaleOfEachTension)
    {
		// Now, create the solver 
		// Assign the solver to the instance variable
		alglib.minqpcreate(problemSizeNumCables, out stateSolverObject);
		// Set quadratic term
		alglib.minqpsetquadraticterm(stateSolverObject, quadraticTermMatrix);

		alglib.minqpsetlinearterm(stateSolverObject, bLinearTerm);
		// Set equality constraints. By specifying upper and lower bounds that are equal, we create equality constraints. 
		// (See documentation for alglib.minqpsetlc2dense)

		// Define box constraints
		double[] lowerBoxConstraints = equalityConstraintForceTorqueValues;
		double[] upperBoxConstraints = equalityConstraintForceTorqueValues;
		double tightTorqueConstraintNewtonMeters = 1.0;
		double looseTorqueConstrainNewtonMeters = 10000.0;
		if (enforcePelvicTorqueBoundsControl == EnforcePelvicTorqueBoundsControl.Enforce)
        {
			// Set narrow torque boundaries for Tx, Ty
			// Lower
			lowerBoxConstraints[3] = -tightTorqueConstraintNewtonMeters;
			lowerBoxConstraints[4] = -tightTorqueConstraintNewtonMeters;
			// Upper
			lowerBoxConstraints[3] = tightTorqueConstraintNewtonMeters;
			lowerBoxConstraints[4] = tightTorqueConstraintNewtonMeters;

		}
		// else don't enforce
        else
        {
			// Set narrow torque boundaries for Tx, Ty
			// Lower
			lowerBoxConstraints[3] = -looseTorqueConstrainNewtonMeters;
			lowerBoxConstraints[4] = -looseTorqueConstrainNewtonMeters;
			// Upper
			lowerBoxConstraints[3] = looseTorqueConstrainNewtonMeters;
			lowerBoxConstraints[4] = looseTorqueConstrainNewtonMeters;
		}


		// Set equality and box constraints. 
		// Third argument = Al = lower bound constraints
		// Fourth argument = Au = upper bound constraints
		// Set Al = Au for equality constraint
		alglib.minqpsetlc2dense(stateSolverObject, structureMatrixPelvisRowsOfInterestAsDoubleArray,
			lowerBoxConstraints, upperBoxConstraints);
		// Set initial tensions guess
		alglib.minqpsetstartingpoint(stateSolverObject, initialGuessTensions);
		// Set lower and upper bounds for each cable tension
		alglib.minqpsetbc(stateSolverObject, lowerTensionBoundariesPerCable, upperTensionBoundariesPerCable);
		// Set expected scale for the solutions
		alglib.minqpsetscale(stateSolverObject, scaleOfEachTension);

		// Choose the mode as BLEIC-based QP solver.
		alglib.minqpsetalgobleic(stateSolverObject, 0.0, 0.0, 0.0, 0);
		// Solve
		alglib.minqpoptimize(stateSolverObject);
		// Save the tensions and the reporter object
		alglib.minqpreport quadraticSolverReporterObject;
		double[] cableTensionSolution;
		alglib.minqpresults(stateSolverObject, out cableTensionSolution, out quadraticSolverReporterObject);

		// If the solution contains any NaN values, we do extra debug printing
		bool solutionContainsNan = false;
		for (int elementIndex = 0; elementIndex < cableTensionSolution.Length; elementIndex++)
        {
			bool isElementNan = double.IsNaN(cableTensionSolution[elementIndex]);
			if(isElementNan == true)
            {
				solutionContainsNan = true;
			}
		}

        if (solutionContainsNan)
        {
			DebugLogIfLevelManagerDebugIsSet("Cable tension solver (alglib) produced NaN result. " +
                "blinearterm: (" + bLinearTerm[0] + ", " + bLinearTerm[1] + ", " + bLinearTerm[2] + ")" +
				" quadratic term matrix diags: " + quadraticTermMatrix[0, 0] + ", " + quadraticTermMatrix[1, 1] + ", " + quadraticTermMatrix[2, 2] 
				+ ") equalityConstraintForceTorqueValues (Fx, Fy, Fz): (" + equalityConstraintForceTorqueValues[0] + ", "
				+ equalityConstraintForceTorqueValues[1] + ", " + equalityConstraintForceTorqueValues[2] + ")");

			bool structureMatrixContainsNan = false;


			// Print the "equality matrix" which is just the structure matrix
			// For each column (each column corresponds to a cable)
			for (int structureColIndex = 0; structureColIndex < problemSizeNumCables; structureColIndex++)
			{
				Vector3 structureMatrixCol = new Vector3((float) structureMatrixPelvisRowsOfInterestAsDoubleArray[0,structureColIndex],
					(float) structureMatrixPelvisRowsOfInterestAsDoubleArray[1, structureColIndex], (float) structureMatrixPelvisRowsOfInterestAsDoubleArray[2, structureColIndex]);
				DebugLogIfLevelManagerDebugIsSet("Cable tension planner NaN computation: equality matrix column " + structureColIndex + " has elements: (" + structureMatrixCol.x + ", " + structureMatrixCol.y + ", " +
					structureMatrixCol.z + ")");
			}
        }
        else
        {
			DebugLogIfLevelManagerDebugIsSet("Cable tension solution does NOT contain NaN. Success!");
        }

		// Return the computed tensions and the solver report
		return (cableTensionSolution, quadraticSolverReporterObject);
	}


	// ASSUMES we have one cable per shank and are just controlling one force component (one row of the force structure matrix per belt). 
	// As a result, no solver is needed, just basic algebra. 
	public (float, float) ComputeCableTensionsForDesiredShankForces(Vector3 desiredForcesFrame0,
			Vector3 desiredTorquesFrame0, Vector3[] columnsOfForceSMatrixLeftShank,
			Vector3[] columnsOfTorqueSMatrixLeftShank, Vector3[] columnsOfForceSMatrixRightShank, 
			Vector3[] columnsOfTorqueSMatrixRightShank, int[] indicesOfStructureMatrixControlledRows, float minimumCableTension,
			float bestGuessInitialCableTensionLeftShank, float bestGuessInitialCableTensionRightShank)
    {

		// The desired force in Vicon frame PER CABLE is the desired force in Vicon frame divided by two. 
		// This means we attempt to divide the force equally between the two legs. 
		Vector3 desiredPerCableForceFrame0 = desiredForcesFrame0 / 2.0f;

		// The left shank force is the desired shank force divided by 2 (distributed among both legs) divided by the corresponing row (single element)
		// of the structure matrix
		double[] rowOfInterestLeftShank = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrixLeftShank,
			columnsOfTorqueSMatrixLeftShank, indicesOfStructureMatrixControlledRows[0]);
		float elementOfStructureMatrixLeftShank = (float) rowOfInterestLeftShank[0];
        // Get the controlled row force/torque values for the left shank (the desired forces/torques), which are equal to the equality constraints
		double[] equalityConstraintForceTorqueValues =
			GetEqualityConstraintForceTorqueValuesForControlledRows(desiredPerCableForceFrame0, desiredTorquesFrame0,
			indicesOfStructureMatrixControlledRows);
		// Solve for the needed tension as T = Fd,i / J(i), where i is the row of the structure matrix and the force/torque vector (Fx, Fy, Fz, Tx, Ty, Tz)
		float leftShankCableTension = (float) equalityConstraintForceTorqueValues[0] / elementOfStructureMatrixLeftShank;

		// The right shank force is the desired shank force divided by 2 (distributed among both legs) divided by the corresponing row (single element)
		// of the structure matrix
		double[] rowOfInterestRightShank = GetRowOfInterestFromStructureMatrixColumns(columnsOfForceSMatrixRightShank,
			columnsOfTorqueSMatrixRightShank, indicesOfStructureMatrixControlledRows[0]);
		float elementOfStructureMatrixRightShank = (float)rowOfInterestRightShank[0];
		// Get the controlled row force/torque values for the left shank (the desired forces/torques), which are equal to the equality constraints
		equalityConstraintForceTorqueValues =
			GetEqualityConstraintForceTorqueValuesForControlledRows(desiredPerCableForceFrame0, desiredTorquesFrame0,
			indicesOfStructureMatrixControlledRows);
		// Solve for the needed tension as T = Fd,i / J(i), where i is the row of the Jacobian and the force/torque vector (Fx, Fy, Fz, Tx, Ty, Tz)
		float rightShankCableTension = (float) equalityConstraintForceTorqueValues[0] / elementOfStructureMatrixRightShank;

		// Return the computed shank cable tensions
		return (leftShankCableTension, rightShankCableTension);
	}


	private double[] GetRowOfInterestFromStructureMatrixColumns(Vector3[] columnsOfForceSMatrix,
		Vector3[] columnsOfTorqueSMatrix, int rowOfInterestIndex)
    {
		// Initialize the row of interest, which has length = # of columns of S matrix = # of cables acting on the body segment
		double[] rowOfInterest = new double[columnsOfForceSMatrix.Length];

		// For each column/element in the row of interest
		for (uint columnIndex = 0; columnIndex < columnsOfForceSMatrix.Length; columnIndex++)
        {
			// Fill the element depending on the row of interest
			if (rowOfInterestIndex == 1) // first row = force in global x-axis row
			{
				rowOfInterest[columnIndex] = columnsOfForceSMatrix[columnIndex].x;
			}else if (rowOfInterestIndex == 2) // second row = force in global y-axis row
            {
				rowOfInterest[columnIndex] = columnsOfForceSMatrix[columnIndex].y;
			}
			else if (rowOfInterestIndex == 3) // third row = force in global z-axis row
			{
				rowOfInterest[columnIndex] = columnsOfForceSMatrix[columnIndex].z;
			}
			else if (rowOfInterestIndex == 4) // fourth row = torque about global x-axis row
			{
				rowOfInterest[columnIndex] = columnsOfTorqueSMatrix[columnIndex].x;
			}
			else if (rowOfInterestIndex == 5) // fifth row = torque about global y-axis row
			{
				rowOfInterest[columnIndex] = columnsOfTorqueSMatrix[columnIndex].y;
			}
			else if (rowOfInterestIndex == 6) // sixth row = torque about global z-axis row
			{
				rowOfInterest[columnIndex] = columnsOfTorqueSMatrix[columnIndex].z;
			}
		}

		// Return the row of interest
		return rowOfInterest;
	}


	private double[,] GetDiagonalMatrixWithValueOnDiagonal(int numCables, double diagonalValue)
    {
		// Create empty double[,] of the right size. Note, default values are 0 for the elements.
		double[,] diagonalMatrix = new double[numCables, numCables];

		// For each row 
		for(int rowIndex = 0; rowIndex < numCables; rowIndex++)
        {
			diagonalMatrix[rowIndex, rowIndex] = diagonalValue;

		}

		// Return 
		return diagonalMatrix;

	}


	private double[] GetEqualityConstraintForceTorqueValuesForControlledRows(Vector3 desiredForcesViconFrame,
		Vector3 desiredTorquesViconFrame, int[] indicesOfStructureMatrixControlledRows)
    {
		// Initialize a double[] which will contain the equality constraint force/torque values
		double[] equalityConstraintForceTorqueValuesForControlledRow = new double[indicesOfStructureMatrixControlledRows.Length];

		// For each controlled row
		for (int controlledRowIndex = 0; controlledRowIndex < indicesOfStructureMatrixControlledRows.Length; controlledRowIndex++)
        {
			// Get the row of interest index
			int rowOfInterestIndex = indicesOfStructureMatrixControlledRows[controlledRowIndex];

			// Fill the element depending on the row of interest
			if (rowOfInterestIndex == 1) // first row = force in global x-axis row
			{
				equalityConstraintForceTorqueValuesForControlledRow[controlledRowIndex] = desiredForcesViconFrame.x;
			}
			else if (rowOfInterestIndex == 2) // second row = force in global y-axis row
			{
				equalityConstraintForceTorqueValuesForControlledRow[controlledRowIndex] = desiredForcesViconFrame.y;
			}
			else if (rowOfInterestIndex == 3) // third row = force in global z-axis row
			{
				equalityConstraintForceTorqueValuesForControlledRow[controlledRowIndex] = desiredForcesViconFrame.z;
			}
			else if (rowOfInterestIndex == 4) // fourth row = torque about global x-axis row
			{
				equalityConstraintForceTorqueValuesForControlledRow[controlledRowIndex] = desiredTorquesViconFrame.x;
			}
			else if (rowOfInterestIndex == 5) // fifth row = torque about global y-axis row
			{
				equalityConstraintForceTorqueValuesForControlledRow[controlledRowIndex] = desiredTorquesViconFrame.y;
			}
			else if (rowOfInterestIndex == 6) // sixth row = torque about global z-axis row
			{
				equalityConstraintForceTorqueValuesForControlledRow[controlledRowIndex] = desiredTorquesViconFrame.z;
			}
		}

		// Return the equality constraints for the controlled rows
		return equalityConstraintForceTorqueValuesForControlledRow;
	}

	public Vector3 GetForceOnPelvisFromCable()
	{
		return forceOnPelvisFromCable;
	}

	public void SetDebugModeFlag(bool debugModeFlagIn)
    {
		debugModeFlag = debugModeFlagIn;

	}

	private void DebugLogIfLevelManagerDebugIsSet(string debugMessage)
    {
		if(debugModeFlag == true)
        {
			Debug.Log(debugMessage);
        }
    }


	public enum EnforcePelvicTorqueBoundsControl
    {
		Enforce,
		Disabled
    }
}
