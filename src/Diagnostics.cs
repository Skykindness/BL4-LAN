using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace BL4LAN {
    public sealed class CaptureFrame {
        public DateTime Utc,ProcessStartedUtc;
        public int ProcessId;
        public bool Running,Responding;
        public double CpuSeconds;
        public long WorkingSet;
        public Dictionary<string,object> Runtime;
        public List<Dictionary<string,object>> Events;
        public string ReadIssue;
    }
    // This recorder describes observations. It never issues game/network commands.
    public sealed class DiagnosticRecorder {
        readonly string root;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=1024*1024,RecursionLimit=32};
        readonly Queue<Dictionary<string,object>> ring=new Queue<Dictionary<string,object>>();
        int ringBytes;
        readonly List<string> nativeEvents=new List<string>();readonly HashSet<string> nativeKeys=new HashSet<string>();int nativeBytes;
        readonly List<string> transitions=new List<string>();int transitionBytes;string lastTransition;
        readonly List<string> triggers=new List<string>();
        string id,role,request,instance,nativeAttempt,lastAttemptKey;
        int pid; DateTime started,sessionStarted,processStarted,triggerAt,missingAt,unresponsiveAt,nextSave;
        bool connected,recovered,worldAvailable,lastRunning;int lastPlayers;
        public string LastReport {get;private set;}
        public string LastError {get;private set;}
        public bool IsActive {get{return id!=null;}}
        public DiagnosticRecorder(string gameRoot){
            root=Path.GetFullPath(gameRoot);RefreshLastReport();
        }
        public void RefreshLastReport(){
            string dir=Path.Combine(root,"BL4-LAN-Data","Reports");
            if(Directory.Exists(dir)&&Plain(dir))LastReport=Directory.EnumerateFiles(dir,"BL4-LAN-report-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        public void Arm(string action,string requestId,int processId,DateTime processStart,DateTime utc){
            bool sameProcess=pid==processId&&processStarted==processStart;
            // Launch observation has no native attempt yet. The first real action
            // fills it in; it is not a retry and must not produce an empty ZIP.
            if(IsActive&&sameProcess&&nativeAttempt==null&&String.IsNullOrEmpty(request)&&triggers.Count==0&&!connected){
                role=Role(action);request=requestId;return;
            }
            if(IsActive){Finish(utc,"new_attempt");if(IsActive)return;}
            if(!sameProcess){
                ring.Clear();ringBytes=0;nativeEvents.Clear();nativeKeys.Clear();nativeBytes=0;
                transitions.Clear();transitionBytes=0;lastTransition=null;lastAttemptKey=null;
                connected=worldAvailable=false;lastPlayers=0;sessionStarted=utc;
            }
            id=Guid.NewGuid().ToString("N");role=Role(action);request=requestId;
            pid=processId;processStarted=processStart;started=utc;instance=null;nativeAttempt=null;
            triggers.Clear();triggerAt=missingAt=unresponsiveAt=nextSave=DateTime.MinValue;recovered=false;LastError=null;
        }
        static string Role(string action){return action=="host"?"host":action=="join"||action=="guest"?"guest":"unknown";}
        public void BindInstance(string expected){if(IsActive&&instance==null&&!String.IsNullOrEmpty(expected))instance=expected.Length>128?expected.Substring(0,128):expected;}
        static string Text(IDictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
        static double Number(IDictionary<string,object> d,string key){double n;return Double.TryParse(Text(d,key),NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:0;}
        static bool Flag(IDictionary<string,object> d,string key){return Text(d,key).Equals("True",StringComparison.OrdinalIgnoreCase);}
        static readonly DateTime Epoch=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);
        void Trigger(string reason,DateTime utc){if(!triggers.Contains(reason))triggers.Add(reason);if(triggerAt==DateTime.MinValue)triggerAt=utc;}
        public void Observe(CaptureFrame f){
            bool justExited=lastRunning&&!f.Running;lastRunning=f.Running;
            var d=f.Runtime;
            bool valid=d!=null&&Number(d,"pid")==f.ProcessId&&Number(d,"timestamp")>=(f.ProcessStartedUtc-Epoch).TotalSeconds&&!String.IsNullOrEmpty(Text(d,"instance"));
            double age=valid?(f.Utc-Epoch).TotalSeconds-Number(d,"timestamp"):Double.PositiveInfinity;
            bool fresh=valid&&age>=-5&&age<=8;
            string phase=valid?Text(d,"status"):"",attempt=valid?Text(d,"attempt_id"):"";
            if(phase=="error"&&!String.IsNullOrEmpty(Text(d,"validation_attempt_id")))attempt="validation/"+Text(d,"validation_attempt_id");
            string observedRole=valid?Text(d,"role"):"";
            if(String.IsNullOrEmpty(observedRole))observedRole=Text(d,"requested_action")=="host"?"host":Text(d,"requested_action")=="join"?"guest":"";
            string key=valid?f.ProcessId+"/"+Text(d,"instance")+"/"+attempt:"";
            bool awaitingAck=IsActive&&nativeAttempt==null&&!String.IsNullOrEmpty(request)&&Text(d,"request_id")!=request;
            bool activePhase=new[]{"connecting","starting_host","connected","hosting","loading","error","disconnected"}.Contains(phase);
            if(IsActive&&f.Running&&(f.ProcessId!=pid||f.ProcessStartedUtc!=processStarted)){Finish(f.Utc,"process_replaced");}
            if(fresh&&!awaitingAck&&activePhase&&!String.IsNullOrEmpty(observedRole)){
                if(IsActive&&nativeAttempt!=null&&key!=lastAttemptKey){Finish(f.Utc,"new_attempt");}
                if(!IsActive&&(key!=lastAttemptKey||phase=="connected"))Arm(observedRole=="host"?"host":"join",Text(d,"request_id"),f.ProcessId,f.ProcessStartedUtc,f.Utc);
                if(IsActive&&(nativeAttempt==null||key==lastAttemptKey)){instance=Text(d,"instance");nativeAttempt=attempt;lastAttemptKey=key;}
                if(IsActive)role=Role(observedRole);
            }
            if(!IsActive&&justExited&&lastAttemptKey!=null){string oldInstance=instance,oldAttempt=nativeAttempt;Arm(role,request,f.ProcessId,f.ProcessStartedUtc,f.Utc);instance=oldInstance;nativeAttempt=oldAttempt;}
            if(IsActive&&valid)BindInstance(Text(d,"instance"));
            if(valid&&f.ProcessId==pid&&f.ProcessStartedUtc==processStarted){
                var transition=new Dictionary<string,object>{{"process_id",pid},{"instance",Text(d,"instance")},{"attempt_id",attempt},{"request_id",Text(d,"request_id")},{"role",observedRole},{"status",phase},{"player_count",Number(d,"player_count")}};
                foreach(string name in new[]{"code","message","error","native_error","validation_attempt_id","server_connection_present","world_name","map_name"})if(d.ContainsKey(name))transition[name]=d[name];
                string signature=json.Serialize(Clean(transition));
                if(signature!=lastTransition){lastTransition=signature;transition["utc"]=f.Utc.ToString("o");string line=json.Serialize(Clean(transition));transitions.Add(line);transitionBytes+=Encoding.UTF8.GetByteCount(line);
                    // Keep early context as well as the latest changes within a hard bound.
                    while(transitions.Count>256||transitionBytes>512*1024){int index=transitions.Count>64?64:0;transitionBytes-=Encoding.UTF8.GetByteCount(transitions[index]);transitions.RemoveAt(index);}
                }
            }
            if(f.Events!=null)foreach(var e in f.Events){
                if(Number(e,"pid")!=pid||Text(e,"instance")!=instance||Number(e,"timestamp")<(processStarted-Epoch).TotalSeconds||Number(e,"timestamp")>(f.Utc-Epoch).TotalSeconds+5||Text(e,"event")=="state")continue;
                string line=json.Serialize(Clean(e));if(!nativeKeys.Add(line))continue;nativeEvents.Add(line);nativeBytes+=Encoding.UTF8.GetByteCount(line);
                while(nativeEvents.Count>128||nativeBytes>512*1024){int index=nativeEvents.Count>32?32:0;string old=nativeEvents[index];nativeEvents.RemoveAt(index);nativeBytes-=Encoding.UTF8.GetByteCount(old);nativeKeys.Remove(old);}
            }
            var row=new Dictionary<string,object>{{"utc",f.Utc.ToString("o")},{"process_id",f.ProcessId},{"running",f.Running},{"responding",f.Responding},{"cpu_seconds",f.CpuSeconds},{"working_set_bytes",f.WorkingSet},{"runtime_age_seconds",Double.IsInfinity(age)?(object)null:age},{"runtime_valid",valid},{"awaiting_request_ack",awaitingAck},{"read_issue",f.ReadIssue},{"runtime",valid?d:null}};
            row=(Dictionary<string,object>)Clean(row);ring.Enqueue(row);ringBytes+=Encoding.UTF8.GetByteCount(json.Serialize(row));
            while(ring.Count>240||ringBytes>2*1024*1024||(ring.Count>1&&f.Utc-DateTime.Parse(Text(ring.Peek(),"utc"),null,DateTimeStyles.RoundtripKind)>TimeSpan.FromSeconds(120)))ringBytes-=Encoding.UTF8.GetByteCount(json.Serialize(ring.Dequeue()));
            if(!IsActive)return;
            if(!f.Running){Finish(f.Utc,"process_exited");return;}
            if(fresh&&!awaitingAck){
                string code=Text(d,"code");bool uncertain=(phase=="error"&&code=="timeout")||phase=="disconnected";
                if(phase=="error"&&!uncertain)Trigger("runtime_reported_error",f.Utc);
                if(uncertain&&(f.Utc-started).TotalSeconds>=60)Trigger("connection_unconfirmed_cause_unknown",f.Utc);
                if(phase=="connected") {if(triggerAt!=DateTime.MinValue)recovered=true;connected=worldAvailable=true;missingAt=DateTime.MinValue;}
                else if(connected&&phase=="hosting"&&role=="host"&&lastPlayers>=2)Trigger("peer_departed_intent_unknown",f.Utc);
                else if(worldAvailable&&phase!="hosting") {if(missingAt==DateTime.MinValue)missingAt=f.Utc;if((f.Utc-missingAt).TotalSeconds>=60)Trigger("world_unavailable_cause_unknown",f.Utc);}
                else missingAt=DateTime.MinValue;
                if(phase=="hosting")worldAvailable=true;
                lastPlayers=(int)Number(d,"player_count");
            }
            if(awaitingAck&&(f.Utc-started).TotalSeconds>=60)Trigger("request_unacknowledged",f.Utc);
            if(!fresh&&(f.Utc-started).TotalSeconds>=60&&(!valid||age>=60))Trigger("mod_updates_stalled",f.Utc);
            if(!f.Responding){if(unresponsiveAt==DateTime.MinValue)unresponsiveAt=f.Utc;if((f.Utc-unresponsiveAt).TotalSeconds>=60)Trigger("process_unresponsive",f.Utc);}else unresponsiveAt=DateTime.MinValue;
            if(triggerAt!=DateTime.MinValue&&(f.Utc-triggerAt).TotalSeconds>=60)Finish(f.Utc,"aftermath_complete");
        }
        public void Finish(DateTime utc,string reason){
            if(!IsActive||utc<nextSave)return;
            if(reason=="process_exited"||reason=="process_replaced")Trigger(reason,utc);
            try{LastReport=Save(utc,reason);LastError=null;id=null;}
            catch(Exception e){LastError=e.GetType().Name+": "+Redact(e.Message);nextSave=utc.AddSeconds(30);}
        }
        string Redact(string value){
            if(value==null)return null;
            value=value.Replace(root,"<game>").Replace(root.Replace('\\','/'),"<game>");
            string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(!String.IsNullOrEmpty(home))value=value.Replace(home,"<user>").Replace(home.Replace('\\','/'),"<user>");
            value=Regex.Replace(value,@"(?i)[a-z]:[\\/][^\r\n""<>]+","<local-path>");
            value=Regex.Replace(value,@"\b(?:\d{1,3}\.){3}\d{1,3}\b",m=>"ip-"+Hash(Encoding.UTF8.GetBytes(m.Value)).Substring(0,10));
            value=Regex.Replace(value,@"(?i)\bauthorization[""']?\s*[:=]\s*[""']?(?:Bearer|Basic)\s+[^\s""',;}]+","Authorization=<redacted>");
            value=Regex.Replace(value,@"(?i)\b(token|password|secret|authorization|api_?key|loginid|epicaccountid)[""']?\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s,;}]+)","$1=<redacted>");
            return value.Length>4096?value.Substring(0,4096)+"<truncated>":value;
        }
        object Clean(object value,int depth=0){
            if(value==null||depth>7)return null;
            var dict=value as IDictionary<string,object>;
            if(dict!=null){var result=new Dictionary<string,object>();foreach(var p in dict.Take(80))result[p.Key]=Regex.IsMatch(p.Key,"token|password|secret|authorization|api.?key|loginid|epicaccountid|username",RegexOptions.IgnoreCase)?"<redacted>":Clean(p.Value,depth+1);return result;}
            if(value is string)return Redact((string)value);
            if(value is IEnumerable)return ((IEnumerable)value).Cast<object>().Take(32).Select(v=>Clean(v,depth+1)).ToArray();
            return value;
        }
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static bool Plain(string path){for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return false;return true;}
        static string Tail(string path,int bytes){
            if(!File.Exists(path)||!Plain(path))return "";
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                bool cut=stream.Length>bytes;stream.Seek(Math.Max(0,stream.Length-bytes),SeekOrigin.Begin);
                using(var r=new StreamReader(stream,Encoding.UTF8,true)){string text=r.ReadToEnd();if(cut){int end=text.IndexOf('\n');text=end<0?"":text.Substring(end+1);}return text;}
            }
        }
        string Save(DateTime utc,string reason){
            string dir=Path.Combine(root,"BL4-LAN-Data","Reports");if(!Plain(dir))throw new IOException("Report directory is redirected");Directory.CreateDirectory(dir);
            var coverage=new Dictionary<string,object>();var entries=new Dictionary<string,string>();
            entries["timeline.jsonl"]=String.Join("\n",ring.Select(v=>json.Serialize(Clean(v))))+"\n";
            entries["transitions.jsonl"]=String.Join("\n",transitions)+"\n";
            var lines=new List<string>();foreach(string line in nativeEvents){var e=json.Deserialize<Dictionary<string,object>>(line);if(Number(e,"pid")==pid&&Number(e,"timestamp")>=(processStarted-Epoch).TotalSeconds)lines.Add(line);}
            foreach(string eventName in new[]{"events.previous.log","events.log"}){string events=Path.Combine(root,"BL4-LAN-Data",eventName);
            try{foreach(string line in Tail(events,512*1024).Split('\n')){
                try{var d=json.Deserialize<Dictionary<string,object>>(line);double stamp=Number(d,"timestamp");
                    if(Number(d,"pid")==pid&&Text(d,"instance")==instance&&stamp>=(processStarted-Epoch).TotalSeconds&&stamp<=(utc-Epoch).TotalSeconds+5)lines.Add(json.Serialize(Clean(d)));
                }catch(ArgumentException){}catch(InvalidOperationException){}
            }coverage[eventName]=File.Exists(events)?"bounded instance-filtered tail":"missing";}catch(Exception e){coverage[eventName]=e.GetType().Name;}}
            var eventLines=lines.Distinct().ToList();long eventBytes=eventLines.Sum(line=>(long)Encoding.UTF8.GetByteCount(line)+1);
            bool eventsTrimmed=false;
            while(eventBytes>1024*1024&&eventLines.Count>0){int index=eventLines.Count>nativeEvents.Count?nativeEvents.Count:0;eventBytes-=Encoding.UTF8.GetByteCount(eventLines[index])+1;eventLines.RemoveAt(index);eventsTrimmed=true;}
            entries["game-events.jsonl"]=String.Join("\n",eventLines)+"\n";coverage["continuous_native_events"]=nativeEvents.Count;coverage["native_tail_trimmed_to_archive_budget"]=eventsTrimmed;
            foreach(var item in new[]{new[]{"sdk-tail.log","OakGame/Binaries/Win64/Plugins/unrealsdk.log"},new[]{"mod-error.txt","BL4-LAN-Data/last-error.txt"}}){
                string path=Path.Combine(root,item[1].Replace('/',Path.DirectorySeparatorChar));
                try{if(File.Exists(path)&&File.GetLastWriteTimeUtc(path)>=started.AddMinutes(-2)){entries[item[0]]=String.Join("\n",Tail(path,128*1024).Split('\n').Select(Redact));coverage[item[0]]="bounded recent tail; may include other process output";}else coverage[item[0]]="missing_or_older_than_attempt";}catch(Exception e){coverage[item[0]]=e.GetType().Name;}
            }
            var versions=new Dictionary<string,object>();
            foreach(string relative in new[]{"OakGame/Binaries/Win64/Borderlands4.exe","OakGame/Binaries/Win64/Plugins/unrealsdk.dll","OakGame/Binaries/Win64/Plugins/pyunrealsdk.dll","sdk_mods/keybinds/keybinds.pyd","sdk_mods/bl4_lan/__init__.py","sdk_mods/bl4_lan/core.py","sdk_mods/bl4_lan/runtime.py","sdk_mods/bl4_lan/fileio.py"}){
                string path=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
                try{if(!File.Exists(path)||!Plain(path)){versions[relative]="missing_or_redirected";continue;}
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())versions[relative]=new{bytes=stream.Length,sha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant(),version=FileVersionInfo.GetVersionInfo(path).FileVersion};
                }catch(Exception e){versions[relative]=e.GetType().Name;}
            }
            entries["versions.json"]=json.Serialize(Clean(versions));
            try{entries["network.json"]=json.Serialize(Clean(NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).Take(16).Select(n=>new Dictionary<string,object>{{"type",n.NetworkInterfaceType.ToString()},{"vpn_family",Regex.Match(n.Name+" "+n.Description,"Radmin|Tailscale|ZeroTier|Hamachi",RegexOptions.IgnoreCase).Value},{"ipv4",n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Take(16).Select(a=>a.Address.ToString()).ToArray()}}).ToArray()));coverage["network"]="active adapter types and pseudonymized IPv4; no packet capture";}catch(Exception e){coverage["network"]=e.GetType().Name;}
            entries["crashes.json"]=json.Serialize(CrashMetadata(utc,coverage));
            entries["README.txt"]="BL4 LAN automatic local report / Автоматический локальный отчёт\r\nSend the ZIP from BOTH players to the person diagnosing the issue. / Передайте отчёты с обоих ПК.\r\nNo video, saves, memory dumps or upload. / Без видео, сохранений, дампов памяти и отправки.\r\nStale updates, peer departure and process exit are observations, not proof of a crash or a network cause. Native errors appear only if the game exposed them.\r\nИсчезновение обновлений, выход друга и закрытие процесса сами по себе не устанавливают причину сбоя.\r\nIP addresses are consistently pseudonymized; local paths and common credential fields are redacted. Review before sharing. / Адреса заменены метками, локальные пути и типовые поля секретов скрыты. Перед отправкой можно просмотреть отчёт.\r\n";
            entries["report.json"]=json.Serialize(Clean(new Dictionary<string,object>{{"schema",1},{"launcher_version","0.2.6"},{"report_id",id},{"role",role},{"request_id",request},{"instance",instance},{"native_attempt_id",nativeAttempt},{"process_id",pid},{"process_started_utc",processStarted.ToString("o")},{"started_utc",sessionStarted.ToString("o")},{"attempt_started_utc",started.ToString("o")},{"ended_utc",utc.ToString("o")},{"end_reason",reason},{"observations",triggers.ToArray()},{"recovered",recovered},{"connection_observed",connected},{"ever_connected",connected},{"cause","unknown_unless_native_evidence_explains_it"},{"coverage",coverage},{"limits",new{ring_seconds=120,max_samples=240,max_raw_archive_bytes=4*1024*1024,aftermath_seconds=60}}}));
            long total=entries.Values.Sum(s=>(long)Encoding.UTF8.GetByteCount(s));if(total>4*1024*1024)throw new IOException("Report content limit exceeded");
            string file=Path.Combine(dir,"BL4-LAN-report-"+utc.ToString("yyyyMMdd-HHmmss")+"-"+role+"-"+id.Substring(0,8)+".zip"),temp=file+".tmp";
            try{using(var archive=ZipFile.Open(temp,ZipArchiveMode.Create))foreach(var entry in entries){using(var w=new StreamWriter(archive.CreateEntry(entry.Key,CompressionLevel.Optimal).Open(),new UTF8Encoding(false)))w.Write(entry.Value);}File.Move(temp,file);}
            finally{if(File.Exists(temp))File.Delete(temp);}
            foreach(string old in Directory.EnumerateFiles(dir,"BL4-LAN-report-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(10))try{if(Plain(old))File.Delete(old);}catch(IOException){}catch(UnauthorizedAccessException){}
            return file;
        }
        object[] CrashMetadata(DateTime utc,Dictionary<string,object> coverage){
            var list=new List<object>();
            string[] roots={Path.Combine(root,"OakGame","Saved","Crashes"),Path.Combine(root,"BL4-LAN-Data","TestProfile","Saved","Crashes"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games","Borderlands 4","Saved","Crashes")};
            foreach(string folder in roots)try{
                if(!Directory.Exists(folder)||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(string sub in Directory.EnumerateDirectories(folder).Take(64)){
                    if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)!=0)continue;string path=Path.Combine(sub,"CrashContext.runtime-xml");
                    if(!File.Exists(path)||!Plain(path)||new FileInfo(path).Length>256*1024||File.GetLastWriteTimeUtc(path)<processStarted||File.GetLastWriteTimeUtc(path)>utc.AddSeconds(5))continue;
                    var doc=new XmlDocument {XmlResolver=null};using(var reader=XmlReader.Create(path,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=256*1024}))doc.Load(reader);
                    var props=doc.SelectSingleNode("/FGenericCrashContext/RuntimeProperties");if(props==null||props.SelectSingleNode("ProcessId")==null||props.SelectSingleNode("ProcessId").InnerText!=pid.ToString())continue;
                    var fields=new Dictionary<string,object>();foreach(string name in new[]{"ProcessId","ErrorMessage","CrashType","SecondsSinceStart","EngineVersion","BuildVersion","GameStateName"}){var node=props.SelectSingleNode(name);if(node!=null)fields[name]=Redact(node.InnerText);}list.Add(fields);if(list.Count>=4)break;
                }
            }catch(Exception e){coverage["crash_metadata_read"]=e.GetType().Name;}
            coverage["crash_metadata"]=list.Count==0?"no_matching_report_in_known_locations":"matching_metadata_only";return list.ToArray();
        }
    }
}
