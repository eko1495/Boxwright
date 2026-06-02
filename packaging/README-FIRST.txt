Boxwright for Windows - read me first
=====================================

1) First run / SmartScreen
   Boxwright is not code-signed yet, so Windows SmartScreen may show
   "Windows protected your PC". Click "More info" -> "Run anyway".
   Your antivirus may also flag the bundled QEMU emulator (qemu\*.exe) - allow it if so.

2) QEMU is bundled - nothing to install
   This package includes QEMU under the qemu\ folder. You do NOT need to install QEMU
   separately; Boxwright uses the bundled copy automatically.

3) The display viewer (one-time install)
   To see a running VM's screen, Boxwright opens it in "remote-viewer" (from virt-viewer),
   which is NOT bundled. Install it once:
     - Get "Virt Viewer" for Windows from https://virt-manager.org/download/
       (mirror: https://releases.pagure.org/virt-viewer/), install it, then click "Open display".
   Until it's installed, VMs still run - only the display window needs it. Boxwright shows a
   reminder if it can't find the viewer.

4) Performance (an honest note)
   On Windows, QEMU's hardware acceleration (WHPX) is generally slower than VMware or
   VirtualBox. Boxwright is a GUI over QEMU; it cannot change QEMU's underlying speed.

5) Where your data lives
   VMs:  %LOCALAPPDATA%\Boxwright\VMs
   Logs: %LOCALAPPDATA%\Boxwright\logs   (or use the "Logs folder" button in the toolbar)

License: Boxwright is MIT (see LICENSE). Bundled QEMU is GPLv2 (see THIRD-PARTY-NOTICES.txt).
