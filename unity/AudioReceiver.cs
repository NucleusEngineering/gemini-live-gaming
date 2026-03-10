using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class AudioReceiver : MonoBehaviour
{
    private AudioSource audioSource;
    private Queue<float> audioBuffer = new Queue<float>();
    
    [Tooltip("Gemini Live output sample rate")]
    public float geminiSampleRate = 24000f; // Default for gemini-2.0-flash audio responses

    private float fractionalIndex = 0f;
    private float currentSample = 0f;
    private int outputSampleRate;
    
    // Buffering logic
    private bool isBuffering = true;
    private int minBufferSamples;
    private int maxBufferSamples;

    private void Awake()
    {
        outputSampleRate = AudioSettings.outputSampleRate;
        // 200ms jitter buffer at Gemini's sample rate
        minBufferSamples = (int)(geminiSampleRate * 0.2f);
        // 1 second max buffer before we start skipping to catch up
        maxBufferSamples = (int)(geminiSampleRate * 1.0f);
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

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (outputSampleRate == 0) return;
        
        float resampleRatio = geminiSampleRate / (float)outputSampleRate;

        lock (audioBuffer)
        {
            // Catch up logic: if buffer is too large, skip some samples
            if (audioBuffer.Count > maxBufferSamples)
            {
                int samplesToSkip = audioBuffer.Count - minBufferSamples;
                for (int s = 0; s < samplesToSkip; s++)
                {
                    audioBuffer.Dequeue();
                }
            }

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
                
                if (isBuffering) break; // Stop processing and wait for buffer
            }
        }
    }
}
