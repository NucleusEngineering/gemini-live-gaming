# Gemini Live Gamedev Assistant

This project connects your Unity editor directly to the **Gemini 2.0 Multimodal Live API**, acting as a real-time pair-programming and debugging assistant that can see your screen and talk with you.

## System Components

1. **Backend Middleware (`/backend`)**: A minimal Google Cloud Run Python FastAPI service that bridges basic WebSockets to Google's official `google-genai` async SDK.
2. **Unity Client (`/unity`)**: Four lightweight standalone C# standard-library scripts you drop into your Unity project to handle screen grabbing, WebSocket streaming, and mic/audio streaming.

## Step 1: Deploy MiddleWare (Google Cloud Run)

You will need Google Cloud CLI installed, and billing enabled on a GCP Project. The Cloud Run service will automatically use its default Compute Engine Service Account to authenticate with Vertex AI, so no API keys are required.

1. Navigate to the `backend` folder via terminal.
2. Run the deployment script:
   ```bash
   ./deploy.sh
   ```
3. The script will output a Service URL like `https://gemini-live-assistant-xxxxx-uc.a.run.app`. 
   Convert this to a WebSocket wss URL: `wss://gemini-live-assistant-xxxxx-uc.a.run.app/ws`

## Step 2: Set up Unity

1. Copy the four `.cs` files from the `unity` directory into your Unity project's `Assets/Scripts` folder.
2. Create an Empty GameObject in your current scene, name it `Gemini Assistant`.
3. Add the `GeminiLiveClient` script to it.
   - Paste the `wss://...` URL into the **Server URL** property.
4. Add the `ScreenStreamer` script to it.
   - You can leave settings as default. Make sure it targets your Main Camera (or drag a specific Camera object onto it).
5. Add the `AudioStreamer` script to it.
   - Leave the Mic device empty to use your default system microphone.
6. Add the `AudioReceiver` script to it.
   - Unity will automatically attach an `AudioSource` component as well. 
   - Wait! Ensure the `AudioSource` has **Play On Awake** checked and its **Volume** is turned up.

## Step 3: Run!

Hit the **Play** button in Unity.
The system will automatically capture your microphone and screen (at 1 FPS), route it through the Cloud Run proxy to the Gemini API, and stream Gemini's replies back out through the `AudioReceiver` AudioSource in realtime.
Talk normally as you script or debug in the editor!

