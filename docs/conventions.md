# Conventions

How we write code, commits, and pull requests. `.editorconfig` is the
machine-enforced source of truth for formatting; this document covers the
human-judgment parts. When in doubt, match the surrounding code.

---

## Code style (C#)

- **`.editorconfig` wins.** Run `dotnet format` before committing; CI rejects
  diffs.
- **Nullable reference types are on** everywhere. No `#nullable disable`. Model
  absence with `?`, not with lying non-null types.
- **File-scoped namespaces** (`namespace Boxwright.Core;`).
- **`var`** when the type is obvious from the right-hand side; explicit type when
  it aids readability.
- **`async`/`await` end to end.** Async methods end in `Async`. Accept a
  `CancellationToken` on anything I/O-bound (sockets, processes, file I/O).
  Never `.Result` / `.Wait()` / `async void` (except event handlers).
- **Immutability by default.** Prefer `record` / `readonly` for models and
  config types. `VmConfig` and friends are value-like.
- **Dependency injection**, no service locators or statics-as-globals. Pass
  collaborators in.
- **`System.Text.Json` only.** No Newtonsoft.
- **One public type per file**, file name matches the type.
- **Comments explain *why*, not *what*.** XML doc comments on public API in
  `Boxwright.Qmp` (it ships to NuGet).

## Layering rules (enforced by review)

- `Boxwright.Qmp` references **none** of our other projects, and no Avalonia.
- `Boxwright.Core` may reference `Qmp`; never Avalonia.
- `Boxwright.App` references `Core`/`Qmp`; contains **no** QEMU/process/QMP
  logic — views and viewmodels only.
- See `CLAUDE.md` §4 for the dependency diagram. A PR that crosses these lines
  needs an ADR.

## MVVM (App layer)

- Use `CommunityToolkit.Mvvm` (source generators for `[ObservableProperty]` /
  `[RelayCommand]`).
- **No business logic in code-behind or views.** Views bind to viewmodels.
- ViewModels orchestrate `Core`; they do not build QEMU command lines or shell
  out to `qemu-img` themselves.

## Testing

- **xUnit.** New QMP commands and Core logic ship with tests.
- The **QMP client is tested without a live QEMU** — drive it against an
  in-memory/loopback fake that speaks the protocol. This keeps tests fast,
  deterministic, and CI-friendly across OSes.
- Test behavior and edge cases (malformed JSON, error replies, events arriving
  mid-command, disconnect), not implementation details.
- `CommandLineBuilder` is pure (config → args) and should be exhaustively unit
  tested — it is the riskiest correctness surface.

---

## Commits

Use **Conventional Commits**:

```
<type>(<optional scope>): <short imperative summary>

<optional body explaining why>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `build`, `ci`, `chore`,
`perf`. Scopes are usually the project: `qmp`, `core`, `app`, or an area like
`accel`, `disk`, `display`.

Examples:
```
feat(qmp): correlate replies by id and expose event stream
fix(accel): fall back to tcg when /dev/kvm is unreadable
docs(adr): add ADR-0004 on display via remote-viewer
```

Keep commits focused. Don't mix a refactor with a feature.

## Branches & PRs

- Branch from `main`: `feat/os-catalog`, `fix/whpx-irqchip`, `docs/roadmap`.
- **PR checklist** (also in `CLAUDE.md` §7):
  - [ ] Builds clean, no new warnings.
  - [ ] `dotnet format` is a no-op.
  - [ ] Tests added/updated where practical.
  - [ ] Cross-platform implications considered; OS-specific features gated and
        labelled.
  - [ ] Affected ADR added/updated if a directive is touched.
  - [ ] User-facing strings are honest about capability/performance.
- Don't reformat unrelated code in a feature PR.
- Squash-merge; the PR title is the squashed commit and should be a valid
  Conventional Commit.

## ADRs

Significant decisions get a record in `docs/adr/`. See `docs/adr/README.md` for
the (short) format and numbering. If your change contradicts an existing ADR,
write a new one that supersedes it — don't diverge silently.
