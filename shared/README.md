# Shared DSH integration

`plugins/` contains the optional authenticated `/api/model-balance` provider, ChatGPT OAuth UI and model diagnostics. The desktop apps continue consuming the same balance response contract. API keys and OAuth grants remain owned by DSH.

`tests/` tests balance normalization, missing/expired authorization, quota windows and login interactions without live credentials.

The plugin integration was validated with DSH 0.2.0-rc.2 on Windows. ChatGPT quota lookup currently discovers the Windows Codex installation and uses an experimental app-server interface; macOS CLI discovery is not implemented here. These plugins are optional and are not automatically installed into an existing macOS setup.
