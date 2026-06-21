# 新開倉資料初始化

把舊倉的資料灌到新倉(或 docker 開發機),分四階段灌完 **63 張表**。

接續 [`移植資料庫.md`](移植資料庫.md) — 那份文件講「怎麼搬 schema」,這份講「**怎麼灌資料**」。
新環境一鍵建置流程見 [`新環境一鍵建置.md`](新環境一鍵建置.md)。

---

## 流程總覽

```
階段 0: schema 已建好 (移植資料庫.md / 新環境一鍵建置.md)
   ↓
階段 1: 1_bcp-seed-essential.ps1   ← PXCWMS_K 必要設定 (11 張)
   ↓
階段 2: 2_bcp-seed-master.ps1      ← PXCWMS_K 業務主檔 (22 張)
   ↓
階段 3: 3_bcp-seed-stock.ps1       ← PXCWMS_K 庫存 + 立庫設備 (7 張)
   ↓
階段 4: 4_bcp-seed-erp-apcnt.ps1   ← ERP 20 張 + APCNT 3 張
   ↓
階段 5: 驗證 + 客製調整
```

| 階段 | 腳本 | DB | 表數 | 用途 | 來源 |
|---|---|---|---|---|---|
| 1 | [`../scripts/1_bcp-seed-essential.ps1`](../scripts/1_bcp-seed-essential.ps1) | PXCWMS_K | 11 | 系統運作必要設定 | 172.20.131.6 |
| 2 | [`../scripts/2_bcp-seed-master.ps1`](../scripts/2_bcp-seed-master.ps1) | PXCWMS_K | 22 | 業務主檔 (員工/貨主/商品/儲位/客戶/資產 + 客戶播種區儲位) | 172.20.131.6 |
| 3 | [`../scripts/3_bcp-seed-stock.ps1`](../scripts/3_bcp-seed-stock.ps1) | PXCWMS_K | 7 | 庫存資料 (`slot_mer`) + 立庫設備 (`ASRS_DEVICE` / `CRANE_DATA`) | 172.20.131.6 |
| 4 | [`../scripts/4_bcp-seed-erp-apcnt.ps1`](../scripts/4_bcp-seed-erp-apcnt.ps1) | ERP / APCNT | 23 | 系統設定 + 使用者主檔 (跳過 LOG) | 192.168.120.100 |

兩台來源 server 不同,連線資訊都寫在各腳本檔頭,要改去檔案裡改。

> ⚠️ **三個前置條件**
> 1. **DB collation 必須是 `Chinese_Taiwan_Stroke_CI_AS`**,否則中文 `varchar` 會變 `?`(看 [`新環境一鍵建置.md`](新環境一鍵建置.md))
> 2. **schema 必須先建好**(BCP 不能匯 schema)
> 3. **`.ps1` 必須是 UTF-8 with BOM**,否則 PowerShell 5.1 用 Big5 讀檔會 parser 錯誤

---

## 階段 1: 必要設定資料 (11 張)

```powershell
.\1_bcp-seed-essential.ps1
```

### 跨倉通用 (直接複製,內容不用改)

| # | Table | 用途 | 筆數查詢 | 備註 |
|---|---|---|---|---|
| 1 | `gridcolname` | 系統各畫面表格的欄位定義 | `SELECT COUNT(*) FROM gridcolname` | 第一張匯,被很多 UI 邏輯參照 |
| 2 | `basic_data` | 基本主檔 (溫層、單位、各種 enum) | `SELECT COUNT(*) FROM basic_data` | 例: `WHERE S_basd_column='Temperature'` |
| 3 | `option_data` | 下拉選單選項 | `SELECT COUNT(*) FROM option_data` | 例: `WHERE S_optd_fieldname='dirh_flag'` |
| 4 | `jobnames` | 職務 / 任務名稱 | `SELECT COUNT(*) FROM jobnames` | 被 `auth_user` 參照 |
| 5 | `predefine_data` | 系統運作數據 (舊倉提供的初始值) | `SELECT COUNT(*) FROM predefine_data` | 含 `ASRS_OUTSLODID` 等立庫出口暫存設定 |
| 6 | `supplyer_group` | 供應商分組 | `SELECT COUNT(*) FROM supplyer_group` | 被 `supplyer_data` 參照 |
| 7 | `slot_control` | 儲位控管規則 | `SELECT COUNT(*) FROM slot_control` | 通常只有一筆全域設定 |

