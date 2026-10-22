using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Threading;

static class DurableFile {
 public static void Write(string path,byte[] bytes,bool replace){
  Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  using(var s=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(bytes,0,bytes.Length);s.Flush(true);}
  if(replace&&File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
 }
 public static void Json(string path,object obj,bool replace){Write(path,new UTF8Encoding(false).GetBytes(Store.Json.Serialize(obj)),replace);}
}

sealed class Preconditions:IOException {public Preconditions(string message):base(message){}}
sealed class CollectionFailure:IOException {
 public readonly string DirectoryPath,Origin,Node;
 public CollectionFailure(string message,string path,string origin,string node):base(message){DirectoryPath=path;Origin=origin;Node=node;}
}

sealed class TaskOperation {
 public const string Source="grayeye", BusinessProtocol="grayeye.collection.v1",Version="0.2.2-test.20261022";
 public static string TasksRoot {get{return Path.Combine(Store.Root,"tasks");}}
 readonly object gate=new object();public readonly string Folder,RunId,MachineId;public int Sequence,Attempt;public string Status,Phase;
 readonly bool simulation;Dictionary<string,object> lastCollection;
 string LogPath {get{return Path.Combine(Folder,"task.log");}}
 public TaskOperation(string root,string machine,string runId,bool test){Folder=Path.Combine(root,runId);RunId=runId;MachineId=machine;simulation=test;Directory.CreateDirectory(Path.Combine(Folder,"events"));Directory.CreateDirectory(Path.Combine(Folder,"receipts"));var files=Events();Sequence=files.Length==0?0:files.Max(p=>int.Parse(Path.GetFileNameWithoutExtension(p)));string state=Path.Combine(Folder,"state.json");if(File.Exists(state)){var s=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(state));Attempt=Convert.ToInt32(s["attempt"]);Status=Convert.ToString(s["status"]);Phase=Convert.ToString(s["phase"]);}else{Status="new";Phase="created";SaveState();}if(files.Length>0){var last=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(files.Last()));if(last.ContainsKey("collection"))lastCollection=(Dictionary<string,object>)last["collection"];if(Convert.ToInt32(last["attempt"])==Attempt){Status=Convert.ToString(last["task_state"]);Phase=Convert.ToString(last["node"]);}}}
 public static string Machine(){string p=Path.Combine(Store.Root,"machine_id.txt");lock(typeof(TaskOperation)){if(!File.Exists(p))DurableFile.Write(p,Encoding.UTF8.GetBytes("grayeye-"+Guid.NewGuid().ToString("N")),false);string id=File.ReadAllText(p).Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[A-Za-z0-9_.:-]{1,160}$"))throw new IOException("本地machine_id无效，已停止回传");return id;}}
 public static TaskOperation New(){return new TaskOperation(TasksRoot,Machine(),Guid.NewGuid().ToString("N"),false);}
 public static TaskOperation RetryLatest(){if(!Directory.Exists(TasksRoot))throw new IOException("没有可重试任务");foreach(string dir in Directory.GetDirectories(TasksRoot).OrderByDescending(Directory.GetLastWriteTimeUtc)){var op=new TaskOperation(TasksRoot,Machine(),Path.GetFileName(dir),false);if(new[]{"failed","stopped","blocked","interrupted"}.Contains(op.Status))return op;}throw new IOException("没有失败、停止或中断任务；单击悬浮球开始新任务");}
 public string[] Events(){return Directory.GetFiles(Path.Combine(Folder,"events"),"*.json").OrderBy(x=>x,StringComparer.Ordinal).ToArray();}
 void SaveState(){DurableFile.Json(Path.Combine(Folder,"state.json"),new{task_run_id=RunId,machine_id=MachineId,source=Source,attempt=Attempt,status=Status,phase=Phase,simulation=simulation},true);}
 public void Log(string message){lock(gate){using(var s=new FileStream(LogPath,FileMode.Append,FileAccess.Write,FileShare.Read)){var b=Encoding.UTF8.GetBytes(DateTimeOffset.Now.ToString("o")+" "+message+Environment.NewLine);s.Write(b,0,b.Length);s.Flush(true);}}}
 public void Progress(string phase){lock(gate){Phase=phase;Log(phase);SaveState();}}
 public void StartAttempt(){lock(gate){if(Status=="completed")throw new IOException("已完成任务不能重新执行；下一次独立采集需新任务ID");Attempt++;lastCollection=null;Status="running";Phase="start";Log("开始采集尝试 "+Attempt+"，task="+RunId);SaveState();Capture("snapshot",null,null,null,null);}}
 public string Capture(string kind,string origin,string node,string message,Dictionary<string,object> collection){lock(gate){
  if(kind!="snapshot"&&kind!="exception")throw new ArgumentException("unsupported event kind");
  if(kind=="exception"&&!new[]{"automation","human","unknown"}.Contains(origin))throw new ArgumentException("unknown exception origin");
  if(collection!=null)lastCollection=collection;
  var payload=new Dictionary<string,object>{{"task_protocol",1},{"task_run_id",RunId},{"source",Source},{"business_protocol",BusinessProtocol},{"machine_id",MachineId},{"event_id",Guid.NewGuid().ToString("N")},{"event_sequence",Sequence+1},{"event_kind",kind},{"log_date",DateTime.Now.ToString("yyyy-MM-dd")},{"client_ts",DateTimeOffset.Now.ToString("o")},{"content",File.Exists(LogPath)?File.ReadAllText(LogPath,Encoding.UTF8):""},{"app_version",Version},{"execution_mode",simulation?"simulation":"live"},{"attempt",Attempt},{"task_state",Status},{"node",node??Phase},
   {"device_code",Collect.Device},{"sender_model","A6L"},{"sender_model_source","authorized_target_model_prefix"},{"device_snapshot",new Dictionary<string,object>{{"device_code",Collect.Device},{"device_code_source","user_configured_authorized_target"},{"ip",Collect.Ip},{"mac",Collect.Mac},{"output_port",1},{"receiver_index",1},{"target_source","user_selected"},{"receiver_model",null},{"receiver_version",null},{"device_app_version",null},{"fpga_version",null},{"missing_fields_status","待确认：本流程尚未读取这些版本/型号"},{"ports_2_to_4",Target.PortStates()}}},
   {"site_environment",new{hdset_version=LocalHdsetVersion(),hdset_version_source="local_file_version",transport="TCP",target_port=10001,customer_issue_resolved=(object)null}}};
  if(kind=="exception"){payload["origin"]=origin;payload["message"]=message??"";}
  if(lastCollection!=null)payload["collection"]=lastCollection;
  string path=Path.Combine(Folder,"events",(Sequence+1).ToString("D10")+".json");DurableFile.Json(path,payload,false);Sequence++;return path;
 }}
 static string LocalHdsetVersion(){try{string path=@"C:\Program Files (x86)\HDSet\HDSet.exe";return File.Exists(path)?System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion:null;}catch{return null;}}
 Dictionary<string,object> Package(string dir,bool complete){
  if(dir==null||!File.Exists(Path.Combine(dir,"manifest.json")))return null;
  var manifest=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(dir,"manifest.json")));
  var result=new Dictionary<string,object>{{"attempt_id",Path.GetFileName(dir)},{"manifest",manifest},{"completion_verified",complete},{"checkpoint_source",complete?"grayeye_receiver_parameters_crc_and_archive":"grayeye_partial_readback"},{"restoration_verified",false}};
  if(File.Exists(dir+".zip")){byte[] b=File.ReadAllBytes(dir+".zip");result["package_sha256"]=Store.Hash(b);result["package_base64"]=Convert.ToBase64String(b);}
  return result;
 }
 public void Complete(string dir){lock(gate){var data=Package(dir,true);if(data==null)throw new IOException("缺少本次采集清单");var m=(Dictionary<string,object>)data["manifest"];if(!m.ContainsKey("task_run_id")||Convert.ToString(m["task_run_id"])!=RunId||!(bool)m["success"]||Convert.ToInt32(m["received_blocks"])!=11||!(bool)m["crc_valid"]||!data.ContainsKey("package_sha256"))throw new IOException("本次采集完成依据不全");Log("11段CRC通过，原始包已本地保存。参数采集完成；客户问题解决状态待确认。");Status="completed";Phase="collection_complete";Capture("snapshot",null,Phase,null,data);SaveState();}}
 public void Failed(string dir,string origin,string node,string message){lock(gate){Status=origin=="human"?"stopped":origin=="unknown"?"blocked":"failed";Phase=node;Log("本次尝试未完成 origin="+origin+" node="+node+" "+message);Capture(origin=="unknown"&&node=="precondition"?"snapshot":"exception",origin,node,message,Package(dir,false));SaveState();}}
 public static void Recover(){RecoverAt(TasksRoot,Machine(),false);}
 internal static void RecoverAt(string root,string machine,bool test){if(!Directory.Exists(root))return;foreach(string dir in Directory.GetDirectories(root)){var op=new TaskOperation(root,machine,Path.GetFileName(dir),test);if(op.Status=="running"){op.Status="interrupted";op.Phase="process_recovery";op.Log("上次进程未正常收尾，停止原因未知；未计为自动故障");op.Capture("exception","unknown",op.Phase,"进程中断，来源未确认",null);op.SaveState();}}}
}

