(() => {
  // Piko AI Completion Notifier - Content Script
  // Strictly privacy-preserving: detects generation state controls only.
  // NEVER reads prompt text, response bodies, chat history, or personal data.

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

  // Intercept clicks on any stop button to distinguish manual cancellation from normal completion
  document.addEventListener('click', (event) => {
    const target = event.target;
    if (!target) return;
    const button = target.closest('button');
    if (!button) return;

    const label = [
      button.getAttribute('data-testid') || '',
      button.getAttribute('aria-label') || '',
      button.getAttribute('title') || '',
      button.innerText || ''
    ].join(' ').toLowerCase();

    if (/\bstop\b/i.test(label) || label.includes('skip') || button.querySelector('rect')) {
      wasManuallyStopped = true;
    }
  }, true);

  function checkGeneratingDom() {
    // 1. Direct testid or aria-label stop buttons used by ChatGPT
    const stopButtons = document.querySelectorAll(
      'button[data-testid="stop-button"], button[data-testid="fruitjuice-stop-button"], button[data-testid*="stop" i], button[aria-label*="stop" i], button[title*="stop" i]'
    );
    for (const btn of stopButtons) {
      if (btn.offsetParent !== null || btn.offsetWidth > 0 || btn.offsetHeight > 0) {
        return true;
      }
    }

    // 2. Reasoning / Thinking state in modern ChatGPT (Thinking ... Skip)
    const thinkingSkipButtons = document.querySelectorAll(
      'button[aria-label*="skip" i], button[data-testid*="skip" i]'
    );
    for (const btn of thinkingSkipButtons) {
      const txt = (btn.innerText || '').trim().toLowerCase();
      if (txt === 'skip' || txt.includes('skip')) {
        if (btn.offsetParent !== null || btn.offsetWidth > 0 || btn.offsetHeight > 0) {
          return true;
        }
      }
    }

    // 3. Stop button containing a square rect SVG
    try {
      const svgRectButtons = document.querySelectorAll('button svg rect');
      for (const rect of svgRectButtons) {
        const btn = rect.closest('button');
        if (btn && (btn.offsetParent !== null || btn.offsetWidth > 0 || btn.offsetHeight > 0)) {
          return true;
        }
      }
    } catch {
      // Ignore selector errors
    }

    // 4. Attribute / class streaming indicators
    if (document.querySelector('[data-is-streaming="true"], .result-streaming, .result-thinking, [data-testid*="thinking" i]')) {
      return true;
    }

    // 5. Fallback scan for buttons with stop labels
    const buttons = document.querySelectorAll('button');
    for (const button of buttons) {
      const aria = button.getAttribute('aria-label') || '';
      const title = button.getAttribute('title') || '';
      const text = button.innerText || '';
      const combined = `${aria} ${title} ${text}`.toLowerCase();
      if (/\bstop\b/i.test(combined) && !combined.includes('audio') && !combined.includes('playback')) {
        if (button.offsetParent !== null || button.offsetWidth > 0 || button.offsetHeight > 0) {
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
        source: 'ChatGPT',
        title: title,
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
          dispatchToWorker('PIKO_AI_START', 'ChatGPT sedang menghasilkan jawaban...');
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
          dispatchToWorker('PIKO_AI_CANCEL', 'Generasi ChatGPT dihentikan.');
        } else {
          dispatchToWorker('PIKO_AI_COMPLETE', 'Jawaban ChatGPT sudah siap ✨');
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
    attributeFilter: ['disabled', 'aria-label', 'data-testid', 'class', 'data-is-streaming']
  });

  evalTimer = window.setInterval(evaluateState, 500);
  evaluateState();
})();
