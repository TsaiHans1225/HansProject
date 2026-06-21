$ErrorActionPreference = "Stop"

# ============================================================================
# 清空 docker (116.42) 內所有「交易/暫存/Log」類資料,只保留主檔
# 用途: 備份一個乾淨的 docker volume,日後測試前可以快速回到「資料潔淨」狀態
#
# 邏輯:
#   - 依「子表 → 父表」順序 DELETE,避免 FK 撞牆
#   - 全程包在 NOCHECK CONSTRAINT 內,跑完再 WITH CHECK 重新啟用
#   - 不用 TRUNCATE: 有 FK 限制不能 TRUNCATE,DELETE 比較通用
#   - 主檔表 (mer_data / slot_data / supplyer_* / customer_* / 各種 _data) 不動
#
# 注意:
#   - 本地測試環境用,不要對 131.6 跑
#   - 跑之前確認 docker compose down + 備份 volume 是個好習慣
# ============================================================================

# === Target ===
$DstServer = "localhost,1433"
$DstDb     = "PXCWMS_K"
$DstUser   = "sa"
$DstPwd    = "YourPassword123!"

# ----------------------------------------------------------------------------
# 清空清單 (子表先,父表後)
# ----------------------------------------------------------------------------
$Tables = @(
    # --- 撿貨 ---
    'pick_group',
    'pick_item',
    'pick_head',
    # --- 訂單分群 / 彙總 ---
    'group_item',
    'group_head',
    'group_list',
    # --- 直撥 / 越庫到貨 ---
    'directallot_box',
    'directallot_item',
    'directallot_head',
    'directallot_list',
    'directallotarrive_item',
    'directallotarrive_head',
    'tmp_dirbatch',
    # --- 訂單 / 出貨 ---
    'order_pick',
    'order_asset',
    'order_repl',
    'order_batch',
    'order_check',
    'order_checkresult',
    'Order_TrialDeviceLog',
    'order_item',
    'order_head',
    # --- 補貨 ---
    'repl_pick',
    'repl_item',
    'repl_head',
    # --- 盤點 / 調整 ---
    'metr_item',
    'metr_head',
    # --- 退倉 / 退廠 ---
    'retn_slot',
    'retnrecord_item',
    'retn_record',
    'retn_item',
    'retn_head',
    'back_item',
    'back_head',
    # --- 物流箱 / 載具 ---
    'tms_box',
    'tmsnonbox_item',
    'tmsnonbox_head',
    'box_item',
    'box_head',
    'rboxitem_log',
    'rbox_head_log',
    'rbox_item',
    'rbox_head',
    # --- 車輛報到/進場 (進貨前置；非主檔 car_data/car_type) ---
    'car_mertemperature',
    'car_checkin',
    'car_record',
    # --- 進貨 (子表先) ---
    'recirecord_alterlog',
    'recirecord_item',
    'reci_record',
    'reci_slot_slodlog',
    'reci_slot_log',
    'reci_slot',
    'pltno_reci',
    'reci_item',
    'reci_head_log',
    'reci_head',
    # --- 立庫交易 ---
    'ASRSJOBERR_CONTAINER',
    'CONTAINER_ASRSOUTLOG',
    'ASRSPALLET_JOB',
    'ASRSP_JOBRESULT',
    'ASRSP_JOBREQUEST',
    'ASRSP_JOBASSIGN',
    # --- 儲位/商品變動 LOG ---
    'slot_job',
    'slot_merLogAfter',
    'slot_merLog',
    'slot_mer_UPDlog',
    'Slot_mer_DelLog',
    'merpackage_alterlog',
    'ALTERHISTORY',
    # --- 暫存表 ---
    'TMP_ORDCFM',
    'tmp_SoConfirm',
    'tmp_SoCusdSort',
    'tmp_SoOrdiSlom',
    'tmp_SOSlom',
    # --- 工作站佔用 / 鎖 / API log / 號碼產生 ---
    'WORKSTATION_RESERVE',
    'lockuser',
    'api_log',
    'head_idno'
)

