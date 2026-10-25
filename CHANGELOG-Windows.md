# Windows contribution notes

## Proposed Windows support, based on macOS 1.1.1

Base: `551d7aa1d8d747d16be2b482bdc35f1d4e2631af` from Alphainfix/dsh-whale.
The earlier Windows implementation was based on macOS 1.1 (`dd6bec5`). This contribution explicitly retains the 1.1.1 fix for reopening a macOS web app whose process exists but has no window. Swift application sources are unchanged from 1.1.1.

- Organize platform implementations under `mac/` and `windows/`; keep optional DSH integrations and their tests under `shared/`.
- Preserve the root macOS build/install command entry points and root Swift package build entry point.
- Add a native Windows tray app with start/stop/restart, service supervision, failure retry limits, source visibility/order/name settings, browser/Edge launch, npm/npx discovery, custom paths and Windows login startup.
- Add `--open` and `--panel` single-instance actions. The panel starts an offline DSH before opening its authenticated web page. VBS launchers avoid a terminal window.
- Include a blue whale Windows icon at seven resolutions and icon-aware packaging.
- Add optional DSH plugins for DeepSeek balances, OpenRouter account/key allowance and ChatGPT subscription quota; missing credentials and failures remain unavailable rather than becoming fake balances.
- Add a ChatGPT login UI using DSH's native `llm-pi-ai/openai-codex` authorization. This does not implement a new OAuth protocol or model inference backend.
- Add Windows process/authentication tests, shared integration tests, and a macOS/Windows artifact workflow. Tagged builds publish both platform archives after both jobs pass.

## Compatibility and verification boundaries

- Windows uses .NET Framework, Node.js and an existing DSH installation. No DSH installation or upgrade is performed automatically.
- Windows retains the `DSHWhale-Vibe` settings/identity to avoid colliding with independent local ports.
- ChatGPT quota uses an experimental Codex app-server interface and currently Windows-specific CLI discovery. The shared plugin is optional for macOS; no macOS quota compatibility is claimed.
- The original macOS balance endpoint contract is preserved. Clawbot status still means stored login state, not a live connectivity probe.
- Windows native controls and service termination behavior differ from macOS. This is functional porting, not pixel-for-pixel parity.
- Live DeepSeek balance, DSH GPT inference and ChatGPT quota were verified during development; live OpenRouter verification requires configured credentials.
- macOS compilation is checked by the workflow; no local macOS runtime/UI test was performed on this Windows machine. Release artifacts are unsigned/self-signed development builds, not notarized or Authenticode-signed products.
