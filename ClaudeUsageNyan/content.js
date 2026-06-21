// content.js — 橋接層（isolated world）
// 1. 監聽 injected.js 的 token 資料
// 2. 在 claude.ai 頁面注入浮動狀態條

// ── i18n helper ──
const ct = (key) => chrome.i18n.getMessage(key) || key;

// ── 常數 ──
const MAX_MESSAGES = 200;
const BAR_ID = 'claude-nyan-bar';
const STYLE_ID = 'claude-nyan-style';

// ── Session data ──
let sessionData = {
  messages: [],
  totalInputTokens: 0,
  totalOutputTokens: 0,
  totalCost: 0,
  lastModel: null,
  sessionStart: Date.now(),
};

function resetSession() {
  sessionData = {
    messages: [], totalInputTokens: 0, totalOutputTokens: 0,
    totalCost: 0, lastModel: null, sessionStart: Date.now(),
  };
  chrome.storage.local.set({ tokenData: sessionData });
}

// 載入已存的 token 資料
chrome.storage.local.get('tokenData', (r) => {
  if (r.tokenData) sessionData = r.tokenData;
});

// ── 監聽 injected.js 的 token 事件 ──
window.addEventListener('__claude_nyan_token__', (e) => {
  const msg = e.detail;
  if (!msg) return;

  sessionData.messages.push(msg);
  sessionData.totalInputTokens += msg.inputTokens || 0;
  sessionData.totalOutputTokens += msg.outputTokens || 0;
  sessionData.totalCost += msg.cost || 0;
  sessionData.lastModel = msg.model || sessionData.lastModel;
  sessionData.lastUpdated = Date.now();

  // 限制訊息數量
  if (sessionData.messages.length > MAX_MESSAGES) {
    sessionData.messages = sessionData.messages.slice(-MAX_MESSAGES);
  }

  chrome.storage.local.set({ tokenData: sessionData });
  chrome.runtime.sendMessage({ type: 'TOKEN_UPDATE', data: sessionData }).catch(() => {});
  updateFloatingBar();
});

// ── 訊息處理 ──
chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  switch (msg.type) {
    case 'GET_TOKEN_DATA':
      sendResponse(sessionData);
      return true;
    case 'RESET_TOKEN_DATA':
      resetSession();
      updateFloatingBar();
      sendResponse({ ok: true });
      return true;
    case 'USAGE_UPDATE':
      updateFloatingBar(msg.data);
      break;
  }
});

// ══════════════════════════════════════
// 浮動狀態條
// ══════════════════════════════════════

let floatingBar = null;
let cachedUsage = null;

function colorClassForPct(pct) {
  if (pct < 50) return 'green';
  if (pct < 75) return 'yellow';
  if (pct < 90) return 'orange';
  return 'red';
}

