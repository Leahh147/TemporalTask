using System;
using System.Collections.Generic;
using UnityEngine;
using UserInTheBox;

namespace UserInTheBox
{
    public class RLEnv_TemporalTask : RLEnv
    {
        [Header("Task Parameters")]
        public float episodeLength = 5f;       // 5 second episodes
        public float minCueTime = 1f;          // Earliest cue at 0 second
        public float maxCueTime = 3f;          // Latest cue at 3 seconds
        public float requiredMovementDistance = 0.4f; // Required Z-direction movement
        public float movementThreshold = 0.001f; // Threshold for detecting early movement
        
        [Header("Audio Settings")]
        public AudioClip audioCueSound;
        public float audioVolume = 0.7f;

        [Header("Reward Settings")]
        public float timingSuccessReward = 10f;      // Reward for waiting until cue (first subtask)
        public float movementSuccessReward = 5f;    // Reward for completing movement (second subtask)
        public float earlyMovementPenalty = 5f;     // Penalty for moving before cue
        private bool _timingRewardGiven = false;    // Track if timing reward was already given
        
        // Internal state
        private float _cueTimestamp;
        private float _episodeTimer = 0f;
        private bool _cueTriggered = false;
        private bool _movementCompleted = false;
        private bool _earlyMovementDetected = false;
        private bool _isRunning = false;
        private int _correctResponses = 0;
        private int _totalResponses = 0;
        private int _earlyMovements = 0;
        private Vector3 _initialHandPosition;
        private int _fixedSeed;
        private bool _debug;
        private Vector3 _restPosition; // The "home" position where the hand should return
        
        // Components
        private AudioSource _audioSource;
        private Transform _interactionPointTransform;
        
        public override void InitialiseGame()
        {
            // Check if debug mode is enabled
            _debug = Application.isEditor;

            // Get game variant and level
            if (!_debug)
            {
                _logging = UitBUtils.GetOptionalArgument("logging");
                
                string fixedSeed = UitBUtils.GetOptionalKeywordArgument("fixedSeed", "0");
                if (!Int32.TryParse(fixedSeed, out _fixedSeed))
                {
                    Debug.Log("Couldn't parse fixed seed from given value, using default 0");
                    _fixedSeed = 0;
                }
            }
            else
            {
                _fixedSeed = 0;
                _logging = false;
                simulatedUser.audioModeOn = true;
                if (simulatedUser.audioModeOn) {
                    audioManager.SignalType = "Mono";
                    audioManager.SampleType = "Amplitude";
                    Debug.Log("Audio mode on, using signal type " + audioManager.SignalType + " and sample type " + audioManager.SampleType);
                }
            }
            
            // Set up audio source
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialize = false;
                _audioSource.spatialBlend = 0.0f;
                _audioSource.playOnAwake = false; // Ensure this is false
                _audioSource.loop = false;        // Ensure this is false
                _audioSource.volume = 1.0f;       // Set a default volume
                
                Debug.Log("Created new AudioSource with playOnAwake=false");
            }
            else
            {
                _audioSource.playOnAwake = false;
                _audioSource.loop = false;
                Debug.Log("Using existing AudioSource, set playOnAwake=false");
            }
            
            // Get interaction point transform
            if (simulatedUser != null)
            {
                _interactionPointTransform = simulatedUser.rightHandController;
                Debug.Log("Using right controller as interaction point");
                
                // Capture initial position as rest position (only once on game start)
                if (_restPosition == Vector3.zero)
                {
                    _restPosition = _interactionPointTransform.position;
                    Debug.Log($"Rest position set to {_restPosition}");
                }
            }
            else
            {
                Debug.LogError("SimulatedUser reference is missing");
            }
            
            // Initialize log dictionary
            _logDict = new Dictionary<string, object>
            {
                { "Points", 0 },
                { "failrateTarget0", 0.0f }
            };
        }
        
        public override void InitialiseReward()
        {
            _reward = 0.0f;
            _cueTriggered = false;
            _movementCompleted = false;
            _earlyMovementDetected = false;
            _episodeTimer = 0f;
            _timingRewardGiven = false; // Reset timing reward flag
            
            if (_interactionPointTransform != null)
            {
                // Use the stored rest position instead of current position
                _initialHandPosition = _restPosition;
            }
        }
        
