// popup.js — Claude 用量喵喵 popup 邏輯

// ── i18n helper ──
function t(key, ...subs) {
  const msg = chrome.i18n.getMessage(key, subs);
  return msg || key;
}

function applyI18n() {
  for (const el of document.querySelectorAll('[data-i18n]')) {
    el.textContent = t(el.dataset.i18n);
  }
  for (const el of document.querySelectorAll('[data-i18n-title]')) {
    el.title = t(el.dataset.i18nTitle);
  }
}

// ── DOM 元素快取 ──
const $ = (sel) => document.querySelector(sel);

const DOM = {
  cards: $('#cards-container'),
  empty: $('#empty-state'),
  error: $('#error-state'),
  errorMsg: $('#error-msg'),
  refreshBtn: $('#refresh-btn'),
  statusDot: $('.status-dot'),
  statusText: $('#status-text'),
  orgName: $('#org-name'),
  tokenSection: $('#token-section'),
  tokenLabel: $('#token-label'),
  officialLabel: $('#official-label'),
  totalInput: $('#total-input'),
  totalOutput: $('#total-output'),
  totalCost: $('#total-cost'),
  currentModel: $('#current-model'),
  msgCount: $('#msg-count'),
  dataSourceTag: $('#data-source-tag'),
  tokenRecent: $('#token-recent'),
  resetBtn: $('#reset-btn'),
  nextRefresh: $('#next-refresh'),
  convLabel: $('.conversations-label'),
};

// ── 計時器管理 ──
let refreshCountdownTimer = null;

function clearTimers() {
  if (refreshCountdownTimer) {
    clearInterval(refreshCountdownTimer);
    refreshCountdownTimer = null;
  }
}

// ── 初始化 ──
document.addEventListener('DOMContentLoaded', () => {
  applyI18n();
  loadData();
  loadTokenData();
  DOM.refreshBtn.addEventListener('click', handleRefresh);
  DOM.resetBtn.addEventListener('click', handleResetTokens);

  // 即時更新 token 資料
  chrome.storage.onChanged.addListener((changes) => {
    if (changes.tokenData) renderTokenData(changes.tokenData.newValue);
  });
});

// Popup 關閉時清理計時器
window.addEventListener('unload', clearTimers);

// ── 載入資料 ──
function loadData() {
  chrome.runtime.sendMessage({ type: 'GET_USAGE' }, (data) => {
    if (chrome.runtime.lastError) return showError(t('bgConnFailed'));
    renderData(data);
  });
}

// ── 手動刷新 ──
function handleRefresh() {
  DOM.refreshBtn.classList.add('spinning');
  setStatus('loading', t('refreshing'));

  chrome.runtime.sendMessage({ type: 'REFRESH' }, (data) => {
    DOM.refreshBtn.classList.remove('spinning');
    if (chrome.runtime.lastError) return showError(t('refreshFailed'));
    renderData(data);
  });
}

// ── 渲染主邏輯 ──
function renderData(data) {
  if (!data) return showEmpty();
  if (data.error) return showError(data.error);
  if (!data.tiers?.length) {
    return data.raw ? showRawData(data) : showEmpty();
  }

  // 成功：顯示卡片
  toggleView('cards');
  DOM.orgName.textContent = data.orgName || 'Personal';
  setStatus('ok', t('connected'));
  updateLastUpdated(data.lastUpdated);

  DOM.cards.innerHTML = '';
  for (const tier of data.tiers) {
    DOM.cards.appendChild(createCard(tier));
  }
}

