const PIKO_BRIDGE_URL = 'http://127.0.0.1:17321/event';
const PIKO_COMMAND_URL = 'http://127.0.0.1:17321/command';
// Keep in sync with BridgeToken in Piko local bridge.
const PIKO_BRIDGE_TOKEN = 'piko-dev-bridge-token-2026';

let lastFallbackNotificationAt = 0;

// Registry of detected media tabs: tabId -> { tabId, playbackState, title, channel, videoId, lastUpdated }
const mediaTabs = new Map();
let lastPlayingTabId = null;

// Track tab closures to avoid routing commands to closed tabs
if (typeof chrome !== 'undefined' && chrome.tabs && chrome.tabs.onRemoved) {
  chrome.tabs.onRemoved.addListener((closedTabId) => {
    mediaTabs.delete(closedTabId);
    if (lastPlayingTabId === closedTabId) {
      lastPlayingTabId = null;
      for (const [id, info] of mediaTabs.entries()) {
        if (info.playbackState === 'playing') {
          lastPlayingTabId = id;
          break;
        }
      }
    }
  });
}

function resolveTargetTab(requestedTabId) {
  // 1. If a specific tab ID was requested and it is still recorded, use it
  if (requestedTabId && mediaTabs.has(requestedTabId)) {
    return requestedTabId;
  }

  // 2. Tab with currently playing media
  if (lastPlayingTabId && mediaTabs.has(lastPlayingTabId) && mediaTabs.get(lastPlayingTabId).playbackState === 'playing') {
    return lastPlayingTabId;
  }
  for (const [id, info] of mediaTabs.entries()) {
    if (info.playbackState === 'playing') {
      lastPlayingTabId = id;
      return id;
    }
  }

  // 3. Most recently updated media tab (e.g. paused)
  let latestTabId = null;
  let latestTime = 0;
  for (const [id, info] of mediaTabs.entries()) {
    if (info.lastUpdated > latestTime) {
      latestTime = info.lastUpdated;
      latestTabId = id;
    }
  }
  if (latestTabId) return latestTabId;

  // 4. Fallback to requestedTabId if passed
  return requestedTabId || null;
}

