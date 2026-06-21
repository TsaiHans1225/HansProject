# TrayWidget - 系統托盤資訊小工具

## 功能
- 🌤 即時天氣（OpenWeatherMap）
- 📈 台股大盤指數 + 個股
- 📰 Yahoo 奇摩新聞標題（可點擊開啟）

## 使用方式
點一下工作列托盤圖示 → 彈出小視窗
右鍵托盤圖示 → 重新整理 / 設定個股 / 結束

## 設定步驟

### 1. 取得天氣 API Key（免費）
1. 前往 https://openweathermap.org/api
2. 註冊免費帳戶
3. 取得 API Key（Free tier 每分鐘60次，已足夠）
4. 開啟 TrayWidgetForm.cs，把這行改成你的 Key：
   ```
   private string weatherApiKey = "YOUR_OPENWEATHERMAP_API_KEY";
   ```
5. 城市也可以改，例如台北：
   ```
   private string weatherCity = "Taipei,TW";
   ```

### 2. 設定個股
- 預設是 00631L.TW（元大台灣50正2）、0050.TW（元大台灣50）、2330.TW（台積電）、00981A.TW
- 可在 TrayWidgetForm.cs 修改：
  ```csharp
  private List<string> stockList = new List<string> { "00631L.TW", "0050.TW", "2330.TW", "00981A.TW" };
  ```
- 或執行後右鍵托盤圖示 → 「設定股票清單」即時修改

## 建置方式

### Visual Studio
1. 開啟 TrayWidget.csproj
2. 按 F5 執行，或 Ctrl+Shift+B 建置

### 命令列
```
dotnet restore
dotnet build
dotnet run
```

## 開機自動啟動（選用）
1. 建置後取得 TrayWidget.exe 路徑
2. 按 Win+R → 輸入 shell:startup
3. 建立 TrayWidget.exe 的捷徑放入該資料夾

## 需求
- .NET 6.0 SDK 或以上
- Windows 10/11
- Newtonsoft.Json（NuGet 自動還原）
