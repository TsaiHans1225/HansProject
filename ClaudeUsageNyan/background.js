// background.js — Claude 用量喵喵 service worker
// 定期從 claude.ai 抓取用量資料

// ── 常數 ──
const ALARM_NAME = 'refresh-usage';
const BADGE_UPDATE_NAME = 'update-badge';
const REFRESH_INTERVAL_MINUTES = 1;
const BADGE_UPDATE_SECONDS = 4;
const USAGE_API_URL = 'https://claude.ai/api/organizations';
const FETCH_TIMEOUT = 15000;

// ── 顏色閾值 ──
const COLOR_THRESHOLDS = [
  { max: 50, color: '#7DBDA3' },
  { max: 75, color: '#E6C44A' },
  { max: 90, color: '#DA7756' },
  { max: Infinity, color: '#C15F3C' },
];

let isFetching = false;

// ── 工具函數 ──
function colorForPct(pct) {
  return COLOR_THRESHOLDS.find(t => pct < t.max)?.color ?? '#C15F3C';
}

function formatResetCountdown(resetAt) {
  const diff = new Date(resetAt).getTime() - Date.now();
  if (diff <= 0) return '已重置';
  const d = Math.floor(diff / 86400000);
  const h = Math.floor((diff % 86400000) / 3600000);
  const m = Math.floor((diff % 3600000) / 60000);
  if (d > 0) return d + 'd ' + h + 'h';
  return h + 'h ' + m + 'm';
}

function setBadge(text, color) {
  chrome.action.setBadgeText({ text: text });
  chrome.action.setBadgeBackgroundColor({ color: color });
  chrome.action.setBadgeTextColor({ color: '#FFFFFF' });
}

// ── 初始化 ──
function ensureAlarms() {
  chrome.alarms.create(ALARM_NAME, {
    delayInMinutes: 0.1,
    periodInMinutes: REFRESH_INTERVAL_MINUTES,
  });
  chrome.alarms.create(BADGE_UPDATE_NAME, {
    delayInMinutes: 0.05,
    periodInMinutes: BADGE_UPDATE_SECONDS / 60,
  });
}

// ── 右鍵選單：開啟小工具視窗 ──
var WIDGET_MENU_ID = 'open-widget';
var widgetWindowId = null;

function createContextMenu() {
  chrome.contextMenus.removeAll(function() {
    chrome.contextMenus.create({
      id: WIDGET_MENU_ID,
      title: '開啟用量小工具',
      contexts: ['action'],
    });
  });
}

function openWidgetWindow() {
  if (widgetWindowId != null) {
    chrome.windows.get(widgetWindowId, {}, function(win) {
      if (chrome.runtime.lastError || !win) {
        widgetWindowId = null;
        openWidgetWindow();
        return;
      }
      chrome.windows.update(widgetWindowId, { focused: true });
    });
    return;
  }
  chrome.windows.create({
    url: chrome.runtime.getURL('widget.html'),
    type: 'popup',
    width: 240,
    height: 220,
  }, function(win) {
    if (win) widgetWindowId = win.id;
  });
}

chrome.windows.onRemoved.addListener(function(winId) {
  if (winId === widgetWindowId) widgetWindowId = null;
});

chrome.contextMenus.onClicked.addListener(function(info) {
  if (info.menuItemId === WIDGET_MENU_ID) openWidgetWindow();
});

chrome.runtime.onInstalled.addListener(function() {
  console.log('Claude 用量喵喵已安裝！');
  ensureAlarms();
  createContextMenu();
});

chrome.runtime.onStartup.addListener(function() {
  ensureAlarms();
  createContextMenu();
});

chrome.alarms.onAlarm.addListener(function(alarm) {
  if (alarm.name === ALARM_NAME) fetchUsageData();
  if (alarm.name === BADGE_UPDATE_NAME) updateBadge();
});

// ── 訊息處理 ──
chrome.runtime.onMessage.addListener(function(msg, _sender, sendResponse) {
  switch (msg.type) {
    case 'REFRESH':
      fetchUsageData().then(sendResponse);
      return true;
    case 'GET_USAGE':
      chrome.storage.local.get('usageData', function(r) { sendResponse(r.usageData || null); });
      return true;
    case 'TOKEN_UPDATE':
      chrome.storage.local.set({ tokenData: msg.data });
      return false;
    case 'GET_TOKEN_DATA':
      chrome.storage.local.get('tokenData', function(r) { sendResponse(r.tokenData || null); });
      return true;
    case 'OPEN_WIDGET':
      openWidgetWindow();
      return false;
    case 'WIDGET_FOCUS':
      if (widgetWindowId != null) {
        chrome.windows.update(widgetWindowId, { focused: true });
      }
      return false;
  }
});

