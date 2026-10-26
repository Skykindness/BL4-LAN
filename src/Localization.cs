using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BL4LAN {
    // Presentation only: runtime JSON and copied diagnostics retain their original text.
    public static class Localization {
        static readonly List<KeyValuePair<string,string>> translations=Build();
        static List<KeyValuePair<string,string>> Build(){
            var result=new List<KeyValuePair<string,string>>();
            foreach(string row in Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                int split=row.IndexOf('|');if(split<0)continue;
                result.Add(new KeyValuePair<string,string>(row.Substring(0,split).Replace("\\n","\n"),row.Substring(split+1).Replace("\\n","\n")));
            }
            return result;
        }
        public static string Translate(string value,string language){
            if(String.IsNullOrEmpty(value))return value;
            bool english=language=="en";
            var duration=System.Text.RegularExpressions.Regex.Match(value,@"^(.*?)(  [0-9]+(?:[.,][0-9]+)? )(?:с\.|s\.)$");
            if(duration.Success)return Translate(duration.Groups[1].Value,language)+duration.Groups[2].Value+(english?"s.":"с.");
            const string prefix="BL4 LAN  /  ";if(value.StartsWith(prefix,StringComparison.Ordinal))return prefix+Translate(value.Substring(prefix.Length),language);
            // Exact entries avoid replacing short button names inside complete instructions.
            foreach(var pair in translations)if(value==pair.Key||value==pair.Value)return english?pair.Value:pair.Key;
            // Runtime messages contain addresses, player counts and raw error details.
            foreach(var pair in fragments)value=value.Replace(english?pair.Key:pair.Value,english?pair.Value:pair.Key);
            return value;
        }
        static readonly KeyValuePair<string,string>[] fragments={
            new KeyValuePair<string,string>("Подключаемся к ","Connecting to "),
            new KeyValuePair<string,string>(". Ожидаем ответ хоста…",". Waiting for the host…"),
            new KeyValuePair<string,string>("Друг подключился","Your friend connected"),
            new KeyValuePair<string,string>("Подключено к хосту","Connected to the host"),
            new KeyValuePair<string,string>(". Игроков: ",". Players: "),
            new KeyValuePair<string,string>("Не удалось загрузить BL4 LAN: ","Could not load BL4 LAN: "),
            new KeyValuePair<string,string>("Обнаружен изменённый файл:\n","A modified file was found:\n"),
            new KeyValuePair<string,string>("\nОн сохранён. Обновление не установлено поверх чужих изменений.","\nThe file was kept. The update did not overwrite external changes."),
            new KeyValuePair<string,string>("Обновление прервано; часть файлов заблокирована. Закройте игру и повторите установку. Резервная копия: ","Update interrupted; some files are locked. Close the game and install again. Backup: ")
        };
        const string Text=@"Играть|Play
Настройки|Settings
Назад|Back
СНАЧАЛА ЗАГРУЗИТЕ ПЕРСОНАЖА|LOAD YOUR CHARACTER FIRST
Сначала нажмите «Продолжить» в игре и загрузите персонажа в свой игровой мир. Затем создайте хост или подключитесь к другу.|Choose Continue in the game and load your character into your own world. Then host or connect to your friend.
Персонаж ещё загружается. Дождитесь игрового мира и интерфейса, затем повторите действие.|Your character is still loading. Wait for the game world and HUD, then try again.
Сначала нажмите «Продолжить» в игре и загрузите своего персонажа.\nЗатем подключитесь по адресу хоста. Лаунчер оставьте открытым.|Choose Continue in the game and load your character first.\nThen connect to the host's address. Keep the launcher open.
Нажмите «Продолжить», загрузите своего персонажа, затем нажмите «Подключиться».|Choose Continue, load your character, then click Connect.
Автосбор уже работает в другом окне лаунчера.|Automatic diagnostics are already running in another launcher window.
Язык|Language
Донат|Donate
Автосбор включён — оставьте лаунчер открытым или сверните в трей.|Automatic diagnostics ready — keep the launcher open or minimize to tray.
Автосбор записывает сессию. При сбое отчёт сохранится автоматически.|Recording the session. A report will be saved automatically after a problem.
Отчёт сессии сохранён — открыть папку с ZIP для отправки.|Session report saved — open the folder containing the ZIP to share.
Автосбор: отчёт пока не сохранён. Проверьте свободное место и доступ к папке игры.|Report not saved yet. Check free space and access to the game folder.
Автосбор недоступен — проверьте выбранную папку игры.|Automatic diagnostics unavailable — check the selected game folder.
ОТЧЁТ ГОТОВ|REPORT READY
Отчёт сессии сохранён. Ссылка на папку с ZIP находится внизу лаунчера.|Session report saved. The link to the ZIP folder is at the bottom of the launcher.
Открыть BL4 LAN|Open BL4 LAN
Выйти из лаунчера|Exit launcher
Настройки игры|Game settings
Обычно достаточно указать папку один раз. Ник меняется после перезапуска игры.|Usually you only need to select the folder once. A new nickname applies after restarting the game.
Папка Borderlands 4|Borderlands 4 folder
Выбрать…|Browse…
Выберите папку Borderlands 4, содержащую OakGame|Select the Borderlands 4 folder containing OakGame
Ник игрока|Player nickname
Сохранить ник|Save nickname
НИК СОХРАНЁН|NICKNAME SAVED
Новое имя появится при следующем запуске игры.|Your new name will appear the next time you start the game.
Предыдущее значение сохраняется\nв резервной копии.|The previous value is kept\nin a backup.
Ваши персонажи|Your characters
Используются ваши обычные персонажи игры.|Your regular game characters are used.
Перед сменой ника или обновлением закройте игру.\nПредыдущие настройки сохраняются в резервной копии.|Close the game before changing your nickname or updating.\nThe previous settings are kept in a backup.
Готово — вернуться к игре|Done — back to play
СОЗДАТЬ ИГРУ\nДруг подключится ко мне|HOST A GAME\nMy friend will connect to me
ПОДКЛЮЧИТЬСЯ\nУ меня есть адрес друга|JOIN A GAME\nI have my friend's address
Мой адрес — отправьте его другу|My address — send it to your friend
Скопировать адрес|Copy address
АДРЕС СКОПИРОВАН|ADDRESS COPIED
Отправьте адрес другу. Оба компьютера должны быть в общей LAN или VPN.|Send the address to your friend. Both computers must share a LAN or VPN.
АДРЕС НЕ НАЙДЕН|ADDRESS NOT FOUND
Подключитесь к LAN или VPN и откройте программу заново.|Connect to a LAN or VPN and reopen the launcher.
Адрес друга|Friend's address
Например: 26.1.2.3:7777|Example: 26.1.2.3:7777
1  УСТАНОВИТЕ МОД|1  INSTALL THE MOD
Один раз или после обновления|Once, or after an update
Установить мод|Install mod
2  ЗАПУСТИТЕ ИГРУ|2  START THE GAME
Загрузите персонажа в мир|Load your character into the world
Запустить игру|Start game
3  ИГРАЙТЕ ВМЕСТЕ|3  PLAY TOGETHER
Откройте игру для друга|Open your game to your friend
Создать хост|Create host
ПЕРЕД ПЕРВЫМ ЗАПУСКОМ|BEFORE YOUR FIRST GAME
Выберите роль и следуйте шагам слева направо.|Choose your role and follow the steps from left to right.
Общая LAN / VPN  ·  7777 UDP|Shared LAN / VPN  ·  7777 UDP
Открыть журнал|Open log
Журнал появится после запуска мода.|The log will appear after the mod starts.
Скопировать сведения|Copy diagnostics
СВЕДЕНИЯ СКОПИРОВАНЫ|DIAGNOSTICS COPIED
Их можно приложить к сообщению о проблеме.|You can attach them to a problem report.
МОД УСТАНОВЛЕН|MOD INSTALLED
1  ОБНОВИТЕ МОД|1  UPDATE THE MOD
1  ВЫБЕРИТЕ ПАПКУ ИГРЫ|1  SELECT THE GAME FOLDER
1  РАСПАКУЙТЕ ВЕСЬ АРХИВ|1  EXTRACT THE FULL ARCHIVE
1  ПРОВЕРЬТЕ УСТАНОВКУ|1  CHECK THE INSTALLATION
Повторная установка не нужна. Можно запускать игру.|The mod is installed. You can start the game.
Только один раз перед первой игрой|Only once before your first game
Версия отличается или не хватает файлов|The version differs or files are missing
Укажите папку в настройках|Select the folder in Settings
Не хватает файлов рядом с программой|Files are missing next to the launcher
Не удалось прочитать файлы игры|Could not read the game files
Выбрать папку|Select folder
Установить / обновить|Install / update
Проверить снова|Check again
1  ЗАПУСТИТЕ ИГРУ|1  START THE GAME
2  ИГРАЙТЕ ВМЕСТЕ|2  PLAY TOGETHER
ГОТОВ К ЗАПУСКУ|READY TO START
Мод уже установлен. Выберите роль, запустите игру и подключайтесь.|The mod is installed. Choose your role, start the game and connect.
Выполните первый шаг выше. Повторять установку перед каждой игрой не нужно.|Complete the first step above. You do not need to reinstall before every game.
Подключиться|Connect
Дождитесь главного меню|Wait for the main menu
Войдите по адресу выше|Join using the address above
Создание хоста перезагрузит локацию. Дождитесь автосохранения.\nПосле создания хоста оставьте программу открытой — здесь будет статус друга.|Creating a host reloads the area. Wait for autosave first.\nKeep the launcher open after hosting to see your friend's status here.
Сначала хост должен открыть свою игру. Адрес нужен от той же LAN или VPN.\nОставьте программу открытой — здесь появится соединение или причина ошибки.|The host must open their game first. Use an address from the same LAN or VPN.\nKeep the launcher open to see the connection status or an error here.
НЕ УДАЛОСЬ|FAILED
Не удалось|Failed
Выберите папку игры с папкой OakGame.|Select the game folder containing OakGame.
При обновлении сохранён ранее выбранный набор персонажей.\nСохранения не переносятся и не объединяются.|Your previously selected character set was preserved during the update.\nSaves are neither moved nor merged.
УСТАНОВКА|INSTALLING
Проверяем и устанавливаем файлы…|Checking and installing files…
ГОТОВО|DONE
Введите адрес хоста, например 26.1.2.3:7777.|Enter the host's address, for example 26.1.2.3:7777.
Некорректный адрес или порт хоста.|Invalid host address or port.
Нужен IPv4-адрес компьютера друга.|An IPv4 address for your friend's computer is required.
Сначала выполните первый шаг в программе: выберите папку, установите мод или распакуйте полный пакет.|Complete the first step: select the game folder, install the mod or extract the full package.
ИГРА УЖЕ ЗАПУЩЕНА|GAME ALREADY RUNNING
Для входа используйте кнопку «Подключиться», для хоста — «Создать хост».|To join, use Connect; to host, use Create host.
Сначала нажмите «Установить / обновить», чтобы установить эту версию мода.|Click Install / update first to install this mod version.
Сначала установите SDK и мод.|Install the SDK and mod first.
ИГРА ЗАПУСКАЕТСЯ|GAME STARTING
Загрузите персонажа в мир, затем нажмите «Создать хост».|Load your character into the world, then click Create host.
Дождитесь главного меню, затем нажмите «Подключиться».|Wait for the main menu, then click Connect.
Запущено несколько копий игры. Оставьте одну для управления из лаунчера.|Multiple game instances are running. Keep one open to control it from the launcher.
Сначала запустите игру.|Start the game first.
Мод ещё не загрузился. Дождитесь главного меню. Если сообщения нет — установите обновление и перезапустите игру.|The mod has not loaded yet. Wait for the main menu. If no message appears, install the update and restart the game.
В игре ещё старая версия мода. Закройте игру, установите обновление и запустите её заново.|The game is using an older mod version. Close it, install the update and start it again.
Нет свежего ответа от игры. Дождитесь окончания загрузки и повторите действие.|No recent response from the game. Wait until loading finishes and try again.
ЗАПРОС ПЕРЕДАН|REQUEST SENT
Создаём хост. Дождитесь перезагрузки локации.|Creating the host. Wait for the area to reload.
Ожидаем подтверждение от игры…|Waiting for confirmation from the game…
ИГРА ЗАКРЫТА|GAME CLOSED
Можно изменить ник, установить обновление или запустить игру.|You can change your nickname, install an update or start the game.
ИГРА НЕ ПОДТВЕРДИЛА ЗАПРОС|GAME HAS NOT CONFIRMED THE REQUEST
Дождитесь главного меню или игрового мира и повторите. Резервные клавиши: F6 — хост, F7 — вход.|Wait for the main menu or game world, then try again. Backup keys: F6 to host, F7 to join.
ОЖИДАЕМ ИГРУ|WAITING FOR THE GAME
Нет свежих данных от мода. Возможно, игра загружает локацию. Состояние соединения сейчас не подтверждено.|No recent mod data. The game may be loading an area. The connection status is currently unconfirmed.
СОЕДИНЕНИЕ УСТАНОВЛЕНО|CONNECTED
ХОСТ ГОТОВ|HOST READY
ПОДКЛЮЧЕНИЕ|CONNECTING
СОЗДАНИЕ ХОСТА|CREATING HOST
ЗАГРУЗКА ЛОКАЦИИ|LOADING AREA
МОД ОТКЛЮЧЁН|MOD DISABLED
ОШИБКА СОЕДИНЕНИЯ|CONNECTION ERROR
МОД ГОТОВ|MOD READY
 с.| s.
СОСТОЯНИЕ НЕДОСТУПНО|STATUS UNAVAILABLE
Не удалось прочитать свежий ответ игры. Текущее соединение не подтверждено. Дождитесь завершения загрузки или откройте журнал.|Could not read a recent game response. The current connection is unconfirmed. Wait for loading to finish or open the log.
Некорректный путь пакета.|Invalid package path.
Путь выходит из папки игры.|The path is outside the game folder.
Некорректная запись журнала установки.|Invalid installation journal entry.
Путь журнала выходит из папки игры.|The journal path is outside the game folder.
Журнал установки повреждён; он сохранён без изменений.|The installation journal is damaged; it was kept unchanged.
Журнал установки повреждён.|The installation journal is damaged.
Противоречивые записи журнала установки.|The installation journal contains conflicting entries.
Сумма файла отличается от журнала установки.|The file checksum differs from the installation journal.
Файл не соответствует журналу предыдущей установки.|The file does not match the previous installation journal.
Папка установки содержит ссылку. Выберите обычную папку игры.|The installation folder contains a link. Select a regular game folder.
Закройте Borderlands 4 перед установкой обновления или сменой ника. Игра не будет закрыта автоматически.|Close Borderlands 4 before updating or changing your nickname. The game will not be closed automatically.
Не удалось проверить запущенную игру. Закройте её перед обновлением.|Could not check the running game. Close it before updating.
Выберите корневую папку Borderlands 4 с папкой OakGame.|Select the Borderlands 4 root folder containing OakGame.
Архив SDK отсутствует или повреждён. Распакуйте полный пакет BL4 LAN заново.|The SDK archive is missing or damaged. Extract the full BL4 LAN package again.
Недопустимый размер файла SDK.|Invalid SDK file size.
Не хватает файлов мода. Распакуйте весь архив рядом с лаунчером.|Mod files are missing. Extract the full archive next to the launcher.
BL4 LAN обновлён. Предыдущая версия сохранена в резервной копии.|BL4 LAN updated. The previous version was saved in a backup.
BL4 LAN установлен и включён. Можно запускать игру.|BL4 LAN installed and enabled. You can start the game.
В этой установке не найден файл настройки ника. Смена имени доступна для репака со steam_emu.ini.|The nickname settings file was not found. Changing the name requires a repack with steam_emu.ini.
Не удалось однозначно найти имя игрока. Исходная настройка сохранена.|Could not identify a unique player name. The original setting was kept.
Ник: от 1 до 32 букв, цифр, пробелов или символов _ . -|Nickname: 1 to 32 letters, digits, spaces or the symbols _ . -
Укажите IPv4-адрес хоста.|Enter the host's IPv4 address.
Адрес должен иметь вид 192.168.1.5 или 192.168.1.5:7777.|Use an address such as 192.168.1.5 or 192.168.1.5:7777.
Укажите адрес одного компьютера и порт от 1024 до 65535.|Enter one computer address and a port from 1024 to 65535.
Сначала загрузите персонажа в игровой мир, затем нажмите F6.|Load your character into the game world first, then press F6.
Создать хост можно после загрузки игровой локации.|You can create a host after loading a game area.
Дождитесь главного меню и нажмите F7 ещё раз.|Wait for the main menu and press F7 again.
Повреждены настройки включения BL4 LAN. Переустановите мод через лаунчер.|BL4 LAN activation settings are damaged. Reinstall the mod using the launcher.
Не удалось определить текущую локацию. Загрузите персонажа в мир.|Could not identify the current area. Load your character into the world.
Хост не ответил вовремя. Проверьте адрес, LAN/VPN и готовность хоста; повторите подключение.|The host did not respond in time. Check the address, LAN/VPN and host readiness, then try connecting again.
Соединение с другом потеряно. Можно подключиться ещё раз.|The connection to your friend was lost. You can try connecting again.
Не удалось создать хост. Закройте другую запущенную копию игры и повторите.|Could not create a host. Close any other running game instance and try again.
Игра отклонила подключение или загрузку локации. Проверьте одинаковую сборку игры и готовность хоста.|The game rejected the connection or area loading. Check that both game builds match and the host is ready.
BL4 LAN включён. Создайте хост или подключитесь через лаунчер.|BL4 LAN is enabled. Host or join using the launcher.
Не удалось включить сеть игры. Посмотрите сообщение в лаунчере.|Could not enable the game's network. Check the message in the launcher.
Адрес друга не указан. В лаунчере выберите «Подключиться» и введите его LAN/VPN-адрес. Если вы хост, используйте «Создать хост» или F6.|Your friend's address is missing. Select Join in the launcher and enter their LAN/VPN address. If you are hosting, use Create host or F6.
Слишком много аргументов. Укажите один адрес IPv4[:порт].|Too many arguments. Enter one address as IPv4[:port].
Мод готов. Создайте хост или подключитесь через лаунчер.|The mod is ready. Host or join using the launcher.
Мод отключён. Кнопка создания хоста или входа включит его.|The mod is disabled. Hosting or joining will enable it.
Хост создан. Ожидаем друга.|Host created. Waiting for your friend.
Друг отключился. Хост ожидает подключения.|Your friend disconnected. The host is waiting for a connection.
Хост временно недоступен. Ожидаем загрузку игрового мира.|The host is temporarily unavailable. Waiting for the game world to load.
Хост больше не активен. Загрузите персонажа в мир и создайте хост заново.|The host is no longer active. Load your character into the world and create a host again.
Ожидаем загрузку локации и персонажа. Проверяем состояние соединения…|Waiting for the area and character to load. Checking the connection status…
Соединение с хостом сейчас не подтверждено. Можно повторить вход.|The connection to the host is currently unconfirmed. You can try joining again.
Игра не подтвердила соединение за 45 секунд. Проверьте адрес, общую LAN/VPN и готовность хоста; затем повторите вход.|The game did not confirm the connection within 45 seconds. Check the address, shared LAN/VPN and host readiness, then try joining again.";
    }
    sealed class RainbowButton : Button {
        readonly Timer animation;readonly System.Diagnostics.Stopwatch elapsed=System.Diagnostics.Stopwatch.StartNew();
        public RainbowButton(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            FlatStyle=FlatStyle.Flat;ForeColor=SystemColors.ControlText;BackColor=SystemColors.Control;Font=new Font("Segoe UI",11,FontStyle.Bold);
            AccessibleRole=AccessibleRole.PushButton;Cursor=Cursors.Hand;TabStop=true;
            animation=new Timer {Interval=40};animation.Tick+=delegate{if(Visible&&!SystemInformation.HighContrast)Invalidate();};animation.Start();
        }
        static Color Hue(double hue){
            // Keep even the blue/violet regions bright enough for black text.
            double x=1-Math.Abs(hue/60%2-1);int rising=155+(int)(x*100);int sector=(int)(hue/60)%6;
            return sector==0?Color.FromArgb(255,rising,155):sector==1?Color.FromArgb(rising,255,155):sector==2?Color.FromArgb(155,255,rising):sector==3?Color.FromArgb(155,rising,255):sector==4?Color.FromArgb(rising,155,255):Color.FromArgb(255,155,rising);
        }
        protected override void OnPaint(PaintEventArgs e){
            if(SystemInformation.HighContrast){base.OnPaint(e);return;}
            var bounds=ClientRectangle;if(bounds.Width<8||bounds.Height<8)return;
            double hue=elapsed.Elapsed.TotalSeconds*24%360;
            using(var gradient=new LinearGradientBrush(bounds,Hue(hue),Hue(hue),0f)){
                gradient.InterpolationColors=new ColorBlend {Colors=new[]{Hue(hue),Hue((hue+60)%360),Hue((hue+120)%360),Hue((hue+180)%360),Hue((hue+240)%360),Hue((hue+300)%360),Hue(hue)},Positions=new[]{0f,1f/6,2f/6,.5f,4f/6,5f/6,1f}};
                e.Graphics.FillRectangle(gradient,bounds);
            }
            Rectangle inner=Rectangle.Inflate(bounds,-3,-3);
            var shadow=inner;shadow.Offset(1,1);
            TextRenderer.DrawText(e.Graphics,Text,Font,shadow,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics,Text,Font,inner,Enabled?Color.Black:SystemColors.GrayText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(inner,-3,-3),Color.Black,Color.White);
        }
        protected override void Dispose(bool disposing){if(disposing){animation.Stop();animation.Dispose();elapsed.Stop();}base.Dispose(disposing);}
    }
}
