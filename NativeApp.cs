using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal static class AppLauncher {
    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    internal static Icon CreatePowerIcon(Color color, string charge) {
        using (var bitmap = new Bitmap(32,32)) {
            using (var drawing = Graphics.FromImage(bitmap))
            using (var digits = new SolidBrush(color))
            using (var font = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var center = new StringFormat(StringFormat.GenericTypographic) {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
            }) {
                drawing.Clear(Color.FromArgb(28,34,41));
                drawing.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float width = drawing.MeasureString(charge,font,PointF.Empty,center).Width;
                float scale = width > 29 ? 29 / width : 1;
                drawing.TranslateTransform(16,0);
                drawing.ScaleTransform(scale,1);
                drawing.DrawString(charge,font,digits,new RectangleF(-16 / scale,0,32 / scale,32),center);
            }
            IntPtr handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
    }
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeteLaptopPower");
    internal static readonly string ConfigPath = Path.Combine(Folder,"config.json");
    internal static Point TrayMenuPosition(Point cursor, Rectangle area, Size size) {
        return new Point(Math.Max(area.Left,Math.Min(cursor.X-size.Width/2,area.Right-size.Width)),
            Math.Max(area.Top,Math.Min(cursor.Y-size.Height-64,area.Bottom-size.Height-32)));
    }
    internal static void OpenReadme() {
        try { Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.txt")); }
        catch (Exception e) { ShowError(e); }
    }
    internal static void Log(string message) {
        try { Directory.CreateDirectory(Folder); File.AppendAllText(Path.Combine(Folder,"app.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine); } catch {}
    }
    static byte[] Resource(string name) {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if (input == null) throw new InvalidOperationException("App resource missing: " + name);
            using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
        }
    }
    [STAThread]
    static int Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { ShowError(e.Exception); };
        try {
            if (args.Length == 1 && args[0] == "--verify") {
                if (Resource("PowerBackend.ps1").Length < 1000 || Resource("AsusControl.cs").Length < 1000 || Resource("Configuration.cs").Length < 1000) return 1;
                return 0;
            }
            using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PeteLaptopPowerActivate"))
            using (var mutex = new Mutex(false,"Local\\PeteLaptopPowerNativeV2")) {
                bool owned;
                try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) {
                    AllowSetForegroundWindow(-1);
                    activate.Set();
                    return 0;
                }
                try {
                    Directory.CreateDirectory(Folder);
                    foreach (string resource in new [] {"PowerBackend.ps1","AsusControl.cs","Configuration.cs"})
                        File.WriteAllBytes(Path.Combine(Folder,resource),Resource(resource));
                    var config = PowerConfig.Load(ConfigPath);
                    string[] oldNames = { "Save power", "Everyday - 80%", "Full performance" };
                    var defaults = PowerConfig.Defaults();
                    bool renamed = false;
                    for (int i = 0; i < oldNames.Length; i++) {
                        if (config.modes[i].name != oldNames[i]) continue;
                        config.modes[i].name = defaults.modes[i].name;
                        renamed = true;
                    }
                    if (renamed) PowerConfig.Save(ConfigPath, config);
                    Log("Starting native window v3.");
                    using (var window = new PowerWindow())
                    using (var menu = new ContextMenuStrip())
                    using (var tray = new NotifyIcon())
                    using (var watch = new PowerWatch(window, tray))
                    using (var clicks = new TrayClicks(tray, () => watch.StatusText, window.BringForward))
                    using (var activationTimer = new System.Windows.Forms.Timer { Interval = 125 }) {
                        menu.Items.Add("Open Laptop Power", null, delegate { window.BringForward(); });
                        var modeMenu = new ToolStripMenuItem("Power mode");
                        var modeItems = new ToolStripMenuItem[3];
                        for (int i = 0; i < modeItems.Length; i++) {
                            int index = i;
                            modeItems[i] = new ToolStripMenuItem(window.ModeName(index));
                            modeItems[i].Click += async delegate { await window.ApplyMode(index); };
                            modeMenu.DropDownItems.Add(modeItems[i]);
                        }
                        menu.Items.Add(modeMenu);
                        var chargeMenu = new ToolStripMenuItem("Battery charge limit");
                        var limitOn = new ToolStripMenuItem("Enable 80% limit");
                        var limitOff = new ToolStripMenuItem("Disable limit (100%)");
                        limitOn.Click += async delegate {
                            bool ok = await window.SetChargeLimit(80);
                            tray.ShowBalloonTip(4000, "Laptop Power", ok ? "ASUS accepted the 80% limit." : "Could not set the charge limit. See the main window.", ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
                        };
                        limitOff.Click += async delegate {
                            bool ok = await window.SetChargeLimit(100);
                            tray.ShowBalloonTip(4000, "Laptop Power", ok ? "ASUS accepted the 100% limit." : "Could not set the charge limit. See the main window.", ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
                        };
                        chargeMenu.DropDownItems.Add(limitOn);
                        chargeMenu.DropDownItems.Add(limitOff);
                        menu.Items.Add(chargeMenu);
                        var gpu = new ToolStripMenuItem("NVIDIA GPU");
                        var gpuOn = new ToolStripMenuItem("On");
                        var gpuOff = new ToolStripMenuItem("Off");
                        gpuOn.Click += async delegate { await watch.OverrideGpu(true); };
                        gpuOff.Click += async delegate { await watch.OverrideGpu(false); };
                        gpu.DropDownItems.Add(gpuOn);
                        gpu.DropDownItems.Add(gpuOff);
                        Action refreshGpuMenu = delegate {
                            gpu.Enabled = watch.CanOverrideGpu;
                            gpuOn.Checked = watch.GpuStatus == "On";
                            gpuOff.Checked = watch.GpuStatus == "Off";
                        };
                        gpu.DropDownOpening += delegate { refreshGpuMenu(); };
                        menu.Items.Add(gpu);
                        menu.Items.Add("Help", null, delegate { OpenReadme(); });
                        menu.Items.Add(new ToolStripSeparator());
                        menu.Items.Add("Exit Laptop Power", null, async delegate {
                            Log("Tray exit requested.");
                            await watch.WaitForGpu();
                            window.ExitFromTray();
                        });
                        menu.Opening += delegate {
                            refreshGpuMenu();
                            string activeName;
                            int active = watch.ActiveMode(out activeName);
                            modeMenu.Enabled = !window.IsBusy;
                            for (int i = 0; i < modeItems.Length; i++) {
                                modeItems[i].Text = window.ModeName(i);
                                modeItems[i].Checked = i == active;
                            }
                            chargeMenu.Enabled = window.ChargeLimitAvailable && !window.ChargeLimitBusy;
                            limitOn.Checked = window.RequestedChargeLimit == 80;
                            limitOff.Checked = window.RequestedChargeLimit == 100;
                        };
                        // Let NotifyIcon handle menu activation and dismissal. Move the
                        // opened menu clear of the taskbar using its actual laid-out size.
                        tray.ContextMenuStrip = menu;
                        menu.Opened += delegate {
                            Point cursor = Cursor.Position;
                            menu.Location = TrayMenuPosition(cursor, Screen.FromPoint(cursor).WorkingArea, menu.Size);
                            Log("Tray right-click menu opened.");
                        };
                        bool chargeProbeStarted = false;
                        activationTimer.Tick += async delegate {
                            if (activate.WaitOne(0)) window.BringForward();
                            if (chargeProbeStarted) return;
                            chargeProbeStarted = true;
                            await window.CheckChargeLimit();
                        };
                        tray.Visible = true;
                        activationTimer.Start();
                        if (Array.IndexOf(args, "--minimized") >= 0) {
                            using (var context = new ApplicationContext()) {
                                window.FormClosed += delegate { context.ExitThread(); };
                                Application.Run(context);
                            }
                        } else {
                            Application.Run(window);
                        }
                        tray.Visible = false;
                    }
                    Log("Window closed.");
                } finally { mutex.ReleaseMutex(); }
            }
            return 0;
        } catch (Exception e) { ShowError(e); return 1; }
    }
    static void ShowError(Exception e) {
        Log(e.ToString());
        MessageBox.Show("Laptop Power encountered an error:\n\n"+e.Message+"\n\nDetails: "+Path.Combine(Folder,"app.log"),"Laptop Power",MessageBoxButtons.OK,MessageBoxIcon.Error);
    }
    internal static async Task<string> RunBackend(string arguments) {
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(shell);
        start.Arguments = "-NoProfile -ExecutionPolicy Bypass -NonInteractive -File \"" + Path.Combine(Folder,"PowerBackend.ps1") + "\" " + arguments;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (var process = new Process { StartInfo=start }) {
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await Task.Run(delegate { process.WaitForExit(); });
            string text = (await output).Trim();
            string error = (await errors).Trim();
            Log(arguments+" -> "+process.ExitCode+"\n"+text+"\n"+error);
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Length > 0 ? error : text);
            return text;
        }
    }
}
