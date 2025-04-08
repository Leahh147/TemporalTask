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
        public Color activateColor = Color.green;
        public Color deactivateColor = Color.red;
        
        [Header("Task Parameters")]
        public float episodeLength = 60f;
        public float minCueInterval = 1.5f;
        public float maxCueInterval = 4.0f;
        public int cuesToGenerate = 15;
        public float responseTimeWindow = 0.8f;
        
        [Header("Audio Settings")]
        public AudioClip activateCueSound;
        public AudioClip deactivateCueSound;
        public float audioVolume = 0.7f;
        
        [Header("Sensory Mode")]
        public bool useAudioCues = true;
        public bool useVisualCues = true;
        
        // Internal state
        private List<CueEvent> _cueEvents = new List<CueEvent>();
        private int _currentCueIndex = 0;
        private float _episodeTimer = 0f;
        private bool _isInResponseWindow = false;
        private CueType _currentCueType;
        private float _responseWindowEndTime = 0f;
        private int _correctResponses = 0;
        private int _totalResponses = 0;
        private bool _isRunning = false;
        private int _successfulActivates = 0;
        private int _successfulDeactivates = 0;
        private int _failedActivates = 0;
        private int _failedDeactivates = 0;
        
        // Components
        private AudioSource _audioSource;
        private Renderer _targetRenderer;
        private Transform _interactionPointTransform;
        private Material _targetMaterial;
        
        public override void InitialiseGame()
        {
            // Log task configuration
            Debug.Log("Initializing Temporal Task with audio mode: " + useAudioCues + ", visual mode: " + useVisualCues);
            
            // Set up audio source
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialize = true;
                _audioSource.spatialBlend = 1.0f; // Full 3D sound
                _audioSource.minDistance = 0.1f;
                _audioSource.maxDistance = 5.0f;
            }
            
            // Find target renderer and prepare
            if (targetObject != null)
            {
                _targetRenderer = targetObject.GetComponent<Renderer>();
                if (_targetRenderer != null)
                {
                    targetObject.transform.localScale = Vector3.one * targetSize;
                    _targetMaterial = _targetRenderer.material;
                    _targetMaterial.color = defaultColor;
                }
                else
                {
                    Debug.LogError("Target object does not have a renderer component");
                }
            }
            else
            {
                Debug.LogError("Target object not assigned");
            }
            
            // Get interaction point transform (fingertip)
            if (simulatedUser != null)
            {
                // Use the right hand controller as the interaction point
                _interactionPointTransform = simulatedUser.rightHandController;
                Debug.Log("Using right controller as interaction point");
            }
            else
            {
                Debug.LogError("SimulatedUser reference is missing");
            }
            
            // Enable logging
            _logging = true;
            
            // Initialize log dictionary
            _logDict = new Dictionary<string, object>
            {
                { "CorrectResponses", 0 },
                { "TotalResponses", 0 },
                { "SuccessRate", 0.0f },
                { "SuccessfulActivates", 0 },
                { "SuccessfulDeactivates", 0 },
                { "FailedActivates", 0 },
                { "FailedDeactivates", 0 }
            };
            
            // Log audio/vision modes once in initialization
            Debug.Log($"Task Configuration - Audio: {useAudioCues}, Visual: {useVisualCues}");

            // Check for command line arguments
            ParseCommandLineArguments();
        }

        private void ParseCommandLineArguments()
        {
            // Read audio mode from command line if provided
            string audioModeArg = UitBUtils.GetOptionalKeywordArgument("audioMode", "");
            if (!string.IsNullOrEmpty(audioModeArg))
            {
                if (audioModeArg == "audio")
                {
                    useAudioCues = true;
                    useVisualCues = false;
                    Debug.Log("Setting to audio-only mode from command line");
                }
                else if (audioModeArg == "visual")
                {
                    useAudioCues = false;
                    useVisualCues = true;
                    Debug.Log("Setting to visual-only mode from command line");
                }
                else if (audioModeArg == "both")
                {
                    useAudioCues = true;
                    useVisualCues = true;
                    Debug.Log("Setting to combined audio-visual mode from command line");
                }
            }

            // Check if we should use a fixed seed
            string seedArg = UitBUtils.GetOptionalKeywordArgument("fixedSeed", "0");
            if (int.TryParse(seedArg, out int seed) && seed != 0)
            {
                UnityEngine.Random.InitState(seed);
                Debug.Log("Using fixed random seed: " + seed);
            }
        }
        
        public override void InitialiseReward()
        {
            _reward = 0.0f;
            _isInResponseWindow = false;
            _episodeTimer = 0f;
            _correctResponses = 0;
            _totalResponses = 0;
            _successfulActivates = 0;
            _successfulDeactivates = 0;
            _failedActivates = 0;
            _failedDeactivates = 0;
        }
        
        protected override void CalculateReward()
        {
            if (!_isRunning)
                return;
                
            // Update timer
            _episodeTimer += Time.deltaTime;
            
            // Check if we need to trigger the next cue
            if (_currentCueIndex < _cueEvents.Count && 
                _episodeTimer >= _cueEvents[_currentCueIndex].timeStamp)
            {
                TriggerCue(_cueEvents[_currentCueIndex]);
                _currentCueIndex++;
            }
            
            // Default reward is zero
            _reward = 0f;
            
            // Check if in response window
            if (_isInResponseWindow)
            {
                // Check if time window has expired
                if (Time.time > _responseWindowEndTime)
                {
                    // Failed to respond in time
                    _isInResponseWindow = false;
                    _totalResponses++;
                    
                    // Record failure based on cue type
                    if (_currentCueType == CueType.Activate)
                    {
                        _failedActivates++;
                    }
                    else
                    {
                        _failedDeactivates++;
                    }
                    
                    UpdateSuccessMetrics();
                }
                else
                {
                    bool isInsideTarget = IsInsideTarget();
                    
                    // Check if player is in correct position based on cue type
                    if ((_currentCueType == CueType.Activate && isInsideTarget) ||
                        (_currentCueType == CueType.Deactivate && !isInsideTarget))
                    {
                        // Correct position - give reward
                        _reward = 1.0f;
                        
                        // Record correct response (just once per cue)
                        if (_isInResponseWindow)
                        {
                            _correctResponses++;
                            
                            // Record success based on cue type
                            if (_currentCueType == CueType.Activate)
                            {
                                _successfulActivates++;
                            }
                            else
                            {
                                _successfulDeactivates++;
                            }
                            
                            _isInResponseWindow = false;
                            _totalResponses++;
                            UpdateSuccessMetrics();
                        }
                    }
                }
            }
            
            // Check if episode is over
            if (_episodeTimer >= episodeLength)
            {
                EndEpisode();
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
            
            // Generate cue events
            GenerateCueEvents();
            
            // Reset target color
            if (_targetRenderer != null)
            {
                _targetMaterial.color = defaultColor;
            }
            
            // Start episode
            _isRunning = true;
            _currentCueIndex = 0;
            _episodeTimer = 0f;
            
            // Reset logging dictionary
            _logDict["CorrectResponses"] = 0;
            _logDict["TotalResponses"] = 0;
            _logDict["SuccessRate"] = 0.0f;
            _logDict["SuccessfulActivates"] = 0;
            _logDict["SuccessfulDeactivates"] = 0;
            _logDict["FailedActivates"] = 0;
            _logDict["FailedDeactivates"] = 0;

            Debug.Log("Temporal Task Reset: Audio mode: " + useAudioCues + ", Visual mode: " + useVisualCues);
        }
        
        private void GenerateCueEvents()
        {
            _cueEvents.Clear();
            float currentTime = minCueInterval; // Start with initial delay
            
            for (int i = 0; i < cuesToGenerate; i++)
            {
                // Add random time interval
                currentTime += UnityEngine.Random.Range(minCueInterval, maxCueInterval);
                
                // Ensure the cue fits within episode length
                if (currentTime >= episodeLength)
                    break;
                
                // Randomly choose between activate and deactivate
                CueType cueType = UnityEngine.Random.value > 0.5f ? CueType.Activate : CueType.Deactivate;
                
                // Create and add the cue event
                CueEvent cueEvent = new CueEvent
                {
                    timeStamp = currentTime,
                    cueType = cueType
                };
                
                _cueEvents.Add(cueEvent);
            }

            Debug.Log("Generated " + _cueEvents.Count + " cue events for the episode");
        }
        
        private void TriggerCue(CueEvent cue)
        {
            // Play audio cue if enabled
            if (useAudioCues)
            {
                AudioClip clipToPlay = (cue.cueType == CueType.Activate) ? 
                    activateCueSound : deactivateCueSound;
                    
                if (clipToPlay != null && _audioSource != null)
                {
                    _audioSource.PlayOneShot(clipToPlay, audioVolume);
                    Debug.Log("Playing audio cue: " + cue.cueType + " at time " + _episodeTimer);
                }
            }
            
            // Show visual cue if enabled
            if (useVisualCues && _targetRenderer != null)
            {
                Color newColor = (cue.cueType == CueType.Activate) ? 
                    activateColor : deactivateColor;
                    
                _targetMaterial.color = newColor;
                Debug.Log("Showing visual cue: " + cue.cueType + " at time " + _episodeTimer);
                
                // Reset color after a short delay
                StartCoroutine(ResetTargetColor(0.5f));
            }
            
            // Start response window
            _isInResponseWindow = true;
            _currentCueType = cue.cueType;
            _responseWindowEndTime = Time.time + responseTimeWindow;
        }
        
        private IEnumerator ResetTargetColor(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_targetRenderer != null)
            {
                _targetMaterial.color = defaultColor;
            }
        }
        
        private void EndEpisode()
        {
            _isRunning = false;
            
            if (_targetRenderer != null)
            {
                _targetMaterial.color = defaultColor;
            }

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
            // Update log dictionary with current success metrics
            _logDict["CorrectResponses"] = _correctResponses;
            _logDict["TotalResponses"] = _totalResponses;
            _logDict["SuccessRate"] = _totalResponses > 0 ? (float)_correctResponses / _totalResponses : 0.0f;
            _logDict["SuccessfulActivates"] = _successfulActivates;
            _logDict["SuccessfulDeactivates"] = _successfulDeactivates;
            _logDict["FailedActivates"] = _failedActivates;
            _logDict["FailedDeactivates"] = _failedDeactivates;
        }
    }

    // Enum for cue types
    public enum CueType
    {
        Activate,  // Player should be inside target
        Deactivate // Player should be outside target
    }

    // Structure to hold cue information
    public struct CueEvent
    {
        public float timeStamp;
        public CueType cueType;
    }
}