static class TaskRunner {
 public static string Execute(TaskOperation task,Action<string> status,CancellationToken cancel){task.StartAttempt();try{string dir=Collect.Run(s=>{task.Progress(s);status(s);},task,cancel);task.Complete(dir);return dir;}catch(CollectionFailure ex){task.Failed(ex.DirectoryPath,ex.Origin,ex.Node,ex.Message);throw;}catch(OperationCanceledException){task.Failed(null,"human","user_stop","用户手动停止");throw;}catch(Exception ex){task.Failed(null,"automation","local_archive_or_journal",ex.Message);throw;}}
}

static class TaskDelivery {
 static readonly object gate=new object();
 public static string ReceiptPath(string taskFolder,string eventPath){return Path.Combine(taskFolder,"receipts",Path.GetFileName(eventPath)+".receipt");}
 public static string ValidateReply(byte[] original,Dictionary<string,object> reply){var input=Store.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(original));object value;if(!reply.TryGetValue("durable",out value)||!(value is bool)||!(bool)value||!reply.TryGetValue("accepted",out value)||!(value is bool)||!(bool)value||!reply.ContainsKey("task_run_id")||Convert.ToString(reply["task_run_id"])!=Convert.ToString(input["task_run_id"]))throw new IOException("回执未确认同任务原件已持久保存");if(reply.ContainsKey("event_id")&&Convert.ToString(reply["event_id"])!=Convert.ToString(input["event_id"]))throw new IOException("回执event_id不匹配");return Describe(reply);}
 public static string Describe(Dictionary<string,object> reply){object val;if(!reply.TryGetValue("sync",out val)||!(val is Dictionary<string,object>))return "已接收；两表同步状态未确认";var sync=(Dictionary<string,object>)val;string a=sync.ContainsKey("success")?Convert.ToString(sync["success"]):"unknown",b=sync.ContainsKey("failure")?Convert.ToString(sync["failure"]):"unknown";if(a=="synced"&&b=="synced")return "已接收；两表同步完成";if(new[]{a,b}.Any(x=>x=="synced")&&new[]{a,b}.All(x=>x=="synced"||x=="not-applicable"))return "已接收；适用表已同步（另一表不适用）";return "已接收；业务主表="+a+"，内部异常表="+b;}
 public static string Flush(string root,Func<byte[],Dictionary<string,object>> transport,bool refresh){lock(gate){if(!Directory.Exists(root))return "没有待发送任务事件";int sent=0,failed=0;string latest="";var errors=new List<string>();foreach(string folder in Directory.GetDirectories(root).OrderBy(x=>x,StringComparer.Ordinal)){
   var files=Directory.GetFiles(Path.Combine(folder,"events"),"*.json").OrderBy(x=>x,StringComparer.Ordinal).ToArray();bool deliveredLast=false;
   foreach(string file in files){string receipt=ReceiptPath(folder,file);byte[] original=File.ReadAllBytes(file);if(File.Exists(receipt)){var old=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt));if(Convert.ToString(old["request_sha256"])!=Store.Hash(original))throw new IOException("事件原件已被更改，停止补传："+Path.GetFileName(file));continue;}
    try{latest=SendOne(folder,file,original,transport);sent++;if(file==files.Last())deliveredLast=true;}catch(Exception ex){failed++;errors.Add(ex.Message);break;}
   }
   if(refresh&&!deliveredLast&&files.Length>0&&files.All(p=>File.Exists(ReceiptPath(folder,p)))){string last=files.Last();var old=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(ReceiptPath(folder,last)));var reply=(Dictionary<string,object>)old["server_reply"];string summary=Describe(reply);if(summary.Contains("queued")||summary.Contains("未确认")||summary.Contains("failed")||summary.Contains("configuration-required")){try{latest=SendOne(folder,last,File.ReadAllBytes(last),transport);sent++;}catch(Exception ex){failed++;errors.Add(ex.Message);}}else latest=summary;}
  }string result="本次提交事件="+sent+"，失败任务="+failed+"；"+Summary(root);if(failed>0)result+="；原件保留待补传："+errors.First();return result;}}
 internal static string Summary(string root){int received=0,pending=0,synced=0,waiting=0,na=0;foreach(var folder in Directory.GetDirectories(root)){var files=Directory.GetFiles(Path.Combine(folder,"events"),"*.json").OrderBy(x=>x,StringComparer.Ordinal).ToArray();if(files.Length==0)continue;if(!files.All(f=>File.Exists(ReceiptPath(folder,f)))){pending++;continue;}received++;var last=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(ReceiptPath(folder,files.Last())));var reply=(Dictionary<string,object>)last["server_reply"];object value;if(!reply.TryGetValue("sync",out value)||!(value is Dictionary<string,object>)){waiting++;continue;}var sync=(Dictionary<string,object>)value;string a=sync.ContainsKey("success")?Convert.ToString(sync["success"]):"unknown",b=sync.ContainsKey("failure")?Convert.ToString(sync["failure"]):"unknown";if(a=="not-applicable"&&b=="not-applicable")na++;else if(new[]{a,b}.All(x=>x=="synced"||x=="not-applicable"))synced++;else waiting++;}return "任务已接收="+received+"，待接收="+pending+"；适用表同步完成="+synced+"，待同步="+waiting+"，仅归档="+na;}
 static string SendOne(string folder,string file,byte[] original,Func<byte[],Dictionary<string,object>> transport){var reply=transport(original);string status=ValidateReply(original,reply);DurableFile.Json(ReceiptPath(folder,file),new{request_sha256=Store.Hash(original),accepted_at=DateTimeOffset.Now.ToString("o"),server_reply=reply},true);return status;}
}

