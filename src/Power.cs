using System;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using Microsoft.Win32.SafeHandles;

internal sealed class Power : IDisposable {
    SafeFileHandle handle;
    public ulong Original, Current;
    public double Unit;
    public bool Ready, Locked;
    public double PL1 { get { return (Current & 0x7fff) * Unit; } }
    public double PL2 { get { return ((Current >> 32) & 0x7fff) * Unit; } }
    public bool Enabled1 { get { return (Current & (1UL << 15)) != 0; } }
    public bool Enabled2 { get { return (Current & (1UL << 47)) != 0; } }
    public void Open() {
        Dispose();
        handle = Native.CreateFile(@"\\?\GLOBALROOT\Device\PawnIO",0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(),"PawnIO недоступен. Установите подписанный драйвер и запустите gpdt от администратора.");
        var blob=File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"drivers","IntelMSR.bin"));
        uint returned;
        if (!Native.DeviceIoControl(handle,(41394u<<16)|(0x821u<<2),blob,(uint)blob.Length,null,0,out returned,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(),"PawnIO не принял модуль IntelMSR.");
        Unit=Math.Pow(0.5,(int)(Read(0x606)&15));
        Current=Read(0x610); Original=Current;
        Locked=(Current&(1UL<<63))!=0;
        if (PL1<=0 || PL1>100 || PL2<=0 || PL2>150) throw new InvalidOperationException("Неожиданные лимиты процессора. Изменения заблокированы.");
        Ready=true;
    }
    byte[] Execute(string name,ulong[] args,int outputLength) {
        byte[] req=new byte[32+args.Length*8];
        Encoding.ASCII.GetBytes(name).CopyTo(req,0);
        for(int i=0;i<args.Length;i++) BitConverter.GetBytes(args[i]).CopyTo(req,32+i*8);
        byte[] result=new byte[outputLength]; uint returned;
        if(!Native.DeviceIoControl(handle,(41394u<<16)|(0x841u<<2),req,(uint)req.Length,result,(uint)result.Length,out returned,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if(returned != outputLength) throw new IOException("Неполный ответ драйвера.");
        return result;
    }
    public ulong Read(uint msr) { return BitConverter.ToUInt64(Execute("ioctl_read_msr",new ulong[]{msr},8),0); }
    void Write(ulong value) { Execute("ioctl_write_msr",new ulong[]{0x610,value},0); }
    public static ulong WithLimits(ulong old,double unit,double pl1,double pl2) {
        if(double.IsNaN(unit) || double.IsInfinity(unit) || double.IsNaN(pl1) || double.IsInfinity(pl1) || double.IsNaN(pl2) || double.IsInfinity(pl2) || unit<=0 || pl1<6 || pl1>15 || pl2<pl1 || pl2>15) throw new ArgumentOutOfRangeException("limits","Допустимо 6–15 Вт, PL2 ≥ PL1.");
        ulong mask=0x7fffUL|(0x7fffUL<<32);
        return (old&~mask)|((ulong)Math.Round(pl1/unit)&0x7fff)|(((ulong)Math.Round(pl2/unit)&0x7fff)<<32);
    }
    public void Apply(double pl1,double pl2) {
        if(!Ready || Locked || !Enabled1 || !Enabled2) throw new InvalidOperationException("Лимиты недоступны, выключены или заблокированы BIOS.");
        ulong before=Read(0x610);
        if((before&(1UL<<63))!=0) throw new InvalidOperationException("BIOS заблокировал запись.");
        ulong desired=WithLimits(before,Unit,pl1,pl2);
        try { Write(desired); Current=Read(0x610); if(Current!=desired) throw new IOException("Процессор не сохранил запрошенные лимиты."); }
        catch { try { Write(before); Current=Read(0x610); } catch {} throw; }
    }
    public void Restore() {
        if(!Ready) throw new InvalidOperationException("Сначала прочитайте лимиты.");
        if((Read(0x610)&(1UL<<63))!=0) throw new InvalidOperationException("Запись заблокирована BIOS.");
        Write(Original); Current=Read(0x610);
        if(Current!=Original) throw new IOException("Не удалось подтвердить восстановление.");
    }
    public void Refresh() { if(Ready) { Current=Read(0x610); Locked=(Current&(1UL<<63))!=0; } }
    public void Dispose() { Ready=false; if(handle!=null) { handle.Dispose(); handle=null; } }
}

public sealed class SavedPowerValues {
    public int Version=1;
    public double PL1,PL2;
}

internal sealed class SavedPowerMode {
    readonly string path;
    SavedPowerValues values;
    public bool Pending { get; private set; }
    public bool HasValue { get { return values!=null; } }
    public double PL1 { get { return values.PL1; } }
    public double PL2 { get { return values.PL2; } }
    public SavedPowerMode(string file) { path=file; }
    public void Load() {
        values=null;Pending=false;
        if(!File.Exists(path))return;
        SavedPowerValues loaded;
        using(var stream=File.OpenRead(path))loaded=(SavedPowerValues)new XmlSerializer(typeof(SavedPowerValues)).Deserialize(stream);
        if(loaded==null || loaded.Version!=1)throw new IOException("Неизвестный формат сохранённого режима мощности.");
        Power.WithLimits(0,0.125,loaded.PL1,loaded.PL2);
        values=loaded;Pending=true;
    }
    public void CancelPending() { Pending=false; }
    public void Save(double slow,double fast) {
        Power.WithLimits(0,0.125,slow,fast);
        var selected=new SavedPowerValues{PL1=slow,PL2=fast};
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=File.Create(temp))new XmlSerializer(typeof(SavedPowerValues)).Serialize(stream,selected);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            values=selected;Pending=false;
        }finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public bool ApplyPending(Action<double,double> apply) {
        if(!Pending)return false;
        apply(values.PL1,values.PL2);
        Pending=false;return true;
    }
    public void Clear() {
        Pending=false;
        if(File.Exists(path))File.Delete(path);
        values=null;
    }
}
