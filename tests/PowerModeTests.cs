using System;
using System.IO;
using System.Xml.Serialization;

class PowerModeTests {
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
    static void Main(string[] args) {
        string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"power-mode.xml");
        var mode=new SavedPowerMode(path);mode.Load();int calls=0;
        Check(!mode.ApplyPending((a,b)=>calls++) && calls==0,"No saved selection means no startup writes");
        mode.Save(6,8);
        var restarted=new SavedPowerMode(path);restarted.Load();
        Check(restarted.Pending && restarted.PL1==6 && restarted.PL2==8,"Selection survives a new application instance");
        Reject(()=>restarted.ApplyPending((a,b)=>{throw new IOException("PawnIO not ready");}),"Unavailable driver reports failure");
        Check(restarted.Pending,"Failed startup application remains pending");
        double slow=0,fast=0;
        restarted.ApplyPending((a,b)=>{calls++;slow=a;fast=b;});
        Check(calls==1 && slow==6 && fast==8 && !restarted.Pending,"Retry restores the saved limits");
        restarted.ApplyPending((a,b)=>calls++);
        Check(calls==1,"Successful restore is not repeatedly enforced");
        restarted.Save(10,13);mode.Load();
        Check(mode.PL1==10 && mode.PL2==13,"New selection atomically replaces previous selection");
        mode.CancelPending();mode.ApplyPending((a,b)=>calls++);
        Check(calls==1,"Manual selection cancels an older pending restore");
        mode.Save(9.5,12.5);restarted.Load();
        Check(restarted.PL1==9.5 && restarted.PL2==12.5,"Custom limits are persisted");
        Reject(()=>mode.Save(double.NaN,13),"NaN cannot be saved");
        Reject(()=>mode.Save(15,8),"Invalid order cannot be saved");
        restarted.Load();Check(restarted.PL1==9.5,"Invalid save preserves previous settings");
        mode.Clear();restarted.Load();Check(!restarted.HasValue && !restarted.Pending,"Restore original disables startup application on next launch");
        File.WriteAllText(path,"broken xml");
        Reject(()=>mode.Load(),"Corrupt settings rejected");Check(!mode.Pending,"Corrupt settings never schedule a write");
        using(var file=File.Create(path))new XmlSerializer(typeof(SavedPowerValues)).Serialize(file,new SavedPowerValues{PL1=100,PL2=100});
        Reject(()=>mode.Load(),"Out of range settings rejected on load");
        using(var file=File.Create(path))new XmlSerializer(typeof(SavedPowerValues)).Serialize(file,new SavedPowerValues{Version=9,PL1=6,PL2=8});
        Reject(()=>mode.Load(),"Unknown format rejected");
        File.Delete(path);
        Console.WriteLine("All power persistence tests passed; no hardware accessed.");
    }
}
