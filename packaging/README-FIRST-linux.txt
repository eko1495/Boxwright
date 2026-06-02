Boxwright for Linux - read me first
===================================

1) Run it
   The AppImage is a single self-contained file. Make it executable, then run it:
     chmod +x Boxwright-<version>-x86_64.AppImage
     ./Boxwright-<version>-x86_64.AppImage
   It bundles the .NET runtime - no .NET install needed.

2) Install QEMU + the display viewer (NOT bundled)
   Boxwright drives your SYSTEM QEMU and opens a VM's screen with "remote-viewer"
   (from virt-viewer). Install them once with your package manager:
     Debian/Ubuntu: sudo apt install qemu-system-x86 qemu-utils virt-viewer
     Fedora:        sudo dnf install qemu-system-x86 qemu-img virt-viewer
     Arch:          sudo pacman -S qemu-full virt-viewer
   Boxwright shows a clear message if it can't find QEMU or the viewer; VMs still run
   without virt-viewer, only the display window needs it.

   For hardware acceleration (KVM), your user needs access to /dev/kvm - usually by
   being in the "kvm" group. Without it, Boxwright falls back to slow TCG emulation.

3) Desktop libraries
   The GUI needs a few common X11/font libraries that ship with virtually every desktop
   Linux install: libx11-6, libice6, libsm6, libfontconfig1, libicu. If the app won't
   start on a minimal system, install those.

4) Where your data lives
   VMs:  ~/.local/share/Boxwright/VMs
   ISOs: ~/.local/share/Boxwright/ISOs   (the "Get an OS" download cache)
   Logs: ~/.local/share/Boxwright/logs

5) Performance
   QEMU speed depends on your host and accelerator (KVM is fast; TCG software emulation
   is slow). Boxwright is a GUI over QEMU; it does not change QEMU's underlying speed.

License: Boxwright is MIT (see LICENSE). QEMU and virt-viewer are your own system
packages - Boxwright neither bundles nor redistributes them.
