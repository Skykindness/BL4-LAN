## Русский

**Обновите мод на обоих ПК при закрытой игре. Скачайте BL4-LAN-v0.2.9.zip и распакуйте целиком.** Устанавливать перед каждой сессией не нужно.

- Автоматическое восстановление пустого ника над напарником через штатный механизм игры.
- Сложность, выбранная гостем до подключения, однократно передаётся серверу. У хоста остаётся собственная настройка. Выбор: Esc → Настройки сессии → Уровень сложности → Применить.
- Исправлен таймер первого запроса после ожидания; дополнена ограниченная диагностика зависаний.
- Исправлен локальный помощник запуска двух клиентов: удалён `-NoMovies`, с которым воспроизводилась блокировка WASD/обзора после «Продолжить».
- Сохранены RU/EN, смена ника, донат и установка один раз.

**Вход:** общая LAN или Radmin VPN → оба «Продолжить» и загрузить персонажа → хост «Создать хост», дождаться перезагрузки → гость подключается по адресу хоста один раз. Ручная команда: `bl4lan join АДРЕС_ХОСТА:7777`. Из меню `mods` сначала выйдите через Q и Enter.

**Проверено:** одна рабочая LAN-пара на одном ПК, первая попытка входа, отсутствие блокировки ввода и передача личной сложности серверу. 359 Python-проверок, 16 наборов C# и 4 проверки установки нового модуля. Совместимость исследовалась на Oak2-Win64-4906557 / UE5.5.4.

**Осталось:** верхние аватары и счётчик группы, штатный «+», видимость ника через сплошные стены, отдельные состояния HUD и прежние зависания входа требуют дальнейшей проверки. Масштабирование сложности/награды на 3–4 игроков и полная кампания не подтверждены. Отчёты: папка игры → BL4-LAN-Data → Reports; при проблеме нужен ZIP с обеих сторон. Автоматической отправки нет.

[Поддержать автора](https://www.donationalerts.com/r/skykindness)

## English

**Close the game and update both PCs. Download BL4-LAN-v0.2.9.zip and extract the entire archive.** No reinstall is needed before every session.

- Automatic recovery of an empty teammate nameplate using the game's native mechanism.
- The guest's pre-join personal difficulty is sent to the server once; the host keeps their own choice. Use Esc → Session Settings → Difficulty → Apply.
- Corrected request timing after waiting; added bounded join diagnostics.
- The local two-client helper no longer uses `-NoMovies`, which reproduced a lingering movement/look lock after Continue.
- RU/EN, nickname editing, donation and one-time installation are retained.

**Join:** shared LAN or Radmin VPN → both Continue into their own world → host Create host and wait for the reload → guest joins the host IP once. Console: `bl4lan join HOST_IP:7777`. Exit `mods` with Q and Enter first.

**Checked:** one healthy two-client LAN pair on one PC, first-attempt join, unblocked input and server receipt of personal difficulty; 359 Python checks, 16 C# suites and 4 new-module installation checks. Investigated build: Oak2-Win64-4906557 / UE5.5.4.

**Remaining limits:** upper party avatars/count, stock “+”, through-wall name visibility, some HUD states and previous join hangs need further work. Three/four-player scaling, loot bonuses and a full campaign have not been validated. Reports: game folder → BL4-LAN-Data → Reports; share a ZIP from each side. Nothing is uploaded automatically.

[Donate](https://www.donationalerts.com/r/skykindness)
