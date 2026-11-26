using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Narrow access to the ASUS System Control Interface. No voltage or fan curve writes.
public sealed class LaptopAsus : IDisposable {
    internal const uint GpuEco = 0x00090020;
    const uint ChargeLimit = 0x00120057;
    // G614PW reports a status word with no readable threshold for this control.
    internal bool CanSetChargeLimit() {
        return Call(0x53545344, ChargeLimit, 0) == 0x01890000;
    }
    internal void SetChargeLimit(int percent) {
        if (percent != 80 && percent != 100) throw new ArgumentOutOfRangeException("percent");
        if (!CanSetChargeLimit()) throw new NotSupportedException("ASUS charge limit is unavailable.");
        int result = Call(0x53564544, ChargeLimit, percent);
        if (result != 1) throw new InvalidOperationException("ASUS rejected the charge limit (response " + result + ").");
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
    readonly SafeFileHandle handle;
    public LaptopAsus() {
        handle = CreateFile(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    int Call(uint method, uint device, int value) {
        byte[] request = new byte[16], response = new byte[16];
        Buffer.BlockCopy(BitConverter.GetBytes(method),0,request,0,4);
        Buffer.BlockCopy(BitConverter.GetBytes(8),0,request,4,4);
        Buffer.BlockCopy(BitConverter.GetBytes(device),0,request,8,4);
        Buffer.BlockCopy(BitConverter.GetBytes(value),0,request,12,4);
        int received;
        if (!DeviceIoControl(handle,0x22240C,request,16,response,16,out received,IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (received < 4) throw new InvalidOperationException("ASUS returned an incomplete response.");
        return BitConverter.ToInt32(response,0);
    }
    public int Read(uint device) {
        int raw = Call(0x53545344,device,0);
        if ((raw & 0x10000)==0 || (raw & 0x80000)!=0) return -1;
        return raw & 0xFFFF;
    }
    public void Write(uint device, int value) {
        if (device != GpuEco) throw new ArgumentException("Unsupported control.");
        if (device == GpuEco && value != 0 && value != 1) throw new ArgumentOutOfRangeException("value");
        if (Read(device)<0) throw new NotSupportedException("This ASUS control is unavailable.");
        int result = Call(0x53564544,device,value);
        if (result!=1) throw new InvalidOperationException("ASUS did not accept the setting (response " + result + ").");
    }
    public void Dispose() { handle.Dispose(); }
}
