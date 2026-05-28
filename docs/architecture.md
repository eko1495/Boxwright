# Architecture

This document describes how Boxwright is put together and **why**. It is the
reference for anyone (human or AI) doing backend work. The short, binding rules
live in `CLAUDE.md` §2; this file explains them.

---

## 1. System overview

Boxwright is a thin, friendly orchestration layer over stock QEMU. It does not
reimplement virtualization — it generates the right command line, launches a
`qemu-system-<arch>` process per VM, and controls that process at runtime over
the QEMU Machine Protocol (QMP).

```
┌──────────────────────────────────────────────────────────────┐
│ Boxwright.App  (Avalonia, MVVM)                                │
│   Views ⇄ ViewModels        no QEMU/process logic here         │
└───────────────┬────────────────────────────────────────────────┘
                │ calls
┌───────────────▼────────────────────────────────────────────────┐
│ Boxwright.Core  (orchestration / domain)                        │
│   • Vm model + VmConfig (JSON)                                  │
│   • CommandLineBuilder  (VmConfig → qemu args)                  │
│   • AcceleratorDetector (kvm/hvf/whpx/tcg)                      │
│   • QemuProcess        (spawn, stdout/stderr, lifecycle)        │
│   • DiskService        (wraps qemu-img)                         │
│   • DisplayLauncher    (remote-viewer for MVP)                  │
└───────────────┬───────────────────────────────┬────────────────┘
                │ uses                            │ spawns
┌───────────────▼─────────────┐    ┌──────────────▼───────────────┐
│ Boxwright.Qmp               │    │ qemu-system-x86_64 (child)    │
│   IQmpClient (JSON-RPC)     │◄───┤   -qmp tcp:127.0.0.1:PORT     │
│   System.Text.Json + socket │QMP │   -spice / -vnc               │
└─────────────────────────────┘    │   -accel <detected>           │
                                    └───────────────────────────────┘
                                                 │ SPICE/VNC
                                    ┌────────────▼──────────────────┐
                                    │ remote-viewer (external, MVP) │
                                    └───────────────────────────────┘
```

---

## 2. Component responsibilities

### `Boxwright.Qmp` — protocol layer
A standalone, GUI-agnostic client for the QEMU Machine Protocol. It is the one
piece designed to be **independently publishable to NuGet** (`Boxwright.Qmp`),
so it must not reference Avalonia, `Boxwright.Core`, or any app concept. Its
job: connect to a QMP socket, perform the capabilities handshake, send
`execute` commands, await typed responses, and surface asynchronous QMP events.

### `Boxwright.Core` — orchestration / domain
Everything that knows what a "VM" is but nothing about pixels. Owns the VM model
and its JSON config, turns a config into a QEMU command line, detects the
accelerator, spawns and supervises the QEMU process, wraps `qemu-img`, and
decides how the display gets shown. May depend on `Qmp`. **Must not** depend on
Avalonia.

### `Boxwright.App` — UI
Avalonia views and viewmodels. Binds to `Core`. Contains **no** QEMU/process/QMP
logic. If a viewmodel is shelling out to `qemu-img`, that logic is in the wrong
project.

---

## 3. Controlling QEMU

### 3.1 Two control channels

QEMU is configured in two distinct ways, and conflating them is a common bug:

1. **Static, boot-time configuration → command-line arguments.** CPU model,
   core/socket topology (`-smp`), memory (`-m`), machine type (`-machine`),
   accelerator (`-accel`), drives (`-drive`/`-blockdev`), NICs (`-netdev`),
   display (`-spice`/`-vnc`), QMP socket (`-qmp`). These are fixed for the life
   of the process. There is no QMP call to change the boot CPU or RAM; you stop
   the VM, rewrite the config, and relaunch.

2. **Dynamic, runtime control → QMP.** Power actions (`system_reset`,
   `system_powerdown`, `quit`, `stop`, `cont`), status queries
   (`query-status`, `query-name`), hotplug (`device_add`/`device_del`),
   screenshots (`screendump`), live disk operations
   (`blockdev-snapshot-sync`, `block_resize`), and migration.

`CommandLineBuilder` owns (1). `IQmpClient` owns (2).

### 3.2 Process lifecycle

1. `AcceleratorDetector` picks the accelerator for the host (see §5).
2. `CommandLineBuilder` turns the `VmConfig` into an argument list, including a
   QMP endpoint: Unix socket on Linux/macOS, TCP on `127.0.0.1` on Windows
   (Windows lacks robust AF_UNIX for this use). A free port/socket path is
   allocated per launch.
3. `QemuProcess` spawns `qemu-system-<arch>` via `System.Diagnostics.Process`,
   capturing stdout/stderr into a per-VM log.
4. Once the process is up, `IQmpClient` connects to the QMP endpoint and runs the
   handshake (§4.2).
5. The UI subscribes to QMP events and to process exit. On exit, sockets are
   torn down and the VM returns to "stopped".

### 3.3 Disk management

`DiskService` wraps the **`qemu-img`** binary as a subprocess:

