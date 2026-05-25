/*
DeepSeek API 用量监控 — 桌面悬浮小窗 v2
编译: csc /target:winexe /out:DeepSeekMonitor.exe DeepSeekMonitor.cs
*/
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class MonitorApp : Form
{
    // ---- 状态 ----
    string apiKey, configPath;
    double initialToppedUp, oldConsumedTotal, lastToppedUp, tokenRate = 500000;
    int countdown = 30;
    System.Windows.Forms.Timer timer;

    // ---- UI ----
    Panel statusDot;
    Label balanceVal, topupVal, tokenVal, progressLabel, pctLabel;
    Panel progressFill;
    Label updateLbl, refreshLbl;
    bool dragging; int dragX, dragY;

    // ---- 颜色 ----
    static readonly Color cBG      = Color.FromArgb(0x0d, 0x11, 0x17);
    static readonly Color cCardBG  = Color.FromArgb(0x16, 0x1b, 0x22);
    static readonly Color cBorder  = Color.FromArgb(0x30, 0x36, 0x3d);
    static readonly Color cText    = Color.FromArgb(0xc9, 0xd1, 0xd9);
    static readonly Color cMuted   = Color.FromArgb(0x8b, 0x94, 0x9e);
    static readonly Color cAccent  = Color.FromArgb(0x58, 0xa6, 0xff);
    static readonly Color cGreen   = Color.FromArgb(0x3f, 0xb9, 0x50);
    static readonly Color cRed     = Color.FromArgb(0xf8, 0x51, 0x49);
    static readonly Color cOrange  = Color.FromArgb(0xd2, 0x99, 0x1d);

    public MonitorApp()
    {
        configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepseek-monitor-config.json");
        LoadConfig();
        this.AutoScaleMode = AutoScaleMode.Dpi;
        InitUI();
        if (string.IsNullOrEmpty(apiKey))
            this.Shown += (s, e) => ShowSettings();
        else
        {
            RefreshData();
            StartTimer();
        }
    }

    // ============ 配置 ============
    void LoadConfig()
    {
        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath, Encoding.UTF8);
                var jss = new JavaScriptSerializer();
                var cfg = jss.Deserialize<dynamic>(json);
                apiKey = cfg["api_key"] ?? "";
                if (cfg.ContainsKey("initial_topped_up") && cfg["initial_topped_up"] != null)
                    initialToppedUp = Convert.ToDouble(cfg["initial_topped_up"]);
                if (cfg.ContainsKey("last_topped_up") && cfg["last_topped_up"] != null)
                    lastToppedUp = Convert.ToDouble(cfg["last_topped_up"]);
                if (cfg.ContainsKey("token_rate") && cfg["token_rate"] != null)
                    tokenRate = Convert.ToDouble(cfg["token_rate"]);
                // 兼容旧版配置文件：读取历史消耗用于校准
                if (initialToppedUp <= 0 && cfg.ContainsKey("consumed_total") && cfg["consumed_total"] != null)
                    oldConsumedTotal = Convert.ToDouble(cfg["consumed_total"]);
            }
        }
        catch { }
    }

    void SaveConfig()
    {
        var cfg = new { api_key = apiKey, initial_topped_up = initialToppedUp,
            last_topped_up = lastToppedUp, token_rate = tokenRate };
        File.WriteAllText(configPath, new JavaScriptSerializer().Serialize(cfg), Encoding.UTF8);
    }

    // ============ UI ============
    void InitUI()
    {
        int W = 330, H = 380;
        this.Text = "DeepSeek 用量监控";
        this.FormBorderStyle = FormBorderStyle.None;
        this.TopMost = true;
        this.BackColor = cBG;
        this.Size = new Size(W, H);
        this.Opacity = 0.97;

        var scr = Screen.PrimaryScreen.WorkingArea;
        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(scr.Width - W - 30, scr.Height - H - 80);

        // 拖拽
        this.MouseDown += (s, e) => { dragging = true; dragX = e.X; dragY = e.Y; };
        this.MouseMove += (s, e) => { if (dragging) { Left += e.X - dragX; Top += e.Y - dragY; } };
        this.MouseUp   += (s, e) => dragging = false;

        // ==== 标题栏 ====
        var bar = NewPanel(cBG, 0, 0, W, 30);
        bar.MouseDown += (s, e) => { dragging = true; dragX = e.X; dragY = e.Y; };
        bar.MouseMove += (s, e) => { if (dragging) { Left += e.X - dragX; Top += e.Y - dragY; } };
        bar.MouseUp   += (s, e) => dragging = false;

        statusDot = new Panel { Size = new Size(8, 8), Location = new Point(12, 11), BackColor = cMuted };
        bar.Controls.Add(statusDot);
        bar.Controls.Add(NewLbl("DeepSeek", cText, 10, true, 24, 8, 100, 16));

        var gear = NewLblHover("=", cMuted, 11, W - 44, 6, cAccent);
        gear.Click += (s, e) => ShowSettings();
        bar.Controls.Add(gear);

        var close = NewLblHover("X", cMuted, 11, W - 24, 6, cRed);
        close.Click += (s, e) => Close();
        bar.Controls.Add(close);

        this.Controls.Add(bar);

        // ==== 余额大卡片 ====
        var balCard = MakeCard(10, 36, W - 20, 114);
        balCard.Controls.Add(NewLbl("余额", cMuted, 8, false, 14, 12, 200, 16));
        balanceVal = NewLbl("--", cText, 26, true, 14, 32, W - 48, 50);
        balCard.Controls.Add(balanceVal);
        balCard.Controls.Add(NewLbl("CNY", cMuted, 8, false, 14, 86, 200, 14));

        // ==== 双卡片行 ====
        int scY = 158, scW = (W - 30) / 2;

        var topupCard = MakeCard(10, scY, scW, 82);
        topupCard.Controls.Add(NewLbl("累计充值", cMuted, 8, false, 0, 10, scW, 14, ContentAlignment.TopCenter));
        topupVal = NewLbl("--", cText, 18, true, 0, 28, scW, 28, ContentAlignment.TopCenter);
        topupCard.Controls.Add(topupVal);
        topupCard.Controls.Add(NewLbl("CNY", cMuted, 7, false, 0, 56, scW, 12, ContentAlignment.TopCenter));

        var tokenCard = MakeCard(20 + scW, scY, scW, 82);
        tokenCard.Controls.Add(NewLbl("已用Token", cMuted, 8, false, 0, 10, scW, 14, ContentAlignment.TopCenter));
        tokenVal = NewLbl("--", cText, 18, true, 0, 28, scW, 28, ContentAlignment.TopCenter);
        tokenCard.Controls.Add(tokenVal);

        // ==== 进度条卡片 ====
        int pgY = 248;
        var progCard = MakeCard(10, pgY, W - 20, 60);
        progressLabel = NewLbl("已消耗 --", cMuted, 8, false, 14, 10, 200, 14);
        progCard.Controls.Add(progressLabel);

        pctLabel = NewLbl("--", cMuted, 8, false, W - 70, 10, 50, 14, ContentAlignment.TopRight);
        progCard.Controls.Add(pctLabel);

        var pbg = new Panel { BackColor = Color.FromArgb(0x1a, 0x1f, 0x27), Size = new Size(W - 48, 6), Location = new Point(14, 38) };
        progressFill = new Panel { BackColor = cAccent, Size = new Size(0, 6), Location = new Point(0, 0) };
        pbg.Controls.Add(progressFill);
        progCard.Controls.Add(pbg);

        // ==== 底部 ====
        updateLbl = NewLbl("等待数据...", cMuted, 8, false, 14, H - 32, 120, 16);
        this.Controls.Add(updateLbl);

        refreshLbl = NewLbl("刷新", cMuted, 8, false, W - 55, H - 32, 45, 16, ContentAlignment.TopRight);
        refreshLbl.Cursor = Cursors.Hand;
        refreshLbl.Click += (s, e) => { countdown = 30; RefreshData(); };
        refreshLbl.MouseEnter += (s, e) => refreshLbl.ForeColor = cText;
        refreshLbl.MouseLeave += (s, e) => refreshLbl.ForeColor = cMuted;
        this.Controls.Add(refreshLbl);

        this.DoubleClick += (s, e) => { countdown = 30; RefreshData(); };
    }

    // ---- 控件辅助 ----
    Panel NewPanel(Color bg, int x, int y, int w, int h)
    {
        var p = new Panel { BackColor = bg, Size = new Size(w, h), Location = new Point(x, y) };
        return p;
    }

    Label NewLbl(string text, Color fg, float size, bool bold, int x, int y, int w, int h,
        ContentAlignment align = ContentAlignment.TopLeft)
    {
        var l = new Label
        {
            Text = text, ForeColor = fg, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            Location = new Point(x, y), Size = new Size(w, h), TextAlign = align
        };
        return l;
    }

    Label NewLblHover(string text, Color fg, float size, int x, int y, Color hover)
    {
        var l = NewLbl(text, fg, size, false, x, y, 16, 16);
        l.Cursor = Cursors.Hand;
        l.MouseEnter += (s, e) => l.ForeColor = hover;
        l.MouseLeave += (s, e) => l.ForeColor = fg;
        return l;
    }

    Panel MakeCard(int x, int y, int w, int h)
    {
        var p = new Panel { BackColor = cCardBG, Size = new Size(w, h), Location = new Point(x, y) };
        p.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(cBorder), 0, 0, w - 1, h - 1);
        // 拖拽
        p.MouseDown += (s, e) => { dragging = true; dragX = e.X; dragY = e.Y; };
        p.MouseMove += (s, e) => { if (dragging) { Left += e.X - dragX; Top += e.Y - dragY; } };
        p.MouseUp   += (s, e) => dragging = false;
        this.Controls.Add(p);
        return p;
    }

    // ============ 设置 ============
    void ShowSettings()
    {
        var dlg = new Form
        {
            Text = "设置", Size = new Size(350, 220), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = cBG,
            StartPosition = FormStartPosition.CenterScreen, TopMost = true
        };

        // API Key
        dlg.Controls.Add(NewLbl("DeepSeek API Key", cMuted, 8, false, 16, 14, 200, 16));
        var keyInput = new TextBox
        {
            Text = apiKey ?? "", BackColor = cCardBG, ForeColor = cText,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9),
            Size = new Size(316, 22), Location = new Point(16, 32), PasswordChar = '*'
        };
        dlg.Controls.Add(keyInput);

        // 累计充值总额
        dlg.Controls.Add(NewLbl("累计充值总额 (CNY，可选，在费用中心可查)", cMuted, 7, false, 16, 60, 300, 14));
        var topupInput = new TextBox
        {
            Text = initialToppedUp > 0 ? initialToppedUp.ToString("F2") : "",
            BackColor = cCardBG, ForeColor = cText,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9),
            Size = new Size(120, 22), Location = new Point(16, 78)
        };
        dlg.Controls.Add(topupInput);

        // Token 换算率
        dlg.Controls.Add(NewLbl("Token换算: 1 CNY ≈", cMuted, 7, false, 16, 106, 105, 14));
        var rateInput = new TextBox
        {
            Text = tokenRate.ToString("F0"),
            BackColor = cCardBG, ForeColor = cText,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9),
            Size = new Size(90, 22), Location = new Point(120, 104)
        };
        dlg.Controls.Add(rateInput);
        dlg.Controls.Add(NewLbl("Token", cMuted, 7, false, 214, 106, 30, 14));
        dlg.Controls.Add(NewLbl("V4-Flash≈700K  |  V3≈500K  |  V4-Pro≈250K  |  R1≈170K", cMuted, 7, false, 16, 128, 310, 14));

        // 按钮
        var btnCancel = new Button
        {
            Text = "取消", FlatStyle = FlatStyle.Flat, BackColor = cCardBG, ForeColor = cText,
            Size = new Size(70, 28), Location = new Point(178, 152)
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => dlg.Close();
        dlg.Controls.Add(btnCancel);

        var btnSave = new Button
        {
            Text = "保存", FlatStyle = FlatStyle.Flat, BackColor = cAccent,
            ForeColor = Color.White, Size = new Size(70, 28), Location = new Point(258, 152)
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += (s, e) =>
        {
            var k = keyInput.Text.Trim();
            if (!k.StartsWith("sk-")) return;
            apiKey = k;
            double v;
            if (double.TryParse(topupInput.Text.Trim(), out v) && v > 0)
                initialToppedUp = v;
            if (double.TryParse(rateInput.Text.Trim(), out v) && v > 0)
                tokenRate = v;
            SaveConfig();
            dlg.Close();
            RefreshData();
            StartTimer();
        };
        dlg.Controls.Add(btnSave);

        dlg.ShowDialog();
    }

    // ============ API ============
    void RefreshData()
    {
        if (string.IsNullOrEmpty(apiKey)) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/user/balance");
                req.Headers["Authorization"] = "Bearer " + apiKey;
                req.Accept = "application/json";
                req.Timeout = 10000;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var json = reader.ReadToEnd();
                    var data = new JavaScriptSerializer().Deserialize<dynamic>(json);
                    BeginInvoke((MethodInvoker)(() => ApplyResult(data)));
                }
            }
            catch (WebException we)
            {
                var r = we.Response as HttpWebResponse;
                var code = r != null ? (int)r.StatusCode : 0;
                var msg = code == 401 ? "API Key 无效" : "请求失败 (" + code + ")";
                BeginInvoke((MethodInvoker)(() =>
                {
                    statusDot.BackColor = cRed;
                    updateLbl.Text = msg;
                }));
            }
            catch (Exception)
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    statusDot.BackColor = cRed;
                    updateLbl.Text = "网络错误";
                }));
            }
        });
    }

    void ApplyResult(dynamic data)
    {
        var info = data["balance_infos"][0];
        double total = Convert.ToDouble(info["total_balance"]);
        double toppedUp = Convert.ToDouble(info["topped_up_balance"]);
        bool available = Convert.ToBoolean(data["is_available"]);

        // 状态灯
        statusDot.BackColor = available ? cGreen : cRed;

        // 余额
        balanceVal.Text = total.ToString("F2");
        if (total <= 2) balanceVal.ForeColor = cRed;
        else if (total <= 15) balanceVal.ForeColor = cOrange;
        else balanceVal.ForeColor = cText;

        // 累计充值
        topupVal.Text = initialToppedUp.ToString("F2");

        // 首次记录 / 充值后自动累加基准
        if (double.IsNaN(initialToppedUp) || initialToppedUp <= 0)
        {
            initialToppedUp = toppedUp + oldConsumedTotal;
            lastToppedUp = toppedUp;
            oldConsumedTotal = 0;
        }
        else if (toppedUp > lastToppedUp)
        {
            // 用户充值了，把新增额累加到基准（保留历史消耗）
            double added = toppedUp - lastToppedUp;
            initialToppedUp += added;
        }
        lastToppedUp = toppedUp;

        // 已消耗金额
        double spent = Math.Max(0, initialToppedUp - toppedUp);

        // Token 估算（可配置换算率，默认 500K/CNY ≈ ¥2/百万token）
        double tokens = spent * tokenRate;
        string tokenStr;
        if (tokens >= 1000000)
            tokenStr = (tokens / 1000000).ToString("F1") + "M";
        else if (tokens >= 1000)
            tokenStr = (tokens / 1000).ToString("F1") + "K";
        else
            tokenStr = tokens.ToString("F0");
        tokenVal.Text = tokenStr;

        // 进度条
        double pct = initialToppedUp > 0 ? Math.Min(100, spent / initialToppedUp * 100) : 0;
        progressLabel.Text = "已消耗 " + spent.ToString("F2") + " CNY";
        pctLabel.Text = pct.ToString("F0") + "%";

        int maxW = progressFill.Parent.Width;
        progressFill.Width = (int)(maxW * pct / 100);
        if (pct < 50) progressFill.BackColor = cGreen;
        else if (pct < 80) progressFill.BackColor = cOrange;
        else progressFill.BackColor = cRed;

        updateLbl.Text = DateTime.Now.ToString("HH:mm:ss");
        countdown = 30;
        SaveConfig();
    }

    // ============ 定时器 ============
    void StartTimer()
    {
        if (timer == null)
        {
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (s, e) =>
            {
                countdown--;
                if (countdown <= 0) { countdown = 30; RefreshData(); }
                refreshLbl.Text = countdown + "s";
            };
        }
        if (!timer.Enabled) timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (timer != null) { timer.Stop(); timer.Dispose(); }
        base.OnFormClosed(e);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MonitorApp());
    }
}
