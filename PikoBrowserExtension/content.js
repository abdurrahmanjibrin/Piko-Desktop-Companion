(() => {
  // Piko AI Completion Notifier - Content Script
  // Strictly privacy-preserving: detects generation state controls only.
  // NEVER reads prompt text, response bodies, chat history, or personal data.
  // Supports ChatGPT, Google Gemini, Claude, and DeepSeek.

  if (typeof window !== 'undefined' && window.__pikoContentCleanup) {
    try { window.__pikoContentCleanup(); } catch {}
  }

  function isExtensionValid() {
    try {
      return typeof chrome !== 'undefined' &&
             !!chrome.runtime &&
             !!chrome.runtime.id &&
             typeof chrome.runtime.sendMessage === 'function';
    } catch {
      return false;
    }
  }

  function detectAiPlatform() {
    const host = (window.location.hostname || '').toLowerCase();
    if (host.includes('gemini.google.com')) return 'Gemini';
    if (host.includes('claude.ai')) return 'Claude';
    if (host.includes('deepseek.com')) return 'DeepSeek';
    if (host.includes('chatgpt.com') || host.includes('openai.com')) return 'ChatGPT';
    return 'AI';
  }

  const currentPlatform = detectAiPlatform();

  let evalTimer = null;
  let observer = null;

  function cleanup() {
    try {
      if (evalTimer) clearInterval(evalTimer);
      if (observer) observer.disconnect();
    } catch {}
  }
  if (typeof window !== 'undefined') {
    window.__pikoContentCleanup = cleanup;
  }

  let isCurrentlyGenerating = false;
  let generationWasActive = false;
  let noGeneratingControlSince = null;
  let wasManuallyStopped = false;
  let lastStartSentAt = 0;
  let lastCompleteSentAt = 0;
  const IDLE_STABILITY_MS = 1500;
  const ALERT_DEBOUNCE_MS = 4000;

  // Intercept clicks on any stop/cancel button to distinguish manual cancellation from normal completion
  document.addEventListener('click', (event) => {
    const target = event.target;
    if (!target) return;
    const button = target.closest('button, [role="button"]');
    if (!button) return;

    const label = [
      button.getAttribute('data-testid') || '',
      button.getAttribute('data-test-id') || '',
      button.getAttribute('aria-label') || '',
      button.getAttribute('title') || '',
      button.innerText || ''
    ].join(' ').toLowerCase();

    if (/\b(stop|hentikan|cancel)\b/i.test(label) || label.includes('skip') || button.querySelector('rect')) {
      wasManuallyStopped = true;
    }
  }, true);

  function isElementVisible(el) {
    if (!el) return false;
    return el.offsetParent !== null || el.offsetWidth > 0 || el.offsetHeight > 0 || el.getClientRects().length > 0;
  }

  function checkGeneratingDom() {
    // 1. ChatGPT: testid or aria-label stop buttons
    const chatGptStopButtons = document.querySelectorAll(
      'button[data-testid="stop-button"], button[data-testid="fruitjuice-stop-button"], button[data-testid*="stop" i], button[aria-label*="stop" i], button[title*="stop" i]'
    );
    for (const btn of chatGptStopButtons) {
      if (isElementVisible(btn)) return true;
    }

    // 2. ChatGPT: reasoning / thinking state (Skip button)
    const thinkingSkipButtons = document.querySelectorAll(
      'button[aria-label*="skip" i], button[data-testid*="skip" i]'
    );
    for (const btn of thinkingSkipButtons) {
      const txt = (btn.innerText || '').trim().toLowerCase();
      if ((txt === 'skip' || txt.includes('skip')) && isElementVisible(btn)) {
        return true;
      }
    }

    // 3. Gemini: Stop button (aria-label="Stop", "Hentikan", etc.), sparkle/loading animations, and progress bar
    if (currentPlatform === 'Gemini' || currentPlatform === 'AI') {
      const geminiStopButtons = document.querySelectorAll(
        'button[aria-label*="stop" i], button[aria-label*="hentikan" i], button[data-test-id*="stop" i]'
      );
      for (const btn of geminiStopButtons) {
        if (isElementVisible(btn)) return true;
      }

      // Check Gemini Material stop icon
      const matIcons = document.querySelectorAll('mat-icon[data-mat-icon-name="stop"], mat-icon[fonticon="stop"]');
      for (const icon of matIcons) {
        const btn = icon.closest('button, [role="button"]');
        if (btn && isElementVisible(btn)) return true;
      }

      // Check Gemini active loading/sparkle animation
      const geminiLoading = document.querySelectorAll(
        'mat-progress-bar:not([style*="display: none"]), .sparkle-animating, gemini-sparkle-loader, [data-test-id="loading-indicator"]'
      );
      for (const el of geminiLoading) {
        if (isElementVisible(el)) return true;
      }
    }

    // 4. Claude: Stop button, streaming indicator
    if (currentPlatform === 'Claude' || currentPlatform === 'AI') {
      const claudeStopButtons = document.querySelectorAll(
        'button[aria-label*="stop" i], button[data-testid*="stop" i], button[aria-label*="hentikan" i]'
      );
      for (const btn of claudeStopButtons) {
        if (isElementVisible(btn)) return true;
      }

      const claudeStreaming = document.querySelectorAll('[data-is-streaming="true"], .font-claude-message.streaming');
      for (const el of claudeStreaming) {
        if (isElementVisible(el)) return true;
      }
    }

    // 5. DeepSeek: Stop button, reasoning/thinking indicators
    if (currentPlatform === 'DeepSeek' || currentPlatform === 'AI') {
      const deepSeekStopButtons = document.querySelectorAll(
        'button[aria-label*="stop" i], div[role="button"][aria-label*="stop" i], button[class*="stop" i], div[class*="stop" i][role="button"]'
      );
      for (const btn of deepSeekStopButtons) {
        if (isElementVisible(btn)) return true;
      }

      const deepSeekLoading = document.querySelectorAll(
        '[class*="ds-icon--loading"], [class*="ds-loading"], .ds-thinking, div[class*="thinking" i]'
      );
      for (const el of deepSeekLoading) {
        if (isElementVisible(el)) return true;
      }
    }

    // 6. Universal cross-AI heuristic: Stop button containing a square rect SVG
    // (Used universally across ChatGPT, Claude, DeepSeek, and modern AI chat inputs)
    try {
      const svgRectButtons = document.querySelectorAll('button svg rect, [role="button"] svg rect');
      for (const rect of svgRectButtons) {
        const btn = rect.closest('button, [role="button"]');
        if (btn && isElementVisible(btn)) {
          // Avoid matching audio / video / non-chat player controls
          const aria = (btn.getAttribute('aria-label') || '').toLowerCase();
          if (!aria.includes('volume') && !aria.includes('playback') && !aria.includes('mute')) {
            return true;
          }
        }
      }
    } catch {}

    // 7. Universal class/attribute streaming indicators
    if (document.querySelector('[data-is-streaming="true"], .result-streaming, .result-thinking, [data-testid*="thinking" i]')) {
      return true;
    }

    // 8. Fallback scan for buttons with explicit stop/hentikan label
    const buttons = document.querySelectorAll('button, [role="button"]');
    for (const button of buttons) {
      const aria = button.getAttribute('aria-label') || '';
      const title = button.getAttribute('title') || '';
      const text = button.innerText || '';
      const combined = `${aria} ${title} ${text}`.toLowerCase();
      if ((/\b(stop|hentikan)\b/i.test(combined)) && !combined.includes('audio') && !combined.includes('playback') && !combined.includes('music')) {
        if (isElementVisible(button)) {
          return true;
        }
      }
    }

    return false;
  }

  function dispatchToWorker(type, title) {
    if (!isExtensionValid()) {
      cleanup();
      return;
    }
    try {
      chrome.runtime.sendMessage({
        type: type,
        source: currentPlatform,
        title: title,
        pageUrl: window.location.href,
        timestamp: Date.now()
      }, () => {
        if (chrome.runtime?.lastError) {}
      });
    } catch (err) {
      cleanup();
    }
  }

  function evaluateState() {
    if (!isExtensionValid()) {
      cleanup();
      return;
    }

    const now = Date.now();
    const generatingNow = checkGeneratingDom();

    if (generatingNow) {
      // Transition from idle -> generating
      if (!isCurrentlyGenerating) {
        isCurrentlyGenerating = true;
        generationWasActive = true;
        noGeneratingControlSince = null;
        wasManuallyStopped = false;

        if (now - lastStartSentAt > 3000) {
          lastStartSentAt = now;
          dispatchToWorker('PIKO_AI_START', `${currentPlatform} sedang menghasilkan jawaban...`);
        }
      }
      return;
    }

    // Currently idle
    if (isCurrentlyGenerating) {
      isCurrentlyGenerating = false;
      noGeneratingControlSince = now;
    }

    if (!generationWasActive) return;

    if (noGeneratingControlSince === null) {
      noGeneratingControlSince = now;
      return;
    }

    // Check if stable idle has persisted past debounce threshold
    if (now - noGeneratingControlSince >= IDLE_STABILITY_MS) {
      generationWasActive = false;
      noGeneratingControlSince = null;

      if (now - lastCompleteSentAt >= ALERT_DEBOUNCE_MS) {
        lastCompleteSentAt = now;
        if (wasManuallyStopped) {
          wasManuallyStopped = false;
          dispatchToWorker('PIKO_AI_CANCEL', `Generasi ${currentPlatform} dihentikan.`);
        } else {
          dispatchToWorker('PIKO_AI_COMPLETE', `Jawaban ${currentPlatform} sudah siap ✨`);
        }
      }
    }
  }

  // Observe DOM changes with debounced polling fallback
  observer = new MutationObserver(evaluateState);
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['disabled', 'aria-label', 'data-testid', 'data-test-id', 'class', 'data-is-streaming']
  });

  evalTimer = window.setInterval(evaluateState, 500);
  evaluateState();
})();
