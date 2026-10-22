using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

static class VisibleData {
 public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"灰eye采集资料");}}
 static string[] Files(string source){return Directory.GetFiles(source,"*",SearchOption.AllDirectories);}
 static string Relative(string root,string file){return file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1);}
 static bool Same(string a,string b){return File.Exists(b)&&Store.Hash(File.ReadAllBytes(a))==Store.Hash(File.ReadAllBytes(b));}
 static void CopyNew(string source,string target){Directory.CreateDirectory(Path.GetDirectoryName(target));if(File.Exists(target)){if(!Same(source,target))throw new IOException("导出目标已有不同内容，保留原文件："+target);return;}byte[] data=File.ReadAllBytes(source);using(var s=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){s.Write(data,0,data.Length);s.Flush(true);}if(Store.Hash(data)!=Store.Hash(File.ReadAllBytes(target)))throw new IOException("导出文件校验失败："+target);}
 public static string Folder(string source,string visibleRoot){Directory.CreateDirectory(visibleRoot);string destination=Path.Combine(visibleRoot,Path.GetFileName(source));string[] files=Files(source);if(files.Any(p=>File.Exists(Path.Combine(destination,Relative(source,p)))&&!Same(p,Path.Combine(destination,Relative(source,p)))))destination+="_导出_"+DateTime.Now.ToString("yyyyMMdd_HHmmssfff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6);Directory.CreateDirectory(destination);foreach(string file in files)CopyNew(file,Path.Combine(destination,Relative(source,file)));return destination;}
 public static string Run(string source,string visibleRoot=null){string root=visibleRoot??Root;string destination=Folder(source,root);if(File.Exists(source+".zip"))CopyNew(source+".zip",Path.Combine(root,Path.GetFileName(destination)+".zip"));return destination;}
 public static string Runs(){Directory.CreateDirectory(Root);string internalRoot=Path.Combine(Store.Root,"runs");if(Directory.Exists(internalRoot))foreach(string dir in Directory.GetDirectories(internalRoot))if(File.Exists(Path.Combine(dir,"manifest.json")))Run(dir);return Root;}
 public static string Tasks(){string destination=Path.Combine(Root,"任务日志与回执");Directory.CreateDirectory(destination);if(Directory.Exists(TaskOperation.TasksRoot))foreach(string task in Directory.GetDirectories(TaskOperation.TasksRoot))Folder(task,destination);return destination;}
 public static void Open(string path){Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});}
 public static void Outcome(TaskOperation task,string source){if(source==null)return;try{task.VisibleDirectory=Run(source);task.Log("采集资料已导出到桌面："+task.VisibleDirectory);}catch(Exception ex){task.ExportError=ex.Message;try{task.Log("采集原件保留，桌面副本导出失败："+ex.Message);task.Capture("exception","automation","visible_export",ex.Message,null);}catch{}}}
}