### 各倉客製 (匯完還要改內容,見階段 6)

| # | Table | 用途 | 筆數查詢 | 為什麼客製 |
|---|---|---|---|---|
| 8 | `SYS_PARAMETER` | 系統參數 (key-value) | `SELECT COUNT(*) FROM SYS_PARAMETER` | `TMS_DBNAME` / 倉別代碼,新倉的 TMS 對接點不同 |
| 9 | `dc_data` | 配送中心資料 | `SELECT COUNT(*) FROM dc_data` | 每倉一筆,代碼/名稱/地址不同 |
| 10 | `WORKSTATION_DATA` | 工作站(實體 PC 對應) | `SELECT COUNT(*) FROM WORKSTATION_DATA` | 新倉現場 PC 名稱不同,通常清空重建 |
| 11 | `defprintjob` | 列印任務 (印表機型號 / 路徑) | `SELECT COUNT(*) FROM defprintjob` | 新倉的印表機型號 / 路徑可能不同 |

> `dc_data` 排在 `WORKSTATION_DATA` / `defprintjob` 前面,後兩者參照 `dc_data` 的倉別代碼。

---

## 階段 2: 業務主檔資料 (22 張)

```powershell
.\2_bcp-seed-master.ps1
```

**前提:階段 1 已跑過**(這些主檔依賴 `jobnames` / `dc_data` / `supplyer_group` 等)。

### 基礎主檔 (無業務 FK 依賴,先匯)

| # | Table | 用途 | 筆數查詢 | 備註 |
|---|---|---|---|---|
| 1 | `employee_data` | 員工主檔 | `SELECT COUNT(*) FROM employee_data` | 被 `auth_user` 參照,測試可只留 2304595 |
| 2 | `owner_data` | 貨主主檔 | `SELECT COUNT(*) FROM owner_data` | 被 `owner_sbu` / `owner_extra` / `mer_data` 參照 |
| 3 | `supplyer_data` | 供應商主檔 | `SELECT COUNT(*) FROM supplyer_data` | 依賴階段 1 的 `supplyer_group` |
| 4 | `customer_data` | 客戶主檔 | `SELECT COUNT(*) FROM customer_data` | 訂單收貨方 |
| 5 | `car_type` | 車型 | `SELECT COUNT(*) FROM car_type` | 被 `car_data` 參照 |
| 6 | `mer_categ` | 商品分類 | `SELECT COUNT(*) FROM mer_categ` | 被 `mer_data` 參照 |
| 7 | `asset_data` | **資產主檔 (籠車/物流箱/Maobag 等承載物)** ★ PDA 揀貨確認必需 | `SELECT COUNT(*) FROM asset_data` | 沒這張會出現「資產主檔未設定」錯誤 |

### 次層主檔 (依賴上述)

| # | Table | 用途 | 筆數查詢 | 依賴 |
|---|---|---|---|---|
| 8 | `auth_user` | 使用者權限 | `SELECT COUNT(*) FROM auth_user` | `employee_data` + `jobnames` |
| 9 | `owner_sbu` | 貨主事業群組 | `SELECT COUNT(*) FROM owner_sbu` | `owner_data` |
| 10 | `owner_extra` | 貨主庫存屬性控管 | `SELECT COUNT(*) FROM owner_extra` | `owner_data` |
| 11 | `car_data` | 車輛主檔 | `SELECT COUNT(*) FROM car_data` | `car_type` |
| 12 | `mer_data` | 商品主檔 | `SELECT COUNT(*) FROM mer_data` | `mer_categ` + `owner_data` |
| 13 | `mer_package` | 商品包裝 | `SELECT COUNT(*) FROM mer_package` | `mer_data`。每商品至少一小包裝 (`I_merp_1qty=1`) + 一大包裝 (`I_merp_boxflag=1`) |

