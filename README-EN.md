# BL4 LAN · by skykindness

[Русский](README-RU.md) · **English** · **[Download 0.2.6](https://github.com/Skykindness/BL4-LAN/releases/tag/v0.2.6)**

Borderlands 4 co-op on two PCs over LAN/VPN. Both players need the same game build and mod version. Select **RU / EN** at the top of the launcher.

## 1. Set up your network

- Over the internet: install [Radmin VPN](https://www.radmin-vpn.com/). One player creates a network and shares its name and password; the other joins. You should see each other in Radmin.
- On the same home LAN, no VPN is needed. Alternatives: [ZeroTier](https://www.zerotier.com/download/), [Tailscale](https://tailscale.com/download), [Hamachi](https://vpn.net/); not separately tested with this mod.

## 2. Install once on each PC

1. Download **BL4-LAN-v0.2.6.zip**, extract the whole archive and open **BL4-LAN.exe**.
2. Open **Settings**, choose the game folder containing **OakGame**, then choose **Back**.
3. With the game closed, use **Install / update** if that button appears.

**Mod installed means you do not need to reinstall before every game.** For updates, use the new full package on both PCs. Change your nickname in Settings with the game closed.

## 3. Connect

**Both players:** launch the game, choose **Continue**, load your own character and wait for the visible world and HUD. Do not join directly from the main menu.

**Host:** choose **Host a game**, copy your Radmin VPN (or home LAN) address and send it to your friend. After autosaving finishes, choose **Create host**. Wait for the area to reload and **Host ready**.

**Guest:** choose **Join a game**, paste the **host's** address and click **Connect** once. Example format: `26.1.2.3:7777`.

Keep the launcher and Radmin running; the launcher can minimize to the tray. The game's SHiFT Connect button is not used by this mod.

## Manual connection

Both players must first load their characters using **Continue**. Use the English keyboard layout, open the console with the **backtick / tilde key**, enter a command and press **Enter**.

| Command | Action |
|---|---|
| `bl4lan host` | Create a host; the current area reloads. |
| `bl4lan join 26.1.2.3:7777` | Join the host. Replace the example with the **host's address**. Omitting the port uses 7777. |
| `bl4lan join` | Join using the address saved by the launcher. |
| `bl4lan status` | Check connection status in the console/launcher. |

Default keys: **F6 — host**, **F7 — join using the launcher's address**, **F8 — status**. F8 does not reconnect.

**Command not recognized?** Enter `mods` and select the number for **BL4 LAN**. If it says **Disabled**, enter `E` and press Enter. Then enter **`Q` → Enter** to leave the mod menu before retrying the connection command. If the mod is absent, check the installation.

## If it fails

Check the address, matching versions, loaded characters and Host ready status. Allow the game on your LAN/VPN in Windows Firewall; the game port is **7777/UDP**.

After a black screen or disconnect, keep the launcher open for up to two minutes. A report link appears at the bottom. Send one ZIP from each PC: **game folder → BL4-LAN-Data → Reports**. Reports are shared manually; no video or saves are collected.

Version 0.2.6 automatically restores the guest's native teammate card and distance indicator. Verified with two game clients on one PC. Guest big-map icons remain unfixed; the party counter and avatar may be incorrect. Respawning and the full campaign have not been fully verified. This is a pre-release.

[Donate — DonationAlerts](https://www.donationalerts.com/r/skykindness) · [Source and build](https://github.com/Skykindness/BL4-LAN/blob/main/BUILD.md) · [Changelog, Russian](CHANGELOG-RU.md)

