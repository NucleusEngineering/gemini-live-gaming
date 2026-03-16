import asyncio
import os
import logging
from dotenv import load_dotenv
from gemini_live import GeminiLive

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

load_dotenv()
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
MODEL = os.getenv("MODEL", "gemini-2.5-flash-native-audio-preview-12-2025") # Defaulting to flash if not set

async def test_resumption():
    client = GeminiLive(api_key=GEMINI_API_KEY, model=MODEL, input_sample_rate=16000)
    
    audio_in = asyncio.Queue()
    video_in = asyncio.Queue()
    text_in = asyncio.Queue()
    
    async def dummy_audio_callback(data):
        pass

    resumption_token = None
    
    print("\n--- Phase 1: Establish Context ---")
    await text_in.put("My secret code is 12345. Remember it.")
    
    # We'll run for a short bit to get the token
    async for event in client.start_session(audio_in, video_in, text_in, dummy_audio_callback):
        if event.get("type") == "session_resumption":
            resumption_token = event.get("token")
            print(f"Captured token: {resumption_token[:10]}...")
        if event.get("type") == "text":
            print(f"Gemini: {event.get('data')}")
        if event.get("type") == "turn_complete":
            break
            
    if not resumption_token:
        print("FAILED: No resumption token received.")
        return

    print("\n--- Phase 2: Resume and Verify ---")
    # Reset queues
    text_in = asyncio.Queue()
    await text_in.put("What was my secret code?")
    
    found_answer = False
    async for event in client.start_session(audio_in, video_in, text_in, dummy_audio_callback, resumption_token=resumption_token):
        if event.get("type") == "text":
            text = event.get("data")
            print(f"Gemini: {text}")
            if "12345" in text:
                found_answer = True
        if event.get("type") == "turn_complete":
            break

    if found_answer:
        print("\nSUCCESS: Gemini remembered the context across sessions!")
    else:
        print("\nFAILED: Gemini did not remember the context.")

if __name__ == "__main__":
    asyncio.run(test_resumption())