function reportCommandFailure(commandId, action, errorMsg) {
  const resultPayload = {
    type: 'media_command_result',
    token: PIKO_BRIDGE_TOKEN,
    source: 'YouTube',
    title: 'Media',
    channel: '',
    commandId: commandId || '',
    commandAction: action || '',
    commandSuccess: false,
    errorMessage: errorMsg || 'Target tab tidak ditemukan',
    playbackState: 'stopped',
    tabId: null,
    timestamp: Date.now()
  };

  fetch(PIKO_BRIDGE_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json;charset=UTF-8' },
    body: JSON.stringify(resultPayload),
    cache: 'no-store'
  }).catch(() => {});
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || !message.type) return false;

  // 1. Content Script polling bridge commands via service worker (avoids webpage CSP & mixed-content blocks)
  if (message.type === 'PIKO_POLL_COMMANDS') {
    fetch(PIKO_COMMAND_URL, {
      method: 'GET',
      cache: 'no-store'
    }).then(async response => {
      if (response.ok) {
        const data = await response.json().catch(() => null);
        if (data && data.ok && data.command && data.command.Action) {
          const cmd = data.command;
          const senderTabId = sender && sender.tab ? sender.tab.id : null;
          const targetTabId = resolveTargetTab(cmd.TabId);

          if (!targetTabId) {
            console.warn('[Piko ServiceWorker] No valid YouTube tab found to execute command:', cmd.Action);
            reportCommandFailure(cmd.Id, cmd.Action, 'Tidak ada tab YouTube aktif yang ditemukan di Chrome');
            sendResponse({ ok: true, command: null });
            return;
          }

          console.log(`[Piko ServiceWorker] Routing command "${cmd.Action}" (id=${cmd.Id}) to target tab ${targetTabId}`);

          // If sender tab is the resolved target tab, deliver via polling response
          if (senderTabId === targetTabId) {
            sendResponse({ ok: true, command: cmd });
            return;
          }

          // Otherwise dispatch directly to target tab via chrome.tabs.sendMessage
          if (typeof chrome !== 'undefined' && chrome.tabs && chrome.tabs.sendMessage) {
            chrome.tabs.sendMessage(targetTabId, { type: 'PIKO_EXECUTE_COMMAND', command: cmd }, () => {
              if (chrome.runtime.lastError) {
                console.warn('[Piko ServiceWorker] Failed to push command to tab:', chrome.runtime.lastError.message);
                reportCommandFailure(cmd.Id, cmd.Action, `Gagal mengirim ke tab ${targetTabId}: ${chrome.runtime.lastError.message}`);
              }
            });
          }

          sendResponse({ ok: true, command: null });
          return;
        }
      }
      sendResponse({ ok: true, command: null });
    }).catch(err => {
      sendResponse({ ok: false, command: null, error: err.message });
    });

    return true; // Keep channel open for async response
  }

  // 2. Command execution confirmation from content script
  if (message.type === 'PIKO_COMMAND_RESULT') {
    const tabId = (sender && sender.tab && sender.tab.id) != null ? sender.tab.id : (message.tabId || null);
    if (tabId != null && mediaTabs.has(tabId)) {
      const entry = mediaTabs.get(tabId);
      entry.playbackState = message.playbackState || (message.success ? (message.action === 'pause' ? 'paused' : 'playing') : entry.playbackState);
      entry.lastUpdated = Date.now();
      if (entry.playbackState === 'playing') lastPlayingTabId = tabId;
    }

    const resultPayload = {
      type: 'media_command_result',
      token: PIKO_BRIDGE_TOKEN,
      source: message.source || 'YouTube',
      title: message.title || 'Media',
      channel: message.channel || '',
      commandId: message.commandId || '',
      commandAction: message.action || '',
      commandSuccess: !!message.success,
      errorMessage: message.errorMessage || '',
      playbackState: message.playbackState || (message.success ? (message.action === 'pause' ? 'paused' : 'playing') : ''),
      tabId: tabId,
      timestamp: Date.now()
    };

    console.log(`[Piko ServiceWorker] Forwarding command confirmation:`, resultPayload.commandAction, resultPayload.commandSuccess, resultPayload.playbackState);

    fetch(PIKO_BRIDGE_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json;charset=UTF-8' },
      body: JSON.stringify(resultPayload),
      cache: 'no-store'
    }).then(res => {
      sendResponse({ ok: res.ok });
    }).catch(err => {
      console.warn('[Piko ServiceWorker] Failed to deliver command result to bridge:', err);
      sendResponse({ ok: false, error: err.message });
    });

    return true;
  }

  // 3. Regular notification & playback status forwarding
  const validTypes = {
    'PIKO_AI_START': 'ai-start',
    'PIKO_AI_COMPLETE': 'ai-complete',
    'PIKO_AI_CANCEL': 'ai-cancel',
    'PIKO_MEDIA_PLAYING': 'media_playing',
    'PIKO_MEDIA_PAUSED': 'media_paused',
    'PIKO_MEDIA_STOPPED': 'media_stopped',
    'PIKO_MEDIA_METADATA': 'media_metadata_changed',
    'PIKO_MEDIA_PROGRESS': 'media_progress_updated',
    'PIKO_MEDIA_COMMAND_RESULT': 'media_command_result'
  };

  const bridgeType = validTypes[message.type];
  if (!bridgeType) return false;

  // Resilient origin extraction from sender, tab, or message source
  const sourceUrl = (sender && (sender.url || (sender.tab && sender.tab.url))) || message.pageUrl || '';
  const isChatGpt = sourceUrl.includes('chatgpt.com') || sourceUrl.includes('chat.openai.com') || message.source === 'ChatGPT';
  const isYouTube = sourceUrl.includes('youtube.com') || (message.source && message.source.includes('YouTube'));

  if (!isChatGpt && !isYouTube) {
    console.warn('[Piko ServiceWorker] Rejected event from unauthorized origin:', sourceUrl, message);
    return false;
  }

  const tabId = (sender && sender.tab && sender.tab.id) != null ? sender.tab.id : (message.tabId || null);

  if (tabId != null && isYouTube) {
    const currentState = message.playbackState || (bridgeType === 'media_playing' ? 'playing' : (bridgeType === 'media_paused' ? 'paused' : 'stopped'));
    mediaTabs.set(tabId, {
      tabId: tabId,
      playbackState: currentState,
      title: message.title || '',
      channel: message.channel || '',
      videoId: message.videoId || '',
      lastUpdated: Date.now()
    });
    if (currentState === 'playing') {
      lastPlayingTabId = tabId;
    }
  }

  const payload = {
    type: bridgeType,
    token: PIKO_BRIDGE_TOKEN,
    source: message.source || (isYouTube ? 'YouTube' : 'ChatGPT'),
    title: message.title || (isYouTube ? 'Media' : 'ChatGPT event'),
    channel: message.channel || '',
    videoId: message.videoId || '',
    thumbnailUrl: message.thumbnailUrl || '',
    pageUrl: message.pageUrl || sourceUrl || '',
    duration: typeof message.duration === 'number' ? message.duration : null,
    currentTime: typeof message.currentTime === 'number' ? message.currentTime : null,
    playbackState: message.playbackState || (bridgeType === 'media_playing' ? 'playing' : (bridgeType === 'media_paused' ? 'paused' : 'stopped')),
    tabId: tabId,
    timestamp: message.timestamp || Date.now()
  };

  console.log(`[Piko ServiceWorker] Forwarding event to bridge:`, bridgeType, payload.title, payload.channel);

  fetch(PIKO_BRIDGE_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json;charset=UTF-8' },
    body: JSON.stringify(payload),
    cache: 'no-store'
  }).then(response => {
    if (response.ok) {
      console.log(`[Piko ServiceWorker] Bridge event delivered successfully: status ${response.status}`);
    } else {
      console.warn(`[Piko ServiceWorker] Bridge responded with error status ${response.status}`);
    }
  }).catch(error => {
    console.warn('[Piko ServiceWorker] Desktop app bridge unreachable on loopback 127.0.0.1:17321:', error);

    // Only show fallback Chrome notification once every 60 seconds if complete event
    const now = Date.now();
    if (bridgeType === 'ai-complete' && now - lastFallbackNotificationAt > 60000) {
      lastFallbackNotificationAt = now;
      try {
        chrome.notifications.create(`piko-bridge-fallback-${now}`, {
          type: 'basic',
          iconUrl: 'icons/piko-128.png',
          title: 'Piko Desktop Companion',
          message: 'Piko tidak terhubung di localhost. Buka aplikasi Piko untuk melihat animasi notch.'
        });
      } catch (notifErr) {
        // Notification permission may not be active
      }
    }
  });

  return true;
});
