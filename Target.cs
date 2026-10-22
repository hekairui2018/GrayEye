using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text.RegularExpressions;

static class Target {
 static Dictionary<string,object> data=new Dictionary<string,object>();
 public static string PathName {get{return Path.Combine(Store.Root,"target.json");}}
 public static void Load(){if(File.Exists(PathName))data=Store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(PathName));}
 static string Text(string key){return data.ContainsKey(key)?Convert.ToString(data[key]):"";}
 public static string Device {get{return Text("device_code");}}
 public static string Ip {get{return Text("ip");}}
 public static string Mac {get{return Text("mac").ToLowerInvariant().Replace(':','-');}}
 public static void Validate(){IPAddress ip;object flag;if(!data.TryGetValue("authorized",out flag)||!(flag is bool)||!(bool)flag||!Device.StartsWith("A6L-",StringComparison.Ordinal)||!IPAddress.TryParse(Ip,out ip)||ip.AddressFamily!=AddressFamily.InterNetwork||!Regex.IsMatch(Mac,@"^[0-9a-f]{2}(-[0-9a-f]{2}){5}$"))throw new Preconditions("尚未配置授权A6L。请在本机target.json填入现场确认的设备名、IPv4、MAC及authorized=true；不会自动扫描或试发未知命令。");}
 public static string EmptyDate {get{return Text("empty_ports_confirmed_date");}}
 public static bool EmptyConfirmed(int port){object ports;if(!data.TryGetValue("empty_ports_user_confirmed",out ports)||!(ports is System.Collections.IEnumerable))return false;foreach(object p in (System.Collections.IEnumerable)ports)if(Convert.ToString(p)==port.ToString())return true;return false;}
 public static string PortNote(int port){return EmptyConfirmed(port)?"未接卡：用户现场确认 "+EmptyDate+"；本次未探测":"连接状态待确认；本次未探测";}
 public static object PortStates(){return Enumerable.Range(2,3).Select(p=>new{port=p,status=EmptyConfirmed(p)?"未接卡":"待确认",source=EmptyConfirmed(p)?"user_confirmed":"not_collected",confirmed_date=EmptyConfirmed(p)?EmptyDate:null,read_this_attempt=false}).ToArray();}
}