static class Upload {
 public static bool Auto(){try{var c=Config();return c.ContainsKey("auto_send")&&c["auto_send"] is bool&&(bool)c["auto_send"];}catch{return false;}}
 static Dictionary<string,object> Config(){return Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Store.Config));}
 public static string Pending(){return TaskDelivery.Flush(TaskOperation.TasksRoot,Post,true);}
 static Dictionary<string,object> Post(byte[] original){
  EnsureLive(original);
  var c=Config();object v;if(!c.TryGetValue("receiver_ready",out v)||!(v is bool)||!(bool)v)throw new IOException("灰eye专用来源、凭据及两表尚未对齐；本地事件保留。请按对接说明配置receiver_ready。");
  string address=c.ContainsKey("endpoint")?Convert.ToString(c["endpoint"]):"",key=c.ContainsKey("upload_key")?Convert.ToString(c["upload_key"]):"";
  Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.AbsolutePath!="/api/huidu/log"||string.IsNullOrWhiteSpace(key))throw new IOException("需配置灰eye独立HTTPS /api/huidu/log地址及upload_key；不沿用旧Bearer令牌");
  return Exchange(uri,key,original,false);
 }
 internal static void EnsureLive(byte[] original){var p=Store.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(original));if(!p.ContainsKey("source")||Convert.ToString(p["source"])!=TaskOperation.Source||!p.ContainsKey("business_protocol")||Convert.ToString(p["business_protocol"])!=TaskOperation.BusinessProtocol||!p.ContainsKey("execution_mode")||Convert.ToString(p["execution_mode"])!="live")throw new IOException("禁止将模拟或非灰eye任务发往接收端");}
 internal static Dictionary<string,object> Exchange(Uri uri,string key,byte[] original,bool isolatedTest){
  if(isolatedTest&&!(uri.IsLoopback&&uri.Scheme=="http"))throw new IOException("隔离测试只允许本机HTTP地址");if(!isolatedTest&&uri.Scheme!="https")throw new IOException("真实发送需要HTTPS");
  ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var request=(HttpWebRequest)WebRequest.Create(uri);request.Method="POST";request.AllowAutoRedirect=false;request.Timeout=12000;request.ReadWriteTimeout=12000;request.ContentType="application/json; charset=utf-8";request.Headers["X-Upload-Key"]=key;request.ContentLength=original.Length;request.Proxy=null;
  try{using(var s=request.GetRequestStream())s.Write(original,0,original.Length);using(var response=(HttpWebResponse)request.GetResponse())using(var s=response.GetResponseStream())using(var memory=new MemoryStream()){byte[] buf=new byte[4096];int count;while((count=s.Read(buf,0,buf.Length))>0){memory.Write(buf,0,count);if(memory.Length>262144)throw new IOException("接收端回执过大");}return Store.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(memory.ToArray()));}}
  catch(WebException ex){var response=ex.Response as HttpWebResponse;throw new IOException(response==null?"网络发送失败，事件原件保留待补传":"接收端HTTP "+(int)response.StatusCode+"；原件保留，不修改事件重试");}
 }
}
