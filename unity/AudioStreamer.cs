using UnityEngine;
using System;
using System.Collections.Generic;

public class AudioStreamer : MonoBehaviour
{
    [Tooltip("Target sample rate for Gemini (16000Hz)")]
    public int sampleRate = 16000;

    [Tooltip("Microphone device name. Leave empty for default.")]
    public string micDeviceName = "";

    [Tooltip("If enabled, mutes the microphone while Gemini is speaking to prevent echo/feedback loops.")]
    public bool enableEchoReduction = true;

    private AudioClip micClip;
    private int lastSamplePosition = 0;
    private bool isRecording = false;
    private AudioReceiver audioReceiver;

    private void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("AudioStreamer: No microphone found!");
            return;
        }

        if (string.IsNullOrEmpty(micDeviceName))
        {
            micDeviceName = Microphone.devices[0];
        }

        // 10 second looping buffer
        micClip = Microphone.Start(micDeviceName, true, 10, sampleRate);
        isRecording = true;
        Debug.Log($"AudioStreamer: Started recording with {micDeviceName} at {sampleRate}Hz");
    }

    private void OnDestroy()
    {
        if (isRecording)
        {
            Microphone.End(micDeviceName);
            isRecording = false;
        }
    }

    private void Update()
    {
        if (!isRecording) return;
        if (GeminiLiveClient.Instance == null || !GeminiLiveClient.Instance.isConnected) return;

        int currentPosition = Microphone.GetPosition(micDeviceName);
        if (currentPosition < 0 || lastSamplePosition == currentPosition) return;

        // If echo reduction is active and Gemini is speaking, we discard the captured microphone data
        if (enableEchoReduction)
        {
            if (audioReceiver == null)
            {
                audioReceiver = FindObjectOfType<AudioReceiver>();
            }

            if (audioReceiver != null && audioReceiver.IsPlayingAudio)
            {
                lastSamplePosition = currentPosition;
                return;
            }
        }

        int sampleCount;
        if (currentPosition > lastSamplePosition)
            sampleCount = currentPosition - lastSamplePosition;
        else
            sampleCount = (micClip.samples - lastSamplePosition) + currentPosition; // Wrapped around

        if (sampleCount > 0)
        {
            float[] samples = new float[sampleCount];
            micClip.GetData(samples, lastSamplePosition);

            // Convert float samples (-1.0 to 1.0) to 16-bit PCM little endian
            byte[] pcmBytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short val = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767);
                byte[] valBytes = BitConverter.GetBytes(val);
                pcmBytes[i * 2] = valBytes[0];
                pcmBytes[i * 2 + 1] = valBytes[1];
            }

            string base64Audio = Convert.ToBase64String(pcmBytes);
            GeminiLiveClient.Instance.SendData("audio", base64Audio);

            lastSamplePosition = currentPosition;
        }
    }
}
