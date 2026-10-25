# Sources and version pins

- Co-op Anything playbook: https://github.com/cranebeaker/coop-anything — local instructions used for triage, research, native transport and test isolation. No game-specific BL4 implementation was found in the supplied export.
- Official BL4 SDK release v0.3: https://github.com/bl-sdk/oak2-mod-manager/releases/tag/v0.3
- Official SDK installation guide: https://bl-sdk.github.io/oak2-mod-db/
- Archive URL: https://github.com/bl-sdk/oak2-mod-manager/releases/download/v0.3/oak2-sdk.zip
- Archive SHA-256, checked against GitHub release metadata: `602675446abed184169fa158be3c8bc81777a71203581e4a248eca8a3d00b5c7`
- SDK sources: https://github.com/bl-sdk/oak2-mod-manager and https://github.com/bl-sdk/mods_base
- Official game FAQ, online co-op/offline single-player description: https://borderlands.2k.com/es-MX/borderlands-4/faq/
- Official game troubleshooting: https://support.borderlands.com/hc/en-us/articles/44493923901971-Borderlands-4-PC-Troubleshooting-and-Optimization-Guide
- Unreal Engine PSO configuration: https://dev.epicgames.com/documentation/unreal-engine/pso-precaching-for-unreal-engine

The local executable investigated had SHA-256 `ba75f7c2874b16efb5536a5c0cc28918a03efd3e55d790d2e4db57de0c3a353a`. A fresh crash context identified `Oak2-Win64-4906557`, UE 5.5.4. This is a compatibility observation, not proof of working co-op on that build or any other build.

No original game files, save files, private crash dumps or account credentials are redistributed.
