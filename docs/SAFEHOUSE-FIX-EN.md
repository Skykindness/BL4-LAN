# Why safehouse datapads disappeared after F6

[Русский](SAFEHOUSE-FIX-RU.md) · [简体中文](SAFEHOUSE-FIX-ZH-CN.md)

After starting a BL4 LAN host, discovering an unopened safehouse could fail to start the “Find the datapad” objective. The datapad was missing too. We reproduced it with only the host in the session. Datapads could persist in areas already discovered in solo play, so we used a save with undiscovered safehouses for the gameplay check.

## How we found it

In one game process we compared three loads for the same safehouse: Continue, F6 to host, and Continue again after returning to the main menu. The quest remained `available` each time. The handler’s binding to the discovery event source went from present to empty and then back to present.

The trace showed the order of events. With Continue, the source was ready by the time the handler registered. With F6, the handler registered first. In one trace the source appeared 562 ms later, and the handler’s reference stayed empty. Discovering the area therefore failed to start the quest that makes the datapad appear.

We also tried loading the save through the stock LAN path. It preserved the bindings, but the test did not open a UDP port for direct connections. The release keeps the working F6 network path and restores the bindings after it.

## What changed in 0.2.13

After creating a host, the mod waits for the new world and the host’s discovery event source. It calls the game’s own binding routine once for eligible safehouse quests with missing bindings. The game then starts the quest and shows the datapad when players discover the safehouse. Existing bindings and the initial registration order are preserved.

On the tested `Oak2-Win64-4906557` build, the trial restored all 13 missing bindings. All 13 quests still showed `available` immediately afterward. We verified the quest and datapads with a solo host and in a fresh host-and-guest session after restoring the save. The packaged 0.2.13 log confirmed 13 of 13 bindings again on an ordinary F6 start. Recovery is disabled on other game builds.

[Recovery code](../src/bl4_lan/safehouses.py)
