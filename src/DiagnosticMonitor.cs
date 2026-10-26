using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BL4LAN {
    // Only filesystem/process APIs run here. All Unreal reads stay in the mod.
    public sealed class DiagnosticMonitor : IDisposable {
        readonly string root;
        readonly DiagnosticRecorder recorder;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=256*1024,RecursionLimit=32};
        readonly ConcurrentQueue<Action<DiagnosticRecorder>> commands=new ConcurrentQueue<Action<DiagnosticRecorder>>();
        readonly Thread worker;
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly ManualResetEvent stop=new ManualResetEvent(false),ready=new ManualResetEvent(false);
        volatile string report,error;
        volatile bool active,disposed,passive=true;
        string expectedRole;
        int lastPid;DateTime lastStart;
        public string LastReport {get{return report;}}
        public string LastError {get{return error;}}
        public bool IsActive {get{return active;}}
        public bool IsPassive {get{return passive;}}
        public string GameRoot {get{return root;}}
        public DiagnosticMonitor(string gameRoot){
            root=Path.GetFullPath(gameRoot);if(root.Length>Path.GetPathRoot(root).Length)root=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            recorder=new DiagnosticRecorder(root);report=recorder.LastReport;
            worker=new Thread(Run){IsBackground=true,Name="BL4 LAN diagnostic observer"};worker.Start();ready.WaitOne();
        }
        public void ExpectLaunch(string role){if(disposed||passive)return;commands.Enqueue(r=>expectedRole=role);wake.Set();}
        public void BeginAttempt(string action,string request,int pid,DateTime start,string instance){
            if(disposed||passive)return;
            DateTime now=DateTime.UtcNow;commands.Enqueue(r=>{r.Arm(action,request,pid,start,now);r.BindInstance(instance);});wake.Set();
        }
        void Run(){
            Mutex ownership=null;bool owns=false;
            try{
                string identity;using(var sha=SHA256.Create())identity=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-","");
                ownership=new Mutex(false,"Local\\BL4LAN.Diagnostics."+identity);
                while(!stop.WaitOne(0)){
                    try{
                        if(!owns)try{owns=ownership.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
                        passive=!owns;
                        if(owns)Poll();
                        else{
                            Action<DiagnosticRecorder> ignored;while(commands.TryDequeue(out ignored)){}
                            recorder.RefreshLastReport();report=recorder.LastReport;active=false;
                        }
                    }catch(Exception e){error=e.GetType().Name+": diagnostic observation unavailable";}
                    finally{ready.Set();}
                    WaitHandle.WaitAny(new WaitHandle[]{stop,wake},1000);
                }
            }catch(Exception e){error=e.GetType().Name+": diagnostic ownership unavailable";}
            finally{
                ready.Set();
                if(owns){
                    try{Action<DiagnosticRecorder> action;while(commands.TryDequeue(out action))action(recorder);recorder.Finish(DateTime.UtcNow,"launcher_closed");report=recorder.LastReport;error=recorder.LastError;}
                    finally{ownership.ReleaseMutex();}
                }
                if(ownership!=null)ownership.Dispose();active=false;
            }
        }
        Process Find(out string issue){
            issue=null;Process selected=null;
            string expected=Path.Combine(root,"OakGame","Binaries","Win64","Borderlands4.exe");
            foreach(var p in Process.GetProcessesByName("Borderlands4")){
                bool match=false;try{match=String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase);}catch{issue="process_path_unavailable";}
                if(!match){p.Dispose();continue;}
                if(selected!=null){selected.Dispose();p.Dispose();issue="multiple_matching_processes";return null;}selected=p;
            }
            return selected;
        }
        List<Dictionary<string,object>> RecentEvents(){
            var result=new List<Dictionary<string,object>>();
            foreach(string name in new[]{"events.previous.log","events.log"})try{
                string path=Installer.Inside(root,"BL4-LAN-Data/"+name);
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                    bool cut=stream.Length>64*1024;stream.Seek(Math.Max(0,stream.Length-64*1024),SeekOrigin.Begin);
                    using(var reader=new StreamReader(stream,Encoding.UTF8,true)){
                        if(cut)reader.ReadLine();string line;while((line=reader.ReadLine())!=null)try{var entry=json.Deserialize<Dictionary<string,object>>(line);object kind;if(entry.TryGetValue("event",out kind)&&Convert.ToString(kind)!="state")result.Add(entry);}catch(ArgumentException){}catch(InvalidOperationException){}
                    }
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(InvalidOperationException){}
            return result;
        }
        void Poll(){
            try{
                Action<DiagnosticRecorder> action;while(commands.TryDequeue(out action))action(recorder);
                string issue;using(var p=Find(out issue)){
                    var f=new CaptureFrame {Utc=DateTime.UtcNow,ProcessId=lastPid,ProcessStartedUtc=lastStart,ReadIssue=issue};
                    if(p!=null){
                        f.Running=true;f.ProcessId=p.Id;f.ProcessStartedUtc=p.StartTime.ToUniversalTime();
                        try{f.Responding=p.Responding;f.CpuSeconds=p.TotalProcessorTime.TotalSeconds;f.WorkingSet=p.WorkingSet64;}catch(InvalidOperationException){f.ReadIssue="process_ended_during_sample";}
                        bool newProcess=lastPid!=f.ProcessId||lastStart!=f.ProcessStartedUtc;
                        if(newProcess&&lastPid!=0&&recorder.IsActive)recorder.Finish(f.Utc,"process_replaced");
                        lastPid=f.ProcessId;lastStart=f.ProcessStartedUtc;
                        // The primary also sees launches from passive windows. Arm
                        // before native status exists so early startup exits survive.
                        if(newProcess&&!recorder.IsActive)recorder.Arm(expectedRole??"unknown",null,lastPid,lastStart,f.Utc);
                        expectedRole=null;
                        string path=Installer.Inside(root,"BL4-LAN-Data/status-"+p.Id+".json");
                        try{
                            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                                if(stream.Length>256*1024)throw new InvalidDataException("Runtime status limit");
                                using(var reader=new StreamReader(stream,Encoding.UTF8,true))f.Runtime=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
                            }
                        }catch(Exception e){f.ReadIssue="runtime_"+e.GetType().Name;}
                    }else if(issue!=null){
                        // Failure to inspect the process is not evidence of its exit.
                        f.Running=lastPid!=0;f.Responding=true;
                    }
                    f.Events=RecentEvents();
                    recorder.Observe(f);
                }
                error=recorder.LastError;
            }catch(Exception e){error=e.GetType().Name+": diagnostic observation unavailable";}
            finally{report=recorder.LastReport;active=recorder.IsActive;}
        }
        public void Dispose(){
            if(disposed)return;disposed=true;stop.Set();worker.Join();wake.Dispose();stop.Dispose();ready.Dispose();
        }
    }
}
