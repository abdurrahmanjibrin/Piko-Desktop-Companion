# Piko AI Completion Notifier

This Chromium extension connects response-generation states from **ChatGPT**, **Google Gemini**, **Claude**, and **DeepSeek** (as well as **YouTube** media playback) to the Piko desktop companion. It is compatible with Google Chrome, Microsoft Edge, and Brave.

## Supported AI Providers
- **ChatGPT** (`chatgpt.com`, `chat.openai.com`)
- **Google Gemini** (`gemini.google.com`)
- **Claude** (`claude.ai`)
- **DeepSeek** (`chat.deepseek.com`, `deepseek.com`)

## Install for local testing

1. Build and run the Piko desktop app first. Piko listens locally on `127.0.0.1:17321`.
2. Open `chrome://extensions` in Chrome or `edge://extensions` in Edge.
3. Enable **Developer mode** toggle.
4. Click **Load unpacked** and select this `PikoBrowserExtension` folder. If already installed, click the **Reload (↻)** icon.
5. Open ChatGPT, Gemini, Claude, or DeepSeek and submit a prompt.
6. When the AI finishes generating, Piko's Dynamic Island instantly expands, plays a chime (if sound is enabled), and notifies you that the answer is ready ✨.

You can also use Piko's **Test Piko alert** button in the AI Bridge tab to test notifications for all four AI providers.

## Privacy & Security

- **Strictly privacy-preserving**: The content script only checks whether generation/stop control elements are visible in the DOM.
- **Zero data collection**: It **NEVER** reads prompt text, response bodies, conversation history, or personal data.
- **Loopback only**: All events are delivered strictly to `127.0.0.1:17321` on the local machine with token authentication. No remote tracking servers are used.
