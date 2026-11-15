# Hardware qualification before a stable release

Automated fake-display and WPF tests do not qualify real hardware. A preview can be built without a code-signing certificate or a hardware matrix. Stable publication checks for a valid installer signature and a version-specific evidence file; it does not manufacture results.

For each case, record the actual monitor model, GPU/driver, cable/dock, Windows version, result and observations. Restore the initial display settings after each write test. Test on a spare/workstation setup rather than interrupting another user's active session.

| Required case | Procedure and acceptance |
|---|---|
| NVIDIA-SDR-DDC | Read and apply gamma, contrast, vibrance and backlight independently. Cancel/expire/keep previews; verify original-color restoration and successful backlight persistence. |
| HDR-GAMMA-GUARD | Enable HDR/advanced color manually. Verify detection and refusal of the legacy gamma route. Verify supported independent backlight controls remain usable. Return to SDR and re-detect. |
| MIXED-DPI | Move main, Workspace and tray popup between 100%, 150% and 200% displays, including negative coordinates. Verify controls, keyboard focus, popup positioning and text scale. |
| SLEEP-WAKE | Make pending changes, sleep and wake the computer. Verify the selected reapply policy and latest due brightness schedule, without outdated retries overriding newer changes. |
| DOCK-RECONNECT | Unplug/reconnect a panel and change its connection. Verify profiles, drafts, identity, manual reassociation and selected-monitor recovery. |
| INTERNAL-PANEL | On a compatible laptop, read the uniquely matched WMI panel, apply a bounded level and restore it. Confirm an external monitor never receives that panel's command. |
| AMD-SDR | Verify standard Windows gamma and supported backlight controls on AMD. Confirm NVIDIA Digital Vibrance is unavailable rather than silently presented as applied. Additional proprietary saturation APIs are not implemented. |
| INTEL-SDR | Repeat standard gamma/backlight checks on Intel, including internal panel association where available. Do not claim proprietary saturation support. |
| INSTALL-UPGRADE-UNINSTALL | Install and upgrade with populated settings; verify backups, local CLI, autostart path, self-contained runtime and uninstall. Test both the normal user and elevation using a different admin account; inspect per-user startup cleanup. |

Store approved evidence as `docs/qualification/<version>.json`, following this shape:

```json
{
  "Version": "2.2.0",
  "TestedAt": "<actual ISO-8601 date>",
  "Operator": "<actual operator>",
  "Cases": [
    { "Id": "NVIDIA-SDR-DDC", "Setup": "<actual hardware>", "Result": "NotTested", "Notes": "<actual observations>" }
  ]
}
```

The example is deliberately incomplete. Do not copy `Passed` into a case without running it. Run `scripts/Test-HardwareEvidence.ps1 -Path <file> -Version <version>` to verify completeness. Automated CI continues to run the complete hardware-independent suite for every change.

Signing uses an existing Windows certificate-store entry:

```powershell
.\build_installer.ps1 -SigningCertificateThumbprint <thumbprint> -CertificateStore CurrentUser -RequireSignature
```

The build validates the existing certificate's private-key availability, validity and code-signing EKU, signs the application/CLI binaries and installer with SHA-256 and a timestamp, and verifies each signature. No certificate is generated, private key exported, account created or certificate purchased by these scripts.
