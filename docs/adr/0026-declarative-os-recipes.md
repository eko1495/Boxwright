# ADR-0026: Declarative OS recipes (community-contributed OS definitions)

- **Status:** Accepted (phase 1 — sourcing — and phase 2a — the initrd-inject install engine — implemented; the cloud-init recipe kind remains)
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
1. **Recipe sourcing / catalog extension — DONE.** A recipe is an OS **catalog document** (the same
   versioned `OsCatalogDocument` the bundled list uses, so existing entries are copy-paste recipes).
   `LocalRecipeCatalogSource` loads `recipes/*.json` from a local folder; `CompositeOsCatalogSource`
   merges sources (remote → cache → bundled, then local recipes layered on top — local wins by id, so a
   recipe both adds new OSes and can pin/replace one). A malformed recipe is skipped, never breaking the
   catalog. CLI `boxwright recipe dir|list|validate`; recipes surface in `os list` and the GUI picker.
   **At this stage a recipe reuses the existing per-family installer** (matched by `osFamily`), so it can
   add a new release of a known family with full unattended support, or any OS for interactive install —
   without a recompile. Its install-specific fields (below) are not yet consumed.
2. **Recipe-driven install engine.**
   - **2a — DONE (initrd-inject).** An optional `OsCatalogEntry.Unattended` recipe (`UnattendedRecipe`)
     carries `kind`, `kernelPath`, `initrdPaths`, `seedFileName`, a `seedTemplate`, and an `append`
     template. `RecipeInstaller` (kind `initrd-inject`) copies the kernel/initrd out of the ISO, injects
     the substituted seed file into the initrd (`InitrdFileInjector`), and boots the substituted kernel
     command line — the preseed/kickstart mechanism, now data-driven. Templates fill `{username}`,
     `{password}`, `{passwordHash}`, `{hostname}`, `{locale}`, `{timezone}`, `{keyboard}`, `{isoLabel}`
     (`RecipeTemplate`, password hashed once per install). `CatalogVmInstaller` routes to it whenever an
     entry has an `unattended` recipe, otherwise the built-in per-family installer. So a community recipe
     can add a Debian/Fedora-style distro's **hands-free** install with no C#.
   - **2b — pending (cloud-init kind).** A `cloud-init` kind that writes the templated `seedTemplate` as a
     NoCloud CIDATA seed disk (generalizing Ubuntu autoinstall) — needs the FAT/CIDATA writer extracted
     from `CloudInitSeedGenerator` to accept an arbitrary user-data string. The four built-in installers
     can then be re-expressed as recipes to prove full coverage.

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
