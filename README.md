<div align="center">

# 🦊 Piko — Dynamic Island AI Desktop Companion

**Your Living Desktop Buddy & Productivity Co-Pilot for Windows 10 & 11**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6?logo=windows&logoColor=white)](https://github.com/abdurrahmanjibrin/Piko-Desktop-Companion)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Browser Extension](https://img.shields.io/badge/Browser%20Extension-Chrome%20%7C%20Edge%20(MV3)-4285F4?logo=google-chrome&logoColor=white)](https://github.com/abdurrahmanjibrin/Piko-Desktop-Companion)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-17%20Passed-success)](#-unit-tests)

[🇮🇩 **Baca Panduan Bahasa Indonesia (README.id.md)**](README.id.md) · [✨ Features](#-key-features) · [📥 Installation](#-installation--setup) · [📅 Google Calendar Setup](#-google-calendar-setup) · [⚡ AI Bridge](#-ai-bridge-antigravity--chatgpt--gemini)

</div>

---

## 📖 Overview

**Piko** is a lightweight, non-intrusive, interactive desktop companion inspired by Apple's Dynamic Island. Resting quietly as a sleek pill notch at the top center of your Windows screen, Piko springs to life with expressive eye animations when you work, study, or wait for AI to generate responses.

Piko unifies your daily workflow into a gorgeous **Obsidian & Lavender Glassmorphic** dashboard:
- ⚡ **Multi-AI Monitor**: Real-time notifications for ChatGPT, Gemini, Claude, DeepSeek, and Google Antigravity IDE.
- 📅 **Google Calendar Sync**: View multi-account events with 1-click Google Meet join links.
- ⏱️ **Auto-Interval Pomodoro**: 25m focus & 5m break loops without distracting tomato icons.
- 💧 **Smart Hydration Reminder**: Gentle reminders to drink water with island tap confirmation.
- 📁 **Smart Storage Organizer**: Safe rule-based folder organization with 100% reversible undo.
- 🎵 **Live Media Bar**: YouTube and media playback status, controls, and volume.
- 📎 **Temporary Shelf**: Drag-and-drop temporary workspace for quick file access.

---

## 🌟 Key Features

### 1. Dynamic Island Notch & Vector Eye Engine
- **Floating Pill Capsule:** Docked borderless at the top of your screen, always on top (`Topmost`), smooth expanding and collapsing animations.
- **Expressive Living Eyes:** Procedural vector eyes that track cursor position, blink naturally, and react dynamically (`Idle`, `Hover`, `Thinking`, `Working`, `Success`, `Alert`, `Sleepy`).

### 2. Multi-AI Bridge (Simultaneous AI Monitoring)
- Watches generation status across multiple AI tools concurrently:
  - **Browser Extension:** Detects ChatGPT, Gemini, Claude, and DeepSeek running in Chrome/Edge.
  - **Google Antigravity IDE:** Built-in local watcher service monitors local session activity automatically.
- **Concurrent Tracking:** If ChatGPT and Antigravity are generating at the same time, Piko displays `[ChatGPT + Antigravity] sedang berpikir...` and notifies you when each finishes.
- **Zero Privacy Leakage:** The bridge communicates strictly over local loopback (`127.0.0.1:17321`). No prompts, conversations, or personal data are ever read or transmitted.

### 3. Google Calendar Multi-Account Synchronization
- Connect unlimited Google accounts using **OAuth 2.0** or **Private iCal URL**.
- Clean daily schedule timeline with account color tags, time slots, and direct **Google Meet** join buttons.
- **100% Private:** Your tokens and calendar links are saved exclusively in your local machine's `%LOCALAPPDATA%\Piko\state.json`. Zero credentials in git!

### 4. Auto-Interval Pomodoro Technique
- Follows the official 4-cycle Pomodoro method:
  - **25 Min Work Session** ➔ **5 Min Short Break**
  - After 4 work sessions ➔ **15–30 Min Long Break**
- Full playback controls: Play (▶), Pause (⏸), Skip (⏭), Reset (↺).
- Customizable task labels and celebratory sound chimes upon completion.

### 5. Smart Hydration Assistant
- Configurable reminder interval (e.g., every 30, 45, or 60 minutes).
- When it's time to drink, Piko's island gently expands with a water notification and chime.
- Simply tap the island capsule to record your water intake and dismiss the reminder.

### 6. Safe AI Storage Assistant & Organizer
- Scans user directories (Downloads, Documents, Desktop, Pictures, Videos) for clutter.
- Categorizes files into 6 types: Documents, Images, Media, Code, Archives, Others.
- Custom target folder rules (e.g. files with `"Tugas"` go directly to the `Tugas/` folder).
- **Absolute Safety:** Never deletes files, automatically resolves name conflicts, and provides **1-Click Reversible Undo**.

---

## 💻 System Requirements

| Component | Requirement |
| :--- | :--- |
| **Operating System** | Windows 10 (Build 19041+) or Windows 11 (64-bit) |
| **Runtime** | [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (Included if using Self-Contained Release) |
| **Browser (Optional)** | Google Chrome, Microsoft Edge, Brave, or any Chromium browser |
| **Screen Resolution** | 1280 × 720 or higher recommended |

---

## 📥 Installation & Setup

### Option A: Quickstart (Pre-built Executable — Recommended for Users)

1. Download the latest `Piko-Desktop-Companion-v*.zip` from the [Releases](https://github.com/abdurrahmanjibrin/Piko-Desktop-Companion/releases) page.
2. Extract the ZIP folder to any convenient location (e.g. `C:\Users\<YourUser>\AppData\Local\Piko` or `D:\Piko`).
3. Right-click `Piko.exe` ➔ **Create shortcut** and move it to your Desktop.
4. Double-click `Piko.exe` to launch!

---

### Option B: Build from Source (For Developers)

#### 1. Clone Repository
```powershell
git clone https://github.com/abdurrahmanjibrin/Piko-Desktop-Companion.git
cd Piko-Desktop-Companion/PikoStarter
```

#### 2. Build Solution
Make sure you have the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed:
```powershell
# Build in Release mode
dotnet build Piko/Piko.csproj -c Release

# Run Piko
dotnet run --project Piko/Piko.csproj
```

#### 3. Publish Standalone Portable Package
To produce a portable standalone executable without requiring .NET SDK:
```powershell
dotnet publish Piko/Piko.csproj -c Release -r win-x64 --self-contained false -o ./publish
```

---

## 🧩 Browser Extension Setup (Chrome / Edge)

The browser extension bridges ChatGPT, Gemini, and YouTube with Piko:

1. Launch Piko on your desktop first (the local bridge runs at `127.0.0.1:17321`).
2. Open your Chromium browser and navigate to:
   - Chrome: `chrome://extensions`
   - Edge: `edge://extensions`
   - Brave: `brave://extensions`
3. Toggle on **Developer mode** (top-right or top-left corner).
4. Click **Load unpacked** (*Muat yang belum dibongkar*).
5. Select the `PikoStarter/PikoBrowserExtension` folder.
6. Pin the Piko extension icon. When connected, the badge will show `ON` with green status!

---

## 📅 Google Calendar Setup

Piko supports two easy methods to connect your Google Calendar. **All credentials and URLs are stored strictly on your local PC (`%LOCALAPPDATA%\Piko\state.json`) and never shared or uploaded.**

### Method 1: Private iCal Feed (Easiest — No API Setup Required!)

1. Open [Google Calendar](https://calendar.google.com/) in your browser.
2. In the left sidebar, hover over your calendar, click the **three dots (⋮)** ➔ **Settings and sharing**.
3. Scroll down to the **Integrate calendar** section.
4. Copy the URL labeled **Secret address in iCal format** (`https://calendar.google.com/calendar/ical/.../basic.ics`).
5. In Piko, click the Dynamic Island to open the dashboard ➔ switch to the **Calendar** tab.
6. Click **＋ Tambah Akun Google** ➔ choose **Metode iCal URL**.
7. Paste your Secret iCal URL and choose your preferred account color badge. Click **Simpan & Sinkronkan**.
8. Your upcoming events and Google Meet links will appear immediately!

---

### Method 2: Google Cloud OAuth 2.0 (Full Official Sync)

1. Open the [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project (e.g. `Piko Calendar`).
3. Enable the **Google Calendar API** under **APIs & Services** ➔ **Library**.
4. Configure the **OAuth Consent Screen**:
   - User Type: **External**
   - App Name: `Piko Desktop Companion`
   - Test Users: Add your Google email address.
5. Create Credentials:
   - Go to **APIs & Services** ➔ **Credentials** ➔ **Create Credentials** ➔ **OAuth Client ID**.
   - Application Type: **Desktop app**.
6. Copy your **Client ID** and **Client Secret**.
7. In Piko, open the **Calendar** tab ➔ click **＋ Tambah Akun Google** ➔ select **Metode OAuth 2.0**.
8. Enter your Client ID and Client Secret, then click **Hubungkan dengan Google**. A secure browser prompt will let you authorize your calendar.

---

## ⚡ AI Bridge (Antigravity & ChatGPT & Gemini)

### Google Antigravity IDE
Piko automatically detects when Google Antigravity IDE is generating answers or running tools by monitoring local conversation state changes. No extra plugins are required—simply keep Piko running while using Antigravity!

### ChatGPT, Gemini, Claude, DeepSeek
Install the included [Browser Extension](#-browser-extension-setup-chrome--edge). When you submit a prompt:
- Piko's eyes switch to `Working` animation.
- Dynamic Island displays which AI is thinking.
- When generation finishes, Piko plays a pleasant chime and presents a completion notification.

---

## 🧪 Unit Tests

Piko includes an extensive xUnit test suite covering the local bridge protocol, storage classifier, file deduplication, reversible undo, and state transitions:

```powershell
dotnet test PikoStarter/Piko.Tests/Piko.Tests.csproj
```
```text
Passed!  - Failed: 0, Passed: 17, Skipped: 0, Total: 17
```

---

## 📂 Project Structure

```text
PikoStarter/
├── Piko/                               # Main WPF Desktop Companion (.NET 8)
│   ├── App.xaml / App.xaml.cs          # Single instance mutex & app lifecycle
│   ├── MainWindow.xaml / .cs           # Dynamic Island, eye engine & 9-tab dashboard
│   ├── Models/                         # AppState, PikoEmotions, Event models
│   ├── Services/
│   │   ├── AntigravityWatcherService.cs# Local Antigravity IDE generation monitor
│   │   ├── GoogleCalendarService.cs    # OAuth 2.0 & iCal calendar synchronization
│   │   ├── LocalBridgeService.cs       # Secure loopback bridge (127.0.0.1:17321)
│   │   ├── PikoEyeController.cs        # Procedural vector eye kinematics & expressions
│   │   ├── StatePersistenceService.cs  # %LOCALAPPDATA%\Piko persistence
│   │   └── StorageOrganizerService.cs  # Rule-based organizer & conflict resolver
│   ├── PikoLauncher.vbs                # Portable silent launcher script
│   └── Piko.csproj                     # WPF Project definition
├── PikoBrowserExtension/               # Chromium Extension (Manifest V3)
│   ├── manifest.json                   # MV3 definition
│   ├── content.js                      # ChatGPT, Gemini, Claude DOM watcher
│   ├── media_content.js                # YouTube & media playback bridge
│   └── service_worker.js               # Background loopback relay to desktop
├── Piko.Tests/                         # xUnit Test Project (17 unit tests)
│   ├── UnitTest1.cs                    # Business logic & bridge validation tests
│   └── Piko.Tests.csproj
├── README.md                           # English documentation
└── README.id.md                        # Panduan lengkap Bahasa Indonesia
```

---

## 🔒 Privacy & Security

- **Local-Only Persistence:** Your settings, tasks, and Google credentials are saved solely on your local drive at:
  ```text
  %LOCALAPPDATA%\Piko\state.json
  ```
- **No Analytics / Telemetry:** Piko does not transmit telemetry or user statistics.
- **Loopback-Only Bridge:** The local bridge binds strictly to IPv4 `127.0.0.1:17321` and rejects non-local or untokenized connections.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE). Contributions, bug reports, and suggestions are warmly welcomed!
