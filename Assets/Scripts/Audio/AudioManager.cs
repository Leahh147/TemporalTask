using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TemporalTask.Audio;

/// <summary>
/// Plays meows or barks audio clips, 
/// moves around randomly.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [SerializeField]
    private string audioFolder = "AudioClips";
    public AudioSource m_AudioSource;
    public AudioSensorComponent m_AudioSensorComponent;
    private List<AudioClip> m_Clips;
    private Vector3 m_TargetPosition;
    private string sampleType = "Amplitude";
    private string signalType = "Mono";

    public string SampleType
    {
        get { return sampleType; }
        set { sampleType = value; }
    }

    public string SignalType
    {
        get { return signalType; }
        set { signalType = value; }
    }

    private void Awake()
    {
        Debug.Log("Looking for audio in: " + Application.dataPath + "/Resources/" + audioFolder);
        m_AudioSource = GetComponent<AudioSource>();
        m_Clips = new List<AudioClip>();
        var clips = Resources.LoadAll(audioFolder, typeof(AudioClip));
        foreach (var clip in clips)
        {
            AudioClip audioClip = (AudioClip)clip;
            m_Clips.Add(audioClip);
            Debug.Log("Loaded audio clip: " + audioClip.name);
        }
        StartCoroutine(PlayAudioClip()); // form an infinite loop

        // m_AudioSource.position = transform.position; // leave it for uitb??
    }

    private IEnumerator PlayAudioClip()
    {
        var clip = m_Clips[Random.Range(0, m_Clips.Count)];
        m_AudioSource.pitch = Time.timeScale;
        m_AudioSource.PlayOneShot(clip);
        yield return new WaitForSecondsRealtime(clip.length);
        StartCoroutine(PlayAudioClip());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }
}
