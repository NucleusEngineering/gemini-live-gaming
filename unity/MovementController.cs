using UnityEngine;
using Unity.FPS.Gameplay;

public class MovementController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("The object to move. If empty, the object this script is attached to will move.")]
    public Transform targetTransform;

    public float movementSpeed = 3f;

    private bool isMovingForward = false;
    private PlayerInputHandler playerInputHandler;

    private void Start()
    {
        if (targetTransform == null)
        {
            targetTransform = transform;
        }

        playerInputHandler = targetTransform.GetComponent<PlayerInputHandler>();
        if (playerInputHandler == null)
        {
            playerInputHandler = targetTransform.GetComponentInChildren<PlayerInputHandler>();
        }

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
            else if (action == "JUMP")
            {
                Debug.Log("MovementController: Jumping.");
                if (playerInputHandler != null)
                {
                    playerInputHandler.externalJumpInputDown = true;
                }
            }
        }
    }

    private void Update()
    {
        if (playerInputHandler != null)
        {
            if (isMovingForward)
            {
                // In PlayerInputHandler, Z represents forward/backward input
                playerInputHandler.externalMoveInput = Vector3.forward;
            }
            else
            {
                playerInputHandler.externalMoveInput = Vector3.zero;
            }
        }
    }
}
