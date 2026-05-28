## What & why

<!-- What does this change and why? Link the issue it resolves. -->

Closes #

## Checklist

- [ ] Builds clean, no new warnings
- [ ] `dotnet format` produces no diff
- [ ] Tests added/updated where practical (QMP testable without a live QEMU)
- [ ] Cross-platform implications considered; any OS-specific behavior is gated
      and clearly labelled
- [ ] An ADR was added/updated if this touches a Prime Directive or decision
- [ ] User-facing strings are honest about capability and performance
- [ ] Respects the layering: `Qmp` has no UI/Core deps; `Core` has no Avalonia;
      `App` has no QEMU/process/QMP logic
- [ ] Conventional Commit title (e.g. `feat(core): …`)