- `qemu-img create -f qcow2 disk.qcow2 40G` — create (qcow2 is the default).
- `qemu-img info --output=json disk.qcow2` — inspect (parse JSON).
- `qemu-img snapshot -l|-c|-a|-d` — internal snapshots.
- `qemu-img convert` — format conversion / compaction.

Internal qcow2 snapshots are the MVP path. Live/external snapshots
(`blockdev-snapshot-sync` via QMP, with `transaction` for multi-disk
consistency) come later.

---

## 4. The QMP client (`Boxwright.Qmp`)

### 4.1 Protocol shape

QMP is line-delimited JSON over a stream socket. The server greets with a
`QMP` capabilities banner; the client must send `qmp_capabilities` before any
other command. Commands look like
`{ "execute": "query-status" }` (optionally with `"arguments"` and a client
`"id"`); replies are either `{ "return": ... }` or `{ "error": ... }`.
Out-of-band, the server also emits `{ "event": ... }` messages at any time
(e.g. `SHUTDOWN`, `RESET`, `STOP`, `RESUME`, `POWERDOWN`).

The two behaviors a correct client must get right:

- **Correlation.** Tag each command with a unique `id` and match the reply by
  `id`. Do not assume strict request/response ordering — interleaved events and
  optional out-of-band execution mean replies must be correlated explicitly.
- **Event stream.** Events arrive unsolicited. Expose them as an observable
  stream the higher layers can subscribe to; never block waiting for a reply in
  a way that drops events.

### 4.2 Suggested C# surface (sketch, not final)

```csharp
namespace Boxwright.Qmp;

public interface IQmpClient : IAsyncDisposable
{
    /// Connects and performs the qmp_capabilities handshake.
    Task ConnectAsync(QmpEndpoint endpoint, CancellationToken ct = default);

    /// Sends an "execute" command and returns the raw "return" payload.
    Task<JsonElement> ExecuteAsync(
        string command,
        object? arguments = null,
        CancellationToken ct = default);

    /// Strongly-typed convenience wrapper over ExecuteAsync.
    Task<TResult> ExecuteAsync<TResult>(
        string command,
        object? arguments = null,
        CancellationToken ct = default);

    /// Hot stream of asynchronous QMP events (SHUTDOWN, RESET, …).
    IObservable<QmpEvent> Events { get; }

    bool IsConnected { get; }
}

public sealed record QmpEndpoint
{
    public static QmpEndpoint Tcp(string host, int port) => /* … */;
    public static QmpEndpoint UnixSocket(string path)    => /* … */;
}

public sealed record QmpEvent(
    string Name,
    JsonElement Data,
    long TimestampSeconds,
    long TimestampMicroseconds);

public sealed class QmpCommandException : Exception
{
    public string ErrorClass { get; }   // e.g. "GenericError", "CommandNotFound"
    public string Description { get; }
}
```

Implementation notes:
- Built on `TcpClient` / `Socket` + `System.Text.Json` (`Utf8JsonReader` /
  async stream reads). ~300 lines for a solid first version.
- A background read loop dispatches replies (by `id`) and events (to `Events`).
- `query-qmp-schema` is fetched once after connect so `Core` can feature-detect
  the installed QEMU version's capabilities instead of guessing.
- No retry/policy logic in the client; that belongs to `Core`.

### 4.3 Why C#/QMP specifically

There is currently **no first-class QMP client on NuGet** (the only adjacent
packages are deprecated libvirt bindings with no real usage). Mature reference
implementations exist for Python (`qemu.qmp`, official) and Node.js. Building a
clean C# client therefore (a) fills a genuine ecosystem gap and (b) is reusable
as a standalone package that can attract its own users — a second
community-building artifact for free.

---

## 5. Acceleration

The accelerator is chosen automatically at launch by `AcceleratorDetector`.
**Never hardcode `kvm`.**

