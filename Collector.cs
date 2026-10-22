using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.IO.Compression;
using System.Reflection;

[assembly:AssemblyTitle("灰eye A6L只读采集助手")]
[assembly:AssemblyVersion("0.2.2.0")]
[assembly:AssemblyFileVersion("0.2.2.1022")]
[assembly:AssemblyInformationalVersion("0.2.2-test.20261022")]

static class Store {
 public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"A6LCollector");
 public static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
 public static string Config=Path.Combine(Root,"settings.json");
 public static void Init(){Directory.CreateDirectory(Root);Directory.CreateDirectory(Path.Combine(Root,"runs"));Directory.CreateDirectory(Path.Combine(Root,"logs"));if(!File.Exists(Config)) File.WriteAllText(Config,"{\"endpoint\":\"\",\"upload_key\":\"\",\"receiver_ready\":false,\"auto_send\":false}",Encoding.UTF8);}
 public static void Log(string s){lock(typeof(Store)) File.AppendAllText(Path.Combine(Root,"logs",DateTime.Now.ToString("yyyyMMdd")+".log"),DateTimeOffset.Now.ToString("o")+" "+s+Environment.NewLine,Encoding.UTF8);}
 public static void Save(string file,object value){File.WriteAllText(file,Json.Serialize(value),Encoding.UTF8);}
 public static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 public static string Csv(params object[] cells){return string.Join(",",cells.Select(x=>"\""+Convert.ToString(x).Replace("\"","\"\"")+"\""));}
 public static Stream Resource(string n){return Assembly.GetExecutingAssembly().GetManifestResourceStream(n);}
}

sealed class Wire:IDisposable {
 TcpClient client; NetworkStream stream; string folder; List<byte[]> tx=new List<byte[]>(),rx=new List<byte[]>();int xmlIndex;CancellationToken cancel;
 public string Guid;
 public Wire(string dir,CancellationToken token){folder=dir;cancel=token;client=new TcpClient();try{cancel.ThrowIfCancellationRequested();var task=client.ConnectAsync(Collect.Ip,10001);if(!task.Wait(5000))throw new IOException("A6L连接超时");stream=client.GetStream();stream.ReadTimeout=5000;stream.WriteTimeout=5000;Send(new byte[]{8,0,1,32,5,0,0,1});if(BitConverter.ToUInt16(Packet(),2)!=0x2002)throw new IOException("SDK握手失败");Xml("##GUID","GetIFVersion",new XElement("version",new XAttribute("value","1000000")));var root=Receive();Guid=(string)root.Attribute("guid");if(string.IsNullOrEmpty(Guid)||Guid=="##GUID")throw new IOException("未建立独立会话");}catch{Dispose();throw;}}
 void Send(byte[] b){cancel.ThrowIfCancellationRequested();tx.Add(b);stream.Write(b,0,b.Length);}
 byte[] Exact(int n){var b=new byte[n];int pos=0;while(pos<n){cancel.ThrowIfCancellationRequested();int k=stream.Read(b,pos,n-pos);if(k==0)throw new IOException("设备断开连接");rx.Add(b.Skip(pos).Take(k).ToArray());pos+=k;}return b;}
 byte[] Packet(){for(int count=0;count<100;count++){var h=Exact(4);int len=BitConverter.ToUInt16(h,0);if(len<4)throw new IOException("帧长度错误");var b=h.Concat(Exact(len-4)).ToArray();if(BitConverter.ToUInt16(b,2)==0x5f){Send(new byte[]{4,0,96,0});continue;}return b;}throw new IOException("心跳次数异常");}
 void Xml(string guid,string method,XElement inner){var entry=new XElement("in",new XAttribute("method",method));if(inner!=null)entry.Add(inner);if(method=="GetBoxHwConfig")entry.Add(new XAttribute("NetCard","1"));var xml=Encoding.UTF8.GetBytes(new XElement("sdk",new XAttribute("guid",guid),entry).ToString(SaveOptions.DisableFormatting));var head=BitConverter.GetBytes((ushort)(xml.Length+12)).Concat(BitConverter.GetBytes((ushort)0x2003)).Concat(BitConverter.GetBytes(xml.Length)).Concat(new byte[4]).ToArray();Send(head.Concat(xml).ToArray());}
 public XElement Receive(){var b=Packet();if(b.Length<12||BitConverter.ToUInt16(b,2)!=0x2004||BitConverter.ToInt32(b,8)!=0||BitConverter.ToInt32(b,4)!=b.Length-12)throw new IOException("响应类型/分片未支持，已停止");File.WriteAllBytes(Path.Combine(folder,"wire_"+(xmlIndex++).ToString("D3")+".xml"),b.Skip(12).ToArray());return XElement.Parse(Encoding.UTF8.GetString(b,12,b.Length-12));}
 public XElement Connections(){Xml(Guid,"GetBoxHwConfig",null);var root=Receive();var ack=root.Element("out");if(ack==null||(string)ack.Attribute("result")!="kSuccess")throw new IOException("连接参数读取失败");return root;}
 public void ReadParameters(){Send(new byte[]{4,0,10,4});if(!Packet().SequenceEqual(new byte[]{5,0,11,4,0}))throw new IOException("设备未允许本次读取");const string command="VVVVVVVVVdUBAQAAAgAAABAAAAAAAAAAAAAAAAAAAAAApmP0Pg==";Xml(Guid,"SetBoxHwConfig",new XElement("Raw",new XAttribute("Value",command)));}
 public void Dispose(){try{if(client!=null)client.Close();}finally{File.WriteAllBytes(Path.Combine(folder,"sent.bin"),tx.SelectMany(x=>x).ToArray());File.WriteAllBytes(Path.Combine(folder,"received.bin"),rx.SelectMany(x=>x).ToArray());}}
}

