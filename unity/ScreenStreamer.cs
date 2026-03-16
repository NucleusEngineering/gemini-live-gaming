using UnityEngine;
using System;
using System.Collections;

public class ScreenStreamer : MonoBehaviour
{
    [Header("Capture Settings")]
    [Tooltip("The camera used for capturing. If empty, Camera.main will be used.")]
    public Camera targetCamera;

    [Tooltip("Frames per second to capture and send to Gemini. 1 FPS is usually sufficient.")]
    public float captureFPS = 1.0f;
    
    [Tooltip("Image compression quality (1-100)")]
    public int jpgQuality = 50;

    [Tooltip("Resolution scale (lower scale = smaller base64 size)")]
    public float resolutionScale = 0.5f;

    private float nextCaptureTime = 0f;

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogError("ScreenStreamer: No camera found! Please assign a target camera.");
        }
    }

    private void Update()
    {
        if (Time.time >= nextCaptureTime)
        {
            nextCaptureTime = Time.time + (1f / captureFPS);

            if (GeminiLiveClient.Instance != null && GeminiLiveClient.Instance.isConnected)
            {
                StartCoroutine(CaptureAndSendFrame());
            }
        }
    }

    private IEnumerator CaptureAndSendFrame()
    {
        // Wait for end of frame before reading render texture
        yield return new WaitForEndOfFrame();

        int targetWidth = Mathf.RoundToInt(Screen.width * resolutionScale);
        int targetHeight = Mathf.RoundToInt(Screen.height * resolutionScale);

        RenderTexture rt = new RenderTexture(targetWidth, targetHeight, 24);
        targetCamera.targetTexture = rt;
        
        Texture2D screenShot = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
        
        targetCamera.Render();
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        screenShot.Apply();
        
        // Reset state
        targetCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        // Encode and send
        byte[] bytes = screenShot.EncodeToJPG(jpgQuality);
        Destroy(screenShot);

        string base64String = Convert.ToBase64String(bytes);
        GeminiLiveClient.Instance.SendData("video", base64String);
    }
}
