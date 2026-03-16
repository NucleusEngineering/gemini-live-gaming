using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class GeminiLiveClient : MonoBehaviour
{
    [Header("Server Settings")]
    [Tooltip("The WebSocket URL of the Cloud Run proxy (e.g., wss://your-app-url.run.app/ws)")]
    public string serverUrl = "ws://localhost:8080/ws";

    [Header("Connection State")]
    public bool isConnected = false;

    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellationTokenSource;

    // Thread-safe queue for incoming messages to be processed on the main thread
    private ConcurrentQueue<GeminiMessage> incomingMessages = new ConcurrentQueue<GeminiMessage>();

    public static GeminiLiveClient Instance { get; private set; }

    [Serializable]
    public class GeminiMessage
    {
        public string type;
        public string data;     // For audio/text/video
        public string name;     // For tool_call
        public ToolArgs args;   // For tool_call
    }

    [Serializable]
    public class ToolArgs
    {
        public string action;
    }

    // Events that other scripts can subscribe to
    public event Action<string> OnTextReceived;
    public event Action<byte[]> OnAudioReceived;
    public event Action<string, string> OnToolCallReceived; // name, action

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private async void Start()
    {
        await ConnectToServer();
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private async Task ConnectToServer()
    {
        if (string.IsNullOrEmpty(serverUrl))
        {
            Debug.LogError("GeminiLiveClient: Server URL is empty!");
            return;
        }

        webSocket = new ClientWebSocket();
        cancellationTokenSource = new CancellationTokenSource();

        try
        {
            Debug.Log($"GeminiLiveClient: Connecting to {serverUrl}...");
            await webSocket.ConnectAsync(new Uri(serverUrl), cancellationTokenSource.Token);
            isConnected = true;
            Debug.Log("GeminiLiveClient: Connected successfully!");

            // Start listening for messages in the background
            _ = ReceiveMessages(cancellationTokenSource.Token);
        }
        catch (Exception e)
        {
            Debug.LogError($"GeminiLiveClient: Connection failed - {e.Message}");
            isConnected = false;
        }
    }

    public void Disconnect()
    {
        if (webSocket != null)
        {
            cancellationTokenSource?.Cancel();
            webSocket.Dispose();
            webSocket = null;
            isConnected = false;
            Debug.Log("GeminiLiveClient: Disconnected.");
        }
    }

    private void Update()
    {
        // Process messages on Unity's main thread
        while (incomingMessages.TryDequeue(out GeminiMessage message))
        {
            try
            {
                if (message.type == "text")
                {
                    OnTextReceived?.Invoke(message.data);
                }
                else if (message.type == "audio")
                {
                    byte[] audioBytes = Convert.FromBase64String(message.data);
                    OnAudioReceived?.Invoke(audioBytes);
                }
                else if (message.type == "tool_call")
                {
                    Debug.Log($"GeminiLiveClient: Tool call received - {message.name} with action {message.args?.action}");
                    OnToolCallReceived?.Invoke(message.name, message.args?.action);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GeminiLiveClient: Error processing message - {e.Message}");
            }
        }
    }

    private async Task ReceiveMessages(CancellationToken token)
    {
        var buffer = new byte[8192 * 4]; // 32KB buffer due to potentially large base64 chunks
        var stringBuilder = new StringBuilder();

        while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            try
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        isConnected = false;
                        Debug.Log("GeminiLiveClient: Server closed connection.");
                        return;
                    }
                    else
                    {
                        string part = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        stringBuilder.Append(part);
                    }
                } while (!result.EndOfMessage);

                string fullMessage = stringBuilder.ToString();
                stringBuilder.Clear();

                // Deserialize and queue
                var msg = JsonUtility.FromJson<GeminiMessage>(fullMessage);
                if (msg != null && !string.IsNullOrEmpty(msg.type))
                {
                    incomingMessages.Enqueue(msg);
                }
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Debug.LogError($"GeminiLiveClient: Receive error - {e.Message}");
                }
                isConnected = false;
                break;
            }
        }
    }

    public void SendData(string type, string base64Data)
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open) return;

        var message = new GeminiMessage { type = type, data = base64Data };
        string json = JsonUtility.ToJson(message);
        
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        // Fire and forget
        _ = webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
