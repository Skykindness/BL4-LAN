using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BL4LAN {
    public static class GameSettings {
        public static void AtomicWrite(string path,byte[] data) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
            try {
                File.WriteAllBytes(temp,data);
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            } finally {if(File.Exists(temp))File.Delete(temp);}
        }
        public static void JsonWrite(string path,object data) {
            AtomicWrite(path,new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(data)));
        }
        public static string Backup(string root,string path,string category) {
            string dir=Installer.Inside(root,"BL4-LAN-Data/Backups/"+category+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+"/placeholder");
            dir=Path.GetDirectoryName(dir);Directory.CreateDirectory(dir);
            string dest=Path.Combine(dir,Path.GetFileName(path));File.Copy(path,dest,false);return dest;
        }
        public static void EnableMod(string root) {
            string file=Installer.Inside(root,"sdk_mods/settings/bl4_lan.json");
            var json=new JavaScriptSerializer();Dictionary<string,object> data=new Dictionary<string,object>();
            if(File.Exists(file)) {
                try {data=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file,Encoding.UTF8));if(data==null)throw new InvalidDataException();}
                catch(Exception e) {if(!(e is ArgumentException || e is InvalidOperationException || e is InvalidDataException))throw;Backup(root,file,"settings-repair");data=new Dictionary<string,object>();}
                object enabled;if(data.TryGetValue("enabled",out enabled) && enabled is bool && (bool)enabled)return;
                Backup(root,file,"settings");
            }
            data["enabled"]=true;JsonWrite(file,data);
        }
        static string NickFile(string root) {return Installer.Inside(root,@"OakGame/Binaries/Win64/steam_emu.ini");}
        static string Decode(byte[] data,out Encoding encoding,out bool bom) {
            bom=data.Length>=3 && data[0]==239 && data[1]==187 && data[2]==191;
            try {encoding=new UTF8Encoding(false,true);return encoding.GetString(data,bom?3:0,data.Length-(bom?3:0));}
            catch(DecoderFallbackException){encoding=Encoding.GetEncoding(1251,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);bom=false;return encoding.GetString(data);}
        }
        static Match[] NickMatches(string text) {
            // Only UserName in the repack's Settings/Steam section is editable.
            string section="";var matches=new List<Match>();
            foreach(Match line in Regex.Matches(text,@"(?m)^.*(?:\r?\n|$)")) {
                string s=line.Value.Trim();if(s.StartsWith("[") && s.EndsWith("]"))section=s.Substring(1,s.Length-2);
                if((section.Equals("Settings",StringComparison.OrdinalIgnoreCase)||section.Equals("Steam",StringComparison.OrdinalIgnoreCase)) && Regex.IsMatch(line.Value,@"^\s*UserName\s*=",RegexOptions.IgnoreCase))matches.Add(line);
            }
            return matches.ToArray();
        }
        public static string GetNickname(string root) {
            string p=NickFile(root);if(!File.Exists(p))return "";
            Encoding enc;bool bom;string text=Decode(File.ReadAllBytes(p),out enc,out bom);var m=NickMatches(text);
            return m.Length==1?m[0].Value.Substring(m[0].Value.IndexOf('=')+1).Trim():"";
        }
        public static void SetNickname(string root,string name) {
            name=(name??"").Trim();
            string file=NickFile(root);if(!File.Exists(file))throw new FileNotFoundException("В этой установке не найден файл настройки ника. Смена имени доступна для репака со steam_emu.ini.");
            byte[] original=File.ReadAllBytes(file);Encoding enc;bool bom;string text=Decode(original,out enc,out bom);var rows=NickMatches(text);
            if(rows.Length!=1)throw new InvalidDataException("Не удалось однозначно найти имя игрока. Исходная настройка сохранена.");
            var row=rows[0];string line=row.Value;int equal=line.IndexOf('=');
            string current=line.Substring(equal+1).Trim();if(current==name)return;
            if(!Regex.IsMatch(name,@"^[\p{L}\p{N}_. -]{1,32}$") || String.IsNullOrWhiteSpace(name))throw new ArgumentException("Ник: от 1 до 32 букв, цифр, пробелов или символов _ . -");
            string newline=line.EndsWith("\r\n")?"\r\n":line.EndsWith("\n")?"\n":"";
            string updated=text.Substring(0,row.Index)+line.Substring(0,equal+1)+name+newline+text.Substring(row.Index+row.Length);
            byte[] bytes=enc.GetBytes(updated);if(bom)bytes=new byte[]{239,187,191}.Concat(bytes).ToArray();
            Backup(root,file,"nickname");AtomicWrite(file,bytes);
        }
        public static void ProfileWarmup(string root,bool skip) {
            string file=Installer.Inside(root,"BL4-LAN-Data/TestProfile/Saved/Config/Windows/Engine.ini");
            string original=File.Exists(file)?File.ReadAllText(file,Encoding.UTF8):"";
            string newline=original.Contains("\r\n")?"\r\n":"\n";
            var lines=original.Replace("\r\n","\n").Split('\n').ToList();
            var result=new List<string>();bool inside=false,found=false;
            foreach(string line in lines) {
                string trimmed=line.Trim();
                if(trimmed.StartsWith("[")&&trimmed.EndsWith("]")) {
                    if(inside&&skip){while(result.Count>0&&String.IsNullOrWhiteSpace(result[result.Count-1]))result.RemoveAt(result.Count-1);result.Add("r.ShaderPipelineCache.Enabled=0");result.Add("r.PSOPrecaching=0");}
                    inside=trimmed.Equals("[ConsoleVariables]",StringComparison.OrdinalIgnoreCase);found|=inside;
                }
                if(inside&&Regex.IsMatch(trimmed,@"^(r\.ShaderPipelineCache\.Enabled|r\.PSOPrecaching)\s*=",RegexOptions.IgnoreCase))continue;
                result.Add(line);
            }
            if(inside&&skip){while(result.Count>0&&String.IsNullOrWhiteSpace(result[result.Count-1]))result.RemoveAt(result.Count-1);result.Add("r.ShaderPipelineCache.Enabled=0");result.Add("r.PSOPrecaching=0");}
            if(!found&&skip){result.Add("[ConsoleVariables]");result.Add("r.ShaderPipelineCache.Enabled=0");result.Add("r.PSOPrecaching=0");}
            string updated=String.Join(newline,result).TrimEnd('\r','\n')+newline;
            if(String.IsNullOrWhiteSpace(original)&&!skip || original==updated)return;
            if(File.Exists(file))Backup(root,file,"profile-options");
            AtomicWrite(file,new UTF8Encoding(false).GetBytes(updated));
        }
    }
}
