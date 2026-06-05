using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScreenCaptureToolCN
{
    internal static class NativeDpi
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int awareness);

        public static void Enable()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                if (SetProcessDpiAwareness(2) == 0)
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            NativeDpi.Enable();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class AppConfig
    {
        public string SaveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        public string Format = "png";
        public string CaptureMode = "Region";
        public string HotkeyModifiers = "Ctrl+Shift";
        public string HotkeyKey = "S";
        public bool LaunchAtLogon;
        public bool CopyToClipboard = true;
        public string ScrollProfile = "Standard";

        public static string ConfigPath
        {
            get { return Path.Combine(Application.StartupPath, "screen_capture_settings_native_cn.ini"); }
        }

        public static AppConfig Load()
        {
            var config = new AppConfig();
            if (!File.Exists(ConfigPath))
            {
                config.Save();
                return config;
            }

            foreach (var line in File.ReadAllLines(ConfigPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                {
                    continue;
                }

                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, index).Trim();
                var value = line.Substring(index + 1).Trim();
                switch (key)
                {
                    case "SaveFolder": config.SaveFolder = value; break;
                    case "Format": config.Format = value; break;
                    case "CaptureMode": config.CaptureMode = value; break;
                    case "HotkeyModifiers": config.HotkeyModifiers = value; break;
                    case "HotkeyKey": config.HotkeyKey = value; break;
                    case "LaunchAtLogon": bool.TryParse(value, out config.LaunchAtLogon); break;
                    case "CopyToClipboard": bool.TryParse(value, out config.CopyToClipboard); break;
                    case "ScrollProfile": config.ScrollProfile = value; break;
                }
            }

            return config;
        }

        public void Save()
        {
            File.WriteAllLines(
                ConfigPath,
                new[]
                {
                    "SaveFolder=" + SaveFolder,
                    "Format=" + Format,
                    "CaptureMode=" + CaptureMode,
                    "HotkeyModifiers=" + HotkeyModifiers,
                    "HotkeyKey=" + HotkeyKey,
                    "LaunchAtLogon=" + LaunchAtLogon,
                    "CopyToClipboard=" + CopyToClipboard,
                    "ScrollProfile=" + ScrollProfile
                },
                new UTF8Encoding(true));
        }
    }

    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 9001;
        private const int WmHotkey = 0x0312;
        private const int ScrollSelectionInsetPixels = 3;
        private const int ScrollMaxSegments = 30;
        private const int ScrollPostSelectionDelayMs = 700;
        private const int ScrollFocusDelayMs = 180;
        private const int ScrollBeforePageDownDelayMs = 120;
        private const int ScrollAfterPageDownDelayMs = 850;
        private const int ScrollRetryExtraDelayMs = 350;
        private const int ScrollMaxCaptureRetriesPerStep = 3;
        private const int ScrollMinUsefulOverlap = 60;
        private const int ScrollMaxOverlapRatioPercent = 92;
        private const int DarkBorderThreshold = 18;
        private const int DarkBorderMaxTrim = 6;
        private const int ScrollLowSegmentWarningThreshold = 2;
        private const int CaptureUiHideDelayMs = 140;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly AppConfig config;
        private readonly TextBox txtSaveFolder;
        private readonly ComboBox cmbFormat;
        private readonly ComboBox cmbMode;
        private readonly TextBox txtHotkey;
        private readonly ComboBox cmbScrollProfile;
        private readonly CheckBox chkLaunchAtLogon;
        private readonly CheckBox chkCopyToClipboard;
        private readonly Label lblStatus;
        private readonly NotifyIcon trayIcon;
        private bool allowExit;
        private bool isCaptureInProgress;
        private string pendingHotkeyModifiers;
        private string pendingHotkeyKey;

        public MainForm()
        {
            config = AppConfig.Load();
            pendingHotkeyModifiers = config.HotkeyModifiers;
            pendingHotkeyKey = config.HotkeyKey;

            Text = "窗口截图工具";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(640, 390);
            Size = new Size(640, 390);
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var lblTitle = new Label { Text = "窗口截图工具", Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold), AutoSize = true, Location = new Point(20, 18) };
            var lblDesc = new Label { Text = "支持全局快捷键、区域截图、全屏截图，截图后由你确认是否保存以及保存位置。", AutoSize = true, Location = new Point(22, 56) };
            var lblSaveFolder = new Label { Text = "默认目录", AutoSize = true, Location = new Point(22, 98) };

            txtSaveFolder = new TextBox { Location = new Point(110, 94), Size = new Size(380, 26) };
            var btnBrowse = new Button { Text = "浏览...", Location = new Point(500, 92), Size = new Size(90, 30) };
            btnBrowse.Click += Browse_Click;

            var lblFormat = new Label { Text = "图片格式", AutoSize = true, Location = new Point(22, 142) };
            cmbFormat = new ComboBox { Location = new Point(110, 138), Size = new Size(120, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFormat.Items.AddRange(new object[] { "png", "jpg", "bmp", "gif", "tiff" });

            var lblMode = new Label { Text = "截图模式", AutoSize = true, Location = new Point(260, 142) };
            cmbMode = new ComboBox { Location = new Point(330, 138), Size = new Size(140, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbMode.Items.AddRange(new object[] { "区域截图", "全屏截图", "滚动截图" });

            var lblHotkey = new Label { Text = "快捷键", AutoSize = true, Location = new Point(22, 186) };
            txtHotkey = new TextBox { Location = new Point(110, 182), Size = new Size(180, 26), ReadOnly = true, BackColor = Color.White };
            txtHotkey.Enter += delegate { lblStatus.Text = "请按下新的快捷键组合。"; };
            txtHotkey.KeyDown += HotkeyBox_KeyDown;

            var lblHotkeyHint = new Label { Text = "先点击输入框，再按下组合键，例如 Ctrl + Shift + S", AutoSize = true, Location = new Point(302, 186) };
            var lblScrollProfile = new Label { Text = "滚动稳定性", AutoSize = true, Location = new Point(22, 226) };
            cmbScrollProfile = new ComboBox { Location = new Point(110, 222), Size = new Size(140, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbScrollProfile.Items.AddRange(new object[] { "快速", "标准", "稳定" });
            var lblScrollHint = new Label { Text = "网页复杂或加载慢时，建议选择“稳定”。", AutoSize = true, Location = new Point(262, 226) };
            chkLaunchAtLogon = new CheckBox { Text = "开机自动启动", AutoSize = true, Location = new Point(110, 258) };
            chkCopyToClipboard = new CheckBox { Text = "截图后自动复制到剪贴板", AutoSize = true, Location = new Point(250, 258) };

            var btnSave = new Button { Text = "保存设置", Location = new Point(110, 292), Size = new Size(120, 36) };
            btnSave.Click += Save_Click;
            var btnCapture = new Button { Text = "立即截图", Location = new Point(248, 292), Size = new Size(120, 36) };
            btnCapture.Click += Capture_Click;

            lblStatus = new Label { Text = "准备就绪。", AutoSize = false, Size = new Size(560, 24), Location = new Point(22, 340) };

            Controls.AddRange(new Control[]
            {
                lblTitle, lblDesc, lblSaveFolder, txtSaveFolder, btnBrowse, lblFormat, cmbFormat,
                lblMode, cmbMode, lblHotkey, txtHotkey, lblHotkeyHint, lblScrollProfile, cmbScrollProfile,
                lblScrollHint, chkLaunchAtLogon, chkCopyToClipboard, btnSave, btnCapture, lblStatus
            });

            var iconPath = Path.Combine(Application.StartupPath, "ScreenCaptureTool_CN.ico");
            trayIcon = new NotifyIcon
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Text = "窗口截图工具",
                Visible = true
            };
            trayIcon.DoubleClick += delegate { ShowFromTray(); };

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("打开设置", null, delegate { ShowFromTray(); });
            trayMenu.Items.Add("立即截图", null, delegate { CaptureWorkflow(); });
            trayMenu.Items.Add("退出", null, delegate
            {
                allowExit = true;
                trayIcon.Visible = false;
                Close();
            });
            trayIcon.ContextMenuStrip = trayMenu;

            Resize += MainForm_Resize;
            FormClosing += MainForm_FormClosing;
            HandleDestroyed += MainForm_HandleDestroyed;
            Shown += MainForm_Shown;

            SyncUiFromConfig();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            Directory.CreateDirectory(config.SaveFolder);
            if (!TryRegisterHotkey())
            {
                MessageBox.Show("默认快捷键 " + GetHotkeyDisplay(config.HotkeyModifiers, config.HotkeyKey) + " 注册失败，请更换一个未被占用的组合键。", "快捷键冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                lblStatus.Text = "默认快捷键已被占用，请修改后保存。";
            }
            else
            {
                lblStatus.Text = "准备就绪。当前快捷键：" + GetHotkeyDisplay(config.HotkeyModifiers, config.HotkeyKey);
            }
        }

        private void SyncUiFromConfig()
        {
            txtSaveFolder.Text = config.SaveFolder;
            cmbFormat.SelectedItem = config.Format;
            cmbMode.SelectedItem =
                config.CaptureMode == "FullScreen" ? "全屏截图" :
                config.CaptureMode == "Scroll" ? "滚动截图" :
                "区域截图";
            cmbScrollProfile.SelectedItem =
                string.Equals(config.ScrollProfile, "Fast", StringComparison.OrdinalIgnoreCase) ? "快速" :
                string.Equals(config.ScrollProfile, "Stable", StringComparison.OrdinalIgnoreCase) ? "稳定" :
                "标准";
            txtHotkey.Text = GetHotkeyDisplay(config.HotkeyModifiers, config.HotkeyKey);
            chkLaunchAtLogon.Checked = config.LaunchAtLogon;
            chkCopyToClipboard.Checked = config.CopyToClipboard;
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择截图默认保存目录";
                dialog.SelectedPath = txtSaveFolder.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtSaveFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private void HotkeyBox_KeyDown(object sender, KeyEventArgs e)
        {
            string key;
            string modifiers;
            if (TryReadHotkey(e, out modifiers, out key))
            {
                pendingHotkeyModifiers = modifiers;
                pendingHotkeyKey = key;
                txtHotkey.Text = GetHotkeyDisplay(modifiers, key);
                lblStatus.Text = "已记录新的快捷键，点击“保存设置”后生效。";
            }
            e.SuppressKeyPress = true;
        }

        private static bool TryReadHotkey(KeyEventArgs e, out string modifiers, out string key)
        {
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            key = e.KeyCode.ToString();
            modifiers = string.Join("+", parts.ToArray());

            if (parts.Count == 0) return false;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return false;
            return true;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            try
            {
                ApplySettings();
                lblStatus.Text = "设置已保存。当前快捷键：" + GetHotkeyDisplay(config.HotkeyModifiers, config.HotkeyKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                lblStatus.Text = "设置保存失败。";
            }
        }

        private void ApplySettings()
        {
            if (string.IsNullOrWhiteSpace(txtSaveFolder.Text))
            {
                throw new InvalidOperationException("默认保存目录不能为空。");
            }

            Directory.CreateDirectory(txtSaveFolder.Text.Trim());
            config.SaveFolder = txtSaveFolder.Text.Trim();
            config.Format = Convert.ToString(cmbFormat.SelectedItem) ?? "png";
            var selectedMode = Convert.ToString(cmbMode.SelectedItem);
            config.CaptureMode =
                selectedMode == "全屏截图" ? "FullScreen" :
                selectedMode == "滚动截图" ? "Scroll" :
                "Region";
            config.HotkeyModifiers = pendingHotkeyModifiers;
            config.HotkeyKey = pendingHotkeyKey;
            config.LaunchAtLogon = chkLaunchAtLogon.Checked;
            config.CopyToClipboard = chkCopyToClipboard.Checked;
            config.ScrollProfile =
                Convert.ToString(cmbScrollProfile.SelectedItem) == "快速" ? "Fast" :
                Convert.ToString(cmbScrollProfile.SelectedItem) == "稳定" ? "Stable" :
                "Standard";
            config.Save();
            UpdateStartupShortcut(config.LaunchAtLogon);

            if (!TryRegisterHotkey())
            {
                throw new InvalidOperationException("快捷键注册失败，可能已被其他程序占用。");
            }
        }

        private void Capture_Click(object sender, EventArgs e)
        {
            CaptureWorkflow();
        }

        private void CaptureWorkflow()
        {
            if (isCaptureInProgress)
            {
                lblStatus.Text = "正在处理上一张截图，请先完成当前预览窗口操作。";
                return;
            }

            var restoreMainWindowAfterCapture = Visible && WindowState != FormWindowState.Minimized;
            isCaptureInProgress = true;

            if (restoreMainWindowAfterCapture)
            {
                Hide();
                Application.DoEvents();
                System.Threading.Thread.Sleep(CaptureUiHideDelayMs);
            }
            else
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(60);
            }

            CaptureResult captured = null;
            try
            {
                captured =
                    config.CaptureMode == "FullScreen" ? CaptureFullScreenResult() :
                    config.CaptureMode == "Scroll" ? CaptureScrollResult() :
                    CaptureRegionResult();
                if (captured == null || captured.Bitmap == null)
                {
                    lblStatus.Text = "截图已取消。";
                    return;
                }

                if (config.CopyToClipboard)
                {
                    Clipboard.SetImage((Image)captured.Bitmap.Clone());
                }

                using (var preview = new PreviewForm(captured.Bitmap, captured.SourceBounds, config.SaveFolder, config.Format))
                {
                    var result = restoreMainWindowAfterCapture ? preview.ShowDialog(this) : preview.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrEmpty(preview.SavedPath))
                    {
                        config.SaveFolder = Path.GetDirectoryName(preview.SavedPath) ?? config.SaveFolder;
                        config.Format = Path.GetExtension(preview.SavedPath).TrimStart('.').ToLowerInvariant();
                        config.Save();
                        SyncUiFromConfig();
                        lblStatus.Text = "已保存：" + preview.SavedPath;
                        trayIcon.BalloonTipTitle = "截图已保存";
                        trayIcon.BalloonTipText = preview.SavedPath;
                        trayIcon.ShowBalloonTip(2500);
                    }
                    else
                    {
                        lblStatus.Text = "已取消保存，截图未写入磁盘。";
                    }
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "截图失败。";
                MessageBox.Show("截图失败：" + ex.Message, "窗口截图工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (captured != null && captured.Bitmap != null)
                {
                    captured.Bitmap.Dispose();
                }

                isCaptureInProgress = false;

                if (restoreMainWindowAfterCapture && !allowExit)
                {
                    Show();
                    WindowState = FormWindowState.Normal;
                    Activate();
                }
            }
        }

        private static CaptureResult CaptureFullScreenResult()
        {
            var bounds = SystemInformation.VirtualScreen;
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            return new CaptureResult(bitmap, bounds);
        }

        private static CaptureResult CaptureRegionResult()
        {
            var bounds = SystemInformation.VirtualScreen;
            using (var screenBitmap = new Bitmap(bounds.Width, bounds.Height))
            using (var graphics = Graphics.FromImage(screenBitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                using (var overlay = new SelectionOverlay(screenBitmap, bounds))
                {
                    if (overlay.ShowDialog() != DialogResult.OK || overlay.SelectedRectangle.Width <= 0 || overlay.SelectedRectangle.Height <= 0)
                    {
                        return null;
                    }

                    var rect = NormalizeRectangleToBounds(overlay.SelectedRectangle, bounds);
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        return null;
                    }

                    System.Threading.Thread.Sleep(CaptureUiHideDelayMs);
                    var crop = CaptureScreenArea(rect);
                    return new CaptureResult(crop, rect);
                }
            }
        }

        private CaptureResult CaptureScrollResult()
        {
            var bounds = SystemInformation.VirtualScreen;
            Rectangle rect;
            var targetWindow = GetForegroundWindow();
            using (var screenBitmap = CaptureVirtualScreenBitmap(bounds))
            using (var overlay = new SelectionOverlay(screenBitmap, bounds))
            {
                if (overlay.ShowDialog() != DialogResult.OK || overlay.SelectedRectangle.Width <= 0 || overlay.SelectedRectangle.Height <= 0)
                {
                    return null;
                }

                rect = NormalizeRectangleToBounds(overlay.SelectedRectangle, bounds);
                rect = InsetRectangle(rect, ScrollSelectionInsetPixels, ScrollSelectionInsetPixels);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return null;
                }
            }

            var scrollOptions = GetScrollOptions();
            lblStatus.Text = "滚动截图准备中（" + GetScrollProfileDisplayName() + "），请确保目标窗口可响应 PageDown 翻页。";
            Application.DoEvents();
            System.Threading.Thread.Sleep(scrollOptions.PostSelectionDelayMs);

            var segments = new List<Bitmap>();
            var overlaps = new List<int>();

            try
            {
                Bitmap previous = null;
                for (var i = 0; i < scrollOptions.MaxSegments; i++)
                {
                    if (targetWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(targetWindow);
                    }
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(scrollOptions.FocusDelayMs);

                    var current = CaptureStableScreenArea(rect, scrollOptions.CaptureStableAttempts, scrollOptions.CaptureStableDelayMs);
                    if (current == null)
                    {
                        break;
                    }

                    if (previous != null)
                    {
                        var captureAccepted = false;
                        for (var retry = 0; retry < scrollOptions.MaxCaptureRetriesPerStep; retry++)
                        {
                            if (ImagesNearlyIdentical(previous, current))
                            {
                                current.Dispose();
                                current = null;
                                break;
                            }

                            var overlap = FindVerticalOverlap(previous, current);
                            if (overlap >= scrollOptions.MinUsefulOverlap && !IsOverlapTooLarge(overlap, current.Height, scrollOptions.MaxOverlapRatioPercent))
                            {
                                overlaps.Add(overlap);
                                captureAccepted = true;
                                break;
                            }

                            current.Dispose();
                            current = null;
                            System.Threading.Thread.Sleep(scrollOptions.RetryExtraDelayMs * (retry + 1));
                            current = CaptureStableScreenArea(rect, scrollOptions.CaptureStableAttempts, scrollOptions.CaptureStableDelayMs);
                            if (current == null)
                            {
                                break;
                            }
                        }

                        if (!captureAccepted)
                        {
                            if (current != null)
                            {
                                current.Dispose();
                            }

                            if (segments.Count <= ScrollLowSegmentWarningThreshold)
                            {
                                ShowScrollCaptureSuggestion("滚动拼接不稳定，已提前停止。", rect, scrollOptions, true);
                            }
                            break;
                        }
                    }

                    segments.Add(current);
                    previous = current;
                    if (targetWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(targetWindow);
                    }
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(scrollOptions.BeforePageDownDelayMs);
                    SendKeys.SendWait("{PGDN}");
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(scrollOptions.AfterPageDownDelayMs);
                }

                if (segments.Count == 0)
                {
                    ShowScrollCaptureSuggestion("未获取到有效截图内容。", rect, scrollOptions, false);
                    return null;
                }

                if (segments.Count <= ScrollLowSegmentWarningThreshold)
                {
                    lblStatus.Text = "滚动截图段数较少，可能未完整翻页。";
                    trayIcon.BalloonTipTitle = "滚动截图提示";
                    trayIcon.BalloonTipText = BuildScrollSuggestionText(rect, scrollOptions, true);
                    trayIcon.ShowBalloonTip(3000);
                }

                var stitched = StitchVerticalSegments(segments, overlaps);
                var cleaned = TrimDarkBorders(stitched, DarkBorderThreshold, DarkBorderMaxTrim);
                if (!object.ReferenceEquals(cleaned, stitched))
                {
                    stitched.Dispose();
                }
                lblStatus.Text = "滚动截图完成，共拼接 " + segments.Count + " 段。";
                return new CaptureResult(cleaned, new Rectangle(rect.X, rect.Y, cleaned.Width, cleaned.Height));
            }
            finally
            {
                foreach (var segment in segments)
                {
                    segment.Dispose();
                }
            }
        }

        private static Bitmap CaptureVirtualScreenBitmap(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            return bitmap;
        }

        private static Bitmap CaptureScreenArea(Rectangle rect)
        {
            rect = NormalizeRectangleToBounds(rect, SystemInformation.VirtualScreen);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return null;
            }

            var bitmap = new Bitmap(rect.Width, rect.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(new Point(rect.X, rect.Y), Point.Empty, rect.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        private static Bitmap CaptureStableScreenArea(Rectangle rect, int maxAttempts, int delayMs)
        {
            Bitmap previous = null;
            for (var attempt = 0; attempt < Math.Max(2, maxAttempts); attempt++)
            {
                var current = CaptureScreenArea(rect);
                if (current == null)
                {
                    if (previous != null)
                    {
                        previous.Dispose();
                    }
                    return null;
                }

                if (previous != null && ImagesNearlyIdentical(previous, current))
                {
                    previous.Dispose();
                    return current;
                }

                if (previous != null)
                {
                    previous.Dispose();
                }
                previous = current;
                System.Threading.Thread.Sleep(Math.Max(30, delayMs));
            }

            return previous;
        }

        private static Rectangle NormalizeRectangleToBounds(Rectangle rect, Rectangle bounds)
        {
            var normalized = Rectangle.Intersect(rect, bounds);
            return normalized;
        }

        private static Rectangle InsetRectangle(Rectangle rect, int insetX, int insetY)
        {
            var insetWidth = Math.Max(0, rect.Width - insetX * 2);
            var insetHeight = Math.Max(0, rect.Height - insetY * 2);
            return new Rectangle(rect.X + insetX, rect.Y + insetY, insetWidth, insetHeight);
        }

        private static bool ImagesNearlyIdentical(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
            {
                return false;
            }

            long diff = 0;
            var sampleStepX = Math.Max(1, a.Width / 40);
            var sampleStepY = Math.Max(1, a.Height / 40);
            var samples = 0;
            for (var y = 0; y < a.Height; y += sampleStepY)
            {
                for (var x = 0; x < a.Width; x += sampleStepX)
                {
                    var ca = a.GetPixel(x, y);
                    var cb = b.GetPixel(x, y);
                    diff += Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
                    samples++;
                }
            }

            if (samples == 0)
            {
                return true;
            }

            var avg = diff / samples;
            return avg < 12;
        }

        private static int FindVerticalOverlap(Bitmap previous, Bitmap current)
        {
            var maxOverlap = Math.Min(previous.Height, current.Height) - 2;
            var minOverlap = Math.Min(60, maxOverlap);
            if (maxOverlap < minOverlap)
            {
                return 0;
            }

            var bestOverlap = 0;
            var bestScore = long.MaxValue;
            for (var overlap = maxOverlap; overlap >= minOverlap; overlap -= 2)
            {
                var score = CompareOverlapScore(previous, current, overlap);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOverlap = overlap;
                }
            }

            return bestScore <= 40 ? bestOverlap : 0;
        }

        private static long CompareOverlapScore(Bitmap previous, Bitmap current, int overlap)
        {
            long diff = 0;
            var left = previous.Width / 5;
            var right = previous.Width - left;
            var sampleStepX = Math.Max(1, (right - left) / 36);
            var sampleStepY = Math.Max(1, overlap / 25);
            var samples = 0;

            for (var y = 0; y < overlap; y += sampleStepY)
            {
                var py = previous.Height - overlap + y;
                var cy = y;
                for (var x = left; x < right; x += sampleStepX)
                {
                    var cp = previous.GetPixel(x, py);
                    var cc = current.GetPixel(x, cy);
                    diff += Math.Abs(cp.R - cc.R) + Math.Abs(cp.G - cc.G) + Math.Abs(cp.B - cc.B);
                    samples++;
                }
            }

            if (samples == 0)
            {
                return long.MaxValue;
            }

            return diff / samples;
        }

        private static Bitmap StitchVerticalSegments(List<Bitmap> segments, List<int> overlaps)
        {
            var totalHeight = segments[0].Height;
            for (var i = 1; i < segments.Count; i++)
            {
                var overlap = i - 1 < overlaps.Count ? overlaps[i - 1] : 0;
                totalHeight += Math.Max(1, segments[i].Height - overlap);
            }

            var output = new Bitmap(segments[0].Width, totalHeight);
            using (var graphics = Graphics.FromImage(output))
            {
                graphics.Clear(Color.White);
                var y = 0;
                graphics.DrawImage(segments[0], 0, 0);
                y += segments[0].Height;

                for (var i = 1; i < segments.Count; i++)
                {
                    var overlap = i - 1 < overlaps.Count ? overlaps[i - 1] : 0;
                    var clippedOverlap = Math.Max(0, Math.Min(overlap, segments[i].Height - 1));
                    var srcHeight = segments[i].Height - clippedOverlap;
                    if (srcHeight <= 0)
                    {
                        continue;
                    }

                    var srcRect = new Rectangle(0, clippedOverlap, segments[i].Width, srcHeight);
                    var destRect = new Rectangle(0, y, srcRect.Width, srcRect.Height);
                    graphics.DrawImage(segments[i], destRect, srcRect, GraphicsUnit.Pixel);
                    y += srcRect.Height;
                }
            }
            return output;
        }

        private void ShowScrollCaptureSuggestion(string reason, Rectangle rect, ScrollCaptureOptions scrollOptions, bool suggestStableProfile)
        {
            var message = reason + Environment.NewLine + Environment.NewLine + BuildScrollSuggestionText(rect, scrollOptions, suggestStableProfile);
            lblStatus.Text = reason;
            MessageBox.Show(message, "滚动截图建议", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string BuildScrollSuggestionText(Rectangle rect, ScrollCaptureOptions scrollOptions, bool suggestStableProfile)
        {
            var tips = new List<string>();
            if (suggestStableProfile && !string.Equals(config.ScrollProfile, "Stable", StringComparison.OrdinalIgnoreCase))
            {
                tips.Add("切换到“稳定”模式后重试。当前模式：" + GetScrollProfileDisplayName());
            }

            tips.Add("尽量缩小选区，避开滚动条、窗口边框、悬浮工具栏和固定页头。当前选区：" + rect.Width + " × " + rect.Height + " 像素。");
            tips.Add("确认目标窗口在前台，且可响应 PageDown 翻页。必要时先点击内容区域再重试。");

            if (scrollOptions.AfterPageDownDelayMs < 900)
            {
                tips.Add("如果页面加载较慢，可改用“稳定”模式，让翻页后等待更久。当前翻页后等待：" + scrollOptions.AfterPageDownDelayMs + " ms。");
            }
            else
            {
                tips.Add("当前已使用较长等待时间；如果仍不稳定，请缩小选区或避开动态区域。当前翻页后等待：" + scrollOptions.AfterPageDownDelayMs + " ms。");
            }

            return "建议：" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", tips.ToArray());
        }

        private ScrollCaptureOptions GetScrollOptions()
        {
            if (string.Equals(config.ScrollProfile, "Fast", StringComparison.OrdinalIgnoreCase))
            {
                return new ScrollCaptureOptions(20, 350, 120, 60, 450, 200, 2, 50, 95, 6, 100);
            }

            if (string.Equals(config.ScrollProfile, "Stable", StringComparison.OrdinalIgnoreCase))
            {
                return new ScrollCaptureOptions(36, 950, 220, 180, 1150, 450, 4, 70, 90, 12, 220);
            }

            return new ScrollCaptureOptions(30, 700, 180, 120, 850, 350, 3, 60, 92, 10, 170);
        }

        private string GetScrollProfileDisplayName()
        {
            if (string.Equals(config.ScrollProfile, "Fast", StringComparison.OrdinalIgnoreCase))
            {
                return "快速";
            }

            if (string.Equals(config.ScrollProfile, "Stable", StringComparison.OrdinalIgnoreCase))
            {
                return "稳定";
            }

            return "标准";
        }

        private static bool IsOverlapTooLarge(int overlap, int currentHeight, int maxOverlapRatioPercent)
        {
            if (currentHeight <= 0)
            {
                return true;
            }

            return overlap * 100 / currentHeight >= maxOverlapRatioPercent;
        }

        private static Bitmap TrimDarkBorders(Bitmap source, int darkThreshold, int maxTrim)
        {
            if (source == null)
            {
                return null;
            }

            var leftTrim = MeasureDarkBorder(source, true, true, darkThreshold, maxTrim);
            var rightTrim = MeasureDarkBorder(source, true, false, darkThreshold, maxTrim);
            var topTrim = MeasureDarkBorder(source, false, true, darkThreshold, maxTrim);
            var bottomTrim = MeasureDarkBorder(source, false, false, darkThreshold, maxTrim);

            if (leftTrim <= 0 && rightTrim <= 0 && topTrim <= 0 && bottomTrim <= 0)
            {
                return source;
            }

            var width = source.Width - leftTrim - rightTrim;
            var height = source.Height - topTrim - bottomTrim;
            if (width <= 10 || height <= 10)
            {
                return source;
            }

            var trimmed = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(trimmed))
            {
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(leftTrim, topTrim, width, height), GraphicsUnit.Pixel);
            }
            return trimmed;
        }

        private static int MeasureDarkBorder(Bitmap bitmap, bool verticalEdge, bool fromStart, int darkThreshold, int maxTrim)
        {
            var limit = Math.Min(maxTrim, verticalEdge ? bitmap.Width / 8 : bitmap.Height / 8);
            var trim = 0;
            for (var offset = 0; offset < limit; offset++)
            {
                if (!IsMostlyDarkLine(bitmap, verticalEdge, fromStart ? offset : (verticalEdge ? bitmap.Width - 1 - offset : bitmap.Height - 1 - offset), darkThreshold))
                {
                    break;
                }
                trim++;
            }
            return trim;
        }

        private static bool IsMostlyDarkLine(Bitmap bitmap, bool verticalLine, int index, int darkThreshold)
        {
            var total = 0;
            var dark = 0;
            if (verticalLine)
            {
                var step = Math.Max(1, bitmap.Height / 120);
                for (var y = 0; y < bitmap.Height; y += step)
                {
                    total++;
                    if (IsDark(bitmap.GetPixel(index, y), darkThreshold))
                    {
                        dark++;
                    }
                }
            }
            else
            {
                var step = Math.Max(1, bitmap.Width / 120);
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    total++;
                    if (IsDark(bitmap.GetPixel(x, index), darkThreshold))
                    {
                        dark++;
                    }
                }
            }

            return total > 0 && dark * 100 / total >= 92;
        }

        private static bool IsDark(Color color, int darkThreshold)
        {
            return color.R <= darkThreshold && color.G <= darkThreshold && color.B <= darkThreshold;
        }

        private bool TryRegisterHotkey()
        {
            if (!IsHandleCreated)
            {
                return false;
            }

            UnregisterHotKey(Handle, HotkeyId);
            config.HotkeyModifiers = pendingHotkeyModifiers;
            config.HotkeyKey = pendingHotkeyKey;

            Keys parsedKey;
            if (!Enum.TryParse(config.HotkeyKey, out parsedKey))
            {
                parsedKey = Keys.S;
            }
            return RegisterHotKey(Handle, HotkeyId, GetModifierValue(config.HotkeyModifiers), (uint)parsedKey);
        }

        private static uint GetModifierValue(string modifiers)
        {
            uint result = 0;
            foreach (var part in (modifiers ?? string.Empty).Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim())
                {
                    case "Alt": result |= 0x1; break;
                    case "Ctrl": result |= 0x2; break;
                    case "Shift": result |= 0x4; break;
                    case "Win": result |= 0x8; break;
                }
            }
            return result;
        }

        private static string GetHotkeyDisplay(string modifiers, string key)
        {
            if (string.IsNullOrWhiteSpace(modifiers))
            {
                return key;
            }
            return modifiers.Replace("+", " + ") + " + " + key;
        }

        private void UpdateStartupShortcut(bool enabled)
        {
            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupDir, "ScreenCaptureTool_CN_Native.lnk");
            if (enabled)
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    throw new InvalidOperationException("无法创建开机启动快捷方式。");
                }

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = Application.ExecutablePath;
                shortcut.WorkingDirectory = Application.StartupPath;
                shortcut.IconLocation = Application.ExecutablePath;
                shortcut.Save();
            }
            else if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                CaptureWorkflow();
            }

            base.WndProc(ref m);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                trayIcon.ShowBalloonTip(2000, "截图工具仍在运行", "你可以继续使用全局快捷键截图。", ToolTipIcon.Info);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                Hide();
                trayIcon.ShowBalloonTip(2000, "已最小化到托盘", "可通过托盘图标重新打开设置窗口。", ToolTipIcon.Info);
            }
        }

        private void MainForm_HandleDestroyed(object sender, EventArgs e)
        {
            if (IsHandleCreated)
            {
                UnregisterHotKey(Handle, HotkeyId);
            }
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
    }

    internal sealed class ScrollCaptureOptions
    {
        public ScrollCaptureOptions(int maxSegments, int postSelectionDelayMs, int focusDelayMs, int beforePageDownDelayMs, int afterPageDownDelayMs, int retryExtraDelayMs, int maxCaptureRetriesPerStep, int minUsefulOverlap, int maxOverlapRatioPercent, int captureStableAttempts, int captureStableDelayMs)
        {
            MaxSegments = maxSegments;
            PostSelectionDelayMs = postSelectionDelayMs;
            FocusDelayMs = focusDelayMs;
            BeforePageDownDelayMs = beforePageDownDelayMs;
            AfterPageDownDelayMs = afterPageDownDelayMs;
            RetryExtraDelayMs = retryExtraDelayMs;
            MaxCaptureRetriesPerStep = maxCaptureRetriesPerStep;
            MinUsefulOverlap = minUsefulOverlap;
            MaxOverlapRatioPercent = maxOverlapRatioPercent;
            CaptureStableAttempts = captureStableAttempts;
            CaptureStableDelayMs = captureStableDelayMs;
        }

        public int MaxSegments { get; private set; }
        public int PostSelectionDelayMs { get; private set; }
        public int FocusDelayMs { get; private set; }
        public int BeforePageDownDelayMs { get; private set; }
        public int AfterPageDownDelayMs { get; private set; }
        public int RetryExtraDelayMs { get; private set; }
        public int MaxCaptureRetriesPerStep { get; private set; }
        public int MinUsefulOverlap { get; private set; }
        public int MaxOverlapRatioPercent { get; private set; }
        public int CaptureStableAttempts { get; private set; }
        public int CaptureStableDelayMs { get; private set; }
    }

    internal sealed class CaptureResult
    {
        public CaptureResult(Bitmap bitmap, Rectangle sourceBounds)
        {
            Bitmap = bitmap;
            SourceBounds = sourceBounds;
        }

        public Bitmap Bitmap { get; private set; }

        public Rectangle SourceBounds { get; private set; }
    }

    internal sealed class PreviewForm : Form
    {
        private readonly Bitmap previewBitmap;
        private readonly PictureBox pictureBox;
        private readonly Panel imagePanel;
        private readonly TextBox txtOcrResult;
        private readonly Label lblStatus;
        private readonly ToolStripButton btnOcrSelect;
        private readonly string defaultFolder;
        private readonly string defaultFormat;
        private float zoomFactor = 1.0f;
        private bool isSelectingOcr;
        private bool isDraggingSelection;
        private int ocrAppendCount; // 连续 OCR 次数计数，用于状态提示
        private Point selectionStart;
        private Rectangle selectionRect;

        public string SavedPath { get; private set; }

        public PreviewForm(Bitmap sourceBitmap, Rectangle sourceBounds, string saveFolder, string format)
        {
            previewBitmap = (Bitmap)sourceBitmap.Clone();
            defaultFolder = saveFolder;
            defaultFormat = string.IsNullOrWhiteSpace(format) ? "png" : format;

            Text = "截图预览";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(820, 620);
            Size = new Size(
                Math.Min(1200, Math.Max(900, sourceBounds.Width + 160)),
                Math.Min(900, Math.Max(680, sourceBounds.Height + 220)));

            var toolStrip = new ToolStrip();
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip.Dock = DockStyle.Top;
            toolStrip.Items.Add("另存为", null, SaveAs_Click);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add("放大", null, delegate { ChangeZoom(1.25f); });
            toolStrip.Items.Add("缩小", null, delegate { ChangeZoom(0.8f); });
            toolStrip.Items.Add("100%", null, delegate { SetZoom(1.0f); });
            toolStrip.Items.Add(new ToolStripSeparator());
            btnOcrSelect = new ToolStripButton("OCR框选");
            btnOcrSelect.Click += ToggleOcrSelection_Click;
            toolStrip.Items.Add(btnOcrSelect);
            toolStrip.Items.Add("复制文字", null, CopyOcrText_Click);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add("关闭", null, delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            });

            imagePanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            pictureBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = previewBitmap,
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = Padding.Empty,
                Location = new Point(0, 0)
            };
            pictureBox.MouseDown += PictureBox_MouseDown;
            pictureBox.MouseMove += PictureBox_MouseMove;
            pictureBox.MouseUp += PictureBox_MouseUp;
            pictureBox.Paint += PictureBox_Paint;
            pictureBox.MouseWheel += PreviewMouseWheel_Zoom;
            imagePanel.MouseWheel += PreviewMouseWheel_Zoom;
            imagePanel.Controls.Add(pictureBox);

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 180,
                Padding = new Padding(12, 10, 12, 12)
            };

            lblStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "预览窗口已打开。可缩放查看；点击“OCR框选”后拖动鼠标识别指定区域。"
            };

            txtOcrResult = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true
            };

            bottomPanel.Controls.Add(txtOcrResult);
            bottomPanel.Controls.Add(lblStatus);

            pictureBox.DoubleClick += SaveAs_Click;
            KeyDown += PreviewForm_KeyDown;
            MouseWheel += PreviewMouseWheel_Zoom;

            Controls.Add(imagePanel);
            Controls.Add(bottomPanel);
            Controls.Add(toolStrip);
            InitializeWindowBounds(sourceBounds);
            SetZoom(1.0f);
        }

        private void PreviewForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.S))
            {
                SaveAs_Click(sender, EventArgs.Empty);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
            {
                ChangeZoom(1.25f);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
            {
                ChangeZoom(0.8f);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                if (isSelectingOcr)
                {
                    isSelectingOcr = false;
                    isDraggingSelection = false;
                    selectionRect = Rectangle.Empty;
                    btnOcrSelect.Checked = false;
                    pictureBox.Cursor = Cursors.Default;
                    pictureBox.Invalidate();
                    lblStatus.Text = "已退出连续 OCR 框选模式。";
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                e.SuppressKeyPress = true;
            }
        }

        private void InitializeWindowBounds(Rectangle sourceBounds)
        {
            var workingArea = Screen.FromPoint(new Point(sourceBounds.Left, sourceBounds.Top)).WorkingArea;
            var finalWidth = Math.Min(sourceBounds.Width, workingArea.Width);
            var finalHeight = Math.Min(sourceBounds.Height, workingArea.Height);

            var x = sourceBounds.Left;
            var y = sourceBounds.Top;

            if (x + finalWidth > workingArea.Right)
            {
                x = workingArea.Right - finalWidth;
            }
            if (y + finalHeight > workingArea.Bottom)
            {
                y = workingArea.Bottom - finalHeight;
            }
            if (x < workingArea.Left)
            {
                x = workingArea.Left;
            }
            if (y < workingArea.Top)
            {
                y = workingArea.Top;
            }

            Bounds = new Rectangle(x, y, finalWidth, finalHeight);
        }

        private void SaveAs_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存截图";
                dialog.InitialDirectory = Directory.Exists(defaultFolder) ? defaultFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                dialog.FileName = "Screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "." + defaultFormat;
                dialog.Filter = "PNG 图片 (*.png)|*.png|JPG 图片 (*.jpg)|*.jpg|BMP 图片 (*.bmp)|*.bmp|GIF 图片 (*.gif)|*.gif|TIFF 图片 (*.tiff)|*.tiff";
                dialog.DefaultExt = defaultFormat;
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var dir = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                previewBitmap.Save(dialog.FileName, GetImageFormatFromExtension(Path.GetExtension(dialog.FileName)));
                SavedPath = dialog.FileName;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void ToggleOcrSelection_Click(object sender, EventArgs e)
        {
            isSelectingOcr = !isSelectingOcr;
            isDraggingSelection = false;
            selectionRect = Rectangle.Empty;
            btnOcrSelect.Checked = isSelectingOcr;
            pictureBox.Cursor = isSelectingOcr ? Cursors.Cross : Cursors.Default;
            lblStatus.Text = isSelectingOcr
                ? "已进入连续 OCR 框选模式，请在图片上拖动鼠标；按 Esc 或再次点击按钮可退出。"
                : "OCR 框选已关闭。";
            pictureBox.Invalidate();
        }

        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isSelectingOcr || e.Button != MouseButtons.Left)
            {
                return;
            }

            isDraggingSelection = true;
            selectionStart = e.Location;
            selectionRect = new Rectangle(e.Location, Size.Empty);
            pictureBox.Invalidate();
        }

        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSelectingOcr || !isDraggingSelection)
            {
                return;
            }

            selectionRect = CreateNormalizedRectangle(selectionStart, e.Location);
            pictureBox.Invalidate();
        }

        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isSelectingOcr || !isDraggingSelection)
            {
                return;
            }

            isDraggingSelection = false;
            selectionRect = CreateNormalizedRectangle(selectionStart, e.Location);
            pictureBox.Invalidate();

            if (selectionRect.Width < 8 || selectionRect.Height < 8)
            {
                selectionRect = Rectangle.Empty;
                pictureBox.Invalidate();
                lblStatus.Text = "OCR 区域太小，请重新框选。";
                return;
            }

            try
            {
                var imageRect = ScaleRectToImage(selectionRect);
                var text = OcrHelper.RecognizeBitmap(CropBitmap(previewBitmap, imageRect));
                if (string.IsNullOrWhiteSpace(text))
                {
                    lblStatus.Text = "本次 OCR 未识别到明显文字，可继续框选。";
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(txtOcrResult.Text))
                    {
                        txtOcrResult.AppendText(Environment.NewLine);
                    }

                    txtOcrResult.AppendText(text);
                    ocrAppendCount++; // 连续 OCR 追加计数
                    lblStatus.Text = "OCR 已追加第 " + ocrAppendCount + " 段文本，可继续框选。";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "OCR 识别失败，可继续框选重试。";
                MessageBox.Show("OCR 识别失败：" + ex.Message, "窗口截图工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                selectionRect = Rectangle.Empty;
                pictureBox.Invalidate();
                if (isSelectingOcr)
                {
                    btnOcrSelect.Checked = true;
                    pictureBox.Cursor = Cursors.Cross;
                }
            }
        }

        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (selectionRect.Width <= 0 || selectionRect.Height <= 0)
            {
                return;
            }

            using (var pen = new Pen(Color.FromArgb(255, 71, 126, 230), 2))
            using (var brush = new SolidBrush(Color.FromArgb(50, 71, 126, 230)))
            {
                e.Graphics.FillRectangle(brush, selectionRect);
                e.Graphics.DrawRectangle(pen, selectionRect);
            }
        }

        private void PreviewMouseWheel_Zoom(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != Keys.Control)
            {
                return;
            }

            if (e.Delta > 0)
            {
                ChangeZoom(1.1f);
            }
            else if (e.Delta < 0)
            {
                ChangeZoom(0.9f);
            }
        }

        private void ChangeZoom(float factor)
        {
            SetZoom(zoomFactor * factor);
        }

        private void SetZoom(float zoom)
        {
            zoomFactor = Math.Max(0.2f, Math.Min(5.0f, zoom));
            pictureBox.Size = new Size(
                Math.Max(1, (int)Math.Round(previewBitmap.Width * zoomFactor)),
                Math.Max(1, (int)Math.Round(previewBitmap.Height * zoomFactor)));
            lblStatus.Text = "当前缩放：" + (int)Math.Round(zoomFactor * 100) + "%";
            pictureBox.Invalidate();
        }

        private static Rectangle CreateNormalizedRectangle(Point start, Point end)
        {
            var x = Math.Min(start.X, end.X);
            var y = Math.Min(start.Y, end.Y);
            var width = Math.Abs(end.X - start.X);
            var height = Math.Abs(end.Y - start.Y);
            return new Rectangle(x, y, width, height);
        }

        private Rectangle ScaleRectToImage(Rectangle viewRect)
        {
            var x = (int)Math.Round(viewRect.X / zoomFactor);
            var y = (int)Math.Round(viewRect.Y / zoomFactor);
            var width = (int)Math.Round(viewRect.Width / zoomFactor);
            var height = (int)Math.Round(viewRect.Height / zoomFactor);

            var rect = new Rectangle(x, y, width, height);
            rect.Intersect(new Rectangle(0, 0, previewBitmap.Width, previewBitmap.Height));
            return rect;
        }

        private static Bitmap CropBitmap(Bitmap source, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                throw new InvalidOperationException("OCR 区域无效。");
            }

            var result = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(result))
            {
                g.DrawImage(source, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
            }
            return result;
        }

        private void CopyOcrText_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtOcrResult.Text))
            {
                lblStatus.Text = "当前没有可复制的 OCR 文本。";
                return;
            }

            Clipboard.SetText(txtOcrResult.Text);
            lblStatus.Text = "OCR 文本已复制到剪贴板。";
        }

        private static ImageFormat GetImageFormatFromExtension(string extension)
        {
            switch ((extension ?? string.Empty).TrimStart('.').ToLowerInvariant())
            {
                case "jpg":
                case "jpeg":
                    return ImageFormat.Jpeg;
                case "bmp":
                    return ImageFormat.Bmp;
                case "gif":
                    return ImageFormat.Gif;
                case "tiff":
                    return ImageFormat.Tiff;
                default:
                    return ImageFormat.Png;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (pictureBox.Image != null)
                {
                    pictureBox.Image.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    internal static class OcrHelper
    {
        public static string RecognizeBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                throw new InvalidOperationException("没有可供 OCR 的图片。");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), "screen_capture_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bitmap.Save(tempPath, ImageFormat.Png);
                return RecognizeFile(tempPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }

                bitmap.Dispose();
            }
        }

        private static string RecognizeFile(string imagePath)
        {
            var escapedPath = imagePath.Replace("'", "''");
            var script = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrEngine,Windows.Media.Ocr,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrResult,Windows.Media.Ocr,ContentType=WindowsRuntime]
function AsTaskByType($typeObj, $op) {
  $m = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
  $gm = $m.MakeGenericMethod(@($typeObj))
  return $gm.Invoke($null, @($op))
}
$img = '" + escapedPath + @"'
$file = (AsTaskByType ([Windows.Storage.StorageFile]) ([Windows.Storage.StorageFile]::GetFileFromPathAsync($img))).GetAwaiter().GetResult()
$stream = (AsTaskByType ([Windows.Storage.Streams.IRandomAccessStream]) ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read))).GetAwaiter().GetResult()
$decoder = (AsTaskByType ([Windows.Graphics.Imaging.BitmapDecoder]) ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream))).GetAwaiter().GetResult()
$bitmap = (AsTaskByType ([Windows.Graphics.Imaging.SoftwareBitmap]) ($decoder.GetSoftwareBitmapAsync())).GetAwaiter().GetResult()
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
$result = (AsTaskByType ([Windows.Media.Ocr.OcrResult]) ($engine.RecognizeAsync($bitmap))).GetAwaiter().GetResult()
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Output $result.Text
";

            var bytes = Encoding.Unicode.GetBytes(script);
            var encodedScript = Convert.ToBase64String(bytes);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动 OCR 识别进程。");
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "系统 OCR 识别失败。" : error.Trim());
                }

                return output.Trim();
            }
        }
    }

    internal sealed class SelectionOverlay : Form
    {
        private readonly Bitmap background;
        private readonly Rectangle virtualBounds;
        private Point startPoint;
        private Rectangle currentRect;
        private bool dragging;

        public SelectionOverlay(Bitmap backgroundBitmap, Rectangle bounds)
        {
            background = (Bitmap)backgroundBitmap.Clone();
            virtualBounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Opacity = 0.25;
            AutoScaleMode = AutoScaleMode.None;

            MouseDown += Overlay_MouseDown;
            MouseMove += Overlay_MouseMove;
            MouseUp += Overlay_MouseUp;
            KeyDown += Overlay_KeyDown;
        }

        public Rectangle SelectedRectangle { get; private set; }

        private void Overlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void Overlay_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            startPoint = e.Location;
            currentRect = new Rectangle(e.Location, Size.Empty);
            dragging = true;
            Invalidate();
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            var x = Math.Min(startPoint.X, e.X);
            var y = Math.Min(startPoint.Y, e.Y);
            var width = Math.Abs(e.X - startPoint.X);
            var height = Math.Abs(e.Y - startPoint.Y);
            currentRect = new Rectangle(x, y, width, height);
            Invalidate();
        }

        private void Overlay_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (currentRect.Width > 0 && currentRect.Height > 0)
            {
                SelectedRectangle = new Rectangle(currentRect.X + virtualBounds.X, currentRect.Y + virtualBounds.Y, currentRect.Width, currentRect.Height);
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (currentRect.Width <= 0 || currentRect.Height <= 0) return;
            var srcRect = new Rectangle(currentRect.X, currentRect.Y, currentRect.Width, currentRect.Height);
            e.Graphics.DrawImage(background, currentRect, srcRect, GraphicsUnit.Pixel);
            using (var pen = new Pen(Color.FromArgb(255, 77, 166, 255), 2))
            {
                e.Graphics.DrawRectangle(pen, currentRect);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                background.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