// ── 核心：抓取用量資料 ──
async function fetchUsageData() {
  if (isFetching) return null;
  isFetching = true;

  try {
    var fetchOpts = {
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      signal: AbortSignal.timeout(FETCH_TIMEOUT),
    };

    var orgsRes = await fetch(USAGE_API_URL, fetchOpts);
    if (!orgsRes.ok) throw new Error('Orgs: ' + orgsRes.status);

    var orgs = await orgsRes.json();
    if (!orgs || !orgs.length) throw new Error('No organizations');

    var orgId = orgs[0].uuid;
    var usageRes = await fetch(USAGE_API_URL + '/' + orgId + '/usage', fetchOpts);
    if (!usageRes.ok) throw new Error('Usage: ' + usageRes.status);

    var usage = await usageRes.json();
    var data = parseUsageData(usage);
    data.lastUpdated = Date.now();
    data.orgName = orgs[0].name || 'Personal';

    await chrome.storage.local.set({ usageData: data });
    notifyContentScript(data);
    console.log('用量已更新', data);
    return data;

  } catch (err) {
    console.error('抓取失敗:', err);
    var errorData = { error: err.message, lastUpdated: Date.now() };
    await chrome.storage.local.set({ usageData: errorData });
    setBadge('!', '#999');
    return errorData;
  } finally {
    isFetching = false;
  }
}

// ── 通知 content script ──
function notifyContentScript(data) {
  chrome.tabs.query({ url: 'https://claude.ai/*' }, function(tabs) {
    if (!tabs || !tabs.length) return;
    for (var i = 0; i < tabs.length; i++) {
      chrome.tabs.sendMessage(tabs[i].id, { type: 'USAGE_UPDATE', data: data }).catch(function() {});
    }
  });
}

// ── 解析用量資料 ──
function parseUsageData(raw) {
  var result = { tiers: [] };
  var tierMap = [
    { key: 'five_hour', type: 'five_hour', label: 'sessionUsage' },
    { key: 'seven_day', type: 'seven_day', label: 'weeklyUsage' },
  ];

  for (var i = 0; i < tierMap.length; i++) {
    var tm = tierMap[i];
    if (raw[tm.key]) {
      result.tiers.push({
        type: tm.type,
        label: tm.label,
        usagePercent: raw[tm.key].utilization != null ? raw[tm.key].utilization : null,
        resetAt: raw[tm.key].resets_at != null ? raw[tm.key].resets_at : null,
      });
    }
  }

  if (raw.extra_usage && raw.extra_usage.is_enabled) {
    result.tiers.push({
      type: 'extra_usage',
      label: 'additionalUsage',
      usagePercent: raw.extra_usage.utilization != null ? raw.extra_usage.utilization : null,
      used: raw.extra_usage.used_credits != null ? raw.extra_usage.used_credits : null,
      limit: raw.extra_usage.monthly_limit != null ? raw.extra_usage.monthly_limit : null,
    });
  }

  return result;
}

// ── Badge 顯示（固定 5h 使用量，不輪播）──
async function updateBadge() {
  var result = await chrome.storage.local.get(['usageData']);
  var data = result.usageData;

  if (!data || data.error || !data.tiers || !data.tiers.length) {
    setBadge('?', '#999');
    return;
  }

  var fiveHour = data.tiers.find(function(t) { return t.type === 'five_hour'; });
  var sevenDay = data.tiers.find(function(t) { return t.type === 'seven_day'; });

  // Badge: 只固定顯示 5h 使用量
  if (fiveHour && fiveHour.usagePercent != null) {
    var pct = Math.round(fiveHour.usagePercent);
    setBadge(pct + '%', colorForPct(pct));
  } else {
    setBadge('OK', '#7DBDA3');
  }

  // Tooltip: 顯示重置倒數
  var resetParts = [];
  if (fiveHour && fiveHour.resetAt) resetParts.push('5h 重置: ' + formatResetCountdown(fiveHour.resetAt));
  if (sevenDay && sevenDay.resetAt) resetParts.push('7d 重置: ' + formatResetCountdown(sevenDay.resetAt));
  chrome.action.setTitle({ title: resetParts.join('\n') || '已顯示用量' });
}
