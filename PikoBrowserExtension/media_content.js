// Piko Browser Extension — Real-Time Media Playback & Metadata Content Script
// High-fidelity, non-invasive detection of YouTube & YouTube Music playback status and metadata

(function () {
  'use strict';

  // Only run in the top-level window, not in embedded frames/ads
  if (window.top !== window.self) return;

  console.log('[Piko Media] YouTube real-time media script loaded on:', location.href);

  let lastSentState = 'stopped'; // 'playing' | 'paused' | 'stopped'
  let lastSentVideoId = '';
  let lastSentTitle = '';
  let lastSentChannel = '';
  let lastSentThumbnail = '';
  let pauseDebounceTimer = null;
  let metadataRetryTimer = null;
  let currentHref = location.href;
  let isPollingCommands = false;

  const boundVideos = new WeakSet();

  function isYouTubeMusic() {
    return location.hostname.includes('music.youtube.com');
  }

  function getVideoId() {
    try {
      const url = new URL(location.href);
      if (url.searchParams.has('v')) {
        return url.searchParams.get('v') || '';
      }
      const pathParts = url.pathname.split('/');
      if (pathParts[1] === 'shorts' && pathParts[2]) return pathParts[2];
      if (pathParts[1] === 'embed' && pathParts[2]) return pathParts[2];
      return '';
    } catch {
      return '';
    }
  }

  function getCleanTitle(raw) {
    if (!raw) return '';
    return raw
      .replace(/\s*-\s*YouTube(?:\s*Music)?$/i, '')
      .replace(/^\(\d+\)\s*/, '') // Remove "(1) " unread notifications count
      .trim();
  }

  function getCurrentMediaMetadata(activeVideo) {
    const isYtMusic = isYouTubeMusic();
    const source = isYtMusic ? 'YouTube Music' : 'YouTube';
    const videoId = getVideoId();

    let title = '';
    let channel = '';
    let thumbnailUrl = '';

    if (isYtMusic) {
      title = document.querySelector('ytmusic-player-bar .title')?.textContent?.trim()
        || document.querySelector('.ytmusic-player-bar.title')?.textContent?.trim()
        || '';

      channel = document.querySelector('ytmusic-player-bar .byline a')?.textContent?.trim()
        || document.querySelector('ytmusic-player-bar .byline')?.textContent?.trim()
        || '';

      const thumbImg = document.querySelector('ytmusic-player-bar .thumbnail img')
        || document.querySelector('ytmusic-player-bar img');
      if (thumbImg && thumbImg.src && !thumbImg.src.startsWith('data:')) {
        thumbnailUrl = thumbImg.src;
      }
    } else {
      // YouTube Watch page selectors (ordered by specificity)
      title = document.querySelector('ytd-watch-metadata #title h1 yt-formatted-string')?.textContent?.trim()
        || document.querySelector('h1.ytd-watch-metadata yt-formatted-string')?.textContent?.trim()
        || document.querySelector('#title h1 yt-formatted-string')?.textContent?.trim()
        || document.querySelector('.ytp-title-link')?.textContent?.trim()
        || document.querySelector('ytd-reel-video-renderer[is-active] .title yt-formatted-string')?.textContent?.trim()
        || '';

      channel = document.querySelector('#owner #channel-name yt-formatted-string a')?.textContent?.trim()
        || document.querySelector('ytd-channel-name yt-formatted-string a')?.textContent?.trim()
        || document.querySelector('#upload-info #channel-name a')?.textContent?.trim()
        || document.querySelector('#owner-sub-count')?.closest('#owner')?.querySelector('#channel-name')?.textContent?.trim()
        || '';
    }

    // Fallback to document.title if DOM title element is not yet populated
    if (!title) {
      title = getCleanTitle(document.title);
    } else {
      title = getCleanTitle(title);
    }

    // High quality thumbnail fallback using videoId
    if (!thumbnailUrl && videoId) {
      thumbnailUrl = `https://i.ytimg.com/vi/${videoId}/hqdefault.jpg`;
    }

    const duration = activeVideo && !isNaN(activeVideo.duration) && activeVideo.duration > 0
      ? Math.round(activeVideo.duration)
      : 0;

    const currentTime = activeVideo && !isNaN(activeVideo.currentTime) && activeVideo.currentTime >= 0
      ? Math.round(activeVideo.currentTime)
      : 0;

    return {
      source,
      title: title || 'YouTube Video',
      channel: channel || 'YouTube',
      videoId,
      thumbnailUrl,
      pageUrl: location.href,
      duration,
      currentTime
    };
  }

  function sendMediaEvent(type, metadata, playbackState) {
    const meta = metadata || getCurrentMediaMetadata(findActiveVideo());
    const state = playbackState || (type === 'PIKO_MEDIA_PLAYING' ? 'playing' : (type === 'PIKO_MEDIA_PAUSED' ? 'paused' : 'stopped'));

    console.log(`[Piko Media] ${type} -> "${meta.title}" (${meta.channel}) [${state}]`);

    try {
      chrome.runtime.sendMessage({
        type: type,
        source: meta.source,
        title: meta.title,
        channel: meta.channel,
        videoId: meta.videoId,
        thumbnailUrl: meta.thumbnailUrl,
        pageUrl: meta.pageUrl,
        duration: meta.duration,
        currentTime: meta.currentTime,
        playbackState: state,
        timestamp: Date.now()
      }, response => {
        if (chrome.runtime.lastError) {
          // Expected when desktop bridge or service worker is sleeping
        }
      });
    } catch (err) {
      console.warn('[Piko Media] SendMessage error:', err);
    }
  }

  function findActiveVideo() {
    const videos = Array.from(document.querySelectorAll('video'));
    // Prioritize currently playing video
    const playing = videos.find(v => !v.paused && !v.ended && (v.currentTime > 0 || v.readyState > 0));
    if (playing) return playing;
    // Otherwise return first valid video
    return videos[0] || null;
  }

  function isVideoPlaying(video) {
    return !!(video && !video.paused && !video.ended && (video.currentTime > 0 || video.readyState > 0));
  }

  function scheduleMetadataSync(retriesLeft = 6) {
    if (metadataRetryTimer) clearTimeout(metadataRetryTimer);

    const video = findActiveVideo();
    const meta = getCurrentMediaMetadata(video);
    const hasValidNewTitle = meta.title && meta.title !== 'YouTube' && meta.title !== 'YouTube Video';
    const isDifferentVideo = meta.videoId && meta.videoId !== lastSentVideoId;
    const isDifferentTitle = meta.title && meta.title !== lastSentTitle;

    if ((isDifferentVideo || isDifferentTitle) && hasValidNewTitle) {
      lastSentVideoId = meta.videoId;
      lastSentTitle = meta.title;
      lastSentChannel = meta.channel;
      lastSentThumbnail = meta.thumbnailUrl;

      const playing = isVideoPlaying(video);
      const eventType = playing ? 'PIKO_MEDIA_PLAYING' : (lastSentState === 'playing' ? 'PIKO_MEDIA_PLAYING' : 'PIKO_MEDIA_METADATA');
      if (playing) lastSentState = 'playing';

      sendMediaEvent(eventType, meta, playing ? 'playing' : 'paused');
    } else if (retriesLeft > 0) {
      // Retry in 350ms while DOM stabilizes
      metadataRetryTimer = setTimeout(() => {
        scheduleMetadataSync(retriesLeft - 1);
      }, 350);
    }
  }

  function handlePlay(video) {
    if (pauseDebounceTimer) {
      clearTimeout(pauseDebounceTimer);
      pauseDebounceTimer = null;
    }

    if (!isVideoPlaying(video)) return;

    const meta = getCurrentMediaMetadata(video);
    const titleChanged = meta.title && meta.title !== lastSentTitle;
    const videoChanged = meta.videoId && meta.videoId !== lastSentVideoId;

    if (lastSentState !== 'playing' || titleChanged || videoChanged) {
      lastSentState = 'playing';
      lastSentVideoId = meta.videoId;
      lastSentTitle = meta.title;
      lastSentChannel = meta.channel;
      lastSentThumbnail = meta.thumbnailUrl;

      sendMediaEvent('PIKO_MEDIA_PLAYING', meta, 'playing');
    }

    // Schedule metadata verification after play in case title finishes updating asynchronously
    scheduleMetadataSync(4);
  }

  function handlePause(video) {
    if (pauseDebounceTimer) {
      clearTimeout(pauseDebounceTimer);
    }

    // Debounce pause by 300ms to ignore momentary seeking or buffering
    pauseDebounceTimer = setTimeout(() => {
      const anyPlaying = Array.from(document.querySelectorAll('video')).some(isVideoPlaying);
      if (!anyPlaying) {
        if (lastSentState === 'playing') {
          lastSentState = 'paused';
          const meta = getCurrentMediaMetadata(video);
          sendMediaEvent('PIKO_MEDIA_PAUSED', meta, 'paused');
        }
      }
    }, 300);
  }

  function handleEnded() {
    if (pauseDebounceTimer) {
      clearTimeout(pauseDebounceTimer);
      pauseDebounceTimer = null;
    }

    const anyPlaying = Array.from(document.querySelectorAll('video')).some(isVideoPlaying);
    if (!anyPlaying && lastSentState !== 'stopped') {
      lastSentState = 'stopped';
      lastSentTitle = '';
      lastSentVideoId = '';
      sendMediaEvent('PIKO_MEDIA_STOPPED', null, 'stopped');
    }
  }

  function bindVideoEvents(video) {
    if (!video) return;

    if (!boundVideos.has(video)) {
      boundVideos.add(video);
      console.log('[Piko Media] Attached event listeners to video element');

      video.addEventListener('playing', () => handlePlay(video), { passive: true });
      video.addEventListener('play', () => handlePlay(video), { passive: true });
      video.addEventListener('pause', () => handlePause(video), { passive: true });
      video.addEventListener('ended', handleEnded, { passive: true });
      video.addEventListener('emptied', handleEnded, { passive: true });
      video.addEventListener('loadeddata', () => scheduleMetadataSync(3), { passive: true });
    }

    if (isVideoPlaying(video)) {
      handlePlay(video);
    }
  }

  // Poll pending commands via Extension Background Service Worker (avoids webpage mixed-content/CSP blocks)
  function pollBridgeCommands() {
    if (isPollingCommands) return;
    isPollingCommands = true;

    try {
      chrome.runtime.sendMessage({ type: 'PIKO_POLL_COMMANDS' }, response => {
        isPollingCommands = false;
        if (chrome.runtime.lastError) return;
        if (response && response.ok && response.command && response.command.Action) {
          executeMediaCommand(response.command);
        }
      });
    } catch {
      isPollingCommands = false;
    }
  }

  function executeMediaCommand(command) {
    const action = (command.Action || '').toLowerCase();
    const commandId = command.Id || '';
    console.log(`[Piko Media] Executing remote command: "${action}" (id=${commandId})`);

    const video = findActiveVideo();
    const isYtMusic = isYouTubeMusic();
    let success = false;
    let errorMsg = '';

    if (!video && action !== 'next' && action !== 'prev') {
      reportCommandResult(commandId, action, false, 'Tidak ada elemen video aktif pada halaman YouTube');
      return;
    }

    try {
      switch (action) {
        case 'pause':
          if (video) {
            video.pause();
            // Fallback: if video did not pause, click YouTube play/pause button
            if (!video.paused) {
              const btn = document.querySelector('.ytp-play-button')
                || document.querySelector('ytmusic-player-bar #play-pause-button');
              if (btn) btn.click();
            }
            success = true;
          }
          break;

        case 'play':
          if (video) {
            const p = video.play();
            if (p && p.catch) {
              p.catch(() => {
                const btn = document.querySelector('.ytp-play-button')
                  || document.querySelector('ytmusic-player-bar #play-pause-button');
                if (btn) btn.click();
              });
            }
            success = true;
          }
          break;

        case 'next':
          if (isYtMusic) {
            const nextBtn = document.querySelector('ytmusic-player-bar .next-button')
              || document.querySelector('.ytmusic-player-bar .next-button');
            if (nextBtn) {
              nextBtn.click();
              success = true;
            } else {
              errorMsg = 'Tombol Next tidak ditemukan di YouTube Music';
            }
          } else {
            const nextBtn = document.querySelector('.ytp-next-button');
            if (nextBtn) {
              nextBtn.click();
              success = true;
            } else {
              // Fallback to first video recommendation
              const rec = document.querySelector('ytd-watch-next-secondary-results-renderer a#thumbnail')
                || document.querySelector('ytd-compact-video-renderer a#thumbnail');
              if (rec) {
                rec.click();
                success = true;
              } else {
                errorMsg = 'Tombol Next / playlist tidak tersedia';
              }
            }
          }
          if (success) {
            scheduleMetadataSync(8);
          }
          break;

        case 'prev':
          if (isYtMusic) {
            const prevBtn = document.querySelector('ytmusic-player-bar .previous-button')
              || document.querySelector('.ytmusic-player-bar .previous-button');
            if (prevBtn) {
              prevBtn.click();
              success = true;
            } else {
              errorMsg = 'Tombol Prev tidak ditemukan di YouTube Music';
            }
          } else {
            if (video && video.currentTime > 3) {
              video.currentTime = 0;
              success = true;
            } else {
              const prevBtn = document.querySelector('.ytp-prev-button');
              if (prevBtn) {
                prevBtn.click();
                success = true;
              } else {
                history.back();
                success = true;
              }
            }
          }
          if (success) {
            scheduleMetadataSync(8);
          }
          break;

        default:
          errorMsg = `Perintah tidak dikenal: ${action}`;
          break;
      }
    } catch (err) {
      success = false;
      errorMsg = err.message || String(err);
    }

    // Verify state after 80ms and send confirmation
    setTimeout(() => {
      const activeVid = findActiveVideo();
      const currentPlaybackState = isVideoPlaying(activeVid)
        ? 'playing'
        : (activeVid && activeVid.paused ? 'paused' : 'stopped');
      const meta = getCurrentMediaMetadata(activeVid);

      reportCommandResult(commandId, action, success, errorMsg, currentPlaybackState, meta);
    }, 80);
  }

  function reportCommandResult(commandId, action, success, errorMsg, playbackState, meta) {
    const metadata = meta || getCurrentMediaMetadata(findActiveVideo());
    const state = playbackState || (isVideoPlaying(findActiveVideo()) ? 'playing' : 'paused');

    try {
      chrome.runtime.sendMessage({
        type: 'PIKO_COMMAND_RESULT',
        commandId: commandId,
        action: action,
        success: success,
        errorMessage: errorMsg,
        playbackState: state,
        source: metadata.source,
        title: metadata.title,
        channel: metadata.channel
      });
    } catch (e) {
      console.warn('[Piko Media] Failed to send command result:', e);
    }
  }

  // Also listen for direct push messages from service worker
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message && message.type === 'PIKO_EXECUTE_COMMAND' && message.command) {
      executeMediaCommand(message.command);
      sendResponse({ received: true });
    }
  });

  function scanAndEvaluate() {
    // Check URL change (SPA pushState/replaceState)
    if (location.href !== currentHref) {
      currentHref = location.href;
      console.log('[Piko Media] Location changed:', currentHref);
      scheduleMetadataSync(6);
    }

    const videos = document.querySelectorAll('video');
    let hasPlayingVideo = false;
    let activeVid = null;

    videos.forEach(video => {
      bindVideoEvents(video);
      if (isVideoPlaying(video)) {
        hasPlayingVideo = true;
        activeVid = video;
      }
    });

    if (hasPlayingVideo) {
      const meta = getCurrentMediaMetadata(activeVid);
      const isNewVideo = meta.videoId && meta.videoId !== lastSentVideoId;
      const isNewTitle = meta.title && meta.title !== lastSentTitle;

      if (lastSentState !== 'playing' || isNewVideo || isNewTitle) {
        lastSentState = 'playing';
        lastSentVideoId = meta.videoId;
        lastSentTitle = meta.title;
        lastSentChannel = meta.channel;
        lastSentThumbnail = meta.thumbnailUrl;
        sendMediaEvent('PIKO_MEDIA_PLAYING', meta, 'playing');
      }
    } else {
      if (lastSentState === 'playing') {
        const hasPausedVideo = Array.from(videos).some(v => v.paused && !v.ended && v.currentTime > 0);
        if (hasPausedVideo) {
          lastSentState = 'paused';
          const meta = getCurrentMediaMetadata(findActiveVideo());
          sendMediaEvent('PIKO_MEDIA_PAUSED', meta, 'paused');
        } else {
          lastSentState = 'stopped';
          lastSentTitle = '';
          lastSentVideoId = '';
          sendMediaEvent('PIKO_MEDIA_STOPPED', null, 'stopped');
        }
      }
    }

    // Continuously poll bridge commands via service worker proxy
    pollBridgeCommands();
  }

  // 1. Initial scan
  scanAndEvaluate();

  // 2. Periodic polling every 600ms to guarantee state alignment and prompt command delivery
  const pollTimer = setInterval(scanAndEvaluate, 600);

  // 3. YouTube SPA Navigation events
  window.addEventListener('yt-navigate-start', () => {
    console.log('[Piko Media] YouTube navigation started');
    scheduleMetadataSync(8);
  });

  window.addEventListener('yt-navigate-finish', () => {
    console.log('[Piko Media] YouTube navigation finished');
    scheduleMetadataSync(8);
  });

  window.addEventListener('yt-page-data-updated', () => {
    scheduleMetadataSync(5);
  });

  window.addEventListener('popstate', () => {
    scheduleMetadataSync(6);
  });

  // 4. Targeted observer for YouTube title and player DOM changes
  const titleObserver = new MutationObserver(() => {
    const meta = getCurrentMediaMetadata(findActiveVideo());
    if (meta.title && meta.title !== lastSentTitle && meta.title !== 'YouTube' && meta.title !== 'YouTube Video') {
      scheduleMetadataSync(2);
    }
  });

  const titleNode = document.querySelector('title');
  if (titleNode) {
    titleObserver.observe(titleNode, { childList: true, characterData: true, subtree: true });
  }

  // 5. Cleanup on unload or tab close
  function handleUnload() {
    if (lastSentState === 'playing') {
      lastSentState = 'stopped';
      lastSentTitle = '';
      lastSentVideoId = '';
      sendMediaEvent('PIKO_MEDIA_STOPPED', null, 'stopped');
    }
  }

  window.addEventListener('beforeunload', handleUnload);
  window.addEventListener('pagehide', handleUnload);
})();