### 儲位 / 區段體系

| # | Table | 用途 | 筆數查詢 | 備註 |
|---|---|---|---|---|
| 14 | `pick_zone` | 進貨/退貨/揀貨暫存區 | `SELECT COUNT(*) FROM pick_zone` | 被 `slot_data` 參照 |
| 15 | `slot_data` | 進貨/退貨/揀貨儲位 | `SELECT COUNT(*) FROM slot_data` | 依賴 `pick_zone` |
| 16 | `ASRS_DATA` | 立庫儲位狀態 | `SELECT COUNT(*) FROM ASRS_DATA` | `S_ASRD_CONTAINER IS NULL` 表示可用空儲位 |
| 17 | `sector_head` | 儲位區段表頭 | `SELECT COUNT(*) FROM sector_head` | 例: `S_sech_id='STK-ZZ'` |
| 18 | `sector_item` | 儲位區段明細 | `SELECT COUNT(*) FROM sector_item` | 依賴 `sector_head` + `slot_data` |
| 19 | `customer_slod` | **客戶播種區儲位對應** ★ PDA 立庫彙總揀貨 / 02-06 維護必需 | `SELECT COUNT(*) FROM customer_slod` | `customer_data` + `slot_data` + `pick_zone` (DAS-F 等播種區) |

### 月台 / 報表 / 列印

| # | Table | 用途 | 筆數查詢 | 備註 |
|---|---|---|---|---|
| 20 | `dock_data` | 卡口 / 月台資料 | `SELECT COUNT(*) FROM dock_data` | 卸貨 / 出貨月台位置 |
| 21 | `export_excel` | F-3 報表匯出設定 | `SELECT COUNT(*) FROM export_excel` | 報表欄位對應 |
| 22 | `printexe_def` | 列印機匯入設定 | `SELECT COUNT(*) FROM printexe_def` | 與階段 1 `defprintjob` 不同,這是匯入端 |

---

## 階段 3: 庫存 + 立庫設備資料 (7 張)

```powershell
.\3_bcp-seed-stock.ps1
```

**前提:階段 2 已跑過**。涵蓋兩件事:
1. 讓 `pxEWMS_OrderGeneration` 找到「商品在哪個儲位、剩多少數量」(`slot_mer`)
2. 讓**截單試算 SP** 找到立庫設備可派工 (`CRANE_DATA` / `ASRS_DEVICE`)

| # | Table | 用途 | 筆數查詢 | 依賴 |
|---|---|---|---|---|
| 1 | `mer_status` | 商品狀態 | `SELECT COUNT(*) FROM mer_status` | 被 `slot_mer` 參照 |
| 2 | `mer_list` | 商品清單延伸 | `SELECT COUNT(*) FROM mer_list` | `mer_data` |
| 3 | `CRANE_DATA` | 堆高機 / 起重機主檔 | `SELECT COUNT(*) FROM CRANE_DATA` | 被 `ASRS_DEVICE` 參照 |
| 4 | `ASRS_DEVICE` | **立庫設備 (升降梯/車輛)** ★ 截單試算必需 | `SELECT COUNT(*) FROM ASRS_DEVICE` | `CRANE_DATA` |
| 5 | `ASRSC_DATA` | 立庫容器資料 | `SELECT COUNT(*) FROM ASRSC_DATA` | `ASRS_DATA` |
| 6 | `ASRSJOBERR_CONTAINER` | 立庫錯誤容器記錄 | `SELECT COUNT(*) FROM ASRSJOBERR_CONTAINER` | `ASRSC_DATA` |
| 7 | `slot_mer` | **儲位庫存** ★ 訂單生成必需 | `SELECT COUNT(*) FROM slot_mer` | `mer_data` + `slot_data` + `ASRSC_DATA` |

