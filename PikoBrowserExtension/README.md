# Piko AI Completion Notifier (experimental)

This Chromium extension connects ChatGPT's response-generation state to the Piko desktop companion. It is intended for Chrome and Microsoft Edge.

## Install for local testing

1. Build and run the Piko desktop app first. Piko listens only on `127.0.0.1:17321`.
2. Open `chrome://extensions` in Chrome or `edge://extensions` in Edge.
3. Enable **Developer mode**.
4. Choose **Load unpacked** and select this `PikoBrowserExtension` folder.
5. Open or reload `https://chatgpt.com/` and submit a prompt.
6. When the visible generating control remains absent for about 1.8 seconds after being detected, the extension sends a small completion event to Piko. Piko wiggles its bar, widens its eyes, changes its status text, and plays a system chime unless sound is muted.

Use Piko's **Test Piko alert** button to test the animation and sound before relying on browser detection.

## Privacy

The content script only checks whether a ChatGPT generation/stop control is present. It does **not** read prompts, generated answers, conversation history, or page text, and it does not send conversation data to any remote server. The event contains only a fixed source label and a generic completion message. Communication with Piko goes to a loopback address on the same computer.

## Limitations

- This is an experimental DOM observer. ChatGPT can change its interface, which may require updating selectors.
- If the browser extension is installed or the page is reloaded in the middle of a response, detection may not always be accurate.
- The notification can also be triggered if generation stops early; the interface cannot always distinguish a completed answer from a user-stopped response.
- This version watches ChatGPT only. Gemini, Claude, and other AI websites are not yet supported.
- Keep the bridge token in this extension and `Piko/MainWindow.xaml.cs` in sync. The included token is for local development only and must be replaced before distributing a public release.
