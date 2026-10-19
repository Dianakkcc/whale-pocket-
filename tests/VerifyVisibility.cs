using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class VerifyVisibility {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static object Field(object value,string name){return value.GetType().GetField(name,Flags).GetValue(value);}
 static void Call(object value,string name){value.GetType().GetMethod(name,Flags).Invoke(value,null);}
 static int RowCount(Form form){return ((ICollection)Field(form,"rows")).Count;}
 static ToolStripMenuItem Menu(Form form){Call(form,"BuildAccountMenu");return (ToolStripMenuItem)Field(form,"accountsMenu");}
 static Form Open(Type type){var form=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public,null,new object[]{new[]{"--offline"}},null);form.Show();Application.DoEvents();return form;}
 [STAThread] static int Main(string[] args){
  try{
   string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
   var assembly=Assembly.LoadFrom(Path.Combine(root,"WhaleWidget.exe"));var type=assembly.GetType("WhalePocketLite.Widget",true);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   using(var form=Open(type)){
    Check(RowCount(form)==3,"Expected three demo accounts");int fullHeight=form.Height;
    var momo=Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="relay_demo");momo.PerformClick();
    Check(RowCount(form)==2&&form.Height<fullHeight,"Hiding MoMo must remove its row and shrink the window");
    Check(!Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="relay_demo").Checked,"Hidden MoMo must be unchecked");
    Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="relay_demo").PerformClick();Check(RowCount(form)==3,"Rechecking MoMo must restore its row");
    Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>x.Text=="全部隐藏").PerformClick();Check(RowCount(form)==0,"All-hidden state must have no rows");
    Check(Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Count(x=>x.Tag!=null)==3,"Hidden accounts must remain selectable");
    Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>x.Text=="全部显示").PerformClick();Check(RowCount(form)==3,"Show-all must restore all accounts");
    Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="relay_demo").PerformClick();
    // A second process changes another account before this window saves again.
    var json=new JavaScriptSerializer();string configFile=Path.Combine(root,"config.json");
    var config=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(configFile));
    var entries=((object[])config["accounts"]).Cast<Dictionary<string,object>>().ToList();entries.Single(x=>Convert.ToString(x["id"])=="codex")["visible"]=false;config["accounts"]=entries;config["customField"]="keep";
    File.WriteAllText(configFile,json.Serialize(config),new UTF8Encoding(false));
    Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="deepseek").PerformClick();
    Check(RowCount(form)==0,"Latest settings from the other process must survive the merge");
    config=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(configFile));Check(Convert.ToString(config["customField"])=="keep","Unknown fields must be preserved");
    Menu(form).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="deepseek").PerformClick();Check(RowCount(form)==1,"Only DeepSeek should be visible");
    form.Close();
   }
   using(var restarted=Open(type)){Check(RowCount(restarted)==1,"Restart must remember the selection");Check(Menu(restarted).DropDownItems.OfType<ToolStripMenuItem>().Single(x=>Convert.ToString(x.Tag)=="relay_demo").Checked==false,"Restart must keep MoMo hidden");restarted.Close();}
   Console.WriteLine("PASS: MoMo hide/show, menu state, resize, all-hidden recovery, latest-config merge, restart persistence");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