static class Collect {
 public static string Device {get{return Target.Device;}} public static string Ip {get{return Target.Ip;}} public static string Mac {get{return Target.Mac;}}
 public static uint Crc(byte[] raw){uint c=0xffffffff;for(int i=8;i<raw.Length-4;i++){c^=raw[i];for(int b=0;b<8;b++)c=(c&1)!=0?(c>>1)^0xedb88320:c>>1;}return ~c;}
 public static void Validate(byte[] b){if(b.Length<21||!b.Take(8).SequenceEqual(new byte[]{85,85,85,85,85,85,85,213})||b[15]*256+b[16]!=b.Length-21||Crc(b)!=BitConverter.ToUInt32(b,b.Length-4))throw new IOException("原始回传帧头、长度或CRC校验失败");}
 public static string Check(){
  Target.Validate();
  // Never open a competing hardware session while another program is connected.
  if(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(c=>c.RemoteEndPoint.Address.ToString()==Ip&&c.RemoteEndPoint.Port==10001&&c.State==TcpState.Established))throw new Preconditions("HDSet或其他程序仍连接A6L。请结束该设备会话后采集，避免并发干扰。");
  using(var ping=new Ping()){try{ping.Send(Ip,700);}catch(PingException){}}
  var psi=new ProcessStartInfo("arp.exe","-a "+Ip){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};string output;
  using(var p=Process.Start(psi)){output=p.StandardOutput.ReadToEnd().ToLowerInvariant();p.WaitForExit();}
  if(!output.Split('\n').Any(line=>line.Contains(Ip)&&line.Contains(Mac)))throw new Preconditions("未发现授权A6L的IP/MAC对应关系；本版仅支持已验证的本机地址，不发送参数读取。");return "授权IP/MAC已核对（ARP）；设备名来自本机授权配置";
 }
 public static Dictionary<string,object> Decode(byte[] raw,string dir){Validate(raw);if(raw.Length!=533||raw[12]!=1||raw[13]!=0||raw[14]!=0)throw new IOException("不是主参数段");var values=new Dictionary<string,object>();var csv=new List<string>{Store.Csv("发送设备","输出网口","目标接收卡","HDSet字段","设备原值","范围")};using(var reader=new StreamReader(Store.Resource("fields.map"),Encoding.UTF8)){string line;while((line=reader.ReadLine())!=null){var parts=line.Split('|');var bits=parts[1].Split(',').Select(int.Parse).ToArray();ulong value=0;for(int n=0;n<bits.Length;n++){int b=bits[n];if(b==-2||(b>=0&&((raw[17+b/8]>>(b%8))&1)!=0))value|=1UL<<n;}values.Add(parts[0],value);csv.Add(Store.Csv(Device,1,1,parts[0],value,"内部原值；未转换单位或枚举"));}}File.WriteAllLines(Path.Combine(dir,"接收卡参数.csv"),csv,Encoding.UTF8);return values;}
 public static string Run(Action<string> status,TaskOperation task,CancellationToken cancel){string id=task.RunId+"_a"+task.Attempt.ToString("D4");string dir=Path.Combine(Store.Root,"runs",id);Directory.CreateDirectory(dir);string phase="identity_preflight",origin="automation";Exception failure=null;var report=new Dictionary<string,object>{{"protocol","a6l.collection.v1"},{"run_id",id},{"task_run_id",task.RunId},{"attempt",task.Attempt},{"device_code",Device},{"sender_model","A6L"},{"ip",Ip},{"mac",Mac},{"output_port",1},{"receiver_index",1},{"started_at",DateTimeOffset.Now.ToString("o")},{"success",false},{"restoration_verified",false},{"write_operations",0},{"app_version",TaskOperation.Version}};
  try{cancel.ThrowIfCancellationRequested();status("检测授权A6L及连接占用…");report["identity_source"]=Check();cancel.ThrowIfCancellationRequested();var blocks=new Dictionary<string,byte[]>();phase="connection_read";
   using(var wire=new Wire(dir,cancel)){status("读取发送设备与连屏原始参数…");var root=wire.Connections();var ack=root.Element("out");int n=0;foreach(var node in ack.Elements("Raw")){byte[] raw=Convert.FromBase64String((string)node.Attribute("Value"));Validate(raw);File.WriteAllBytes(Path.Combine(dir,"connection_"+(n++)+".bin"),raw);}report["connection_raw_blocks"]=n;report["screen_fields"]=ack.Elements("Screen").Select(x=>x.Attributes().ToDictionary(a=>a.Name.ToString(),a=>(object)a.Value)).ToArray();phase="receiver_read";
    status("回读网口1 / 接收卡1…");wire.ReadParameters();var deadline=DateTime.UtcNow.AddSeconds(18);int responses=0;
    while(blocks.Count<11&&DateTime.UtcNow<deadline&&responses++<40){cancel.ThrowIfCancellationRequested();root=wire.Receive();var error=root.Element("out");if(error!=null&&(string)error.Attribute("result")!="kSuccess")throw new IOException("设备拒绝读取："+(string)error.Attribute("result"));var node=root.Element("Reaspone");if(node==null)continue;byte[] raw=Convert.FromBase64String((string)node.Attribute("Value"));File.WriteAllBytes(Path.Combine(dir,"response_"+responses.ToString("D2")+".bin"),raw);Validate(raw);if(raw.Length!=533)continue;string key=BitConverter.ToString(raw,12,3).Replace("-","");if(!(key=="010000"||key=="020000"||key=="030000"||Enumerable.Range(0,8).Any(i=>key=="04000"+i)))throw new IOException("出现未验证的参数段编号");if(blocks.ContainsKey(key)&&!blocks[key].SequenceEqual(raw))throw new IOException("同段重复数据冲突");blocks[key]=raw;report["received_blocks"]=blocks.Count;}
   }
   if(blocks.Count!=11)throw new IOException("回传不完整："+blocks.Count+"/11 段，已保存实际收到数据");cancel.ThrowIfCancellationRequested();phase="parameter_decode";report["parameters"]=Decode(blocks["010000"],dir);report["verified_fields"]=133;report["crc_valid"]=true;report["received_blocks"]=11;report["success"]=true;report["classification"]="原始参数采集包；不是可恢复SSR备份";
   File.WriteAllLines(Path.Combine(dir,"发送设备与网口.csv"),new[]{Store.Csv("发送设备","IP","MAC","输出网口","目标卡","说明"),Store.Csv(Device,Ip,Mac,1,1,"本次实际读取目标，不代表全网在线卡数量"),Store.Csv(Device,Ip,Mac,2,"",Target.PortNote(2)),Store.Csv(Device,Ip,Mac,3,"",Target.PortNote(3)),Store.Csv(Device,Ip,Mac,4,"",Target.PortNote(4))},Encoding.UTF8);
  }catch(Exception ex){failure=ex;if(ex is OperationCanceledException||cancel.IsCancellationRequested)origin="human";else if(ex is Preconditions){origin="unknown";phase="precondition";}report["success"]=false;report["error"]=origin=="human"?"用户手动停止":ex.Message;report["error_origin"]=origin;report["error_node"]=phase;Store.Log(id+" collection failed: "+report["error"]);}finally{report["finished_at"]=DateTimeOffset.Now.ToString("o");Store.Save(Path.Combine(dir,"manifest.json"),report);var hashes=Directory.GetFiles(dir).Select(p=>Store.Hash(File.ReadAllBytes(p))+"  "+Path.GetFileName(p));File.WriteAllLines(Path.Combine(dir,"SHA256SUMS.txt"),hashes,Encoding.UTF8);ZipFile.CreateFromDirectory(dir,dir+".zip");Store.Log(id+" saved success="+report["success"]);}
  if(failure!=null)throw new CollectionFailure((string)report["error"]+"\n数据已保留："+dir,dir,origin,phase);return dir;
 }
}

