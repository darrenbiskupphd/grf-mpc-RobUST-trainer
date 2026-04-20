# GRF-Aware Model Predictive Control for RobUST

This repository contains the Unity C# implementation of the GRF-Aware Model Predictive Controller for the RobUST cable-driven rehabilitation platform. 

Unlike traditional quasi-static, position-based controllers (e.g., ZMP-constrained 8-cable impedance control), this project introduces a proactive, dynamics-aware framework adapted from legged robotics. By explicitly integrating live Ground Reaction Force (GRF) data and centroidal dynamics into a convex Quadratic Program (QP), the system provides fluid, underactuated (4-cable) assistance that respects the user's natural balance dynamics.

## Core Methodology

### Centroidal Dynamics & SRB Approximation
To compute predictive control without requiring a full-body multicamera motion capture system, this controller reduces the human user to a **Single Rigid Body (SRB)** model. 
* **State Tracking:** A single HTC Vive tracker on the posterior pelvis (sacrum) serves as a reliable proxy for the user's Center of Mass (CoM) position and velocity.
* **GRF Integration:** Floor-mounted force plates stream live Center of Pressure (CoP) and GRF vectors at 100 Hz to close the dynamic feedback loop.
* **Control Objective:** The MPC optimizes cable tensions over a finite horizon (0.5s look-ahead) to manipulate the user's centroidal dynamics, prioritizing fall-prevention (vertical CoM and pitch/roll rates) while allowing horizontal/planar compliance.

### The Convex QP Formulation
The MPC is formulated as a condensed convex Quadratic Program solved in soft real-time using the **ALGLIB** `minqp` solver. 
* **State Vector:** 12-dimensional (CoM position, Euler angles, linear velocity, angular velocity).
* **Input:** Tension commands for a 4-cable underactuated setup.
* **Constraints:** Strictly enforces 10 N minimum tension (preventing cable slack) and 200 N maximum tension directly within the solver.

## Architecture Additions

This project builds on the foundational [RobUST Boilerplate](https://github.com/darrenbiskupphd/RobUST-Boilerplate) by extending the `BaseController<T>` with predictive capabilities:

* **`MPCSolver.cs`**: The core high-level planner. Handles the forward Euler discretization, constructs the QP matrices, applies state/input penalties, and interfaces with ALGLIB.
* **`ImpedanceController.cs`**: The baseline fully-actuated (8-cable) Cartesian impedance controller. It computes a restoring wrench based on kinematic deviations, serving as the high-gain trajectory tracking standard for experimental comparison.

## Experimental Protocols

This repository includes the three specific task protocols used to validate the MPC against standard 8-cable impedance control:

1. **Disturbance Rejection (Standing + Perturbation)**
   * **Goal:** Arrest momentum from sudden unmeasured posterior impulses.
   * **Reference:** Static initial CoM position.
2. **Dynamic Tracking (Deep Lunge)**
   * **Goal:** Test the efficiency of tracking a moving centroidal target.
   * **Reference:** Linear interpolation from neutral standing to a 20 cm vertical drop waypoint and back.
3. **Workspace Expansion (Star Excursion Balance Test)**
   * **Goal:** Map the dynamically stable workspace and test controller transparency.
   * **Reference:** Static neutral standing posture while the user performs single-leg maximal reaches.

## Running the Experiments

### Prerequisites
* For Impedance Control, the physical RobUST system must be configured in the **8-cable (underactuated)** arrangement.
* Vicon Force Plates must be calibrated and streaming via the `ForcePlateManager`.
* The user's specific biometrics (mass, height, shoulder width) must be accurately entered into the Unity Inspector to correctly scale the inertia tensor and compute cable attachment points.
* Base system setup and hardware calibration must be completed as per the [RobUST Boilerplate](INSERT_LINK_HERE) documentation.

### Execution Steps
1. Launch the LabVIEW PXIe low-level controller (1 kHz load-cell feedback loop).
2. Ensure Vicon is in "Live Mode" and SteamVR trackers are paired and tracking.
3. Ensure SteamVR is tracking and all trackers are connected.
4. Enter Play mode in Unity. The system will initiate with all tensions set to zero.
5. Select the desired experimental task (Disturbance, Lunge, or Star Excursion) in the `TaskReferenceGenerator`.
6. Controls:
   - "O" sends zero tension to all motors. It also resets the trajectory pacer to index 0
   - "T" enables transparent mode
   - "M" enables MPC. It is configured to only enable if the number of cables is set to 4
   - "I" enables Impedance Control. It is configured to only enable if the number of cables is 8
   - "SPACEBAR" initiates a trajectory
