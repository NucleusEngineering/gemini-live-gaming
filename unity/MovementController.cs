using UnityEngine;

public class MovementController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float movementSpeed = 3f;
    
    private bool isMovingForward = false;

    private void Start()
    {
        if (GeminiLiveClient.Instance != null)
        {
            GeminiLiveClient.Instance.OnToolCallReceived += HandleToolCall;
        }
        else
        {
            Debug.LogError("MovementController: No GeminiLiveClient found!");
        }
    }

    private void OnDestroy()
    {
        if (GeminiLiveClient.Instance != null)
        {
            GeminiLiveClient.Instance.OnToolCallReceived -= HandleToolCall;
        }
    }

    private void HandleToolCall(string toolName, string action)
    {
        if (toolName == "control_movement")
        {
            if (action == "START_FORWARD")
            {
                Debug.Log("MovementController: Starting forward movement.");
                isMovingForward = true;
            }
            else if (action == "STOP")
            {
                Debug.Log("MovementController: Stopping movement.");
                isMovingForward = false;
            }
        }
    }

    private void Update()
    {
        if (isMovingForward)
        {
            transform.Translate(Vector3.forward * movementSpeed * Time.deltaTime);
        }
    }
}
