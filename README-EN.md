# BL4 LAN · by skykindness

**0.2.13** · [Русский](README-RU.md) · [简体中文](README-ZH-CN.md)

Borderlands 4 co-op over LAN or VPN. The launcher lets you host, join by IP and change your nickname. Version 0.2.13 restores quests and datapads when a newly hosted session discovers a safehouse for the first time. [Download from Releases](https://github.com/Skykindness/BL4-LAN/releases) · [How we found and fixed the bug](https://github.com/Skykindness/BL4-LAN/blob/main/docs/SAFEHOUSE-FIX-EN.md)

## How to play

1. Close the game on both PCs and extract the **entire** `BL4-LAN-v0.2.13.zip`. Run `BL4-LAN.exe`, select the game folder containing `OakGame` and click **Install / update**. Install once and whenever you update.
2. Use matching game and mod builds. Join the same local network or Radmin VPN network. On a VPN, use the host's address on that network.
3. Both players select **Continue** and load their characters into the world. The host selects **Create host** and waits for the area to reload. The guest selects **Connect** and enters the host address, for example `26.1.2.3:7777`. Keep the launcher open.

Choose **RU / EN / 简体中文** in the launcher. You can change your nickname in launcher Settings. Restart the game to apply it.

## Hotkeys and commands

**F6** hosts, **F7** joins the saved address, and **F8** shows status. If the text-based `mods` menu is open in the console, exit it with **Q → Enter**.

| Command | Action |
| --- | --- |
| `bl4lan host` | Host after loading your character |
| `bl4lan join IP:7777` | Join the host by address |
| `bl4lan status` | Show the session status |

## Compatibility and troubleshooting

- Safehouse recovery is enabled only on the tested game build, `Oak2-Win64-4906557`.
- To set personal difficulty, select **Esc → Session Settings → Difficulty → Apply**. The guest's choice is retained when joining. Rare loot bonuses and scaling for 3–4 players are still unverified.
- Party avatars and count, the stock “+”, through-wall names and some HUD states have limitations. The full campaign and 3–4 player sessions still need dedicated testing.
- If you hit an issue, collect the ZIP report from both PCs under `BL4-LAN-Data/Reports` in the game folder. Reports stay local until shared and contain no save files.

[Support the author](https://www.donationalerts.com/r/skykindness)
