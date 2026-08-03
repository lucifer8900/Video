"use strict";

const ui = {
  blockedPanel: document.getElementById("blocked-panel"),
  blockedList: document.getElementById("blocked-list"),
  card: document.getElementById("review-card"),
  progress: document.getElementById("progress"),
  responseId: document.getElementById("response-id"),
  emotion: document.getElementById("emotion"),
  dialogue: document.getElementById("dialogue"),
  sourceNodes: document.getElementById("source-nodes"),
  issueCodes: document.getElementById("issue-codes"),
  video: document.getElementById("video"),
  audio: document.getElementById("audio"),
  videoStatus: document.getElementById("video-status"),
  audioStatus: document.getElementById("audio-status"),
  rawAudio: document.getElementById("raw-audio"),
  pronunciation: document.getElementById("pronunciation"),
  subtitle: document.getElementById("subtitle"),
  lipSync: document.getElementById("lip-sync"),
  pass: document.getElementById("pass"),
  redo: document.getElementById("redo"),
  previous: document.getElementById("previous"),
  next: document.getElementById("next"),
  export: document.getElementById("export"),
  message: document.getElementById("message")
};

let session;
let index = 0;

async function api(path, body) {
  const response = await fetch(path, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Lip-Sync-Review-Token": session.sessionToken
    },
    body: JSON.stringify(body)
  });
  const payload = await response.json();
  if (!response.ok) throw new Error(payload.code || "review.request_failed");
  return payload;
}

function current() { return session.items[index]; }
function allChecks() {
  return {
    rawAudioUnmasked: ui.rawAudio.checked,
    pronunciationPassed: ui.pronunciation.checked,
    subtitlePassed: ui.subtitle.checked,
    lipSyncPassed: ui.lipSync.checked
  };
}

function renderBlocked() {
  ui.blockedList.replaceChildren();
  for (const item of session.blockedItems) {
    const row = document.createElement("li");
    row.textContent = `${item.responseId}: ${item.issueCodes.join(", ") || item.sourceReadinessStatus}`;
    ui.blockedList.appendChild(row);
  }
  ui.blockedPanel.hidden = session.blockedItems.length === 0;
}

function render() {
  renderBlocked();
  if (session.items.length === 0) {
    ui.card.hidden = true;
    ui.progress.textContent = `0 条可审核；${session.blockedItems.length} 条被严格阻塞。`;
    ui.previous.disabled = true;
    ui.next.disabled = true;
    ui.export.disabled = false;
    return;
  }

  const item = current();
  ui.card.hidden = false;
  ui.progress.textContent = `第 ${index + 1} / ${session.items.length} 条；已决定 ${session.items.filter(value => value.decision !== null).length} 条。`;
  ui.responseId.textContent = item.responseId;
  ui.emotion.textContent = item.emotionText;
  ui.dialogue.textContent = item.dialogueText;
  ui.sourceNodes.textContent = `来源节点：${item.sourceNodeIds.join(", ")}`;
  ui.issueCodes.textContent = `仍未解决：${item.sourceIssueCodes.join(", ") || "无"}`;
  ui.video.src = item.videoUrl;
  ui.audio.src = item.audioUrl;
  ui.videoStatus.textContent = item.videoPlaybackCompleted ? "已完整播放" : "必须完整播放";
  ui.audioStatus.textContent = item.audioPlaybackCompleted ? "已完整播放" : "必须完整播放";
  ui.rawAudio.checked = item.checks?.rawAudioUnmasked || false;
  ui.pronunciation.checked = item.checks?.pronunciationPassed || false;
  ui.subtitle.checked = item.checks?.subtitlePassed || false;
  ui.lipSync.checked = item.checks?.lipSyncPassed || false;
  const playbackReady = item.videoPlaybackCompleted && item.audioPlaybackCompleted;
  ui.pass.disabled = !playbackReady;
  ui.redo.disabled = !playbackReady;
  ui.previous.disabled = index === 0;
  ui.next.disabled = index + 1 >= session.items.length;
  ui.export.disabled = session.items.some(value => value.decision === null);
  ui.message.textContent = item.decision ? `当前决定：${item.decision}` : "";
}

async function completePlayback(kind, element) {
  try {
    const item = current();
    const state = await api("/api/playback", {
      responseId: item.responseId,
      expectedRevision: item.revision,
      kind,
      playedMilliseconds: Math.max(1, Math.round(element.duration * 1000))
    });
    Object.assign(item, state);
    render();
  } catch (error) { ui.message.textContent = error.message; }
}

async function decide(requestedDecision) {
  try {
    const item = current();
    const state = await api("/api/decision", {
      responseId: item.responseId,
      expectedRevision: item.revision,
      requestedDecision,
      checks: allChecks()
    });
    Object.assign(item, state);
    render();
  } catch (error) { ui.message.textContent = error.message; }
}

ui.video.addEventListener("ended", () => completePlayback("original_video", ui.video));
ui.audio.addEventListener("ended", () => completePlayback("reference_audio", ui.audio));
ui.pass.addEventListener("click", () => decide("pass"));
ui.redo.addEventListener("click", () => decide("redo"));
ui.previous.addEventListener("click", () => { index -= 1; render(); });
ui.next.addEventListener("click", () => { index += 1; render(); });
ui.export.addEventListener("click", async () => {
  try {
    await api("/api/export", {});
    ui.message.textContent = "审核报告已原子写入命令行指定位置。";
  } catch (error) { ui.message.textContent = error.message; }
});

fetch("/api/session", { cache: "no-store" })
  .then(response => response.json())
  .then(payload => { session = payload; render(); })
  .catch(error => { ui.progress.textContent = error.message; });
