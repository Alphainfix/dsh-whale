using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshWhale {
    static class Style {
        public static readonly float Scale = GetScale();
        // Windows 11 rounds, outlines and shadows the panel itself (DWM); earlier versions get a square outline.
        public static readonly bool Win11 = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
        // A light grey ground like Windows 11 flyouts, with the status rows on a white card, so the panel
        // stands apart from white windows behind it.
        public static readonly Color Back = Color.FromArgb(243, 243, 243);
        public static readonly Color Card = Color.White;
        public static readonly Color CardEdge = Color.FromArgb(229, 229, 229);
        public static readonly Color Edge = Color.FromArgb(196, 196, 196);
        public static readonly Color Text = Color.FromArgb(26, 26, 26);
        public static readonly Color SubText = Color.FromArgb(96, 96, 96);
        public static readonly Color Muted = Color.FromArgb(138, 138, 138);
        public static readonly Color Line = Color.FromArgb(236, 236, 236);
        public static readonly Color Hover = Color.FromArgb(232, 232, 232);
        public static readonly Color Accent = SystemAccent();
        static float GetScale() { using(var g=Graphics.FromHwnd(IntPtr.Zero))return g.DpiX/96f; }
        public static int P(double n) { return (int)Math.Round(n * Scale); }
        public static Font Font(float px, bool bold) { return new Font("Microsoft YaHei UI", px * .75f, bold ? FontStyle.Bold : FontStyle.Regular); }
        // The accent colour picked in Windows settings (DWM keeps it as 0xAABBGGRR), darkened when white text would not read on it.
        static Color SystemAccent() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM")) {
                    object value = key == null ? null : key.GetValue("AccentColor");
                    if (value is int) {
                        int abgr = (int)value;
                        var c = Color.FromArgb(abgr & 255, (abgr >> 8) & 255, (abgr >> 16) & 255);
                        return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255 > 0.45 ? Mix(c, Color.Black, 0.35) : c;
                    }
                }
            } catch { }
            return Color.FromArgb(0, 95, 184);
        }
        public static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        public static GraphicsPath Round(RectangleF r, float radius) {
            float d = radius * 2; var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right-d,r.Y,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
        }
        public static Label Label(string text, int x, int y, int width, int height, float size, bool bold, Color color) {
            return new Label { Text=text, Bounds=new Rectangle(P(x),P(y),P(width),P(height)), Font=Font(size,bold), ForeColor=color, BackColor=Color.Transparent };
        }
        // Shrinks artwork for small sizes the way the Mac's 1x/2x assets look: halve while it is more than twice
        // too big, then one high-quality bicubic pass, so thin parts stay smooth instead of jagged.
        public static Bitmap Scaled(Image source, int size) {
            Image current = source; bool owned = false;
            while (current.Width / 2 >= size * 2) {
                var half = new Bitmap(current.Width / 2, current.Height / 2, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(half)) { Quality(g); g.DrawImage(current, 0, 0, half.Width, half.Height); }
                if (owned) current.Dispose();
                current = half; owned = true;
            }
            var result = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(result)) { Quality(g); g.DrawImage(current, 0, 0, size, size); }
            if (owned) current.Dispose();
            return result;
        }
        static void Quality(Graphics g) { g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.CompositingQuality=CompositingQuality.HighQuality; g.SmoothingMode=SmoothingMode.AntiAlias; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        // Windows 11 only: native rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE) and a visible outline (DWMWA_BORDER_COLOR).
        public static void RoundCorners(IntPtr hwnd) {
            if (!Win11) return;
            try { int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4); int border = Edge.R | (Edge.G << 8) | (Edge.B << 16); DwmSetWindowAttribute(hwnd, 34, ref border, 4); } catch { }
        }
    }
    // Windows 11 style push button; Link draws text only. The arrow cursor is kept, as in native Windows buttons.
    class SoftButton : Button {
        public bool Link, Primary;
        public TextFormatFlags Align = TextFormatFlags.HorizontalCenter;
        bool hover, pressed;
        public SoftButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); Font=Style.Font(13,false); FlatStyle=FlatStyle.Flat; TabStop=true; }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left){pressed=true;Invalidate();} base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Parent == null ? Style.Back : Parent.BackColor);
            Color fg;
            if (Link) fg = !Enabled || pressed ? Style.Muted : hover ? Style.Text : Style.SubText;
            else {
                Color fill, edge;
                if (!Enabled) { fill=Color.FromArgb(249,249,249); edge=Color.FromArgb(232,232,232); fg=Color.FromArgb(160,160,160); }
                else if (Primary) { fill=pressed?Style.Mix(Style.Accent,Color.Black,.12):hover?Style.Mix(Style.Accent,Color.White,.10):Style.Accent; edge=Style.Mix(fill,Color.Black,.10); fg=Color.White; }
                else { fill=pressed?Color.FromArgb(240,240,240):hover?Color.FromArgb(249,249,249):Color.White; edge=hover?Color.FromArgb(208,208,208):Color.FromArgb(220,220,220); fg=pressed?Style.SubText:Style.Text; }
                using(var path=Style.Round(new RectangleF(.5f,.5f,Width-1.5f,Height-1.5f),Style.P(5))) {
                    using(var b=new SolidBrush(fill)) g.FillPath(b,path);
                    using(var p=new Pen(edge)) g.DrawPath(p,path);
                }
            }
            TextRenderer.DrawText(g,Text,Font,ClientRectangle,fg,Align|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            if(Focused && ShowFocusCues) using(var path=Style.Round(new RectangleF(1.5f,1.5f,Width-3.5f,Height-3.5f),Style.P(4))) using(var p=new Pen(Style.Text,Style.P(1.5))) g.DrawPath(p,path);
        }
    }
    // One status row on the panel's card: ● title …… value. Updated in place, so a refresh never flickers.
    class StatusLine : Control {
        string title="", value="", health="gray"; bool emphasis;
        public StatusLine() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); BackColor=Style.Card; Height=Style.P(30); }
        public void Set(string title,string value,string health,bool emphasis) {
            if(title==this.title&&value==this.value&&health==this.health&&emphasis==this.emphasis)return;
            this.title=title;this.value=value;this.health=health;this.emphasis=emphasis;AccessibleName=title+" "+value;Invalidate();
        }
        public static Color Dot(string health) { return health=="green"?Color.FromArgb(47,199,89):health=="yellow"?Color.FromArgb(245,166,35):health=="gray"?Color.FromArgb(174,174,178):Color.FromArgb(240,73,69); }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            Color color=Dot(health); int d=Style.P(14),c=Style.P(6),x=Style.P(10),cy=Height/2,right=Style.P(12),titleLeft=x+d+Style.P(8);
            using(var b=new SolidBrush(Color.FromArgb(48,color))) g.FillEllipse(b,x,cy-d/2,d,d);
            using(var b=new SolidBrush(color)) g.FillEllipse(b,x+(d-c)/2,cy-c/2,c,c);
            using(var titleFont=Style.Font(13,false)) using(var valueFont=Style.Font(13,emphasis)) {
                // The value takes the width it needs (at most half the row) and the title the rest, so long names are not cut short.
                int valueWidth=Math.Min(Width/2,TextRenderer.MeasureText(g,value,valueFont,new Size(int.MaxValue,Height),TextFormatFlags.NoPadding).Width+Style.P(2));
                TextRenderer.DrawText(g,value,valueFont,new Rectangle(Width-right-valueWidth,0,valueWidth,Height),emphasis?Style.Text:Style.SubText,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g,title,titleFont,new Rectangle(titleLeft,0,Math.Max(0,Width-right-valueWidth-Style.P(10)-titleLeft),Height),Style.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            }
        }
    }
    public class SettingsForm : Form {
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
            permission.Items.AddRange(new object[]{"跟随 DSH 默认","完全访问(不弹审批)"}); permission.SelectedIndex=settings.FullAccess?1:0; rest.Controls.Add(permission);
            hint=Style.Label("只影响小鲸鱼下一次拉起 DSH；想马上生效，在面板里点「重启服务」。",0,160,420,36,11,false,Color.Gray); rest.Controls.Add(hint);
            var advanced=Button("Windows 连接与启动选项…",20,0,210,22,delegate { using(var form=new WindowsOptions(settings,demo)) { if(form.ShowDialog(this)==DialogResult.OK) Commit(false); } }); advanced.Name="advanced";advanced.Link=true;advanced.Align=TextFormatFlags.Left;Controls.Add(advanced);
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
            Text="Windows 连接与启动选项";BackColor=Color.White;Font=Style.Font(12,false);AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(Style.P(490),Style.P(344));FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
            string[] labels={"Node.js 路径","DSH JavaScript 入口","工作目录"};string[] values={settings.NodePath,settings.DshPath,settings.WorkingDirectory};var boxes=new List<TextBox>();
            for(int i=0;i<3;i++){Controls.Add(Style.Label(labels[i],20,20+i*55,450,20,12,false,Color.Black));var t=new TextBox{Text=values[i],Bounds=new Rectangle(Style.P(20),Style.P(42+i*55),Style.P(450),Style.P(25))};Controls.Add(t);boxes.Add(t);}
            var app=new CheckBox{Text="用独立窗口打开 DSH（优先 Chrome，没有就用 Edge）",Checked=settings.AppWindow,Bounds=new Rectangle(Style.P(20),Style.P(195),Style.P(450),Style.P(25))};Controls.Add(app);
            var login=new CheckBox{Text="登录 Windows 时启动小鲸鱼",Bounds=new Rectangle(Style.P(20),Style.P(223),Style.P(450),Style.P(25)),Enabled=!demo};Controls.Add(login);
            const string keyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";using(var key=Registry.CurrentUser.OpenSubKey(keyPath))login.Checked=key!=null&&(key.GetValue("DSHWhale")!=null||key.GetValue("DSHWhale-Vibe")!=null);
            Controls.Add(Style.Label("DSH 怎么运行",20,256,115,20,12,false,Color.Black));
            var host=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(Style.P(140),Style.P(252),Style.P(330),Style.P(27)),Font=Style.Font(12,false)};
            host.Items.AddRange(new object[]{"自动（只有桌面版的配置、没有 web 配置时不另起 dsh web）","由小鲸鱼启动和看护 dsh web","只看状态，不启动也不停止"});
            string[] hostModes={"auto","manage","observe"};int hostIndex=Array.IndexOf(hostModes,settings.HostMode??"auto");host.SelectedIndex=hostIndex<0?0:hostIndex;Controls.Add(host);
            var save=new SoftButton{Text="保存",Primary=true,Bounds=new Rectangle(Style.P(390),Style.P(298),Style.P(80),Style.P(28))};Controls.Add(save);
            save.Click+=delegate{try{string node=boxes[0].Text.Trim(),entry=boxes[1].Text.Trim(),cwd=boxes[2].Text.Trim();if(node.Length>0&&!File.Exists(node))throw new IOException("Node.js 路径不存在");if(entry.Length>0&&!File.Exists(entry))throw new IOException("DSH 入口不存在");if(cwd.Length>0&&!Directory.Exists(cwd))throw new IOException("工作目录不存在");settings.NodePath=node;settings.DshPath=entry;settings.WorkingDirectory=cwd;settings.HostMode=hostModes[Math.Max(0,host.SelectedIndex)];settings.AppWindow=app.Checked;if(!demo)using(var key=Registry.CurrentUser.CreateSubKey(keyPath)){if(login.Checked)key.SetValue("DSHWhale",Native.Quote(Application.ExecutablePath)+" --autostart");else key.DeleteValue("DSHWhale",false);key.DeleteValue("DSHWhale-Vibe",false);}DialogResult=DialogResult.OK;Close();}catch(Exception e){MessageBox.Show(e.Message,"保存失败");}};
        }
    }
    public enum QuitChoice { Cancel, KeepService, StopService }
    // Asks with the actions written on the buttons: a Yes/No box shows English labels on English Windows.
    class QuitPrompt : Form {
        QuitChoice choice = QuitChoice.Cancel;
        public static QuitChoice Ask(Icon icon) { using (var prompt = new QuitPrompt(icon)) { prompt.ShowDialog(); return prompt.choice; } }
        QuitPrompt(Icon icon) {
            Text="退出小鲸鱼"; Icon=icon; BackColor=Color.White; Font=Style.Font(13,false); AutoScaleMode=AutoScaleMode.None;
            FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(Style.P(420),Style.P(156));
            Controls.Add(Style.Label("Windows 上的 DSH 服务要继续运行吗？",22,20,380,26,14,true,Style.Text));
            Controls.Add(Style.Label("保留服务：DSH 照常运行，下次打开小鲸鱼会接管它。\n停止服务：先停掉 DSH 再退出。",22,52,380,44,12,false,Style.SubText));
            var keep=Choice("保留服务并退出",22,110,140,QuitChoice.KeepService); keep.Primary=true;
            Choice("停止服务并退出",170,110,140,QuitChoice.StopService);
            var cancel=Choice("取消",318,110,80,QuitChoice.Cancel);
            AcceptButton=keep; CancelButton=cancel;
        }
        SoftButton Choice(string text,int x,int y,int width,QuitChoice result) {
            var b=new SoftButton{Text=text,Bounds=new Rectangle(Style.P(x),Style.P(y),Style.P(width),Style.P(30))};
            b.Click+=delegate{choice=result;DialogResult=DialogResult.OK;Close();};
            Controls.Add(b); return b;
        }
    }
}
