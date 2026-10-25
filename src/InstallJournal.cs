using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BL4LAN {
    public sealed class InstallJournal {
        readonly string file, root;
        readonly Dictionary<string,string> rows=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        InstallJournal(string file, string root) { this.file=file; this.root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar; }
        string Validate(string path, string hash) {
            if(String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(":") || !Regex.IsMatch(hash??"",@"^[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("Некорректная запись журнала установки.");
            string normalized=path.Replace('/',Path.DirectorySeparatorChar);
            string full=Path.GetFullPath(Path.Combine(root,normalized));
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь журнала выходит из папки игры.");
            return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar,'/');
        }
        public static InstallJournal Load(string file, string root) {
            var result=new InstallJournal(file,root);
            if(!File.Exists(file)) return result;
            var old=result.json.Deserialize<List<Dictionary<string,string>>>(File.ReadAllText(file,Encoding.UTF8));
            if(old==null) throw new InvalidDataException("Журнал установки повреждён; он сохранён без изменений.");
            foreach(var row in old) {
                string path, hash;
                if(row==null || !row.TryGetValue("path",out path) || !row.TryGetValue("sha256",out hash)) throw new InvalidDataException("Журнал установки повреждён.");
                path=result.Validate(path,hash);
                if(result.rows.ContainsKey(path) && !String.Equals(result.rows[path],hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Противоречивые записи журнала установки.");
                result.rows[path]=hash.ToLowerInvariant();
            }
            return result;
        }
        public void Append(string path, string hash) {
            path=Validate(path,hash);
            if(rows.ContainsKey(path)) {
                if(!String.Equals(rows[path],hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Сумма файла отличается от журнала установки.");
                return;
            }
            rows[path]=hash.ToLowerInvariant();
            string temp=file+".tmp-"+Guid.NewGuid().ToString("N");
            try {
                File.WriteAllText(temp,json.Serialize(rows.Select(kv=>new{path=kv.Key,sha256=kv.Value}).ToArray()),Encoding.UTF8);
                if(File.Exists(file)) File.Replace(temp,file,null); else File.Move(temp,file);
            } catch { rows.Remove(path); throw; }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        public bool Owns(string path, string hash) {
            path=Validate(path,hash); string old;
            return rows.TryGetValue(path,out old) && String.Equals(old,hash,StringComparison.OrdinalIgnoreCase);
        }
        public void Update(string path, string previousHash, string hash) {
            path=Validate(path,hash); Validate(path,previousHash);
            string old;
            if(!rows.TryGetValue(path,out old)) {Append(path,hash);return;}
            if(String.Equals(old,hash,StringComparison.OrdinalIgnoreCase))return;
            if(!String.Equals(old,previousHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Файл не соответствует журналу предыдущей установки.");
            rows.Remove(path);
            try {Append(path,hash);}catch{rows[path]=old;throw;}
        }
    }
}