| Host OS                | Accelerator flag | Notes |
|------------------------|------------------|-------|
| Linux                  | `-accel kvm`     | Needs access to `/dev/kvm`. The good case. |
| macOS (Apple Silicon)  | `-accel hvf`     | Hypervisor.framework. The reason UTM is fast. Requires the hypervisor entitlement on the signed app. |
| macOS (Intel)          | `-accel hvf`     | Same path. (Intel's HAXM was discontinued in Jan 2023 and must not be used.) |
| Windows                | `-accel whpx`    | Windows Hypervisor Platform feature must be enabled. May need `kernel-irqchip=off`; can conflict with a co-installed VirtualBox. |
| Any (fallback)         | `-accel tcg`     | Pure software emulation. Always works, always slow. Used when no HW accel is available or for foreign-arch guests. |

Detection strategy: probe for the platform accelerator (presence of `/dev/kvm`;
`hvf` availability on macOS; WHPX feature on Windows) and fall back to `tcg`.
Surface the chosen accelerator in the UI so users understand their performance.

**Honesty rule:** Windows (WHPX) is genuinely slower than VMware/VirtualBox on
Windows. The README and UI say so plainly. This is an upstream QEMU/WHPX
reality, not something the GUI can engineer away.

---

## 6. Display / console

The hardest UX problem. Strategy is staged deliberately.

| Stage | Approach | Trade-off |
|-------|----------|-----------|
| **MVP** | QEMU runs a **SPICE** server (`-spice`); Boxwright launches the external **`remote-viewer`** (from virt-viewer) against it. | Zero embedding work; works on all three OSes today. Looks like a separate window; requires virt-viewer present. |
| **v0.3** | Embed a **VNC** client rendered inside an Avalonia control. QEMU exposes `-vnc`. | Universally supported, pure-C# feasible. Lacks SPICE's clipboard/folder/USB sharing. |
| **v1.0** | Embed a **SPICE** client (clipboard, folder sharing, multi-monitor, USB redirect). | Best UX. No Avalonia-native SPICE widget exists, so this means FFI to spice-gtk (painful cross-platform), wrapping an experimental Rust spice client, or implementing the protocol — a multi-month effort. |

**Anti-pattern:** embedding QEMU's own SDL/GTK window inside Avalonia. It is
fragile and platform-specific. Do not attempt it.

---

## 7. Networking

- **Default: user-mode networking (SLIRP)** via `-netdev user`. No admin/root
  required; outbound works, the guest is NATed. This is the zero-friction
  default and must remain the default.
- **Advanced: bridged / TAP.** `-netdev tap` for VMs visible on the LAN. This is
  Linux-first (TAP setup is OS-specific and often needs privileges) and is an
  explicitly-gated advanced feature, not part of the default path.
- Port-forwarding (e.g. host:2222 → guest:22) is exposed as a friendly option
  over SLIRP `hostfwd`.

---

## 8. Guest integration

- **QEMU Guest Agent (QGA)** over virtio-serial: clean shutdown, IP reporting,
  file/exec helpers. Optional, detected at runtime.
- **spice-vdagent** in the guest: clipboard sharing and dynamic resolution with
  the SPICE display.
- **virtio-win auto-attach:** when the user creates a **Windows** guest,
  Boxwright auto-attaches the virtio-win driver ISO so storage/network/display
  drivers are available during install. This is a key "it just works" touch,
  mirroring what Quickemu papers over manually.

---

## 9. VM configuration format

One VM is one folder containing one human-readable JSON config plus its
disk(s). Copying the folder moves the VM. No registry, no database, no daemon
state. (See ADR-0006.)

```jsonc
{
  "schemaVersion": 1,
  "id": "9f1c2a3e-…",
  "name": "Ubuntu 24.04",
  "arch": "x86_64",
  "machine": "q35",
  "firmware": "uefi",            // "bios" | "uefi"
  "cpu": { "model": "host", "sockets": 1, "cores": 4, "threads": 1 },
  "memoryMiB": 4096,
  "disks": [
    { "file": "disk.qcow2", "format": "qcow2", "interface": "virtio" }
  ],
  "removableMedia": [
    { "type": "cdrom", "file": "ubuntu-24.04.iso", "attached": true }
  ],
  "network": { "mode": "user", "model": "virtio-net",
               "portForwards": [ { "hostPort": 2222, "guestPort": 22 } ] },
  "display": { "protocol": "spice", "gl": false },
  "accelerator": "auto",         // resolved at launch; never persisted as "kvm"
  "boot": { "order": "cd", "menu": false }
}
```

`CommandLineBuilder` is the single place that translates this into QEMU args.
The schema is versioned (`schemaVersion`) so migrations are explicit.

---

## 10. Packaging & distribution

- **Bundle QEMU per platform** (like UTM). The Windows installer ships
  `qemu-system-*` + `qemu-img`; macOS ships a **signed** QEMU inside the `.app`
  with the hypervisor entitlement; on Linux we can bundle (Flatpak) or use the
  system QEMU.
- **GPL hygiene:** QEMU is shipped **unmodified** and invoked as a subprocess.
  Provide the corresponding source (or a written offer + upstream link)
  alongside binaries. The Boxwright code itself stays MIT. (See ADR-0005.)
- Installer size grows by ~80–200 MB due to bundled QEMU; acceptable.
- **CI matrix:** Windows x64, macOS arm64, macOS x64, Linux x64, Linux arm64.

---

## 11. Security considerations

- **No elevated privileges on the default path.** SLIRP networking and qcow2
  disks need no admin/root. Features that do (TAP, some USB redirection) are
  gated and clearly labelled.
- **Untrusted ISO/OS sources.** When the OS-catalog download feature lands,
  verify checksums/signatures of fetched images and show provenance. Never
  auto-execute downloaded content beyond booting it in the guest.
- **USB redirection on Windows** needs UsbDk/libusb shipped and is opt-in.
- VM data stays inside the per-VM folder; Boxwright does not phone home.

---

## 12. Open technical questions (track as ADRs when resolved)

- Embedded VNC vs. embedded SPICE first for v0.3/v1.0 — which yields better
  effort/value?
- Whether to spin `Boxwright.Qmp` into its own repo once it stabilizes (monorepo
  for now; see ADR-0007).
- macOS code-signing / notarization pipeline for a hypervisor-entitled app.
- Reuse of Quickemu's OS-catalog JSON (license-compatible) vs. our own catalog.
