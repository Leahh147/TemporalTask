using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UserInTheBox;

namespace UserInTheBox
{
    public class RLEnv_TemporalTask : RLEnv
    {
        [Header("Target Settings")]
        public GameObject targetObject;
        public float targetSize = 0.1f;
        public Color defaultColor = Color.white;
        
        [Header("Task Parameters")]
        public float episodeLength = 60f;
        public float minCueInterval = 1.5f;
        public float maxCueInterval = 4.0f;
        public int cuesToGenerate = 15;
        public float responseTimeWindow = 0.8f;
        
        [Header("Audio Settings")]
        public AudioClip audioCueSound;
        public float audioVolume = 0.7f;

        [Header("Reward Settings")]
        public bool useDenseReward = true;
        public float missedResponsePenalty = 0.2f;
        
        // Internal state
        private List<float> _cueTimestamps = new List<float>();
        private int _currentCueIndex = 0;
        private float _episodeTimer = 0f;
        private bool _isInResponseWindow = false;
        private float _responseWindowEndTime = 0f;
        private int _correctResponses = 0;
        private int _totalResponses = 0;
        private bool _isRunning = false;
        private int _successfulTouches = 0;
        private int _missedTouches = 0;
        private float _distanceReward = 0.0f;
        private float _previousPoints = 0;
        private float _unsuccessfulContactReward = 0.0f;
        private Vector3 _lastHandPosition;
        private float _effortCost = 0.0f;
        private bool _denseGameReward;
        private int _fixedSeed;
        private bool _debug;
        
        // Components
        private AudioSource _audioSource;
        private Renderer _targetRenderer;
        private Transform _interactionPointTransform;
        private Material _targetMaterial;
        
        public override void InitialiseGame()
        {
            // Check if debug mode is enabled
            _debug = Application.isEditor;  //UitBUtils.GetOptionalArgument("debug");

            // Get game variant and level
            if (!_debug)
            {
                _logging = UitBUtils.GetOptionalArgument("logging");
                _denseGameReward = !UitBUtils.GetOptionalArgument("sparse");
                if (_denseGameReward)
                {
                    Debug.Log("Dense game reward enabled");
                }
                else
                {
                    Debug.Log("Sparse game reward enabled");
                }

                string fixedSeed = UitBUtils.GetOptionalKeywordArgument("fixedSeed", "0");
                // Try to parse given fixed seed string to int
                if (!Int32.TryParse(fixedSeed, out _fixedSeed))
                {
                    Debug.Log("Couldn't parse fixed seed from given value, using default 0");
                    _fixedSeed = 0;
                }

            }
            else
            {
                _denseGameReward = true;
                _fixedSeed = 0;
                _logging = false;
                simulatedUser.audioModeOn = true;
                if (simulatedUser.audioModeOn) {
                    audioManager.SignalType = "Mono";
                    audioManager.SampleType = "Amplitude";
                    Debug.Log("Audio mode on, using signal type " + audioManager.SignalType + " and sample type " + audioManager.SampleType);
                }
            }
        }
        
        public override void InitialiseReward()
        {
            _reward = 0.0f;
            _isInResponseWindow = false;
            _episodeTimer = 0f;
            _correctResponses = 0;
            _totalResponses = 0;
            _successfulTouches = 0;
            _missedTouches = 0;
            _distanceReward = 0.0f;
            _previousPoints = 0;
            _unsuccessfulContactReward = 0.0f;
            _effortCost = 0.0f;
            _lastHandPosition = _interactionPointTransform != null ? _interactionPointTransform.position : Vector3.zero;
        }
        
