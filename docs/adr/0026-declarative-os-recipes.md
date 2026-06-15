# ADR-0026: Declarative OS recipes (community-contributed OS definitions)

- **Status:** Proposed
- **Date:** 2026-06-15

## Context
The v1.0 roadmap line "Plugin/recipe API for community-contributed OS definitions" wants people to add a
distro — and how to install it unattended — **without recompiling Boxwright**. Today an OS lives in two
places: a catalog entry (`OsCatalogEntry`: download URL, SHA-256, family, recommended specs — already
served from a **remote, community-maintainable manifest**, ADR-0020) and a hand-written C#
`IUnattendedInstaller` per family (Ubuntu autoinstall, Debian preseed, Fedora kickstart, Windows
Autounattend). Adding a new distro's *unattended* install therefore needs a code change and a release.

The fork is **declarative recipes (data) vs. code plugins (assemblies)**. Code plugins
(`AssemblyLoadContext`/MEF) were rejected: loading arbitrary community C# is a supply-chain and sandboxing
liability, a cross-platform-parity risk, a potential GPL-contamination vector, and exactly the scope creep
`roadmap.md` warns against. The directives (no daemon, MIT, cross-platform parity, no admin) point at a
**data-driven** extension instead.

## Decision (proposed)
- **A recipe is JSON, not code.** It generalizes the per-family installers into a data-driven engine. A
  recipe carries the catalog entry's fields **plus** how to install:
  - `installKind`: one of `iso-autoinstall`, `cloud-image`, `preseed-initrd`, `kickstart-initrd`,
    `windows-autounattend` — the mechanisms we already implement.
  - media locations inside the ISO (`kernelPath`, `initrdPath`), an `appendTemplate` (kernel cmdline), and
    a `seedTemplate` (the cloud-init/preseed/kickstart/Autounattend document) with placeholders
    (`{username}`, `{passwordHash}`, `{hostname}`, …) filled from the existing `UnattendedAnswers`.
  - SHA-256 stays **mandatory** for every download (unchanged from ADR-0010).
- **Two recipe sources, same shape:** the existing remote catalog (community PRs to the manifest repo) and
  a **local folder** (`~/.config/Boxwright/recipes/*.json`, per-OS app-data) the user drops files into.
  Both feed the same `IOsCatalogSource` pipeline (remote → cache → bundled → local). Adding an OS becomes
  "add a file," no recompile — the actual "plugin" extension point.
- **A generic recipe-driven installer.** `UnattendedInstallerResolver` selects a `RecipeInstaller` that
  reads the recipe's `installKind` + templates instead of branching in C#. The four existing installers
  are **re-expressed as built-in recipes** (proving the schema covers them) and can then be deleted or
  kept as the bundled set.
- **Trust model (documented, explicit).** A recipe never runs host-side code: its only effects are QEMU
  arguments and a seed document that runs **inside the guest** (which any installer already does). The
  risks are a malicious download URL or a hostile seed — mitigated by mandatory SHA-256 and by recipes
  being *explicitly user-installed or in the curated manifest*. We state plainly: install recipes only
  from sources you trust, exactly as you would an OS image.

## Phasing
1. **Schema + proof:** define the recipe JSON (versioned, like the catalog) and re-express the four
   existing installers as recipes; unit-test that each produces the same `UnattendedInstallPlan` /
   command line as today (a behavior-preserving refactor).
2. **Local recipe folder:** load `recipes/*.json` into the catalog pipeline; CLI `boxwright recipe
   list|validate <file>` and surfacing in `os list`.
3. **Generic installer:** `RecipeInstaller` drives install from the recipe; retire (or keep as bundled)
   the hardcoded family installers.

## Consequences
- **Easier:** the community adds a distro by editing data, not C#; the install layer stops being four
  bespoke classes and becomes one engine + a recipe set; offline/local recipes work the same as remote.
- **Harder / accepted:** the recipe schema must be expressive enough for real installers without becoming
  a Turing tarpit — Phase 1 (re-expressing the existing four) is the guardrail; anything they can't model
  declaratively stays a built-in. Versioning/compat of the recipe schema is a long-term maintenance load.
  Windows Autounattend is the most template-heavy and may need the richest placeholder set.

## Alternatives considered
- **Code plugins (load C# assemblies).** Rejected — security/supply-chain, GPL/license hygiene
  (ADR-0005), cross-platform fragility, and scope creep. A constrained out-of-process script hook was also
  rejected: it reintroduces a privilege/cross-platform surface for little gain over declarative recipes.
- **Leave the catalog as-is and keep adding C# installers.** Rejected: it's the status quo that the
  roadmap item exists to remove — every new distro is a code change + release.
