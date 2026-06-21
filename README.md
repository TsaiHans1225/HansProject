# HansProject

這個 repository 下包含兩個獨立的子專案：

| 子專案 | 說明 | 技術 |
|---|---|---|
| [`TrayWidget/`](TrayWidget/) | Windows 系統匣（system tray）小工具 | C# / .NET Framework (WinForms) |
| [`WC2026/`](WC2026/) | 2026 世界盃 48 隊資料視覺化網頁 | 靜態 HTML + JavaScript |

---

## TrayWidget

Windows 桌面常駐小工具（WinForms）。

- 進入點：`TrayWidget/Program.cs`、主視窗 `TrayWidgetForm.cs`
- 方案檔：`TrayWidget/TrayWidget.sln`（用 Visual Studio 開啟）
- 套件：以 `packages.config` 管理；首次建置時 Visual Studio / NuGet 會自動還原 `packages/`
- `bin/`、`obj/`、`.vs/`、`packages/` 為建置產物與還原內容，**不納入版控**（見 `.gitignore`）

## WC2026

2026 世界盃（美加墨，48 隊）球員聯賽／俱樂部／身價／戰鬥力的互動式排行網頁。

- 主頁面：`WC2026/WC2026_Big5_Ranking.html`（直接用瀏覽器開啟即可）
- 資料檔（與 HTML 同目錄、由 HTML 自動載入）：
  - `clubdata.js` — 各隊俱樂部分布
  - `clubmeta.js` — 俱樂部台灣譯名 + 五大聯賽標記
- `WC2026_Big5_Ranking.md` — Markdown 版排行表

> 詳細功能說明見 [`WC2026/README.md`](WC2026/README.md)。
