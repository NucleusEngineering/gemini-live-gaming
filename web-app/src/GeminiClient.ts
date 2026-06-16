export interface GeminiEvent {
  type: "text" | "user" | "gemini" | "audio" | "interrupted" | "turn_complete" | "error" | "session_resumption";
  data?: string;
  text?: string;
  error?: string;
  token?: string;
}

export class GeminiClient {
  private ws: WebSocket | null = null;
  private audioContext: AudioContext | null = null;
  private micStream: MediaStream | null = null;
  private screenStream: MediaStream | null = null;
  private videoElement: HTMLVideoElement | null = null;
  private canvas: HTMLCanvasElement | null = null;
  private onEvent: (event: GeminiEvent) => void;
  private isRunning = false;
  private nextPlayTime = 0;
  private lastFrameData: string | null = null;
  public micMuted = false;

  constructor(onEvent: (event: GeminiEvent) => void, videoElement?: HTMLVideoElement) {
    this.onEvent = onEvent;
    if (videoElement) this.videoElement = videoElement;
  }

  public async start(url: string, token?: string | null) {
    if (this.isRunning) return;
    this.isRunning = true;

    try {
      const wsUrl = token ? `${url}?token=${encodeURIComponent(token)}` : url;
      this.ws = new WebSocket(wsUrl);
      this.ws.onmessage = (msg) => this.handleMessage(msg.data);
      this.ws.onclose = () => this.stop();
      this.ws.onerror = () => {
        this.onEvent({ type: "error", error: "WebSocket error" });
        this.stop();
      };

      await new Promise((resolve, reject) => {
        if (!this.ws) return reject();
        this.ws.onopen = resolve;
        setTimeout(() => reject(new Error("Timeout")), 5000);
      });

      this.audioContext = new AudioContext({ sampleRate: 24000 });
      this.nextPlayTime = this.audioContext.currentTime;

      await this.startMicrophone();
      await this.startScreenShare();
      this.startVideoStreaming();
    } catch (err) {
      console.error(err);
      this.onEvent({ type: "error", error: String(err) });
      this.stop();
    }
  }

  public stop() {
    this.isRunning = false;
    this.ws?.close();
    this.micStream?.getTracks().forEach((t) => t.stop());
    this.screenStream?.getTracks().forEach((t) => t.stop());
    this.audioContext?.close();
    this.ws = null;
    this.micStream = null;
    this.screenStream = null;
    this.audioContext = null;
    this.lastFrameData = null;
  }

  public sendText(text: string) {
    if (!this.isRunning || !this.ws || this.ws.readyState !== WebSocket.OPEN) {
      console.warn("Cannot send text: GeminiClient is not running or socket is not open");
      return;
    }
    this.ws.send(JSON.stringify({ type: "text", data: text }));
  }

  private arrayBufferToBase64(buffer: ArrayBuffer): string {
    let binary = '';
    const bytes = new Uint8Array(buffer);
    const len = bytes.byteLength;
    for (let i = 0; i < len; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
  }

  private async startMicrophone() {
    this.micStream = await navigator.mediaDevices.getUserMedia({
      audio: {
        sampleRate: 16000,
        channelCount: 1,
        echoCancellation: true,
        noiseSuppression: true,
      },
    });

    const micContext = new AudioContext({ sampleRate: 16000 });
    const source = micContext.createMediaStreamSource(this.micStream);
    const processor = micContext.createScriptProcessor(4096, 1, 1);

    source.connect(processor);
    processor.connect(micContext.destination);

    processor.onaudioprocess = (e) => {
      if (!this.isRunning || !this.ws || this.ws.readyState !== WebSocket.OPEN) return;
      if (this.micMuted) return;

      const inputData = e.inputBuffer.getChannelData(0);
      // Convert to 16-bit PCM
      const pcm16 = new Int16Array(inputData.length);
      for (let i = 0; i < inputData.length; i++) {
        pcm16[i] = Math.max(-1, Math.min(1, inputData[i])) * 0x7fff;
      }

      const base64 = this.arrayBufferToBase64(pcm16.buffer);
      this.ws.send(JSON.stringify({ type: "audio", data: base64 }));
    };
  }

  private async startScreenShare() {
    this.screenStream = await navigator.mediaDevices.getDisplayMedia({
      video: { frameRate: 5 },
      audio: false,
    });

    if (!this.videoElement) {
      this.videoElement = document.createElement("video");
    }
    this.videoElement.srcObject = this.screenStream;
    this.videoElement.play();

    this.canvas = document.createElement("canvas");
  }

  private startVideoStreaming() {
    const stream = async () => {
      if (!this.isRunning || !this.ws || this.ws.readyState !== WebSocket.OPEN || !this.screenStream || !this.videoElement || !this.canvas) return;

      const ctx = this.canvas.getContext("2d");
      if (ctx) {
        // Use original resolution
        const width = this.videoElement.videoWidth;
        const height = this.videoElement.videoHeight;

        this.canvas.width = width;
        this.canvas.height = height;
        ctx.drawImage(this.videoElement, 0, 0, width, height);

        const jpeg = this.canvas.toDataURL("image/jpeg", 0.7);
        const base64 = jpeg.split(",")[1];
        if (base64 && base64.length > 0) {
          if (base64 !== this.lastFrameData) {
            this.ws.send(JSON.stringify({ type: "video", data: base64 }));
            this.lastFrameData = base64;
          }
        } else {
          console.warn("Skipping empty video frame");
        }
      }

      if (this.isRunning) {
        setTimeout(stream, 1000); // 1 FPS
      }
    };
    stream();
  }

  private async handleMessage(data: string) {
    try {
      const msg = JSON.parse(data);
      if (msg.type === "audio") {
        this.playAudio(msg.data);
      } else if (msg.type === "interrupted") {
        this.interruptAudio();
        this.onEvent(msg);
      } else {
        this.onEvent(msg);
      }
    } catch (e) {
      console.error("Error parsing message", e);
    }
  }

  private async playAudio(base64: string) {
    if (!this.audioContext) return;

    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }

    const pcm16 = new Int16Array(bytes.buffer);
    const float32 = new Float32Array(pcm16.length);
    for (let i = 0; i < pcm16.length; i++) {
      float32[i] = pcm16[i] / 0x7fff;
    }

    const buffer = this.audioContext.createBuffer(1, float32.length, 24000);
    buffer.getChannelData(0).set(float32);

    const source = this.audioContext.createBufferSource();
    source.buffer = buffer;
    source.connect(this.audioContext.destination);

    // Dynamic playback time to avoid gaps
    const now = this.audioContext.currentTime;
    if (this.nextPlayTime < now) {
      this.nextPlayTime = now + 0.05; // Small buffer
    }

    source.start(this.nextPlayTime);
    this.nextPlayTime += buffer.duration;
  }

  private interruptAudio() {
    // In a real implementation, we would want to stop the currently playing source.
    // However, AudioBufferSourceNode is fire-and-forget unless we keep track of them.
    // For simplicity, we can just close and recreate the audio context or maintain a list.
    // Let's just reset the nextPlayTime for now.
    // Actually, it's better to keep track of active sources.

    // Simple approach: reset context
    if (this.audioContext) {
      this.audioContext.close();
      this.audioContext = new AudioContext({ sampleRate: 24000 });
      this.nextPlayTime = this.audioContext.currentTime;
    }
  }
}