        protected override void CalculateReward()
        {
            if (!_isRunning)
                return;
                
            // Update timer
            _episodeTimer += Time.deltaTime;
            
            // Check if we need to trigger the cue
            if (!_cueTriggered && _episodeTimer >= _cueTimestamp)
            {
                TriggerCue();
            }
            
            // Default reward is zero
            _reward = 0f;
            
            // Handle movement detection and rewards
            if (_interactionPointTransform != null)
            {
                Vector3 currentPos = _interactionPointTransform.position;
                Vector3 movement = currentPos - _initialHandPosition;
                
                // Before cue is triggered - check for early movement
                if (!_cueTriggered)
                {
                    if (Mathf.Abs(movement.z) > movementThreshold && !_earlyMovementDetected)
                    {
                        // Penalize early movement - only once per episode
                        _reward -= earlyMovementPenalty;
                        _earlyMovementDetected = true;
                        _earlyMovements++;
                        Debug.Log($"Early movement detected! Z-movement: {movement.z:F2}m");

                        // Update metrics
                        UpdateSuccessMetrics();
                        
                        // Reset hand position 
                        ResetHandPosition();
                    }
                    else if (_earlyMovementDetected)
                    {
                        // Keep enforcing rest position without additional penalties
                        ResetHandPosition();
                    }
                }
                // After cue is triggered - give timing reward and check movement distance
                else if (_cueTriggered && !_movementCompleted)
                {
                    // FIRST SUBTASK: Reward for correct timing (waiting for cue)
                    // Only give this reward once per episode and only if no early movement
                    if (!_timingRewardGiven && !_earlyMovementDetected)
                    {
                        _reward += timingSuccessReward;
                        _timingRewardGiven = true;
                        Debug.Log($"First subtask complete: Timing success reward: +{timingSuccessReward}");
                    }
                    
                    // SECOND SUBTASK: Check movement distance requirement
                    if (movement.z >= requiredMovementDistance)
                    {
                        // Success! Movement completed
                        _movementCompleted = true;
                        _correctResponses++;
                        
                        // Give reward for movement completion (second subtask)
                        _reward += movementSuccessReward;
                        _logDict["Points"] = _correctResponses;
                        
                        _totalResponses++;
                        UpdateSuccessMetrics();
                        
                        Debug.Log($"Movement task completed! Moved {movement.z:F2}m in Z direction");
                        
                        // Reset hand position 
                        ResetHandPosition();
                        
                        // End the episode immediately on full success
                        EndEpisode();
                    }
                }
            }
            
            // Check if episode is over
            if (_episodeTimer >= episodeLength)
            {
                EndEpisode();
            }
        }
        
        // Update the ResetHandPosition method
        private void ResetHandPosition()
        {
            if (_interactionPointTransform != null)
            {
                // Force the controller back to the initial rest position
                _interactionPointTransform.position = _restPosition;
                Debug.Log($"Hand position reset to {_restPosition}");
            }
            else
            {
                Debug.LogWarning("Cannot reset hand position - interaction point not found");
            }
        }
        
        public override void UpdateIsFinished()
        {
            _isFinished = !_isRunning;
        }
        
        public override float GetTimeFeature()
        {
            return _episodeTimer / episodeLength;
        }
        
        public override void Reset()
        {
            // Reset internal state
            InitialiseReward();
            
            // Generate a new cue time for this episode
            GenerateCueTime();
            
            // Reset hand position
            ResetHandPosition();
            
            // Start episode
            _isRunning = true;
            _episodeTimer = 0f;
            
            // Reset logging dictionary
            _logDict["Points"] = 0;
            _logDict["failrateTarget0"] = 0.0f;

            Debug.Log($"New episode started - Cue will trigger at {_cueTimestamp:F2}s");
        }
        
        private void GenerateCueTime()
        {
            // Generate a single random cue time
            _cueTimestamp = UnityEngine.Random.Range(minCueTime, maxCueTime);
        }
        
        private void TriggerCue()
        {
            if (audioCueSound == null)
            {
                Debug.LogError("No audio clip assigned to audioCueSound!");
                return;
            }
            
            if (_audioSource == null)
            {
                Debug.LogError("No AudioSource available!");
                return;
            }
            
            // Play the sound with explicit parameters for debugging
            _audioSource.clip = audioCueSound;
            _audioSource.volume = audioVolume;
            _audioSource.Play();
            
            Debug.Log($"Attempting to play audio cue at time {_episodeTimer:F2}s (clip length: {audioCueSound.length}s)");
            
            _cueTriggered = true;
        }
        
        private void EndEpisode()
        {
            _isRunning = false;
            
            Debug.Log($"Episode ended - Successful movements so far: {_correctResponses}/{_totalResponses + _earlyMovements} " +
                      $"(Early movements: {_earlyMovements})");
        }
        
        private void UpdateSuccessMetrics()
        {
            int failures = _totalResponses - _correctResponses + _earlyMovements;
            int attempts = _totalResponses + _earlyMovements;
            _logDict["failrateTarget0"] = attempts > 0 ? (float)failures / attempts : 0.0f;
        }
    }
}