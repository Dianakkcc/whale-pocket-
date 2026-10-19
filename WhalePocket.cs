using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WhalePocketShared;

namespace WhalePocket {
 public sealed class Pocket {
  string root, data, preview; bool verify, settingsPreview, busy, rebuilding, testing;
  Window win; StackPanel cards, switches; FrameworkElement dashboard, settings;
  TextBlock sync, saveHint;
  List<CheckBox> accountToggles = new List<CheckBox>();
  Dictionary<string,object> config, snapshot; Dictionary<string,object> secrets;
  JavaScriptSerializer json = new JavaScriptSerializer();
  DispatcherTimer ticker; Process poller; DateTime nextRefresh = DateTime.MinValue, lastWrite = DateTime.MinValue;
  string lastConfig="";
  Dictionary<string,int> viewIndex = new Dictionary<string,int>();
  string portraitStyle="wave";
  double glassTransparency=25;
  DispatcherTimer appearanceSaveTimer;
  string[] portraitIds={"wave","cheer","rest"};
  string[] portraitNames={"挥手招呼","元气比耶","闭眼小憩"};
  string[] portraitFiles={"whale-portrait.png","whale-cheer.png","whale-think.png"};
  string[] ids = {"deepseek","openai","codex","claude"};
  string[] names = {"DeepSeek","ChatGPT API","Codex","Claude Code"};
  string[] colors = {"#629FCD","#65AF9E","#9298CF","#CA9C87","#8B9BD4"};
  string[] pale = {"#DCEEF9","#E0F3EC","#EBE8FA","#F8E9E0"};
  static Mutex instance;
  public static bool SettingsOnly;
  public static void Run(string dir, bool test, string shot, bool settingsShot) {
   instance = new Mutex(false, SettingsOnly?"Local\\WhalePocket_Settings_UI":"Local\\WhalePocket_Liquid_UI");
   if (!test && !instance.WaitOne(0)) return;
   try { new Pocket(dir, test, shot, settingsShot).Show(); }
   finally { if (!test) instance.ReleaseMutex(); instance.Dispose(); }
  }
  Pocket(string dir, bool test, string shot, bool settingsShot) {
   root = dir; data = Path.Combine(root,"data"); Directory.CreateDirectory(data);
   verify=test; preview=shot; settingsPreview=settingsShot;
   AccountPreferences.EnsureConfig(root);config=Read(Path.Combine(root,"config.json")); secrets=ReadSecrets();
   using(var r = new StreamReader(Path.Combine(root,"界面.xaml"), Encoding.UTF8)) win=(Window)XamlReader.Load(r.BaseStream);
   BuildPortraitMenu();
   var appearance=Read(Path.Combine(data,"appearance.json"));
   SelectPortrait(S(appearance,"mascot","wave"),false);
   var transparencySlider=Find<Slider>("TransparencySlider");
   transparencySlider.Value=Math.Max(0,Math.Min(90,N(appearance,"glassTransparency",25)));
   ApplyTransparency(transparencySlider.Value);
   appearanceSaveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
   appearanceSaveTimer.Tick+=(s,e)=>{appearanceSaveTimer.Stop();SaveAppearance();};
   transparencySlider.ValueChanged+=(s,e)=>{ApplyTransparency(e.NewValue);appearanceSaveTimer.Stop();appearanceSaveTimer.Start();};
   Find<Image>("Mascot").ContextMenu=Find<Border>("Shell").ContextMenu;
   cards=Find<StackPanel>("Cards"); switches=Find<StackPanel>("Switches");
   dashboard=Find<FrameworkElement>("Dashboard"); settings=Find<FrameworkElement>("Settings");
   sync=Find<TextBlock>("SyncText"); saveHint=Find<TextBlock>("SaveHint");
   var state=Read(Path.Combine(data,"window.json"));
   win.Left=N(state,"left",SystemParameters.WorkArea.Right-win.Width-16); win.Top=N(state,"top",80);
   win.Left=Math.Max(SystemParameters.VirtualScreenLeft,Math.Min(win.Left,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-win.Width));
   win.Top=Math.Max(SystemParameters.VirtualScreenTop,Math.Min(win.Top,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-180));
   win.Topmost=B(state,"topmost",true);
   Find<Button>("PinButton").Opacity=win.Topmost?1:0.45;
   Find<Button>("CloseButton").Click+=(s,e)=>win.Close();
   Find<Button>("PinButton").Click+=(s,e)=>{ win.Topmost=!win.Topmost; Find<Button>("PinButton").Opacity=win.Topmost?1:0.45; SavePosition(); };
   Find<FrameworkElement>("Shell").MouseLeftButtonDown+=(s,e)=>{
    if (e.OriginalSource is System.Windows.Shapes.Ellipse || ParentButton(e.OriginalSource as DependencyObject)) return;
    try {win.DragMove();SavePosition();} catch(InvalidOperationException){}
   };
   Find<Button>("ManageButton").Click+=(s,e)=>OpenSettings();
   Find<MenuItem>("MenuManage").Click+=(s,e)=>OpenSettings();
   Find<MenuItem>("MenuRefresh").Click+=(s,e)=>{Refresh();Refract();};
   Find<MenuItem>("MenuPin").IsChecked=win.Topmost;
   Find<MenuItem>("MenuPin").Click+=(s,e)=>{win.Topmost=Find<MenuItem>("MenuPin").IsChecked;Find<CheckBox>("PinToggle").IsChecked=win.Topmost;SavePosition();};
   Find<MenuItem>("MenuClose").Click+=(s,e)=>win.Close();
   Find<Button>("BackButton").Click+=(s,e)=>{ Find<Popup>("SettingsPopup").IsOpen=false;settings.Visibility=Visibility.Collapsed; Render(); };
   Find<Button>("RefreshButton").Click+=(s,e)=>{Refresh();Refract();};
   Find<Button>("ScanButton").Click+=(s,e)=>Refresh();
   Find<Popup>("SettingsPopup").Closed+=(s,e)=>{settings.Visibility=Visibility.Collapsed;if(SettingsOnly)win.Close();};
   RoutedEventHandler pinChanged=(s,e)=>{if(rebuilding)return;win.Topmost=Find<CheckBox>("PinToggle").IsChecked==true;Find<MenuItem>("MenuPin").IsChecked=win.Topmost;SavePosition();};
   Find<CheckBox>("PinToggle").Checked+=pinChanged;Find<CheckBox>("PinToggle").Unchecked+=pinChanged;
   Find<FrameworkElement>("Mascot").MouseLeftButtonDown+=(s,e)=>{
    var a=new DoubleAnimation(0,-6,TimeSpan.FromMilliseconds(180)); a.AutoReverse=true;
    var t=new TranslateTransform(); Find<FrameworkElement>("Mascot").RenderTransform=t;
    t.BeginAnimation(TranslateTransform.YProperty,a); e.Handled=true;
   };
   win.Closed+=(s,e)=>{if(ticker!=null)ticker.Stop();appearanceSaveTimer.Stop();if(!verify)SaveAppearance();SavePosition();};
  }
  bool ParentButton(DependencyObject o) { while(o!=null){if(o is Button)return true;o=VisualTreeHelper.GetParent(o);}return false; }
  void BuildPortraitMenu() {
   var menu=Find<MenuItem>("MenuPortraits");
   for(int i=0;i<portraitIds.Length;i++){
    string id=portraitIds[i];
    var item=new MenuItem{Header=portraitNames[i],Tag=id,IsCheckable=true};
    item.ToolTip=new Image{Source=new BitmapImage(new Uri(Path.Combine(root,portraitFiles[i]))),Width=85,Height=80,Stretch=Stretch.Uniform};
    item.Click+=(s,e)=>{SelectPortrait(id,true);e.Handled=true;};menu.Items.Add(item);
   }
  }
  void SelectPortrait(string id,bool save) {
   int index=Array.IndexOf(portraitIds,id);if(index<0)index=0;
   var picture=new BitmapImage();picture.BeginInit();picture.CacheOption=BitmapCacheOption.OnLoad;picture.UriSource=new Uri(Path.Combine(root,portraitFiles[index]));picture.EndInit();picture.Freeze();
   Find<Image>("Mascot").Source=picture;Find<Image>("Mascot").ToolTip=portraitNames[index]+" · 右键切换立绘";portraitStyle=portraitIds[index];
   foreach(MenuItem item in Find<MenuItem>("MenuPortraits").Items)item.IsChecked=Convert.ToString(item.Tag)==portraitStyle;
   if(save)SaveAppearance();
   if(Find<UniformGrid>("PortraitOptions")!=null)BuildPortraitCards();
  }
  void ApplyTransparency(double value) {
   glassTransparency=Math.Round(Math.Max(0,Math.Min(90,value)));
   double opacity=1-glassTransparency/100;
   Find<Grid>("GlassSurface").Opacity=opacity;
   // An opaque base makes the slider absolute; zero also hides the captured desktop.
   Find<Border>("Backdrop").Opacity=Math.Min(1,glassTransparency/25);
   Find<Border>("MascotGlow").Opacity=opacity;
   Find<TextBlock>("TransparencyValue").Text=glassTransparency.ToString("0",CultureInfo.InvariantCulture)+"%";
  }
  void SaveAppearance() {
   var appearance=Read(Path.Combine(data,"appearance.json"));
   appearance["mascot"]=portraitStyle;appearance["glassTransparency"]=glassTransparency;
   Write(Path.Combine(data,"appearance.json"),appearance);
  }
  T Find<T>(string n) where T:class {return win.FindName(n) as T;}
  static Dictionary<string,object> D(object o) {return o as Dictionary<string,object> ?? new Dictionary<string,object>();}
  static object V(Dictionary<string,object> d,string k) {object v;return d!=null&&d.TryGetValue(k,out v)?v:null;}
  static string S(Dictionary<string,object> d,string k,string fallback="") {object v=V(d,k);return v==null?fallback:Convert.ToString(v,CultureInfo.InvariantCulture);}
  static double N(Dictionary<string,object> d,string k,double fallback=0) {double n;return double.TryParse(S(d,k),NumberStyles.Any,CultureInfo.InvariantCulture,out n)?n:fallback;}
  static bool B(Dictionary<string,object> d,string k,bool fallback=false) {object v=V(d,k);return v is bool?(bool)v:fallback;}
  static List<Dictionary<string,object>> Items(object v) {var a=v as IEnumerable;if(a==null||v is string)return new List<Dictionary<string,object>>();var list=new List<Dictionary<string,object>>();foreach(var x in a)list.Add(D(x));return list;}
  Dictionary<string,object> Read(string f) {try{return D(json.DeserializeObject(File.ReadAllText(f,Encoding.UTF8)));}catch{return new Dictionary<string,object>();}}
  void Write(string f,object o) {File.WriteAllText(f,json.Serialize(o),new UTF8Encoding(false));}
  List<Dictionary<string,object>> Accounts {get{return AccountPreferences.Accounts(config,snapshot);}}
  void SetAccountVisible(IEnumerable<string> ids,bool visible){
   config=AccountPreferences.SetVisible(root,ids,visible);lastConfig=File.ReadAllText(Path.Combine(root,"config.json"),Encoding.UTF8);Render();
  }
  Dictionary<string,object> ReadSecrets() {
   try {var bytes=Convert.FromBase64String(File.ReadAllText(Path.Combine(data,"keys.dpapi")));return D(json.DeserializeObject(Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser))));}
   catch{return new Dictionary<string,object>();}
  }
  void SaveSecrets() {var b=ProtectedData.Protect(Encoding.UTF8.GetBytes(json.Serialize(secrets)),null,DataProtectionScope.CurrentUser);File.WriteAllText(Path.Combine(data,"keys.dpapi"),Convert.ToBase64String(b));}
  void SavePosition() {if(verify)return;if(SettingsOnly){var state=Read(Path.Combine(data,"window.json"));state["topmost"]=win.Topmost;Write(Path.Combine(data,"window.json"),state);return;}Write(Path.Combine(data,"window.json"),new{left=win.Left,top=win.Top,topmost=win.Topmost});}
  Brush Brush(string color) {return (Brush)new BrushConverter().ConvertFromString(color);}
  TextBlock Text(string text,double size,string color,bool bold=false) {return new TextBlock{Text=text,FontSize=size,Foreground=Brush(color),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};}
  void Show() {
   LoadSnapshot();Render();win.Opacity=0;
   ticker=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
   ticker.Tick+=(s,e)=>Tick();ticker.Start();
   win.Loaded+=(s,e)=>{
    if(SettingsOnly){nextRefresh=DateTime.MaxValue;win.ShowActivated=false;OpenSettings();return;}
    if(verify){VerifyUi();VerifyPortraits();}
    Refract();if(!verify)Refresh();
    if(settingsPreview)OpenSettings();
    if(verify){var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};t.Tick+=(a,b)=>{t.Stop();if(!String.IsNullOrEmpty(preview))SavePreview(preview);win.Close();};t.Start();}
   };
   win.ShowDialog();
  }
  string FindNode() {
   var paths=new[]{Environment.GetEnvironmentVariable("DSH_NODE"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache","codex-runtimes","codex-primary-runtime","dependencies","node","bin","node.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".dsh","dsh-runtimes","dsh-primary-runtime","dependencies","node","bin","node.exe")};
   foreach(var p in paths)if(!String.IsNullOrEmpty(p)&&File.Exists(p))return p;
   foreach(var p in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){var f=Path.Combine(p,"node.exe");if(File.Exists(f))return f;}
   return "node.exe";
  }
  void Refresh() {
   if(verify||busy||testing)return;
   try {
    var p=new ProcessStartInfo(FindNode(),"--disable-warning=ExperimentalWarning \""+Path.Combine(root,"auto-poller.mjs")+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true};
    p.EnvironmentVariables["WHALE_DS_KEY"]=S(secrets,"deepseek");p.EnvironmentVariables["WHALE_OPENAI_KEY"]=S(secrets,"openai");
    poller=Process.Start(p);poller.BeginOutputReadLine();poller.BeginErrorReadLine();busy=true;sync.Text="正在识别本机账户…";Find<Button>("RefreshButton").IsEnabled=false;Find<Button>("ScanButton").IsEnabled=false;Find<Button>("ScanButton").Content="扫描中…";Find<TextBlock>("DiscoveryStatus").Text="正在读取本机登录和余额";
   }catch{sync.Text="未找到 Node.js，请检查运行环境";saveHint.Text=sync.Text;}
   nextRefresh=DateTime.UtcNow.AddMinutes(Math.Max(1,N(config,"refreshMinutes",10)));
  }
  void Tick() {
   try {
    string currentConfig=File.Exists(Path.Combine(root,"config.json"))?File.ReadAllText(Path.Combine(root,"config.json"),Encoding.UTF8):"";
    if(!testing&&currentConfig!=lastConfig){config=Read(Path.Combine(root,"config.json"));lastConfig=currentConfig;Render();if(Find<Popup>("SettingsPopup").IsOpen)BuildAccountSettings();}
    if(busy&&poller.HasExited){
     busy=false;Find<Button>("RefreshButton").IsEnabled=true;Find<Button>("ScanButton").IsEnabled=true;Find<Button>("ScanButton").Content="↻  重新扫描";
     if(poller.ExitCode!=0){sync.Text="刷新失败 · 点击重新扫描";saveHint.Text=sync.Text;}
     else {LoadSnapshot();Render();}
     poller.Dispose();poller=null;
    }
    var f=Path.Combine(data,"balances.json");if(File.Exists(f)&&File.GetLastWriteTimeUtc(f)>lastWrite){LoadSnapshot();Render();}
    if(!verify&&!busy&&DateTime.UtcNow>=nextRefresh)Refresh();
   }catch{sync.Text="数据暂不可用 · 点击 ↻ 重试";}
  }
  void LoadSnapshot() {var f=Path.Combine(data,"balances.json");snapshot=Read(f);if(File.Exists(f))lastWrite=File.GetLastWriteTimeUtc(f);if(Find<Popup>("SettingsPopup").IsOpen)BuildAccountSettings();}
  void Render() {
   cards.Children.Clear();int count=0,ready=0;
   foreach(var a in Accounts){if(!B(a,"visible"))continue;count++;string id=S(a,"id");var state=D(V(D(V(snapshot,"accounts")),id));if(S(state,"status")=="ok")ready++;cards.Children.Add(Card(a,state));}
   Find<TextBlock>("AccountCount").Text=count+" 个账户";
   Find<FrameworkElement>("EmptyState").Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
   double ms=N(snapshot,"updatedAtMs");
   if(!busy)sync.Text=ms>0?"◦ "+DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToLocalTime().ToString("HH:mm")+" 更新 · "+ready+" 家已连接":"◦ 等待第一份能量";
  }
  Border Card(Dictionary<string,object> account,Dictionary<string,object> state) {
   string id=S(account,"id"),name=S(account,"display",id);int i=Array.IndexOf(ids,S(account,"kind"));if(i<0)i=4;
   string status=S(state,"status"),accent=colors[i];var views=Items(V(state,"views"));
   int ix=viewIndex.ContainsKey(id)?viewIndex[id]:0;if(ix>=views.Count)ix=0;
   var view=views.Count>0?views[ix]:new Dictionary<string,object>();
   bool available=views.Count>0&&(status=="ok"||status=="stale");bool hasRatio=available&&V(view,"ratio")!=null;double ratio=Math.Max(0,Math.Min(1,N(view,"ratio")));
   var border=new Border{Padding=new Thickness(0,5,0,5),ToolTip=S(state,"source")+"\n"+S(state,"detail","右键管理账户")};
   var stack=new StackPanel();border.Child=stack;
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var label=Text(name+(S(view,"key")=="cost"?" · 已用":""),10.5,"#52738D");label.TextTrimming=TextTrimming.CharacterEllipsis;label.Margin=new Thickness(0,0,6,0);label.FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");row.Children.Add(label);
   var amount=Text(available?S(view,"text"):(status=="error"?"稍后重试":status=="limited"?"暂无额度":"识别中…"),available?14:9,"#365D7C",true);amount.FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");Grid.SetColumn(amount,1);row.Children.Add(amount);stack.Children.Add(row);
   var line=new Grid{Margin=new Thickness(0,5,0,0)};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});
   var track=new Border{Height=4,CornerRadius=new CornerRadius(2),Background=Brush("#1D5386AF"),ClipToBounds=true,VerticalAlignment=VerticalAlignment.Center};
   var fill=new Border{Height=4,CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left,Width=0,Background=new LinearGradientBrush(((SolidColorBrush)Brush(i==0?"#6CACD0":colors[i])).Color,((SolidColorBrush)Brush(i==0?"#A0D5EB":i==1?"#A5D9C7":i==2?"#C5C4EB":"#EAC5B2")).Color,0)};track.Child=fill;line.Children.Add(track);
   if(hasRatio)track.Loaded+=(s,e)=>fill.BeginAnimation(FrameworkElement.WidthProperty,new DoubleAnimation(Math.Max(ratio>0?3:0,track.ActualWidth*ratio),TimeSpan.FromMilliseconds(350)));
   var percent=Text(status=="stale"?"缓存":hasRatio?Math.Round(ratio*100)+"%":"—",8,"#7C9DB4");percent.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(percent,1);line.Children.Add(percent);
   if(views.Count>1){
    row.Children.Remove(label);
    var switchView=new Button{Content=name+" · "+(S(view,"key")=="week"?"周":"5h"),FontSize=10.5,Padding=new Thickness(0),Background=Brush("#00FFFFFF"),BorderBrush=Brush("#00FFFFFF"),Foreground=Brush("#52738D"),ToolTip="点击切换 5 小时 / 每周额度",HorizontalAlignment=HorizontalAlignment.Left};
    switchView.Click+=(s,e)=>{viewIndex[id]=(ix+1)%views.Count;Render();};row.Children.Add(switchView);
   }
   stack.Children.Add(line);return border;
  }
  bool TryEpoch(double n,out DateTimeOffset d){try{d=DateTimeOffset.FromUnixTimeSeconds((long)n);return true;}catch{d=DateTimeOffset.MinValue;return false;}}
  UIElement Logo(int i,string color) {
   var c=new Canvas{Width=24,Height=24,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
   string[] shape={"M3,12 C7,16 18,18 19,8 L22,6 L22,12 L19,14 C17,21 6,21 3,12 M7,11 C8,7 14,6 18,9 M9,6 L12,3 L14,7","M12,3 L19,7 L19,16 L12,20 L5,16 L5,7 Z M12,3 L12,12 L19,16 M5,7 L12,12 L5,16","M8,7 L3,12 L8,17 M16,7 L21,12 L16,17 M14,5 L10,19","M12,2 L12,22 M2,12 L22,12 M5,5 L19,19 M5,19 L19,5 M8,3 L16,21 M3,8 L21,16 M3,16 L21,8 M8,21 L16,3"};
   c.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(shape[i]),Stroke=Brush(color),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});return c;
  }
  void OpenSettings(int selected=-1) {
   config=Read(Path.Combine(root,"config.json"));
   BuildAccountSettings();BuildPortraitCards();
   rebuilding=true;Find<CheckBox>("PinToggle").IsChecked=win.Topmost;rebuilding=false;
   settings.Visibility=Visibility.Visible;Find<Popup>("SettingsPopup").IsOpen=true;
  }
  void BuildAccountSettings() {
   rebuilding=true;switches.Children.Clear();accountToggles.Clear();var accounts=Accounts;int connected=0;
   foreach(var a in accounts){
    string id=S(a,"id"),name=S(a,"display",id);var state=D(V(D(V(snapshot,"accounts")),id));
    string status=S(state,"status");if(status=="ok")connected++;
    int index=Array.IndexOf(ids,S(a,"kind"));if(index<0)index=4;
    var content=new StackPanel{Margin=new Thickness(0,0,10,0)};
    var headline=new Grid();headline.ColumnDefinitions.Add(new ColumnDefinition());headline.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
    var title=Text(name,12,"#3F627F",true);title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new Thickness(0,0,5,0);headline.Children.Add(title);
    var views=Items(V(state,"views"));var amount=Text(views.Count>0?S(views[0],"text"):"",12,colors[index],true);Grid.SetColumn(amount,1);headline.Children.Add(amount);content.Children.Add(headline);
    var source=Text(S(a,"source"),9,"#8CA2B4");source.Margin=new Thickness(0,5,0,0);source.TextTrimming=TextTrimming.CharacterEllipsis;content.Children.Add(source);
    string hint=status=="ok"?(S(state,"source").Contains("令牌")?"●  令牌额度已同步":S(state,"source").Contains("费用")?"●  本月费用已同步":"●  余量已同步"):status=="stale"?"◷  暂用上次数据":status=="limited"?"◦  已识别 · 额度接口不可用":"◦  暂未连接，稍后扫描";
    var statusText=Text(hint,9,status=="ok"?"#70A79F":"#AF9887");statusText.Margin=new Thickness(0,4,0,0);content.Children.Add(statusText);
    var visibility=Text(B(a,"visible")?"显示中":"已隐藏",9,B(a,"visible")?"#5E98AF":"#9BAFBE");visibility.Margin=new Thickness(0,4,0,0);content.Children.Add(visibility);
    var toggle=new CheckBox{Content=content,IsChecked=B(a,"visible"),ToolTip="在挂件中显示此账户 · 关闭后可随时重新打开\n"+S(a,"origin")+"\n"+S(state,"source")+"\n"+S(state,"detail"),Tag=id};
    System.Windows.Automation.AutomationProperties.SetName(toggle,"显示账户："+name);
    RoutedEventHandler handler=(s,e)=>{if(rebuilding)return;try{SetAccountVisible(new[]{id},toggle.IsChecked==true);visibility.Text=toggle.IsChecked==true?"显示中":"已隐藏";saveHint.Text=name+(toggle.IsChecked==true?"已放回挂件":"已隐藏")+" · 自动保存";}catch{saveHint.Text="保存失败，请检查目录权限或 config.json";rebuilding=true;toggle.IsChecked=AccountPreferences.IsVisible(config,id);rebuilding=false;}};
    toggle.Checked+=handler;toggle.Unchecked+=handler;accountToggles.Add(toggle);
    switches.Children.Add(new Border{Child=toggle,Background=Brush("#95FFFFFF"),BorderBrush=Brush("#C5FFFFFF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(12,3,12,3),Margin=new Thickness(0,0,0,7)});
   }
   if(accounts.Count==0){var empty=Text("尚未发现本机账户",11,"#7F9EB4");empty.Margin=new Thickness(10,14,10,18);switches.Children.Add(empty);}
   Find<TextBlock>("DiscoveryStatus").Text=busy?"正在读取本机登录和余额":accounts.Count+" 个账户 · "+connected+" 个已同步";
   var notices=V(snapshot,"notices") as IEnumerable;
   saveHint.Text="每 "+Math.Max(1,N(config,"refreshMinutes",10))+" 分钟刷新 · 开关自动保存";
   if(notices!=null)foreach(var n in notices)saveHint.Text=Convert.ToString(n);
   rebuilding=false;
  }
  void BuildPortraitCards() {
   var panel=Find<UniformGrid>("PortraitOptions");panel.Children.Clear();
   for(int i=0;i<portraitIds.Length;i++){
    string id=portraitIds[i];bool selected=id==portraitStyle;
    var body=new StackPanel();body.Children.Add(new Image{Source=new BitmapImage(new Uri(Path.Combine(root,portraitFiles[i]))),Width=48,Height=44,Stretch=Stretch.Uniform});
    var label=Text(portraitNames[i],9,selected?"#517FA6":"#8AA0B4");label.HorizontalAlignment=HorizontalAlignment.Center;label.Margin=new Thickness(0,5,0,0);body.Children.Add(label);
    var button=new Button{Content=body,Margin=new Thickness(i==0?0:3,0,i==2?0:3,0),Padding=new Thickness(4,7,4,7),Background=Brush(selected?"#B3D5EBFC":"#62FFFFFF"),BorderBrush=Brush(selected?"#9BBFDC":"#ACFFFFFF"),ToolTip=portraitNames[i]};
    button.Click+=(s,e)=>{SelectPortrait(id,true);saveHint.Text="已换成"+portraitNames[Array.IndexOf(portraitIds,id)]+" · 自动保存";};panel.Children.Add(button);
   }
  }
  void VerifyUi() {
   string original=File.ReadAllText(Path.Combine(root,"config.json"),Encoding.UTF8);testing=true;
   try {
    OpenSettings();int expected=accountToggles.Count;
    foreach(var toggle in accountToggles)toggle.IsChecked=false;
    if(cards.Children.Count!=0||Find<FrameworkElement>("EmptyState").Visibility!=Visibility.Visible)throw new Exception("All-hidden UI check failed");
    if(Accounts.Any(a=>B(a,"visible")))throw new Exception("Visibility persistence check failed");
    if(expected>0){accountToggles[0].IsChecked=true;if(cards.Children.Count!=1)throw new Exception("Single-account UI check failed");}
    foreach(var toggle in accountToggles)toggle.IsChecked=true;
    if(cards.Children.Count!=expected)throw new Exception("Dynamic-account UI check failed");
    Write(Path.Combine(data,"ui-verification.json"),new{allHidden=true,singleAccount=expected>0,dynamicAccounts=expected,visibilityPersisted=true});
   }finally{
    File.WriteAllText(Path.Combine(root,"config.json"),original,new UTF8Encoding(false));config=Read(Path.Combine(root,"config.json"));
    Find<Popup>("SettingsPopup").IsOpen=false;settings.Visibility=Visibility.Collapsed;dashboard.Visibility=Visibility.Visible;Render();testing=false;
   }
  }
  void VerifyPortraits() {
   string original=portraitStyle;
   try {
    foreach(MenuItem item in Find<MenuItem>("MenuPortraits").Items){
     item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
     string selected=Convert.ToString(item.Tag);
     if(portraitStyle!=selected||S(Read(Path.Combine(data,"appearance.json")),"mascot")!=selected)throw new Exception("Portrait selection was not persisted");
     if(Find<MenuItem>("MenuPortraits").Items.Cast<MenuItem>().Count(m=>m.IsChecked)!=1)throw new Exception("Portrait selection is not exclusive");
     var source=Find<Image>("Mascot").Source as BitmapImage;
     if(source==null||!source.UriSource.LocalPath.EndsWith(portraitFiles[Array.IndexOf(portraitIds,selected)]))throw new Exception("Portrait image was not switched");
    }
    Write(Path.Combine(data,"portrait-verification.json"),new{threeImagesLoaded=true,menuSwitches=true,exclusiveSelection=true,selectionPersisted=true});
   }finally{SelectPortrait(original,true);}
  }
  void Refract() {
   // Capture only at startup or explicit refresh; moving preserves the current glass surface.
   win.Opacity=0;var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(110)};
   t.Tick+=(s,e)=>{t.Stop();try{Capture();}catch{}win.Opacity=1;};t.Start();
  }
  void Capture() {
   var shell=Find<FrameworkElement>("Shell");if(shell.ActualWidth<1||shell.ActualHeight<1)return;
   var p=shell.PointToScreen(new Point(0,0));var source=PresentationSource.FromVisual(win);var m=source.CompositionTarget.TransformToDevice;
   const int pad=45;int width=(int)Math.Ceiling(shell.ActualWidth*m.M11),height=(int)Math.Ceiling(shell.ActualHeight*m.M22);
   using(var bmp=new System.Drawing.Bitmap(width+pad*2,height+pad*2)){
    using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen((int)p.X-pad,(int)p.Y-pad,0,0,bmp.Size);
    using(var toned=new System.Drawing.Bitmap(bmp.Width,bmp.Height)){
     using(var g=System.Drawing.Graphics.FromImage(toned)){
      var matrix=new System.Drawing.Imaging.ColorMatrix(new float[][]{new float[]{.43f,.12f,.10f,0,0},new float[]{.16f,.50f,.19f,0,0},new float[]{.04f,.06f,.55f,0,0},new float[]{0,0,0,1,0},new float[]{.20f,.26f,.30f,0,1}});
      using(var attrs=new System.Drawing.Imaging.ImageAttributes()){attrs.SetColorMatrix(matrix);g.DrawImage(bmp,new System.Drawing.Rectangle(0,0,bmp.Width,bmp.Height),0,0,bmp.Width,bmp.Height,System.Drawing.GraphicsUnit.Pixel,attrs);}
     }
     using(var g=System.Drawing.Graphics.FromImage(bmp))g.DrawImageUnscaled(toned,0,0);
    }
    BitmapImage image;
    using(var ms=new MemoryStream()){bmp.Save(ms,System.Drawing.Imaging.ImageFormat.Png);ms.Position=0;image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=ms;image.EndInit();image.Freeze();}
    var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(image,new Rect(0,0,width+pad*2,height+pad*2));visual.Effect=new BlurEffect{Radius=32,RenderingBias=RenderingBias.Quality};
    var blurred=new RenderTargetBitmap(width+pad*2,height+pad*2,96,96,PixelFormats.Pbgra32);blurred.Render(visual);blurred.Freeze();
    var crop=new CroppedBitmap(blurred,new Int32Rect(pad,pad,width,height));crop.Freeze();
    Find<Border>("Backdrop").Background=new ImageBrush(crop){Stretch=Stretch.Fill,Opacity=0.62};
   }
  }
  void SavePreview(string file) {
   win.UpdateLayout();FrameworkElement target=settingsPreview?Find<Popup>("SettingsPopup").Child as FrameworkElement:win;
   target.UpdateLayout();double extraWidth=settingsPreview?target.Margin.Left+target.Margin.Right:0,extraHeight=settingsPreview?target.Margin.Top+target.Margin.Bottom:0;
   var image=new RenderTargetBitmap((int)Math.Ceiling((target.ActualWidth+extraWidth)*2),(int)Math.Ceiling((target.ActualHeight+extraHeight)*2),192,192,PixelFormats.Pbgra32);image.Render(target);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(file))encoder.Save(stream);
  }
 }
}
