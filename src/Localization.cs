using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BL4LAN {
    // Presentation only: runtime JSON and copied diagnostics retain their original text.
    public static class Localization {
        static readonly List<string[]> translations=Build();
        static List<string[]> Build(){
            var result=new List<string[]>();
            foreach(string row in Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                string[] parts=row.Split(new[]{'|'},3);if(parts.Length<3)continue;
                for(int i=0;i<3;i++)parts[i]=parts[i].Replace("\\n","\n");
                result.Add(parts);
            }
            return result;
        }
        static int Index(string language){return language=="en"?1:language=="zh"?2:0;}
        public static string Translate(string value,string language){
            if(String.IsNullOrEmpty(value))return value;
            int index=Index(language);
            var duration=System.Text.RegularExpressions.Regex.Match(value,@"^(.*?)(  [0-9]+(?:[.,][0-9]+)? )(?:с\.|s\.|秒)$");
            if(duration.Success)return Translate(duration.Groups[1].Value,language)+duration.Groups[2].Value+(index==1?"s.":index==2?"秒":"с.");
            const string prefix="BL4 LAN  /  ";if(value.StartsWith(prefix,StringComparison.Ordinal))return prefix+Translate(value.Substring(prefix.Length),language);
            // Exact entries avoid replacing short button names inside complete instructions.
            foreach(var row in translations)for(int i=0;i<row.Length;i++)if(value==row[i])return row[index];
            // Runtime messages contain addresses, player counts and raw error details.
            foreach(var row in fragments)for(int i=0;i<row.Length;i++)if(i!=index)value=value.Replace(row[i],row[index]);
            return value;
        }
        static readonly string[][] fragments=new[]{
            new[]{"Подключаемся к ","Connecting to ","正在连接 "},
            new[]{". Ожидаем ответ хоста…",". Waiting for the host…","。等待主机响应…"},
            new[]{"Друг подключился","Your friend connected","好友已连接"},
            new[]{"Подключено к хосту","Connected to the host","已连接到主机"},
            new[]{". Игроков: ",". Players: ","。玩家数："},
            new[]{"Не удалось загрузить BL4 LAN: ","Could not load BL4 LAN: ","无法加载 BL4 LAN："},
            new[]{"Обнаружен изменённый файл:\n","A modified file was found:\n","发现被修改的文件：\n"},
            new[]{"\nОн сохранён. Обновление не установлено поверх чужих изменений.","\nThe file was kept. The update did not overwrite external changes.","\n文件已保留，更新未覆盖外部改动。"},
            new[]{"Обновление прервано; часть файлов заблокирована. Закройте игру и повторите установку. Резервная копия: ","Update interrupted; some files are locked. Close the game and install again. Backup: ","更新被中断，部分文件被占用。请关闭游戏后重新安装。备份："}
        };
        const string Text=@"Играть|Play|开始游戏
Настройки|Settings|设置
Назад|Back|返回
СНАЧАЛА ЗАГРУЗИТЕ ПЕРСОНАЖА|LOAD YOUR CHARACTER FIRST|请先载入角色
Сначала нажмите «Продолжить» в игре и загрузите персонажа в свой игровой мир. Затем создайте хост или подключитесь к другу.|Choose Continue in the game and load your character into your own world. Then host or connect to your friend.|请先在游戏中点击「继续」，将角色载入你自己的游戏世界。然后创建主机或连接好友。
Персонаж ещё загружается. Дождитесь игрового мира и интерфейса, затем повторите действие.|Your character is still loading. Wait for the game world and HUD, then try again.|角色仍在载入。请等待游戏世界和界面就绪后重试。
Сначала нажмите «Продолжить» в игре и загрузите своего персонажа.\nЗатем подключитесь по адресу хоста. Лаунчер оставьте открытым.|Choose Continue in the game and load your character first.\nThen connect to the host's address. Keep the launcher open.|请先在游戏中点击「继续」并载入角色。\n然后输入主机地址进行连接。保持启动器开启。
Нажмите «Продолжить», загрузите своего персонажа, затем нажмите «Подключиться».|Choose Continue, load your character, then click Connect.|点击「继续」，载入角色，然后点击「连接」。
Автосбор уже работает в другом окне лаунчера.|Automatic diagnostics are already running in another launcher window.|自动诊断已在另一个启动器窗口中运行。
Язык|Language|语言
Донат|Donate|捐赠
Автосбор включён — оставьте лаунчер открытым или сверните в трей.|Automatic diagnostics ready — keep the launcher open or minimize to tray.|自动诊断已开启——保持启动器打开或最小化到托盘。
Автосбор записывает сессию. При сбое отчёт сохранится автоматически.|Recording the session. A report will be saved automatically after a problem.|正在记录本次会话。出现问题时将自动保存报告。
Отчёт сессии сохранён — открыть папку с ZIP для отправки.|Session report saved — open the folder containing the ZIP to share.|会话报告已保存——打开 ZIP 所在文件夹以便发送。
Автосбор: отчёт пока не сохранён. Проверьте свободное место и доступ к папке игры.|Report not saved yet. Check free space and access to the game folder.|自动诊断：报告尚未保存。请检查磁盘空间和游戏文件夹的访问权限。
Автосбор недоступен — проверьте выбранную папку игры.|Automatic diagnostics unavailable — check the selected game folder.|自动诊断不可用——请检查所选的游戏文件夹。
ОТЧЁТ ГОТОВ|REPORT READY|报告就绪
Отчёт сессии сохранён. Ссылка на папку с ZIP находится внизу лаунчера.|Session report saved. The link to the ZIP folder is at the bottom of the launcher.|会话报告已保存。ZIP 文件夹的链接在启动器底部。
Открыть BL4 LAN|Open BL4 LAN|打开 BL4 LAN
Выйти из лаунчера|Exit launcher|退出启动器
Настройки игры|Game settings|游戏设置
Обычно достаточно указать папку один раз. Ник меняется после перезапуска игры.|Usually you only need to select the folder once. A new nickname applies after restarting the game.|通常只需选择一次文件夹。新昵称在重启游戏后生效。
Папка Borderlands 4|Borderlands 4 folder|Borderlands 4 文件夹
Выбрать…|Browse…|浏览…
Выберите папку Borderlands 4, содержащую OakGame|Select the Borderlands 4 folder containing OakGame|请选择包含 OakGame 的 Borderlands 4 文件夹
Ник игрока|Player nickname|玩家昵称
Сохранить ник|Save nickname|保存昵称
НИК СОХРАНЁН|NICKNAME SAVED|昵称已保存
Новое имя появится при следующем запуске игры.|Your new name will appear the next time you start the game.|新名字将在下次启动游戏时生效。
Предыдущее значение сохраняется\nв резервной копии.|The previous value is kept\nin a backup.|先前的设置已保留\n在备份中。
Ваши персонажи|Your characters|你的角色
Используются ваши обычные персонажи игры.|Your regular game characters are used.|使用你平时的游戏角色。
Перед сменой ника или обновлением закройте игру.\nПредыдущие настройки сохраняются в резервной копии.|Close the game before changing your nickname or updating.\nThe previous settings are kept in a backup.|修改昵称或更新前请先关闭游戏。\n之前的设置会保存在备份中。
Готово — вернуться к игре|Done — back to play|完成——返回游戏
СОЗДАТЬ ИГРУ\nДруг подключится ко мне|HOST A GAME\nMy friend will connect to me|创建游戏\n好友将连接到我
ПОДКЛЮЧИТЬСЯ\nУ меня есть адрес друга|JOIN A GAME\nI have my friend's address|加入游戏\n我有好友的地址
Мой адрес — отправьте его другу|My address — send it to your friend|我的地址——发给好友
Скопировать адрес|Copy address|复制地址
АДРЕС СКОПИРОВАН|ADDRESS COPIED|地址已复制
Отправьте адрес другу. Оба компьютера должны быть в общей LAN или VPN.|Send the address to your friend. Both computers must share a LAN or VPN.|把地址发给好友。两台电脑必须在同一局域网或 VPN 中。
АДРЕС НЕ НАЙДЕН|ADDRESS NOT FOUND|未找到地址
Подключитесь к LAN или VPN и откройте программу заново.|Connect to a LAN or VPN and reopen the launcher.|请连接局域网或 VPN 后重新打开程序。
Адрес друга|Friend's address|好友的地址
Например: 26.1.2.3:7777|Example: 26.1.2.3:7777|例如：26.1.2.3:7777
1  УСТАНОВИТЕ МОД|1  INSTALL THE MOD|1  安装模组
Один раз или после обновления|Once, or after an update|仅需一次，或更新后执行
Установить мод|Install mod|安装模组
2  ЗАПУСТИТЕ ИГРУ|2  START THE GAME|2  启动游戏
Загрузите персонажа в мир|Load your character into the world|将角色载入游戏世界
Запустить игру|Start game|启动游戏
3  ИГРАЙТЕ ВМЕСТЕ|3  PLAY TOGETHER|3  一起游玩
Откройте игру для друга|Open your game to your friend|向好友开放你的游戏
Создать хост|Create host|创建主机
ПЕРЕД ПЕРВЫМ ЗАПУСКОМ|BEFORE YOUR FIRST GAME|首次游玩前
Выберите роль и следуйте шагам слева направо.|Choose your role and follow the steps from left to right.|选择你的身份，然后按从左到右的步骤操作。
Общая LAN / VPN  ·  7777 UDP|Shared LAN / VPN  ·  7777 UDP|共享局域网 / VPN  ·  7777 UDP
Открыть журнал|Open log|打开日志
Журнал появится после запуска мода.|The log will appear after the mod starts.|日志将在模组启动后出现。
Скопировать сведения|Copy diagnostics|复制诊断信息
СВЕДЕНИЯ СКОПИРОВАНЫ|DIAGNOSTICS COPIED|诊断信息已复制
Их можно приложить к сообщению о проблеме.|You can attach them to a problem report.|可将其附在问题反馈中。
МОД УСТАНОВЛЕН|MOD INSTALLED|模组已安装
1  ОБНОВИТЕ МОД|1  UPDATE THE MOD|1  更新模组
1  ВЫБЕРИТЕ ПАПКУ ИГРЫ|1  SELECT THE GAME FOLDER|1  选择游戏文件夹
1  РАСПАКУЙТЕ ВЕСЬ АРХИВ|1  EXTRACT THE FULL ARCHIVE|1  解压完整压缩包
1  ПРОВЕРЬТЕ УСТАНОВКУ|1  CHECK THE INSTALLATION|1  检查安装
Повторная установка не нужна. Можно запускать игру.|The mod is installed. You can start the game.|无需重复安装，可以启动游戏了。
Только один раз перед первой игрой|Only once before your first game|首次游玩前只需一次
Версия отличается или не хватает файлов|The version differs or files are missing|版本不一致或缺少文件
Укажите папку в настройках|Select the folder in Settings|请在设置中选择文件夹
Не хватает файлов рядом с программой|Files are missing next to the launcher|启动器旁缺少文件
Не удалось прочитать файлы игры|Could not read the game files|无法读取游戏文件
Выбрать папку|Select folder|选择文件夹
Установить / обновить|Install / update|安装 / 更新
Проверить снова|Check again|重新检查
1  ЗАПУСТИТЕ ИГРУ|1  START THE GAME|1  启动游戏
2  ИГРАЙТЕ ВМЕСТЕ|2  PLAY TOGETHER|2  一起游玩
ГОТОВ К ЗАПУСКУ|READY TO START|准备就绪
Мод уже установлен. Выберите роль, запустите игру и подключайтесь.|The mod is installed. Choose your role, start the game and connect.|模组已安装。选择你的身份，启动游戏并连接。
Выполните первый шаг выше. Повторять установку перед каждой игрой не нужно.|Complete the first step above. You do not need to reinstall before every game.|请完成上面的第一步。每次游玩前无需重复安装。
Подключиться|Connect|连接
Дождитесь главного меню|Wait for the main menu|等待主菜单
Войдите по адресу выше|Join using the address above|使用上方地址加入
Создание хоста перезагрузит локацию. Дождитесь автосохранения.\nПосле создания хоста оставьте программу открытой — здесь будет статус друга.|Creating a host reloads the area. Wait for autosave first.\nKeep the launcher open after hosting to see your friend's status here.|创建主机会重新加载区域。请先等待自动保存。\n创建主机后保持程序开启——这里会显示好友的状态。
Сначала хост должен открыть свою игру. Адрес нужен от той же LAN или VPN.\nОставьте программу открытой — здесь появится соединение или причина ошибки.|The host must open their game first. Use an address from the same LAN or VPN.\nKeep the launcher open to see the connection status or an error here.|主机需要先开启游戏。请使用同一局域网或 VPN 下的地址。\n保持程序开启——这里会显示连接状态或错误原因。
НЕ УДАЛОСЬ|FAILED|操作失败
Не удалось|Failed|失败
Выберите папку игры с папкой OakGame.|Select the game folder containing OakGame.|请选择包含 OakGame 的游戏文件夹。
При обновлении сохранён ранее выбранный набор персонажей.\nСохранения не переносятся и не объединяются.|Your previously selected character set was preserved during the update.\nSaves are neither moved nor merged.|更新时保留了你之前选择的角色组。\n存档不会被转移或合并。
УСТАНОВКА|INSTALLING|正在安装
Проверяем и устанавливаем файлы…|Checking and installing files…|正在检查并安装文件…
ГОТОВО|DONE|完成
Введите адрес хоста, например 26.1.2.3:7777.|Enter the host's address, for example 26.1.2.3:7777.|请输入主机地址，例如 26.1.2.3:7777。
Некорректный адрес или порт хоста.|Invalid host address or port.|主机地址或端口无效。
Нужен IPv4-адрес компьютера друга.|An IPv4 address for your friend's computer is required.|需要好友电脑的 IPv4 地址。
Сначала выполните первый шаг в программе: выберите папку, установите мод или распакуйте полный пакет.|Complete the first step: select the game folder, install the mod or extract the full package.|请先完成第一步：选择游戏文件夹、安装模组或解压完整安装包。
ИГРА УЖЕ ЗАПУЩЕНА|GAME ALREADY RUNNING|游戏已在运行
Для входа используйте кнопку «Подключиться», для хоста — «Создать хост».|To join, use Connect; to host, use Create host.|加入请点「连接」，创建主机请点「创建主机」。
Сначала нажмите «Установить / обновить», чтобы установить эту версию мода.|Click Install / update first to install this mod version.|请先点击「安装 / 更新」安装此版本模组。
Сначала установите SDK и мод.|Install the SDK and mod first.|请先安装 SDK 和模组。
ИГРА ЗАПУСКАЕТСЯ|GAME STARTING|游戏启动中
Загрузите персонажа в мир, затем нажмите «Создать хост».|Load your character into the world, then click Create host.|将角色载入游戏世界，然后点击「创建主机」。
Дождитесь главного меню, затем нажмите «Подключиться».|Wait for the main menu, then click Connect.|等待主菜单，然后点击「连接」。
Запущено несколько копий игры. Оставьте одну для управления из лаунчера.|Multiple game instances are running. Keep one open to control it from the launcher.|检测到多个游戏实例。请保留一个以便从启动器控制。
Сначала запустите игру.|Start the game first.|请先启动游戏。
Мод ещё не загрузился. Дождитесь главного меню. Если сообщения нет — установите обновление и перезапустите игру.|The mod has not loaded yet. Wait for the main menu. If no message appears, install the update and restart the game.|模组尚未加载。请等待主菜单。如果没有消息出现，请安装更新并重启游戏。
В игре ещё старая версия мода. Закройте игру, установите обновление и запустите её заново.|The game is using an older mod version. Close it, install the update and start it again.|游戏中的模组版本过旧。请关闭游戏，安装更新后重新启动。
Нет свежего ответа от игры. Дождитесь окончания загрузки и повторите действие.|No recent response from the game. Wait until loading finishes and try again.|没有收到游戏的最新响应。请等待加载完成后重试。
ЗАПРОС ПЕРЕДАН|REQUEST SENT|请求已发送
Создаём хост. Дождитесь перезагрузки локации.|Creating the host. Wait for the area to reload.|正在创建主机。请等待区域重新加载。
Ожидаем подтверждение от игры…|Waiting for confirmation from the game…|等待游戏确认…
ИГРА ЗАКРЫТА|GAME CLOSED|游戏已关闭
Можно изменить ник, установить обновление или запустить игру.|You can change your nickname, install an update or start the game.|你可以修改昵称、安装更新或启动游戏。
ИГРА НЕ ПОДТВЕРДИЛА ЗАПРОС|GAME HAS NOT CONFIRMED THE REQUEST|游戏未确认请求
Дождитесь главного меню или игрового мира и повторите. Резервные клавиши: F6 — хост, F7 — вход.|Wait for the main menu or game world, then try again. Backup keys: F6 to host, F7 to join.|请等待主菜单或进入游戏世界后重试。备用快捷键：F6 — 创建主机，F7 — 加入。
ОЖИДАЕМ ИГРУ|WAITING FOR THE GAME|等待游戏中
Нет свежих данных от мода. Возможно, игра загружает локацию. Состояние соединения сейчас не подтверждено.|No recent mod data. The game may be loading an area. The connection status is currently unconfirmed.|没有收到模组的最新数据。游戏可能正在加载区域。当前连接状态未确认。
СОЕДИНЕНИЕ УСТАНОВЛЕНО|CONNECTED|已连接
ХОСТ ГОТОВ|HOST READY|主机就绪
ПОДКЛЮЧЕНИЕ|CONNECTING|连接中
СОЗДАНИЕ ХОСТА|CREATING HOST|正在创建主机
ЗАГРУЗКА ЛОКАЦИИ|LOADING AREA|区域加载中
МОД ОТКЛЮЧЁН|MOD DISABLED|模组已禁用
ОШИБКА СОЕДИНЕНИЯ|CONNECTION ERROR|连接错误
МОД ГОТОВ|MOD READY|模组就绪
 с.| s.| 秒
СОСТОЯНИЕ НЕДОСТУПНО|STATUS UNAVAILABLE|状态不可用
Не удалось прочитать свежий ответ игры. Текущее соединение не подтверждено. Дождитесь завершения загрузки или откройте журнал.|Could not read a recent game response. The current connection is unconfirmed. Wait for loading to finish or open the log.|无法读取游戏的最新响应。当前连接未确认。请等待加载完成或打开日志。
Некорректный путь пакета.|Invalid package path.|安装包路径无效。
Путь выходит из папки игры.|The path is outside the game folder.|路径超出了游戏文件夹。
Некорректная запись журнала установки.|Invalid installation journal entry.|安装日志条目无效。
Путь журнала выходит из папки игры.|The journal path is outside the game folder.|日志路径超出了游戏文件夹。
Журнал установки повреждён; он сохранён без изменений.|The installation journal is damaged; it was kept unchanged.|安装日志已损坏；已原样保留。
Журнал установки повреждён.|The installation journal is damaged.|安装日志已损坏。
Противоречивые записи журнала установки.|The installation journal contains conflicting entries.|安装日志中存在冲突的条目。
Сумма файла отличается от журнала установки.|The file checksum differs from the installation journal.|文件校验和与安装日志不符。
Файл не соответствует журналу предыдущей установки.|The file does not match the previous installation journal.|文件与上次安装日志不匹配。
Папка установки содержит ссылку. Выберите обычную папку игры.|The installation folder contains a link. Select a regular game folder.|安装文件夹包含链接。请选择普通的游戏文件夹。
Закройте Borderlands 4 перед установкой обновления или сменой ника. Игра не будет закрыта автоматически.|Close Borderlands 4 before updating or changing your nickname. The game will not be closed automatically.|更新或修改昵称前请先关闭 Borderlands 4。程序不会自动关闭游戏。
Не удалось проверить запущенную игру. Закройте её перед обновлением.|Could not check the running game. Close it before updating.|无法检查运行中的游戏。更新前请先关闭它。
Выберите корневую папку Borderlands 4 с папкой OakGame.|Select the Borderlands 4 root folder containing OakGame.|请选择包含 OakGame 的 Borderlands 4 根目录。
Архив SDK отсутствует или повреждён. Распакуйте полный пакет BL4 LAN заново.|The SDK archive is missing or damaged. Extract the full BL4 LAN package again.|SDK 压缩包缺失或已损坏。请重新解压完整的 BL4 LAN 安装包。
Недопустимый размер файла SDK.|Invalid SDK file size.|SDK 文件大小无效。
Не хватает файлов мода. Распакуйте весь архив рядом с лаунчером.|Mod files are missing. Extract the full archive next to the launcher.|缺少模组文件。请把完整压缩包解压到启动器旁。
BL4 LAN обновлён. Предыдущая версия сохранена в резервной копии.|BL4 LAN updated. The previous version was saved in a backup.|BL4 LAN 已更新。旧版本已保存在备份中。
BL4 LAN установлен и включён. Можно запускать игру.|BL4 LAN installed and enabled. You can start the game.|BL4 LAN 已安装并启用。可以启动游戏了。
В этой установке не найден файл настройки ника. Смена имени доступна для репака со steam_emu.ini.|The nickname settings file was not found. Changing the name requires a repack with steam_emu.ini.|未找到昵称设置文件。修改名字需要带 steam_emu.ini 的重打包版本。
Не удалось однозначно найти имя игрока. Исходная настройка сохранена.|Could not identify a unique player name. The original setting was kept.|无法确定唯一的玩家名。已保留原始设置。
Ник: от 1 до 32 букв, цифр, пробелов или символов _ . -|Nickname: 1 to 32 letters, digits, spaces or the symbols _ . -|昵称：1 到 32 个字母、数字、空格或符号 _ . -
Укажите IPv4-адрес хоста.|Enter the host's IPv4 address.|请输入主机的 IPv4 地址。
Адрес должен иметь вид 192.168.1.5 или 192.168.1.5:7777.|Use an address such as 192.168.1.5 or 192.168.1.5:7777.|地址格式应为 192.168.1.5 或 192.168.1.5:7777。
Укажите адрес одного компьютера и порт от 1024 до 65535.|Enter one computer address and a port from 1024 to 65535.|请输入一台电脑的地址，端口范围 1024 到 65535。
Сначала загрузите персонажа в игровой мир, затем нажмите F6.|Load your character into the game world first, then press F6.|请先将角色载入游戏世界，然后按 F6。
Создать хост можно после загрузки игровой локации.|You can create a host after loading a game area.|加载游戏区域后才能创建主机。
Дождитесь главного меню и нажмите F7 ещё раз.|Wait for the main menu and press F7 again.|请等待主菜单，然后再次按 F7。
Повреждены настройки включения BL4 LAN. Переустановите мод через лаунчер.|BL4 LAN activation settings are damaged. Reinstall the mod using the launcher.|BL4 LAN 的启用配置已损坏。请通过启动器重新安装模组。
Не удалось определить текущую локацию. Загрузите персонажа в мир.|Could not identify the current area. Load your character into the world.|无法确定当前区域。请将角色载入游戏世界。
Хост не ответил вовремя. Проверьте адрес, LAN/VPN и готовность хоста; повторите подключение.|The host did not respond in time. Check the address, LAN/VPN and host readiness, then try connecting again.|主机未及时响应。请检查地址、局域网/VPN 和主机状态，然后重新连接。
Соединение с другом потеряно. Можно подключиться ещё раз.|The connection to your friend was lost. You can try connecting again.|与好友的连接已断开。可以重新连接。
Не удалось создать хост. Закройте другую запущенную копию игры и повторите.|Could not create a host. Close any other running game instance and try again.|无法创建主机。请关闭其他运行中的游戏实例后重试。
Игра отклонила подключение или загрузку локации. Проверьте одинаковую сборку игры и готовность хоста.|The game rejected the connection or area loading. Check that both game builds match and the host is ready.|游戏拒绝了连接或区域加载。请确认双方游戏版本一致且主机已就绪。
BL4 LAN включён. Создайте хост или подключитесь через лаунчер.|BL4 LAN is enabled. Host or join using the launcher.|BL4 LAN 已启用。请通过启动器创建主机或加入游戏。
Не удалось включить сеть игры. Посмотрите сообщение в лаунчере.|Could not enable the game's network. Check the message in the launcher.|无法启用游戏网络。请查看启动器中的提示信息。
Адрес друга не указан. В лаунчере выберите «Подключиться» и введите его LAN/VPN-адрес. Если вы хост, используйте «Создать хост» или F6.|Your friend's address is missing. Select Join in the launcher and enter their LAN/VPN address. If you are hosting, use Create host or F6.|未填写好友地址。请在启动器中选择「连接」并输入对方的局域网/VPN 地址。如果你是主机，请使用「创建主机」或 F6。
Слишком много аргументов. Укажите один адрес IPv4[:порт].|Too many arguments. Enter one address as IPv4[:port].|参数过多。请只输入一个地址，格式 IPv4[:端口]。
Мод готов. Создайте хост или подключитесь через лаунчер.|The mod is ready. Host or join using the launcher.|模组已就绪。请通过启动器创建主机或加入游戏。
Мод отключён. Кнопка создания хоста или входа включит его.|The mod is disabled. Hosting or joining will enable it.|模组已禁用。点击创建主机或加入会自动启用它。
Хост создан. Ожидаем друга.|Host created. Waiting for your friend.|主机已创建。等待好友加入。
Друг отключился. Хост ожидает подключения.|Your friend disconnected. The host is waiting for a connection.|好友已断开连接。主机正在等待连接。
Хост временно недоступен. Ожидаем загрузку игрового мира.|The host is temporarily unavailable. Waiting for the game world to load.|主机暂时不可用。等待游戏世界加载。
Хост больше не активен. Загрузите персонажа в мир и создайте хост заново.|The host is no longer active. Load your character into the world and create a host again.|主机已失效。请将角色载入游戏世界并重新创建主机。
Ожидаем загрузку локации и персонажа. Проверяем состояние соединения…|Waiting for the area and character to load. Checking the connection status…|等待区域和角色加载。正在检查连接状态…
Соединение с хостом сейчас не подтверждено. Можно повторить вход.|The connection to the host is currently unconfirmed. You can try joining again.|与主机的连接尚未确认。可以重新尝试加入。
Игра не подтвердила соединение за 45 секунд. Проверьте адрес, общую LAN/VPN и готовность хоста; затем повторите вход.|The game did not confirm the connection within 45 seconds. Check the address, shared LAN/VPN and host readiness, then try joining again.|游戏在 45 秒内未确认连接。请检查地址、共享局域网/VPN 和主机状态，然后重新加入。";
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