// ── 建立用量卡片 ──
function createCard(tier) {
  const pct = tier.usagePercent ?? 0;
  const colorClass = getColorClass(pct);
  const card = document.createElement('div');
  card.className = `usage-card ${colorClass}`;

  const resetText = tier.resetAt ? formatResetTime(tier.resetAt) : '';
  const modelText = tier.modelName ? escapeHtml(tier.modelName) : '';
  const usageText = (tier.used != null && tier.limit != null)
    ? `<span class="card-detail-item"><span class="card-detail-icon">📊</span>${tier.used} / ${tier.limit}</span>`
    : '';

  card.innerHTML = `
    <div class="card-header">
      <span class="card-label">${escapeHtml(t(tier.label))}</span>
      <span class="card-percent ${colorClass}">${Math.round(pct)}%</span>
    </div>
    <div class="progress-track">
      <div class="progress-fill ${colorClass}" style="width: 0%"></div>
    </div>
    <div class="card-details">
      ${resetText ? `<span class="card-detail-item"><span class="card-detail-icon">⏰</span>${resetText}</span>` : ''}
      ${modelText ? `<span class="card-detail-item"><span class="card-detail-icon">🤖</span>${modelText}</span>` : ''}
      ${usageText}
    </div>
  `;

  // 進度條動畫（雙 rAF 確保觸發 transition）
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      const fill = card.querySelector('.progress-fill');
      if (fill) fill.style.width = `${Math.min(pct, 100)}%`;
    });
  });

  return card;
}

// ── 顯示 raw 資料（debug） ──
function showRawData(data) {
  toggleView('cards');
  setStatus('ok', t('connectedRaw'));
  updateLastUpdated(data.lastUpdated);
  DOM.orgName.textContent = data.orgName || 'Personal';

  DOM.cards.innerHTML = `
    <div class="usage-card green">
      <div class="card-header">
        <span class="card-label">${t('rawDataLabel')}</span>
      </div>
      <pre style="font-size:10px;color:var(--text-sub);white-space:pre-wrap;word-break:break-all;
           max-height:200px;overflow-y:auto;font-family:'SF Mono',monospace;margin-top:8px;
           background:#F8F5FF;padding:8px;border-radius:8px;">${escapeHtml(JSON.stringify(data.raw, null, 2))}</pre>
      <p style="font-size:10px;color:var(--text-light);margin-top:8px;text-align:center;">${t('rawDataHint')}</p>
    </div>
  `;
}

// ── 狀態切換 ──
function toggleView(view) {
  DOM.cards.style.display = view === 'cards' ? 'flex' : 'none';
  DOM.empty.style.display = view === 'empty' ? 'block' : 'none';
  DOM.error.style.display = view === 'error' ? 'block' : 'none';
}

function showEmpty() {
  toggleView('empty');
  setStatus('error', t('notLoggedIn'));
}

function showError(msg) {
  toggleView('error');
  DOM.errorMsg.textContent = msg;
  setStatus('error', t('connError'));
}

// ── Helpers ──
function getColorClass(pct) {
  if (pct < 50) return 'green';
  if (pct < 75) return 'yellow';
  if (pct < 90) return 'orange';
  return 'red';
}

function setStatus(type, text) {
  DOM.statusDot.className = `status-dot ${type === 'error' ? 'error' : type === 'loading' ? 'loading' : ''}`;
  DOM.statusText.textContent = text;
}

// ── 刷新倒數 ──
function updateLastUpdated(timestamp) {
  if (!timestamp) return;
  clearTimers();

  const el = DOM.nextRefresh;
  if (!el) return;

  const INTERVAL = 60_000; // 1 min
  const nextRefreshAt = timestamp + INTERVAL;

  function tick() {
    const remaining = Math.max(0, nextRefreshAt - Date.now());
    const sec = Math.ceil(remaining / 1000);
    el.textContent = `${sec} 秒後刷新`;

    if (remaining <= 0) {
      clearTimers();
      el.textContent = '刷新中...';
      loadData();
    }
  }

  tick();
  refreshCountdownTimer = setInterval(tick, 1000);
}

// ── 時間格式化 ──
const MS_DAY = 86_400_000;
const MS_HOUR = 3_600_000;
const MS_MIN = 60_000;

