# Docker SQL Server 2019 開發環境

本機 Docker 跑的 SQL Server 2019 Developer Edition，給開發/測試用。
所有 DB（`PXCWMS_K`、`mid_WES`、`ERP`、`APCNT`...）都存在 named volume 裡，靠整包 volume 備份/還原來搬移與回復。

| 項目 | 值 |
|---|---|
| Container | `mssql-2019-dev` |
| Port | `1433` |
| 帳號 / 密碼 | `sa` / `YourPassword123!` |
| Collation | `Chinese_Taiwan_Stroke_CI_AS` |
| Volume | `docker-mssql2019_mssql2019-data` |

> compose 有加 `-T272` trace flag：關閉 IDENTITY cache，避免容器重啟後流水號跳 1000/10000。

---

## 容器操作

```powershell
docker compose up -d        # 啟動
docker compose stop         # 暫停
docker compose down         # 移除容器（資料留在 volume）
docker ps                   # 確認執行中
```

---

## 資料夾結構

```
docker-mssql2019/
├── docker-compose.yml
├── README.md
├── backup/                ← 整顆 docker volume 備份
│   ├── scripts/           ← backup.ps1 / restore.ps1
│   └── data/              ← mssql-image.tar / mssql-volume.tar.gz (gitignore)
└── bcp/
    ├── scripts/           ← clean-for-backup.ps1 (清交易資料、留主檔)
    ├── data/              ← BCP 匯出的 .dat 主檔資料 (gitignore)
    └── docs/              ← BCP 表清單細節說明
```

`.ps1` 透過 `$PSScriptRoot` 相對找其他目錄，搬資料夾要連帶改腳本內路徑。

---

## 備份與還原

統一用整包 volume 備份（含 image、所有 DB、Login、設定），不用維護 DB 清單：

| 想做 | 跑這個 |
|---|---|
| 備份 | `.\backup\scripts\backup.ps1` |
| 還原 | `.\backup\scripts\restore.ps1` |

```powershell
.\backup\scripts\backup.ps1     # 會停容器幾分鐘，跑完自動重啟
```

輸出在 `backup\data\`：
- `mssql-image.tar`（~530 MB）
- `mssql-volume.tar.gz`（依資料量）

還原時若本機已有同名 volume，腳本會要求輸入 `YES` 確認後才覆蓋。

**換電腦**：裝好 Docker Desktop → 把整個 `docker-mssql2019\` 資料夾複製過去 → 跑 `.\backup\scripts\restore.ps1`。

---

## 製作「乾淨資料」備份

測試把資料弄髒後，想回到「只有主檔、沒有交易資料」的狀態，流程是：

```powershell
.\bcp\scripts\clean-for-backup.ps1   # 1. 清空交易/暫存/log 表，主檔不動
.\backup\scripts\backup.ps1          # 2. 壓一份乾淨的 volume 備份
```

`clean-for-backup.ps1` 做的事：
- **PXCWMS_K**：依「子表 → 父表」順序 DELETE 約 80 張交易表（訂單/撿貨/進貨/退倉/立庫/暫存/log），並把 `slot_mer` 的分配量（`reserqty`/`replqty`）歸零 — 庫存量不動
- **mid_WES**：動態抓所有 `mid_*` 中介表清空（只留 `mid_config`），日後新增中間表會自動納入
- **ERP**：只清操作 log 表，USERS / 權限 / 選單都不動
- **IDENTITY 重置**：清掉的表全部 `DBCC CHECKIDENT RESEED 0`，新建的單 sysno 從 1 重新編（DELETE 本身不會重置計數器）

⚠️ 只能對本機 docker 跑，不要對正式環境跑。

日後測試弄髒了：`restore.ps1` 還原乾淨備份即可。

---

## 排程備份（可選）

Windows 工作排程器 → 新增工作：
- 觸發：每天凌晨 2:00
- 動作：`powershell.exe -ExecutionPolicy Bypass -File D:\docker-mssql2019\backup\scripts\backup.ps1`

⚠️ 整包備份會**停容器幾分鐘**，別排在白天上班時間。

---

## 常見錯誤

| 錯誤 | 解法 |
|---|---|
| 還原時 volume 已存在 | restore.ps1 會問，輸入 `YES` 覆蓋；或先 `docker compose down` + `docker volume rm` |
| `mssql-tools/bin/sqlcmd: no such file` | 新版 image 路徑是 `mssql-tools18`，且 sqlcmd 要加 `-C` 參數 |
| 中文 `varchar` 變 `?` | DB collation 必須是 `Chinese_Taiwan_Stroke_CI_AS`（compose 已設） |
| 容器啟動失敗 `mkdir ... read-only file system` | Docker Desktop 沒分享該磁碟，把 compose 的 bind mount 移除或改用 named volume |
