using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class AudioReceiver : MonoBehaviour
{
    private AudioSource audioSource;
    private Queue<float> audioBuffer = new Queue<float>();
    
    [Tooltip("Gemini Live output sample rate")]
    public float geminiSampleRate = 24000f; // Default for gemini-3.1-flash-live audio responses

    private float fractionalIndex = 0f;
    private float currentSample = 0f;
    private int outputSampleRate;
    
    // Buffering logic
    private bool isBuffering = true;
    private int minBufferSamples;

    public bool IsPlayingAudio
    {
        get
        {
            lock (audioBuffer)
            {
                return !isBuffering && audioBuffer.Count > 0;
            }
        }
    }

    private void Awake()
    {
        outputSampleRate = AudioSettings.outputSampleRate;
        // 200ms jitter buffer at Gemini's sample rate
        minBufferSamples = (int)(geminiSampleRate * 0.2f);
    }

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        // Ensure the AudioSource is playing so OnAudioFilterRead will get called
        if (!audioSource.isPlaying)
        {
            audioSource.Play();
        }
        
        if (GeminiLiveClient.Instance != null)
        {
            GeminiLiveClient.Instance.OnAudioReceived += HandleAudioReceived;
            GeminiLiveClient.Instance.OnInterrupted += HandleInterrupted;
        }
        else
        {
            Debug.LogError("AudioReceiver: No GeminiLiveClient found!");
        }
    }

    private void OnDestroy()
    {
        if (GeminiLiveClient.Instance != null)
        {
            GeminiLiveClient.Instance.OnAudioReceived -= HandleAudioReceived;
            GeminiLiveClient.Instance.OnInterrupted -= HandleInterrupted;
        }
    }

    private void HandleAudioReceived(byte[] pcmData)
    {
        float[] floatSamples = new float[pcmData.Length / 2];
        for (int i = 0; i < floatSamples.Length; i++)
        {
            short sample = BitConverter.ToInt16(pcmData, i * 2);
            floatSamples[i] = sample / 32768f;
        }

        lock (audioBuffer)
        {
            foreach (var f in floatSamples)
            {
                audioBuffer.Enqueue(f);
            }
        }
    }

    private void HandleInterrupted()
    {
        lock (audioBuffer)
        {
            audioBuffer.Clear();
            isBuffering = true;
            fractionalIndex = 0f;
            currentSample = 0f;
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (outputSampleRate == 0) return;
        
        float resampleRatio = geminiSampleRate / (float)outputSampleRate;

        lock (audioBuffer)
        {
            // Buffering logic
            if (isBuffering)
            {
                if (audioBuffer.Count >= minBufferSamples)
                {
                    isBuffering = false;
                }
                else
                {
                    // Silence while buffering
                    for (int i = 0; i < data.Length; i++) data[i] = 0;
                    return;
                }
            }

            for (int i = 0; i < data.Length; i += channels)
            {
                fractionalIndex += resampleRatio;
                while (fractionalIndex >= 1f)
                {
                    fractionalIndex -= 1f;
                    if (audioBuffer.Count > 0)
                    {
                        currentSample = audioBuffer.Dequeue();
                    }
                    else
                    {
                        // Ran out of data, start buffering again
                        isBuffering = true;
                        currentSample = 0f;
                        break;
                    }
                }

                for (int c = 0; c < channels; c++)
                {
                    data[i + c] = currentSample;
                }
                
                if (isBuffering)
                {
                    // Fill remaining buffer with silence to avoid click/pop/stale data playing
                    for (int j = i + channels; j < data.Length; j++)
                    {
                        data[j] = 0f;
                    }
                    break;
                }
            }
        }
    }
}