function formatResetTime(resetAt) {
  try {
    const date = new Date(resetAt);
    if (isNaN(date.getTime())) return '';

    const diff = date.getTime() - Date.now();
    if (diff <= 0) return t('alreadyReset');

    const days = Math.floor(diff / MS_DAY);
    const hours = Math.floor((diff % MS_DAY) / MS_HOUR);
    const minutes = Math.floor((diff % MS_HOUR) / MS_MIN);

    let timeStr;
    if (days > 0) timeStr = `${days}d ${hours}h`;
    else if (hours > 0) timeStr = `${hours}h ${minutes}m`;
    else timeStr = `${minutes}m`;

    return t('resetIn', timeStr);
  } catch {
    return '';
  }
}

// ── Token 資料 ──
function loadTokenData() {
  // 先嘗試 content script，再 fallback storage
  chrome.tabs.query({ url: 'https://claude.ai/*', active: true }, (tabs) => {
    if (tabs?.length) {
      chrome.tabs.sendMessage(tabs[0].id, { type: 'GET_TOKEN_DATA' }, (data) => {
        if (chrome.runtime.lastError || !data) {
          return loadTokenFromStorage();
        }
        renderTokenData(data);
      });
    } else {
      loadTokenFromStorage();
    }
  });
}

function loadTokenFromStorage() {
  chrome.storage.local.get('tokenData', (r) => renderTokenData(r.tokenData));
}

function renderTokenData(data) {
  if (!data || (!data.totalInputTokens && !data.totalOutputTokens)) {
    DOM.tokenSection.style.display = 'none';
    DOM.tokenLabel.style.display = 'none';
    return;
  }

  DOM.tokenSection.style.display = 'block';
  DOM.tokenLabel.style.display = 'flex';
  DOM.officialLabel.style.display = 'flex';

  DOM.totalInput.textContent = formatNumber(data.totalInputTokens || 0);
  DOM.totalOutput.textContent = formatNumber(data.totalOutputTokens || 0);
  DOM.totalCost.textContent = `$${(data.totalCost || 0).toFixed(4)}`;
  DOM.currentModel.textContent = simplifyModelName(data.lastModel);

  const messages = data.messages || [];
  DOM.msgCount.textContent = messages.length;

  if (DOM.convLabel) {
    DOM.convLabel.textContent = t('conversations', messages.length)
      .replace(String(messages.length), '').trim();
  }

  // 資料來源標籤
  const hasOfficial = messages.some(m => m.isOfficial);
  DOM.dataSourceTag.style.display = 'inline';
  DOM.dataSourceTag.textContent = t(hasOfficial ? 'hasOfficial' : 'estimate');
  DOM.dataSourceTag.className = hasOfficial ? 'tag official' : 'tag';

  // 最近 8 則訊息
  DOM.tokenRecent.innerHTML = '';
  const recent = messages.slice(-8).reverse();
  for (const msg of recent) {
    const div = document.createElement('div');
    div.className = 'token-msg';
    const total = (msg.inputTokens || 0) + (msg.outputTokens || 0);
    div.innerHTML = `
      <span class="token-msg-model">${simplifyModelName(msg.model)}</span>
      <span class="token-msg-tokens">${formatNumber(total)} tokens</span>
      <span class="token-msg-cost">$${(msg.cost || 0).toFixed(4)}</span>
    `;
    DOM.tokenRecent.appendChild(div);
  }
}

function handleResetTokens() {
  chrome.tabs.query({ url: 'https://claude.ai/*' }, (tabs) => {
    if (tabs?.length) {
      chrome.tabs.sendMessage(tabs[0].id, { type: 'RESET_TOKEN_DATA' });
    }
  });
  chrome.storage.local.remove('tokenData');
  renderTokenData(null);
}

// ── 格式化工具 ──
function formatNumber(n) {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K';
  return String(n);
}

function simplifyModelName(model) {
  if (!model) return '—';
  const names = { opus: 'Opus', sonnet: 'Sonnet', haiku: 'Haiku' };
  for (const [key, name] of Object.entries(names)) {
    if (model.includes(key)) return name;
  }
  return model.split('-').slice(0, 2).join(' ');
}

function escapeHtml(str) {
  const el = document.createElement('span');
  el.textContent = str;
  return el.innerHTML;
}
