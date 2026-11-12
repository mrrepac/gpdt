using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class Native {
    internal delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] internal struct Kbd { public uint vk, scan, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawDevice { public ushort page, usage; public uint flags; public IntPtr target; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawHeader { public uint type,size; public IntPtr device,wparam; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size=32)] internal struct InputUnion { [FieldOffset(0)] public KeyboardInput key; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint type; public InputUnion data; }
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int id,HookProc fn,IntPtr mod,uint tid);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint GetRawInputData(IntPtr raw,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
    [DllImport("hid.dll")] internal static extern int HidP_GetUsages(int type,ushort page,ushort link,[Out] ushort[] usages,ref uint count,IntPtr preparsed,byte[] report,uint length);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] internal static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlapped);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
}
