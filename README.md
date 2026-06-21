# HansProject

個人雜項工具的 monorepo，底下是四個各自獨立、互不相依的子專案：

| 子專案 | 說明 | 技術 |
|---|---|---|
| [`TrayWidget/`](TrayWidget/) | Windows 系統匣常駐小工具：天氣 / 台股 / 新聞 / **Claude 用量** | C# · .NET Framework 4.8 (WinForms) |
| [`WC2026/`](WC2026/) | 2026 世界盃 48 隊互動式排行網頁 | 靜態 HTML + JavaScript |
| [`docker-mssql2019/`](docker-mssql2019/) | 本機 Docker 跑的 SQL Server 2019 開發環境 + 整包 volume 備份/還原 | Docker Compose · PowerShell |
| [`ClaudeUsageNyan/`](ClaudeUsageNyan/) | Claude 用量追蹤 Chrome 擴充（[上游](https://github.com/zarqarwi/claude-usage-nyan) fork） | Chrome Extension MV3 |

> 每個子專案都不綁定 `C:\HansProject` 路徑，clone 到任何位置（含 D 槽）都能用。唯一例外：ClaudeUsageNyan 以「載入未封裝」裝進 Chrome 後，搬資料夾需要在 `chrome://extensions` 重新載入。

---

## TrayWidget

Windows 桌面常駐小工具（WinForms），點工作列托盤圖示即可看到天氣、台股、新聞與 Claude 用量。

- 進入點 [`Program.cs`](TrayWidget/Program.cs)、主視窗 [`TrayWidgetForm.cs`](TrayWidget/TrayWidgetForm.cs)
- 方案檔 [`TrayWidget.sln`](TrayWidget/TrayWidget.sln)（Visual Studio 開啟）
- 套件由 `packages.config` 管理，首次建置自動還原 `packages/`

> 完整功能與建置說明見 [`TrayWidget/README.md`](TrayWidget/README.md)。

## WC2026

2026 世界盃（美加墨，48 隊）的球員聯賽／俱樂部／身價／戰鬥力互動式排行網頁。

- 主頁面 [`WC2026_Big5_Ranking.html`](WC2026/WC2026_Big5_Ranking.html)，雙擊用瀏覽器開啟即可，無需後端
- 資料檔 `clubdata.js`、`clubmeta.js` 由 HTML 自動載入

> 詳細說明見 [`WC2026/README.md`](WC2026/README.md)。

## docker-mssql2019

本機開發用的 SQL Server 2019 Developer Edition（Docker），DB 存在 named volume，靠整包 volume 備份/還原搬移與回復。

- `docker compose up -d` 啟動，連線 `localhost:1433`
- 備份/還原腳本在 `backup/scripts/`

> 容器操作、備份還原、清乾淨資料流程見 [`docker-mssql2019/README.md`](docker-mssql2019/README.md)。

## ClaudeUsageNyan

追蹤 claude.ai 用量的 Chrome 擴充（fork 自 [zarqarwi/claude-usage-nyan](https://github.com/zarqarwi/claude-usage-nyan)）。在工具列 badge 顯示 5 小時／7 天用量，並提供官方數據 vs 即時 token 推算的雙來源對照。

> 安裝與架構說明見 [`ClaudeUsageNyan/README.md`](ClaudeUsageNyan/README.md)。TrayWidget 的 Claude 用量備援來源即是讀取本擴充寫入的資料。
