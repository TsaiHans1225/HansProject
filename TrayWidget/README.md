# TrayWidget — 系統托盤資訊小工具

Windows 桌面常駐的系統匣（system tray）小工具，一眼掌握天氣、台股、新聞與 Claude 用量。深色主題、WinForms 自繪 UI。

## 功能

- 🌤 **即時天氣 + 5 天預報** — 來源 [Open-Meteo](https://open-meteo.com/)，**免費、免 API key**；預設台北（緯經度 25.03 / 121.57），含降雨機率
- 📈 **台股** — 加權指數 + 最多 4 檔自選個股漲跌（來源 Yahoo Finance）
- 📰 **新聞** — Bing 新聞 RSS（台灣），可翻頁、點擊開啟原文
- 🐱 **Claude 用量** — 5 小時 / 7 天用量百分比與重置倒數；托盤圖示直接以數字 + 顏色顯示 5h 用量
- 🚫 **防止螢幕休眠** — 可從右鍵選單切換

### Claude 用量怎麼來的

兩段式，API 優先、失敗才走備援：

1. **直接 API（主要）** — 自動掃描所有 Chrome profile 的 cookie，逐一試打 `claude.ai/api/.../usage`，哪個 profile 有效登入就用哪個。
2. **LevelDB 備援** — 若 API 全失敗，改讀 [ClaudeUsageNyan](../ClaudeUsageNyan/) 擴充寫入 Chrome 的用量資料（掃所有 profile，挑 `lastUpdated` 最新的）。

> 若用量顯示讀取失敗，多半是 **claude.ai 沒登入** —— 在任一 Chrome profile 登入後，下次更新（每分鐘一次）就會自動抓到。

## 使用方式

- **左鍵**點托盤圖示 → 彈出資訊面板
- **右鍵**托盤圖示 → 重新整理 / 設定個股 / 防止休眠 / 結束

### 設定個股

預設 `00631L.TW`（元大台灣50正2）、`0050.TW`（元大台灣50）、`2330.TW`（台積電）、`00981A.TW`。

- 執行後右鍵 → **「設定個股」**，輸入逗號分隔代號即時修改（最多 4 檔），或
- 改 [`TrayWidgetForm.cs`](TrayWidgetForm.cs) 的 `stockList` 預設值。

### 改天氣地點

修改 [`TrayWidgetForm.cs`](TrayWidgetForm.cs) 的 `weatherLat` / `weatherLon` / `weatherCityName`（緯經度制，非城市名）。

## 建置

**環境需求**：Windows 10/11、Visual Studio 2022（含 .NET desktop 開發工作負載）。本專案是 **.NET Framework 4.8 + WinForms**，用 `packages.config` 管理 NuGet（非 SDK-style，故 `dotnet build` 不適用）。

### Visual Studio（建議）

1. 開啟 [`TrayWidget.sln`](TrayWidget.sln)
2. 按 `F5` 執行，或 `Ctrl+Shift+B` 建置（首次會自動還原 NuGet 套件）

### 命令列（MSBuild）

```powershell
# 先還原套件（VS 內或 nuget.exe）
nuget restore TrayWidget.sln
# 建置
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TrayWidget.csproj -t:Build -p:Configuration=Release
```

產物：`bin\Release\TrayWidget.exe`。

## 開機自動啟動（選用）

1. 取得 `bin\Release\TrayWidget.exe` 路徑
2. `Win+R` → 輸入 `shell:startup`
3. 在開啟的資料夾建立該 exe 的捷徑

## NuGet 套件

| 套件 | 用途 |
|---|---|
| `Newtonsoft.Json` | 解析天氣 / 股市 / 用量 JSON |
| `System.Data.SQLite.Core` + `Stub.*` | 讀取 Chrome Cookies SQLite |
| `Portable.BouncyCastle` | AES-256-GCM 解密 Chrome cookie |
| `LevelDB.Standard` | 讀取擴充寫入的 LevelDB 用量資料 |