> **為什麼立庫設備關鍵?** 截單試算 SP (`SP_Order_TrialOrdData` / `sp_Order_TrialSlotMaxRepl` / `sp_Order_TrialSlotPiecePRepl` / `sp_Order_TrialSlotTiHiPRepl`) 會去查 `ASRS_DEVICE` 找可派工的設備。沒資料 → 試算找不到設備 → 「庫存總分配量=0」「庫存不足量=N」,即使 `slot_mer` 明明有庫存也會試算失敗。

> 訂單/揀貨/批次的「交易性資料」(`order_head` / `pick_head` / `group_item` 等)**預設不匯** — 量太大(`order_item` 1100 萬、`pick_item` 1300 萬筆),且程式會自己 INSERT。要匯時把腳本內 `# $Tables += @(...)` 區塊解除註解。

---

## 階段 4: ERP + APCNT 資料 (23 張)

```powershell
.\4_bcp-seed-erp-apcnt.ps1
```

**來源 server 不同**:`192.168.120.100 / pxwms_poc`(不是 PXCWMS_K 那台 `172.20.131.6`)。
**來源 DB 名稱不同**:`APCNT_O` / `ERP_O`(目標是 `APCNT` / `ERP`)。

### APCNT (3 張) — 連線設定

| # | Table | 用途 | 筆數查詢 |
|---|---|---|---|
| 1 | `XSC_NETSERVER` | 伺服器設定 | `SELECT COUNT(*) FROM APCNT.dbo.XSC_NETSERVER` |
| 2 | `XSC_NET_DBALIAS` | DB 別名對照 | `SELECT COUNT(*) FROM APCNT.dbo.XSC_NET_DBALIAS` |
| 3 | `XSC_NET_CONNECT_DEFINITION_PC` | PC 連線定義 | `SELECT COUNT(*) FROM APCNT.dbo.XSC_NET_CONNECT_DEFINITION_PC` |

### ERP (20 張)

#### 系統設定 / 主檔 (8 張)

| # | Table | 用途 | 筆數查詢 |
|---|---|---|---|
| 1 | `PARAMDEFINE` | 參數定義 | `SELECT COUNT(*) FROM ERP.dbo.PARAMDEFINE` |
| 2 | `MENUITEMTYPE` | 選單類型 | `SELECT COUNT(*) FROM ERP.dbo.MENUITEMTYPE` |
| 3 | `MENUTABLE` | 選單定義 | `SELECT COUNT(*) FROM ERP.dbo.MENUTABLE` |
| 4 | `MENUAUTH` | 選單權限 | `SELECT COUNT(*) FROM ERP.dbo.MENUAUTH` |
| 5 | `Portal_Menu` | Portal 選單 | `SELECT COUNT(*) FROM ERP.dbo.Portal_Menu` |
| 6 | `SiteAuthority` | 站台權限 | `SELECT COUNT(*) FROM ERP.dbo.SiteAuthority` |
| 7 | `USERDEFINE` | 使用者欄位定義 | `SELECT COUNT(*) FROM ERP.dbo.USERDEFINE` |
| 8 | `ORG` | 組織 | `SELECT COUNT(*) FROM ERP.dbo.ORG` |

#### 使用者 / 群組 / 權限 (10 張)

