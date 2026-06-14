# Boxwright.Cli

The headless command-line front end (`boxwright`) — a thin shell over `Boxwright.Core`
for scripting, CI, and headless/SSH hosts. See **ADR-0022**.

It has **no QEMU/process/QMP logic of its own**: parsing, presentation, and a composition
root that mirrors the App's service wiring **minus Avalonia**. The CLI and GUI share the same
per-VM folders (`vm.json`) and runtime state (`runtime.json`, ADR-0014), so a VM created in one
can be started, stopped, or adopted by the other — no daemon, no IPC.

## Commands

```
boxwright list [--json]                # all VMs + run status
boxwright info <id|name> [--json]      # a VM's configuration
boxwright create <name> --os <id> [--unattended --user U --password P [--hostname H]]
boxwright create <name> [options]      # blank VM + fresh disk (optionally --iso PATH)
boxwright clone <id|name> <new-name> [--linked]   # full copy, or qcow2 overlay
boxwright start <id|name> [--detach] [--display] [--timeout=SECONDS]
boxwright stop <id|name> [--force] [--timeout=SECONDS]
boxwright display <id|name>            # open remote-viewer against a running VM
boxwright delete <id|name> --yes
boxwright os list [--json]             # OS catalog ids the GUI's one-click flow uses
boxwright snapshot list [--json]|create|restore|delete <id|name> [tag]   # offline qcow2 snapshots
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
- **`clone`** requires the source stopped; `--linked` makes an instant qcow2 overlay backed by
  the source's disks (keep the source in place), otherwise it's a full independent copy.
- **`snapshot restore`** rolls the disk back to a tag (VM stopped — offline qcow2 access).

- **`start`** runs in the foreground by default (Ctrl+C → graceful shutdown). `--detach` leaves
  the VM running for a later `stop`/`display`.
- `snapshot create`/`delete` require the VM to be stopped (offline qcow2 access). Live snapshots
  of a running VM are a separate, GUI-side feature (ADR-0021).

## Configuration

- `BOXWRIGHT_VMS_DIR` — override the VMs root (defaults to the per-OS local app-data path, the
  same location the GUI uses).

## Exit codes

`0` success · `1` a user-level error (bad argument, VM not found, action invalid in the current
state) · `130` cancelled with Ctrl+C.
