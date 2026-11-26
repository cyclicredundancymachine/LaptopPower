using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class PowerWindow : Form {
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);
    readonly Button[] modes=new Button[3];
    readonly Label[] descriptions=new Label[3];
    readonly Button restore,verify,settings;
    readonly Label summary;
    readonly Label heading;
    readonly Label liveGpu;
    readonly Button toggleGpu;
    bool gpuIsOn;
    readonly Button toggleChargeLimit;
    readonly Label chargeLimitStatus;
    readonly DataGridView results;
    readonly TextBox detail;
    PowerConfig config;
    internal PowerWatch PowerMonitor { get; set; }
    bool busy;
    bool editing;
    internal bool IsBusy { get { return busy || editing; } }
    internal string ModeName(int index) { return index < 0 ? "Windows power plan" : config.modes[index].name; }
    internal int GpuPreference(int mode, int supply) {
        if (mode < 0) return 0;
        var profile = config.modes[mode];
        switch (supply) {
            case 1: return profile.nvidiaBarrel;
            case 2: return profile.nvidiaUsb;
            case 3: return profile.nvidiaBattery;
            default: return 0;
        }
    }
    internal Task ApplyMode(int mode) {
        if (IsBusy) return Task.FromResult(0);
        return Execute("-Action Apply -ModeIndex " + mode);
    }
    internal bool ChargeLimitAvailable { get; private set; }
    internal bool ChargeLimitBusy { get; private set; }
    internal int RequestedChargeLimit { get; private set; }
    bool exiting;
    public PowerWindow() {
        Text="Laptop Power";ClientSize=new Size(950,800);MinimumSize=new Size(790,720);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(241,246,250);ForeColor=Color.FromArgb(23,42,58);
        Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(24)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,184));root.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,49));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,68));root.RowStyles.Add(new RowStyle(SizeType.Absolute,43));root.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        heading=new Label {Text="Laptop Power",Dock=DockStyle.Fill,Font=new Font("Segoe UI",23,FontStyle.Bold),AutoEllipsis=true};
        root.Controls.Add(heading,0,0);
        liveGpu=new Label {Text="NVIDIA GPU right now: checking...",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            Font=new Font("Segoe UI",11,FontStyle.Bold),ForeColor=Color.FromArgb(145,91,0),AutoEllipsis=true};
        var gpuControls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        gpuControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gpuControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toggleGpu = ActionButton("Turn NVIDIA on", 155);
        toggleGpu.Enabled = false;
        toggleGpu.Click += async delegate {
            if (PowerMonitor == null || IsBusy) return;
            string result = await PowerMonitor.OverrideGpu(!gpuIsOn);
            if (result != null) summary.Text = result;
        };
        gpuControls.Controls.Add(liveGpu, 0, 0);
        gpuControls.Controls.Add(toggleGpu, 1, 0);
        root.Controls.Add(gpuControls,0,1);
        root.RowStyles[8].Height = 44;
        var chargeControls = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        toggleChargeLimit = ActionButton("Enable 80% limit", 165);
        chargeLimitStatus = new Label { Text = "Checking ASUS charge control…", AutoSize = true, Margin = new Padding(5, 10, 0, 0) };
        toggleChargeLimit.Click += async delegate { await SetChargeLimit(RequestedChargeLimit == 80 ? 100 : 80); };
        toggleChargeLimit.Enabled = false;
        chargeControls.Controls.Add(toggleChargeLimit);
        chargeControls.Controls.Add(chargeLimitStatus);
        root.Controls.Add(chargeControls, 0, 8);
        var cards=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0)};
        cards.RowStyles.Add(new RowStyle(SizeType.Absolute,48));cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Color[] colors={Color.FromArgb(13,106,83),Color.FromArgb(31,78,132),Color.FromArgb(123,60,34)};
        for(int i=0;i<3;i++) {
            int selected=i;cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));
            var button=new Button {Dock=DockStyle.Fill,BackColor=colors[i],ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Font=new Font("Segoe UI",11,FontStyle.Bold),Margin=new Padding(3,2,3,2)};
            button.FlatAppearance.BorderSize=0;button.Click+=async delegate {await ApplyMode(selected);};
            modes[i]=button;cards.Controls.Add(button,i,0);
            descriptions[i]=new Label {Dock=DockStyle.Fill,Font=new Font("Segoe UI",9),BackColor=Color.White,Padding=new Padding(10,6,5,0),Margin=new Padding(3,0,3,8)};cards.Controls.Add(descriptions[i],i,1);
        }
        root.Controls.Add(cards,0,2);
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=new Padding(0),WrapContents=false};
        settings=ActionButton("Mode settings...",160);verify=ActionButton("Verify & fix",205);restore=ActionButton("Restore saved settings",195);
        settings.Click+=async delegate {
            bool saved=false;
            editing=true;
            try {
                using(var editor=new SettingsWindow(PowerConfig.Load(AppLauncher.ConfigPath))) {
                    saved=editor.ShowDialog(this)==DialogResult.OK;
                }
            }catch(Exception e){MessageBox.Show(this,e.Message,"Settings",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally {editing=false;}
            if(!saved)return;
            ReloadConfig();
            string activeName;
            int active=PowerMonitor==null?-1:PowerMonitor.ActiveMode(out activeName);
            if(active>=0)await ApplyMode(active);
            else {
                results.Rows.Clear();
                detail.Text="Select a Laptop Power mode to apply the saved settings.";
                summary.Text="Configuration saved. Select a mode to apply it.";
                UpdateGpuRows();
            }
        };
        verify.Click+=async delegate {
            if(PowerMonitor==null)return;
            string active;
            int target=PowerMonitor.ActiveMode(out active);
            if(target<0){summary.Text="Select Endurance, Balanced, or Boost before using Verify & fix.";return;}
            await Execute("-Action Verify -ModeIndex "+target,true);
        };
        restore.Click+=async delegate {await Execute("-Action Restore");};
        var help=ActionButton("Help",70);
        help.Click+=delegate {AppLauncher.OpenReadme();};
        actions.Controls.Add(settings);actions.Controls.Add(verify);actions.Controls.Add(restore);actions.Controls.Add(help);root.Controls.Add(actions,0,3);
        summary=new Label {Text="Ready. Choose a mode, or verify and fix the active mode's saved settings.",Dock=DockStyle.Fill,BackColor=Color.FromArgb(224,240,239),Padding=new Padding(8,3,8,0),Margin=new Padding(3,3,3,3)};root.Controls.Add(summary,0,4);
        results=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,GridColor=Color.FromArgb(220,230,239),CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,BorderStyle=BorderStyle.None,AutoGenerateColumns=false};
        results.Columns.Add(new DataGridViewTextBoxColumn {Name="setting",HeaderText="Setting",FillWeight=33});
        results.Columns.Add(new DataGridViewTextBoxColumn {Name="requested",HeaderText="Requested / target",FillWeight=25});
        results.Columns.Add(new DataGridViewTextBoxColumn {Name="actual",HeaderText="Read back",FillWeight=24});
        results.Columns.Add(new DataGridViewTextBoxColumn {Name="status",HeaderText="Result",FillWeight=18});
        results.RowTemplate.Height=29;results.ColumnHeadersHeight=33;results.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(246,249,252);results.DefaultCellStyle.ForeColor=Color.FromArgb(25,30,40);results.DefaultCellStyle.BackColor=Color.White;
        results.DefaultCellStyle.SelectionBackColor=Color.FromArgb(198,218,242);results.DefaultCellStyle.SelectionForeColor=Color.Black;
        results.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;results.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(33,61,82);results.EnableHeadersVisualStyles=false;
        results.SelectionChanged+=delegate {if(results.SelectedRows.Count>0){var item=results.SelectedRows[0].Tag as SettingResult;if(item!=null)detail.Text=item.setting+": "+item.status+Environment.NewLine+item.detail;}};
        root.Controls.Add(results,0,5);
        detail=new TextBox {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.White,ForeColor=Color.FromArgb(37,62,81),Text="Select a result row for details.",Margin=new Padding(3,8,3,3)};root.Controls.Add(detail,0,6);
        root.Controls.Add(new Label {Text="Verified = value read back. Unconfirmed = hardware cannot prove the result.\nCPU percentages are Windows policy values, not measured speed or CPU usage.",Dock=DockStyle.Fill,Font=new Font("Segoe UI",9),Margin=new Padding(3,6,3,0)},0,7);
        Controls.Add(root);ReloadConfig();
        string lastReport=Path.Combine(AppLauncher.Folder,"last-report.json");
        if(File.Exists(lastReport)) {
            try {ShowReport(new JavaScriptSerializer().Deserialize<OperationReport>(File.ReadAllText(lastReport)));summary.Text="Last report (Verify & fix to refresh): "+summary.Text;}catch(Exception e){AppLauncher.Log("Previous report unavailable: "+e.Message);}
        }
        FormClosing+=delegate(object sender,FormClosingEventArgs e){
            if(e.CloseReason!=CloseReason.UserClosing)return;
            if(exiting && !busy)return;
            e.Cancel=true;
            if(exiting)summary.Text="Please wait for the current settings operation to finish.";
            else Hide();
        };
        Shown+=delegate {AppLauncher.Log("Version 3 window shown.");Activate();};
    }
    internal async Task CheckChargeLimit() {
        try {
            ChargeLimitAvailable = await Task.Run(() => {
                using (var asus = new LaptopAsus()) return asus.CanSetChargeLimit();
            });
        } catch (Exception ex) { AppLauncher.Log("Charge limit probe: " + ex.Message); }
        if (IsDisposed) return;
        toggleChargeLimit.Enabled = ChargeLimitAvailable;
        chargeLimitStatus.Text = ChargeLimitAvailable ? "Current charge limit unavailable to read" : "ASUS charge control unavailable";
    }

    internal async Task<bool> SetChargeLimit(int percent) {
        if (!ChargeLimitAvailable || ChargeLimitBusy) return false;
        ChargeLimitBusy = true;
        toggleChargeLimit.Enabled = false;
        chargeLimitStatus.Text = "Applying " + percent + "%…";
        try {
            await Task.Run(() => {
                using (var asus = new LaptopAsus()) asus.SetChargeLimit(percent);
            });
            RequestedChargeLimit = percent;
            chargeLimitStatus.Text = "ASUS accepted " + percent + "%";
            AppLauncher.Log(chargeLimitStatus.Text + " charge limit; threshold readback unavailable.");
            return true;
        } catch (Exception ex) {
            RequestedChargeLimit = 0;
            chargeLimitStatus.Text = "Charge limit could not be set";
            summary.Text = ex.Message;
            AppLauncher.Log("Charge limit request: " + ex.Message);
            return false;
        } finally {
            ChargeLimitBusy = false;
            toggleChargeLimit.Text = RequestedChargeLimit == 80 ? "Disable 80% limit" : "Enable 80% limit";
            toggleChargeLimit.Enabled = ChargeLimitAvailable;
        }
    }
    internal void UpdateActiveMode(string mode) {
        string title="Laptop Power - "+mode;
        if(Text!=title){Text=title;heading.Text=title;}
    }

    internal void UpdateGpuState(string state, bool switching, string failure) {
        string value=switching?"Switching...":state=="On"?"On (verified)":state;
        string label="NVIDIA GPU right now: "+value;
        if(!switching && failure!=null && state!="On" && state!="Off")label+=" - check results below";
        liveGpu.Text=label;
        gpuIsOn = state == "On";
        toggleGpu.Text = switching ? "Switching NVIDIA…" : gpuIsOn ? "Turn NVIDIA off" : "Turn NVIDIA on";
        toggleGpu.Enabled = !switching && !IsBusy && state != "Unavailable";
        liveGpu.ForeColor=switching?Color.FromArgb(145,91,0):state=="On"?Color.FromArgb(31,78,132):
            state=="Off"?Color.FromArgb(13,106,83):Color.FromArgb(175,35,35);
    }

    internal void UpdateGpuRows() {
        if(PowerMonitor==null)return;
        var item=PowerMonitor.CurrentGpuRow();
        var existing=results.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => IsGpuRow(row.Tag as SettingResult));
        if(existing==null){results.Rows.Insert(0,item.setting,item.requested,item.actual,item.status);existing=results.Rows[0];}
            existing.Cells["setting"].Value=item.setting;
            existing.Cells["requested"].Value=item.requested;
            existing.Cells["actual"].Value=item.actual;
            existing.Cells["status"].Value=item.status;
            existing.Tag=item;
            foreach(DataGridViewCell cell in existing.Cells)cell.ToolTipText=item.detail;
            bool unknown=item.status=="Source unknown";
            existing.DefaultCellStyle.ForeColor=unknown?Color.DarkRed:Color.Empty;
            existing.DefaultCellStyle.SelectionForeColor=unknown?Color.DarkRed:Color.Empty;
            existing.Cells["status"].Style.ForeColor=unknown?Color.DarkRed:item.status=="Verified"?Color.FromArgb(0,105,60):
                item.status=="Failed" || item.status=="Mismatch"?Color.FromArgb(175,35,35):Color.FromArgb(70,85,105);
            if(existing.Selected)detail.Text=item.setting+": "+item.status+Environment.NewLine+item.detail;
    }
    static bool IsGpuRow(SettingResult row) {
        return row != null && row.setting != null && row.setting.StartsWith("NVIDIA GPU",StringComparison.Ordinal);
    }
    internal void BringForward() {
        if(!Visible)Show();
        if(WindowState==FormWindowState.Minimized){WindowState=FormWindowState.Normal;ShowWindow(Handle,9);}
        // Explicitly show even when the process was launched with STARTF_USESHOWWINDOW/SW_HIDE.
        ShowWindow(Handle,5);
        BringToFront();
        Activate();
        bool foreground=SetForegroundWindow(Handle);
        AppLauncher.Log("Open requested: visible="+Visible+", state="+WindowState+", bounds="+Bounds+", foreground="+foreground);
        foreach(Form owned in OwnedForms)if(owned.Visible)owned.Activate();
    }
    internal void ExitFromTray() {
        if(IsBusy || ChargeLimitBusy){BringForward();summary.Text="Finish the current settings operation before exiting.";return;}
        exiting=true;
        try {Close();} finally {exiting=false;}
    }
    static Button ActionButton(string text,int width) {var button=new Button {Text=text,Width=width,Height=33,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.FromArgb(23,42,58),Margin=new Padding(3,3,7,3)};button.FlatAppearance.BorderColor=Color.FromArgb(182,201,215);return button;}
    string ScreenSummary(PowerValues value) {return value.screenAction=="Set"?value.screenBrightness+"%":value.screenAction.ToLowerInvariant();}
    static string NvidiaLabel(int setting) {
        switch (setting) {
            case 1: return "On";
            case 2: return "Off";
            default: return "Unchanged";
        }
    }
    static string NvidiaSummary(PowerModeConfig mode) {
        string battery = NvidiaLabel(mode.nvidiaBattery);
        string usb = NvidiaLabel(mode.nvidiaUsb);
        string charger = NvidiaLabel(mode.nvidiaBarrel);
        return "NVIDIA\n   Battery: " + battery + "\n   USB-C: " + usb + "\n   ASUS charger: " + charger;
    }
    void ReloadConfig() {
        config=PowerConfig.Load(AppLauncher.ConfigPath);
        for(int i=0;i<3;i++) {
            var mode=config.modes[i];modes[i].Text=mode.name;
            string purpose=i==0?"Save battery power":i==1?"Everyday performance":"Maximum performance";
            descriptions[i].Text=NvidiaSummary(mode)+"\nCPU: "+mode.pluggedIn.cpuMaximum+"% mains / "+mode.battery.cpuMaximum+"% battery\nScreen: "+ScreenSummary(mode.pluggedIn)+" / "+ScreenSummary(mode.battery)+"\n"+purpose;
        }
    }
    static int ResultPriority(SettingResult row) {
        string name=row.setting;
        if(name.StartsWith("NVIDIA GPU",StringComparison.Ordinal))return row.status=="Not active"?1:0;
        if(name.StartsWith("CPU maximum",StringComparison.Ordinal))return 2;
        if(name.StartsWith("CPU boost",StringComparison.Ordinal))return 3;
        if(name.StartsWith("Screen brightness",StringComparison.Ordinal))return 4;
        if(name.StartsWith("CPU efficiency",StringComparison.Ordinal))return 5;
        if(name.StartsWith("CPU minimum",StringComparison.Ordinal))return 6;
        if(name.StartsWith("Turn screen",StringComparison.Ordinal))return 7;
        if(name.StartsWith("Sleep",StringComparison.Ordinal))return 8;
        return 9;
    }
    void ShowReport(OperationReport report) {
        if(report==null || report.rows==null)throw new InvalidDataException("The settings service returned an empty report.");
        report.rows=report.rows.Where(row=>row.setting!="Keyboard brightness" && row.setting!="ASUS thermal preset").OrderBy(ResultPriority).ToArray();
        // Older saved reports contain one NVIDIA row per source. The live row replaces them.
        var visibleRows = report.rows.Where(row => !IsGpuRow(row));
        summary.Text=report.mode+" | "+report.time+Environment.NewLine+String.Join(", ",report.rows.GroupBy(row=>row.status).Select(group=>group.Count()+" "+group.Key.ToLowerInvariant()));
        results.Rows.Clear();
        foreach(var item in visibleRows) {
            int index=results.Rows.Add(item.setting,item.requested,item.actual,item.status);var row=results.Rows[index];row.Tag=item;
            foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=item.detail;
            var status=row.Cells["status"];
            if(item.status=="Verified")status.Style.ForeColor=Color.FromArgb(0,105,60);
            else if(item.status=="Failed" || item.status=="Mismatch")status.Style.ForeColor=Color.FromArgb(175,35,35);
            else if(item.status=="Unconfirmed" || item.status=="Unsupported")status.Style.ForeColor=Color.FromArgb(145,91,0);
            else status.Style.ForeColor=Color.FromArgb(70,85,105);
        }
        if(results.Rows.Count>0){results.ClearSelection();results.Rows[0].Selected=true;}
        UpdateGpuRows();
    }
    async Task Execute(string command,bool repair=false) {
        if(busy)return;busy=true;
        foreach(var button in modes)button.Enabled=false;
        settings.Enabled=verify.Enabled=restore.Enabled=false;UseWaitCursor=true;
        summary.Text=repair?"Checking the saved profile and repairing mismatches. Please wait...":"Applying settings. Please wait...";
        try {
            ReloadConfig();
            if(PowerMonitor!=null)await PowerMonitor.WaitForGpu();
            string json=await AppLauncher.RunBackend(command);
            var report=new JavaScriptSerializer().Deserialize<OperationReport>(json);
            if(repair && report.rows.Any(row=>row.status=="Mismatch")) {
                json=await AppLauncher.RunBackend("-Action Apply -ModeIndex "+report.targetModeIndex);
                report=new JavaScriptSerializer().Deserialize<OperationReport>(json);
            }
            if(PowerMonitor!=null && report.targetModeIndex>=0) {
                await PowerMonitor.VerifyGpu(report.targetModeIndex,repair || command.Contains("-Action Apply"));
            }
            if(PowerMonitor!=null) {
                report.rows=report.rows.Concat(new[]{PowerMonitor.CurrentGpuRow()}).ToArray();
            }
            report.summary=(repair?"Verify & fix results. ":"Results. ")+String.Join(", ",report.rows.GroupBy(row=>row.status).Select(group=>group.Count()+" "+group.Key.ToLowerInvariant()));
            File.WriteAllText(Path.Combine(AppLauncher.Folder,"last-report.json"),new JavaScriptSerializer().Serialize(report));
            ShowReport(report);
        }catch(Exception e){
            results.Rows.Clear();summary.Text="The operation could not finish.";detail.Text=e.Message;
            results.Rows.Add("Operation","Complete request","Error","Failed");
            if(e.Message.IndexOf("access",StringComparison.OrdinalIgnoreCase)>=0)detail.AppendText("\r\nIf Windows reports access denied, close the app and use Run as administrator.");
        }finally {busy=false;foreach(var button in modes)button.Enabled=true;settings.Enabled=verify.Enabled=restore.Enabled=true;UseWaitCursor=false;}
    }
}