# 主檔 (這些絕對不動,寫在這裡只是給人讀):
#   slot_mer (庫存量不動;但 step 3.5 會把它的 reserqty/replqty 分配殘留歸零)
#   mer_data / mer_list / mer_package / mer_categ / mer_status
#   slot_data / pick_zone / sector_head / sector_item / slot_control
#   ASRS_DATA / ASRSC_DATA / ASRS_DEVICE / CRANE_DATA
#   supplyer_data / supplyer_addr / supplyer_addrtype / supplyer_slod / supplyer_group
#   customer_data / customer_slod
#   owner_data / owner_sbu / owner_extra
#   employee_data / auth_user / car_data / car_type / dock_data
#   basic_data / predefine_data / dc_data / option_data / SYS_PARAMETER
#   printexe_def / export_excel / defprintjob / PRT_TASKLIST / gridcolname
#   asset_data / WORKSTATION_DATA / jobnames

Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  清空 docker 交易資料 ($($Tables.Count) 張表)" -ForegroundColor Yellow
Write-Host "  目標: $DstServer / $DstDb" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host ""

# 跑之前先記筆數,讓你看清掉了多少
Write-Host "[step 1] 清空前筆數..." -ForegroundColor Cyan
$beforeSql = "SET NOCOUNT ON; " + (($Tables | ForEach-Object {
    "SELECT '$_' AS T, COUNT(*) AS N FROM [dbo].[$_]"
}) -join " UNION ALL ")
$before = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $DstDb -C -h -1 -W -Q $beforeSql 2>&1
$beforeMap = @{}
foreach ($line in $before) {
    if ($line -match '^(\S+)\s+(\d+)$') { $beforeMap[$matches[1]] = [int]$matches[2] }
}
$totalBefore = 0
$beforeMap.Values | ForEach-Object { $totalBefore += $_ }
Write-Host "  共 $totalBefore 筆待清" -ForegroundColor Gray
Write-Host ""

Write-Host "[step 2] 逐表 NOCHECK + DELETE..." -ForegroundColor Cyan
$idx = 0
foreach ($table in $Tables) {
    $idx++
    $before = if ($beforeMap.ContainsKey($table)) { $beforeMap[$table] } else { 0 }
    Write-Host ("  [{0,2}/{1}] {2,-30} {3,8} 筆..." -f $idx, $Tables.Count, $table, $before) -NoNewline -ForegroundColor Gray
    if ($before -eq 0) {
        Write-Host " SKIP (已空)" -ForegroundColor DarkGray
        continue
    }
    $sql = @"
SET NOCOUNT ON;
ALTER TABLE [dbo].[$table] NOCHECK CONSTRAINT ALL;
DELETE FROM [dbo].[$table];
"@
    $out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $DstDb -C -b -Q $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAIL" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "清空失敗: $table"
    }
    Write-Host " OK" -ForegroundColor Green
}

Write-Host ""
# ----------------------------------------------------------------------------
# 第二點五階段: 重置 IDENTITY 計數器
# DELETE 不會重置 IDENTITY (TRUNCATE 才會,但有 FK 不能用),
# 不重置的話清完後新建的單 sysno 會接著舊值繼續跳。
# RESEED 0 後,下一筆 INSERT 會拿到 1。
# 不管表剛才有沒有資料都重置 (空表的計數器也可能殘留高值)。
# ----------------------------------------------------------------------------
Write-Host "[step 2.5] 重置 IDENTITY 計數器..." -ForegroundColor Cyan
$reseedSql = "SET NOCOUNT ON;`n" + (($Tables | ForEach-Object {
    "IF OBJECTPROPERTY(OBJECT_ID('dbo.$_'),'TableHasIdentity') = 1 DBCC CHECKIDENT ('dbo.$_', RESEED, 0) WITH NO_INFOMSGS;"
}) -join "`n")
$out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $DstDb -C -b -Q $reseedSql 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL" -ForegroundColor Red
    $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    throw "IDENTITY 重置失敗"
}
Write-Host "  OK (有 IDENTITY 的表已全部 RESEED 0)" -ForegroundColor Green