| # | Table | 用途 | 筆數查詢 |
|---|---|---|---|
| 9 | `GROUPS` | 群組 | `SELECT COUNT(*) FROM ERP.dbo.GROUPS` |
| 10 | `USERS` | 使用者主檔 | `SELECT COUNT(*) FROM ERP.dbo.USERS` |
| 11 | `USERSADM` | 管理員 | `SELECT COUNT(*) FROM ERP.dbo.USERSADM` |
| 12 | `USERSDEV` | 開發者 | `SELECT COUNT(*) FROM ERP.dbo.USERSDEV` |
| 13 | `SYS_EEP_USERS` | EEP 使用者 | `SELECT COUNT(*) FROM ERP.dbo.SYS_EEP_USERS` |
| 14 | `USERGROUPS` | 使用者-群組關聯 | `SELECT COUNT(*) FROM ERP.dbo.USERGROUPS` |
| 15 | `USERMENUS` | 使用者-選單權限 | `SELECT COUNT(*) FROM ERP.dbo.USERMENUS` |
| 16 | `GROUPMENUS` | 群組-選單權限 | `SELECT COUNT(*) FROM ERP.dbo.GROUPMENUS` |
| 17 | `USERFUNCS` | 使用者功能 | `SELECT COUNT(*) FROM ERP.dbo.USERFUNCS` |
| 18 | `USER_TOKEN` | 有效 token | `SELECT COUNT(*) FROM ERP.dbo.USER_TOKEN` |

#### 版本 / 發布 (2 張)

| # | Table | 用途 | 筆數查詢 |
|---|---|---|---|
| 19 | `MENU_VERSION` | 選單版本 | `SELECT COUNT(*) FROM ERP.dbo.MENU_VERSION` |
| 20 | `MENU_RELEASE` | 選單發布 | `SELECT COUNT(*) FROM ERP.dbo.MENU_RELEASE` |

### 跳過的 LOG / HISTORY 表

| DB | Table | 筆數參考 | 為什麼跳過 |
|---|---|---|---|
| APCNT | `XSC_NET_CONNECTION_HISTORY` | ~50,000 | 連線歷史 LOG |
| APCNT | `XSC_NET_CONNECTION` | ~18,000 | 即時連線記錄 |
| APCNT | `XSC_NETERROR_LOG` | ~5,600 | 錯誤 LOG |
| ERP | `ERP_INFO_LOG` | ~58,000 | 訊息 LOG |
| ERP | `ERP_CLICK_LOG` | ~33,000 | 點擊 LOG |
| ERP | `ERP_EXE_LOG` | ~8,000 | 執行 LOG |
| ERP | `USER_TOKEN_LOG` | ~4,000 | Token 操作 LOG |
| ERP | `USER_TOKEN_HISTORY` | ~3,800 | Token 歷史 |
| ERP | `MENU_RELEASE_LOG` | ~800 | 發布 LOG |
| ERP | `MenuClickCount` | ~250 | 點擊統計 |

> 都是執行期累積的資料,新環境用不到。要的話到腳本內 `$Plans` 加進去就好(兩個 DB 都沒 FK,順序自由)。

---

## 四支腳本的共同行為

- 每張表 `bcp out` → `NOCHECK CONSTRAINT ALL` + `DELETE FROM` → `bcp in`
- 全部跑完統一 `WITH CHECK CHECK CONSTRAINT ALL` 重新驗證 FK
- 任一張失敗就 `throw` 中止,並印出 bcp / sqlcmd 完整錯誤訊息
- **可重複執行**(會清空再灌)
- ⚠️ `DELETE FROM` 會清掉目標表現有資料,確認 target 是測試環境再跑

> 為什麼用 `DELETE FROM` 不用 `TRUNCATE`?SQL Server 規則:**只要有 FK 參照這張表(即使 NOCHECK 停用)就不能 TRUNCATE**。`owner_data` / `mer_data` 等被多張表參照的主檔會卡住,所以統一用 `DELETE`。

---

## 階段 5: 驗證

### 5.1 PXCWMS_K 全部 40 張表的筆數

來源 / target 都跑這段,輸出比對。差距太大或筆數 0 就回去看 BCP log。

