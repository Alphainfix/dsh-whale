using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshWhale {
    static class Style {
        public static readonly float Scale = GetScale();
        static float GetScale() { using(var g=Graphics.FromHwnd(IntPtr.Zero))return g.DpiX/96f; }
        public static int P(double n) { return (int)Math.Round(n * Scale); }
        public static Font Font(float px, bool bold) { return new Font("Microsoft YaHei UI", px * .75f, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static GraphicsPath Round(RectangleF r, float radius) {
            float d = radius * 2; var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right-d,r.Y,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
        }
        public static Label Label(string text, int x, int y, int width, int height, float size, bool bold, Color color) {
            return new Label { Text=text, Bounds=new Rectangle(P(x),P(y),P(width),P(height)), Font=Font(size,bold), ForeColor=color, BackColor=Color.Transparent };
        }
    }
    class SoftButton : Button {
        public bool Link, Primary;
        bool hover;
        public SoftButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); Cursor=Cursors.Hand; Font=Style.Font(13,false); FlatStyle=FlatStyle.Flat; TabStop=true; }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            Color fg=Enabled ? (Primary ? Color.White : Link ? Color.FromArgb(90,90,90) : Color.Black) : Color.FromArgb(170,170,170);
            if (!Link) {
                using(var p=Style.Round(new RectangleF(1,1,Width-3,Height-3),Style.P(6))) {
                    using(var fill=new SolidBrush(Primary ? Color.FromArgb(0,122,255) : hover ? Color.FromArgb(243,243,245) : Color.White)) e.Graphics.FillPath(fill,p);
                    using(var border=new Pen(Primary ? Color.FromArgb(0,110,240) : Color.FromArgb(220,220,223))) e.Graphics.DrawPath(border,p);
                }
            }
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,fg,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
        }
    }
    class StatusLine : Control {
        readonly string title,value,health; readonly bool emphasis;
        public StatusLine(string title,string value,string health,bool emphasis) {
            this.title=title;this.value=value;this.health=health;this.emphasis=emphasis;
            Size=new Size(Style.P(260),Style.P(22)); Margin=new Padding(0,0,0,Style.P(4)); SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            AccessibleName=title+" "+value;
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            Color color=health=="green"?Color.FromArgb(47,199,89):health=="yellow"?Color.FromArgb(245,186,40):Color.FromArgb(240,73,69);
            using(var b=new SolidBrush(Color.FromArgb(38,color))) g.FillEllipse(b,0,Style.P(4),Style.P(14),Style.P(14));
            using(var b=new SolidBrush(color)) g.FillEllipse(b,Style.P(4),Style.P(8),Style.P(6),Style.P(6));
            using(var f=Style.Font(13,true)) TextRenderer.DrawText(g,title,f,new Rectangle(Style.P(23),0,Width-Style.P(99),Height),Color.Black,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            using(var f=Style.Font(13,emphasis)) TextRenderer.DrawText(g,value,f,new Rectangle(Width-Style.P(80),0,Style.P(80),Height),emphasis?Color.Black:Color.FromArgb(90,90,90),TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
        }
    }
    public class SettingsForm : Form {
        public event Action LoginRequested;
        public event Action<Settings> Saved;
        readonly Settings settings;
        readonly bool demo;
        List<SourceSetting> entries;
        Dictionary<string,Reading> readings;
        bool reachable, loading;
        string balanceProblem="";
        readonly FlowLayoutPanel list;
        readonly Label notice, hint;
        readonly CheckBox claw;
        readonly ComboBox permission;
        readonly Panel rest;
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,string text);
        public SettingsForm(Settings current, Dictionary<string,Reading> available, bool demo) {
            this.demo=demo;
            settings=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(current));
            readings=available; reachable=demo; entries=Balances.Layout(settings.Sources,available);
            Text="小鲸鱼设置"; BackColor=Color.White; Font=Style.Font(13,false); AutoScaleMode=AutoScaleMode.None;
            FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen;
            Controls.Add(Style.Label("余额显示",20,18,420,24,14,true,Color.Black));
            Controls.Add(Style.Label("列表来自 DSH 的余额接口。勾选的会显示在面板上，名字可以直接改，右边的箭头调整顺序。",20,48,420,42,12,false,Color.Gray));
            notice=Style.Label("",20,94,420,42,12,false,Color.FromArgb(180,100,20)); Controls.Add(notice);
            list=new FlowLayoutPanel { Location=new Point(Style.P(20),Style.P(94)), Width=Style.P(420), FlowDirection=FlowDirection.TopDown, WrapContents=false, AutoScroll=true, BackColor=Color.FromArgb(251,251,252), Padding=new Padding(Style.P(5)) }; Controls.Add(list);
            rest=new Panel { Left=Style.P(20),Width=Style.P(420),Height=Style.P(190),BackColor=Color.White }; Controls.Add(rest);
            var reset=Button("恢复默认",0,10,76,24,delegate { settings.Sources=null; entries=Balances.Layout(null,readings); Commit(false); Rebuild(); }); rest.Controls.Add(reset);
            rest.Controls.Add(new Panel { Bounds=new Rectangle(0,Style.P(46),Style.P(420),1),BackColor=Color.FromArgb(225,225,225) });
            rest.Controls.Add(Style.Label("其他",0,62,420,22,14,true,Color.Black));
            claw=new CheckBox { Text="显示微信 Clawbot 状态",Checked=settings.ShowClawbot,Bounds=new Rectangle(0,Style.P(91),Style.P(420),Style.P(25)),Font=Style.Font(13,false) }; rest.Controls.Add(claw);
            rest.Controls.Add(Style.Label("启动 DSH 的权限模式",0,126,137,25,12,false,Color.FromArgb(60,60,60)));
            permission=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(Style.P(137),Style.P(122),Style.P(283),Style.P(27)),Font=Style.Font(12,false) };
            permission.Items.AddRange(new object[]{"跟随 DSH 默认(需要审批)","完全访问(不弹审批)"}); permission.SelectedIndex=settings.FullAccess?1:0; rest.Controls.Add(permission);
            hint=Style.Label("只影响小鲸鱼下一次拉起 DSH；想马上生效，在面板里点「重启服务」。",0,160,420,36,11,false,Color.Gray); rest.Controls.Add(hint);
            var advanced=Button("Windows 连接与启动选项…",20,0,210,22,delegate { using(var form=new WindowsOptions(settings,demo)) { if(form.ShowDialog(this)==DialogResult.OK) Commit(false); } }); advanced.Name="advanced";advanced.Link=true;Controls.Add(advanced);
            var login=Button("登录 ChatGPT…",285,0,155,22,delegate{if(LoginRequested!=null)LoginRequested();});login.Name="chatgpt";login.Link=true;Controls.Add(login);
            claw.CheckedChanged+=delegate { if(!loading) {settings.ShowClawbot=claw.Checked;Commit(false);} };
            permission.SelectedIndexChanged+=delegate { if(!loading) {settings.FullAccess=permission.SelectedIndex==1;Commit(false);} };
            Rebuild();
        }
        static SoftButton Button(string text,int x,int y,int w,int h,EventHandler action) { var b=new SoftButton { Text=text,Bounds=new Rectangle(Style.P(x),Style.P(y),Style.P(w),Style.P(h)) }; b.Click+=action;return b; }
        void Commit(bool sources) { if(sources) settings.Sources=entries.Select(x=>new SourceSetting{Id=x.Id,Title=x.Title,Visible=x.Visible}).ToList(); if(Saved!=null) Saved(settings); }
        void Rebuild() {
            loading=true; list.SuspendLayout(); foreach(Control c in list.Controls.Cast<Control>().ToArray()) c.Dispose();list.Controls.Clear();
            notice.Text=String.IsNullOrEmpty(balanceProblem)?"现在读不到余额接口，下面保留上次保存的设置。":balanceProblem;
            notice.Visible=!reachable;list.Top=Style.P(reachable?94:142);
            if(entries.Count==0) list.Controls.Add(Style.Label("还没有可显示的余额源。",0,0,395,40,12,false,Color.Gray));
            for(int i=0;i<entries.Count;i++) {
                var item=entries[i];int index=i;Reading r;readings.TryGetValue(item.Id,out r);
                var row=new Panel{Width=Style.P(404),Height=Style.P(44),Margin=new Padding(0),BackColor=list.BackColor};
                var check=new CheckBox{Checked=item.Visible,Bounds=new Rectangle(Style.P(4),Style.P(13),Style.P(21),Style.P(22))}; row.Controls.Add(check);
                var name=new TextBox{Text=item.Title??"",BorderStyle=BorderStyle.None,BackColor=row.BackColor,Font=Style.Font(13,true),Bounds=new Rectangle(Style.P(29),Style.P(6),Style.P(290),Style.P(20))};row.Controls.Add(name);
                string fallback=r==null?item.Id:r.DefaultTitle; SendMessage(name.Handle,0x1501,IntPtr.Zero,fallback);
                var detail=Style.Label(Describe(item.Id,r),29,26,300,17,10,false,Color.Gray);detail.Name="detail";row.Tag=item.Id;row.Controls.Add(detail);
                var up=Button("⌃",345,10,25,25,delegate{MoveSource(index,-1);});up.Link=true;up.Enabled=i>0;row.Controls.Add(up);
                var down=Button("⌄",373,10,25,25,delegate{MoveSource(index,1);});down.Link=true;down.Enabled=i<entries.Count-1;row.Controls.Add(down);
                if(reachable && r==null) {var remove=Button("⊖",318,10,24,25,delegate{entries.Remove(item);Commit(true);Rebuild();});remove.Link=true;row.Controls.Add(remove);}
                check.CheckedChanged+=delegate{if(!loading){item.Visible=check.Checked;Commit(true);}};
                name.TextChanged+=delegate{if(!loading){item.Title=name.Text;Commit(true);}};
                if(i<entries.Count-1) row.Controls.Add(new Panel{Bounds=new Rectangle(0,Style.P(43),Style.P(404),1),BackColor=Color.FromArgb(225,225,225)});
                list.Controls.Add(row);
            }
            list.Height=Style.P(Math.Min(280,Math.Max(44,entries.Count*44)+10));list.ResumeLayout();
            rest.Top=list.Bottom+Style.P(2);Controls["advanced"].Top=rest.Bottom+Style.P(7);
            Controls["chatgpt"].Top=Controls["advanced"].Top;
            ClientSize=new Size(Style.P(460),Controls["advanced"].Bottom+Style.P(16));loading=false;
        }
        static string Describe(string id,Reading r){return r==null?id:id+" · "+(!String.IsNullOrEmpty(r.Error)?r.Error:(r.Kind=="quota"?"额度":r.Kind=="prepaid"?"预付":"余额")+" · "+r.Text);}
        void MoveSource(int index,int offset){int next=index+offset;if(next<0||next>=entries.Count)return;var x=entries[index];entries[index]=entries[next];entries[next]=x;Commit(true);Rebuild();}
        public void UpdateReadings(Dictionary<string,Reading> values,bool available){UpdateReadings(values,available,"");}
        public void UpdateReadings(Dictionary<string,Reading> values,bool available,string problem) {
            bool changed=reachable!=available || balanceProblem!=problem; readings=values;reachable=available;balanceProblem=problem;
            var merged=Balances.Layout(settings.Sources==null?null:entries,values);
            if(!merged.Select(x=>x.Id).SequenceEqual(entries.Select(x=>x.Id))) {entries=merged;changed=true;}
            if(changed && !list.ContainsFocus) {Rebuild();return;}
            foreach(Control row in list.Controls) {string id=row.Tag as string;if(id==null)continue;Reading r;values.TryGetValue(id,out r);row.Controls["detail"].Text=Describe(id,r);}
        }
    }
    class WindowsOptions : Form {
        public WindowsOptions(Settings settings,bool demo) {
            Text="Windows 连接与启动选项";BackColor=Color.White;Font=Style.Font(12,false);AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(Style.P(490),Style.P(300));FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
            string[] labels={"Node.js 路径","DSH JavaScript 入口","工作目录"};string[] values={settings.NodePath,settings.DshPath,settings.WorkingDirectory};var boxes=new List<TextBox>();
            for(int i=0;i<3;i++){Controls.Add(Style.Label(labels[i],20,20+i*55,450,20,12,false,Color.Black));var t=new TextBox{Text=values[i],Bounds=new Rectangle(Style.P(20),Style.P(42+i*55),Style.P(450),Style.P(25))};Controls.Add(t);boxes.Add(t);}
            var app=new CheckBox{Text="用 Edge 独立窗口打开 DSH",Checked=settings.AppWindow,Bounds=new Rectangle(Style.P(20),Style.P(195),Style.P(450),Style.P(25))};Controls.Add(app);
            var login=new CheckBox{Text="登录 Windows 时启动小鲸鱼",Bounds=new Rectangle(Style.P(20),Style.P(223),Style.P(450),Style.P(25)),Enabled=!demo};Controls.Add(login);
            const string keyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";using(var key=Registry.CurrentUser.OpenSubKey(keyPath))login.Checked=key!=null&&key.GetValue("DSHWhale-Vibe")!=null;
            var save=new SoftButton{Text="保存",Bounds=new Rectangle(Style.P(390),Style.P(260),Style.P(80),Style.P(26))};Controls.Add(save);
            save.Click+=delegate{try{string node=boxes[0].Text.Trim(),entry=boxes[1].Text.Trim(),cwd=boxes[2].Text.Trim();if(node.Length>0&&!File.Exists(node))throw new IOException("Node.js 路径不存在");if(entry.Length>0&&!File.Exists(entry))throw new IOException("DSH 入口不存在");if(cwd.Length>0&&!Directory.Exists(cwd))throw new IOException("工作目录不存在");settings.NodePath=node;settings.DshPath=entry;settings.WorkingDirectory=cwd;settings.AppWindow=app.Checked;if(!demo)using(var key=Registry.CurrentUser.CreateSubKey(keyPath)){if(login.Checked)key.SetValue("DSHWhale-Vibe",Native.Quote(Application.ExecutablePath));else key.DeleteValue("DSHWhale-Vibe",false);}DialogResult=DialogResult.OK;Close();}catch(Exception e){MessageBox.Show(e.Message,"保存失败");}};
        }
    }
}
