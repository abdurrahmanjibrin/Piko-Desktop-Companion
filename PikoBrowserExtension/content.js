(() => {
  // Piko AI Completion Notifier - Content Script
  // Strictly privacy-preserving: detects generation state controls only.
  // NEVER reads prompt text, response bodies, chat history, or personal data.

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

    if (label.includes('stop') && (label.includes('generating') || label.includes('response') || label.includes('streaming') || label.includes('button'))) {
      wasManuallyStopped = true;
    }
  }, true);

  function checkGeneratingDom() {
    // 1. Direct testid stop buttons used by ChatGPT
    const stopButtons = document.querySelectorAll(
      'button[data-testid="stop-button"], button[data-testid="fruitjuice-stop-button"], button[aria-label*="stop generating" i], button[aria-label*="stop streaming" i], button[aria-label*="stop response" i]'
    );
    for (const btn of stopButtons) {
      if (btn.offsetParent !== null || btn.offsetWidth > 0 || btn.offsetHeight > 0) {
        return true;
      }
    }

    // 2. Attribute / class streaming indicators
    if (document.querySelector('[data-is-streaming="true"], .result-streaming')) {
      return true;
    }

    // 3. Fallback scan for buttons with stop labels
    const buttons = document.querySelectorAll('button');
    for (const button of buttons) {
      const aria = button.getAttribute('aria-label') || '';
      const title = button.getAttribute('title') || '';
      const text = button.innerText || '';
      const combined = `${aria} ${title} ${text}`.toLowerCase();
      if (/\bstop\s+(generating|streaming|response|generation)\b/i.test(combined)) {
        if (button.offsetParent !== null || button.offsetWidth > 0 || button.offsetHeight > 0) {
          return true;
        }
      }
    }

    return false;
  }

  function dispatchToWorker(type, title) {
    try {
      if (chrome.runtime?.id) {
        chrome.runtime.sendMessage({
          type: type,
          source: 'ChatGPT',
          title: title,
          timestamp: Date.now()
        });
      }
    } catch (err) {
      // Chrome extension context may be reloading or disconnected
      console.debug('[Piko] Event dispatch skipped:', err);
    }
  }

  function evaluateState() {
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
  const observer = new MutationObserver(evaluateState);
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['disabled', 'aria-label', 'data-testid', 'class', 'data-is-streaming']
  });

  window.setInterval(evaluateState, 500);
  evaluateState();
})();