```sql
USE PXCWMS_K
SELECT
    t.name AS TableName,
    i.rows AS RowCounts
FROM sys.tables t
INNER JOIN sys.sysindexes i ON t.object_id = i.id
WHERE i.indid < 2
  AND t.name IN (
        -- 階段 1 必要設定 (11)
        'gridcolname', 'basic_data', 'option_data', 'jobnames',
        'predefine_data', 'supplyer_group', 'slot_control',
        'SYS_PARAMETER', 'dc_data', 'WORKSTATION_DATA', 'defprintjob',
        -- 階段 2 業務主檔 (22)
        'employee_data', 'auth_user', 'owner_data', 'owner_sbu', 'owner_extra',
        'supplyer_data', 'customer_data', 'car_type', 'car_data',
        'mer_categ', 'mer_data', 'mer_package', 'asset_data',
        'pick_zone', 'slot_data', 'ASRS_DATA', 'sector_head', 'sector_item', 'customer_slod',
        'dock_data', 'export_excel', 'printexe_def',
        -- 階段 3 庫存 + 立庫設備 (7)
        'mer_status', 'mer_list', 'CRANE_DATA', 'ASRS_DEVICE',
        'ASRSC_DATA', 'ASRSJOBERR_CONTAINER', 'slot_mer'
      )
ORDER BY t.name
```

### 5.2 ERP + APCNT 有資料的表 (應該共 23 張)

```sql
SELECT 'ERP' AS db, t.name AS TableName, i.rows AS RowCounts
FROM ERP.sys.tables t
INNER JOIN ERP.sys.sysindexes i ON t.object_id = i.id
WHERE i.indid < 2 AND i.rows > 0
UNION ALL
SELECT 'APCNT', t.name, i.rows
FROM APCNT.sys.tables t
INNER JOIN APCNT.sys.sysindexes i ON t.object_id = i.id
WHERE i.indid < 2 AND i.rows > 0
ORDER BY db, RowCounts DESC
```

### 5.3 中文沒亂碼

```sql
SELECT TOP 3 N_picz_name FROM PXCWMS_K.dbo.pick_zone
```

