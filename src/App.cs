using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

public class Binding {
    public string Name;
    public int Vk, Scan;
    public bool Extended, Verified;
    public int Target;
    public bool Ctrl, Alt, Shift, Win;
    public string Evidence;
    public int HidUsage;
    public string DeviceMatch;
    public string Identity { get { return Vk+":"+Scan+":"+Extended; } }
}

internal sealed class HidEdges {
    readonly Dictionary<string,HashSet<ushort>> previous=new Dictionary<string,HashSet<ushort>>();
    public ushort[] Update(string device,IEnumerable<ushort> usages) { var now=new HashSet<ushort>(usages.Where(x=>x!=0));HashSet<ushort> before;if(!previous.TryGetValue(device,out before))before=new HashSet<ushort>();var pressed=now.Where(x=>!before.Contains(x)).ToArray();previous[device]=now;return pressed; }
    public void Clear(){previous.Clear();}
}
internal sealed class MainForm : Form {
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    List<Binding> bindings;
    readonly Dictionary<string,List<int>> held=new Dictionary<string,List<int>>();
    readonly Dictionary<int,int> outputRefs=new Dictionary<int,int>();
    readonly HashSet<string> captureHeld=new HashSet<string>();
    readonly HidEdges hidEdges=new HidEdges();
    Native.HookProc callback;
    IntPtr hook;
    DataGridView grid;
    PageHost pages;
    readonly List<DashboardButton> keyTiles=new List<DashboardButton>();
    readonly List<DashboardButton> powerTiles=new List<DashboardButton>();
    ComboBox keyPicker;
    Label homePower, homeState;
    static readonly Color Ground=Color.FromArgb(17,18,20), Surface=Color.FromArgb(28,30,33), Ink=Color.FromArgb(245,245,246), Muted=Color.FromArgb(159,164,172);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);

    CheckBox enable,ctrl,alt,shift,win;
    ComboBox target;
    Label status,captureHint,powerStatus;
    TextBox log;
    Button captureButton,applyPower;
    NumericUpDown pl1,pl2;
    NotifyIcon tray;
    Timer timer;
    DateTime captureUntil=DateTime.MinValue,monitorUntil=DateTime.MinValue;
    int captureRow=-1;
    bool exiting;
    bool captureWasEnabled, pausingCapture;
    Power power=new Power();
    ManagementEventWatcher brightnessWatcher;
    string reportPath;
    readonly BrightnessFilter brightnessFilter=new BrightnessFilter();
    Label filterStatus;
    bool filterRunning;
    DateTime nextPowerRead=DateTime.MinValue;
    public MainForm(bool activateMappings=false, bool startInTray=false) {
        SuspendLayout();
        AutoScaleMode=AutoScaleMode.Dpi;using(var dpi=CreateGraphics())AutoScaleDimensions=new SizeF(dpi.DpiX,dpi.DpiY);
        Font=new Font("Segoe UI",10);
        Text="gpdt · GPD MicroPC 2"; ClientSize=new Size(1400,600); MinimumSize=new Size(1400,600);
        StartPosition=FormStartPosition.CenterScreen;
        using(var iconStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("gpdt.ico"))using(var embeddedIcon=new Icon(iconStream))Icon=(Icon)embeddedIcon.Clone(); BackColor=Ground; ForeColor=Ink;
        Directory.CreateDirectory(Path.Combine(root,"data")); reportPath=Path.Combine(root,"data","keyboard-diagnostic.log");
        if(!File.Exists(Path.Combine(root,"data","bindings.xml")) && File.Exists(Path.Combine(root,"defaults","bindings.xml"))) {
            File.Copy(Path.Combine(root,"defaults","bindings.xml"),Path.Combine(root,"data","bindings.xml"));
            File.WriteAllText(Path.Combine(root,"data","brightness-copy-paste-v1"),"Release defaults initialized.");
        }
        LoadBindings();
        if(!File.Exists(Path.Combine(root,"data","brightness-copy-paste-v1"))) {
            for(int i=1;i<=2;i++) { var b=bindings[i];b.HidUsage=i==1?0x70:0x6f;b.DeviceMatch=i==1?"HID#INTC816&Col0E#":"HID#INTC816&Col0D#";b.Verified=true;b.Evidence="Physical HID diagnostic 2026-11-12";b.Target=i==1?67:86;b.Ctrl=true;b.Alt=b.Shift=b.Win=false; }
            SaveBindings();File.WriteAllText(Path.Combine(root,"data","brightness-copy-paste-v1"),"Configured from physical HID diagnostics.");
        }
        var outer=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(24,16,24,12)};
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,80)); outer.RowStyles.Add(new RowStyle(SizeType.Percent,100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        var canvas=new Panel{Dock=DockStyle.Fill,AutoScroll=true};Controls.Add(canvas);outer.Dock=DockStyle.None;canvas.Controls.Add(outer);
        EventHandler arrange=delegate { float scale=DeviceScale();int width=Math.Min(canvas.ClientSize.Width,(int)(1200*scale));outer.Bounds=new Rectangle(Math.Max(0,(canvas.ClientSize.Width-width)/2),0,width,Math.Max(canvas.ClientSize.Height,500)); };
        canvas.Resize+=arrange;Shown+=arrange;
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,126));
        header.Controls.Add(new Label{Text="gpdt",AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,UseCompatibleTextRendering=true,Font=new Font("Segoe UI",24,FontStyle.Bold),ForeColor=Ink},0,0);
        homeState=new Label{Text="GPD MicroPC 2",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,ForeColor=Muted};header.Controls.Add(homeState,1,0);
        var more=Btn("Ещё ···",delegate {});more.AutoSize=false;more.MinimumSize=Size.Empty;more.Size=new Size(110,40);more.Anchor=AnchorStyles.Right;more.Margin=new Padding(8);header.Controls.Add(more,2,0);
        var menuMore=new ContextMenuStrip();
        menuMore.Items.Add("Главная",null,delegate { pages.SelectedIndex=0; });
        menuMore.Items.Add("Настроить клавиши",null,delegate { pages.SelectedIndex=1; });
        menuMore.Items.Add("Настройки мощности",null,delegate { pages.SelectedIndex=2; });
        menuMore.Items.Add("Диагностика",null,delegate { pages.SelectedIndex=3; });
        menuMore.Items.Add(new ToolStripSeparator());
        menuMore.Items.Add("Отменить последний сброс",null,delegate { UndoReset(); });
        menuMore.Items.Add("Сбросить все назначения…",null,delegate { ResetBindings(true); });
        more.Click+=delegate { menuMore.Show(more,new Point(0,more.Height)); };outer.Controls.Add(header,0,0);
        pages=new PageHost{Dock=DockStyle.Fill}; outer.Controls.Add(pages,0,1);
        var home=new Panel{Dock=DockStyle.Fill};var keyboard=new Panel{Dock=DockStyle.Fill};var powerTab=new Panel{Dock=DockStyle.Fill};var diagnostics=new Panel{Dock=DockStyle.Fill};pages.Controls.AddRange(new[]{home,keyboard,powerTab,diagnostics});
        BuildKeyboard(keyboard);BuildPower(powerTab);BuildDiagnostics(diagnostics);BuildHome(home);pages.SelectedIndex=0;
        foreach(var page in new[]{keyboard,powerTab,diagnostics}) {
            var back=Btn("← Главная",delegate { pages.SelectedIndex=0; });back.Dock=DockStyle.Top;back.Height=40;page.Controls.Add(back);
        }
        Theme(this);
        status=new Label{Text="Нажмите на клавишу, чтобы изменить её действие.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI",9),ForeColor=Muted,AutoEllipsis=true};outer.Controls.Add(status,0,2);
        int dark=1;try { DwmSetWindowAttribute(Handle,20,ref dark,4); }catch {}
        tray=new NotifyIcon{Icon=Icon,Text="gpdt — переназначения выключены",Visible=true};
        var menu=new ContextMenuStrip(); menu.Items.Add("Открыть gpdt",null,delegate { Show(); WindowState=FormWindowState.Normal; Activate(); });
        menu.Items.Add("Выключить переназначения",null,delegate { enable.Checked=false; });
        menu.Items.Add("Выход",null,delegate { exiting=true; Close(); }); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { Show(); WindowState=FormWindowState.Normal; Activate(); };
        callback=KeyboardHook;
        hook=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"Не удалось подключить перехват клавиш.");
        var raw=new[]{new Native.RawDevice{page=1,usage=6,flags=0x100,target=Handle},new Native.RawDevice{page=12,usage=1,flags=0x100,target=Handle}};
        if(!Native.RegisterRawInputDevices(raw,(uint)raw.Length,(uint)Marshal.SizeOf(typeof(Native.RawDevice)))) Note("RAW INPUT: ошибка регистрации "+Marshal.GetLastWin32Error());
        timer=new Timer{Interval=100}; timer.Tick+=Tick; timer.Start();
        System.Threading.Tasks.Task.Run((Action)(()=>{
            try {
                var watcher=new ManagementEventWatcher("root\\wmi","SELECT * FROM WmiMonitorBrightnessEvent");
                watcher.EventArrived+=delegate(object sender,EventArrivedEventArgs e) { if(Recording && !IsDisposed) { string message="WMI brightness="+e.NewEvent["Brightness"];try{BeginInvoke((Action)(()=>Note(message)));}catch(InvalidOperationException){} } };
                lock(power) { if(exiting || IsDisposed){watcher.Dispose();return;} brightnessWatcher=watcher;watcher.Start(); }
            }catch{}
        }));
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(!exiting && e.CloseReason==CloseReason.UserClosing && enable.Checked) { e.Cancel=true; Hide(); tray.ShowBalloonTip(2500,"gpdt работает в фоне","Выключить переназначения или выйти можно через значок в трее.",ToolTipIcon.Info); return; }
            exiting=true;brightnessFilter.Dispose();ReleaseAll(); Native.UnhookWindowsHookEx(hook); timer.Stop(); tray.Dispose();
            lock(power) { if(brightnessWatcher!=null) { brightnessWatcher.Stop();brightnessWatcher.Dispose(); } power.Dispose(); }
        };
        Shown+=delegate { SelectRow();enable.Checked=activateMappings || (File.Exists(Path.Combine(root,"data","enabled.txt")) && File.ReadAllText(Path.Combine(root,"data","enabled.txt")).Trim()=="true");PollPower();if(startInTray)BeginInvoke((Action)(()=>Hide())); };
        ResumeLayout(true);
    }
    float DeviceScale() { using(var g=CreateGraphics())return g.DpiX/96f; }
    Button Btn(string text,EventHandler action) { var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(100,34),FlatStyle=FlatStyle.Flat,BackColor=Surface,ForeColor=Ink,Margin=new Padding(4)}; b.FlatAppearance.BorderColor=Color.FromArgb(65,69,75); b.FlatAppearance.MouseOverBackColor=Color.FromArgb(42,45,50);b.Click+=action; return b; }
    Label Info(string text) { return new Label{Text=text,AutoSize=true,MaximumSize=new Size(770,0),Margin=new Padding(5,7,5,7)}; }
    FlowLayoutPanel Flow() { return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(8),FlowDirection=FlowDirection.TopDown,WrapContents=false}; }
    void Theme(Control parent) {
        foreach(Control c in parent.Controls) {
            if(c is TabPage)c.BackColor=Ground;
            else if(c is TextBox || c is ComboBox || c is NumericUpDown) { c.BackColor=Surface;c.ForeColor=Ink; }
            else if(c is Button) { c.BackColor=Surface;c.ForeColor=Ink; }
            else { c.BackColor=Ground;c.ForeColor=Ink; }
            var combo=c as ComboBox;
            if(combo!=null) {
                combo.FlatStyle=FlatStyle.Flat;combo.DrawMode=DrawMode.OwnerDrawFixed;combo.ItemHeight=combo.Font.Height+8;
                combo.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                    var box=(ComboBox)sender;
                    using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(55,60,67):Surface))e.Graphics.FillRectangle(brush,e.Bounds);
                    if(e.Index>=0)TextRenderer.DrawText(e.Graphics,box.Items[e.Index].ToString(),box.Font,e.Bounds,Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                    e.DrawFocusRectangle();
                };
            }
            Theme(c);
        }
    }
    void BuildHome(Control page) {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(0,8,0,0)};
        foreach(int height in new[]{44,108,44,104,48,36})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(layout);
        var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        heading.Controls.Add(new Label{Text="Клавиши",Font=new Font("Segoe UI",14,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
        enable.Dock=DockStyle.Right;enable.Width=190;enable.TextAlign=ContentAlignment.MiddleLeft;heading.Controls.Add(enable,1,0);layout.Controls.Add(heading,0,0);
        var keys=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=8,RowCount=1,Margin=Padding.Empty};
        for(int i=0;i<8;i++)keys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,12.5f));
        keys.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int i=1;i<bindings.Count;i++) {
            int index=i;var tile=new DashboardButton{Dock=DockStyle.Fill,Margin=new Padding(4),Compact=true,Title=bindings[i].Name,AccessibleName=bindings[i].Name};
            tile.Click+=delegate { grid.CurrentCell=grid.Rows[index].Cells[0];SelectRow();pages.SelectedIndex=1; };keyTiles.Add(tile);keys.Controls.Add(tile,i-1,0);
        }
        layout.Controls.Add(keys,0,1);
        layout.Controls.Add(new Label{Text="Мощность процессора",Font=new Font("Segoe UI",14,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,2);
        var profiles=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        string[] names={"Экономия","Баланс","Мощность"};int[] slow={6,10,15},fast={8,13,15};
        for(int i=0;i<3;i++) {
            profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));int index=i;
            var tile=new DashboardButton{Dock=DockStyle.Fill,Margin=new Padding(4),IsPower=true,Title=names[i],Detail=slow[i]+" / "+fast[i]+" Вт",AccessibleName=names[i]+", применить "+slow[i]+" / "+fast[i]+" Вт"};
            tile.Click+=delegate { ApplyPreset(index,slow[index],fast[index]); };powerTiles.Add(tile);profiles.Controls.Add(tile,i,0);
        }
        layout.Controls.Add(profiles,0,3);
        homePower=new Label{Text="PL1 / PL2 · Нажмите на режим, чтобы применить лимиты.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI",9),Padding=new Padding(4,0,4,0)};layout.Controls.Add(homePower,0,4);
        var hint=new Label{Text="● Изменено   ·   Нажмите на клавишу для настройки",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI",9),Padding=new Padding(4,0,4,0)};layout.Controls.Add(hint,0,5);RefreshTiles();
    }
    void RefreshTiles() {
        for(int i=0;i<keyTiles.Count;i++) { var b=bindings[i+1];var tile=keyTiles[i];tile.Detail=b.Target==0?"Штатно":ActionName(b);tile.Marked=b.Target!=0;tile.AccessibleName=b.Name+", "+tile.Detail;tile.Invalidate(); }
        if(homeState!=null && enable!=null)homeState.Text=enable.Checked?"●  Работает":"○  Выключено";
    }
    void ApplyPreset(int index,int slow,int fast) {
        try {
            if(!power.Ready)power.Open();else power.Refresh();
            power.Apply(slow,fast);ShowPower("Лимиты применены.");PowerLog("preset "+index);
            pl1.Value=slow;pl2.Value=fast;
            nextPowerRead=DateTime.Now.AddSeconds(5);
        }catch(Exception ex){homePower.Text="Не удалось применить режим. Подробнее: Ещё → Настройки мощности.";Error(ex);}
    }
    void BuildKeyboard(Control page) {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(8)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,12)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,130)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); page.Controls.Add(layout);
        enable=new CheckBox{Text="Включены",Dock=DockStyle.Fill};
        enable.CheckedChanged+=delegate { ReleaseAll();hidEdges.Clear(); status.Text=enable.Checked?"Переназначения ВКЛЮЧЕНЫ — сохранённые действия работают.":"Переназначения ВЫКЛЮЧЕНЫ — кнопки выполняют штатные действия."; tray.Text=enable.Checked?"gpdt — переназначения включены":"gpdt — переназначения выключены";if(!pausingCapture)File.WriteAllText(Path.Combine(root,"data","enabled.txt"),enable.Checked?"true":"false"); ConfigureFilter();RefreshTiles(); };
        grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None};
        grid.Columns.Add("name","Кнопка"); grid.Columns.Add("code","Определение Windows"); grid.Columns.Add("action","Назначение"); grid.Columns[0].FillWeight=23; grid.Columns[1].FillWeight=47; grid.Columns[2].FillWeight=30; grid.RowTemplate.Height=29;
        grid.SelectionChanged+=delegate { SelectRow(); };grid.Visible=false;layout.Controls.Add(grid,0,0);RefreshGrid();
        keyPicker=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Font=new Font("Segoe UI",16),Margin=new Padding(4,12,4,8)};
        foreach(var b in bindings)keyPicker.Items.Add(b.Name);
        keyPicker.SelectedIndexChanged+=delegate { if(keyPicker.SelectedIndex>=0)grid.CurrentCell=grid.Rows[keyPicker.SelectedIndex].Cells[0]; };keyPicker.SelectedIndex=1;layout.Controls.Add(keyPicker,0,1);
        var edit=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        captureButton=Btn("Определить кнопку",delegate { BeginCapture(); }); edit.Controls.Add(captureButton);
        target=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=145,Margin=new Padding(4,9,4,4)};
        target.Items.Add(new KeyChoice(0,"Без изменения")); target.Items.Add(new KeyChoice(-1,"Отключить кнопку"));
        foreach(int k in new[]{27,45,46,44,9,13,8,32,36,35,33,34,37,38,39,40,173,174,175}) target.Items.Add(new KeyChoice(k,((Keys)k).ToString()));
        for(int k=112;k<=135;k++) target.Items.Add(new KeyChoice(k,((Keys)k).ToString()));
        for(int k=65;k<=90;k++) target.Items.Add(new KeyChoice(k,((Keys)k).ToString()));
        for(int k=48;k<=57;k++) target.Items.Add(new KeyChoice(k,((Keys)k).ToString()));
        edit.Controls.Add(target);
        ctrl=new CheckBox{Text="Ctrl",AutoSize=true}; alt=new CheckBox{Text="Alt",AutoSize=true}; shift=new CheckBox{Text="Shift",AutoSize=true}; win=new CheckBox{Text="Win",AutoSize=true};
        edit.Controls.AddRange(new Control[]{ctrl,alt,shift,win}); edit.Controls.Add(Btn("Сохранить и включить",delegate { SaveSelected(); }));
        edit.Controls.Add(Btn("Сбросить выбранную…",delegate { ResetBindings(false); }));
        var more=Btn("Ещё…",delegate {});
        var resetMenu=new ContextMenuStrip();
        resetMenu.Items.Add("Отменить последний сброс",null,delegate { UndoReset(); });
        resetMenu.Items.Add(new ToolStripSeparator());
        resetMenu.Items.Add("Сбросить все назначения…",null,delegate { ResetBindings(true); });
        more.ContextMenuStrip=resetMenu; more.Click+=delegate { resetMenu.Show(more,new Point(0,more.Height)); };
        edit.Controls.Add(more); layout.Controls.Add(edit,0,2);
        var hints=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};
        hints.RowStyles.Add(new RowStyle(SizeType.Absolute,65));hints.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        filterStatus=new Label{Dock=DockStyle.Fill,Text="Фильтр яркости выключен.",Font=new Font("Segoe UI",9)};hints.Controls.Add(filterStatus,0,0);
        captureHint=new Label{Dock=DockStyle.Fill,Text="Чтобы яркость не менялась, назначьте действие обеим кнопкам яркости. Можно выбрать «Отключить кнопку».",Font=new Font("Segoe UI",9)};hints.Controls.Add(captureHint,0,1);layout.Controls.Add(hints,0,3);
    }
    void BuildPower(Control page) {
        var flow=Flow(); page.Controls.Add(flow);
        flow.Controls.Add(Info("Intel Core i3-N300  ·  лимиты мощности пакета процессора"));
        powerStatus=Info("Лимиты ещё не прочитаны. Для доступа используется подписанный драйвер PawnIO."); flow.Controls.Add(powerStatus);
        var actions=new FlowLayoutPanel{AutoSize=true,Width=750};
        actions.Controls.Add(Btn("Прочитать PL1 / PL2",delegate { ReadPower(); }));
        actions.Controls.Add(Btn("Установить PawnIO…",delegate { try { Process.Start(new ProcessStartInfo(File.Exists(Path.Combine(root,"drivers","PawnIO_setup.exe"))?Path.Combine(root,"drivers","PawnIO_setup.exe"):"https://github.com/namazso/PawnIO.Setup/releases/latest"){UseShellExecute=true,Verb="runas"}); } catch(Exception ex) { Error(ex); } }));
        actions.Controls.Add(Btn("Перезапуск от администратора",delegate { try { Process.Start(new ProcessStartInfo(Application.ExecutablePath){UseShellExecute=true,Verb="runas",Arguments="--elevated"}); exiting=true; Close(); } catch(Exception ex) { Error(ex); } })); flow.Controls.Add(actions);
        var values=new FlowLayoutPanel{AutoSize=true,Width=750}; values.Controls.Add(Info("PL1, длительный (Вт)"));
        pl1=new NumericUpDown{Minimum=6,Maximum=15,DecimalPlaces=1,Increment=0.5M,Value=10,Width=75}; values.Controls.Add(pl1);
        values.Controls.Add(Info("PL2, кратковременный (Вт)")); pl2=new NumericUpDown{Minimum=6,Maximum=15,DecimalPlaces=1,Increment=0.5M,Value=15,Width=75}; values.Controls.Add(pl2); flow.Controls.Add(values);
        var presets=new FlowLayoutPanel{AutoSize=true,Width=750};
        presets.Controls.Add(Btn("Экономия: 6 / 8",delegate { pl1.Value=6;pl2.Value=8; })); presets.Controls.Add(Btn("Баланс: 10 / 13",delegate { pl1.Value=10;pl2.Value=13; })); presets.Controls.Add(Btn("Мощность: 15 / 15",delegate { pl1.Value=15;pl2.Value=15; })); flow.Controls.Add(presets);
        var commit=new FlowLayoutPanel{AutoSize=true,Width=750};
        applyPower=Btn("Применить лимиты",delegate { try { power.Apply((double)pl1.Value,(double)pl2.Value); ShowPower("Запись MSR проверена. Фактическая мощность зависит также от BIOS и нагрузки."); PowerLog("apply"); } catch(Exception ex) { Error(ex); } }); applyPower.Enabled=false; commit.Controls.Add(applyPower);
        commit.Controls.Add(Btn("Вернуть исходные",delegate { try { power.Restore();ShowPower("Исходные значения восстановлены.");PowerLog("restore"); } catch(Exception ex) { Error(ex); } })); flow.Controls.Add(commit);
        flow.Controls.Add(Info("Профили заполняют поля; запись происходит только по кнопке «Применить». Диапазон этой версии — 6–15 Вт. Временные окна и остальные биты регистров сохраняются. Автоприменения при запуске нет."));
        flow.Controls.Add(Info("Лимит мощности — не текущее потребление и не мощность всего компьютера. Дополнительные ограничения BIOS / MMIO могут оказаться ниже PL1 / PL2. gpdt не снимает блокировки BIOS."));
        flow.Controls.Add(Info("Если запись недоступна: BIOS → OEM System Configuration → Configurable TDP. Вход в BIOS MicroPC 2 — Backspace при включении."));
    }
    void BuildDiagnostics(Control page) {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(8)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,120));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(layout);
        layout.Controls.Add(Info("Проверка на 60 секунд: нажмите верхние девять кнопок по очереди. Записываются технические коды, RAW INPUT и HID Consumer Usages. Во время проверки не вводите пароли. После таймера запись автоматически остановится."),0,0);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};
        actions.Controls.Add(Btn("Начать проверку (60 с)",delegate { enable.Checked=false; monitorUntil=DateTime.Now.AddSeconds(60); Note("=== START 60s ===");status.Text="Диагностика: нажмите верхний ряд слева направо."; }));
        actions.Controls.Add(Btn("Остановить",delegate { monitorUntil=DateTime.MinValue;EndCapture();Note("=== STOP ===");status.Text="Запись остановлена."; }));
        actions.Controls.Add(Btn("Папка отчётов",delegate { Process.Start("explorer.exe",Path.Combine(root,"data")); })); layout.Controls.Add(actions,0,1);
        log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Font=new Font("Consolas",9),WordWrap=false};layout.Controls.Add(log,0,2);
    }
    void PollPower() {
        nextPowerRead=DateTime.Now.AddSeconds(5);
        try { if(!power.Ready)power.Open();else power.Refresh();ShowPower(""); }
        catch(Exception ex) { applyPower.Enabled=false;powerStatus.Text=ex.Message;homePower.Text="Текущий режим недоступен · Ещё → Настройки мощности";foreach(var tile in powerTiles){tile.Marked=false;tile.Enabled=false;tile.Invalidate();} }
    }
    void Tick(object sender,EventArgs e) {
        if(DateTime.Now>=nextPowerRead)PollPower();
        if(filterRunning)try {
            bool wasBlocking=brightnessFilter.Blocking;
            foreach(int usage in brightnessFilter.Poll()) {
                if(!enable.Checked)break;
                var binding=bindings.FirstOrDefault(b=>b.Verified && b.HidUsage==usage && b.Target>0);
                if(binding!=null)SendChord(binding);
            }
            if(filterRunning) {
                filterStatus.Text=brightnessFilter.Blocking?"Фильтр активен: назначения работают без изменения яркости. Подавлено отчётов: "+brightnessFilter.Suppressed:"Подключение фильтра яркости… Каналы: "+brightnessFilter.Seen+"/3";
                if(!wasBlocking && brightnessFilter.Blocking)Note("FILTER BLOCK: direct channels verified; brightness suppression active");
            }
        }catch(Exception ex){brightnessFilter.Dispose();filterRunning=false;filterStatus.Text="Кнопки яркости работают штатно. "+ex.Message;Note("FILTER STOP: "+ex.Message);}
        if(captureRow>=0 && DateTime.Now>captureUntil) { EndCapture();captureHint.Text="За 10 секунд клавиатурный код не получен. Проверьте вкладку «Диагностика»: возможен отдельный HID/ACPI-канал."; }
        if(monitorUntil!=DateTime.MinValue && DateTime.Now>monitorUntil) { monitorUntil=DateTime.MinValue; Note("=== AUTO STOP ===");status.Text="Диагностика завершена. Отчёт сохранён в data."; }
    }
    void ConfigureFilter() {
        brightnessFilter.Dispose();filterRunning=false;
        if(!enable.Checked){filterStatus.Text="Фильтр выключен: штатная яркость работает.";return;}
        if(!new[]{0x6f,0x70}.All(u=>bindings.Any(b=>b.Verified && b.HidUsage==u && b.Target!=0))) {
            filterStatus.Text="Кнопки яркости работают штатно. Для подавления задайте действия обеим кнопкам.";return;
        }
        try { brightnessFilter.Start();filterRunning=true;filterStatus.Text="Фильтр подключается автоматически. Нажимать кнопки для подготовки не нужно.";Note("FILTER CONNECT: automatic channel readiness"); }
        catch(Exception ex){filterStatus.Text="Кнопки яркости работают штатно. "+ex.Message;Note("FILTER UNAVAILABLE: "+ex.Message);}
    }
    bool Recording { get { return DateTime.Now<monitorUntil || (captureRow>=0 && DateTime.Now<captureUntil); } }
    void BeginCapture() { if(grid.CurrentRow==null)return;if(bindings[grid.CurrentRow.Index].HidUsage!=0){captureHint.Text="Эта HID-кнопка уже определена. Выберите назначение и нажмите «Сохранить и включить».";return;}if(captureRow>=0)EndCapture();captureWasEnabled=enable.Checked;pausingCapture=true;enable.Checked=false;pausingCapture=false;captureRow=grid.CurrentRow.Index;captureUntil=DateTime.Now.AddSeconds(10);captureHint.Text="Нажмите один раз: "+bindings[captureRow].Name+". Ожидание 10 секунд…";Note("CAPTURE "+bindings[captureRow].Name); }
    void EndCapture() { bool restore=captureRow>=0&&captureWasEnabled;captureRow=-1;captureUntil=DateTime.MinValue;if(restore)enable.Checked=true; }
    void CaptureKey(Native.Kbd k) {
        int row=captureRow; if(row<0)return;
        var b=bindings[row];b.Vk=(int)k.vk;b.Scan=(int)k.scan;b.Extended=(k.flags&1)!=0;b.Verified=true;b.Evidence="Keyboard hook";
        EndCapture();SaveBindings();RefreshGrid();grid.CurrentCell=grid.Rows[row].Cells[0];captureHint.Text="Определено: "+b.Name+" → "+Code(b)+". Теперь выберите назначение и сохраните.";
    }
    IntPtr KeyboardHook(int n,IntPtr w,IntPtr l) {
        if(n<0)return Native.CallNextHookEx(hook,n,w,l);
        var k=(Native.Kbd)Marshal.PtrToStructure(l,typeof(Native.Kbd));
        if((k.flags&0x10)!=0)return Native.CallNextHookEx(hook,n,w,l);
        bool down=w.ToInt64()==0x100 || w.ToInt64()==0x104;bool up=w.ToInt64()==0x101 || w.ToInt64()==0x105;
        string id=k.vk+":"+k.scan+":"+((k.flags&1)!=0);
        if(down && k.vk==19 && IsDown(17) && IsDown(18)) { enable.Checked=false;return (IntPtr)1; }
        if(Recording && down) { string message=string.Format("HOOK vk=0x{0:X2} scan=0x{1:X2} ext={2}",k.vk,k.scan,(k.flags&1)!=0); BeginInvoke((Action)(()=>Note(message))); }
        if(up && captureHeld.Remove(id))return (IntPtr)1;
        if(captureRow>=0 && down && k.vk!=16 && k.vk!=17 && k.vk!=18 && k.vk!=91 && k.vk!=92 && !(k.vk>=160&&k.vk<=165)) {
            captureHeld.Add(id);var copy=k; BeginInvoke((Action)(()=>CaptureKey(copy)));return (IntPtr)1;
        }
        List<int> active;
        if(held.TryGetValue(id,out active)) { if(up) { Release(active);held.Remove(id); } else if(down && active.Count>0) Send(active[active.Count-1],false); return (IntPtr)1; }
        if(enable.Checked && down) {
            var b=bindings.FirstOrDefault(x=>x.Verified && x.Target!=0 && x.Identity==id);
            if(b!=null) {
                var keys=new List<int>();
                if(b.Target>0) { if(b.Ctrl && !IsDown(17))keys.Add(17);if(b.Alt && !IsDown(18))keys.Add(18);if(b.Shift && !IsDown(16))keys.Add(16);if(b.Win && !IsDown(91))keys.Add(91);keys.Add(b.Target); }
                held[id]=keys;
                foreach(int key in keys) { int refs;outputRefs.TryGetValue(key,out refs);outputRefs[key]=refs+1;if(refs==0)Send(key,false); }
                return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(hook,n,w,l);
    }
    static bool IsDown(int key) { return (Native.GetAsyncKeyState(key)&0x8000)!=0; }
    void Send(int key,bool up) {
        bool extended=key==45 || key==46 || key==44 || (key>=33&&key<=40) || key==91 || key==92 || (key>=173&&key<=175);
        var input=new Native.Input{type=1,data=new Native.InputUnion{key=new Native.KeyboardInput{vk=(ushort)key,flags=(up?2u:0u)|(extended?1u:0u)}}};
        if(Native.SendInput(1,new[]{input},Marshal.SizeOf(typeof(Native.Input)))!=1) BeginInvoke((Action)(()=>{enable.Checked=false;status.Text="Windows отклонила ввод. Проверьте права окна назначения.";}));
    }
    void Release(List<int> keys) { for(int i=keys.Count-1;i>=0;i--) { int key=keys[i],refs; if(outputRefs.TryGetValue(key,out refs)) { if(refs<=1) { outputRefs.Remove(key);Send(key,true); } else outputRefs[key]=refs-1; } } }
    void ReleaseAll() { foreach(var pair in held)Release(pair.Value);held.Clear(); }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0xff && (Recording || (enable!=null && enable.Checked))) { try { ReadRaw(m.LParam); } catch(Exception ex) { Note("RAW ERROR "+ex.Message); } }
        base.WndProc(ref m);
    }
    void ReadRaw(IntPtr input) {
        uint size=0,hs=(uint)Marshal.SizeOf(typeof(Native.RawHeader));
        if(Native.GetRawInputData(input,0x10000003,IntPtr.Zero,ref size,hs)==uint.MaxValue || size<hs || size>65536)return;
        IntPtr ptr=Marshal.AllocHGlobal((int)size);
        try {
            if(Native.GetRawInputData(input,0x10000003,ptr,ref size,hs)==uint.MaxValue)return;
            var header=(Native.RawHeader)Marshal.PtrToStructure(ptr,typeof(Native.RawHeader)); int offset=(int)hs;
            if(header.type==1 && Recording) { ushort scan=(ushort)Marshal.ReadInt16(ptr,offset),flags=(ushort)Marshal.ReadInt16(ptr,offset+2),vk=(ushort)Marshal.ReadInt16(ptr,offset+6);if((flags&1)==0)Note(string.Format("RAW KEY vk=0x{0:X2} scan=0x{1:X2} flags={2} device={3}",vk,scan,flags,DeviceName(header.device))); }
            if(header.type==2) {
                int bytes=Marshal.ReadInt32(ptr,offset),count=Marshal.ReadInt32(ptr,offset+4);if(bytes<1||count<1||bytes>4096||count>64||offset+8+(long)bytes*count>size)return;
                uint prepSize=0;Native.GetRawInputDeviceInfo(header.device,0x20000005,IntPtr.Zero,ref prepSize);if(prepSize==0)return;
                IntPtr prep=Marshal.AllocHGlobal((int)prepSize);
                try { if(Native.GetRawInputDeviceInfo(header.device,0x20000005,prep,ref prepSize)==uint.MaxValue)return;
                    for(int i=0;i<count;i++) { byte[] report=new byte[bytes];Marshal.Copy(IntPtr.Add(ptr,offset+8+i*bytes),report,0,bytes);ushort[] usages=new ushort[128];uint num=128;
                        int result=Native.HidP_GetUsages(0,12,0,usages,ref num,prep,report,(uint)bytes);
                        string desc=result>=0?string.Join(",",usages.Take((int)num).Select(x=>"0x"+x.ToString("X4"))):"parse=0x"+result.ToString("X8");
                        string device=DeviceName(header.device);
                        if(Recording)Note("HID consumer="+desc+" report="+BitConverter.ToString(report)+" device="+device);
                        if(result>=0) {
                            var pressed=hidEdges.Update(device+":"+report[0],usages.Take((int)num));
                            if(enable.Checked && !Recording)foreach(ushort usage in pressed.Where(u=>u!=0x6f && u!=0x70)) {
                                var binding=bindings.FirstOrDefault(b=>b.Verified && b.HidUsage==usage && b.Target>0 && !string.IsNullOrEmpty(b.DeviceMatch) && device.IndexOf(b.DeviceMatch,StringComparison.OrdinalIgnoreCase)>=0);
                                if(binding!=null)SendChord(binding);
                            }
                        }
                    }
                } finally { Marshal.FreeHGlobal(prep); }
            }
        } finally { Marshal.FreeHGlobal(ptr); }
    }
    string DeviceName(IntPtr device) {
        uint size=0;Native.GetRawInputDeviceInfo(device,0x20000007,IntPtr.Zero,ref size);if(size==0||size>8192)return "unknown";
        IntPtr ptr=Marshal.AllocHGlobal((int)(size+1)*2);try { if(Native.GetRawInputDeviceInfo(device,0x20000007,ptr,ref size)==uint.MaxValue)return "unknown";return Marshal.PtrToStringUni(ptr); }finally{Marshal.FreeHGlobal(ptr);}
    }
    void Note(string message) { string line=DateTime.Now.ToString("HH:mm:ss.fff")+" "+message+Environment.NewLine; if(log!=null) { if(log.TextLength>60000)log.Clear();log.AppendText(line); } try { File.AppendAllText(reportPath,line); }catch{} }
    void ReadPower() { try { if(!power.Ready)power.Open();else power.Refresh(); ShowPower("Значения MSR прочитаны; MMIO-ограничения отдельно не проверены.");PowerLog("read"); }catch(Exception ex) { applyPower.Enabled=false;powerStatus.Text=ex.Message;PowerLog("unavailable: "+ex.Message); } }
    void ShowPower(string text) {
        bool usable=power.Ready&&power.Enabled1&&power.Enabled2;
        int[] low={6,10,15},high={8,13,15};int selected=-1;
        for(int i=0;i<powerTiles.Count;i++) {
            var tile=powerTiles[i];tile.Marked=usable&&power.PL1==low[i]&&power.PL2==high[i];
            if(tile.Marked)selected=i;
            tile.Enabled=usable&&!power.Locked;
            tile.AccessibleName=tile.Title+", "+tile.Detail+(tile.Marked?", активен":"");tile.Invalidate();
        }
        if(homePower!=null)homePower.Text=(selected>=0?"Активен: "+powerTiles[selected].Title:usable?"Другие лимиты (BIOS / вручную)":"Лимиты отключены")+" · PL1 / PL2: "+power.PL1.ToString("0.#")+" / "+power.PL2.ToString("0.#")+" Вт"+(power.Locked?" · Заблокировано BIOS":"");
        powerStatus.Text=string.Format("PL1: {0:0.##} Вт ({1}) · PL2: {2:0.##} Вт ({3})\nБлокировка BIOS: {4}. {5}",power.PL1,power.Enabled1?"включён":"выключен",power.PL2,power.Enabled2?"включён":"выключен",power.Locked?"да":"нет",text);
        applyPower.Enabled=usable&&!power.Locked;
    }
    void PowerLog(string action) { File.AppendAllText(Path.Combine(root,"data","power-diagnostic.log"),DateTime.Now.ToString("s")+" "+action+" current=0x"+power.Current.ToString("X16")+" original=0x"+power.Original.ToString("X16")+" unit="+power.Unit+Environment.NewLine); }
    void Error(Exception ex) { MessageBox.Show(this,ex.Message,"gpdt",MessageBoxButtons.OK,MessageBoxIcon.Information); }
    void SendChord(Binding b) {
        var keys=new List<int>();if(b.Ctrl&&!IsDown(17))keys.Add(17);if(b.Alt&&!IsDown(18))keys.Add(18);if(b.Shift&&!IsDown(16))keys.Add(16);if(b.Win&&!IsDown(91))keys.Add(91);keys.Add(b.Target);
        var inputs=new List<Native.Input>();
        foreach(int key in keys)inputs.Add(ChordInput(key,false));
        for(int i=keys.Count-1;i>=0;i--)inputs.Add(ChordInput(keys[i],true));
        uint sent=Native.SendInput((uint)inputs.Count,inputs.ToArray(),Marshal.SizeOf(typeof(Native.Input)));
        if(sent!=inputs.Count){for(int i=keys.Count-1;i>=0;i--)Send(keys[i],true);enable.Checked=false;status.Text="Windows отклонила сочетание. Проверьте права окна назначения.";}
    }
    static Native.Input ChordInput(int key,bool up) { bool ext=key==45||key==46||key==44||(key>=33&&key<=40)||key==91||key==92||(key>=173&&key<=175);return new Native.Input{type=1,data=new Native.InputUnion{key=new Native.KeyboardInput{vk=(ushort)key,flags=(up?2u:0u)|(ext?1u:0u)}}}; }
    string Code(Binding b) { if(b.HidUsage!=0)return "HID "+b.HidUsage.ToString("X4")+" ✓ · через фильтр яркости";return b.Verified?string.Format("VK {0:X2} · scan {1:X2}{2} ✓",b.Vk,b.Scan,b.Extended?" E0":""):"Нажмите «Определить кнопку»"; }
    string ActionName(Binding b) { if(b.Target==0)return "Без изменения";if(b.Target<0)return "Отключена";return (b.Ctrl?"Ctrl+":"")+(b.Alt?"Alt+":"")+(b.Shift?"Shift+":"")+(b.Win?"Win+":"")+((Keys)b.Target); }
    void RefreshGrid() { int selected=grid.CurrentRow==null?0:grid.CurrentRow.Index;grid.Rows.Clear();foreach(var b in bindings)grid.Rows.Add(b.Name,Code(b),ActionName(b)); if(grid.Rows.Count>selected)grid.CurrentCell=grid.Rows[selected].Cells[0]; RefreshTiles(); }
    void SelectRow() { if(target==null||grid.CurrentRow==null)return;var b=bindings[grid.CurrentRow.Index];if(keyPicker!=null && keyPicker.SelectedIndex!=grid.CurrentRow.Index)keyPicker.SelectedIndex=grid.CurrentRow.Index;target.SelectedIndex=0;for(int i=0;i<target.Items.Count;i++)if(((KeyChoice)target.Items[i]).Key==b.Target)target.SelectedIndex=i;ctrl.Checked=b.Ctrl;alt.Checked=b.Alt;shift.Checked=b.Shift;win.Checked=b.Win; }
    void SaveSelected() { if(grid.CurrentRow==null||target.SelectedItem==null)return;var b=bindings[grid.CurrentRow.Index];int key=((KeyChoice)target.SelectedItem).Key;if(key!=0&&!b.Verified){Error(new Exception("Сначала определите физическую кнопку."));return;}ReleaseAll();b.Target=key;b.Ctrl=ctrl.Checked;b.Alt=alt.Checked;b.Shift=shift.Checked;b.Win=win.Checked;SaveBindings();RefreshGrid();if(enable.Checked)ConfigureFilter();else enable.Checked=true;status.Text="Назначение сохранено и включено: "+b.Name+" → "+ActionName(b); }
    string ResetBackup { get { return Path.Combine(root,"data","bindings-before-reset.xml"); } }
    void ResetBindings(bool all) {
        if(grid.CurrentRow==null)return;
        var selected=bindings[grid.CurrentRow.Index];
        string message=all?"Сбросить ВСЕ назначения? Все кнопки вернутся к обычным действиям Windows.":"Сбросить только «"+selected.Name+"» ("+ActionName(selected)+")?";
        if(!all && (selected.HidUsage==111||selected.HidUsage==112))message+="\n\nФильтр работает для пары кнопок: пока одной из них не назначено действие, обе кнопки будут менять яркость.";
        message+="\n\nСброс можно отменить через «Ещё → Отменить последний сброс».";
        if(MessageBox.Show(this,message,"gpdt — подтверждение сброса",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        try {
            using(var stream=File.Create(ResetBackup+".tmp"))new XmlSerializer(typeof(List<Binding>)).Serialize(stream,bindings);
            if(File.Exists(ResetBackup))File.Replace(ResetBackup+".tmp",ResetBackup,null);else File.Move(ResetBackup+".tmp",ResetBackup);
            ReleaseAll();
            foreach(var b in bindings)if(all||b==selected){b.Target=0;b.Ctrl=b.Alt=b.Shift=b.Win=false;}
            SaveBindings();RefreshGrid();SelectRow();ConfigureFilter();
            status.Text=all?"Все назначения сброшены. Отмена доступна в меню «Ещё».":"Сброшена только кнопка «"+selected.Name+"». Отмена доступна в меню «Ещё».";
        }catch(Exception ex){Error(ex);}
    }
    void UndoReset() {
        if(!File.Exists(ResetBackup)){MessageBox.Show(this,"Нет сохранённого сброса для отмены.","gpdt");return;}
        try {
            List<Binding> previous;
            using(var stream=File.OpenRead(ResetBackup))previous=(List<Binding>)new XmlSerializer(typeof(List<Binding>)).Deserialize(stream);
            if(previous.Count!=bindings.Count||bindings.Any(b=>previous.Count(p=>p.Name==b.Name)!=1))throw new Exception("Резервная копия назначений повреждена.");
            if(MessageBox.Show(this,"Вернуть назначения перед последним сбросом? Более поздние изменения назначений будут заменены.","gpdt — отмена сброса",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            ReleaseAll();
            foreach(var b in bindings){var p=previous.First(x=>x.Name==b.Name);b.Target=p.Target;b.Ctrl=p.Ctrl;b.Alt=p.Alt;b.Shift=p.Shift;b.Win=p.Win;}
            SaveBindings();RefreshGrid();SelectRow();if(enable.Checked)ConfigureFilter();else enable.Checked=true;
            status.Text="Назначения восстановлены и включены.";
        }catch(Exception ex){Error(ex);}
    }
    void SaveBindings() { string path=Path.Combine(root,"data","bindings.xml"),temp=path+".tmp";using(var stream=File.Create(temp))new XmlSerializer(typeof(List<Binding>)).Serialize(stream,bindings);if(File.Exists(path)){string history=Path.Combine(root,"data","binding-history");Directory.CreateDirectory(history);File.Copy(path,Path.Combine(history,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".xml"));File.Replace(temp,path,path+".bak");}else File.Move(temp,path); }
    void LoadBindings() { try { using(var stream=File.OpenRead(Path.Combine(root,"data","bindings.xml")))bindings=(List<Binding>)new XmlSerializer(typeof(List<Binding>)).Deserialize(stream);if(bindings.Count!=9)throw new Exception(); } catch { bindings=new List<Binding>();string[] names={"Esc","Яркость −","Яркость +","Без звука","Громкость −","Громкость +","Insert","Print Screen","Delete"};foreach(string name in names)bindings.Add(new Binding{Name=name}); } }
    sealed class KeyChoice { public int Key;string name;public KeyChoice(int k,string n){Key=k;name=n;}public override string ToString(){return name;} }
}

internal sealed class PageHost : Panel {
    int selected;
    public int SelectedIndex { get { return selected; } set { selected=value;for(int i=0;i<Controls.Count;i++)Controls[i].Visible=i==value; } }
}

internal sealed class DashboardButton : Button {
    public string Title="",Detail="";
    public bool Marked,Compact,IsPower;
    bool hover,pressed;
    public DashboardButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;TabStop=true; }
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent==null?Color.FromArgb(17,18,20):Parent.BackColor);
        float scale=g.DpiX/96f;var rect=new Rectangle(1,1,Width-3,Height-3);int r=(int)(12*scale);
        using(var shape=new System.Drawing.Drawing2D.GraphicsPath()) {
            shape.AddArc(rect.Left,rect.Top,r,r,180,90);shape.AddArc(rect.Right-r,rect.Top,r,r,270,90);shape.AddArc(rect.Right-r,rect.Bottom-r,r,r,0,90);shape.AddArc(rect.Left,rect.Bottom-r,r,r,90,90);shape.CloseFigure();
            using(var fill=new SolidBrush(Color.FromArgb(pressed?43:hover?37:27,pressed?46:hover?40:29,pressed?50:hover?44:32)))g.FillPath(fill,shape);
            using(var pen=new Pen(Focused?Color.White:Marked?Color.FromArgb(190,217,201):Color.FromArgb(72,77,84),Focused||Marked?2:1))g.DrawPath(pen,shape);
        }
        int offset=pressed?1:0;int pad=(int)((Compact?8:16)*scale);
        int top=14+offset, line=32;
        Color ink=Enabled?Color.White:Color.FromArgb(145,150,158);
        using(var title=new Font("Segoe UI",Compact?10:11,FontStyle.Bold))TextRenderer.DrawText(g,Title,title,new Rectangle(pad,top,Width-pad*2,line),ink,TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
        string detailText=Detail+(IsPower&&Marked?"   ✓ Активен":"");
        using(var detail=new Font("Segoe UI",10))TextRenderer.DrawText(g,detailText,detail,new Rectangle(pad,Height-34+offset,Width-pad*2,line),Marked?Color.FromArgb(221,240,228):Color.FromArgb(171,177,185),TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
        if(Marked)using(var dot=new SolidBrush(Color.FromArgb(200,224,208)))g.FillEllipse(dot,Width-14*scale,7*scale,5*scale,5*scale);
        if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(7,7,Width-14,Height-14),Color.White,Color.FromArgb(27,29,32));
    }
}

internal static class Program {
    [STAThread] static void Main(string[] args) {
        if(args.Contains("--self-test")) { SelfTest();return; }
        if(args.Contains("--power-probe")) { Probe();return; }
        if(args.Contains("--power-test")) { TestPower();return; }
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Native.SetProcessDPIAware();
        using(var single=new System.Threading.Mutex(false,@"Local\gpdt-application")) {
            bool acquired=false;
            try { try { acquired=single.WaitOne(args.Contains("--elevated")?5000:0); }catch(System.Threading.AbandonedMutexException){acquired=true;}
                if(!acquired){MessageBox.Show("gpdt уже работает. Откройте окно через значок в трее.","gpdt");return;}
                Application.Run(new MainForm(args.Contains("--enable"),args.Contains("--tray")));
            }catch(Exception ex){MessageBox.Show(ex.ToString(),"gpdt — ошибка запуска");}
            finally { if(acquired)single.ReleaseMutex(); }
        }
    }
    static void Probe() {
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","power-probe.txt");
        using(var p=new Power())try { p.Open();File.WriteAllText(path,string.Format("READ OK\r\nPL1={0}\r\nPL2={1}\r\nEnabled1={2}\r\nEnabled2={3}\r\nLocked={4}\r\nMSR=0x{5:X16}\r\nUnit={6}\r\nNo writes performed.",p.PL1,p.PL2,p.Enabled1,p.Enabled2,p.Locked,p.Current,p.Unit)); }
        catch(Exception ex){File.WriteAllText(path,"READ FAILED\r\n"+ex.Message+"\r\nNo writes performed.");}
    }
    static void TestPower() {
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","power-write-test.txt");
        using(var p=new Power()) {
            bool opened=false;
            try {
                p.Open();opened=true;
                File.WriteAllText(path,"Original=0x"+p.Original.ToString("X16")+Environment.NewLine+"Original PL1="+p.PL1+" PL2="+p.PL2+Environment.NewLine);
                if(p.PL1!=10 || p.PL2!=13)throw new InvalidOperationException("Test expects the measured 10/13 W baseline; no test write performed.");
                p.Apply(9,13);System.Threading.Thread.Sleep(2000);p.Refresh();
                File.AppendAllText(path,"After 2s: PL1="+p.PL1+" PL2="+p.PL2+" MSR=0x"+p.Current.ToString("X16")+Environment.NewLine);
                if(p.PL1!=9 || p.PL2!=13)throw new IOException("Power limits did not persist.");
                File.AppendAllText(path,"WRITE VERIFIED"+Environment.NewLine);
            }catch(Exception ex){File.AppendAllText(path,"TEST ERROR: "+ex.Message+Environment.NewLine);}
            finally { if(opened)try { p.Restore();File.AppendAllText(path,"RESTORE VERIFIED: 0x"+p.Current.ToString("X16")+Environment.NewLine); }catch(Exception ex){File.AppendAllText(path,"RESTORE FAILED: "+ex.Message+Environment.NewLine);} }
        }
    }
    static void SelfTest() { Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"));
        ulong old=0x0043800000dd8000UL;
        ulong value=Power.WithLimits(old,0.125,6,8);
        ulong mask=0x7fffUL|(0x7fffUL<<32);
        if((value&~mask)!=(old&~mask))throw new Exception("Control bits changed");
        if((value&0x7fff)!=48 || ((value>>32)&0x7fff)!=64)throw new Exception("Invalid encoding");
        bool rejected=false;try{Power.WithLimits(old,0.125,16,16);}catch(ArgumentOutOfRangeException){rejected=true;}if(!rejected)throw new Exception("Range check missing");
        rejected=false;try{Power.WithLimits(old,0.125,12,8);}catch(ArgumentOutOfRangeException){rejected=true;}if(!rejected)throw new Exception("Order check missing");
        if(Marshal.SizeOf(typeof(Native.Input))!=40)throw new Exception("INPUT ABI mismatch");
        if(Marshal.SizeOf(typeof(Native.RawHeader))!=24)throw new Exception("RAWINPUTHEADER ABI mismatch");
        var edges=new HidEdges();
        if(edges.Update("a",new ushort[]{0x70}).Length!=1)throw new Exception("Missing HID down");
        if(edges.Update("a",new ushort[]{0x70}).Length!=0)throw new Exception("Duplicate HID action");
        if(edges.Update("b",new ushort[]{0x70}).Length!=1)throw new Exception("Devices not independent");
        if(edges.Update("a",new ushort[]{0}).Length!=0)throw new Exception("Action on release");
        if(edges.Update("a",new ushort[]{0x70}).Length!=1)throw new Exception("Second press lost");
        edges.Clear();if(edges.Update("a",new ushort[]{0x70}).Length!=1)throw new Exception("Reset failed");
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","self-test.txt"),"PASS: power field preservation, power units, range/order validation, x64 INPUT/RAWINPUT ABI; HID down/repeat/up/second press/device isolation/reset.");
    }
}