Write-Host ""
Write-Host "[step 3] 重新啟用 FK..." -ForegroundColor Cyan
$fkSql = "SET NOCOUNT ON;`n" + (($Tables | ForEach-Object {
    "ALTER TABLE [dbo].[$_] WITH CHECK CHECK CONSTRAINT ALL;"
}) -join "`n")
$out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $DstDb -C -b -Q $fkSql 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "FK 重啟用警告 (有些表可能本來就有 trusted=0,可忽略):" -ForegroundColor Yellow
    $out | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

Write-Host ""
# ----------------------------------------------------------------------------
# 第三點五階段: 釋放 slot_mer 上的「分配/預留」殘留 (UPDATE 不 DELETE)
# slot_mer 是庫存主檔(37291 筆,庫存量 L_slom_1qty 絕不能動)，
# 但試算/截單會在它上面鎖 L_slom_reserqty(已分配)、L_slom_replqty(補貨)。
# 測試跑完這些殘留不歸零,下次試算庫存會少算 → 必須釋放。
# 只 UPDATE 這兩個欄位歸零,庫存量原封不動。
# ----------------------------------------------------------------------------
Write-Host "[step 3.5] 釋放 slot_mer 分配/預留量 (reserqty/replqty 歸零)..." -ForegroundColor Cyan
$reserCntSql = "SET NOCOUNT ON; SELECT COUNT(*) FROM slot_mer WHERE L_slom_reserqty<>0 OR L_slom_replqty<>0;"
$reserCnt = (sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $DstDb -C -h -1 -W -Q $reserCntSql | Select-Object -First 1).Trim()
Write-Host ("  slot_mer 有分配/補貨殘留: {0} 筆..." -f $reserCnt) -NoNewline -ForegroundColor Gray
if ([int]$reserCnt -eq 0) {
    Write-Host " SKIP (已乾淨)" -ForegroundColor DarkGray
} else {
    $reserSql = @"
SET NOCOUNT ON;
UPDATE slot_mer
   SET L_slom_reserqty = 0,
       L_slom_replqty  = 0
 WHERE L_slom_reserqty <> 0
    OR L_slom_replqty  <> 0;
"@
    $out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $DstDb -C -b -Q $reserSql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAIL" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "釋放 slot_mer 分配量失敗"
    }
    Write-Host " OK" -ForegroundColor Green
}

Write-Host ""
# ----------------------------------------------------------------------------
# 第四階段: mid_WES 中介庫 (WMS ↔ WES 之間的中介表)
# 動態抓所有 mid_* 表，只留 mid_config (設定檔)。
# mid_WES 無 FK，DELETE 順序無關；新增中間表會自動納入，不需改腳本。
# ----------------------------------------------------------------------------
Write-Host "[step 4] 清空 mid_WES 中介表..." -ForegroundColor Cyan
$midDb = 'mid_WES'
# 動態抓 mid_WES 裡所有中介表，排除設定檔 mid_config。
# 這樣日後新增的中間表(mid_reci_*/mid_ASRS*/mid_adjust_* 等)會自動納入，永不漏清。
$midListSql = "SET NOCOUNT ON; SELECT name FROM sys.tables WHERE name LIKE 'mid[_]%' AND name <> 'mid_config' ORDER BY name;"
$MidTables = @(sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -h -1 -W -Q $midListSql 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
Write-Host "  動態偵測到 $($MidTables.Count) 張 mid_ 表(已排除 mid_config)" -ForegroundColor Gray
$midIdx = 0
$midTotalBefore = 0
foreach ($table in $MidTables) {
    $midIdx++
    $cntSql = "SET NOCOUNT ON; SELECT COUNT(*) FROM [dbo].[$table];"
    $cnt = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -h -1 -W -Q $cntSql | Select-Object -First 1
    $before = [int]$cnt.Trim()
    $midTotalBefore += $before
    Write-Host ("  [{0,2}/{1}] mid_WES.{2,-22} {3,8} 筆..." -f $midIdx, $MidTables.Count, $table, $before) -NoNewline -ForegroundColor Gray
    if ($before -eq 0) { Write-Host " SKIP" -ForegroundColor DarkGray; continue }
    $sql = @"
SET NOCOUNT ON;
ALTER TABLE [dbo].[$table] NOCHECK CONSTRAINT ALL;
DELETE FROM [dbo].[$table];
ALTER TABLE [dbo].[$table] WITH CHECK CHECK CONSTRAINT ALL;
"@
    $out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -b -Q $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAIL" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "清空失敗: mid_WES.$table"
    }
    Write-Host " OK" -ForegroundColor Green
}

