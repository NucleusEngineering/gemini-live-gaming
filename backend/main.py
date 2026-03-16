import os
import asyncio
import json
import base64
import logging
from dotenv import load_dotenv
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from google import genai
from google.genai import types
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

# Project Root for RAG tools
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def get_project_context() -> str:
    """
    Lists the files available in the project for the assistant to read.
    """
    files_list = []
    for root, dirs, files in os.walk(PROJECT_ROOT):
        # Skip hidden directories and node_modules
        dirs[:] = [d for d in dirs if not d.startswith('.') and d != 'node_modules']
        for file in files:
            if not file.startswith('.'):
                rel_path = os.path.relpath(os.path.join(root, file), PROJECT_ROOT)
                files_list.append(rel_path)
    
    return "Available project files:\n" + "\n".join(files_list)

def read_project_file(file_path: str) -> str:
    """
    Reads the content of a project file.
    """
    abs_path = os.path.join(PROJECT_ROOT, file_path)
    # Security check: ensure the path is within PROJECT_ROOT
    if not os.path.abspath(abs_path).startswith(os.path.abspath(PROJECT_ROOT)):
        return "Error: Access denied. Path outside of project root."
    
    if not os.path.exists(abs_path):
        return f"Error: File not found: {file_path}"
    
    try:
        with open(abs_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return f"Error reading file: {e}"

def ensure_file_search_store(client: genai.Client, store_name: str, file_path: str) -> str:
    """
    Ensures a File Search Store exists and contains the specified file.
    """
    try:
        # 1. Check if store exists
        store = None
        for s in client.file_search_stores.list():
            if s.display_name == store_name:
                store = s
                break
        
        # 2. Create if not exists
        if not store:
            logger.info(f"Creating new File Search Store: {store_name}")
            store = client.file_search_stores.create(config={"display_name": store_name})
        
        # 3. Sync file
        abs_file_path = os.path.join(PROJECT_ROOT, file_path)
        if os.path.exists(abs_file_path):
            file_name = os.path.basename(file_path)
            
            logger.info(f"Uploading {file_path} to Gemini Files...")
            uploaded_file = client.files.upload(file=abs_file_path, config={"display_name": file_name})
            file_uri = uploaded_file.name

            # Check if file is in store
            file_in_store = False
            # Note: file_search_stores.files.list() might not be available directly in all versions, 
            # but we can try to add the file to the store anyway, it should be idempotent or we can check.
            # For simplicity, we add it. 
            logger.info(f"Adding file {file_name} to store {store_name}")
            try:
                client.file_search_stores.import_file(
                    file_search_store_name=store.name,
                    file_name=file_uri
                )
            except Exception as e:
                # If already exists, it might error. We can ignore if it's already there.
                if "already exists" in str(e).lower():
                    pass
                else:
                    logger.warning(f"Note on adding file to store: {e}")

        return store.name
    except Exception as e:
        logger.error(f"Error in ensure_file_search_store: {e}")
        return None

def control_movement(action: str) -> str:
    """
    Controls the character movement in Unity.
    Args:
        action (str): The action to perform. Can be "START_FORWARD", "JUMP", or "STOP".
    """
    logger.info(f"Movement action triggered: {action}")
    return f"Movement action '{action}' triggered."

# Prepare System Instruction with README context
base_instruction = """
You are UniAgent, a helpful Unity game development assistant.
You are directly connected to the user's Unity Editor. 
You can see their screen (viewport) and hear their voice. 
Help them with debugging, writing C# scripts, understanding shaders, and navigating the Unity interface.
Keep responses concise and helpful.

You have access to the project's file structure and can read files using the provided tools.
Use 'get_project_context' to see what files are available and 'read_project_file' to study their implementation.

You can also control the game movement. 
When the user says "move forward", call 'control_movement' with action "START_FORWARD".
When the user says "Jump", call 'control_movement' with action "JUMP".
When the user says "Stop movement", call 'control_movement' with action "STOP".
Always confirm to the user that you are triggering the movement.
"""

try:
    readme_path = os.path.join(PROJECT_ROOT, "README.md")
    if os.path.exists(readme_path):
        with open(readme_path, 'r', encoding='utf-8') as f:
            readme_content = f.read()
        system_instruction = f"{base_instruction}\n\nProject Documentation (README.md):\n{readme_content}"
    else:
        system_instruction = base_instruction
except Exception as e:
    logger.error(f"Error reading README: {e}")
    system_instruction = base_instruction

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

    # Initialize client and ensure store
    client = genai.Client(api_key=GEMINI_API_KEY)
    store_name = "GamedevLiveStore"
    storyline_path = "secret-documentation-data/STORYLINE.md"
    
    # Run store management in a thread to not block the async loop if needed, 
    # but here it's fine as we are at the start of the websocket connection.
    loop = asyncio.get_running_loop()
    actual_store_name = await loop.run_in_executor(None, ensure_file_search_store, client, store_name, storyline_path)
    
    file_search_store_names = [actual_store_name] if actual_store_name else None

    async def audio_output_callback(data):
        # Convert audio bytes to base64 for Unity
        audio_b64 = base64.b64encode(data).decode('utf-8')
        await websocket.send_text(json.dumps({"type": "audio", "data": audio_b64}))

    gemini_client = GeminiLive(
        api_key=GEMINI_API_KEY, 
        model=MODEL, 
        input_sample_rate=16000,
        system_instruction=system_instruction,
        tools=[get_project_context, read_project_file, control_movement],
        tool_mapping={
            "get_project_context": get_project_context,
            "read_project_file": read_project_file,
            "control_movement": control_movement
        },
        file_search_store_names=file_search_store_names
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
