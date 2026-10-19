using System;
using System.IO;
using System.Linq;
using System.Windows;
namespace WhalePocket {
 internal static class SettingsEntry {
  [STAThread] static int Main(string[] args) {
   try {
    bool verify=args.Contains("--verify");
    Pocket.SettingsOnly=!verify;
    int index=Array.IndexOf(args,"--preview");
    Pocket.Run(AppDomain.CurrentDomain.BaseDirectory,verify,index>=0&&index+1<args.Length?args[index+1]:"",args.Contains("--settings-preview"));
    return 0;
   }catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-error.log"),e.ToString());if(!args.Contains("--verify"))MessageBox.Show("设置暂时无法打开，请查看 settings-error.log","鲸娘钱包");return 1;}
  }
 }
}
