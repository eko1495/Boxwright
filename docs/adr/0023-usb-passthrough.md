# ADR-0023: Host USB passthrough (by vendor:product)

- **Status:** Accepted
- **Date:** 2026-06-15

## Context
The roadmap's Stage-3 line "USB passthrough wizard" lets a VM use a real host USB device (a YubiKey,
a webcam, a flashing dongle). QEMU does this with `-device usb-host`, matched either by **bus/address**
(unstable — changes on replug) or by **`vendorid`/`productid`** (stable across replugs). The host-side
device access is QEMU's job; Boxwright only has to (a) let the user identify a device and (b) put the
right argument on the command line.

Two halves have very different cross-platform stories (Directive 4):
- **Wiring the passthrough** (`-device usb-host,vendorid=…,productid=…`) is pure command-line — it works
  wherever QEMU's `usb-host` does (Linux and Windows via libusb; macOS/HVF support is limited). No
  host-OS code on our side.
- **Enumerating** the connected host devices (to pick from) is inherently host-specific: Linux exposes
  them in sysfs, Windows via SetupAPI, macOS via IOKit. There is no portable API.

## Decision
- **Identify devices by `vendorid:productid`** (4 hex digits each), persisted on the VM as
  `VmConfig.UsbDevices` (`UsbPassthroughConfig { VendorId, ProductId, Description }`). Stable across
  replug, human-readable in `vm.json`, and the natural key for both the command line and `device_add`.
- **`CommandLineBuilder` emits one `-device usb-host,vendorid=0x…,productid=0x…,id=usbpassN`** per
  configured device, onto the USB controller the builder already adds (`-usb`). Empty list → no change,
  so existing VMs and the golden test are unaffected. This is in Core, so a configured VM passes the
  device through whether it's launched from the GUI or the CLI.
- **Enumeration is capability-gated behind `IUsbDeviceEnumerator`** with `IsSupported`, one
  implementation per OS, each isolating the bug-prone parsing into a pure, testable method:
  **Linux** parses **sysfs** (`/sys/bus/usb/devices/*/{idVendor,idProduct,…}`); **macOS** runs
  `system_profiler SPUSBDataType -json` and parses the JSON tree; **Windows** uses **SetupAPI**
  P/Invoke and parses each device's hardware id (`USB\VID_046D&PID_C52B`). A host with no
  implementation falls back to an `Unsupported` enumerator (`IsSupported = false`) and the user adds a
  device by vendor:product manually. This honors Directive 4: degrade gracefully with a clear message,
  never silently single-OS.
- **CLI surface:** `boxwright usb list` (host devices, gated), `usb show <vm>`,
  `usb add <vm> <vvvv:pppp> [--description] [--now]`, `usb remove <vm> <vvvv:pppp> [--now]`. `add`/`remove`
  edit the persisted config (next boot); **`--now`** also applies the change live to a running VM.
- **Live hot-plug** (`--now`): `IRunningVm.AttachUsbAsync`/`DetachUsbAsync` issue QMP `device_add`
  (driver `usb-host`, `vendorid`/`productid`) / `device_del`, keyed by the **same** `UsbId.DeviceId`
  handle (`usb-vvvv-pppp`) the command line uses — so a device passed through at boot can also be
  unplugged live by its vendor:product. The CLI re-adopts the running VM (ADR-0014) to do this and
  leaves the adopted handle undisposed (disposing would clear `runtime.json`).

## Consequences
- **Easier:** a VM can use a real USB device, configured from either front end (CLI `usb` commands or
  the GUI settings picker); the wiring is one small, golden-tested command-line addition; all three
  desktop OSes enumerate the host's devices.
- **Verification caveat:** the parsing of each platform's output is unit-tested, but the macOS
  `system_profiler` call, the Windows SetupAPI P/Invoke, and the Avalonia picker view were authored on
  Linux/headless and **not exercised on a real macOS/Windows host or a running GUI**. They compile, are
  OS-gated, and the parsers are tested; the platform calls themselves want a smoke test on the real OS.
- **Harder / deferred:** a device claimed by the host driver may need unbinding on Linux (documented, not
  automated). USB **2.0/3.0 controller** selection (qemu-xhci) is left at QEMU's default for now.

## Alternatives considered
- **Match by bus/address.** Rejected as the primary key: it changes when the device is replugged or the
  hub topology shifts, so a saved VM config would silently target the wrong (or no) device. vendor:product
  is stable. (Bus/address could be added later for disambiguating two identical devices.)
- **Shell out to `lsusb`.** Rejected: it's not always installed and is Linux-only anyway; sysfs is always
  present on Linux and needs no dependency.
- **Block the feature on full cross-platform enumeration.** Rejected against Directive 4's own escape
  hatch: capability-gate and degrade. Linux enumeration + universal vendor:product entry ships value now
  without pretending Windows/macOS listing exists.
