# Contributing to Boxwright

Thanks for your interest! Boxwright is early-stage, so the most valuable
contributions right now are focused PRs, good bug reports (with your VM's JSON
config and the per-VM QEMU log), and discussion of the architecture.

Please read [`CLAUDE.md`](CLAUDE.md) first — it states the project's
**non-negotiable directives**. They are not arbitrary; each maps to an
[ADR](docs/adr/README.md). Working against them wastes everyone's time.

## Quick start

```bash
dotnet restore
dotnet build
dotnet test
dotnet format            # must produce no diff before you commit
dotnet run --project src/Boxwright.App
```

Running VMs in development needs QEMU on `PATH` (Linux: distro package; macOS:
Homebrew; Windows: a build from qemu.weilnetz.de).

## The ground rules (short version)

1. **Direct QEMU control via QMP. No `libvirt`.** (ADR-0001)
2. **QEMU is a subprocess — never linked/vendored.** Keeps our MIT license clean
   against QEMU's GPL. (ADR-0005)
3. **Cross-platform parity.** Features work on Windows, macOS, and Linux — or are
   capability-gated and degrade gracefully with a clear message. No silently
   Linux-only code.
4. **Never hardcode the accelerator.** Auto-detect kvm/hvf/whpx/tcg. (ADR-0003)
5. **Respect the layering.** `Boxwright.Qmp` depends on nothing of ours and no
   Avalonia; `Core` may use `Qmp` but not Avalonia; `App` holds no
   QEMU/process/QMP logic. (CLAUDE.md §4)
6. **`System.Text.Json` only.** No Newtonsoft.
7. **Be honest in user-facing copy** about capability and performance.

## Making a change

1. Open an issue first for anything non-trivial, so we can agree on the approach.
2. Branch from `main` (`feat/…`, `fix/…`, `docs/…`).
3. Follow [`docs/conventions.md`](docs/conventions.md) (style, commits, tests).
4. If your change touches a directive/decision, **add or update an ADR**
   (`docs/adr/`) rather than diverging silently.
5. Add tests. The QMP client must be testable **without a live QEMU** (drive it
   against a loopback fake).

## Commits & PRs

- **Conventional Commits**: `feat(qmp): …`, `fix(accel): …`, `docs(adr): …`.
- **PR checklist:**
  - [ ] Builds clean, no new warnings
  - [ ] `dotnet format` is a no-op
  - [ ] Tests added/updated where practical
  - [ ] Cross-platform implications considered; OS-specific bits gated & labelled
  - [ ] ADR added/updated if a directive is affected
  - [ ] User-facing strings honest about capability/performance
- Don't reformat unrelated code in a feature PR. PRs are squash-merged; the title
  must be a valid Conventional Commit.

## Reporting bugs

Include: your OS and arch, the chosen accelerator (shown in the UI), the **VM's
JSON config**, and the **per-VM QEMU log**. A reproducer beats a description.

## Code of conduct

Be respectful and constructive. We want a welcoming project. Harassment or
hostility isn't tolerated.
