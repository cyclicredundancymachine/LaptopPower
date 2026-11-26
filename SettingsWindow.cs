using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal sealed class ValueEditor {
    readonly NumericUpDown min,max,efficiency,brightness,display,sleep;
    readonly ComboBox boost,screen;
    public ValueEditor(TableLayoutPanel table,int column,PowerValues value) {
        min=Number(table,column,9,0,100,value.cpuMinimum);
        max=Number(table,column,4,1,100,value.cpuMaximum);
        boost=Choice(table,column,5,new [] {"Disabled","Enabled","Aggressive","Efficient enabled","Efficient aggressive","Aggressive at guaranteed","Efficient aggressive at guaranteed"},value.boostMode);
        efficiency=Number(table,column,8,0,100,value.efficiencyPreference);
        screen=Choice(table,column,6,new [] {"Set brightness below","Keep current brightness","Restore pre-app brightness"},Array.IndexOf(new [] {"Set","Keep","Restore"},value.screenAction));
        brightness=Number(table,column,7,1,100,value.screenBrightness);
        display=Number(table,column,10,-1,1440,value.displayTimeoutMinutes);
        sleep=Number(table,column,11,-1,1440,value.sleepTimeoutMinutes);
        brightness.Enabled=screen.SelectedIndex==0;
        screen.SelectedIndexChanged+=delegate {brightness.Enabled=screen.SelectedIndex==0;};
    }
    internal static NumericUpDown Number(TableLayoutPanel table,int column,int row,int low,int high,int value) {
        var control=new NumericUpDown {Minimum=low,Maximum=high,Value=value,Dock=DockStyle.Fill,Margin=new Padding(5,4,5,4)};
        table.Controls.Add(control,column,row);return control;
    }
    internal static ComboBox Choice(TableLayoutPanel table,int column,int row,string[] names,int index) {
        var control=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,Margin=new Padding(5,4,5,4)};
        control.Items.AddRange(names);control.SelectedIndex=index;table.Controls.Add(control,column,row);return control;
    }
    public PowerValues Read() {
        return new PowerValues {cpuMinimum=(int)min.Value,cpuMaximum=(int)max.Value,boostMode=boost.SelectedIndex,efficiencyPreference=(int)efficiency.Value,screenAction=new [] {"Set","Keep","Restore"}[screen.SelectedIndex],screenBrightness=(int)brightness.Value,displayTimeoutMinutes=(int)display.Value,sleepTimeoutMinutes=(int)sleep.Value};
    }
}

internal sealed class ModeEditor {
    readonly TextBox name;
    readonly ValueEditor mains,battery;
    readonly ComboBox gpuBattery,gpuUsb,gpuBarrel;
    readonly string id;
    public ModeEditor(TabPage page,PowerModeConfig mode) {
        id=mode.id;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=15,Padding=new Padding(12),AutoScroll=true};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));
        for(int i=0;i<13;i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        string[] labels={"Setting","NVIDIA GPU on battery","NVIDIA GPU on USB-C","NVIDIA GPU on ASUS barrel charger","CPU maximum (%)","CPU boost behavior","Screen brightness behavior","Screen brightness (%)","Efficiency preference (0 fast / 100 save)","CPU minimum (%)","Turn screen off (minutes)","Sleep (minutes)","Mode name"};
        for(int i=0;i<labels.Length;i++)layout.Controls.Add(new Label {Text=labels[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,i);
        layout.Controls.Add(new Label {Text="Plugged in",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI",10,FontStyle.Bold)},1,0);
        layout.Controls.Add(new Label {Text="On battery",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI",10,FontStyle.Bold)},2,0);
        mains=new ValueEditor(layout,1,mode.pluggedIn);battery=new ValueEditor(layout,2,mode.battery);
        name=new TextBox {Text=mode.name,MaxLength=45,Dock=DockStyle.Fill,Margin=new Padding(5,4,5,4)};layout.Controls.Add(name,1,12);layout.SetColumnSpan(name,2);
        var help=new Label {Text="Timeouts: -1 keeps the current setting; 0 means never. Screen 'Restore' uses brightness saved before the first mode change.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(70,70,70)};
        string[] gpuChoices={"Keep current","On","Off"};
        gpuBattery=ValueEditor.Choice(layout,1,1,gpuChoices,mode.nvidiaBattery);layout.SetColumnSpan(gpuBattery,2);
        gpuUsb=ValueEditor.Choice(layout,1,2,gpuChoices,mode.nvidiaUsb);layout.SetColumnSpan(gpuUsb,2);
        gpuBarrel=ValueEditor.Choice(layout,1,3,gpuChoices,mode.nvidiaBarrel);layout.SetColumnSpan(gpuBarrel,2);
        layout.Controls.Add(help,0,13);layout.SetColumnSpan(help,3);
        var limits=new Label {Text="NVIDIA choices follow the active Laptop Power mode and power source. Turning it off can interrupt GPU apps. Keep current leaves it alone.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(100,70,15)};
        layout.Controls.Add(limits,0,14);layout.SetColumnSpan(limits,3);
        page.Controls.Add(layout);
    }
    public PowerModeConfig Read() {return new PowerModeConfig {id=id,name=name.Text.Trim(),pluggedIn=mains.Read(),battery=battery.Read(),keyboardBrightness=-1,thermalMode=-1,nvidiaBattery=gpuBattery.SelectedIndex,nvidiaUsb=gpuUsb.SelectedIndex,nvidiaBarrel=gpuBarrel.SelectedIndex};}
}

internal sealed class SettingsWindow : Form {
    readonly ModeEditor[] editors=new ModeEditor[3];
    public SettingsWindow(PowerConfig config) {
        Text="Laptop Power | Mode settings";BackColor=Color.FromArgb(241,246,250);ForeColor=Color.FromArgb(23,42,58);Font=new Font("Segoe UI",9);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(850,625);MinimumSize=new Size(700,550);StartPosition=FormStartPosition.CenterParent;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(16)};
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,36));root.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        var tabs=new TabControl {Dock=DockStyle.Fill};
        for(int i=0;i<3;i++){var page=new TabPage(config.modes[i].name){BackColor=Color.White};editors[i]=new ModeEditor(page,config.modes[i]);tabs.TabPages.Add(page);}
        root.Controls.Add(tabs,0,0);
        var path=new TextBox {Text=AppLauncher.ConfigPath,ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=BackColor};root.Controls.Add(path,0,1);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button {Text="Save and apply",Width=140,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(14,105,86),ForeColor=Color.White};save.Click+=delegate {
            try {
                var updated=new PowerConfig {version=1,modes=new [] {editors[0].Read(),editors[1].Read(),editors[2].Read()}};
                PowerConfig.Save(AppLauncher.ConfigPath,updated);DialogResult=DialogResult.OK;Close();
            }catch(Exception e){MessageBox.Show(this,e.Message,"Check your settings",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        };
        var cancel=new Button {Text="Cancel",Width=100,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.White,DialogResult=DialogResult.Cancel};
        buttons.Controls.Add(save);buttons.Controls.Add(cancel);root.Controls.Add(buttons,0,2);Controls.Add(root);
        AcceptButton=save;CancelButton=cancel;
    }
}
