using System;
using System.IO;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// UI thread owns control IOCTLs; asynchronous HID completions only queue edges.
internal sealed class BrightnessFilter : IDisposable {
    readonly object gate=new object();
    readonly FileStream[] readers=new FileStream[2];
    readonly bool[] held=new bool[2];
    readonly Queue<int> presses=new Queue<int>();
    readonly SafeFileHandle[] controls=new SafeFileHandle[2];
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Get_Device_Interface_List_SizeW")]
    static extern uint InterfaceSize(out uint size,ref Guid guid,string device,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Get_Device_Interface_ListW")]
    static extern uint InterfaceList(ref Guid guid,string device,[Out] char[] buffer,uint size,uint flags);
    static string[] Interfaces() {
        Guid guid=new Guid("0498b91a-6753-43d7-a483-259ca8e67019");
        uint size;if(InterfaceSize(out size,ref guid,null,0)!=0 || size>65536)throw new IOException("UMDF interface enumeration failed");
        char[] buffer=new char[size];if(InterfaceList(ref guid,null,buffer,size,0)!=0)throw new IOException("UMDF interfaces changed; retry");
        return new string(buffer).Split(new[]{'\0'},StringSplitOptions.RemoveEmptyEntries);
    }
    string failure;
    bool active,blocking;
    public bool Blocking { get { lock(gate)return blocking; } }
    public uint Seen { get; private set; }
    public ulong Suppressed { get; private set; }
    void Command(uint mode) {
        byte[] input=new byte[8];uint returned;
        Array.Copy(BitConverter.GetBytes(3u),input,4);
        Array.Copy(BitConverter.GetBytes(mode),0,input,4,4);
        foreach(var control in controls)if(control!=null && !control.IsInvalid && !Native.DeviceIoControl(control,0x22e000,input,8,null,0,out returned,IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"UMDF command error="+Marshal.GetLastWin32Error());
    }
    public void Start() {
        Dispose();
        try {
            string[] paths=Interfaces();
            for(int i=0;i<2;i++) {
                string marker=i==0?"INTC816&Col0D#":"INTC816&Col0E#";
                string path=Array.Find(paths,p=>p.IndexOf(marker,StringComparison.OrdinalIgnoreCase)>=0);
                if(path==null)throw new IOException("UMDF-фильтр не загружен для "+marker);
                controls[i]=Native.CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0x120000,IntPtr.Zero);
                if(controls[i].IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"Не удалось открыть UMDF-фильтр");
                byte[] version=new byte[56];uint count;
                if(!Native.DeviceIoControl(controls[i],0x226004,null,0,version,56,out count,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                if(count!=56 || BitConverter.ToUInt32(version,0)!=3)throw new IOException("Требуется UMDF protocol 3");
            }
            Command(1);
            lock(gate){active=true;failure=null;}
            for(int i=0;i<2;i++) {
                string path=i==0?@"\\?\HID#INTC816&Col0D#3&10839a5c&0&000c#{4d1e55b2-f16f-11cf-88cb-001111000030}":@"\\?\HID#INTC816&Col0E#3&10839a5c&0&000d#{4d1e55b2-f16f-11cf-88cb-001111000030}";
                var handle=Native.CreateFile(path,0x80000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
                if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error,"Не удалось открыть кнопку яркости");}
                var stream=new FileStream(handle,FileAccess.Read,2,true);
                lock(gate)readers[i]=stream;
                Read(i,stream);
            }
        }catch{Dispose();throw;}
    }
    void Read(int index,FileStream stream) {
        byte[] report=new byte[2];
        try { stream.BeginRead(report,0,2,ar=>{
            try {
                int length=stream.EndRead(ar);
                lock(gate) {
                    if(!active || readers[index]!=stream)return;
                    if(length!=2 || report[0]!=(index==0?0x14:0x15) || report[1]>1)throw new IOException("Неожиданный HID-отчёт");
                    bool down=report[1]==1;
                    if(blocking && down && !held[index])presses.Enqueue(index==0?0x6f:0x70);
                    held[index]=down;
                }
                Read(index,stream);
            }catch(Exception ex){Fail(index,stream,ex);}
        },null); }catch(Exception ex){Fail(index,stream,ex);}
    }
    void Fail(int index,FileStream stream,Exception ex) {
        lock(gate)if(active && readers[index]==stream)failure=ex.Message;
    }
    public int[] Poll() {
        lock(gate) {
            if(!active)return new int[0];
            if(failure!=null)throw new IOException(failure);
        }
        Seen=0;Suppressed=0;
        for(int i=0;i<2;i++) {
            byte[] data=new byte[56];uint returned;
            if(!Native.DeviceIoControl(controls[i],0x226004,null,0,data,56,out returned,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(returned!=56 || BitConverter.ToUInt32(data,0)!=3)throw new IOException("UMDF protocol mismatch");
            uint mode=BitConverter.ToUInt32(data,4),attached=BitConverter.ToUInt32(data,8);
            Seen|=BitConverter.ToUInt32(data,12);Suppressed+=BitConverter.ToUInt64(data,32);
            if(attached!=(1u<<i) || mode==0 || (Blocking && mode!=2))throw new IOException("Связь с UMDF-фильтром прервана. Перезапустите переназначения.");
        }
        lock(gate) {
            // Protocol v2 verifies queued owner reads, without physical calibration.
            if(!blocking && Seen==3) { Command(2);blocking=true;presses.Clear(); }
            else Command(blocking?2u:1u);
            int[] result=presses.ToArray();presses.Clear();return result;
        }
    }
    public void Dispose() {
        FileStream[] closing=new FileStream[2];
        lock(gate) {
            active=blocking=false;failure=null;presses.Clear();Seen=0;
            for(int i=0;i<2;i++){closing[i]=readers[i];readers[i]=null;held[i]=false;}
        }
        try{Command(0);}catch{} for(int i=0;i<2;i++){if(controls[i]!=null){controls[i].Dispose();controls[i]=null;}}
        foreach(var stream in closing)if(stream!=null)stream.Dispose();
    }
}
