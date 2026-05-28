# Boxwright.App

The Avalonia desktop GUI (MVVM via CommunityToolkit.Mvvm).

References `Boxwright.Core` (and transitively `Boxwright.Qmp`). **Contains no
QEMU/process/QMP/business logic** — if a viewmodel is building a QEMU command
line or shelling out to `qemu-img`, that logic belongs in `Core`. Views bind to
viewmodels; no logic in code-behind. (See [`CLAUDE.md` §4](../../CLAUDE.md) and
[ADR-0002](../../docs/adr/0002-avalonia-ui.md).)

## MVP screens

- VM list with lifecycle actions (create / start / stop / pause / reset / delete)
- New-VM flow with sensible defaults
- Per-VM settings panel
- "Open display" → launches the SPICE viewer

## Scaffolding note

This stub expects the Avalonia packages, `App.axaml`, `Program.cs`, and an
`app.manifest`. The quickest way to generate a correct Avalonia skeleton is the
Avalonia templates (`dotnet new install Avalonia.Templates` then
`dotnet new avalonia.mvvm`), after which you can fold the generated files into
this project and wire it to `Boxwright.Core`.
