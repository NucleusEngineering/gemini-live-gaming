import os
import asyncio
import json
import base64
import logging
from dotenv import load_dotenv
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from gemini_live import GeminiLive

# Load environment variables
load_dotenv()

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Configuration
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
MODEL = os.getenv("MODEL", "gemini-2.5-flash-native-audio-preview-12-2025")

@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    logger.info("Client connected.")
    
    # Extract resumption token from query params if present
    params = websocket.query_params
    initial_resumption_token = params.get("token")
    if initial_resumption_token:
        logger.info(f"Received resumption token from client: {initial_resumption_token[:10]}...")

    audio_input_queue = asyncio.Queue()
    video_input_queue = asyncio.Queue()
    text_input_queue = asyncio.Queue()

    async def audio_output_callback(data):
        # Convert audio bytes to base64 for Unity
        audio_b64 = base64.b64encode(data).decode('utf-8')
        await websocket.send_text(json.dumps({"type": "audio", "data": audio_b64}))

    gemini_client = GeminiLive(
        api_key=GEMINI_API_KEY, 
        model=MODEL, 
        input_sample_rate=16000
    )

    async def receive_from_unity():
        try:
            while True:
                msg = await websocket.receive_text()
                try:
                    payload = json.loads(msg)
                    msg_type = payload.get("type")
                    data = payload.get("data")
                    
                    if not data or not data.strip():
                        continue

                    if msg_type == "video":
                        # Decode base64 to bytes (JPEG)
                        raw_bytes = base64.b64decode(data)
                        await video_input_queue.put(raw_bytes)
                    elif msg_type == "audio":
                        # PCM 16kHz from Unity
                        raw_bytes = base64.b64decode(data)
                        await audio_input_queue.put(raw_bytes)
                    elif msg_type == "text":
                        await text_input_queue.put(data)
                except Exception as e:
                    logger.error(f"Error parsing Unity message: {e}")
        except WebSocketDisconnect:
            logger.info("Unity disconnected.")
        except Exception as e:
            logger.error(f"Error in receive_from_unity: {e}")

    receive_task = asyncio.create_task(receive_from_unity())

    async def run_session(resumption_token=None):
        while True:
            try:
                logger.info(f"Starting Gemini session (Resumption: {resumption_token is not None}).")
                start_time = asyncio.get_event_loop().time()
                async for event in gemini_client.start_session(
                    audio_input_queue=audio_input_queue,
                    video_input_queue=video_input_queue,
                    text_input_queue=text_input_queue,
                    audio_output_callback=audio_output_callback
                ):
                    if event:
                        if event.get("type") == "session_resumption":
                            token = event.get("token")
                            if token:
                                resumption_token = token
                                logger.info(f"Updated resumption token: {resumption_token[:10]}...")
                        
                        # Forward ALL events (including resumption) to client
                        await websocket.send_text(json.dumps(event))
                
                duration = asyncio.get_event_loop().time() - start_time
                logger.info(f"Gemini session ended after {duration:.2f}s. Restarting...")
            except Exception as e:
                logger.error(f"Error in Gemini session: {e}. Restarting in 1s...", exc_info=True)
                await asyncio.sleep(1)

    try:
        await run_session(resumption_token=initial_resumption_token)
    except asyncio.CancelledError:
        logger.info("Session task cancelled.")
    except Exception as e:
        logger.error(f"Top level session error: {e}")
    finally:
        receive_task.cancel()
        try:
            await websocket.close()
        except:
            pass

if __name__ == "__main__":
    import uvicorn
    port = int(os.getenv("PORT", 8080))
    uvicorn.run(app, host="0.0.0.0", port=port)
