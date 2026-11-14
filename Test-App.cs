using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
internal static class AppChecks {
    static void Require(bool pass,string message) { if(!pass)throw new Exception(message); }
    [STAThread] static void Main() {
        var menuPosition=AppLauncher.TrayMenuPosition(new System.Drawing.Point(1900,1070),
            new System.Drawing.Rectangle(0,0,1920,1080),new System.Drawing.Size(280,180));
        Require(menuPosition.Y+180<=1048 && menuPosition.X+280<=1920,"Tray menu extends into taskbar area");
        int opens=0;
        using(var tray=new NotifyIcon())
        using(var clicks=new TrayClicks(tray,()=>"Test status",()=>opens++)) {
            var up=typeof(TrayClicks).GetMethod("MouseUp",BindingFlags.Instance|BindingFlags.NonPublic);
            Action<MouseButtons> click=button=>up.Invoke(clicks,new object[]{tray,new MouseEventArgs(button,1,0,0,0)});
            click(MouseButtons.Left);
            Require(opens==0,"Single click opened the app");
            click(MouseButtons.Left);
            Require(opens==1,"Two completed clicks did not open the app");
            click(MouseButtons.Left);
            click(MouseButtons.Right);
            click(MouseButtons.Left);
            Require(opens==1,"Right click did not cancel pending double click");
            click(MouseButtons.Left);
            Require(opens==2,"Second double click did not open the app");
            click(MouseButtons.Left);
            var native=typeof(TrayClicks).GetMethod("NativeDoubleClick",BindingFlags.Instance|BindingFlags.NonPublic);
            native.Invoke(clicks,new object[]{tray,new MouseEventArgs(MouseButtons.Left,2,0,0,0)});
            click(MouseButtons.Left);
            Require(opens==3,"Native double click and trailing mouse-up must open exactly once");
        }
        Console.WriteLine("PASS: tray single click, double click, and right-click cancellation.");
        int attempts=0,delays=0;
        string outcome=GpuSwitch.Retry(
            delegate { attempts++; if(attempts<3)throw new InvalidOperationException("Transient refusal");return "On"; },
            delegate(int number){},delegate(int milliseconds){delays++;});
        Require(outcome=="On" && attempts==3 && delays==2,"Retry success path failed");
        attempts=0;delays=0;
        try {
            GpuSwitch.Retry(delegate {attempts++;throw new InvalidOperationException("Refused");},
                delegate(int number){},delegate(int milliseconds){delays++;});
            throw new Exception("Retry did not fail");
        } catch(InvalidOperationException ex) {
            Require(attempts==3 && delays==2 && ex.Message.Contains("3 attempts"),"Retry limit failed");
        }
        attempts=0;
        try {
            GpuSwitch.Retry(delegate {attempts++;throw new OperationCanceledException();},
                delegate(int number){},delegate(int milliseconds){throw new Exception("Cancellation delayed");});
            throw new Exception("Cancellation was swallowed");
        } catch(OperationCanceledException) {Require(attempts==1,"Cancellation retried");}
        attempts=0;
        try {
            GpuSwitch.Retry(delegate {attempts++;throw new GpuSwitch.BlockedException("Display in use");},
                delegate(int number){},delegate(int milliseconds){throw new Exception("Blocked operation delayed");});
            throw new Exception("Blocked operation was swallowed");
        } catch(GpuSwitch.BlockedException) {Require(attempts==1,"Blocked operation retried");}
        Console.WriteLine("PASS: three-attempt retry, success, cancellation, and blocked-operation paths.");

        var config=PowerConfig.Defaults();
        config.modes[0].nvidiaBattery=2;config.modes[0].nvidiaUsb=1;config.modes[0].nvidiaBarrel=0;
        config.modes[0].battery.cpuMaximum=37;
        using(var page=new TabPage()) {
            var editor=new ModeEditor(page,config.modes[0]);
            var restored=editor.Read();
            Require(restored.nvidiaBattery==2 && restored.nvidiaUsb==1 && restored.nvidiaBarrel==0 && restored.battery.cpuMaximum==37,
                "Reordered mode editor lost saved values");
            var table=(TableLayoutPanel)page.Controls[0];
            Require(table.GetControlFromPosition(0,1).Text=="NVIDIA GPU on battery","NVIDIA is not first");
            Require(!table.Controls.Cast<Control>().Any(c=>c.Text.Contains("Keyboard brightness")||c.Text.Contains("ASUS thermal preset")),
                "Removed controls still present");
        }
        var rank=typeof(PowerWindow).GetMethod("ResultPriority",BindingFlags.Static|BindingFlags.NonPublic);
        Func<string,int> priority=name=>(int)rank.Invoke(null,new object[]{new SettingResult{setting=name,status="Verified"}});
        Require(priority("NVIDIA GPU - battery")<priority("CPU maximum - battery") &&
            priority("CPU maximum - battery")<priority("Screen brightness - live"),"Result ordering failed");
        Console.WriteLine("PASS: NVIDIA-first editor and report order; removed controls absent; existing values preserved.");
        Type watcher=typeof(PowerWatch);
        Type kind=watcher.GetNestedType("PromptKind",BindingFlags.NonPublic);
        var target=watcher.GetMethod("TargetMode",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(string name in new[]{"Battery","Usb","Drain"})
            Require((int)target.Invoke(null,new[]{Enum.Parse(kind,name)})==0,"Endurance prompt target changed");
        Require((int)target.Invoke(null,new[]{Enum.Parse(kind,"Asus")})==1,"ASUS prompt must select Balanced");
        Console.WriteLine("PASS: ASUS charger prompt selects Balanced; battery and USB-C select Endurance.");
    }
}
