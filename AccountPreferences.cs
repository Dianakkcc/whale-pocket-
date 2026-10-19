using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace WhalePocketShared {
 // Both windows use the same visibility rule and merge changes with the latest file.
 public static class AccountPreferences {
  public static void EnsureConfig(string root){
   string file=Path.Combine(root,"config.json");if(File.Exists(file))return;
   string example=Path.Combine(root,"config.example.json");
   string text=File.Exists(example)?File.ReadAllText(example,Encoding.UTF8):"{\"refreshMinutes\":10,\"accounts\":[]}";
   using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(text);
  }
  static object Value(Dictionary<string,object> item,string key){object value;return item!=null&&item.TryGetValue(key,out value)?value:null;}
  static string Text(Dictionary<string,object> item,string key){return Convert.ToString(Value(item,key));}
  static List<Dictionary<string,object>> Items(object value){
   var result=new List<Dictionary<string,object>>();var items=value as IEnumerable;
   if(items!=null&&!(value is string))foreach(var item in items){var entry=item as Dictionary<string,object>;if(entry!=null)result.Add(entry);}
   return result;
  }
  public static bool IsVisible(Dictionary<string,object> config,string id){
   var pref=Items(Value(config,"accounts")).FirstOrDefault(a=>Text(a,"id")==id);
   var value=Value(pref,"visible");return !(value is bool)||((bool)value);
  }
  public static List<Dictionary<string,object>> Accounts(Dictionary<string,object> config,Dictionary<string,object> snapshot){
   return Items(Value(snapshot,"catalog")).Select(a=>{
    var copy=new Dictionary<string,object>(a);copy["visible"]=IsVisible(config,Text(a,"id"));return copy;
   }).OrderBy(a=>Text(a,"kind")=="deepseek"?0:Text(a,"kind")=="relay"?1:2).ToList();
  }
  public static Dictionary<string,object> SetVisible(string root,IEnumerable<string> ids,bool visible){
   var file=Path.Combine(root,"config.json");var json=new JavaScriptSerializer();
   var config=File.Exists(file)?json.DeserializeObject(File.ReadAllText(file,Encoding.UTF8)) as Dictionary<string,object>:new Dictionary<string,object>();
   if(config==null)throw new InvalidDataException("账户设置格式异常，请先检查 config.json。");
   var accounts=Items(Value(config,"accounts"));
   foreach(string id in ids.Distinct()){
    var pref=accounts.FirstOrDefault(a=>Text(a,"id")==id);
    if(pref==null){pref=new Dictionary<string,object>{{"id",id}};accounts.Add(pref);}
    pref["visible"]=visible;
   }
   config["accounts"]=accounts;
   string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllText(temp,json.Serialize(config),new UTF8Encoding(false));
   if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
   return config;
  }
 }
}
