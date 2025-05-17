# Temporal Task: Audio-Cued Movement Project

## Overview

The **Temporal Task** is a reinforcement learning environment that requires agents to perform precisely timed movements in response to audio cues. This task is designed to develop and test an agent's ability to:

* **Wait patiently** for an audio cue
* **React promptly** when the cue is given
* **Move a specified distance** in the Z-direction

The task requires both **inhibitory control** (waiting for the cue) and **precise temporal response** (moving when cued).

---

## Task Details

### Basic Parameters

* **Episode Length:** 5 seconds
* **Cue Timing:** Random between 0–3 seconds into the episode
* **Required Movement:** 0.4 meters in the **positive Z direction**
* **Movement Threshold:** 0.001 m (movements above this threshold are detected)

### Audio Cue

* Plays **once per episode** at a **random time between 1–3 seconds**
* **Mono amplitude-based** for clear signal detection

---

## Success Conditions

* The agent must **remain still until the audio cue plays**
* After the cue, the agent must **move at least 0.4 m** in the Z direction
* Episode ends immediately upon successful completion

---

## Failure Conditions

* **Early movement** (before the audio cue)
* **Insufficient movement** after the cue
* **Episode timeout** (5 seconds)

---

## Reward Structure

* **Success Reward:** +10 points for completing the required movement
* **Early Movement Penalty:** -5 points for moving before the audio cue
* **Sparse Rewards Only:** No dense rewards provided (success/failure only)

---

## Environment Setup

### Unity Environment

* **Audio-only** environment (no visual targets)
* **Right controller** tracks hand position and movement
* Hand is **reset to original position** after success
* **Early movements detected and penalized**

### User-in-the-Box (UitB) Integration

* Integrated into the **User-in-the-Box (UitB)** framework
* Uses **MoblArmsWrist** biomechanical model
* Tracks **effort cost** via `CumulativeFatigue3CCr`
* Provides **proprioceptive perception** with end-effector position

---

## Training Configuration

* **Algorithm:** PPO (Proximal Policy Optimization)
* **Policy:** `MultiInputActorCriticPolicyTanhActions`
* **Network Architecture:** \[256, 256] with `LeakyReLU` activation
* **Time Steps:** 50 million
* **Workers:** 10
* **Learning Rate:** Linear schedule from `5e-5` to `1e-7`

---

## Usage

### Running the Environment

Audio processing is **always enabled** in this project. The environment is configured to use:

* **Mono audio signals**
* **Amplitude-based sampling**

No additional command-line arguments are required for these defaults. The random seed is **not set at this stage**, but **seed options will be added soon**.

---

### Train and Evaluate

To **train** and **evaluate** the Temporal Task environment, use the following commands:

```bash
python uitb/train/trainer.py uitb/config/mobl_arms_temporal_task_linux_train_2.yaml
```

```bash
python uitb/test/evaluator.py simulators/mobl_arms_temporal_task_linux_train_2 --record --num_episodes 10
```

> 🔧 *Note: Seed options will be introduced soon to support reproducibility.*


---

## Metrics

The environment tracks and logs the following:

* **Points:** Number of successful movements
* **EffortCost:** Cumulative movement effort cost
* **failrateTarget0:** Ratio of failed attempts (early attempts and failures) to total attempts

---

## Project Structure

* `RLEnv_TemporalTask.cs`: Main environment implementation that defines rewards, handles environment resetting, manages task state, tracks metrics, and interfaces with UitB through sim2vr.

* **User-in-the-Box (UitB) configuration files**: Define biomechanical model setup and perception modules for training integration.

* **sim2vr**: The system that aligns user simulation with the temporal task in VR by establishing a continuous closed loop between the two processes.

* **Audio assets**: Sound files providing temporal cues to the simulated user for timing-based interactions.

---

## Note

This environment is a toy project. It is designed for **biomechanically realistic movement training** and tests **timed audio response**, a critical component of more complex interactive tasks. The result provides a theoretical foundation for the timed audio cues in other VR applications, such as **Whac-A-Mole**.

---