# mid_WES 也重置 IDENTITY (含剛才 SKIP 的空表)
if ($MidTables.Count -gt 0) {
    $reseedSql = "SET NOCOUNT ON;`n" + (($MidTables | ForEach-Object {
        "IF OBJECTPROPERTY(OBJECT_ID('dbo.$_'),'TableHasIdentity') = 1 DBCC CHECKIDENT ('dbo.$_', RESEED, 0) WITH NO_INFOMSGS;"
    }) -join "`n")
    $out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -b -Q $reseedSql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  mid_WES IDENTITY 重置 FAIL" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "mid_WES IDENTITY 重置失敗"
    }
    Write-Host "  mid_WES IDENTITY 已重置" -ForegroundColor Green
}

Write-Host ""
# ----------------------------------------------------------------------------
# 第四點五階段: mid_WES 裡的 DPS/CAPS 接口表 (ATOP 電子標籤)
# 這些表在 mid_WES 庫但「不是 mid_ 開頭」,所以 step 4 的 LIKE 'mid_%' 抓不到。
# 算箱(CalcBoxDps)會拋 PO/POHEADER/CARTON/BATCH;不清的話下次算箱會撞 PK_BATCH。
# 全部當交易資料清空(ITEM_MAST/EMPLOYEE/INVENTORY 是 WMS 拋過去的鏡像,清掉下次同步會重建)。
# ----------------------------------------------------------------------------
Write-Host "[step 4.5] 清空 mid_WES 的 DPS/CAPS 接口表..." -ForegroundColor Cyan
$DpsTables = @(
    'PO',
    'POHEADER',
    'CARTON',
    'BATCH',
    'CARTON_PRINT_LOG',
    'LABEL_PRINT_LOG',
    'INVENTORY_NOTIFY',
    'REPLENISH',
    'ITEM_MAST',
    'EMPLOYEE',
    'INVENTORY'
)
$dpsIdx = 0
$dpsTotalBefore = 0
foreach ($table in $DpsTables) {
    $dpsIdx++
    $cntSql = "SET NOCOUNT ON; IF OBJECT_ID('dbo.$table') IS NULL SELECT -1 ELSE SELECT COUNT(*) FROM [dbo].[$table];"
    $cnt = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -h -1 -W -Q $cntSql | Select-Object -First 1
    $before = [int]$cnt.Trim()
    Write-Host ("  [{0,2}/{1}] mid_WES.{2,-20} {3,8} 筆..." -f $dpsIdx, $DpsTables.Count, $table, $before) -NoNewline -ForegroundColor Gray
    if ($before -lt 0) { Write-Host " SKIP (表不存在)" -ForegroundColor DarkGray; continue }
    $dpsTotalBefore += $before
    if ($before -eq 0) { Write-Host " SKIP" -ForegroundColor DarkGray; continue }
    $sql = @"
SET NOCOUNT ON;
ALTER TABLE [dbo].[$table] NOCHECK CONSTRAINT ALL;
DELETE FROM [dbo].[$table];
ALTER TABLE [dbo].[$table] WITH CHECK CHECK CONSTRAINT ALL;
"@
    $out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -b -Q $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAIL" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "清空失敗: mid_WES.$table"
    }
    Write-Host " OK" -ForegroundColor Green
}
# DPS 表也重置 IDENTITY (含剛才 SKIP 的空表)
$reseedSql = "SET NOCOUNT ON;`n" + (($DpsTables | ForEach-Object {
    "IF OBJECT_ID('dbo.$_') IS NOT NULL AND OBJECTPROPERTY(OBJECT_ID('dbo.$_'),'TableHasIdentity') = 1 DBCC CHECKIDENT ('dbo.$_', RESEED, 0) WITH NO_INFOMSGS;"
}) -join "`n")
$out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $midDb -C -b -Q $reseedSql 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  DPS 表 IDENTITY 重置 FAIL" -ForegroundColor Red
    $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    throw "DPS 表 IDENTITY 重置失敗"
}
Write-Host "  DPS/CAPS 接口表 IDENTITY 已重置" -ForegroundColor Green

