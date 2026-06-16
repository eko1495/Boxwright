# Boxwright.Cli

The headless command-line front end (`boxwright`) — a thin shell over `Boxwright.Core`
for scripting, CI, and headless/SSH hosts. See **ADR-0022**.

It has **no QEMU/process/QMP logic of its own**: parsing, presentation, and a composition
root that mirrors the App's service wiring **minus Avalonia**. The CLI and GUI share the same
per-VM folders (`vm.json`) and runtime state (`runtime.json`, ADR-0014), so a VM created in one
can be started, stopped, or adopted by the other — no daemon, no IPC.

## Commands

```
boxwright list [--json]                # all VMs + run status + on-disk size
boxwright info <id|name> [--json]      # a VM's configuration + per-disk usage
boxwright set <id|name> [--name N] [--memory MiB] [--cpus N] [--firmware bios|uefi] [--os-type linux|windows|macos] [--arch A] [--display-protocol spice|vnc] [--gl true|false] [--audio true|false] [--boot-menu true|false]   # edit a VM's settings
boxwright create <name> --os <id> [--unattended --user U --password P [--hostname H]]
boxwright create <name> [options]      # blank VM + fresh disk (optionally --iso PATH)
boxwright clone <id|name> <new-name> [--linked]   # full copy, or qcow2 overlay
boxwright template list [--json]|create <id|name>|new <template> <name> [--full]|delete <template> --yes
boxwright start <id|name> [--detach] [--display] [--timeout=SECONDS]
boxwright stop <id|name> [--force] [--timeout=SECONDS]
boxwright display <id|name>            # open remote-viewer against a running VM
boxwright delete <id|name> --yes
boxwright os list [--json]             # OS catalog ids the GUI's one-click flow uses
boxwright recipe dir|list [--json]|validate <file>   # local OS recipes that extend the catalog
boxwright snapshot list [--json]|create|restore|delete <id|name> [tag]   # offline qcow2 snapshots
boxwright check <id|name> [--json]     # check disks for corruption (qemu-img check)
boxwright usb list [--json]            # host USB devices (where the OS supports enumeration)
boxwright usb show|add|remove <id|name> <vendor:product> [--description=TEXT] [--now]   # USB passthrough
boxwright net show <id|name> [--json]                                  # network mode
boxwright net set <id|name> <user|bridge|tap> [--bridge=NAME] [--device=NAME]   # bridged/TAP (Linux)
```

VMs are addressed by **id, exact name, or a unique id prefix**. Options are `--flag`, `--key value`,
or `--key=value`.

- **`create --os <id>`** builds from a catalog OS (`os list` shows the ids), running the same Core
  orchestration as the GUI: download + verify the image, prep the disk, and seed it. Resource sizes
  default from the catalog's recommended spec; `--memory`/`--cpus`/`--disk`/`--firmware` override them.
  - An **installer ISO** boots interactively by default; add `--unattended --user --password` for a
    hands-free install (where the OS family supports it — Ubuntu/Debian/Fedora).
  - A **cloud image** is always seeded, so `--user` and `--password` are required (the seed is the
    only login the guest gets).
  - Windows (which needs a user-supplied ISO + virtio + Autounattend) stays **GUI-only** for now.

- **`--json`** on the read commands (`list`, `info`, `os list`, `snapshot list`) emits
  camelCase JSON for `jq`-friendly scripting instead of the human table.
- **Disk usage:** `list` shows each VM's on-disk footprint (a `DISK` column) and `info` breaks it down
  per disk (actual on-disk vs virtual capacity) via `qemu-img info`. Best-effort — if `qemu-img` is
  missing or a disk is unreadable the size shows as `—` (JSON: `null`) rather than failing the command.
- **`clone`** requires the source stopped; `--linked` makes an instant qcow2 overlay backed by
  the source's disks (keep the source in place), otherwise it's a full independent copy.
- **`snapshot`** create/restore/delete span **every** qcow2 disk of the VM, so a multi-disk VM stays
  consistent (a tag is only restorable when present on all disks). `restore` rolls back to a tag (VM
  stopped — offline qcow2 access).
- **`set`** edits a VM's boot-time settings (the CLI counterpart to the GUI settings panel) — only the
  options you pass change; everything else (id, disks, networking) is preserved. Settings take effect on
  the next launch, so editing a running VM is allowed but flagged. Renames are rejected if the name is
  taken by another VM.
- **`check`** runs `qemu-img check` on every qcow2 disk (VM stopped — a live disk reads as corrupt) and
  reports corruptions/leaks per disk. Exits non-zero when any disk is corrupted or its check fails, so it
  works as a health gate in scripts. Leaks (wasted-but-safe clusters) don't fail the check.

- **`start`** runs in the foreground by default (Ctrl+C → graceful shutdown). `--detach` leaves
  the VM running for a later `stop`/`display`.
- **`usb`** passes a host USB device through by **vendor:product** (e.g. `046d:c52b`), which is stable
  across replug. `add`/`remove` edit the config (next boot); add `--now` to also hot-plug/unplug a
  running VM live (QMP `device_add`/`device_del`). `usb list` needs host enumeration (Linux sysfs today;
  on Windows/macOS it reports unsupported — add by vendor:product from Device Manager / System
  Information). See ADR-0023.
- **`recipe`** — drop an OS **catalog document** (`*.json`, same shape as the bundled catalog) into the
  recipes folder (`recipe dir` prints it) and that OS appears in `os list` and the GUI picker, no
  recompile. `recipe list`/`validate` help author them. Local recipes layer over the remote/bundled
  catalog (a recipe can add or, by id, replace an entry). A recipe can also carry an optional
  **`unattended`** block (`kernelPath`, `initrdPaths`, `seedTemplate`, `append`) in two kinds so a distro
  installs hands-free with no C#: `initrd-inject` (Debian/Fedora-style — a templated preseed/kickstart named
  by `seedFileName` is injected into the initrd) and `cloud-init` (Ubuntu-autoinstall-style — the templated
  user-data is written as a NoCloud CIDATA seed disk, initrd untouched). Templates fill
  `{username}`/`{passwordHash}`/`{hostname}`/`{isoLabel}`/… See ADR-0026.
- **`template`** turns a stopped VM into a reusable frozen base (`create`) and stamps out instances from
  it (`new`, linked by default — instant — or `--full`). A template can't be booted; each instance is a
  fresh concrete VM with its own id and MAC. See ADR-0025.
- **`delete`** refuses (even with `--yes`) when the VM backs a **linked clone** — deleting it would corrupt
  the clone — and lists the dependents so you can remove them (or `--full`-clone them off) first.
- **`net`** sets a VM's network mode: `user` (SLIRP NAT, the default), `bridge` (join a host bridge via
  `qemu-bridge-helper`), or `tap` (a pre-created TAP device). Bridge/TAP are **Linux-only** (a launch on
  another host fails with a clear message) and need the host bridge/TAP + setuid helper set up yourself —
  Boxwright never runs as root. See ADR-0024.
- `snapshot create`/`delete` require the VM to be stopped (offline qcow2 access). Live snapshots
  of a running VM are a separate, GUI-side feature (ADR-0021).

## Configuration

- `BOXWRIGHT_VMS_DIR` — override the VMs root (defaults to the per-OS local app-data path, the
  same location the GUI uses).

## Exit codes

`0` success · `1` a user-level error (bad argument, VM not found, action invalid in the current
state) · `130` cancelled with Ctrl+C.
