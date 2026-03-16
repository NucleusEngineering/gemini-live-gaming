import React, { useState, useEffect, useRef } from 'react';
import { GeminiClient } from './GeminiClient';
import type { GeminiEvent } from './GeminiClient';
import { Mic, Monitor, StopCircle, Play, AlertCircle, MessageSquare, Send } from 'lucide-react';
import './App.css';

interface Message {
  role: 'user' | 'gemini';
  text: string;
}

const App: React.FC = () => {
  const [isConnected, setIsConnected] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [currentGeminiText, setCurrentGeminiText] = useState('');
  const [currentUserText, setCurrentUserText] = useState('');
  const [inputText, setInputText] = useState('');
  const [error, setError] = useState<string | null>(null);

  const clientRef = useRef<GeminiClient | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const onEventRef = useRef<(event: GeminiEvent) => void>(() => { });

  // Refs to track current transcription state for deterministic handling
  const currentUserTextRef = useRef('');
  const currentGeminiTextRef = useRef('');

  const aggregateSentence = (prev: string, next: string) => {
    if (next.startsWith(prev)) return next;
    return prev + next;
  };

  useEffect(() => {
    onEventRef.current = (event: GeminiEvent) => {
      if (event.type === 'error') {
        setError(event.error || 'Unknown error');
        setIsConnected(false);
      } else if (event.type === 'user') {
        currentUserTextRef.current = aggregateSentence(currentUserTextRef.current, event.text || '');
        setCurrentUserText(currentUserTextRef.current);
      } else if (event.type === 'gemini') {
        currentGeminiTextRef.current = aggregateSentence(currentGeminiTextRef.current, event.text || '');
        setCurrentGeminiText(currentGeminiTextRef.current);
      } else if (event.type === 'interrupted') {
        currentGeminiTextRef.current = '';
        setCurrentGeminiText('');
        setMessages(prev => [...prev, { role: 'gemini', text: '[Interrupted]' }]);
      } else if (event.type === 'session_resumption') {
        if (event.token) {
          localStorage.setItem('gemini_resumption_token', event.token);
          console.log("Saved resumption token to localStorage");
        }
      } else if (event.type === 'turn_complete') {
        // Move current texts to messages only once
        const userText = currentUserTextRef.current.trim();
        const geminiText = currentGeminiTextRef.current.trim();

        if (userText || geminiText) {
          setMessages(prev => {
            const next = [...prev];
            // Deduplicate: avoid adding the exact same user message twice in a row
            const lastMsg = next.length > 0 ? next[next.length - 1] : null;

            if (userText && !(lastMsg && lastMsg.role === 'user' && lastMsg.text === userText)) {
              next.push({ role: 'user', text: userText });
            }
            if (geminiText) {
              next.push({ role: 'gemini', text: geminiText });
            }
            return next;
          });

          // Clear current transcription state
          currentUserTextRef.current = '';
          currentGeminiTextRef.current = '';
          setCurrentUserText('');
          setCurrentGeminiText('');
        }
      }
    };
  }, []);

  useEffect(() => {
    if (!clientRef.current) {
      clientRef.current = new GeminiClient((ev) => onEventRef.current(ev), videoRef.current || undefined);
    }
    return () => {
      clientRef.current?.stop();
    };
  }, []);

  const handleStart = async () => {
    setError(null);
    try {
      const url = "ws://localhost:8080/ws";
      const token = localStorage.getItem('gemini_resumption_token');
      await clientRef.current?.start(url, token);
      setIsConnected(true);
      setMessages([]);
    } catch {
      setError("Failed to start session. Is the backend running?");
    }
  };

  const handleStop = () => {
    clientRef.current?.stop();
    setIsConnected(false);
  };

  const handleSendText = () => {
    if (inputText.trim() && isConnected) {
      clientRef.current?.sendText(inputText);
      setInputText('');
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleSendText();
    }
  };

  return (
    <div className="app-container">
      <header>
        <h1>Antigravity <span>Live</span></h1>
        <div className="status-badge" data-connected={isConnected}>
          {isConnected ? "Live Session" : "Disconnected"}
        </div>
      </header>

      <main>
        <div className="main-content">
          <div className="chat-window">
            <div className="messages-list">
              {messages.map((m, i) => (
                <div key={i} className={`message role-${m.role}`}>
                  <div className="avatar">
                    {m.role === 'user' ? <Mic size={16} /> : <MessageSquare size={16} />}
                  </div>
                  <div className="text">{m.text}</div>
                </div>
              ))}

              {(currentUserText || currentGeminiText) && (
                <div className="live-transcriptions">
                  {currentUserText && (
                    <div className="message role-user live">
                      <div className="avatar"><Mic size={16} /></div>
                      <div className="text">{currentUserText}</div>
                    </div>
                  )}
                  {currentGeminiText && (
                    <div className="message role-gemini live">
                      <div className="avatar"><MessageSquare size={16} /></div>
                      <div className="text">{currentGeminiText}</div>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>

          <div className="video-preview-container">
            <video ref={videoRef} autoPlay playsInline muted />
            {!isConnected && <div className="video-placeholder">Screen Share Preview</div>}
          </div>
        </div>

        {error && (
          <div className="error-toast">
            <AlertCircle size={20} />
            <span>{error}</span>
          </div>
        )}

        {isConnected && (
          <div className="message-input-container">
            <input
              type="text"
              placeholder="Type a message to Gemini..."
              value={inputText}
              onChange={(e) => setInputText(e.target.value)}
              onKeyPress={handleKeyPress}
            />
            <button className="btn-send" onClick={handleSendText} disabled={!inputText.trim()}>
              <Send size={18} />
            </button>
          </div>
        )}

        <div className="controls">
          {!isConnected ? (
            <button className="btn-primary" onClick={handleStart}>
              <Play size={20} />
              <span>Start Session</span>
            </button>
          ) : (
            <button className="btn-secondary" onClick={handleStop}>
              <StopCircle size={20} />
              <span>Stop Session</span>
            </button>
          )}

          <div className="indicators">
            <div className={`ind ${isConnected ? 'active' : ''}`} title="Microphone">
              <Mic size={20} />
            </div>
            <div className={`ind ${isConnected ? 'active' : ''}`} title="Screen Share">
              <Monitor size={20} />
            </div>
          </div>
        </div>
      </main>

      <footer>
        <p>Built with Gemini 2.0 Multimodal Live API</p>
      </footer>
    </div>
  );
};

export default App;