function createFloatingBar() {
  // 防止重複注入
  const existing = document.getElementById(BAR_ID);
  if (existing) { floatingBar = existing; return existing; }
  if (floatingBar) return floatingBar;

  // 注入樣式（僅一次）
  if (!document.getElementById(STYLE_ID)) {
    const style = document.createElement('style');
    style.id = STYLE_ID;
    style.textContent = `
      #${BAR_ID} {
        position: fixed; bottom: 16px; right: 16px; z-index: 99999;
        font-family: -apple-system, 'SF Pro Text', sans-serif; font-size: 12px;
        pointer-events: auto; transition: opacity .3s, transform .3s;
      }
      #${BAR_ID}:hover { transform: translateY(-2px); }
      #${BAR_ID}.nyan-minimized #nyan-bar-inner { padding: 4px 8px; }
      #${BAR_ID}.nyan-minimized .nyan-pill,
      #${BAR_ID}.nyan-minimized .nyan-divider { display: none; }
      #nyan-bar-inner {
        display: flex; align-items: center; gap: 6px;
        background: rgba(255,252,248,.92); backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        border: 1px solid rgba(193,95,60,.15); border-radius: 20px;
        padding: 5px 12px; box-shadow: 0 2px 12px rgba(61,46,34,.1);
        cursor: default; user-select: none;
      }
      @media (prefers-color-scheme: dark) {
        #nyan-bar-inner {
          background: rgba(45,40,35,.92);
          border-color: rgba(218,119,86,.2);
          box-shadow: 0 2px 12px rgba(0,0,0,.3);
        }
        .nyan-pill { color: #D4C8B8 !important; }
        .nyan-pill.green { background: rgba(61,158,122,.2) !important; color: #7DBDA3 !important; }
        .nyan-pill.yellow { background: rgba(230,196,74,.2) !important; color: #E6C44A !important; }
        .nyan-pill.orange { background: rgba(218,119,86,.2) !important; color: #DA7756 !important; }
        .nyan-pill.red { background: rgba(193,95,60,.2) !important; color: #C15F3C !important; }
        .nyan-divider { color: #5A524A !important; }
      }
      #nyan-bar-cat { font-size: 14px; cursor: pointer; transition: transform .2s; }
      #nyan-bar-cat:hover { transform: scale(1.2) rotate(10deg); }
      .nyan-pill {
        background: rgba(244,243,238,.8); color: #7A6B5D;
        padding: 2px 8px; border-radius: 12px; font-weight: 600;
        font-size: 11px; white-space: nowrap; transition: all .3s;
      }
      .nyan-pill.green { background: rgba(61,158,122,.1); color: #3A9E7A; }
      .nyan-pill.yellow { background: rgba(230,196,74,.12); color: #B59A18; }
      .nyan-pill.orange { background: rgba(218,119,86,.12); color: #C15F3C; }
      .nyan-pill.red { background: rgba(193,95,60,.15); color: #A03020; }
      .nyan-pill.nyan-token { background: rgba(184,160,216,.12); color: #8A70B0; }
      .nyan-divider { color: #D4C8B8; font-size: 10px; }
    `;
    document.head.appendChild(style);
  }

  // 建立 DOM（用 textContent 避免 XSS）
  const bar = document.createElement('div');
  bar.id = BAR_ID;

  const inner = document.createElement('div');
  inner.id = 'nyan-bar-inner';

  const cat = document.createElement('span');
  cat.id = 'nyan-bar-cat';
  cat.textContent = '🐱';
  cat.addEventListener('click', () => bar.classList.toggle('nyan-minimized'));

  const pills = [
    { id: 'nyan-5h', title: ct('sessionUsage'), text: '⏱ —' },
    { id: 'nyan-7d', title: ct('weeklyUsage'), text: '📅 —' },
    { id: 'nyan-extra', title: ct('additionalUsage'), text: '💳 —' },
  ];

  inner.appendChild(cat);
  for (const p of pills) {
    const span = document.createElement('span');
    span.id = p.id;
    span.className = 'nyan-pill';
    span.title = p.title;
    span.textContent = p.text;
    inner.appendChild(span);
  }

  const divider = document.createElement('span');
  divider.className = 'nyan-divider';
  divider.textContent = '│';
  inner.appendChild(divider);

  const cost = document.createElement('span');
  cost.id = 'nyan-cost';
  cost.className = 'nyan-pill nyan-token';
  cost.title = ct('realtimeTracking');
  cost.textContent = '⚡ $0';
  inner.appendChild(cost);

  bar.appendChild(inner);
  document.body.appendChild(bar);

  floatingBar = bar;
  return bar;
}

function updateFloatingBar(usageData) {
  if (usageData) cachedUsage = usageData;

  // 等 DOM ready
  if (!document.body) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', () => updateFloatingBar(usageData), { once: true });
    } else {
      requestAnimationFrame(() => updateFloatingBar(usageData));
    }
    return;
  }

  const bar = createFloatingBar();

  // 更新官方用量
  if (cachedUsage?.tiers) {
    const tierMap = [
      { type: 'five_hour', id: 'nyan-5h', icon: '⏱' },
      { type: 'seven_day', id: 'nyan-7d', icon: '📅' },
      { type: 'extra_usage', id: 'nyan-extra', icon: '💳' },
    ];

    for (const { type, id, icon } of tierMap) {
      const tier = cachedUsage.tiers.find(t => t.type === type);
      if (tier?.usagePercent != null) {
        const pct = Math.round(tier.usagePercent);
        const el = bar.querySelector(`#${id}`);
        if (el) {
          el.textContent = `${icon} ${pct}%`;
          el.className = `nyan-pill ${colorClassForPct(pct)}`;
        }
      }
    }
  }

  // 更新即時費用
  if (sessionData.totalCost > 0) {
    const el = bar.querySelector('#nyan-cost');
    if (el) {
      const c = sessionData.totalCost;
      el.textContent = `⚡ $${c < 1 ? c.toFixed(3) : c.toFixed(2)}`;
    }
  }
}

// ── 初始化 ──
chrome.storage.local.get('usageData', (result) => {
  if (result.usageData && !result.usageData.error) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', () => updateFloatingBar(result.usageData), { once: true });
    } else {
      updateFloatingBar(result.usageData);
    }
  }
});

console.log('🐱 Claude 用量喵喵 content script 已載入！');
