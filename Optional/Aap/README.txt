AirBridge optional AAP bridge — development milestone 0.1 (2026-11-09)

STATUS
- Production AirBridge 3.0.22, its launch shortcut, settings and audio paths are unchanged.
- No driver was installed/staged/loaded. No certificate was created/imported.
- Secure Boot, test-signing, HVCI, pairing, default outputs and startup settings were not changed.
- This directory is NOT consumed by the normal AirBridge build or startup.
- Candidate driver is UNSIGNED. Do not install these development artifacts.

IMPLEMENTED
Protocol.cs: L2CAP message-oriented AAP framing; handshake and notification subscription;
noise-mode command encoding; conservative noise/wear/battery notification parsing.
Primary/secondary ear identity is not assumed to mean left/right. Transitional/unknown
wear states remain explicit. Battery unknown is null, not 0. Malformed notifications
are discarded, not used to overwrite successful state.
Session: serialized calls, no ready state until a recognized report, noise changes require
fresh prior noise state and matching mode report after sending; timeout is not success.
It does NOT claim the report cryptographically proves command acknowledgement (AAP
notifications have no request ID). Handshake failure disconnects; disposal is idempotent.
WindowsChannel.cs: SetupAPI enumeration; exclusive device handle; pinned NTPods IOCTL ABI;
packed 10-byte address/PSM connect request; response bounds checks; overlapped I/O cancellation
followed by draining completion BEFORE releasing buffers. The driver must cooperate with
cancellation: cleanup can wait beyond the requested deadline. No hard realtime guarantee.
NativeAdapter.cs: implements existing INativeHeadsetTransport for a single explicit device;
only noise writes, wear reads. Model allowlist limits ANC/adaptive modes. Model/firmware
compatibility, particularly AirPods 4 ANC, still requires real device validation.
No auto-pause on ear removal, gesture remapping, microphone streaming or hearing-aid writes.
No background polling thread or production UI integration yet. A host must opt in explicitly.

SOURCE / LICENSING
Kernel candidate is NTPods upstream, NOT an independently authored AirBridge kernel driver:
https://github.com/arctumn/ntpods/tree/188e45df20259b616ce155be812086259d1a2488/windows/drivers/aap
vendor/ntpods contains unchanged source and GPL-3.0 license. Preserve corresponding source
and notices with any redistribution; do not silently merge its code into closed-source builds.
research contains the exact protocol references and upstream license; it is not compiled.
Managed implementation uses the documented wire/IOCTL facts, not imported upstream modules.
Protocol facts are empirical, principally AirPods Pro 2 USB-C firmware 7A305; not Apple API guarantees.

BUILD / VERIFY (build-time PowerShell only)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
  Produces bin/AirBridge.Aap.dll and bin/AapProbe.exe, runs offline tests automatically.
  Uses .NET Framework 4.8 compiler and the existing AirBridge-3.0.22 Core assembly.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\BuildDriver.ps1
  Compiles pinned third-party kernel source with existing project-local WDK/SDK and VS2022.
  Does not install/sign. Tools currently resolve from this workspace; see paths in script.
bin/AapProbe.exe --list
  Enumerates the candidate device interface only; does not open it or send IOCTLs.
  There is intentionally no hardware-connect command in this milestone.

VERIFICATION
Managed build: PASS. 37 checks plus 10000 randomized malformed packet cases: PASS.
Read-only enumeration: 0 matching interfaces on this machine.
Driver x64 compile/link: PASS, WDK 10.0.26100.6584, SDK 10.0.26100.9457, KMDF 1.15.
InfVerif /w: exit 0. Inf2Cat /os:10_X64: no errors/warnings; unsigned catalog generated.
Authenticode: NotSigned. Catalog generation is NOT signature or hardware certification.
Actual L2CAP open, earbud control, disconnect on process crash, sleep/resume and HVCI:
NOT TESTED. Do not infer real-device success from synthetic protocol tests.

INSTALLATION GATES / SYSTEM IMPACT
Not ready for installation. Before asking for installation approval:
1. Narrow kernel IOCTL access/ACL, bound sizes, permit only the AAP PSM, and verify the
   requested address against the selected Bluetooth service's device/container identity.
2. Remove or independently review unused ATT/hearing-aid operations. Current upstream
   exports those operations even though this client never invokes them.
3. Audit cancellation, PnP removal, sleep/resume and concurrent callback lifetime;
   verify file-close cleanup after an actual process crash. Offline mocks cannot prove it.
4. Verify signature under kernel policy (including catalog), memory-integrity compatibility
   and exact Windows version. Source build alone establishes none of these.
5. Review GPL distribution obligations before product bundling.

An eventual install adds a persistent PnP kernel driver package/service for the AirPods AAP
Bluetooth service. Exiting AirBridge should close its channel, but does NOT uninstall the
driver. It is not a virtual audio device and is not intended to alter the audio default,
though Bluetooth operations can interrupt playback and kernel faults can affect Windows.
Driver replacement/removal may require reconnect or reboot and administrator rights.
A developer test-signing route may require trust-store/boot changes and conflict with Secure
Boot. NONE are authorized or performed here. Prefer a properly Microsoft-signed route;
verify eligibility and HVCI separately rather than promise no security-setting changes.
Microsoft signing references:
https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-

FUTURE ROLLBACK PLAN (documentation, no removal commands executed)
Before install record the exact published OEM INF, current service driver binding and
security settings; never remove all Bluetooth drivers or infer an OEM number.
Stop the optional host, release channel, uninstall only that recorded AAP package/device
binding, then verify ordinary A2DP/HFP audio and reconnect. Reboot only if required and approved.
Remove only certificates explicitly created for this experiment, if any; restore boot/security
values to their recorded originals. Do not modify pre-existing third-party settings.
For this milestone rollback is simply not using/deleting this Optional/Aap directory;
production 3.0.22 does not reference it.
