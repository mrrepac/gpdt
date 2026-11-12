using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

internal static class Autostart {
    const string TaskName="gpdt - user logon";
    static dynamic Connect() {
        dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        service.Connect();return service;
    }
    static dynamic Find(dynamic folder) {
        try{return folder.GetTask(TaskName);}
        catch(COMException ex){if(ex.ErrorCode==unchecked((int)0x80070002))return null;throw;}
    }
    static bool SamePath(string path) {
        return string.Equals(Path.GetFullPath(path.Trim('"')),Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath),StringComparison.OrdinalIgnoreCase);
    }
    public static bool IsEnabled() {
        dynamic service=Connect(),task=Find(service.GetFolder("\\"));
        if(task==null || !task.Enabled)return false;
        dynamic definition=task.Definition;
        if(definition.Principal.RunLevel!=1 || definition.Principal.LogonType!=3)return false;
        if(definition.Actions.Count!=1 || definition.Actions.Item(1).Type!=0)return false;
        if(!SamePath((string)definition.Actions.Item(1).Path) || (string)definition.Actions.Item(1).Arguments!="--enable --tray")return false;
        string sid=WindowsIdentity.GetCurrent().User.Value;
        for(int i=1;i<=definition.Triggers.Count;i++) {
            dynamic trigger=definition.Triggers.Item(i);
            if(trigger.Type==9 && trigger.Enabled) {
                string user=trigger.UserId;
                if(user==sid || string.Equals(user,WindowsIdentity.GetCurrent().Name,StringComparison.OrdinalIgnoreCase))return true;
            }
        }
        return false;
    }
    public static void SetEnabled(bool enabled) {
        dynamic service=Connect(),folder=service.GetFolder("\\");
        if(!enabled) {
            dynamic task=Find(folder);
            // A portable copy must not remove the startup entry of another installation.
            if(task!=null && task.Definition.Actions.Count==1 && task.Definition.Actions.Item(1).Type==0 && SamePath((string)task.Definition.Actions.Item(1).Path))folder.DeleteTask(TaskName,0);
            return;
        }
        string user=WindowsIdentity.GetCurrent().User.Value;
        dynamic definition=service.NewTask(0);
        definition.RegistrationInfo.Description="gpdt: переназначения клавиш при входе в Windows";
        definition.Principal.UserId=user;definition.Principal.LogonType=3;definition.Principal.RunLevel=1;
        definition.Settings.Enabled=true;definition.Settings.DisallowStartIfOnBatteries=false;
        definition.Settings.StopIfGoingOnBatteries=false;definition.Settings.ExecutionTimeLimit="PT0S";
        definition.Settings.MultipleInstances=2;
        dynamic trigger=definition.Triggers.Create(9);trigger.UserId=user;trigger.Enabled=true;
        dynamic action=definition.Actions.Create(0);action.Path=System.Windows.Forms.Application.ExecutablePath;
        action.Arguments="--enable --tray";action.WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory;
        folder.RegisterTaskDefinition(TaskName,definition,6,user,null,3,null);
        if(!IsEnabled())throw new IOException("Не удалось проверить настройку автозапуска.");
    }
}
