# BL4 LAN · by skykindness

**0.2.9** · [Русский](README-RU.md)

LAN / VPN co-op. Adds automatic recovery of an empty teammate nameplate; a local two-client test displayed the host name above the character on the guest. The owner confirmed map markers, teleport and live HP/shields on two PCs in 0.2.7.

## Play together

1. Close the game and extract the complete archive on both PCs. In BL4-LAN.exe, select the game folder → Install / update. Installation is needed once; the launcher detects a ready installation.
2. Use the same home LAN or join the same Radmin VPN network. Both PCs need matching game and mod builds. For VPN, use the host address from that VPN.
3. Both players choose Continue and load their own characters into the world. The host selects Create host and waits for the reload. The guest selects Join and enters the host address. Keep the launcher open.

## Personal difficulty

In game: **Esc → Session Settings → Difficulty → Apply**. Each player chooses their own setting. A choice made before joining is automatically sent to the server once. In the local pair, the server kept the host on Hard and received the guest's Easy choice without another post-join adjustment. UVHM / True Mode are separate. Exact rare-loot bonuses and scaling with 3–4 players have not been confirmed.

## Optional console commands

Open the game console. If the `mods` menu is active, exit it with **Q → Enter** first. Then:

- `bl4lan host` — host after loading your character;
- `bl4lan join HOST_IP:7777` — join;
- `bl4lan status` — inspect the connection.

An ordinary connection attempt is enough for diagnostics. ZIP reports are saved automatically under the game folder → **BL4-LAN-Data → Reports**. Share the latest report from both PCs. Scheduled snapshots run 2, 10 and 30 seconds after a ready party is observed. No video, saves, memory dumps, packet capture or automatic upload.

Join hangs, party avatars and counts remain under investigation. Through-wall name visibility and the new recovery on separate PCs still need validation. The stock “+” does not yet join a LAN host. Client resolution/window-mode changes were tested without crashes; the host was not tested.

For local tests, do not add `-NoMovies`: an owned comparison reproduced a lingering CutsceneIntro input lock with that flag. The updated two-client helper omits it. Version 0.2.9 was checked on two clients on one PC; this does not establish a fault-free full campaign.

Select **RU / EN** in the launcher. [Donate](https://www.donationalerts.com/r/skykindness).