        protected override void CalculateReward()
        {
            if (!_isRunning)
                return;
                
            // Update timer
            _episodeTimer += Time.deltaTime;
            
            // Check if we need to trigger the next cue
            if (_currentCueIndex < _cueTimestamps.Count && 
                _episodeTimer >= _cueTimestamps[_currentCueIndex])
            {
                TriggerCue();
                _currentCueIndex++;
            }
            
            // Default reward is zero
            _reward = 0f;
            
            // Primary reward logic for correct responses
            if (_isInResponseWindow)
            {
                // Check if time window has expired
                if (Time.time > _responseWindowEndTime)
                {
                    // Failed to respond in time
                    _isInResponseWindow = false;
                    _totalResponses++;
                    _missedTouches++;
                    
                    // Small negative reward for missed response
                    if (useDenseReward)
                        _reward -= missedResponsePenalty;
                    
                    UpdateSuccessMetrics();
                }
                else
                {
                    bool isInsideTarget = IsInsideTarget();
                    
                    // If the agent touches the target during the response window, give reward
                    if (isInsideTarget)
                    {
                        // Record correct response (just once per cue)
                        if (_isInResponseWindow)
                        {
                            _correctResponses++;
                            _successfulTouches++;
                            
                            // Update Points for whacamole compatibility
                            _logDict["Points"] = _correctResponses;
                            _reward = (_correctResponses - _previousPoints) * 10;
                            _previousPoints = _correctResponses;
                            
                            _isInResponseWindow = false;
                            _totalResponses++;
                            UpdateSuccessMetrics();
                        }
                    }
                }
            }
            
            // Add dense spatial reward component if enabled
            if (useDenseReward)
            {
                // Distance reward
                _distanceReward = CalculateDistanceReward();
                _reward += _distanceReward;
                _logDict["DistanceReward"] = _distanceReward;
                
                // Unsuccessful contact reward (when not in response window)
                if (_interactionPointTransform != null && !_isInResponseWindow && targetObject != null)
                {
                    // Check if close to but not inside target
                    float distance = Vector3.Distance(_interactionPointTransform.position, targetObject.transform.position);
                    float targetRadius = targetSize / 2.0f;
                    if (distance < targetRadius * 1.5f && distance >= targetRadius)
                    {
                        // Calculate velocity (simplified)
                        Vector3 currentPos = _interactionPointTransform.position;
                        Vector3 velocity = Vector3.zero;
                        
                        if (_lastHandPosition != Vector3.zero)
                        {
                            velocity = (currentPos - _lastHandPosition) / Time.deltaTime;
                        }
                        
                        _unsuccessfulContactReward = velocity.magnitude * 0.01f;
                        _reward += _unsuccessfulContactReward;
                        _logDict["RewardUnsuccessfulContact"] = _unsuccessfulContactReward;
                    }
                    else
                    {
                        _logDict["RewardUnsuccessfulContact"] = 0.0f;
                    }
                }
                
                // Calculate effort cost
                if (_interactionPointTransform != null)
                {
                    Vector3 currentPos = _interactionPointTransform.position;
                    if (_lastHandPosition != Vector3.zero)
                    {
                        Vector3 movement = currentPos - _lastHandPosition;
                        _effortCost = movement.magnitude * 0.05f;
                        _lastHandPosition = currentPos;
                    }
                    else
                    {
                        _effortCost = 0.0f;
                        _lastHandPosition = _interactionPointTransform.position;
                    }
                    
                    _logDict["EffortCost"] = _effortCost;
                }
            }
            
            // Check if episode is over
            if (_episodeTimer >= episodeLength)
            {
                EndEpisode();
            }
        }

        // Calculate distance-based reward to encourage proximity to target
        private float CalculateDistanceReward()
        {
            if (_interactionPointTransform == null || targetObject == null)
                return 0f;
                
            // Calculate distance between hand and target
            float distance = Vector3.Distance(_interactionPointTransform.position, targetObject.transform.position);
            
            // Convert to a reward using an exponential decay function
            // This gives higher rewards for being closer to the target
            return _distRewardFunc(distance);
        }

        private float _distRewardFunc(float distance)
        {
            // Using the Whacamole formulation: (exp(-10*dist)-1)/10
            // This gives a steeper reward curve that approaches 0 asymptotically
            return (float)(Math.Exp(-10 * distance) - 1) / 10;
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
            
            // Generate cue events
            GenerateCueEvents();
            
            // Start episode
            _isRunning = true;
            _currentCueIndex = 0;
            _episodeTimer = 0f;
            
            // Reset logging dictionary - only the five required keys
            _logDict["Points"] = 0;
            _logDict["RewardUnsuccessfulContact"] = 0.0f;
            _logDict["DistanceReward"] = 0.0f;
            _logDict["EffortCost"] = 0.0f;
            _logDict["failrateTarget0"] = 0.0f;

            Debug.Log("Audio-Only Temporal Task Reset");
        }
        
        private void GenerateCueEvents()
        {
            _cueTimestamps.Clear();
            float currentTime = minCueInterval; // Start with initial delay
            
            for (int i = 0; i < cuesToGenerate; i++)
            {
                // Add random time interval
                currentTime += UnityEngine.Random.Range(minCueInterval, maxCueInterval);
                
                // Ensure the cue fits within episode length
                if (currentTime >= episodeLength)
                    break;
                
                // Add the timestamp
                _cueTimestamps.Add(currentTime);
            }

            Debug.Log("Generated " + _cueTimestamps.Count + " audio cues for the episode");
        }
        
        private void TriggerCue()
        {
            // Play audio cue
            if (audioCueSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(audioCueSound, audioVolume);
                Debug.Log("Playing audio cue at time " + _episodeTimer);
            }
            
            // Start response window
            _isInResponseWindow = true;
            _responseWindowEndTime = Time.time + responseTimeWindow;
        }
        
        private void EndEpisode()
        {
            _isRunning = false;
            
            Debug.Log("Temporal Task Episode ended - Correct responses: " + _correctResponses + 
                      "/" + _totalResponses + " (" + (_totalResponses > 0 ? (float)_correctResponses / _totalResponses * 100 : 0) + "%)");
        }
        
        // Check if interaction point is inside target
        private bool IsInsideTarget()
        {
            if (_interactionPointTransform == null || targetObject == null)
                return false;
                
            // Simple distance check
            float distance = Vector3.Distance(_interactionPointTransform.position, targetObject.transform.position);
            float targetRadius = targetSize / 2.0f;
            return distance < targetRadius;
        }
        
        private void UpdateSuccessMetrics()
        {
            // Update only the failrateTarget0 since Points is updated directly in CalculateReward
            _logDict["failrateTarget0"] = _totalResponses > 0 ? (float)_missedTouches / _totalResponses : 0.0f;
        }
    }
}