sealed class Ball:Form {
 Image image;Point down,origin;bool moved,busy,closing;NotifyIcon tray;Label state; ToolTip tip=new ToolTip();CancellationTokenSource cancellation;System.Windows.Forms.Timer retryTimer;
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08080080;return p;}}
 public Ball(){Text="灰eye A6L悬浮采集球";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Size=new Size(LayeredBall.Pixels(80),LayeredBall.Pixels(80));StartPosition=FormStartPosition.Manual;Location=new Point(Screen.PrimaryScreen.WorkingArea.Right-100,Screen.PrimaryScreen.WorkingArea.Bottom-260);using(var s=Store.Resource("eye.png")) image=new Bitmap(s);Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);Cursor=Cursors.Hand;
  var menu=new ContextMenuStrip();menu.Items.Add("新任务：采集A6L网口1 / 卡1",null,(s,e)=>Run());menu.Items.Add("重试上次未完成任务（沿用ID）",null,(s,e)=>RunTask(true));menu.Items.Add("人工停止当前采集",null,(s,e)=>Stop());menu.Items.Add("打开本地采集目录",null,(s,e)=>Process.Start("explorer.exe",Path.Combine(Store.Root,"runs")));menu.Items.Add("admin 管理面板",null,(s,e)=>Admin());menu.Items.Add("send 手动补传任务事件",null,(s,e)=>Send());menu.Items.Add("导出当天日志到桌面",null,(s,e)=>Export());menu.Items.Add("退出",null,(s,e)=>Close());ContextMenuStrip=menu;
  MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){down=MousePosition;origin=Location;moved=false;}};MouseMove+=(s,e)=>{if(e.Button==MouseButtons.Left){var delta=new Size(MousePosition.X-down.X,MousePosition.Y-down.Y);if(Math.Abs(delta.Width)+Math.Abs(delta.Height)>5)moved=true;if(moved)Location=origin+delta;}};MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Left&&!moved)Run();};
  tray=new NotifyIcon{Icon=Icon,Text="灰eye：右键管理",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>{Show();TopMost=true;};tip.SetToolTip(this,"单击新任务；右键重试 / 停止 / admin / send");FormClosing+=(s,e)=>{if(busy){e.Cancel=true;closing=true;Stop();Notice("正在保存当前任务后退出…");}};FormClosed+=(s,e)=>{retryTimer.Stop();retryTimer.Dispose();tray.Visible=false;tray.Dispose();image.Dispose();};Store.Log("app started "+TaskOperation.Version);retryTimer=new System.Windows.Forms.Timer{Interval=60000};retryTimer.Tick+=(s,e)=>{if(Upload.Auto()&&!busy)Send();};retryTimer.Start();Shown+=(s,e)=>{if(Upload.Auto())Send();};
 }
 protected override void OnShown(EventArgs e){base.OnShown(e);LayeredBall.Draw(Handle,image,Left,Top,Width,Height);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(image!=null&&IsHandleCreated&&Visible)LayeredBall.Draw(Handle,image,Left,Top,Width,Height);}
 void Notice(string text){Store.Log(text);if(state!=null&&!state.IsDisposed)state.Text=text;tray.BalloonTipTitle="A6L采集助手";tray.BalloonTipText=text.Length>220?text.Substring(0,220):text;tray.ShowBalloonTip(5000);}
 void Run(){RunTask(false);}
 void Stop(){if(cancellation!=null){cancellation.Cancel();Notice("已请求人工停止，正在保留本次已收数据");}else Notice("当前没有正在执行的硬件采集");}
 async void RunTask(bool retry){if(busy){Notice("正在处理，请等待本次完成");return;}busy=true;cancellation=new CancellationTokenSource();bool completed=false;try{var op=retry?TaskOperation.RetryLatest():TaskOperation.New();await Task.Run(()=>TaskRunner.Execute(op,s=>BeginInvoke((Action)(()=>tip.SetToolTip(this,s))),cancellation.Token));completed=true;Notice("采集完成：11段及133字段已本地保存。task="+op.RunId);}catch(Exception ex){Notice(ex.Message);}finally{cancellation.Dispose();cancellation=null;busy=false;tip.SetToolTip(this,"单击新任务；右键重试 / 停止 / admin / send");}if(closing){Close();return;}if(Upload.Auto()){Notice((completed?"采集已完成。":"本次采集未完成。")+"正在补传任务事件，上传状态单独显示");Send();}}
 async void Send(){if(busy){Notice("正在处理，请等待");return;}busy=true;try{Notice(await Task.Run(()=>Upload.Pending()));}catch(Exception ex){Notice("回传失败（不改变采集结果）："+ex.Message);}finally{busy=false;if(closing)Close();}}
 void Export(){try{var date=DateTime.Now.ToString("yyyyMMdd");string source=Path.Combine(Store.Root,"logs",date+".log");string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);string zip=Path.Combine(desktop,"A6L采集助手日志_"+DateTime.Now.ToString("yyyyMMdd_HHmmssfff")+".zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))archive.CreateEntryFromFile(source,date+".log");var files=new System.Collections.Specialized.StringCollection();files.Add(zip);Clipboard.SetFileDropList(files);Notice("日志副本已导出桌面并复制到剪贴板："+zip);}catch(Exception ex){Notice("导出失败："+ex.Message);}}
 void Admin(){var f=new Form{Text="灰eye admin "+TaskOperation.Version,Size=new Size(600,340),StartPosition=FormStartPosition.CenterScreen,Icon=Icon};var flow=new FlowLayoutPanel{Dock=DockStyle.Top,Height=135,Padding=new Padding(12)};Action<string,Action> button=(label,action)=>{var b=new Button{Text=label,AutoSize=true,Height=32};b.Click+=(s,e)=>action();flow.Controls.Add(b);};button("新任务采集",Run);button("重试上次任务",()=>RunTask(true));button("人工停止",Stop);button("send 手动补传",Send);button("导出日志到桌面",Export);button("配置接收端",()=>Process.Start("notepad.exe",Store.Config));button("配置授权A6L",()=>{if(!File.Exists(Target.PathName))DurableFile.Json(Target.PathName,new{device_code="",ip="",mac="",authorized=false,empty_ports_user_confirmed=new int[0],empty_ports_confirmed_date=""},false);Process.Start("notepad.exe",Target.PathName);Notice("保存授权设备配置后请重启灰eye");});button("打开采集目录",()=>Process.Start("explorer.exe",Path.Combine(Store.Root,"runs")));button("查看任务与回执",()=>{Directory.CreateDirectory(TaskOperation.TasksRoot);Process.Start("explorer.exe",TaskOperation.TasksRoot);});
  state=new Label{Dock=DockStyle.Fill,Padding=new Padding(15),Text="每次单击为新任务；重试沿用原任务ID。\n业务主表与内部异常表由接收端分流，同一任务可同时进入两表。\n已接收不等于两表同步；上传失败不改变采集结果。\n仅A6L网口1卡1。先结束HDSet设备会话。不是可恢复SSR。"};f.Controls.Add(state);f.Controls.Add(flow);f.KeyPreview=true;string seq="";DateTime last=DateTime.MinValue;f.KeyPress+=(s,e)=>{if((DateTime.Now-last).TotalSeconds>2)seq="";last=DateTime.Now;seq=(seq+char.ToLowerInvariant(e.KeyChar));if(seq.Length>5)seq=seq.Substring(seq.Length-5);if(seq.EndsWith("admin")){seq="";Export();}else if(seq.EndsWith("send")){seq="";Send();}};f.Show();}
}

static class Program {
 [STAThread]static int Main(string[] args){Store.Init();try{Target.Load();if(args.Length>0&&args[0]=="--selftest")return SelfTest(args.Length>1?args[1]:null);if(args.Length>0&&args[0]=="--collect"){Console.WriteLine(TaskRunner.Execute(TaskOperation.New(),Console.WriteLine,CancellationToken.None));return 0;}bool owned;using(var mutex=new Mutex(true,"Local\\A6LCollector.Test.v1",out owned)){if(!owned)return 0;TaskOperation.Recover();LayeredBall.EnableDpi();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Ball());}return 0;}catch(Exception ex){Store.Log("fatal: "+ex.Message);Console.Error.WriteLine(ex.Message);return 1;}}
 static int SelfTest(string path){if(path==null)throw new ArgumentException("--selftest requires a saved response_*.bin path");var raw=File.ReadAllBytes(path);Collect.Validate(raw);var test=Path.Combine(Store.Root,"selftest");Directory.CreateDirectory(test);var fields=Collect.Decode(raw,test);if(fields.Count!=133)throw new Exception("field count");var broken=(byte[])raw.Clone();broken[19]^=1;bool rejected=false;try{Collect.Validate(broken);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("CRC did not reject");Store.Save(Path.Combine(test,"result.json"),new{success=true,fields=fields.Count,crc_corruption_rejected=true,device_operations=0,values=fields});Console.WriteLine("PASS: 133 fields; CRC rejection; no device or upload operations");return 0;}
}
