using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class PowerWatch : IDisposable {
    [StructLayout(LayoutKind.Sequential)]
    struct BatteryState {
        public byte AcOnline, Present, Charging, Discharging;
        public uint Reserved, MaxCapacity, RemainingCapacity;
        public int Rate;
        public uint EstimatedTime, Alert1, Alert2;
    }
    [DllImport("powrprof.dll")]
    static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, out BatteryState state, uint size);
    [DllImport("powrprof.dll")]
    static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")]
    static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr subgroup, IntPtr setting,
        [Out] byte[] buffer, ref uint size);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    enum Source { Unknown, Battery, External, ExternalWithDrain }
    enum PromptKind { Battery, BatteryNotice, Usb, UsbNotice, Asus, AsusNotice, Drain }
    readonly PowerWindow window;
    readonly NotifyIcon tray;
    internal string StatusText { get; private set; }
    Icon chargeIcon;
    int? iconPercent;
    int iconSource = -1;
    Task<Tuple<int,string>> chargerProbe;
    string gpuStatus = "Unconfirmed";
    string gpuContext, attemptedPolicy, overrideContext;
    bool gpuBusy;
    internal string GpuStatus { get { return gpuStatus; } }
    internal bool CanOverrideGpu { get { return !gpuBusy && !window.IsBusy && gpuStatus != "Unavailable"; } }
    Task gpuOperation = Task.FromResult(0);
    string gpuFailure;
    bool disposed;
    int chargerType;
    Guid lastExternalPlan;
    string externalPlanName;
    DateTime nextProbe;
    readonly Timer timer = new Timer { Interval = 1000 };
    readonly Stopwatch drain = new Stopwatch();
    readonly Stopwatch recovery = new Stopwatch();
    readonly Stopwatch countdown = new Stopwatch();
    bool drainEpisode;
    bool ignoreDrain;
    DateTimeOffset? remindAt;
    Source previous;
    PromptKind promptKind;
    int previousSupply;
    bool usbPromptPending;
    bool asusPromptPending;
    Form prompt;
    Label message;

    public PowerWatch(PowerWindow window, NotifyIcon tray) {
        this.window = window;
        this.tray = tray;
        window.PowerMonitor = this;
        Poll();
        timer.Tick += delegate { Poll(); };
        timer.Start();
    }

    string UpdateChargerStatus(Source source) {
        bool online = source == Source.External || source == Source.ExternalWithDrain;
        if (chargerProbe != null && chargerProbe.IsCompleted) {
            var reading = chargerProbe.Result;
            gpuStatus = reading.Item2;
            chargerType = online ? reading.Item1 : 0;
            chargerProbe = null;
        }
        if (!online) chargerType = 0;
        if (!gpuBusy && chargerProbe == null && DateTime.UtcNow >= nextProbe) {
            nextProbe = DateTime.UtcNow.AddSeconds(3);
            chargerProbe = Task.Run(delegate {
                int charger = 0;
                try {
                    using (var asus = new LaptopAsus()) {
                        if (online) charger = asus.Read(0x12006C) & 0xff;
                        return Tuple.Create(charger,GpuSwitch.Read(asus));
                    }
                } catch (System.ComponentModel.Win32Exception) { return Tuple.Create(charger,"Unavailable"); }
                catch (InvalidOperationException) { return Tuple.Create(charger,"Unconfirmed"); }
            });
        }
        return source == Source.Battery ? "On battery" : chargerType == 1 ? "AC adapter" : chargerType == 2 ? "USB-C power" : online ? "Charger unknown" : "Power unknown";
    }

    void UpdateChargeIcon(int source, int? percent) {
        if (source == iconSource && percent == iconPercent) return;
        Color color = source == 3 ? Color.FromArgb(255,91,91) : source == 2 ? Color.FromArgb(255,203,65) :
            source == 1 ? Color.FromArgb(76,223,133) : Color.Silver;
        var next = AppLauncher.CreatePowerIcon(color,percent.HasValue ? percent.Value.ToString() : "–");
        tray.Icon = next;
        window.Icon = next;
        if (chargeIcon != null) chargeIcon.Dispose();
        chargeIcon = next;
        iconSource = source;
        iconPercent = percent;
    }

    static bool StillOnProfile(int mode, int supply, LaptopAsus asus) {
        var line = SystemInformation.PowerStatus.PowerLineStatus;
        if (supply == 3 ? line != PowerLineStatus.Offline : line != PowerLineStatus.Online || (asus.Read(0x12006C) & 0xff) != supply)
            return false;
        IntPtr scheme;
        if (PowerGetActiveScheme(IntPtr.Zero,out scheme) != 0 || scheme == IntPtr.Zero) return false;
        try { return (Guid)Marshal.PtrToStructure(scheme,typeof(Guid)) == new Guid("b86b6310-905f-4af3-88c8-cb65129d0d0" + (mode+1)); }
        finally { LocalFree(scheme); }
    }

    internal async Task<string> OverrideGpu(bool on) {
        if (gpuBusy || window.IsBusy) return null;
        overrideContext = gpuContext;
        string ignored;
        int mode = ActiveMode(out ignored);
        int source = CurrentSupply();
        gpuOperation = ChangeGpu(on, mode, source);
        await gpuOperation;
        string expected = on ? "On" : "Off";
        if (gpuStatus == expected) return "Verified NVIDIA is " + expected.ToLowerInvariant() + ".";
        return gpuFailure ?? "The NVIDIA request was canceled after the mode or power source changed.";
    }

    int CurrentSupply() {
        var line = SystemInformation.PowerStatus.PowerLineStatus;
        return line == PowerLineStatus.Offline ? 3 : line == PowerLineStatus.Online ? chargerType : 0;
    }

    bool MatchesContext(int mode, int supply, LaptopAsus asus) {
        if (disposed) return false;
        if (mode >= 0) return StillOnProfile(mode,supply,asus);
        string ignored;
        return ActiveMode(out ignored) == mode && (supply == 3 ? SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline :
            SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online && (asus.Read(0x12006C) & 0xff) == supply);
    }

    async Task ChangeGpu(bool on, int mode, int supply) {
        gpuBusy = true;
        gpuStatus = "Switching";
        gpuFailure = null;
        window.UpdateGpuState(gpuStatus, gpuBusy, gpuFailure);
        try {
            var pendingProbe = chargerProbe;
            if (pendingProbe != null) {
                await pendingProbe;
                if (chargerProbe == pendingProbe) chargerProbe = null;
            }
            gpuStatus = await Task.Run(() => GpuSwitch.Retry(
                () => GpuSwitch.Apply(on, asus => MatchesContext(mode,supply,asus)),
                attempt => AppLauncher.Log("NVIDIA attempt " + attempt + " of 3"),
                milliseconds => System.Threading.Thread.Sleep(milliseconds)));
            AppLauncher.Log("NVIDIA GPU verified " + gpuStatus);
        } catch (OperationCanceledException) {
            gpuStatus = "Unconfirmed";
            AppLauncher.Log("NVIDIA retry canceled after mode or source change.");
        } catch (Exception ex) {
            gpuStatus = "Unconfirmed";
            gpuFailure = ex.Message;
            AppLauncher.Log("NVIDIA GPU switch: " + ex.Message);
            if (!disposed) tray.ShowBalloonTip(8000,"Laptop Power",ex.Message,ToolTipIcon.Warning);
        } finally {
            gpuBusy = false;
            nextProbe = DateTime.MinValue;
            if (!disposed) window.UpdateGpuState(gpuStatus, gpuBusy, gpuFailure);
        }
    }

    internal Task WaitForGpu() { return gpuOperation; }

    internal async Task VerifyGpu(int mode, bool repair) {
        await gpuOperation;
        string ignored;
        if (ActiveMode(out ignored) != mode) return;
        int supply = CurrentSupply();
        try {
            var reading = await Task.Run(() => {
                using (var asus = new LaptopAsus()) return Tuple.Create(
                    SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? 3 : asus.Read(0x12006C) & 0xff,
                    GpuSwitch.Read(asus));
            });
            supply = reading.Item1;
            gpuStatus = reading.Item2;
            int target = window.GpuPreference(mode,supply);
            // Verify & fix explicitly returns control to the saved profile for this source.
            if (repair && target != 0 && supply >= 1 && supply <= 3) {
                overrideContext = null;
                attemptedPolicy = mode + ":" + supply + ":" + target;
                if (gpuStatus != (target == 1 ? "On" : "Off")) {
                    gpuOperation = ChangeGpu(target == 1,mode,supply);
                    await gpuOperation;
                } else gpuFailure = null;
            }
        } catch (Exception ex) { gpuStatus = "Unconfirmed"; gpuFailure = ex.Message; }
    }

    internal SettingResult CurrentGpuRow() {
        string[] sources = { "ASUS charger", "USB-C", "battery" };
        int supply = CurrentSupply();
        if (supply < 1 || supply > sources.Length) {
            return new SettingResult { setting = "NVIDIA GPU - power source unknown", requested = "Unavailable",
                actual = gpuStatus, status = "Source unknown",
                detail = "Laptop Power could not identify the current power source or its NVIDIA rule." };
        }
        string title;
        int mode = ActiveMode(out title);
        int target = window.GpuPreference(mode, supply);
        string desired = mode < 0 ? "No Laptop Power rule" : target == 0 ? "Keep current" : target == 1 ? "On" : "Off";
        bool driverError = gpuStatus.StartsWith("Driver error", StringComparison.Ordinal);
        string result = gpuBusy ? "Pending" : driverError || gpuFailure != null ? "Failed" :
            overrideContext == mode + ":" + supply ? "Override" : target == 0 ? "Observed" :
            gpuStatus == desired ? "Verified" : gpuStatus == "On" || gpuStatus == "Off" ? "Mismatch" : "Unconfirmed";
        string detail = driverError ? "Windows reports " + gpuStatus + "." : gpuFailure;
        if (detail == null) detail = result == "Override" ? "A temporary NVIDIA override is active. Verify & fix returns to the saved profile." :
            "GPU state uses ASUS firmware and Windows device presence.";
        return new SettingResult { setting = "NVIDIA GPU - " + sources[supply-1], requested = desired,
            actual = gpuStatus, status = result, detail = detail };
    }

    internal int ActiveMode(out string title) {
        title = "Power plan unavailable";
        IntPtr scheme;
        if (PowerGetActiveScheme(IntPtr.Zero, out scheme) != 0 || scheme == IntPtr.Zero) return -1;
        try {
            Guid id = (Guid)Marshal.PtrToStructure(scheme, typeof(Guid));
            switch (id.ToString()) {
                case "b86b6310-905f-4af3-88c8-cb65129d0d01": return 0;
                case "b86b6310-905f-4af3-88c8-cb65129d0d02": return 1;
                case "b86b6310-905f-4af3-88c8-cb65129d0d03": return 2;
                default:
                    if (externalPlanName == null || lastExternalPlan != id) {
                        uint size = 0;
                        PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
                        externalPlanName = "Other power plan";
                        if (size > 0 && size <= 4096) {
                            var buffer = new byte[size];
                            if (PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0) {
                                string name = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
                                if (!String.IsNullOrWhiteSpace(name)) externalPlanName = name;
                            }
                        }
                        lastExternalPlan = id;
                    }
                    title = externalPlanName;
                    return -1;
            }
        } finally { LocalFree(scheme); }
    }

    Source ReadSource() {
        var line = SystemInformation.PowerStatus.PowerLineStatus;
        BatteryState state;
        bool available = CallNtPowerInformation(5, IntPtr.Zero, 0, out state, (uint)Marshal.SizeOf(typeof(BatteryState))) == 0 && state.Present != 0;
        bool valid = available && state.Rate != int.MinValue;
        bool draining = valid && state.Discharging != 0 && state.Rate <= -2000;
        if (line != PowerLineStatus.Online || !draining) drain.Reset();
        else if (!drain.IsRunning) drain.Start();
        if (line == PowerLineStatus.Online && !draining && drainEpisode) {
            if (!recovery.IsRunning) recovery.Start();
        } else recovery.Reset();
        if (line == PowerLineStatus.Offline || recovery.Elapsed.TotalSeconds >= 30) {
            drainEpisode = false;
            ignoreDrain = false;
            remindAt = null;
        }
        if (drain.Elapsed.TotalSeconds >= 15) drainEpisode = true;
        if (line == PowerLineStatus.Offline) return Source.Battery;
        if (line != PowerLineStatus.Online) return Source.Unknown;
        return drainEpisode ? Source.ExternalWithDrain : Source.External;
    }

    void Poll() {
        var source = ReadSource();
        string supply = UpdateChargerStatus(source);
        string title;
        int mode = ActiveMode(out title);
        if (mode >= 0) title = window.ModeName(mode);
        window.UpdateActiveMode(title);
        int sourceCode = source == Source.Battery ? 3 : chargerType == 1 || chargerType == 2 ? chargerType : 0;
        if (sourceCode != 0) {
            if (sourceCode != previousSupply) {
                AppLauncher.Log("Power source changed: " + previousSupply + " -> " + sourceCode);
                usbPromptPending = sourceCode == 2 && previousSupply != 0;
                asusPromptPending = sourceCode == 1 && previousSupply != 0;
                previousSupply = sourceCode;
            }
            string context = mode + ":" + sourceCode;
            if (context != gpuContext) {
                gpuContext = context;
                overrideContext = null;
                attemptedPolicy = null;
            }
            int preference = window.GpuPreference(mode,sourceCode);
            string policy = context + ":" + preference;
            if (preference != 0 && policy != attemptedPolicy && overrideContext != context && !gpuBusy && !window.IsBusy) {
                attemptedPolicy = policy;
                gpuOperation = ChangeGpu(preference == 1,mode,sourceCode);
            } else if (preference == 0) attemptedPolicy = null;
        }
        var battery = SystemInformation.PowerStatus;
        var chargeStatus = battery.BatteryChargeStatus;
        float fraction = battery.BatteryLifePercent;
        int? percent = chargeStatus != BatteryChargeStatus.Unknown && (chargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && fraction >= 0 && fraction <= 1
            ? (int?)Math.Round(fraction * 100) : null;
        UpdateChargeIcon(sourceCode,percent);
        string charge = percent.HasValue ? "Battery: " + percent.Value + "%" : "Battery: unavailable";
        string details = charge + "\n" + supply + "\nNVIDIA: " + gpuStatus;
        StatusText = title + "\n" + details;
        int titleLimit = Math.Min(32, 62 - details.Length);
        if (title.Length > titleLimit) title = title.Substring(0, titleLimit - 1) + "…";
        string tooltip = title + "\n" + details;
        if (tray.Text != tooltip) tray.Text = tooltip;
        window.UpdateGpuState(gpuStatus,gpuBusy,gpuFailure);
        window.UpdateGpuRows();
        bool justUnplugged = source == Source.Battery && (previous == Source.External || previous == Source.ExternalWithDrain);
        if (source != Source.Unknown) previous = source;
        if (prompt != null && (!MatchesPrompt(promptKind, source) || (IsCountdown(promptKind) && (mode == TargetMode(promptKind) || window.IsBusy)))) ClosePrompt();
        if (justUnplugged) ShowPrompt(mode == 0 || window.IsBusy ? PromptKind.BatteryNotice : PromptKind.Battery, mode);
        if (usbPromptPending && sourceCode == 2 && !window.IsBusy) {
            usbPromptPending = false;
            ClosePrompt();
            ShowPrompt(UsbPromptForMode(mode), mode);
        }
        if (asusPromptPending && sourceCode == 1 && !window.IsBusy) {
            asusPromptPending = false;
            ClosePrompt();
            ShowPrompt(mode == 1 ? PromptKind.AsusNotice : PromptKind.Asus, mode);
        }
        if (prompt == null && source == Source.ExternalWithDrain && drain.Elapsed.TotalSeconds >= 15 &&
            !window.IsBusy && !ignoreDrain && (!remindAt.HasValue || DateTimeOffset.Now >= remindAt.Value)) {
            remindAt = null;
            ShowPrompt(PromptKind.Drain, mode);
        }
        if (prompt == null || !IsCountdown(promptKind)) return;
        int seconds = Math.Max(0, 30 - (int)countdown.Elapsed.TotalSeconds);
        string heading = promptKind == PromptKind.Asus ? "ASUS charger connected." :
            promptKind == PromptKind.Usb ? "USB-C power connected. This charger may supply less power." : "On battery power";
        message.Text = heading + "\nSwitching to " + window.ModeName(TargetMode(promptKind)) + " in " + seconds + " seconds.";
        if (seconds == 0) Apply();
    }

    static bool IsCountdown(PromptKind kind) { return kind == PromptKind.Battery || kind == PromptKind.Usb || kind == PromptKind.Asus; }
    static PromptKind UsbPromptForMode(int mode) { return mode == 0 ? PromptKind.UsbNotice : PromptKind.Usb; }
    static int TargetMode(PromptKind kind) { return kind == PromptKind.Asus || kind == PromptKind.AsusNotice ? 1 : 0; }

    bool MatchesPrompt(PromptKind kind, Source source) {
        switch (kind) {
            case PromptKind.Battery: return source == Source.Battery;
            case PromptKind.BatteryNotice: return source == Source.Battery;
            case PromptKind.Usb:
            case PromptKind.UsbNotice: return chargerType == 2 && (source == Source.External || source == Source.ExternalWithDrain);
            case PromptKind.Asus:
            case PromptKind.AsusNotice: return chargerType == 1 && (source == Source.External || source == Source.ExternalWithDrain);
            default: return source == Source.ExternalWithDrain;
        }
    }

    void ShowPrompt(PromptKind kind, int mode) {
        AppLauncher.Log("Showing power popup: " + kind);
        promptKind = kind;
        bool pluggedIn = kind == PromptKind.Drain;
        if (IsCountdown(kind)) countdown.Restart();
        prompt = new Form {
            Text = "Laptop Power", ClientSize = new Size(pluggedIn ? 600 : 450, 158), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            TopMost = true, BackColor = Color.FromArgb(241,246,250), Font = window.Font
        };
        message = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), ForeColor = Color.FromArgb(23,42,58) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 49, Padding = new Padding(10,3,0,0) };
        var apply = new Button { Text = pluggedIn ? "Switch to " + window.ModeName(0) : "Switch now", AutoSize = true };
        var dismiss = new Button { Text = pluggedIn ? "Ignore" : "Dismiss", AutoSize = true };
        apply.Click += delegate { Apply(); };
        dismiss.Click += delegate { if (pluggedIn) ignoreDrain = true; ClosePrompt(); };
        actions.Controls.Add(dismiss);
        if (pluggedIn) {
            message.Text = "Your laptop is plugged in, but the battery is still draining.\n" +
                (mode == 0 ? "The power-saving mode is already active." : "Switching to " + window.ModeName(0) + " may reduce power use.");
            var minutes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            int[] intervals = { 5, 15, 30, 60 };
            foreach (int interval in intervals) minutes.Items.Add(interval + " min");
            minutes.SelectedIndex = 1;
            var remind = new Button { Text = "Remind me in", AutoSize = true };
            remind.Click += delegate { remindAt = DateTimeOffset.Now.AddMinutes(intervals[minutes.SelectedIndex]); ClosePrompt(); };
            actions.Controls.Add(remind);
            actions.Controls.Add(minutes);
            if (mode != 0) actions.Controls.Add(apply);
        } else {
            var open = new Button { Text = "Open Laptop Power", AutoSize = true };
            open.Click += delegate { ClosePrompt(); window.BringForward(); };
            if (kind == PromptKind.BatteryNotice) {
                message.Text = "External power disconnected. The laptop is now on battery.";
                if (mode == 0) message.Text += "\n" + window.ModeName(0) + " is already active.";
            } else if (kind == PromptKind.UsbNotice) {
                message.Text = "USB-C power connected. This charger may supply less power.\n" + window.ModeName(0) + " is already active.";
            } else if (kind == PromptKind.AsusNotice) {
                message.Text = "ASUS charger connected.\n" + window.ModeName(1) + " is already active.";
            } else actions.Controls.Add(apply);
            actions.Controls.Add(open);
        }
        prompt.Controls.Add(message);
        prompt.Controls.Add(actions);
        if (!pluggedIn) prompt.CancelButton = dismiss;
        prompt.FormClosed += delegate {
            if (pluggedIn && drainEpisode && !ignoreDrain && !remindAt.HasValue)
                remindAt = DateTimeOffset.Now.AddMinutes(15);
            prompt = null;
            countdown.Reset();
        };
        prompt.Show();
    }

    async void Apply() {
        PromptKind kind = promptKind;
        bool applicable = MatchesPrompt(kind, ReadSource()) && !window.IsBusy;
        ClosePrompt();
        if (applicable && (kind == PromptKind.Usb || kind == PromptKind.Asus)) {
            int expectedCharger = kind == PromptKind.Asus ? 1 : 2;
            applicable = await Task.Run(delegate {
                try { using (var asus = new LaptopAsus()) return (asus.Read(0x12006C) & 0xff) == expectedCharger; }
                catch (System.ComponentModel.Win32Exception) { return false; }
                catch (InvalidOperationException) { return false; }
            });
            applicable = applicable && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online && !window.IsBusy;
        }
        if (applicable) await window.ApplyMode(TargetMode(kind));
    }

    void ClosePrompt() { if (prompt != null) prompt.Close(); }
    public void Dispose() {
        disposed = true;
        timer.Dispose();
        ClosePrompt();
        tray.Icon = null;
        if (!window.IsDisposed) window.Icon = null;
        if (chargeIcon != null) chargeIcon.Dispose();
    }
}
