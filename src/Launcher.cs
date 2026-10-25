using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyTitle("BL4 LAN by skykindness")]
[assembly: AssemblyCompany("skykindness")]
[assembly: AssemblyVersion("0.2.1.0")]

namespace BL4LAN {
    static class Program {
        [STAThread] static void Main() {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Launcher());
        }
    }
    public class Launcher : Form {
        readonly string bundle=AppDomain.CurrentDomain.BaseDirectory;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        TextBox folder,address,nickname;RadioButton host,guest;
        bool legacyProfile;ComboBox interfaces;
        readonly Dictionary<string,bool> profileChoices=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        Label status,statusTitle;Button connect,saveNick;
        Panel pagePlay,pageSettings,hostAddressBox,guestAddressBox;
        Button navPlay,navSettings;Label roleHint,launchHint,connectHint,profileNotice;
        Button installButton,launchButton;Label installTitle,installHint,launchTitle,connectTitle;
        InstallationState installationState=InstallationState.Unavailable;
        Control surface;
        Timer timer;Notice notice;
        string lastPhase="",pendingId=null,pendingInstance=null;
        DateTime pendingSince;bool hadRuntime;
        Color yellow=Color.FromArgb(245,193,54),red=Color.FromArgb(255,116,105),green=Color.FromArgb(94,220,152);
        public Launcher() {
            Text="BL4 LAN • by skykindness";ClientSize=new Size(900,740);
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
            StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(18,26,33);
            ForeColor=Color.WhiteSmoke;Font=new Font("Segoe UI",10);DoubleBuffered=true;
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            var assembly=Assembly.GetExecutingAssembly();
            using(var stream=assembly.GetManifestResourceStream("BL4LAN.Brand.ico"))if(stream!=null)using(var source=new Icon(stream))Icon=(Icon)source.Clone();
            using(var stream=assembly.GetManifestResourceStream("BL4LAN.Brand.png"))if(stream!=null)using(var source=Image.FromStream(stream)){
                var logo=new PictureBox {Left=24,Top=18,Width=72,Height=72,SizeMode=PictureBoxSizeMode.Zoom,Image=new Bitmap(source)};
                Controls.Add(logo);FormClosed+=delegate{logo.Image.Dispose();};
            }
            AddLabel("BL4 LAN",108,15,390,46,28,yellow);
            AddLabel("by skykindness",111,61,350,26,11,Color.WhiteSmoke);
            navPlay=AddButton("Играть",646,26,106,delegate{ShowPage(false);});
            navSettings=AddButton("Настройки",764,26,112,delegate{ShowPage(true);});
            AddLabel("0.2.1  ·  LAN / VPN",646,75,230,22,9,Color.Silver);
            pagePlay=new Panel {Left=24,Top=120,Width=852,Height=562};Controls.Add(pagePlay);
            pageSettings=new Panel {Left=24,Top=120,Width=852,Height=562,Visible=false};Controls.Add(pageSettings);
            surface=pageSettings;
            AddLabel("Настройки игры",0,0,800,35,18,yellow);
            AddLabel("Обычно достаточно указать папку один раз. Ник меняется после перезапуска игры.",0,43,840,28,10,Color.Silver);
            AddLabel("Папка Borderlands 4",0,92,700,23);
            folder=new TextBox {Left=0,Top=120,Width=714,Text=FindDefaultGame()};pageSettings.Controls.Add(folder);
            AddButton("Выбрать…",730,117,122,delegate {
                using(var d=new FolderBrowserDialog()) {
                    d.Description="Выберите папку Borderlands 4, содержащую OakGame";
                    if(d.ShowDialog(this)==DialogResult.OK){folder.Text=d.SelectedPath;LoadGame();}
                }
            });
            AddLabel("Ник игрока",0,180,430,23);
            nickname=new TextBox {Left=0,Top=208,Width=356,MaxLength=32};pageSettings.Controls.Add(nickname);
            saveNick=AddButton("Сохранить ник",372,205,174,delegate {Guard(delegate {
                string root=GameRoot();Installer.RequireClosed(root);GameSettings.SetNickname(root,nickname.Text);
                SetStatus("НИК СОХРАНЁН","Новое имя появится при следующем запуске игры.",yellow);
                ShowPage(false);
            });});
            AddLabel("Предыдущее значение сохраняется\nв резервной копии.",570,205,282,43,9,Color.Silver);
            AddLabel("Ваши персонажи",0,290,800,30,14,yellow);
            profileNotice=AddLabel("Используются ваши обычные персонажи игры.",0,336,840,55,10,Color.Silver);
            AddLabel("Перед сменой ника или обновлением закройте игру.\nПредыдущие настройки сохраняются в резервной копии.",0,404,840,50,10,Color.Silver);
            AddButton("Готово — вернуться к игре",0,490,310,delegate{Guard(delegate{SavePreferences();ShowPage(false);});});
            surface=pagePlay;
            host=new RadioButton {Left=0,Top=0,Width=418,Height=62,Text="СОЗДАТЬ ИГРУ\nДруг подключится ко мне",Checked=true,Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter};
            guest=new RadioButton {Left=434,Top=0,Width=418,Height=62,Text="ПОДКЛЮЧИТЬСЯ\nУ меня есть адрес друга",Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter};
            pagePlay.Controls.Add(host);pagePlay.Controls.Add(guest);
            hostAddressBox=new Panel {Left=0,Top=82,Width=852,Height=100,BackColor=Color.FromArgb(28,41,50)};
            guestAddressBox=new Panel {Left=0,Top=82,Width=852,Height=100,BackColor=Color.FromArgb(28,41,50)};
            pagePlay.Controls.Add(hostAddressBox);pagePlay.Controls.Add(guestAddressBox);
            surface=hostAddressBox;
            AddLabel("Мой адрес — отправьте его другу",16,10,800,25,11,yellow);
            interfaces=new ComboBox {Left=16,Top=49,Width=594,DropDownStyle=ComboBoxStyle.DropDownList};hostAddressBox.Controls.Add(interfaces);
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if(nic.OperationalStatus!=OperationalStatus.Up)continue;
                foreach(var ip in nic.GetIPProperties().UnicastAddresses)
                    if(ip.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(ip.Address))
                        interfaces.Items.Add(ip.Address+"  ·  "+nic.Name);
            }
            if(interfaces.Items.Count>0) {
                interfaces.SelectedIndex=0;
                for(int i=0;i<interfaces.Items.Count;i++)if(Regex.IsMatch(interfaces.Items[i].ToString(),"Radmin|Tailscale|ZeroTier|Hamachi",RegexOptions.IgnoreCase)){interfaces.SelectedIndex=i;break;}
            }
            AddButton("Скопировать адрес",626,45,210,delegate {if(interfaces.SelectedItem!=null){Clipboard.SetText(interfaces.SelectedItem.ToString().Split(' ')[0]+":7777");SetStatus("АДРЕС СКОПИРОВАН","Отправьте адрес другу. Оба компьютера должны быть в общей LAN или VPN.",yellow);}else SetStatus("АДРЕС НЕ НАЙДЕН","Подключитесь к LAN или VPN и откройте программу заново.",yellow);});
            surface=guestAddressBox;
            AddLabel("Адрес друга",16,10,250,25,11,yellow);
            AddLabel("Например: 26.1.2.3:7777",400,12,435,25,10,Color.Silver);
            address=new TextBox {Left=16,Top=49,Width=820};guestAddressBox.Controls.Add(address);
            surface=pagePlay;
            installTitle=AddLabel("1  УСТАНОВИТЕ МОД",0,210,268,27,11,yellow);
            installHint=AddLabel("Один раз или после обновления",0,245,268,43,10,Color.Silver);
            installButton=AddButton("Установить мод",0,294,268,delegate{Guard(delegate{
                if(installationState==InstallationState.SelectGame){ShowPage(true);folder.Focus();}
                else if(installationState==InstallationState.PackageIncomplete||installationState==InstallationState.Unavailable)RefreshInstallation();
                else Install();
            });});
            launchTitle=AddLabel("2  ЗАПУСТИТЕ ИГРУ",292,210,268,27,11,yellow);
            launchHint=AddLabel("Загрузите персонажа в мир",292,245,268,43,10,Color.Silver);
            launchButton=AddButton("Запустить игру",292,294,268,delegate{Guard(Launch);});
            connectTitle=AddLabel("3  ИГРАЙТЕ ВМЕСТЕ",584,210,268,27,11,yellow);
            connectHint=AddLabel("Откройте игру для друга",584,245,268,43,10,Color.Silver);
            connect=AddButton("Создать хост",584,294,268,delegate{Guard(SendAction);});
            connect.BackColor=yellow;connect.ForeColor=Color.Black;
            host.CheckedChanged+=delegate{UpdateMode();};guest.CheckedChanged+=delegate{UpdateMode();};
            roleHint=AddLabel("",0,395,852,46,10,Color.Silver);
            var panel=new Panel {Left=0,Top=450,Width=852,Height=112,BackColor=Color.FromArgb(28,41,50)};
            pagePlay.Controls.Add(panel);
            statusTitle=new Label {Left=16,Top=12,Width=820,Height=26,Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=yellow,Text="ПЕРЕД ПЕРВЫМ ЗАПУСКОМ"};
            status=new Label {Left=16,Top=47,Width=820,Height=59,Font=new Font("Segoe UI",10),Text="Выберите роль и следуйте шагам слева направо."};
            panel.Controls.Add(statusTitle);panel.Controls.Add(status);
            surface=null;
            AddLabel("Общая LAN / VPN  ·  7777 UDP",24,705,300,24,9,Color.Silver);
            var logs=new LinkLabel {Left=388,Top=703,Width=194,Height=24,Text="Открыть журнал",LinkColor=yellow,ActiveLinkColor=Color.White};
            logs.LinkClicked+=delegate{Guard(delegate{string p=Installer.Inside(GameRoot(),"BL4-LAN-Data/events.log");if(!File.Exists(p))throw new InvalidOperationException("Журнал появится после запуска мода.");Process.Start(new ProcessStartInfo(p){UseShellExecute=true});});};Controls.Add(logs);
            var copy=new LinkLabel {Left=642,Top=703,Width=234,Height=24,Text="Скопировать сведения",LinkColor=yellow,ActiveLinkColor=Color.White};
            copy.LinkClicked+=delegate{Guard(delegate{using(var p=FindRunning()){var d=ReadRuntime(p,true);Clipboard.SetText(json.Serialize(d));SetStatus("СВЕДЕНИЯ СКОПИРОВАНЫ","Их можно приложить к сообщению о проблеме.",yellow);}});};Controls.Add(copy);
            LoadPreferences();LoadGame();UpdateMode();ShowPage(false);
            folder.Leave+=delegate{LoadGame();};
            timer=new Timer {Interval=700};timer.Tick+=delegate{UpdateStatus();};timer.Start();
            FormClosed+=delegate{timer.Stop();if(notice!=null)notice.Dispose();};
        }
        void ShowPage(bool settings){pagePlay.Visible=!settings;pageSettings.Visible=settings;navPlay.BackColor=settings?Color.FromArgb(39,57,69):yellow;navPlay.ForeColor=settings?Color.WhiteSmoke:Color.Black;navSettings.BackColor=settings?yellow:Color.FromArgb(39,57,69);navSettings.ForeColor=settings?Color.Black:Color.WhiteSmoke;if(!settings&&timer!=null)RefreshInstallation();}
        void RefreshInstallation(){installationState=Installer.CheckInstallation(folder.Text.Trim(),bundle);UpdateInstallationUI();}
        int Units(int value){return (int)Math.Round(value*(pagePlay==null?1.0:pagePlay.Width/852.0));}
        void UpdateInstallationUI(){
            bool ready=installationState==InstallationState.Ready;
            installButton.Visible=!ready;installTitle.ForeColor=ready?green:yellow;
            installTitle.Text=ready?"МОД УСТАНОВЛЕН":installationState==InstallationState.Missing?"1  УСТАНОВИТЕ МОД":installationState==InstallationState.UpdateRequired?"1  ОБНОВИТЕ МОД":installationState==InstallationState.SelectGame?"1  ВЫБЕРИТЕ ПАПКУ ИГРЫ":installationState==InstallationState.PackageIncomplete?"1  РАСПАКУЙТЕ ВЕСЬ АРХИВ":"1  ПРОВЕРЬТЕ УСТАНОВКУ";
            installHint.Text=ready?"Повторная установка не нужна. Можно запускать игру.":installationState==InstallationState.Missing?"Только один раз перед первой игрой":installationState==InstallationState.UpdateRequired?"Версия отличается или не хватает файлов":installationState==InstallationState.SelectGame?"Укажите папку в настройках":installationState==InstallationState.PackageIncomplete?"Не хватает файлов рядом с программой":"Не удалось прочитать файлы игры";
            installButton.Text=installationState==InstallationState.Missing?"Установить мод":installationState==InstallationState.SelectGame?"Выбрать папку":installationState==InstallationState.UpdateRequired?"Установить / обновить":"Проверить снова";
            installTitle.Width=Units(ready?852:268);installHint.Width=Units(ready?852:268);
            installHint.Height=Units(ready?26:43);
            installTitle.Top=Units(ready?202:210);installHint.Top=Units(ready?235:245);
            launchTitle.Text=ready?"1  ЗАПУСТИТЕ ИГРУ":"2  ЗАПУСТИТЕ ИГРУ";connectTitle.Text=ready?"2  ИГРАЙТЕ ВМЕСТЕ":"3  ИГРАЙТЕ ВМЕСТЕ";
            int left=ready?0:292,right=ready?434:584,width=ready?418:268;
            launchTitle.SetBounds(Units(left),Units(ready?276:210),Units(width),Units(27));launchHint.SetBounds(Units(left),Units(ready?310:245),Units(width),Units(43));launchButton.SetBounds(Units(left),Units(ready?354:294),Units(width),Units(38));
            connectTitle.SetBounds(Units(right),Units(ready?276:210),Units(width),Units(27));connectHint.SetBounds(Units(right),Units(ready?310:245),Units(width),Units(43));connect.SetBounds(Units(right),Units(ready?354:294),Units(width),Units(38));
            if(statusTitle.Text=="ПЕРЕД ПЕРВЫМ ЗАПУСКОМ"||statusTitle.Text=="ГОТОВ К ЗАПУСКУ")SetStatus(ready?"ГОТОВ К ЗАПУСКУ":"ПЕРЕД ПЕРВЫМ ЗАПУСКОМ",ready?"Мод уже установлен. Выберите роль, запустите игру и подключайтесь.":"Выполните первый шаг выше. Повторять установку перед каждой игрой не нужно.",yellow);
        }
        void UpdateMode(){
            if(connect==null||roleHint==null)return;
            bool isHost=host.Checked;hostAddressBox.Visible=isHost;guestAddressBox.Visible=!isHost;address.Enabled=!isHost;
            host.BackColor=isHost?yellow:Color.FromArgb(39,57,69);host.ForeColor=isHost?Color.Black:Color.WhiteSmoke;
            guest.BackColor=!isHost?yellow:Color.FromArgb(39,57,69);guest.ForeColor=!isHost?Color.Black:Color.WhiteSmoke;
            connect.Text=isHost?"Создать хост":"Подключиться";
            launchHint.Text=isHost?"Загрузите персонажа в мир":"Дождитесь главного меню";
            connectHint.Text=isHost?"Откройте игру для друга":"Войдите по адресу выше";
            roleHint.Text=isHost?"Создание хоста перезагрузит локацию. Дождитесь автосохранения.\nПосле создания хоста оставьте программу открытой — здесь будет статус друга.":"Сначала хост должен открыть свою игру. Адрес нужен от той же LAN или VPN.\nОставьте программу открытой — здесь появится соединение или причина ошибки.";
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using(var p=new Pen(yellow,Math.Max(1,Units(2))))e.Graphics.DrawLine(p,Units(24)+AutoScrollPosition.X,Units(105)+AutoScrollPosition.Y,Units(876)+AutoScrollPosition.X,Units(105)+AutoScrollPosition.Y);
        }
        protected override void OnShown(EventArgs e){
            base.OnShown(e);var area=Screen.FromControl(this).WorkingArea;
            int maxWidth=Math.Max(320,area.Width-24),maxHeight=Math.Max(240,area.Height-24);
            if(Width<=maxWidth&&Height<=maxHeight)return;
            Size content=ClientSize;AutoScroll=true;AutoScrollMinSize=content;
            Size=new Size(Math.Min(Width,maxWidth),Math.Min(Height,maxHeight));
            Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        }
        Label AddLabel(string text,int x,int y,int width,int height,int size=10,Color? color=null) {
            var l=new Label {Text=text,Left=x,Top=y,Width=width,Height=height,ForeColor=color??ForeColor,Font=new Font("Segoe UI",size),BackColor=Color.Transparent};(surface??this).Controls.Add(l);return l;
        }
        Button AddButton(string text,int x,int y,int width,Action action) {
            var b=new Button {Text=text,Left=x,Top=y,Width=width,Height=38,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(39,57,69),ForeColor=Color.WhiteSmoke};
            b.Click+=delegate{action();};(surface??this).Controls.Add(b);return b;
        }
        void Guard(Action action) {try{action();}catch(Exception e){SetStatus("НЕ УДАЛОСЬ",e.Message,red);ShowNotice("Не удалось",e.Message,red);}}
        void SetStatus(string title,string body,Color color){statusTitle.Text=title;statusTitle.ForeColor=color;status.Text=body;}
        void ShowNotice(string title,string body,Color color){if(notice!=null)notice.Dispose();notice=new Notice(title,body,color);notice.Show();}
        string FindDefaultGame(){
            var roots=new[]{Path.Combine(Path.GetPathRoot(Environment.SystemDirectory),"Games"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steamapps","common"),Path.GetDirectoryName(bundle.TrimEnd(Path.DirectorySeparatorChar))??bundle};
            foreach(string root in roots)try{
                string direct=Path.Combine(root,"Borderlands 4");if(File.Exists(Path.Combine(direct,@"OakGame\Binaries\Win64\Borderlands4.exe")))return direct;
                if(!Directory.Exists(root))continue;
                foreach(string child in Directory.EnumerateDirectories(root).Take(100)){
                    string candidate=Path.Combine(child,"Borderlands 4");
                    if(File.Exists(Path.Combine(candidate,@"OakGame\Binaries\Win64\Borderlands4.exe")))return candidate;
                }
            }catch{}
            return "";
        }
        string GameRoot(){
            string root=Path.GetFullPath(folder.Text.Trim());
            if(!File.Exists(Installer.Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe")))throw new InvalidOperationException("Выберите папку игры с папкой OakGame.");
            return root.TrimEnd(Path.DirectorySeparatorChar);
        }
        void LoadGame(){
            try {string root=GameRoot();nickname.Text=GameSettings.GetNickname(root);saveNick.Enabled=nickname.Text.Length>0;
                string previous=Path.Combine(root,@"BL4-LAN-Data\TestProfile\Saved\SaveGames");
                bool hasCharacters=Directory.Exists(previous)&&Directory.EnumerateFiles(previous,"*.sav",SearchOption.AllDirectories).Any();
                bool preferred;bool remembered=profileChoices.TryGetValue(root,out preferred);
                legacyProfile=hasCharacters&&(!remembered||preferred);
                if(hasCharacters&&!remembered)profileChoices[root]=true;
                profileNotice.Text=legacyProfile?"При обновлении сохранён ранее выбранный набор персонажей.\nСохранения не переносятся и не объединяются.":"Используются ваши обычные персонажи игры.";
            }catch{nickname.Text="";saveNick.Enabled=false;}
            RefreshInstallation();
        }
        void LoadPreferences(){
            try {var d=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(bundle,"launcher-settings.json"),Encoding.UTF8));
                if(d.ContainsKey("folder"))folder.Text=Convert.ToString(d["folder"]);
                if(d.ContainsKey("address"))address.Text=Convert.ToString(d["address"]);
                if(d.ContainsKey("guest"))guest.Checked=Convert.ToBoolean(d["guest"]);
                profileChoices.Clear();
                if(d.ContainsKey("profileChoices"))foreach(var item in (Dictionary<string,object>)d["profileChoices"])profileChoices[Path.GetFullPath(item.Key).TrimEnd(Path.DirectorySeparatorChar)]=Convert.ToBoolean(item.Value);
                if(d.ContainsKey("privateProfile")&&!String.IsNullOrWhiteSpace(folder.Text))profileChoices[Path.GetFullPath(folder.Text).TrimEnd(Path.DirectorySeparatorChar)]=Convert.ToBoolean(d["privateProfile"]);
            }catch{}
        }
        void SavePreferences(){
            string root=GameRoot();if(!profileChoices.ContainsKey(root))profileChoices[root]=legacyProfile;
            GameSettings.JsonWrite(Path.Combine(bundle,"launcher-settings.json"),new{folder=root,address=address.Text.Trim(),guest=guest.Checked,privateProfile=profileChoices[root],profileChoices=profileChoices});
        }
        void Install(){SetStatus("УСТАНОВКА","Проверяем и устанавливаем файлы…",yellow);Refresh();string message=Installer.Install(GameRoot(),bundle);RefreshInstallation();SetStatus("ГОТОВО",message,green);}
        string ValidateAddress(string value){
            value=value.Trim();if(!Regex.IsMatch(value,@"^\d{1,3}(\.\d{1,3}){3}(:\d{1,5})?$"))throw new ArgumentException("Введите адрес хоста, например 26.1.2.3:7777.");
            string[] parts=value.Split(':');IPAddress ip;int port=7777;
            if(!IPAddress.TryParse(parts[0],out ip)||(parts.Length==2&&!int.TryParse(parts[1],out port))||port<1024||port>65535)throw new ArgumentException("Некорректный адрес или порт хоста.");
            byte[] b=ip.GetAddressBytes();if(b[0]>=224||ip.Equals(IPAddress.Any))throw new ArgumentException("Нужен IPv4-адрес компьютера друга.");
            return ip+":"+port;
        }
        void SaveConnection(){
            string endpoint=guest.Checked?ValidateAddress(address.Text):"";
            GameSettings.JsonWrite(Installer.Inside(GameRoot(),"BL4-LAN-Data/connection.json"),new{address=endpoint,mode=host.Checked?"host":"guest"});
            SavePreferences();
        }
        void Launch(){
            RefreshInstallation();if(installationState!=InstallationState.Ready)throw new InvalidOperationException("Сначала выполните первый шаг в программе: выберите папку, установите мод или распакуйте полный пакет.");
            string root=GameRoot();var existing=FindRunning();
            if(existing!=null){existing.Dispose();SetStatus("ИГРА УЖЕ ЗАПУЩЕНА","Для входа используйте кнопку «Подключиться», для хоста — «Создать хост».",yellow);return;}
            foreach(string name in new[]{"__init__.py","core.py","runtime.py","fileio.py"}){
                string dest=Installer.Inside(root,"sdk_mods/bl4_lan/"+name),src=Path.Combine(bundle,"sdk_mods","bl4_lan",name);
                if(!File.Exists(dest)||!File.Exists(src)||Installer.Hash(File.ReadAllBytes(dest))!=Installer.Hash(File.ReadAllBytes(src)))throw new InvalidOperationException("Сначала нажмите «Установить / обновить», чтобы установить эту версию мода.");
            }
            if(!File.Exists(Installer.Inside(root,"OakGame/Binaries/Win64/dsound.dll")))throw new InvalidOperationException("Сначала установите SDK и мод.");
            SaveConnection();GameSettings.EnableMod(root);
            if(!String.IsNullOrWhiteSpace(nickname.Text))GameSettings.SetNickname(root,nickname.Text);
            string args="-port=7777";
            if(legacyProfile){
                string profile=Path.Combine(root,"BL4-LAN-Data","TestProfile");
                args+=" -UserDir=\""+profile+"\" -SaveToUserDir";
            }
            string exe=Installer.Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe");
            Process.Start(new ProcessStartInfo(exe,args){WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=true});
            SetStatus("ИГРА ЗАПУСКАЕТСЯ",host.Checked?"Загрузите персонажа в мир, затем нажмите «Создать хост».":"Дождитесь главного меню, затем нажмите «Подключиться».",yellow);
        }
        Process FindRunning(){
            string expected=Installer.Inside(GameRoot(),"OakGame/Binaries/Win64/Borderlands4.exe");Process found=null;
            foreach(var p in Process.GetProcessesByName("Borderlands4")){
                bool match=false;try{match=String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase);}catch{}
                if(!match){p.Dispose();continue;}
                if(found!=null){found.Dispose();p.Dispose();throw new InvalidOperationException("Запущено несколько копий игры. Оставьте одну для управления из лаунчера.");}
                found=p;
            }
            return found;
        }
        Dictionary<string,object> ReadRuntime(Process p,bool require){
            if(p==null){if(require)throw new InvalidOperationException("Сначала запустите игру.");return null;}
            string file=Installer.Inside(GameRoot(),"BL4-LAN-Data/status-"+p.Id+".json");
            if(!File.Exists(file)){if(require)throw new InvalidOperationException("Мод ещё не загрузился. Дождитесь главного меню. Если сообщения нет — установите обновление и перезапустите игру.");return null;}
            var d=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file,Encoding.UTF8));
            double stamp=Convert.ToDouble(d["timestamp"]);DateTime at=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(stamp);
            if(Convert.ToInt32(d["pid"])!=p.Id || p.StartTime.ToUniversalTime()>at || !d.ContainsKey("instance")){
                if(require)throw new InvalidOperationException("В игре ещё старая версия мода. Закройте игру, установите обновление и запустите её заново.");return null;
            }
            if(require && (DateTime.UtcNow-at).TotalSeconds>8)throw new InvalidOperationException("Нет свежего ответа от игры. Дождитесь окончания загрузки и повторите действие.");
            return d;
        }
        void SendAction(){
            SaveConnection();
            using(var p=FindRunning()){
                var d=ReadRuntime(p,true);string instance=Convert.ToString(d["instance"]);
                pendingId=Guid.NewGuid().ToString("N");pendingInstance=instance;pendingSince=DateTime.UtcNow;
                GameSettings.JsonWrite(Installer.Inside(GameRoot(),"BL4-LAN-Data/request-"+p.Id+".json"),new{id=pendingId,instance=instance,action=host.Checked?"host":"join",address=guest.Checked?ValidateAddress(address.Text):"",expires=(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds+60});
                SetStatus("ЗАПРОС ПЕРЕДАН",host.Checked?"Создаём хост. Дождитесь перезагрузки локации.":"Ожидаем подтверждение от игры…",yellow);
            }
        }
        void UpdateStatus(){
            try{
                using(var p=FindRunning()){
                    if(p==null){if(hadRuntime){SetStatus("ИГРА ЗАКРЫТА","Можно изменить ник, установить обновление или запустить игру.",yellow);hadRuntime=false;}return;}
                    var d=ReadRuntime(p,false);if(d==null)return;hadRuntime=true;
                    string phase=Convert.ToString(d["status"]),message=Convert.ToString(d["message"]);
                    if(pendingId!=null){
                        if(Convert.ToString(d["instance"])!=pendingInstance){pendingId=null;}
                        else if(Convert.ToString(d["request_id"])==pendingId)pendingId=null;
                        else if((DateTime.UtcNow-pendingSince).TotalSeconds<20)return;
                        else {SetStatus("ИГРА НЕ ПОДТВЕРДИЛА ЗАПРОС","Дождитесь главного меню или игрового мира и повторите. Резервные клавиши: F6 — хост, F7 — вход.",red);return;}
                    }
                    double age=(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds-Convert.ToDouble(d["timestamp"]);
                    if(age>8){SetStatus("ОЖИДАЕМ ИГРУ","Нет свежих данных от мода. Возможно, игра загружает локацию. Состояние соединения сейчас не подтверждено.",yellow);return;}
                    string title=phase=="connected"?"СОЕДИНЕНИЕ УСТАНОВЛЕНО":phase=="hosting"?"ХОСТ ГОТОВ":phase=="connecting"?"ПОДКЛЮЧЕНИЕ":phase=="starting_host"?"СОЗДАНИЕ ХОСТА":phase=="loading"?"ЗАГРУЗКА ЛОКАЦИИ":phase=="disabled"?"МОД ОТКЛЮЧЁН":phase=="error"||phase=="disconnected"?"ОШИБКА СОЕДИНЕНИЯ":"МОД ГОТОВ";
                    Color color=phase=="connected"||phase=="hosting"?green:phase=="error"||phase=="disconnected"?red:yellow;
                    if(d.ContainsKey("elapsed_seconds")&&(phase=="connecting"||phase=="starting_host"))message+="  "+d["elapsed_seconds"]+" с.";
                    SetStatus(title,message,color);
                    if(phase!=lastPhase){if(phase=="connected"||phase=="hosting"||phase=="error"||phase=="disconnected")ShowNotice(title,message,color);lastPhase=phase;}
                }
            }catch{if(hadRuntime)SetStatus("СОСТОЯНИЕ НЕДОСТУПНО","Не удалось прочитать свежий ответ игры. Текущее соединение не подтверждено. Дождитесь завершения загрузки или откройте журнал.",yellow);}
        }
    }
    sealed class Notice : Form {
        Timer timer;
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        public Notice(string title,string body,Color accent){
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
            ClientSize=new Size(450,128);BackColor=Color.FromArgb(28,41,50);ForeColor=Color.WhiteSmoke;
            var area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Right-Width-20,area.Bottom-Height-20);
            Controls.Add(new Label {Left=16,Top=13,Width=418,Height=26,Text="BL4 LAN  /  "+title,ForeColor=accent,Font=new Font("Segoe UI",10,FontStyle.Bold)});
            Controls.Add(new Label {Left=16,Top=45,Width=418,Height=77,Text=body,Font=new Font("Segoe UI",10)});
            timer=new Timer {Interval=7000};timer.Tick+=delegate{Close();};timer.Start();
            FormClosed+=delegate{timer.Stop();timer.Dispose();};Click+=delegate{Close();};
        }
    }
}