Write-Host ""
# ----------------------------------------------------------------------------
# 第五階段: ERP 庫的操作 log
# 留 USERS / GROUPS / MENU* / Token / 權限相關 (動到會壞 ERP 登入)
# ----------------------------------------------------------------------------
Write-Host "[step 5] 清空 ERP 庫的 log 表..." -ForegroundColor Cyan
$ErpLogTables = @(
    'ERP_CLICK_LOG',
    'ERP_EXE_LOG',
    'ERP_INFO_LOG',
    'USER_TOKEN_LOG',
    'USER_TOKEN_HISTORY'
)
$erpDb = 'ERP'
$erpIdx = 0
$erpTotalBefore = 0
foreach ($table in $ErpLogTables) {
    $erpIdx++
    $cntSql = "SET NOCOUNT ON; SELECT COUNT(*) FROM [dbo].[$table];"
    $cnt = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $erpDb -C -h -1 -W -Q $cntSql | Select-Object -First 1
    $before = [int]$cnt.Trim()
    $erpTotalBefore += $before
    Write-Host ("  [{0,2}/{1}] ERP.{2,-22} {3,8} 筆..." -f $erpIdx, $ErpLogTables.Count, $table, $before) -NoNewline -ForegroundColor Gray
    if ($before -eq 0) { Write-Host " SKIP" -ForegroundColor DarkGray; continue }
    $sql = @"
SET NOCOUNT ON;
ALTER TABLE [dbo].[$table] NOCHECK CONSTRAINT ALL;
DELETE FROM [dbo].[$table];
ALTER TABLE [dbo].[$table] WITH CHECK CHECK CONSTRAINT ALL;
"@
    $out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $erpDb -C -b -Q $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAIL" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "清空失敗: ERP.$table"
    }
    Write-Host " OK" -ForegroundColor Green
}

# ERP log 表也重置 IDENTITY
$reseedSql = "SET NOCOUNT ON;`n" + (($ErpLogTables | ForEach-Object {
    "IF OBJECTPROPERTY(OBJECT_ID('dbo.$_'),'TableHasIdentity') = 1 DBCC CHECKIDENT ('dbo.$_', RESEED, 0) WITH NO_INFOMSGS;"
}) -join "`n")
$out = sqlcmd -S $DstServer -U $DstUser -P $DstPwd -d $erpDb -C -b -Q $reseedSql 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERP IDENTITY 重置 FAIL" -ForegroundColor Red
    $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    throw "ERP IDENTITY 重置失敗"
}
Write-Host "  ERP IDENTITY 已重置" -ForegroundColor Green

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  完成!" -ForegroundColor Green
Write-Host "  PXCWMS_K 清掉 $totalBefore 筆" -ForegroundColor Green
Write-Host "  mid_WES   清掉 $midTotalBefore 筆 (mid_ 表) + $dpsTotalBefore 筆 (DPS/CAPS 接口表)" -ForegroundColor Green
Write-Host "  ERP       清掉 $erpTotalBefore 筆" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "下一步:" -ForegroundColor Cyan
Write-Host "  1. 確認主檔還在 (mer_data / slot_mer / supplyer_data 等)" -ForegroundColor Gray
Write-Host "  2. 用 docker compose down + 壓 volume 做乾淨備份" -ForegroundColor Gray
Write-Host "  3. 日後測試弄髒了,從備份還原 → 重跑 BCP 主腳本即可" -ForegroundColor Gray
