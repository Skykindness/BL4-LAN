using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace BL4LAN {
    public enum InstallationState { Ready, Missing, UpdateRequired, PackageIncomplete, SelectGame, Unavailable }
    public static class Installer {
        public const string SDKHash="602675446abed184169fa158be3c8bc81777a71203581e4a248eca8a3d00b5c7";
        static readonly Dictionary<string,string> Legacy=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"sdk_mods/bl4_lan/core.py","0d621077508e54e02ce77cfa1e163f7c54d53c06de13c332b125bf22f88dc039"},
            {"sdk_mods/bl4_lan/__init__.py","adca10e507c1a5453999b876d52a6b602c2fc9ebb1acd835c1cad02e29e0fe98"}
        };
        public static string Hash(byte[] data) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        public static string Inside(string root,string relative) {
            string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Path.IsPathRooted(relative) || relative.Contains(":"))throw new InvalidDataException("Некорректный путь пакета.");
            string full=Path.GetFullPath(Path.Combine(prefix,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Путь выходит из папки игры.");
            string current=full;
            while(current!=null && (current.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||current==prefix.TrimEnd(Path.DirectorySeparatorChar))) {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Папка установки содержит ссылку. Выберите обычную папку игры.");
                current=Path.GetDirectoryName(current);
            }
            return full;
        }
        public static void RequireClosed(string root) {
            string expected=Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe");
            foreach(var p in Process.GetProcessesByName("Borderlands4"))using(p) {
                try {if(String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Закройте Borderlands 4 перед установкой обновления или сменой ника. Игра не будет закрыта автоматически.");}
                catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("Не удалось проверить запущенную игру. Закройте её перед обновлением.");}
                catch(InvalidOperationException){if(!p.HasExited)throw;}
            }
        }
        public static InstallationState CheckInstallation(string root,string bundle) {
            try {
                if(String.IsNullOrWhiteSpace(root)||!File.Exists(Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe")))return InstallationState.SelectGame;
                string sdk=Path.Combine(bundle,"oak2-sdk-v0.3.zip");
                if(!File.Exists(sdk)||Hash(File.ReadAllBytes(sdk))!=SDKHash)return InstallationState.PackageIncomplete;
                var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(string name in new[]{"__init__.py","core.py","runtime.py","fileio.py"}){
                    string source=Path.Combine(bundle,"sdk_mods","bl4_lan",name);
                    if(!File.Exists(source))return InstallationState.PackageIncomplete;
                    expected.Add("sdk_mods/bl4_lan/"+name,Hash(File.ReadAllBytes(source)));
                }
                using(var z=ZipFile.OpenRead(sdk))foreach(var entry in z.Entries){
                    if(entry.FullName.EndsWith("/"))continue;
                    using(var stream=entry.Open())using(var memory=new MemoryStream()){stream.CopyTo(memory);expected.Add(entry.FullName,Hash(memory.ToArray()));}
                }
                int present=0,matches=0;
                foreach(var file in expected){
                    string path=Inside(root,file.Key);
                    if(!File.Exists(path))continue;
                    present++;
                    if(Hash(File.ReadAllBytes(path))==file.Value)matches++;
                }
                return matches==expected.Count?InstallationState.Ready:present==0?InstallationState.Missing:InstallationState.UpdateRequired;
            }catch{return InstallationState.Unavailable;}
        }
        public static string Install(string root,string bundle) {
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if(!File.Exists(Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe")))throw new InvalidOperationException("Выберите корневую папку Borderlands 4 с папкой OakGame.");
            RequireClosed(root);
            string sdk=Path.Combine(bundle,"oak2-sdk-v0.3.zip");
            if(!File.Exists(sdk)||Hash(File.ReadAllBytes(sdk))!=SDKHash)throw new InvalidDataException("Архив SDK отсутствует или повреждён. Распакуйте полный пакет BL4 LAN заново.");
            var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            using(var z=ZipFile.OpenRead(sdk))foreach(var e in z.Entries) {
                if(e.FullName.EndsWith("/"))continue;
                if(e.Length>64*1024*1024)throw new InvalidDataException("Недопустимый размер файла SDK.");
                Inside(root,e.FullName);
                using(var s=e.Open())using(var m=new MemoryStream()){s.CopyTo(m);files.Add(e.FullName,m.ToArray());}
            }
            foreach(string name in new[]{"__init__.py","core.py","runtime.py","fileio.py"}) {
                string src=Path.Combine(bundle,"sdk_mods","bl4_lan",name);
                if(!File.Exists(src))throw new FileNotFoundException("Не хватает файлов мода. Распакуйте весь архив рядом с лаунчером.");
                files.Add("sdk_mods/bl4_lan/"+name,File.ReadAllBytes(src));
            }
            string manifest=Inside(root,"BL4-LAN-Data/installed.json");var journal=InstallJournal.Load(manifest,root);
            var replaced=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var f in files) {
                string dest=Inside(root,f.Key);if(!File.Exists(dest))continue;
                byte[] old=File.ReadAllBytes(dest);string hash=Hash(old);
                if(hash==Hash(f.Value))continue;
                string legacy;bool ours=f.Key.StartsWith("sdk_mods/bl4_lan/",StringComparison.OrdinalIgnoreCase);
                if(!ours || !((Legacy.TryGetValue(f.Key,out legacy)&&legacy==hash)||journal.Owns(f.Key,hash)))throw new InvalidOperationException("Обнаружен изменённый файл:\n"+dest+"\nОн сохранён. Обновление не установлено поверх чужих изменений.");
                replaced.Add(f.Key,old);
            }
            byte[] previousJournal=File.Exists(manifest)?File.ReadAllBytes(manifest):null;
            var written=new List<string>();string backupDir=null;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(manifest));
                foreach(var f in files) {
                    string dest=Inside(root,f.Key);
                    if(File.Exists(dest)&&Hash(File.ReadAllBytes(dest))==Hash(f.Value))continue;
                    byte[] old;
                    if(replaced.TryGetValue(f.Key,out old)) {
                        if(backupDir==null){backupDir=Path.GetDirectoryName(Inside(root,"BL4-LAN-Data/Backups/update-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+"/placeholder"));Directory.CreateDirectory(backupDir);}
                        string backup=Path.Combine(backupDir,f.Key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(backup));File.WriteAllBytes(backup,old);
                        journal.Update(f.Key,Hash(old),Hash(f.Value));
                    } else journal.Append(f.Key,Hash(f.Value));
                    written.Add(f.Key);GameSettings.AtomicWrite(dest,f.Value);
                }
                GameSettings.EnableMod(root);
            } catch(Exception failure) {
                var rollbackErrors=new List<Exception>();
                foreach(string rel in written.AsEnumerable().Reverse()) {
                    try {
                        string dest=Inside(root,rel);byte[] old;
                        if(replaced.TryGetValue(rel,out old)) {
                            // A failed atomic replacement can leave the locked original intact.
                            if(!File.Exists(dest)||Hash(File.ReadAllBytes(dest))!=Hash(old))GameSettings.AtomicWrite(dest,old);
                        } else if(File.Exists(dest)&&Hash(File.ReadAllBytes(dest))==Hash(files[rel]))File.Delete(dest);
                    } catch(Exception error){rollbackErrors.Add(error);}
                }
                try {if(previousJournal!=null)GameSettings.AtomicWrite(manifest,previousJournal);else if(File.Exists(manifest))File.Delete(manifest);}
                catch(Exception error){rollbackErrors.Add(error);}
                if(rollbackErrors.Count>0)throw new IOException("Обновление прервано; часть файлов заблокирована. Закройте игру и повторите установку. Резервная копия: "+(backupDir??Path.GetDirectoryName(manifest)),failure);
                throw;
            }
            return replaced.Count>0?"BL4 LAN обновлён. Предыдущая версия сохранена в резервной копии.":"BL4 LAN установлен и включён. Можно запускать игру.";
        }
    }
}
