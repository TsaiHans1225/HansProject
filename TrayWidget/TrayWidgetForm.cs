using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Timer = System.Windows.Forms.Timer;

namespace TrayWidget
{
    public class NewsItem
    {
        public string Title;
        public string Link;
        public string ImageUrl;
    }

    public class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 10;
        public Color FillColor { get; set; } = Color.White;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = MakeRoundedRect(new Rectangle(0, 0, Width, Height), Radius))
            using (var brush = new SolidBrush(FillColor))
                e.Graphics.FillPath(brush, path);
        }

        private static GraphicsPath MakeRoundedRect(Rectangle r, int rad)
        {
            int d = rad * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    public class TrayWidgetForm : Form
    {
        // 在類別欄位區加入
        private ToolStripMenuItem keepAwakeItem;
        private bool userExiting = false;

        private NotifyIcon trayIcon;
        private Panel popupPanel;
        private bool popupVisible = false;

        private Label lblClaudeSession, lblClaudeWeekly;
        private ProgressBar pbSession, pbWeekly;

        private Label lblWeatherIcon, lblWeatherTemp, lblWeatherDesc, lblWeatherCity, lblWeatherHiLo;
        private Label lblTWSE, lblTWSEChange;
        private Panel stockPanel;
        private Panel newsPanel;

        private List<NewsItem> newsList = new List<NewsItem>();
        private int newsPage = 0;

        // ===== 設定 =====
        private double weatherLat = 25.03;   // 台北
        private double weatherLon = 121.57;
        private string weatherCityName = "Taipei";
        private List<string> stockList = new List<string> { "00631L.TW", "0050.TW", "2330.TW", "00981A.TW" };
        // ================

        private Timer refreshTimer;
        private static readonly HttpClient httpClient = CreateHttpClient(useCookies: true);
        // Claude API 要手動帶 Cookie header，UseCookies 必須關掉（.NET Framework 會吃掉手動設的 Cookie）
        private static readonly HttpClient claudeHttpClient = CreateHttpClient(useCookies: false);
        private static readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);
        private Panel forecastPanel;

        private static HttpClient CreateHttpClient(bool useCookies)
        {
            var handler = new HttpClientHandler { UseCookies = useCookies };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            return c;
        }

        // ── 深色主題色 ──
        private static readonly Color BgColor = Color.FromArgb(30, 32, 40);
        private static readonly Color CardBg = Color.FromArgb(42, 45, 58);
        private static readonly Color TextPrimary = Color.FromArgb(235, 237, 245);
        private static readonly Color TextSecondary = Color.FromArgb(160, 165, 185);
        private static readonly Color SectionLabel = Color.FromArgb(130, 135, 155);

        private const int PanelW = 330;
        private const int CardMargin = 12;
        private const int ContentW = 306;

        public TrayWidgetForm()
        {
            // 防止螢幕休眠
            SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED);
            InitializeComponent();
            BuildPopupPanel();
            Task.Run(() => RefreshAll());
            refreshTimer = new Timer();
            refreshTimer.Interval = 1 * 60 * 1000;
            refreshTimer.Tick += (s, e) => Task.Run(() => RefreshAll());
            refreshTimer.Start();
        }

        private void InitializeComponent()
        {
            this.Text = "TrayWidget";
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(0, 0);
            this.Opacity = 0;

            // 載入 icon：優先嵌入資源，fallback 到 cat.ico 檔案
            Icon appIcon;
            var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("TrayWidget.cat.ico");
            if (stream != null)
                appIcon = new Icon(stream);
            else
                appIcon = new Icon(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath), "cat.ico"));

            trayIcon = new NotifyIcon { Icon = appIcon, Text = "用量喵喵桌面版", Visible = true };
            trayIcon.MouseClick += TrayIcon_MouseClick;

            var menu = new ContextMenuStrip();
            menu.Items.Add("重新整理", null, (s, e) => Task.Run(() => RefreshAll()));
            menu.Items.Add("設定個股", null, OpenStockSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("結束", null, (s, e) => { userExiting = true; trayIcon.Visible = false; Application.Exit(); });
            trayIcon.ContextMenuStrip = menu;

            keepAwakeItem = new ToolStripMenuItem("防止休眠　✓") { Checked = true };
            keepAwakeItem.Click += (s, e) =>
            {
                keepAwakeItem.Checked = !keepAwakeItem.Checked;
                if (keepAwakeItem.Checked)
                {
                    SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED);
                    keepAwakeItem.Text = "防止休眠　✓";
                }
                else
                {
                    SetThreadExecutionState(ES_CONTINUOUS);
                    keepAwakeItem.Text = "防止休眠　✗";
                }
            };
            menu.Items.Insert(0, keepAwakeItem);
        }

        private Label Lbl(string t, int x, int y, int w, int h, Color c, float fs, FontStyle sty)
        {
            return new Label
            {
                Text = t, Location = new Point(x, y), Size = new Size(w, h),
                ForeColor = c, Font = new Font("Microsoft JhengHei", fs, sty), BackColor = Color.Transparent
            };
        }

        private Label SectionTitle(string text, int x, int y)
        {
            return new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(200, 18),
                ForeColor = SectionLabel, Font = new Font("Microsoft JhengHei", 8f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
        }

        private RoundedPanel RCard(int x, int y, int w, int h, Color fill)
        {
            return new RoundedPanel
            {
                Location = new Point(x, y), Size = new Size(w, h),
                FillColor = fill, Radius = 10, BackColor = Color.Transparent
            };
        }

        private void BuildPopupPanel()
        {
            popupPanel = new Panel
            {
                Size = new Size(PanelW, 776),
                BackColor = BgColor,
                Visible = false
            };

            popupPanel.Paint += (s, e) =>
            {
                using (var p = new Pen(Color.FromArgb(60, 63, 80), 1))
                    e.Graphics.DrawRectangle(p, 0, 0, popupPanel.Width - 1, popupPanel.Height - 1);
            };

            int y = 14;

            // ══════ 天氣 ══════
            var wCard = RCard(CardMargin, y, ContentW, 88, Color.FromArgb(35, 90, 160));
            lblWeatherCity = Lbl("城市", 14, 8, 200, 16, Color.FromArgb(180, 210, 255), 7.5f, FontStyle.Regular);
            lblWeatherIcon = Lbl("...", 14, 28, 50, 36, Color.White, 18f, FontStyle.Bold);
            lblWeatherTemp = Lbl("--°C", 66, 26, 120, 38, Color.White, 20f, FontStyle.Bold);
            lblWeatherHiLo = Lbl("", 186, 36, 108, 20, Color.FromArgb(210, 230, 255), 9.5f, FontStyle.Bold);
            lblWeatherDesc = Lbl("讀取中...", 66, 63, 240, 16, Color.FromArgb(170, 210, 255), 7.5f, FontStyle.Regular);
            wCard.Controls.AddRange(new Control[] { lblWeatherCity, lblWeatherIcon, lblWeatherTemp, lblWeatherHiLo, lblWeatherDesc });
            popupPanel.Controls.Add(wCard);
            y += 94;

            // ══════ 5天預報 ══════
            forecastPanel = new Panel
            {
                Location = new Point(CardMargin, y), Size = new Size(ContentW, 72),
                BackColor = Color.FromArgb(28, 72, 130)
            };
            popupPanel.Controls.Add(forecastPanel);
            y += 78;

            // ══════ 大盤 ══════
            popupPanel.Controls.Add(SectionTitle("台  股", CardMargin + 2, y));
            y += 20;
            var iCard = RCard(CardMargin, y, ContentW, 48, CardBg);
            iCard.Controls.Add(Lbl("加權指數", 14, 6, 80, 14, TextSecondary, 7.5f, FontStyle.Regular));
            lblTWSE = Lbl("--", 14, 24, 120, 20, TextPrimary, 13f, FontStyle.Bold);
            lblTWSEChange = Lbl("", 140, 24, 155, 20, TextSecondary, 9f, FontStyle.Regular);
            iCard.Controls.Add(lblTWSE);
            iCard.Controls.Add(lblTWSEChange);
            popupPanel.Controls.Add(iCard);
            y += 56;

            // ══════ 個股三欄 ══════
            stockPanel = new Panel
            {
                Location = new Point(CardMargin, y), Size = new Size(ContentW, 54),
                BackColor = Color.Transparent
            };
            popupPanel.Controls.Add(stockPanel);
            y += 60;

            // ══════ Claude 用量 ══════
            popupPanel.Controls.Add(SectionTitle("Claude 用量", CardMargin + 2, y));
            y += 20;
            var cCard = RCard(CardMargin, y, ContentW, 76, CardBg);
            lblClaudeSession = Lbl("5小時用量：--", 14, 8, 240, 14, TextPrimary, 8f, FontStyle.Regular);
            pbSession = new ProgressBar { Location = new Point(14, 24), Size = new Size(228, 7), Minimum = 0, Maximum = 100, Value = 0, Style = ProgressBarStyle.Continuous };
            var lblSessionPct = new Label { Name = "lblSessionPct", Text = "0%", Location = new Point(248, 22), Size = new Size(45, 14), ForeColor = TextSecondary, Font = new Font("Microsoft JhengHei", 7.5f), BackColor = Color.Transparent };
            lblClaudeWeekly = Lbl("7天用量：--", 14, 40, 240, 14, TextPrimary, 8f, FontStyle.Regular);
            pbWeekly = new ProgressBar { Location = new Point(14, 56), Size = new Size(228, 7), Minimum = 0, Maximum = 100, Value = 0, Style = ProgressBarStyle.Continuous };
            var lblWeeklyPct = new Label { Name = "lblWeeklyPct", Text = "0%", Location = new Point(248, 54), Size = new Size(45, 14), ForeColor = TextSecondary, Font = new Font("Microsoft JhengHei", 7.5f), BackColor = Color.Transparent };
            cCard.Controls.Add(lblClaudeSession); cCard.Controls.Add(pbSession); cCard.Controls.Add(lblSessionPct);
            cCard.Controls.Add(lblClaudeWeekly); cCard.Controls.Add(pbWeekly); cCard.Controls.Add(lblWeeklyPct);
            popupPanel.Controls.Add(cCard);
            y += 84;

            // ══════ 新聞 ══════
            popupPanel.Controls.Add(SectionTitle("Bing 新聞", CardMargin + 2, y));
            var btnPrev = new Button
            {
                Text = "◀", Location = new Point(PanelW - 90, y - 1), Size = new Size(32, 18),
                FlatStyle = FlatStyle.Flat, Font = new Font("Arial", 7f),
                ForeColor = TextSecondary, BackColor = Color.FromArgb(50, 54, 68)
            };
            var btnNext = new Button
            {
                Text = "▶", Location = new Point(PanelW - 54, y - 1), Size = new Size(32, 18),
                FlatStyle = FlatStyle.Flat, Font = new Font("Arial", 7f),
                ForeColor = TextSecondary, BackColor = Color.FromArgb(50, 54, 68)
            };
            btnPrev.FlatAppearance.BorderSize = 0;
            btnNext.FlatAppearance.BorderSize = 0;
            btnPrev.Click += (s, e) => { if (newsPage > 0) { newsPage--; RenderNewsPage(); } };
            btnNext.Click += (s, e) => { if ((newsPage + 1) * 3 < newsList.Count) { newsPage++; RenderNewsPage(); } };
            popupPanel.Controls.Add(btnPrev);
            popupPanel.Controls.Add(btnNext);
            y += 22;

            newsPanel = new Panel
            {
                Location = new Point(CardMargin, y), Size = new Size(ContentW, 340),
                BackColor = Color.Transparent, AutoScroll = false
            };
            popupPanel.Controls.Add(newsPanel);
            this.Controls.Add(popupPanel);
        }

        // ══════════════════════════════════════
        //  Tray Icon：動態顯示 5h 用量 %
        // ══════════════════════════════════════
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        private Icon previousTrayIcon;

        private void UpdateTrayIconWithPct(int pct)
        {
            const int size = 16;
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                Color bg;
                if (pct < 50) bg = Color.FromArgb(72, 160, 120);
                else if (pct < 75) bg = Color.FromArgb(210, 170, 50);
                else if (pct < 90) bg = Color.FromArgb(220, 130, 60);
                else bg = Color.FromArgb(210, 70, 60);
                g.Clear(bg);

                float fs = pct >= 100 ? 6.5f : (pct >= 10 ? 8f : 9f);
                using (var font = new Font("Arial", fs, FontStyle.Bold))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(pct.ToString(), font, Brushes.White, new RectangleF(0, 0, size, size), sf);

                var hIcon = bmp.GetHicon();
                var newIcon = Icon.FromHandle(hIcon);
                var oldIcon = previousTrayIcon;
                previousTrayIcon = newIcon;
                trayIcon.Icon = newIcon;
                trayIcon.Text = string.Format("用量喵喵桌面版 — Claude 5h: {0}%", pct);
                if (oldIcon != null)
                {
                    var oldHandle = oldIcon.Handle;
                    oldIcon.Dispose();
                    DestroyIcon(oldHandle);
                }
            }
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) TogglePopup();
        }

        private void TogglePopup()
        {
            if (popupVisible)
            {
                popupPanel.Visible = false; this.Hide(); popupVisible = false;
            }
            else
            {
                PositionPopup();
                this.Opacity = 1;
                this.Show();
                this.WindowState = FormWindowState.Normal;
                popupPanel.Visible = true;
                this.BringToFront();
                this.Activate();
                popupVisible = true;
            }
        }

        private void PositionPopup()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(wa.Right - PanelW - 14, wa.Bottom - 790);
            this.Size = new Size(PanelW + 4, 786);
            popupPanel.Location = new Point(0, 0);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (popupVisible) { popupPanel.Visible = false; this.Hide(); popupVisible = false; }
        }

        private async Task RefreshAll()
        {
            if (!await refreshLock.WaitAsync(0)) return; // 防止 timer 與手動點擊重入
            try
            {
                SafeInvoke(() => trayIcon.Text = "用量喵喵桌面版 (更新中...)");
                await Task.WhenAll(FetchWeather(), FetchStocks(), FetchNews(), FetchClaudeUsage());
                SafeInvoke(() => { if (trayIcon.Text.Contains("更新中")) trayIcon.Text = "用量喵喵桌面版"; });
            }
            finally { refreshLock.Release(); }
        }

        // ══════════════════════════════════════
        //  天氣（Open-Meteo，免費免 API key）
        // ══════════════════════════════════════
        private static string WmoIcon(int code)
        {
            if (code <= 1) return "☀";
            if (code <= 3) return "☁";
            if (code <= 48) return "🌫";
            if (code <= 57) return "🌧";
            if (code <= 67) return "🌧";
            if (code <= 77) return "❄";
            if (code <= 82) return "🌧";
            if (code <= 86) return "❄";
            if (code <= 99) return "⛈";
            return "☁";
        }

        private static string WmoDesc(int code)
        {
            if (code == 0) return "晴天";
            if (code == 1) return "大致晴朗";
            if (code == 2) return "局部多雲";
            if (code == 3) return "陰天";
            if (code <= 48) return "起霧";
            if (code <= 57) return "毛毛雨";
            if (code <= 67) return "下雨";
            if (code <= 77) return "下雪";
            if (code <= 82) return "陣雨";
            if (code <= 86) return "陣雪";
            if (code <= 99) return "雷雨";
            return "多雲";
        }

        private async Task FetchWeather()
        {
            try
            {
                // 現在天氣
                var curUrl = string.Format(
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code&timezone=Asia%2FTaipei",
                    weatherLat, weatherLon);
                var curJson = await httpClient.GetStringAsync(curUrl);
                var curObj = JObject.Parse(curJson);
                var current = curObj["current"];
                var temp = Math.Round((double)current["temperature_2m"]);
                var wmoCode = (int)current["weather_code"];

                // 5天預報（含降雨機率）
                var fcUrl = string.Format(
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Asia%2FTaipei&forecast_days=6",
                    weatherLat, weatherLon);
                var fcJson = await httpClient.GetStringAsync(fcUrl);
                var fcObj = JObject.Parse(fcJson)["daily"];
                var dates = (JArray)fcObj["time"];
                var codes = (JArray)fcObj["weather_code"];
                var highs = (JArray)fcObj["temperature_2m_max"];
                var lows = (JArray)fcObj["temperature_2m_min"];
                var pops = (JArray)fcObj["precipitation_probability_max"];

                int todayHi = (int)Math.Round((double)highs[0]);
                int todayLo = (int)Math.Round((double)lows[0]);
                int todayPop = pops[0].Type == JTokenType.Null ? 0 : (int)pops[0];

                var daily = new List<(string day, string icon, double hi, double lo, int pop)>();
                // 跳過今天(index 0)，取後面5天
                for (int i = 1; i < dates.Count && daily.Count < 5; i++)
                {
                    var d = DateTime.Parse(dates[i].ToString());
                    int pop = pops[i].Type == JTokenType.Null ? 0 : (int)pops[i];
                    daily.Add((
                        d.ToString("MM/dd"),
                        WmoIcon((int)codes[i]),
                        Math.Round((double)highs[i]),
                        Math.Round((double)lows[i]),
                        pop
                    ));
                }

                SafeInvoke(() =>
                {
                    lblWeatherTemp.Text = temp + "°C";
                    lblWeatherDesc.Text = string.Format("{0}　💧 {1}%", WmoDesc(wmoCode), todayPop);
                    lblWeatherHiLo.Text = string.Format("↑{0}°  ↓{1}°", todayHi, todayLo);
                    lblWeatherIcon.Text = WmoIcon(wmoCode);
                    lblWeatherCity.Text = weatherCityName;
                    RenderForecast(daily);
                });
            }
            catch (Exception ex) { SafeInvoke(() => lblWeatherDesc.Text = "天氣錯誤: " + ex.Message); }
        }

        // ══════════════════════════════════════
        //  股市（大盤 + 個股四欄含漲跌%）
        // ══════════════════════════════════════
        private async Task FetchStocks()
        {
            try
            {
                // 大盤
                var json = await httpClient.GetStringAsync("https://query1.finance.yahoo.com/v8/finance/chart/%5ETWII?interval=1d&range=2d");
                var meta = JObject.Parse(json)["chart"]["result"][0]["meta"];
                var price = Math.Round((double)meta["regularMarketPrice"]);
                var prev = Math.Round((double)meta["chartPreviousClose"]);
                var chg = price - prev;
                var pct = Math.Round(chg / prev * 100, 2);
                var arrow = chg >= 0 ? "▲" : "▼";
                var cc = chg >= 0 ? Color.FromArgb(255, 120, 100) : Color.FromArgb(100, 220, 140);
                SafeInvoke(() =>
                {
                    lblTWSE.Text = price.ToString("N0");
                    lblTWSEChange.Text = string.Format("{0}{1}%", arrow, Math.Abs(pct));
                    lblTWSEChange.ForeColor = cc;
                });

                // 個股 — 固定四欄
                SafeInvoke(() => stockPanel.Controls.Clear());

                int colCount = 4;
                int gap = 6;
                int chipW = (stockPanel.Width - gap * (colCount - 1)) / colCount;
                int chipH = 52;

                for (int idx = 0; idx < stockList.Count && idx < colCount; idx++)
                {
                    var sym = stockList[idx];
                    try
                    {
                        var sJson = await httpClient.GetStringAsync(string.Format(
                            "https://query1.finance.yahoo.com/v8/finance/chart/{0}?interval=1d&range=2d",
                            Uri.EscapeDataString(sym)));
                        var sm = JObject.Parse(sJson)["chart"]["result"][0]["meta"];
                        var sp = Math.Round((double)sm["regularMarketPrice"], 2);
                        var sPrev = Math.Round((double)sm["chartPreviousClose"], 2);
                        var sc = sp - sPrev;
                        var sPct = sPrev != 0 ? Math.Round(sc / sPrev * 100, 2) : 0.0;
                        var sn = sym.Replace(".TW", "");
                        var isUp = sc >= 0;
                        var chipBg = isUp ? Color.FromArgb(60, 35, 35) : Color.FromArgb(30, 55, 40);
                        var chipFg = isUp ? Color.FromArgb(255, 130, 110) : Color.FromArgb(100, 225, 145);

                        int cx = idx * (chipW + gap);
                        // capture for closure
                        var _sn = sn; var _sp = sp; var _sPct = sPct;
                        var _bg = chipBg; var _fg = chipFg; var _cx = cx; var _isUp = isUp;

                        SafeInvoke(() =>
                        {
                            var chip = new RoundedPanel
                            {
                                Location = new Point(_cx, 0), Size = new Size(chipW, chipH),
                                FillColor = _bg, Radius = 8, BackColor = Color.Transparent
                            };
                            chip.Controls.Add(new Label
                            {
                                Text = _sn, Location = new Point(8, 4), Size = new Size(chipW - 14, 15),
                                Font = new Font("Microsoft JhengHei", 7.5f, FontStyle.Bold),
                                ForeColor = TextPrimary, BackColor = Color.Transparent
                            });
                            chip.Controls.Add(new Label
                            {
                                Text = (_isUp ? "▲" : "▼") + " " + _sp,
                                Location = new Point(8, 20), Size = new Size(chipW - 14, 14),
                                Font = new Font("Microsoft JhengHei", 7f),
                                ForeColor = _fg, BackColor = Color.Transparent
                            });
                            chip.Controls.Add(new Label
                            {
                                Text = (_sPct >= 0 ? "+" : "") + _sPct + "%",
                                Location = new Point(8, 35), Size = new Size(chipW - 14, 14),
                                Font = new Font("Microsoft JhengHei", 6.5f),
                                ForeColor = _fg, BackColor = Color.Transparent
                            });
                            stockPanel.Controls.Add(chip);
                        });
                    }
                    catch
                    {
                        int cx2 = idx * (chipW + gap);
                        var symName = sym.Replace(".TW", "");
                        SafeInvoke(() =>
                        {
                            var chip = new RoundedPanel
                            {
                                Location = new Point(cx2, 0), Size = new Size(chipW, chipH),
                                FillColor = CardBg, Radius = 8, BackColor = Color.Transparent
                            };
                            chip.Controls.Add(new Label
                            {
                                Text = symName + " --", Location = new Point(8, 16),
                                Size = new Size(chipW - 14, 16),
                                Font = new Font("Microsoft JhengHei", 7.5f),
                                ForeColor = TextSecondary, BackColor = Color.Transparent
                            });
                            stockPanel.Controls.Add(chip);
                        });
                    }
                }
            }
            catch { SafeInvoke(() => lblTWSE.Text = "讀取失敗"); }
        }

        // ══════════════════════════════════════
        //  新聞
        // ══════════════════════════════════════
        private async Task FetchNews()
        {
            try
            {
                var xml = await httpClient.GetStringAsync("https://www.bing.com/news/search?q=台灣&format=rss&setlang=zh-TW");
                var doc = XDocument.Parse(xml);
                var list = new List<NewsItem>();
                foreach (var item in doc.Descendants("item"))
                {
                    if (list.Count >= 12) break;
                    var title = item.Element("title")?.Value ?? "";
                    var link = item.Element("link")?.Value ?? "";
                    var imgUrl = "";
                    XNamespace ns = "https://www.bing.com/news/search?q=%E5%8F%B0%E7%81%A3&format=rss&setlang=zh-TW";
                    var imgEl = item.Element(ns + "Image");
                    if (imgEl != null) imgUrl = imgEl.Value;
                    if (string.IsNullOrEmpty(imgUrl))
                    {
                        XNamespace media = "http://search.yahoo.com/mrss/";
                        var mc = item.Element(media + "content");
                        if (mc != null) imgUrl = (string)mc.Attribute("url") ?? "";
                    }
                    if (string.IsNullOrEmpty(imgUrl))
                    {
                        var enc = item.Element("enclosure");
                        if (enc != null) imgUrl = (string)enc.Attribute("url") ?? "";
                    }
                    if (!string.IsNullOrEmpty(title))
                        list.Add(new NewsItem { Title = title, Link = link, ImageUrl = imgUrl });
                }
                newsList = list;
                newsPage = 0;
                SafeInvoke(() => RenderNewsPage());
            }
            catch (Exception ex)
            {
                SafeInvoke(() =>
                {
                    newsPanel.Controls.Clear();
                    newsPanel.Controls.Add(new Label { Text = ex.Message, ForeColor = TextSecondary, Size = new Size(280, 60) });
                });
            }
        }

        // ══════════════════════════════════════
        //  Claude 用量（直接 API → LevelDB 備援）
        // ══════════════════════════════════════

        private static readonly string ChromeUserData = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data");

        /// <summary>列出所有含 Cookies 檔的 Chrome profile（Default + Profile N）</summary>
        private static IEnumerable<string> EnumerateChromeProfiles()
        {
            if (!System.IO.Directory.Exists(ChromeUserData)) yield break;
            foreach (var dir in System.IO.Directory.GetDirectories(ChromeUserData))
            {
                var name = System.IO.Path.GetFileName(dir);
                if (name != "Default" && !name.StartsWith("Profile ")) continue;
                var cookiePath = System.IO.Path.Combine(dir, "Network", "Cookies");
                if (System.IO.File.Exists(cookiePath)) yield return name;
            }
        }

        /// <summary>取得 Chrome 的 AES-256-GCM 金鑰（DPAPI 解密）</summary>
        private static byte[] GetChromeAesKey()
        {
            var localStatePath = System.IO.Path.Combine(ChromeUserData, "Local State");
            var json = System.IO.File.ReadAllText(localStatePath);
            var obj = JObject.Parse(json);
            var encKeyB64 = obj["os_crypt"]["encrypted_key"].ToString();
            var encKey = Convert.FromBase64String(encKeyB64);
            // 去掉 "DPAPI" 前綴（5 bytes）
            var keyBytes = new byte[encKey.Length - 5];
            Array.Copy(encKey, 5, keyBytes, 0, keyBytes.Length);
            return ProtectedData.Unprotect(keyBytes, null, DataProtectionScope.CurrentUser);
        }

        /// <summary>用 BouncyCastle AES-256-GCM 解密 Chrome cookie</summary>
        private static string DecryptCookieValue(byte[] encrypted, byte[] aesKey)
        {
            if (encrypted == null || encrypted.Length < 3 + 12 + 16) return null;
            // 前 3 bytes = "v10" 或 "v20"，接著 12 bytes nonce，其餘 = ciphertext + 16 bytes GCM tag
            var nonce = new byte[12];
            Array.Copy(encrypted, 3, nonce, 0, 12);
            var ciphertextWithTag = new byte[encrypted.Length - 3 - 12];
            Array.Copy(encrypted, 3 + 12, ciphertextWithTag, 0, ciphertextWithTag.Length);

            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(false, new AeadParameters(new KeyParameter(aesKey), 128, nonce));
            var plain = new byte[cipher.GetOutputSize(ciphertextWithTag.Length)];
            var len = cipher.ProcessBytes(ciphertextWithTag, 0, ciphertextWithTag.Length, plain, 0);
            cipher.DoFinal(plain, len);

            // 去掉尾端 null
            int end = plain.Length;
            while (end > 0 && plain[end - 1] == 0) end--;
            return Encoding.UTF8.GetString(plain, 0, end);
        }

        /// <summary>從 Chrome Cookies SQLite 取得指定 domain 的所有 cookie（指定 profile）</summary>
        private static Dictionary<string, string> GetChromeCookies(string domain, string profile)
        {
            var cookies = new Dictionary<string, string>();
            var aesKey = GetChromeAesKey();

            var cookieDbPath = System.IO.Path.Combine(
                ChromeUserData, profile, "Network", "Cookies");
            if (!System.IO.File.Exists(cookieDbPath)) return cookies;

            // 複製到暫存避免鎖定（每個 profile 用獨立暫存檔）
            var tmpDb = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tw_chrome_cookies_" + Math.Abs(profile.GetHashCode()));
            System.IO.File.Copy(cookieDbPath, tmpDb, true);

            try
            {
                using (var conn = new SQLiteConnection(string.Format("Data Source={0};Version=3;Read Only=True;", tmpDb)))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name, encrypted_value FROM cookies WHERE host_key LIKE @domain";
                        cmd.Parameters.AddWithValue("@domain", "%" + domain + "%");
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var name = reader.GetString(0);
                                var encValue = (byte[])reader[1];
                                if (encValue != null && encValue.Length > 0)
                                {
                                    try
                                    {
                                        string value;
                                        // v10/v20 prefix → AES-GCM，否則 DPAPI 直接解
                                        if (encValue.Length >= 3 && encValue[0] == 'v' && encValue[1] == '1')
                                            value = DecryptCookieValue(encValue, aesKey);
                                        else
                                            value = Encoding.UTF8.GetString(
                                                ProtectedData.Unprotect(encValue, null, DataProtectionScope.CurrentUser));

                                        if (!string.IsNullOrEmpty(value))
                                            cookies[name] = value;
                                    }
                                    catch { /* 單一 cookie 解密失敗就跳過 */ }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                try { System.IO.File.Delete(tmpDb); } catch { }
            }

            return cookies;
        }

        /// <summary>直接呼叫 Claude API 取得用量：掃所有 profile 的 cookie，哪個能成功就用哪個</summary>
        private async Task<bool> TryFetchClaudeUsageFromApi()
        {
            foreach (var profile in EnumerateChromeProfiles())
            {
                Dictionary<string, string> cookies;
                try { cookies = GetChromeCookies("claude.ai", profile); }
                catch { continue; }                 // 該 profile cookie 讀取失敗 → 換下一個
                if (cookies.Count == 0) continue;    // 沒有 claude.ai cookie → 換下一個

                try
                {
                    if (await TryFetchUsageWithCookies(cookies, profile)) return true;
                }
                catch { /* 403 / 解析失敗等 → 換下一個 profile */ }
            }
            return false;
        }

        /// <summary>用指定 profile 的 cookie 打一次 Claude API；成功才更新 UI 並回傳 true</summary>
        private async Task<bool> TryFetchUsageWithCookies(Dictionary<string, string> cookies, string profile)
        {
            var cookieHeader = string.Join("; ", cookies.Select(kv => kv.Key + "=" + kv.Value));

            // 1) 取得組織
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://claude.ai/api/organizations"))
            {
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                var resp = await claudeHttpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return false;

                var orgJson = await resp.Content.ReadAsStringAsync();
                var orgs = JArray.Parse(orgJson);
                if (orgs.Count == 0) return false;
                var orgId = orgs[0]["uuid"]?.ToString();
                if (string.IsNullOrEmpty(orgId)) return false;

                // 2) 取得用量
                using (var uReq = new HttpRequestMessage(HttpMethod.Get,
                    string.Format("https://claude.ai/api/organizations/{0}/usage", orgId)))
                {
                    uReq.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                    var uResp = await claudeHttpClient.SendAsync(uReq);
                    if (!uResp.IsSuccessStatusCode) return false;

                    var usageJson = await uResp.Content.ReadAsStringAsync();
                    var usage = JObject.Parse(usageJson);

                    int fiveHour = 0, sevenDay = 0;
                    string fiveReset = "", sevenReset = "";

                    // 解析 five_hour（Claude API 的 utilization 就是整數百分比 0-100）
                    var fh = usage["five_hour"];
                    if (fh != null)
                    {
                        var util = fh["utilization"];
                        if (util != null && util.Type != JTokenType.Null)
                            fiveHour = (int)Math.Round((double)util);
                        fiveReset = FormatResetTime(fh["resets_at"]?.ToString());
                    }

                    // 解析 seven_day
                    var sd = usage["seven_day"];
                    if (sd != null)
                    {
                        var util = sd["utilization"];
                        if (util != null && util.Type != JTokenType.Null)
                            sevenDay = (int)Math.Round((double)util);
                        sevenReset = FormatResetTime(sd["resets_at"]?.ToString());
                    }

                    ApplyClaudeUsageUI(fiveHour, sevenDay, fiveReset, sevenReset, "API (" + profile + ")");
                    return true;
                }
            }
        }

        /// <summary>格式化重置時間</summary>
        private static string FormatResetTime(string resetIso)
        {
            if (string.IsNullOrEmpty(resetIso)) return "";
            if (!DateTimeOffset.TryParse(resetIso, out var dt)) return "";
            var remaining = dt.ToLocalTime() - DateTimeOffset.Now;
            if (remaining.TotalMinutes < 1) return "即將重置";
            if (remaining.TotalHours < 1) return string.Format("{0}m 後重置", (int)remaining.TotalMinutes);
            if (remaining.TotalDays < 1) return string.Format("{0}h {1}m 後重置", (int)remaining.TotalHours, remaining.Minutes);
            return string.Format("{0}d {1}h 後重置", (int)remaining.TotalDays, remaining.Hours);
        }

        /// <summary>更新 Claude 用量 UI</summary>
        private void ApplyClaudeUsageUI(int fiveHour, int sevenDay, string fiveReset, string sevenReset, string source)
        {
            SafeInvoke(() =>
            {
                lblClaudeSession.Text = string.Format("5小時用量　{0}", fiveReset);
                lblClaudeWeekly.Text = string.Format("7天用量　{0}", sevenReset);
                pbSession.Value = Math.Min(fiveHour, 100);
                pbWeekly.Value = Math.Min(sevenDay, 100);
                var card = pbSession.Parent;
                foreach (Control c in card.Controls)
                {
                    if (c.Name == "lblSessionPct") c.Text = fiveHour + "%";
                    if (c.Name == "lblWeeklyPct") c.Text = sevenDay + "%";
                }
                UpdateTrayIconWithPct(fiveHour);
            });
        }

        /// <summary>主方法：API 優先 → LevelDB 備援</summary>
        private async Task FetchClaudeUsage()
        {
            string apiErr = null;
            try
            {
                if (await TryFetchClaudeUsageFromApi()) return;
                apiErr = "API 回應非 200（cookie 可能失效，請重新登入 claude.ai）";
            }
            catch (Exception ex) { apiErr = "API: " + ex.Message; }

            // 備援：讀取 Chrome 擴充功能 LevelDB
            await Task.Run(() => FetchClaudeUsageFromLevelDB(apiErr));
        }

        /// <summary>掃所有 Profile 下所有擴充 settings，找含 usageData 且 lastUpdated 最新的那個</summary>
        private static (JObject usage, string extId, string profile) FindFreshestUsageFromLevelDB()
        {
            JObject best = null;
            long bestTs = long.MinValue;
            string bestExt = null, bestProfile = null;

            if (!System.IO.Directory.Exists(ChromeUserData)) return (null, null, null);

            foreach (var profileDir in System.IO.Directory.GetDirectories(ChromeUserData))
            {
                var profileName = System.IO.Path.GetFileName(profileDir);
                if (profileName != "Default" && !profileName.StartsWith("Profile ")) continue;
                var settingsRoot = System.IO.Path.Combine(profileDir, "Local Extension Settings");
                if (!System.IO.Directory.Exists(settingsRoot)) continue;

                foreach (var extDir in System.IO.Directory.GetDirectories(settingsRoot))
                {
                    var extId = System.IO.Path.GetFileName(extDir);
                    var raw = TryReadLevelDbKey(extDir, "usageData");
                    if (string.IsNullOrEmpty(raw)) continue;

                    JObject obj;
                    try { obj = JObject.Parse(raw); } catch { continue; }
                    if (obj["tiers"] == null) continue;

                    long ts = obj["lastUpdated"]?.Type == JTokenType.Integer ? (long)obj["lastUpdated"] : 0;
                    if (ts > bestTs) { bestTs = ts; best = obj; bestExt = extId; bestProfile = profileName; }
                }
            }
            return (best, bestExt, bestProfile);
        }

        // leveldb.dll 開到 Chrome 寫到一半的損毀副本時會丟 AccessViolationException（損毀狀態例外），
        // 一般 catch 攔不到、會直接終結整個 process —— 需要這兩個屬性才能讓下面的 catch 生效
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static string TryReadLevelDbKey(string srcPath, string key)
        {
            string tmpPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tw_ext_" + Math.Abs(srcPath.GetHashCode()));
            try
            {
                if (System.IO.Directory.Exists(tmpPath)) System.IO.Directory.Delete(tmpPath, true);
                System.IO.Directory.CreateDirectory(tmpPath);
                foreach (var f in System.IO.Directory.GetFiles(srcPath))
                {
                    var fname = System.IO.Path.GetFileName(f);
                    if (fname == "LOCK") continue;
                    System.IO.File.Copy(f, System.IO.Path.Combine(tmpPath, fname), true);
                }
                var options = new LevelDB.Options { CreateIfMissing = false };
                using (var db = new LevelDB.DB(options, tmpPath))
                    return db.Get(key);
            }
            catch { return null; }
            finally
            {
                try { System.IO.Directory.Delete(tmpPath, true); } catch { }
            }
        }

        /// <summary>從 LevelDB 讀取擴充功能資料（需要 Chrome 開啟時更新過）</summary>
        private void FetchClaudeUsageFromLevelDB(string apiErr = null)
        {
            try
            {
                var found = FindFreshestUsageFromLevelDB();
                if (found.usage == null)
                {
                    var hint = apiErr != null ? apiErr : "找不到 ClaudeUsageNyan 擴充的 usageData";
                    SafeInvoke(() => lblClaudeSession.Text = "用量讀取失敗：" + hint);
                    return;
                }

                var tiers = (JArray)found.usage["tiers"];
                int fiveHour = 0, sevenDay = 0;
                string fiveReset = "", sevenReset = "";

                foreach (var tier in tiers)
                {
                    var type = tier["type"]?.ToString() ?? "";
                    var up = tier["usagePercent"];
                    int p = 0;
                    if (up != null && up.Type != JTokenType.Null)
                        p = (int)Math.Round((double)up);
                    var reset = tier["resetAt"]?.ToString() ?? "";
                    var resetStr = FormatResetTime(reset);
                    if (type == "five_hour") { fiveHour = p; fiveReset = resetStr; }
                    if (type == "seven_day") { sevenDay = p; sevenReset = resetStr; }
                }

                ApplyClaudeUsageUI(fiveHour, sevenDay, fiveReset, sevenReset,
                    "LevelDB " + found.profile + "/" + found.extId.Substring(0, 6));
            }
            catch (Exception ex)
            {
                var hint = apiErr != null ? apiErr : ex.Message;
                SafeInvoke(() => lblClaudeSession.Text = "用量讀取失敗：" + hint);
            }
        }

        // ══════════════════════════════════════
        //  渲染
        // ══════════════════════════════════════
        private void RenderForecast(List<(string day, string icon, double hi, double lo, int pop)> daily)
        {
            forecastPanel.Controls.Clear();
            int cellW = forecastPanel.Width / Math.Max(daily.Count, 1);
            for (int i = 0; i < daily.Count; i++)
            {
                var d = daily[i];
                var cell = new Panel { Location = new Point(i * cellW, 0), Size = new Size(cellW, 72), BackColor = Color.Transparent };
                cell.Controls.Add(new Label { Text = d.day, Location = new Point(2, 2), Size = new Size(cellW - 4, 13), Font = new Font("Microsoft JhengHei", 7f), ForeColor = Color.FromArgb(160, 200, 240), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
                cell.Controls.Add(new Label { Text = d.icon, Location = new Point(2, 15), Size = new Size(cellW - 4, 16), Font = new Font("Segoe UI Emoji", 9f), ForeColor = Color.White, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
                cell.Controls.Add(new Label { Text = d.hi + "°", Location = new Point(2, 32), Size = new Size(cellW - 4, 13), Font = new Font("Microsoft JhengHei", 7.5f), ForeColor = Color.FromArgb(255, 190, 130), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
                cell.Controls.Add(new Label { Text = d.lo + "°", Location = new Point(2, 44), Size = new Size(cellW - 4, 12), Font = new Font("Microsoft JhengHei", 7f), ForeColor = Color.FromArgb(140, 190, 240), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
                cell.Controls.Add(new Label { Text = "💧 " + d.pop + "%", Location = new Point(2, 57), Size = new Size(cellW - 4, 13), Font = new Font("Segoe UI Emoji", 7f), ForeColor = Color.FromArgb(130, 200, 255), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
                forecastPanel.Controls.Add(cell);
            }
        }

        private void RenderNewsPage()
        {
            newsPanel.Controls.Clear();
            int start = newsPage * 3;
            int ny = 0;
            for (int i = start; i < Math.Min(start + 3, newsList.Count); i++)
            {
                var news = newsList[i];
                var card = new RoundedPanel
                {
                    Location = new Point(0, ny), Size = new Size(newsPanel.Width, 100),
                    FillColor = CardBg, Radius = 8, BackColor = Color.Transparent, Cursor = Cursors.Hand
                };

                var picBox = new PictureBox
                {
                    Location = new Point(8, 8), Size = new Size(96, 84),
                    SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(55, 58, 72)
                };
                if (!string.IsNullOrEmpty(news.ImageUrl))
                {
                    var captured = news.ImageUrl;
                    Task.Run(async () =>
                    {
                        try
                        {
                            var bytes = await httpClient.GetByteArrayAsync(captured);
                            using (var ms = new System.IO.MemoryStream(bytes))
                            {
                                var img = Image.FromStream(ms);
                                SafeInvoke(() => { if (!picBox.IsDisposed) picBox.Image = img; });
                            }
                        }
                        catch { }
                    });
                }

                var lbl = new Label
                {
                    Text = news.Title, Location = new Point(112, 8),
                    Size = new Size(newsPanel.Width - 124, 82),
                    Font = new Font("Microsoft JhengHei", 8.5f),
                    ForeColor = TextPrimary, BackColor = Color.Transparent
                };

                var cap = news.Link;
                card.Click += (s, e) => { try { Process.Start(cap); } catch { } };
                lbl.Click += (s, e) => { try { Process.Start(cap); } catch { } };
                picBox.Click += (s, e) => { try { Process.Start(cap); } catch { } };
                card.Controls.Add(picBox);
                card.Controls.Add(lbl);
                newsPanel.Controls.Add(card);
                ny += 108;
            }
        }

        private void SafeInvoke(Action a)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                if (InvokeRequired) Invoke(a);
                else a();
            }
        }

        private void OpenStockSettings(object sender, EventArgs e)
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "請輸入個股代號（逗號分隔，最多4個）\n範例：00631L.TW,0050.TW,2330.TW,00981A.TW",
                "設定個股清單", string.Join(",", stockList));
            if (!string.IsNullOrEmpty(input))
            {
                stockList.Clear();
                foreach (var s in input.Split(','))
                {
                    var sym = s.Trim();
                    if (!string.IsNullOrEmpty(sym)) stockList.Add(sym);
                }
                if (stockList.Count > 4) stockList = stockList.Take(4).ToList();
                Task.Run(() => RefreshAll());
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 只有使用者按「結束」或 Windows 登出/關機時才真的退出；
            // 其它來源（誤觸 Alt+F4、UI 例外、外部關閉訊號）一律改為隱藏，托盤常駐
            if (!userExiting
                && e.CloseReason != CloseReason.WindowsShutDown
                && e.CloseReason != CloseReason.TaskManagerClosing
                && e.CloseReason != CloseReason.ApplicationExitCall)
            {
                e.Cancel = true;
                if (popupVisible) { popupPanel.Visible = false; popupVisible = false; }
                this.Hide();
                return;
            }

            SetThreadExecutionState(ES_CONTINUOUS); // 還原系統預設
            try { trayIcon.Visible = false; } catch { }
            base.OnFormClosing(e);
        }
    }
}
