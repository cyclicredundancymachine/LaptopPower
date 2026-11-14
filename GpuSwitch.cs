using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class GpuSwitch {
    internal sealed class BlockedException : InvalidOperationException {
        internal BlockedException(string message) : base(message) { }
    }

    internal static string Retry(Func<string> attempt, Action<int> progress, Action<int> delay) {
        Exception last = null;
        for (int number = 1; number <= 3; number++) {
            progress(number);
            try { return attempt(); }
            catch (BlockedException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException ex) { last = ex; }
            catch (Win32Exception ex) { last = ex; }
            if (number < 3) delay(2000);
        }
        throw new InvalidOperationException("NVIDIA could not be confirmed after 3 attempts. " + last.Message, last);
    }
    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfo {
        public int Size;
        public Guid ClassGuid;
        public uint Instance;
        public IntPtr Reserved;
    }
    [DllImport("setupapi.dll", SetLastError=true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid display, IntPtr enumerator, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr list, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool SetupDiGetDeviceInstanceId(IntPtr list, ref DeviceInfo info, StringBuilder id, int capacity, IntPtr required);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("cfgmgr32.dll")]
    static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint instance, uint flags);

    // 0 absent, 1 present and started, -1 present but not ready.
    static int NvidiaDevice(out uint problemCode) {
        problemCode = 0;
        Guid display = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
        IntPtr list = SetupDiGetClassDevs(ref display, IntPtr.Zero, IntPtr.Zero, 2);
        if (list == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            for (uint i=0; ; i++) {
                var info = new DeviceInfo { Size=Marshal.SizeOf(typeof(DeviceInfo)) };
                if (!SetupDiEnumDeviceInfo(list,i,ref info)) {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) return 0;
                    throw new Win32Exception(error);
                }
                var id = new StringBuilder(256);
                if (!SetupDiGetDeviceInstanceId(list,ref info,id,id.Capacity,IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!id.ToString().StartsWith(@"PCI\VEN_10DE&",StringComparison.OrdinalIgnoreCase)) continue;
                uint status, problem;
                uint result = CM_Get_DevNode_Status(out status,out problem,info.Instance,0);
                problemCode = problem;
                return result==0 && (status & 8)!=0 && problem==0 ? 1 : -1;
            }
        } finally { SetupDiDestroyDeviceInfoList(list); }
    }

    internal static string Read(LaptopAsus asus) {
        int eco = asus.Read(LaptopAsus.GpuEco);
        if (eco != 0 && eco != 1) return "Unavailable";
        uint problem;
        int device = NvidiaDevice(out problem);
        if (device < 0 && problem != 0) return "Driver error " + problem;
        if (eco == 1 && device == 0) return "Off";
        if (eco == 0 && device == 1) return "On";
        return "Unconfirmed";
    }

    internal static string Apply(bool on, Func<LaptopAsus,bool> stillWanted) {
        using (var asus=new LaptopAsus()) {
            int target = on ? 0 : 1;
            int eco = asus.Read(LaptopAsus.GpuEco);
            if (eco != 0 && eco != 1) throw new BlockedException("ASUS GPU switching is unavailable.");
            if (!stillWanted(asus)) throw new OperationCanceledException("The power source or mode changed.");
            if (eco != target) {
                if (!on) {
                    int displays = asus.Read(0x00090030);
                    if (asus.Read(0x00090016) != 1 || displays < 0 || ((displays >> 4) & displays & 15) != 0 ||
                        Screen.AllScreens.Length != 1 || NvidiaOwnsDesktop())
                        throw new BlockedException("Switching NVIDIA off requires hybrid graphics and a display that does not use NVIDIA.");
                }
                asus.Write(LaptopAsus.GpuEco,target);
            }
            string expected = on ? "On" : "Off";
            string observed = "Unconfirmed";
            var wait=Stopwatch.StartNew();
            do {
                if (!stillWanted(asus)) throw new OperationCanceledException("The power source or mode changed.");
                observed = Read(asus);
                if (observed == expected) return expected;
                Thread.Sleep(400);
            } while (wait.Elapsed.TotalSeconds < 10);
            throw new InvalidOperationException("Windows did not confirm NVIDIA " + expected.ToLowerInvariant() + ". Current state: " + observed + ".");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct DisplayDevice {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string device,uint index,ref DisplayDevice result,uint flags);

    static bool NvidiaOwnsDesktop() {
        bool integratedDesktop=false;
        for (uint i=0; ; i++) {
            var display = new DisplayDevice { Size=Marshal.SizeOf(typeof(DisplayDevice)) };
            if (!EnumDisplayDevices(null,i,ref display,0)) break;
            if ((display.Flags & 1)==0) continue;
            if (display.Id.IndexOf("VEN_10DE",StringComparison.OrdinalIgnoreCase)>=0) return true;
            if (display.Id.IndexOf("VEN_1002",StringComparison.OrdinalIgnoreCase)>=0 ||
                display.Id.IndexOf("VEN_8086",StringComparison.OrdinalIgnoreCase)>=0) integratedDesktop=true;
        }
        return !integratedDesktop;
    }
}