預期看到 `冷凍棧式立庫` 等中文,**不能是 `??`**。如果亂碼,看[常見錯誤](#常見錯誤)的 collation 那一條。

### 5.4 訂單生成測試

開 `pxEWMS_OrderGeneration` 工具,應能查到商品 + 可揀貨庫存。

---

## 階段 6: 客製調整

舊倉資料灌進來後,以下表**內容不能直接用**,要改成新倉自己的設定。

### 階段 1 PXCWMS_K 必要設定的客製表

| Table | 改什麼 | 怎麼查 |
|---|---|---|
| `dc_data` | 配送中心代碼 / 名稱 / 地址 | `SELECT * FROM dc_data` |
| `SYS_PARAMETER` | `TMS_DBNAME`、倉別代碼 | `SELECT * FROM SYS_PARAMETER WHERE S_SYSP_ID LIKE '%DBNAME%'`<br>`... LIKE '%DC%'` / `... LIKE '%WAREHOUSE%'` |
| `WORKSTATION_DATA` | 實體 PC 對應 | `SELECT * FROM WORKSTATION_DATA`,通常清空後用 WMS UI 重建 |
| `defprintjob` | 印表機型號 / 路徑 | `SELECT * FROM defprintjob` |

### 階段 2 PXCWMS_K 業務主檔

依新倉現場狀況決定:**保留 / 全部清空 / 部分調整**。

| Table | 常見處理 |
|---|---|
| `employee_data` / `auth_user` | 通常清空,等新倉員工進來再建(測試可保留 2304595) |
| `owner_data` / `owner_sbu` / `owner_extra` | 看新倉貨主而定,通常重建 |
| `supplyer_data` / `customer_data` | 各倉不同,通常清空 |
| `mer_categ` / `mer_data` / `mer_package` | 商品結構各倉不同,需重建 |
| `car_type` / `car_data` | 車輛各倉獨立 |
| `asset_data` | 籠車 / 物流箱 / Maobag 等承載物,各倉編號規則不同,通常重建 |
| `pick_zone` / `slot_data` / `sector_head` / `sector_item` | **必須重建** — 儲位佈局完全不同 |
| `ASRS_DATA` | **必須重建** — 立庫物理結構不同 |
| `dock_data` | 卡口位置不同 |
| `export_excel` / `printexe_def` | 通常可沿用 |

### 階段 3 PXCWMS_K 庫存 + 立庫設備

| Table | 常見處理 |
|---|---|
| `mer_status` | 系統狀態定義,通常沿用 |
| `mer_list` | 看是否需要該倉商品延伸資料 |
| `CRANE_DATA` / `ASRS_DEVICE` | **必須依新倉設備調整** — 各倉的堆高機/升降梯/車輛數量型號不同 |
| `ASRSC_DATA` / `ASRSJOBERR_CONTAINER` | 跟 `ASRS_DATA` 同步 — 立庫重建就一起重建 |
| `slot_mer` | **新倉開倉前清空**,等實際進貨後才有庫存 |

### 階段 4 ERP / APCNT

| Table | 常見處理 |
|---|---|
| APCNT 連線設定 (`XSC_NETSERVER` / `XSC_NET_DBALIAS` / `XSC_NET_CONNECT_DEFINITION_PC`) | **必改** — 新環境的 server / DB 別名 / PC 名稱不同 |
| ERP `ORG` | 組織代碼依新倉設定 |
| ERP `USERS` 系列 | 通常清空,等新倉人員進來再建 |
| ERP `MENUTABLE` / `MENUAUTH` / `Portal_Menu` | 通常沿用 |

---

## 常見錯誤

| 錯誤 | 原因 | 解法 |
|---|---|---|
| 中文 `varchar` 變 `?` | DB / column collation 是 Latin1 | 整個 DB 重建,server collation 設為 `Chinese_Taiwan_Stroke_CI_AS`(看 [`新環境一鍵建置.md`](新環境一鍵建置.md)) |
| `UnexpectedToken` (PowerShell parser 錯) | `.ps1` 沒有 UTF-8 BOM,中文被 Big5 讀錯 | VSCode 另存為 `UTF-8 with BOM` |
| `Cannot truncate table because it is being referenced by a FOREIGN KEY` | 被別張表 FK 參照 | 四支腳本已改用 `DELETE FROM`,理論上不會再遇到 |
| `bcp in` PK 重複 (例: `Violation of PRIMARY KEY`) | `TRUNCATE` 沒清掉 (FK 擋住) → bcp 灌進去就 PK 重複 | 同上,改 `DELETE` 已修 |
| `INSERT statement conflicted with FOREIGN KEY constraint` | 子表先匯、父表還沒進來 | 看腳本內 `$Tables` 順序,父表要在前 |
| `WITH CHECK CHECK CONSTRAINT ALL` 失敗 | 上游主檔沒進來 | 檢查階段 1~3 全部跑過,所有上游表都有資料 |
| `Unable to open BCP host data-file` | 路徑錯或 `.dat` 沒產生 | 確認 `bcp\` 子目錄有檔案 |
| `./d:/...` `CommandNotFoundException` | VSCode「執行檔案」按鈕產生混合路徑 | 改在終端機手打 `.\bcp-seed-xxx.ps1` |
| `bcp` 中文亂碼 | 用了 `-c`(純文字 ANSI) | 一律用 `-N`(Unicode native binary),腳本已用 |

---

## 重做時的清理

不用 DROP DATABASE,直接重跑腳本即可(內部會 DELETE 後再 bcp in)。
若連 schema 都要重建或 collation 錯,看 [`新環境一鍵建置.md`](新環境一鍵建置.md)。
