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
[assembly: AssemblyVersion("0.2.5.0")]

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
        Button navSettings;Label roleHint,launchHint,connectHint,profileNotice;
        Button installButton,launchButton;Label installTitle,installHint,launchTitle,connectTitle;
        InstallationState installationState=InstallationState.Unavailable;
        Control surface;
        Timer timer;Notice notice;
        DiagnosticMonitor diagnostics;LinkLabel reportLink;NotifyIcon tray;string notifiedReport;
        ComboBox languageSelector;Button donateButton;string language="ru";bool loadingPreferences;
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
            AddLabel("BL4 LAN",108,7,280,54,28,yellow);
            AddLabel("by skykindness",111,65,270,28,11,Color.WhiteSmoke);
            donateButton=new RainbowButton {Text=T("Донат"),Left=412,Top=26,Width=208,Height=42,AccessibleName=T("Донат"),TabIndex=0};
            donateButton.Click+=delegate{Guard(delegate{Process.Start(DonationLink());});};Controls.Add(donateButton);
            AddLabel(T("Язык"),416,77,90,24,9,Color.Silver);
            languageSelector=new ComboBox {Left=514,Top=73,Width=106,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=T("Язык")};
            languageSelector.Items.AddRange(new object[]{"RU","EN"});languageSelector.SelectedIndex=0;Controls.Add(languageSelector);
            navSettings=AddButton(T("Настройки"),646,26,230,delegate{ShowPage(!pageSettings.Visible);});
            AddLabel("0.2.5  ·  LAN / VPN",646,75,230,22,9,Color.Silver);
            pagePlay=new Panel {Left=24,Top=120,Width=852,Height=562};Controls.Add(pagePlay);
            pageSettings=new Panel {Left=24,Top=120,Width=852,Height=562,Visible=false};Controls.Add(pageSettings);
            surface=pageSettings;
            AddLabel(T("Настройки игры"),0,0,800,35,18,yellow);
            AddLabel(T("Обычно достаточно указать папку один раз. Ник меняется после перезапуска игры."),0,43,840,28,10,Color.Silver);
            AddLabel(T("Папка Borderlands 4"),0,92,700,23);
            folder=new TextBox {Left=0,Top=120,Width=714,Text=FindDefaultGame()};pageSettings.Controls.Add(folder);
            AddButton(T("Выбрать…"),730,117,122,delegate {
                using(var d=new FolderBrowserDialog()) {
                    d.Description=T("Выберите папку Borderlands 4, содержащую OakGame");
                    if(d.ShowDialog(this)==DialogResult.OK){folder.Text=d.SelectedPath;LoadGame();}
                }
            });
            AddLabel(T("Ник игрока"),0,180,430,23);
            nickname=new TextBox {Left=0,Top=208,Width=356,MaxLength=32};pageSettings.Controls.Add(nickname);
            saveNick=AddButton(T("Сохранить ник"),372,205,174,delegate {Guard(delegate {
                string root=GameRoot();Installer.RequireClosed(root);GameSettings.SetNickname(root,nickname.Text);
                SetStatus(T("НИК СОХРАНЁН"),T("Новое имя появится при следующем запуске игры."),yellow);
                ShowPage(false);
            });});
            AddLabel(T("Предыдущее значение сохраняется\nв резервной копии."),570,205,282,43,9,Color.Silver);
            AddLabel(T("Ваши персонажи"),0,290,800,30,14,yellow);
            profileNotice=AddLabel(T("Используются ваши обычные персонажи игры."),0,336,840,55,10,Color.Silver);
            AddLabel(T("Перед сменой ника или обновлением закройте игру.\nПредыдущие настройки сохраняются в резервной копии."),0,404,840,50,10,Color.Silver);
            AddButton(T("Готово — вернуться к игре"),0,490,310,delegate{Guard(delegate{SavePreferences();ShowPage(false);});});
            surface=pagePlay;
            host=new RadioButton {Left=0,Top=0,Width=418,Height=62,Text=T("СОЗДАТЬ ИГРУ\nДруг подключится ко мне"),Checked=true,Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter};
            guest=new RadioButton {Left=434,Top=0,Width=418,Height=62,Text=T("ПОДКЛЮЧИТЬСЯ\nУ меня есть адрес друга"),Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter};
            pagePlay.Controls.Add(host);pagePlay.Controls.Add(guest);
            hostAddressBox=new Panel {Left=0,Top=82,Width=852,Height=100,BackColor=Color.FromArgb(28,41,50)};
            guestAddressBox=new Panel {Left=0,Top=82,Width=852,Height=100,BackColor=Color.FromArgb(28,41,50)};
            pagePlay.Controls.Add(hostAddressBox);pagePlay.Controls.Add(guestAddressBox);
            surface=hostAddressBox;
            AddLabel(T("Мой адрес — отправьте его другу"),16,10,800,25,11,yellow);
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
            AddButton(T("Скопировать адрес"),626,45,210,delegate {if(interfaces.SelectedItem!=null){Clipboard.SetText(interfaces.SelectedItem.ToString().Split(' ')[0]+":7777");SetStatus(T("АДРЕС СКОПИРОВАН"),T("Отправьте адрес другу. Оба компьютера должны быть в общей LAN или VPN."),yellow);}else SetStatus(T("АДРЕС НЕ НАЙДЕН"),T("Подключитесь к LAN или VPN и откройте программу заново."),yellow);});
            surface=guestAddressBox;
            AddLabel(T("Адрес друга"),16,10,250,25,11,yellow);
            AddLabel(T("Например: 26.1.2.3:7777"),400,12,435,25,10,Color.Silver);
            address=new TextBox {Left=16,Top=49,Width=820};guestAddressBox.Controls.Add(address);
            surface=pagePlay;
            installTitle=AddLabel(T("1  УСТАНОВИТЕ МОД"),0,210,268,27,11,yellow);
            installHint=AddLabel(T("Один раз или после обновления"),0,245,268,43,10,Color.Silver);
            installButton=AddButton(T("Установить мод"),0,294,268,delegate{Guard(delegate{
                if(installationState==InstallationState.SelectGame){ShowPage(true);folder.Focus();}
                else if(installationState==InstallationState.PackageIncomplete||installationState==InstallationState.Unavailable)RefreshInstallation();
                else Install();
            });});
            launchTitle=AddLabel(T("2  ЗАПУСТИТЕ ИГРУ"),292,210,268,27,11,yellow);
            launchHint=AddLabel(T("Загрузите персонажа в мир"),292,245,268,43,10,Color.Silver);
            launchButton=AddButton(T("Запустить игру"),292,294,268,delegate{Guard(Launch);});
            connectTitle=AddLabel(T("3  ИГРАЙТЕ ВМЕСТЕ"),584,210,268,27,11,yellow);
            connectHint=AddLabel(T("Откройте игру для друга"),584,245,268,43,10,Color.Silver);
            connect=AddButton(T("Создать хост"),584,294,268,delegate{Guard(SendAction);});
            connect.BackColor=yellow;connect.ForeColor=Color.Black;
            host.CheckedChanged+=delegate{UpdateMode();};guest.CheckedChanged+=delegate{UpdateMode();};
            roleHint=AddLabel("",0,395,852,46,10,Color.Silver);
            var panel=new Panel {Left=0,Top=450,Width=852,Height=112,BackColor=Color.FromArgb(28,41,50)};
            pagePlay.Controls.Add(panel);
            statusTitle=new Label {Left=16,Top=12,Width=820,Height=26,Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=yellow,Text=T("ПЕРЕД ПЕРВЫМ ЗАПУСКОМ")};
            status=new Label {Left=16,Top=47,Width=820,Height=59,Font=new Font("Segoe UI",10),Text=T("Выберите роль и следуйте шагам слева направо.")};
            panel.Controls.Add(statusTitle);panel.Controls.Add(status);
            surface=null;
            reportLink=new LinkLabel {Left=24,Top=683,Width=852,Height=20,Font=new Font("Segoe UI",9),Text=T("Автосбор включён — оставьте лаунчер открытым или сверните в трей."),ForeColor=Color.Silver,LinkColor=yellow,ActiveLinkColor=Color.White,LinkArea=new LinkArea(0,0)};
            reportLink.LinkClicked+=delegate{Guard(delegate{if(diagnostics!=null&&!String.IsNullOrEmpty(diagnostics.LastReport)&&File.Exists(diagnostics.LastReport))Process.Start(new ProcessStartInfo(Path.GetDirectoryName(diagnostics.LastReport)){UseShellExecute=true});});};Controls.Add(reportLink);
            AddLabel(T("Общая LAN / VPN  ·  7777 UDP"),24,705,300,24,9,Color.Silver);
            var logs=new LinkLabel {Left=388,Top=703,Width=194,Height=24,Text=T("Открыть журнал"),LinkColor=yellow,ActiveLinkColor=Color.White};
            logs.LinkClicked+=delegate{Guard(delegate{string p=Installer.Inside(GameRoot(),"BL4-LAN-Data/events.log");if(!File.Exists(p))throw new InvalidOperationException(T("Журнал появится после запуска мода."));Process.Start(new ProcessStartInfo(p){UseShellExecute=true});});};Controls.Add(logs);
            var copy=new LinkLabel {Left=642,Top=703,Width=234,Height=24,Text=T("Скопировать сведения"),LinkColor=yellow,ActiveLinkColor=Color.White};
            copy.LinkClicked+=delegate{Guard(delegate{using(var p=FindRunning()){var d=ReadRuntime(p,true);Clipboard.SetText(json.Serialize(d));SetStatus(T("СВЕДЕНИЯ СКОПИРОВАНЫ"),T("Их можно приложить к сообщению о проблеме."),yellow);}});};Controls.Add(copy);
            LoadPreferences();LoadGame();UpdateMode();ShowPage(false);
            languageSelector.SelectedIndexChanged+=delegate{if(loadingPreferences)return;language=languageSelector.SelectedIndex==1?"en":"ru";ApplyLanguage(this);if(notice!=null)ApplyLanguage(notice);if(tray!=null)foreach(ToolStripItem item in tray.ContextMenuStrip.Items)item.Text=T(item.Text);UpdateDiagnostics();Guard(SaveLanguagePreferences);};
            folder.Leave+=delegate{LoadGame();};
            timer=new Timer {Interval=700};timer.Tick+=delegate{UpdateStatus();};timer.Start();
            var trayMenu=new ContextMenuStrip();trayMenu.Items.Add(T("Открыть BL4 LAN"),null,delegate{RestoreFromTray();});trayMenu.Items.Add(T("Выйти из лаунчера"),null,delegate{Close();});
            tray=new NotifyIcon {Icon=Icon,Text="BL4 LAN • by skykindness",ContextMenuStrip=trayMenu};tray.DoubleClick+=delegate{RestoreFromTray();};
            Resize+=delegate{if(WindowState==FormWindowState.Minimized){tray.Visible=true;Hide();}};
            FormClosed+=delegate{timer.Stop();timer.Dispose();if(notice!=null)notice.Dispose();};
        }
        protected override void Dispose(bool disposing){if(disposing){if(timer!=null){timer.Stop();timer.Dispose();}if(diagnostics!=null){diagnostics.Dispose();diagnostics=null;}if(tray!=null){tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();tray=null;}}base.Dispose(disposing);}
        void RestoreFromTray(){Show();WindowState=FormWindowState.Normal;Activate();tray.Visible=false;}
        void EnsureDiagnostics(){
            string root=GameRoot();if(diagnostics!=null&&String.Equals(root,diagnostics.GameRoot,StringComparison.OrdinalIgnoreCase))return;
            if(diagnostics!=null)diagnostics.Dispose();diagnostics=new DiagnosticMonitor(root);notifiedReport=diagnostics.LastReport;
        }
        void UpdateDiagnostics(){
            try{
                EnsureDiagnostics();string path=diagnostics.LastReport;
                if(!String.IsNullOrEmpty(diagnostics.LastError)){reportLink.Text=T("Автосбор: отчёт пока не сохранён. Проверьте свободное место и доступ к папке игры.");reportLink.ForeColor=red;reportLink.LinkArea=new LinkArea(0,0);return;}
                reportLink.LinkColor=yellow;reportLink.ForeColor=Color.Silver;
                reportLink.Text=!String.IsNullOrEmpty(path)?T("Отчёт сессии сохранён — открыть папку с ZIP для отправки."):diagnostics.IsPassive?T("Автосбор уже работает в другом окне лаунчера."):diagnostics.IsActive?T("Автосбор записывает сессию. При сбое отчёт сохранится автоматически."):T("Автосбор включён — оставьте лаунчер открытым или сверните в трей.");
                reportLink.LinkArea=new LinkArea(0,String.IsNullOrEmpty(path)?0:reportLink.Text.Length);
                if(!String.IsNullOrEmpty(path)&&path!=notifiedReport){notifiedReport=path;if(!diagnostics.IsPassive)ShowNotice(T("ОТЧЁТ ГОТОВ"),T("Отчёт сессии сохранён. Ссылка на папку с ZIP находится внизу лаунчера."),yellow);}
            }catch{reportLink.Text=T("Автосбор недоступен — проверьте выбранную папку игры.");reportLink.ForeColor=red;reportLink.LinkArea=new LinkArea(0,0);}
        }
        static ProcessStartInfo DonationLink(){return new ProcessStartInfo("https://www.donationalerts.com/r/skykindness"){UseShellExecute=true};}
        string T(string value){return Localization.Translate(value,language);}
        void ApplyLanguage(Control root){
            if(!(root is TextBox)&&!(root is ComboBox))root.Text=T(root.Text);
            if(!String.IsNullOrEmpty(root.AccessibleName))root.AccessibleName=T(root.AccessibleName);
            foreach(Control child in root.Controls)ApplyLanguage(child);
        }
        void SaveLanguagePreferences(){
            string path=Path.Combine(bundle,"launcher-settings.json");var data=new Dictionary<string,object>();
            try{data=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8))??data;}catch{}
            data["language"]=language;data["folder"]=folder.Text.Trim();data["address"]=address.Text.Trim();data["guest"]=guest.Checked;
            // Per-folder choices were migrated when loading preferences. A legacy
            // global flag could otherwise be reapplied to a newly selected folder.
            data.Remove("privateProfile");
            data["profileChoices"]=profileChoices;GameSettings.JsonWrite(path,data);
        }
        void ShowPage(bool settings){pagePlay.Visible=!settings;pageSettings.Visible=settings;navSettings.Text=settings?T("Назад"):T("Настройки");navSettings.BackColor=settings?yellow:Color.FromArgb(39,57,69);navSettings.ForeColor=settings?Color.Black:Color.WhiteSmoke;if(!settings&&timer!=null)RefreshInstallation();}
        void RefreshInstallation(){installationState=Installer.CheckInstallation(folder.Text.Trim(),bundle);UpdateInstallationUI();}
        int Units(int value){return (int)Math.Round(value*(pagePlay==null?1.0:pagePlay.Width/852.0));}
        void UpdateInstallationUI(){
            bool ready=installationState==InstallationState.Ready;
            installButton.Visible=!ready;installTitle.ForeColor=ready?green:yellow;
            installTitle.Text=ready?T("МОД УСТАНОВЛЕН"):installationState==InstallationState.Missing?T("1  УСТАНОВИТЕ МОД"):installationState==InstallationState.UpdateRequired?T("1  ОБНОВИТЕ МОД"):installationState==InstallationState.SelectGame?T("1  ВЫБЕРИТЕ ПАПКУ ИГРЫ"):installationState==InstallationState.PackageIncomplete?T("1  РАСПАКУЙТЕ ВЕСЬ АРХИВ"):T("1  ПРОВЕРЬТЕ УСТАНОВКУ");
            installHint.Text=ready?T("Повторная установка не нужна. Можно запускать игру."):installationState==InstallationState.Missing?T("Только один раз перед первой игрой"):installationState==InstallationState.UpdateRequired?T("Версия отличается или не хватает файлов"):installationState==InstallationState.SelectGame?T("Укажите папку в настройках"):installationState==InstallationState.PackageIncomplete?T("Не хватает файлов рядом с программой"):T("Не удалось прочитать файлы игры");
            installButton.Text=installationState==InstallationState.Missing?T("Установить мод"):installationState==InstallationState.SelectGame?T("Выбрать папку"):installationState==InstallationState.UpdateRequired?T("Установить / обновить"):T("Проверить снова");
            installTitle.Width=Units(ready?852:268);installHint.Width=Units(ready?852:268);
            installHint.Height=Units(ready?26:43);
            installTitle.Top=Units(ready?202:210);installHint.Top=Units(ready?235:245);
            launchTitle.Text=ready?T("1  ЗАПУСТИТЕ ИГРУ"):T("2  ЗАПУСТИТЕ ИГРУ");connectTitle.Text=ready?T("2  ИГРАЙТЕ ВМЕСТЕ"):T("3  ИГРАЙТЕ ВМЕСТЕ");
            int left=ready?0:292,right=ready?434:584,width=ready?418:268;
            launchTitle.SetBounds(Units(left),Units(ready?276:210),Units(width),Units(27));launchHint.SetBounds(Units(left),Units(ready?310:245),Units(width),Units(43));launchButton.SetBounds(Units(left),Units(ready?354:294),Units(width),Units(38));
            connectTitle.SetBounds(Units(right),Units(ready?276:210),Units(width),Units(27));connectHint.SetBounds(Units(right),Units(ready?310:245),Units(width),Units(43));connect.SetBounds(Units(right),Units(ready?354:294),Units(width),Units(38));
            if(statusTitle.Text==T("ПЕРЕД ПЕРВЫМ ЗАПУСКОМ")||statusTitle.Text==T("ГОТОВ К ЗАПУСКУ"))SetStatus(ready?T("ГОТОВ К ЗАПУСКУ"):T("ПЕРЕД ПЕРВЫМ ЗАПУСКОМ"),ready?T("Мод уже установлен. Выберите роль, запустите игру и подключайтесь."):T("Выполните первый шаг выше. Повторять установку перед каждой игрой не нужно."),yellow);
        }
        void UpdateMode(){
            if(connect==null||roleHint==null)return;
            bool isHost=host.Checked;hostAddressBox.Visible=isHost;guestAddressBox.Visible=!isHost;address.Enabled=!isHost;
            host.BackColor=isHost?yellow:Color.FromArgb(39,57,69);host.ForeColor=isHost?Color.Black:Color.WhiteSmoke;
            guest.BackColor=!isHost?yellow:Color.FromArgb(39,57,69);guest.ForeColor=!isHost?Color.Black:Color.WhiteSmoke;
            connect.Text=isHost?T("Создать хост"):T("Подключиться");
            launchHint.Text=T("Загрузите персонажа в мир");
            connectHint.Text=isHost?T("Откройте игру для друга"):T("Войдите по адресу выше");
            roleHint.Text=isHost?T("Создание хоста перезагрузит локацию. Дождитесь автосохранения.\nПосле создания хоста оставьте программу открытой — здесь будет статус друга."):T("Сначала нажмите «Продолжить» в игре и загрузите своего персонажа.\nЗатем подключитесь по адресу хоста. Лаунчер оставьте открытым.");
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
        void Guard(Action action) {try{action();}catch(Exception e){SetStatus(T("НЕ УДАЛОСЬ"),e.Message,red);ShowNotice(T("Не удалось"),e.Message,red);}}
        void SetStatus(string title,string body,Color color){statusTitle.Text=T(title);statusTitle.ForeColor=color;status.Text=T(body);}
        void ShowNotice(string title,string body,Color color){if(notice!=null)notice.Dispose();notice=new Notice(T(title),T(body),color);notice.Show();}
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
            if(!File.Exists(Installer.Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe")))throw new InvalidOperationException(T("Выберите папку игры с папкой OakGame."));
            return root.TrimEnd(Path.DirectorySeparatorChar);
        }
        void LoadGame(){
            try {string root=GameRoot();nickname.Text=GameSettings.GetNickname(root);saveNick.Enabled=nickname.Text.Length>0;
                string previous=Path.Combine(root,@"BL4-LAN-Data\TestProfile\Saved\SaveGames");
                bool hasCharacters=Directory.Exists(previous)&&Directory.EnumerateFiles(previous,"*.sav",SearchOption.AllDirectories).Any();
                bool preferred;bool remembered=profileChoices.TryGetValue(root,out preferred);
                legacyProfile=hasCharacters&&(!remembered||preferred);
                if(hasCharacters&&!remembered)profileChoices[root]=true;
                profileNotice.Text=legacyProfile?T("При обновлении сохранён ранее выбранный набор персонажей.\nСохранения не переносятся и не объединяются."):T("Используются ваши обычные персонажи игры.");
            }catch{nickname.Text="";saveNick.Enabled=false;}
            RefreshInstallation();
        }
        void LoadPreferences(){
            loadingPreferences=true;language="ru";
            try {var d=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(bundle,"launcher-settings.json"),Encoding.UTF8));
                if(d.ContainsKey("language")&&Convert.ToString(d["language"])=="en")language="en";
                if(d.ContainsKey("folder"))folder.Text=Convert.ToString(d["folder"]);
                if(d.ContainsKey("address"))address.Text=Convert.ToString(d["address"]);
                if(d.ContainsKey("guest"))guest.Checked=Convert.ToBoolean(d["guest"]);
                profileChoices.Clear();
                if(d.ContainsKey("profileChoices"))foreach(var item in (Dictionary<string,object>)d["profileChoices"])profileChoices[Path.GetFullPath(item.Key).TrimEnd(Path.DirectorySeparatorChar)]=Convert.ToBoolean(item.Value);
                if(d.ContainsKey("privateProfile")&&!String.IsNullOrWhiteSpace(folder.Text))profileChoices[Path.GetFullPath(folder.Text).TrimEnd(Path.DirectorySeparatorChar)]=Convert.ToBoolean(d["privateProfile"]);
            }catch{}finally{languageSelector.SelectedIndex=language=="en"?1:0;ApplyLanguage(this);loadingPreferences=false;}
        }
        void SavePreferences(){
            string root=GameRoot();if(!profileChoices.ContainsKey(root))profileChoices[root]=legacyProfile;
            GameSettings.JsonWrite(Path.Combine(bundle,"launcher-settings.json"),new{folder=root,address=address.Text.Trim(),guest=guest.Checked,privateProfile=profileChoices[root],profileChoices=profileChoices,language=language});
        }
        void Install(){SetStatus(T("УСТАНОВКА"),T("Проверяем и устанавливаем файлы…"),yellow);Refresh();string message=Installer.Install(GameRoot(),bundle);RefreshInstallation();SetStatus(T("ГОТОВО"),message,green);}
        string ValidateAddress(string value){
            value=value.Trim();if(!Regex.IsMatch(value,@"^\d{1,3}(\.\d{1,3}){3}(:\d{1,5})?$"))throw new ArgumentException(T("Введите адрес хоста, например 26.1.2.3:7777."));
            string[] parts=value.Split(':');IPAddress ip;int port=7777;
            if(!IPAddress.TryParse(parts[0],out ip)||(parts.Length==2&&!int.TryParse(parts[1],out port))||port<1024||port>65535)throw new ArgumentException(T("Некорректный адрес или порт хоста."));
            byte[] b=ip.GetAddressBytes();if(b[0]>=224||ip.Equals(IPAddress.Any))throw new ArgumentException(T("Нужен IPv4-адрес компьютера друга."));
            return ip+":"+port;
        }
        void SaveConnection(){
            string endpoint=guest.Checked?ValidateAddress(address.Text):"";
            GameSettings.JsonWrite(Installer.Inside(GameRoot(),"BL4-LAN-Data/connection.json"),new{address=endpoint,mode=host.Checked?"host":"guest"});
            SavePreferences();
        }
        void Launch(){
            RefreshInstallation();if(installationState!=InstallationState.Ready)throw new InvalidOperationException(T("Сначала выполните первый шаг в программе: выберите папку, установите мод или распакуйте полный пакет."));
            string root=GameRoot();var existing=FindRunning();
            if(existing!=null){existing.Dispose();SetStatus(T("ИГРА УЖЕ ЗАПУЩЕНА"),T("Для входа используйте кнопку «Подключиться», для хоста — «Создать хост»."),yellow);return;}
            foreach(string name in new[]{"__init__.py","core.py","runtime.py","fileio.py"}){
                string dest=Installer.Inside(root,"sdk_mods/bl4_lan/"+name),src=Path.Combine(bundle,"sdk_mods","bl4_lan",name);
                if(!File.Exists(dest)||!File.Exists(src)||Installer.Hash(File.ReadAllBytes(dest))!=Installer.Hash(File.ReadAllBytes(src)))throw new InvalidOperationException(T("Сначала нажмите «Установить / обновить», чтобы установить эту версию мода."));
            }
            if(!File.Exists(Installer.Inside(root,"OakGame/Binaries/Win64/dsound.dll")))throw new InvalidOperationException(T("Сначала установите SDK и мод."));
            SaveConnection();GameSettings.EnableMod(root);
            if(!String.IsNullOrWhiteSpace(nickname.Text))GameSettings.SetNickname(root,nickname.Text);
            string args="-port=7777";
            if(legacyProfile){
                string profile=Path.Combine(root,"BL4-LAN-Data","TestProfile");
                args+=" -UserDir=\""+profile+"\" -SaveToUserDir";
            }
            string exe=Installer.Inside(root,"OakGame/Binaries/Win64/Borderlands4.exe");
            try{EnsureDiagnostics();diagnostics.ExpectLaunch(host.Checked?"host":"guest");}catch{reportLink.Text=T("Автосбор недоступен — проверьте выбранную папку игры.");}
            Process.Start(new ProcessStartInfo(exe,args){WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=true});
            SetStatus(T("ИГРА ЗАПУСКАЕТСЯ"),host.Checked?T("Загрузите персонажа в мир, затем нажмите «Создать хост»."):T("Нажмите «Продолжить», загрузите своего персонажа, затем нажмите «Подключиться»."),yellow);
        }
        Process FindRunning(){
            string expected=Installer.Inside(GameRoot(),"OakGame/Binaries/Win64/Borderlands4.exe");Process found=null;
            foreach(var p in Process.GetProcessesByName("Borderlands4")){
                bool match=false;try{match=String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase);}catch{}
                if(!match){p.Dispose();continue;}
                if(found!=null){found.Dispose();p.Dispose();throw new InvalidOperationException(T("Запущено несколько копий игры. Оставьте одну для управления из лаунчера."));}
                found=p;
            }
            return found;
        }
        Dictionary<string,object> ReadRuntime(Process p,bool require){
            if(p==null){if(require)throw new InvalidOperationException(T("Сначала запустите игру."));return null;}
            string file=Installer.Inside(GameRoot(),"BL4-LAN-Data/status-"+p.Id+".json");
            if(!File.Exists(file)){if(require)throw new InvalidOperationException(T("Мод ещё не загрузился. Дождитесь главного меню. Если сообщения нет — установите обновление и перезапустите игру."));return null;}
            var d=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file,Encoding.UTF8));
            if(require&&(!d.ContainsKey("version")||Convert.ToString(d["version"])!=Assembly.GetExecutingAssembly().GetName().Version.ToString(3)))throw new InvalidOperationException(T("В игре ещё старая версия мода. Закройте игру, установите обновление и запустите её заново."));
            double stamp=Convert.ToDouble(d["timestamp"]);DateTime at=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(stamp);
            if(Convert.ToInt32(d["pid"])!=p.Id || p.StartTime.ToUniversalTime()>at || !d.ContainsKey("instance")){
                if(require)throw new InvalidOperationException(T("В игре ещё старая версия мода. Закройте игру, установите обновление и запустите её заново."));return null;
            }
            if(require && (DateTime.UtcNow-at).TotalSeconds>8)throw new InvalidOperationException(T("Нет свежего ответа от игры. Дождитесь окончания загрузки и повторите действие."));
            return d;
        }
        static bool PlayerPrepared(Dictionary<string,object> data){object value;return data.TryGetValue("player_world_ready",out value)&&value is bool&&(bool)value;}
        void ShowPreparation(Dictionary<string,object> data){object hint;string text=data.TryGetValue("player_world_hint",out hint)?Convert.ToString(hint):"";if(String.IsNullOrEmpty(text))text="Сначала нажмите «Продолжить» в игре и загрузите персонажа в свой игровой мир. Затем создайте хост или подключитесь к другу.";SetStatus(T("СНАЧАЛА ЗАГРУЗИТЕ ПЕРСОНАЖА"),T(text),yellow);}
        void SendAction(){
            using(var p=FindRunning()){
                var d=ReadRuntime(p,true);string instance=Convert.ToString(d["instance"]);
                if(pendingId!=null&&instance==pendingInstance&&Convert.ToString(d["request_id"])!=pendingId&&(DateTime.UtcNow-pendingSince).TotalSeconds<20)return;
                string phase=Convert.ToString(d["status"]);
                if(phase=="connecting"||phase=="starting_host"||phase=="loading"||(phase=="connected"&&guest.Checked)){UpdateStatus();return;}
                // Disabled mods have no native snapshot. The mod enables and
                // validates character readiness before dispatching any travel.
                if(phase!="disabled"&&!PlayerPrepared(d)){ShowPreparation(d);return;}
                SaveConnection();
                pendingId=Guid.NewGuid().ToString("N");pendingInstance=instance;pendingSince=DateTime.UtcNow;
                try{EnsureDiagnostics();diagnostics.BeginAttempt(host.Checked?"host":"join",pendingId,p.Id,p.StartTime.ToUniversalTime(),instance);}catch{reportLink.Text=T("Автосбор недоступен — проверьте выбранную папку игры.");}
                GameSettings.JsonWrite(Installer.Inside(GameRoot(),"BL4-LAN-Data/request-"+p.Id+".json"),new{id=pendingId,instance=instance,action=host.Checked?"host":"join",address=guest.Checked?ValidateAddress(address.Text):"",expires=(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds+60});
                SetStatus(T("ЗАПРОС ПЕРЕДАН"),host.Checked?T("Создаём хост. Дождитесь перезагрузки локации."):T("Ожидаем подтверждение от игры…"),yellow);
            }
        }
        void UpdateStatus(){
            UpdateDiagnostics();
            try{
                using(var p=FindRunning()){
                    if(p==null){if(hadRuntime){SetStatus(T("ИГРА ЗАКРЫТА"),T("Можно изменить ник, установить обновление или запустить игру."),yellow);hadRuntime=false;}return;}
                    var d=ReadRuntime(p,false);if(d==null)return;hadRuntime=true;
                    string phase=Convert.ToString(d["status"]),message=Convert.ToString(d["message"]);
                    if(pendingId!=null){
                        if(Convert.ToString(d["instance"])!=pendingInstance){pendingId=null;}
                        else if(Convert.ToString(d["request_id"])==pendingId)pendingId=null;
                        else if((DateTime.UtcNow-pendingSince).TotalSeconds<20)return;
                        else {SetStatus(T("ИГРА НЕ ПОДТВЕРДИЛА ЗАПРОС"),T("Дождитесь главного меню или игрового мира и повторите. Резервные клавиши: F6 — хост, F7 — вход."),red);return;}
                    }
                    double age=(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds-Convert.ToDouble(d["timestamp"]);
                    if(age>8){SetStatus(T("ОЖИДАЕМ ИГРУ"),T("Нет свежих данных от мода. Возможно, игра загружает локацию. Состояние соединения сейчас не подтверждено."),yellow);return;}
                    if(phase=="ready"&&!PlayerPrepared(d)){ShowPreparation(d);return;}
                    string title=phase=="connected"?T("СОЕДИНЕНИЕ УСТАНОВЛЕНО"):phase=="hosting"?T("ХОСТ ГОТОВ"):phase=="connecting"?T("ПОДКЛЮЧЕНИЕ"):phase=="starting_host"?T("СОЗДАНИЕ ХОСТА"):phase=="loading"?T("ЗАГРУЗКА ЛОКАЦИИ"):phase=="disabled"?T("МОД ОТКЛЮЧЁН"):phase=="error"||phase=="disconnected"?T("ОШИБКА СОЕДИНЕНИЯ"):T("МОД ГОТОВ");
                    Color color=phase=="connected"||phase=="hosting"?green:phase=="error"||phase=="disconnected"?red:yellow;
                    if(d.ContainsKey("elapsed_seconds")&&(phase=="connecting"||phase=="starting_host"))message+="  "+d["elapsed_seconds"]+T(" с.");
                    SetStatus(title,message,color);
                    if(phase!=lastPhase){if(phase=="connected"||phase=="hosting"||phase=="error"||phase=="disconnected")ShowNotice(title,message,color);lastPhase=phase;}
                }
            }catch{if(hadRuntime)SetStatus(T("СОСТОЯНИЕ НЕДОСТУПНО"),T("Не удалось прочитать свежий ответ игры. Текущее соединение не подтверждено. Дождитесь завершения загрузки или откройте журнал."),yellow);}
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


