using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WhalePocketShared;

namespace WhalePocketLite {
 static class Entry {
  [STAThread] static int Main(string[] args) {
   Native.SetProcessDPIAware();
   using(var mutex=new Mutex(false,"Local\\WhalePocket_Liquid_UI")){
    bool owns=false;
    try{
     try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
     if(!owns)return 0;
     Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
     Application.Run(new Widget(args));return 0;
    }catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"widget-error.log"),e.ToString());return 1;}
    finally{if(owns)mutex.ReleaseMutex();}
   }
  }
 }
 sealed class Widget:Form {
  readonly string root=AppDomain.CurrentDomain.BaseDirectory;
  readonly JavaScriptSerializer json=new JavaScriptSerializer();
  readonly Dictionary<string,int> viewIndex=new Dictionary<string,int>();
  readonly Dictionary<string,Bitmap> portraits=new Dictionary<string,Bitmap>();
  readonly string[] portraitIds={"wave","cheer","rest"},portraitNames={"挥手招呼","元气比耶","闭眼小憩"},portraitFiles={"whale-portrait.png","whale-cheer.png","whale-think.png"};
  Dictionary<string,object> config,snapshot,appearance;
  readonly System.Windows.Forms.Timer refreshTimer=new System.Windows.Forms.Timer(),changeTimer=new System.Windows.Forms.Timer();
  FileSystemWatcher dataWatcher,configWatcher;
  Process poller,settingsProcess;
  bool closing,rendering,busy,offline;
  double transparency;string portrait="wave",previewFile;float scale=1;
  ContextMenuStrip menu;ToolStripMenuItem accountsMenu;ToolTip tooltip;List<Dictionary<string,object>> rows=new List<Dictionary<string,object>>();
  int hoverRow=-2;
  string DataFile(string name){return Path.Combine(root,"data",name);}
  static Dictionary<string,object> D(object v){return v as Dictionary<string,object>??new Dictionary<string,object>();}
  static object V(Dictionary<string,object>d,string key){object v;return d!=null&&d.TryGetValue(key,out v)?v:null;}
  static string S(Dictionary<string,object>d,string key,string fallback=""){var v=V(d,key);return v==null?fallback:Convert.ToString(v,CultureInfo.InvariantCulture);}
  static double N(Dictionary<string,object>d,string key,double fallback=0){double n;return double.TryParse(S(d,key),NumberStyles.Any,CultureInfo.InvariantCulture,out n)&&!double.IsNaN(n)&&!double.IsInfinity(n)?n:fallback;}
  static bool B(Dictionary<string,object>d,string key,bool fallback=false){var v=V(d,key);return v is bool?(bool)v:fallback;}
  static List<Dictionary<string,object>> Items(object value){var list=new List<Dictionary<string,object>>();var e=value as IEnumerable;if(e!=null&&!(value is string))foreach(var x in e)list.Add(D(x));return list;}
  Dictionary<string,object> Read(string file){try{return D(json.DeserializeObject(File.ReadAllText(file,Encoding.UTF8)));}catch{return new Dictionary<string,object>();}}
  void Write(string file,object value){File.WriteAllText(file,json.Serialize(value),new UTF8Encoding(false));}
  public Widget(string[] args){
   Text="鲸娘钱包";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
   using(var g=CreateGraphics())scale=g.DpiX/96f;
   Directory.CreateDirectory(Path.Combine(root,"data"));
   AccountPreferences.EnsureConfig(root);offline=args.Contains("--offline");
   int previewIndex=Array.IndexOf(args,"--preview");if(previewIndex>=0&&previewIndex+1<args.Length)previewFile=args[previewIndex+1];
   config=Read(Path.Combine(root,"config.json"));snapshot=Read(DataFile("balances.json"));LoadAppearance();
   var state=Read(DataFile("window.json"));TopMost=B(state,"topmost",true);
   Size=new Size((int)(284*scale),(int)(84*scale));
   Location=new Point((int)(N(state,"left",(Screen.PrimaryScreen.WorkingArea.Right-Width-16)/scale)*scale),(int)(N(state,"top",80)*scale));
   if(!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(Bounds)))Location=new Point(Screen.PrimaryScreen.WorkingArea.Right-Width-16,80);
   tooltip=new ToolTip{InitialDelay=500,ReshowDelay=100,AutoPopDelay=16000,ShowAlways=true};
   BuildMenu();
   refreshTimer.Interval=Math.Max(60000,(int)(N(config,"refreshMinutes",10)*60000));refreshTimer.Tick+=(s,e)=>RefreshBalances();
   changeTimer.Interval=160;changeTimer.Tick+=(s,e)=>{changeTimer.Stop();ReloadFiles();};
   Shown+=(s,e)=>{
    DrawWidget();
    if(previewFile!=null){var t=new System.Windows.Forms.Timer{Interval=1200};t.Tick+=(a,b)=>{t.Stop();t.Dispose();Close();};t.Start();return;}
    WatchFiles();if(!offline){refreshTimer.Start();RefreshBalances();}
   };
   MouseDown+=(s,e)=>{
    if(e.Button!=MouseButtons.Left)return;
    if(e.X<74*scale){CyclePortrait();return;}
    int i=(int)((e.Y/scale-24)/43);
    if(i>=0&&i<rows.Count&&e.Y/scale<24+i*43+23){
     string id=S(rows[i],"id");var views=Items(V(State(id),"views"));
     if(views.Count>1){viewIndex[id]=((viewIndex.ContainsKey(id)?viewIndex[id]:0)+1)%views.Count;DrawWidget();return;}
    }
    Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
   };
   MouseMove+=(s,e)=>UpdateTooltip(e.Location);
  }
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x80;return p;}}
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override void OnPaintBackground(PaintEventArgs e){}
  protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x232)SavePosition();}
  void SavePosition(){if(previewFile==null)Write(DataFile("window.json"),new{left=Left/scale,top=Top/scale,topmost=TopMost});}
  Dictionary<string,object> State(string id){return D(V(D(V(snapshot,"accounts")),id));}
  void LoadAppearance(){appearance=Read(DataFile("appearance.json"));portrait=S(appearance,"mascot","wave");if(!portraitIds.Contains(portrait))portrait="wave";transparency=Math.Max(0,Math.Min(90,N(appearance,"glassTransparency",25)));}
  void SetPortrait(string id){appearance=Read(DataFile("appearance.json"));appearance["mascot"]=id;Write(DataFile("appearance.json"),appearance);LoadAppearance();DrawWidget();}
  void CyclePortrait(){SetPortrait(portraitIds[(Array.IndexOf(portraitIds,portrait)+1)%portraitIds.Length]);}
  void ReloadFiles(){
   if(closing)return;string previous=json.Serialize(new{config,snapshot,appearance});var c=Read(Path.Combine(root,"config.json"));if(c.Count>0)config=c;
   var s=Read(DataFile("balances.json"));if(s.Count>0)snapshot=s;
   LoadAppearance();var position=Read(DataFile("window.json"));TopMost=B(position,"topmost",true);
   refreshTimer.Interval=Math.Max(60000,(int)(N(config,"refreshMinutes",10)*60000));if(previous!=json.Serialize(new{config,snapshot,appearance}))DrawWidget();
  }
  void WatchFiles(){
   FileSystemEventHandler changed=(s,e)=>QueueChange();RenamedEventHandler renamed=(s,e)=>QueueChange();
   dataWatcher=new FileSystemWatcher(Path.Combine(root,"data"),"*.json"){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName};dataWatcher.Changed+=changed;dataWatcher.Created+=changed;dataWatcher.Renamed+=renamed;dataWatcher.EnableRaisingEvents=true;
   configWatcher=new FileSystemWatcher(root,"config.json"){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName};configWatcher.Changed+=changed;configWatcher.Created+=changed;configWatcher.Renamed+=renamed;configWatcher.EnableRaisingEvents=true;
  }
  void QueueChange(){if(closing||!IsHandleCreated)return;try{BeginInvoke((Action)(()=>{if(closing)return;changeTimer.Stop();changeTimer.Start();}));}catch(InvalidOperationException){}}
  string FindNode(){
   string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   var candidates=new[]{Environment.GetEnvironmentVariable("DSH_NODE"),Path.Combine(home,".cache","codex-runtimes","codex-primary-runtime","dependencies","node","bin","node.exe"),Path.Combine(home,".dsh","dsh-runtimes","dsh-primary-runtime","dependencies","node","bin","node.exe")};
   foreach(var p in candidates)if(!String.IsNullOrEmpty(p)&&File.Exists(p))return p;return "node.exe";
  }
  void RefreshBalances(){
   if(busy||closing)return;
   try{
    var start=new ProcessStartInfo(FindNode(),"--disable-warning=ExperimentalWarning \""+Path.Combine(root,"auto-poller.mjs")+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true};
    try{var file=DataFile("keys.dpapi");if(File.Exists(file)){var keys=D(json.DeserializeObject(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(file)),null,DataProtectionScope.CurrentUser))));start.EnvironmentVariables["WHALE_DS_KEY"]=S(keys,"deepseek");start.EnvironmentVariables["WHALE_OPENAI_KEY"]=S(keys,"openai");}}catch{}
    busy=true;poller=new Process{StartInfo=start,EnableRaisingEvents=true};
    poller.Exited+=(s,e)=>{var finished=(Process)s;if(closing){finished.Dispose();return;}try{BeginInvoke((Action)(()=>{busy=false;if(poller==finished)poller=null;finished.Dispose();ReloadFiles();}));}catch(InvalidOperationException){finished.Dispose();}};
    poller.Start();poller.BeginOutputReadLine();poller.BeginErrorReadLine();
   }catch{busy=false;if(poller!=null){poller.Dispose();poller=null;}}
  }
  void OpenSettings(){
   if(settingsProcess!=null&&!settingsProcess.HasExited){Native.SetForegroundWindow(settingsProcess.MainWindowHandle);return;}
   SavePosition();settingsProcess=Process.Start(new ProcessStartInfo(Path.Combine(root,"WhaleSettings.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root});
   settingsProcess.EnableRaisingEvents=true;settingsProcess.Exited+=(s,e)=>{var p=(Process)s;QueueChange();p.Dispose();settingsProcess=null;};
  }
  void SetVisible(IEnumerable<string> ids,bool visible){
   try{config=AccountPreferences.SetVisible(root,ids,visible);DrawWidget();}
   catch{MessageBox.Show("账户选择未能保存，请检查目录写入权限或 config.json。","鲸娘钱包");}
  }
  void BuildAccountMenu(){
   foreach(ToolStripItem item in accountsMenu.DropDownItems.Cast<ToolStripItem>().ToArray())item.Dispose();
   accountsMenu.DropDownItems.Clear();
   var accounts=AccountPreferences.Accounts(config,snapshot);
   accountsMenu.Text="显示账户（"+accounts.Count(a=>B(a,"visible"))+"/"+accounts.Count+"）";
   if(accounts.Count==0){accountsMenu.DropDownItems.Add(new ToolStripMenuItem("尚未发现账户，请先扫描"){Enabled=false});return;}
   foreach(var account in accounts){
    string id=S(account,"id");var item=new ToolStripMenuItem(S(account,"display",id)){Checked=B(account,"visible"),CheckOnClick=true,Tag=id,ToolTipText="勾选显示，取消勾选隐藏 · 自动保存"};
    item.Click+=(s,e)=>SetVisible(new[]{id},item.Checked);accountsMenu.DropDownItems.Add(item);
   }
   accountsMenu.DropDownItems.Add(new ToolStripSeparator());
   accountsMenu.DropDownItems.Add("全部显示",null,(s,e)=>SetVisible(accounts.Select(a=>S(a,"id")),true));
   accountsMenu.DropDownItems.Add("全部隐藏",null,(s,e)=>SetVisible(accounts.Select(a=>S(a,"id")),false));
  }
  void BuildMenu(){
   menu=new ContextMenuStrip{ShowImageMargin=false,ShowCheckMargin=true,Font=new Font("Microsoft YaHei UI",9),BackColor=Color.FromArgb(237,246,254),ForeColor=Color.FromArgb(75,107,132),Renderer=new MilkMenuRenderer(),Padding=new Padding(5),AutoSize=true};
   accountsMenu=new ToolStripMenuItem("显示账户");menu.Items.Add(accountsMenu);
   menu.Items.Add("账户与外观",null,(s,e)=>OpenSettings());
   var portraitsMenu=new ToolStripMenuItem("切换立绘");
   for(int i=0;i<portraitIds.Length;i++){string id=portraitIds[i];portraitsMenu.DropDownItems.Add(new ToolStripMenuItem(portraitNames[i],null,(s,e)=>SetPortrait(id)){Tag=id});}
   menu.Items.Add(portraitsMenu);menu.Items.Add("扫描账户 / 刷新",null,(s,e)=>{if(!offline)RefreshBalances();});
   var pin=new ToolStripMenuItem("始终置顶",null,(s,e)=>{TopMost=!TopMost;SavePosition();});menu.Items.Add(pin);
   menu.Items.Add(new ToolStripSeparator());menu.Items.Add("收起小挂件",null,(s,e)=>Close());
   menu.Opening+=(s,e)=>{ReloadFiles();BuildAccountMenu();pin.Checked=TopMost;foreach(ToolStripMenuItem item in portraitsMenu.DropDownItems)item.Checked=(string)item.Tag==portrait;};ContextMenuStrip=menu;
  }
  void UpdateTooltip(Point p){
   int index=p.X<74*scale?-1:(int)((p.Y/scale-24)/43);if(index==hoverRow)return;hoverRow=index;
   string text="拖动移动 · 右键设置";
   if(index<0)text=portraitNames[Array.IndexOf(portraitIds,portrait)]+" · 点击换立绘 · 右键设置";
   else if(index<rows.Count){var s=State(S(rows[index],"id"));text=S(s,"source")+"\n"+S(s,"detail");}
   tooltip.SetToolTip(this,text);
  }
  Bitmap Portrait(){
   Bitmap bitmap;if(portraits.TryGetValue(portrait,out bitmap))return bitmap;
   using(var source=Image.FromFile(Path.Combine(root,portraitFiles[Array.IndexOf(portraitIds,portrait)]))){
    int width=(int)(68*scale),height=(int)(64*scale);bitmap=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
    using(var g=Graphics.FromImage(bitmap)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;float fit=Math.Min(width/(float)source.Width,height/(float)source.Height);g.DrawImage(source,(width-source.Width*fit)/2,0,source.Width*fit,source.Height*fit);}
    for(int y=(int)(height*.83);y<height;y++)for(int x=0;x<width;x++){var c=bitmap.GetPixel(x,y);bitmap.SetPixel(x,y,Color.FromArgb((int)(c.A*(height-1-y)/(height*.17)),c.R,c.G,c.B));}
   }portraits[portrait]=bitmap;return bitmap;
  }
  static Color C(int alpha,int r,int g,int b){return Color.FromArgb(Math.Max(0,Math.Min(255,alpha)),r,g,b);}
  static GraphicsPath Rounded(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  void Label(Graphics g,string value,float size,Color color,RectangleF bounds,bool bold=false,bool right=false){
   using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=right?StringAlignment.Far:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(value,font,brush,bounds,format);
  }
  void DrawWidget(){
   if(closing||rendering||!IsHandleCreated)return;rendering=true;
   try{
    rows=AccountPreferences.Accounts(config,snapshot).Where(a=>B(a,"visible")).ToList();
    float shellHeight=Math.Max(51,rows.Count*43+10),height=Math.Max(84,19+shellHeight+13);
    Size=new Size((int)Math.Ceiling(284*scale),(int)Math.Ceiling(height*scale));
    using(var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb)){
     using(var g=Graphics.FromImage(bitmap)){
      g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.ScaleTransform(scale,scale);
      int alpha=(int)Math.Round(255*(1-transparency/100));var shell=new RectangleF(33,19,242,shellHeight);
      for(int i=5;i>=1;i--)using(var p=Rounded(new RectangleF(shell.X-i,shell.Y-i+2,shell.Width+i*2,shell.Height+i*2),19+i))using(var b=new SolidBrush(C((int)(alpha*.009),57,118,155)))g.FillPath(b,p);
      using(var p=Rounded(shell,19))using(var b=new LinearGradientBrush(shell,C(alpha,246,252,255),C(alpha,208,231,251),65f))g.FillPath(b,p);
      using(var p=Rounded(shell,19))using(var pen=new Pen(C((int)(alpha*.85),255,255,255),1))g.DrawPath(pen,p);
      using(var p=Rounded(new RectangleF(34,20,240,shellHeight-2),18))using(var pen=new Pen(C((int)(alpha*.3),170,214,242),.7f))g.DrawPath(pen,p);
      using(var b=new LinearGradientBrush(new RectangleF(52,21,204,1),C(0,255,255,255),C(alpha,255,255,255),0f))g.FillRectangle(b,52,21,204,1);
      using(var p=Rounded(new RectangleF(12,20,56,52),25))using(var b=new SolidBrush(C(alpha,222,243,255)))g.FillPath(b,p);
      g.DrawImage(Portrait(),new RectangleF(8,5,68,64));
      if(rows.Count==0)Label(g,"右键选择显示的账户",10,Color.FromArgb(110,155,181),new RectangleF(77,35,185,20));
      for(int i=0;i<rows.Count;i++)DrawRow(g,rows[i],24+i*43);
     }
     if(previewFile!=null)bitmap.Save(previewFile,ImageFormat.Png);
     Native.Present(Handle,bitmap,Left,Top);
    }
   }finally{rendering=false;}
  }
  void DrawRow(Graphics g,Dictionary<string,object>a,float top){
   string id=S(a,"id"),kind=S(a,"kind"),status;var state=State(id);status=S(state,"status");var views=Items(V(state,"views"));
   int ix=viewIndex.ContainsKey(id)?viewIndex[id]:0;if(ix>=views.Count)ix=0;var view=views.Count>0?views[ix]:new Dictionary<string,object>();
   bool available=views.Count>0&&(status=="ok"||status=="stale"),hasRatio=available&&V(view,"ratio")!=null;double ratio=Math.Max(0,Math.Min(1,N(view,"ratio")));
   string name=S(a,"display",id)+(S(view,"key")=="cost"?" · 已用":views.Count>1?S(view,"key")=="week"?" · 周":" · 5h":"");
   Label(g,name,10.5f,Color.FromArgb(82,115,141),new RectangleF(77,top+3,107,20));
   string amount=available?S(view,"text"):status=="error"?"稍后重试":status=="limited"?"暂无额度":"识别中…";
   Label(g,amount,available?14:9,Color.FromArgb(54,93,124),new RectangleF(177,top+1,84,23),true,true);
   using(var p=Rounded(new RectangleF(77,top+29,154,4),2))using(var b=new SolidBrush(Color.FromArgb(29,83,134,175)))g.FillPath(b,p);
   if(hasRatio&&ratio>0){float width=(float)(154*ratio);Color c=kind=="deepseek"?Color.FromArgb(108,172,208):kind=="relay"?Color.FromArgb(139,155,212):kind=="openai"?Color.FromArgb(101,175,158):Color.FromArgb(150,153,206);
    using(var p=Rounded(new RectangleF(77,top+29,Math.Max(1,width),4),2))using(var b=new LinearGradientBrush(new RectangleF(77,top+29,154,4),c,Color.FromArgb(174,216,236),0f))g.FillPath(b,p);
   }
   Label(g,status=="stale"?"缓存":hasRatio?Math.Round(ratio*100)+"%":"—",8,Color.FromArgb(124,157,180),new RectangleF(233,top+23,28,16),false,true);
  }
  protected override void OnFormClosed(FormClosedEventArgs e){
   closing=true;refreshTimer.Stop();changeTimer.Stop();if(dataWatcher!=null)dataWatcher.Dispose();if(configWatcher!=null)configWatcher.Dispose();SavePosition();
   tooltip.Dispose();menu.Dispose();foreach(var p in portraits.Values)p.Dispose();refreshTimer.Dispose();changeTimer.Dispose();base.OnFormClosed(e);
  }
 }
 sealed class MilkMenuRenderer:ToolStripProfessionalRenderer {
  public MilkMenuRenderer():base(new MilkColors()){RoundedEdges=true;}
  protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var pen=new Pen(Color.FromArgb(206,226,241)))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
 }
 sealed class MilkColors:ProfessionalColorTable {
  public override Color MenuItemSelected{get{return Color.FromArgb(213,233,249);}}
  public override Color MenuItemBorder{get{return Color.FromArgb(213,233,249);}}
  public override Color ToolStripDropDownBackground{get{return Color.FromArgb(237,246,254);}}
  public override Color ImageMarginGradientBegin{get{return ToolStripDropDownBackground;}}
  public override Color ImageMarginGradientMiddle{get{return ToolStripDropDownBackground;}}
  public override Color ImageMarginGradientEnd{get{return ToolStripDropDownBackground;}}
 }
 static class Native {
  [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;public POINT(int x,int y){X=x;Y=y;}}
  [StructLayout(LayoutKind.Sequential)]struct SIZE{public int W,H;public SIZE(int w,int h){W=w;H=h;}}
  [StructLayout(LayoutKind.Sequential,Pack=1)]struct BLEND{public byte Op,Flags,Alpha,Format;}
  [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")]public static extern bool ReleaseCapture();
  [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
  [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr h,IntPtr dc);
  [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
  [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,ref POINT pt,ref SIZE size,IntPtr src,ref POINT origin,int key,ref BLEND blend,int flags);
  public static void Present(IntPtr h,Bitmap image,int x,int y){
   IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),bitmap=IntPtr.Zero,old=IntPtr.Zero;
   try{bitmap=image.GetHbitmap(Color.FromArgb(0));old=SelectObject(memory,bitmap);var pt=new POINT(x,y);var origin=new POINT(0,0);var size=new SIZE(image.Width,image.Height);var blend=new BLEND{Op=0,Flags=0,Alpha=255,Format=1};if(!UpdateLayeredWindow(h,screen,ref pt,ref size,memory,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
   finally{if(old!=IntPtr.Zero)SelectObject(memory,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
  }
 }
}
