# BL4 LAN · by skykindness

[Русский](README-RU.md) · **English** · **[Download 0.2.5](https://github.com/Skykindness/BL4-LAN/releases/tag/v0.2.5)**

Borderlands 4 co-op on two PCs over LAN/VPN. Both players need the same game build and mod version. Select **RU / EN** at the top of the launcher.

## 1. Set up your network

- Over the internet: install [Radmin VPN](https://www.radmin-vpn.com/). One player creates a network and shares its name and password; the other joins. You should see each other in Radmin.
- On the same home LAN, no VPN is needed. Alternatives: [ZeroTier](https://www.zerotier.com/download/), [Tailscale](https://tailscale.com/download), [Hamachi](https://vpn.net/); not separately tested with this mod.

## 2. Install once on each PC

1. Download **BL4-LAN-v0.2.5.zip**, extract the whole archive and open **BL4-LAN.exe**.
2. Open **Settings**, choose the game folder containing **OakGame**, then choose **Back**.
3. With the game closed, use **Install / update** if that button appears.

**Mod installed means you do not need to reinstall before every game.** For updates, use the new full package on both PCs. Change your nickname in Settings with the game closed.

## 3. Connect

**Both players:** launch the game, choose **Continue**, load your own character and wait for the visible world and HUD. Do not join directly from the main menu.

**Host:** choose **Host a game**, copy your Radmin VPN (or home LAN) address and send it to your friend. After autosaving finishes, choose **Create host**. Wait for the area to reload and **Host ready**.

**Guest:** choose **Join a game**, paste the **host's** address and click **Connect** once. Example format: `26.1.2.3:7777`.

Keep the launcher and Radmin running; the launcher can minimize to the tray. The game's SHiFT Connect button is not used by this mod.

## If it fails

Check the address, matching versions, loaded characters and Host ready status. Allow the game on your LAN/VPN in Windows Firewall; the game port is **7777/UDP**.

After a black screen or disconnect, keep the launcher open for up to two minutes. A report link appears at the bottom. Send one ZIP from each PC: **game folder → BL4-LAN-Data → Reports**. Reports are shared manually; no video or saves are collected.

A successful first-attempt connection between two PCs has been reported for 0.2.5. Long-session stability, full campaign play and teammate highlighting have not been fully verified.

[Donate — DonationAlerts](https://www.donationalerts.com/r/skykindness) · [Source and build](https://github.com/Skykindness/BL4-LAN/blob/main/BUILD.md) · [Changelog, Russian](CHANGELOG-RU.md)

