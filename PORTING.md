# Porting notes

## C# -> C++ mapping

| C# | Qt/C++ |
|---|---|
| `AppSettings` | `AppSettings` + Qt JSON |
| `BrowserDefinition` | `BrowserDefinition` |
| `CertificatePolicyEntry` | `CertificatePolicyEntry` |
| `PolicyState` | `PolicyState` |
| `PolicyStore` | abstract `PolicyStore` |
| `Microsoft.Win32.Registry` | Win32 Registry API |
| `JavaScriptSerializer` | `QJsonDocument` |
| `X509Certificate2` | OpenSSL `X509` |
| `OpenFileDialog` / `SaveFileDialog` | `QFileDialog` |
| `DataGridView` | `QTableWidget` |
| `MessageBox` | `QMessageBox` |
| WinForms | Qt Widgets |

## Important behavioral details retained

1. `CAPlatformIntegrationEnabled=false` means the platform trust store is
   disabled for browser server-auth path building.
2. `CACertificatesWithConstraints` is stored as an array of objects containing
   a base64 DER certificate and optional `permitted_dns_names` /
   `permitted_cidrs`.
3. The GUI requires at least one DNS name for every entry when applying.
4. Legacy CIDR entries are preserved when loading/importing but cannot be
   applied by the v2 GUI.
5. DNS input is normalized to lowercase ASCII/IDNA form.
6. A bare DNS name can be expanded to `name` + `.name`.
7. Only CA certificates are accepted.
8. Private keys are never read or exported.

## Deliberate cross-platform changes

The original program is Windows-only and uses HKCU. The port introduces
`PolicyScope`:

- Windows: HKCU policy store.
- Linux: `<root>/{managed,recommended}/qchrome-certificates.json`.
- macOS: `/Library/Managed Preferences/<bundle>.plist` for managed or
  `~/Library/Preferences/<bundle>.plist` for recommended.

The Linux and macOS paths are intentionally isolated in their platform
backends so they can be adjusted without touching the GUI/model.

## Known first-version gaps

- macOS managed-policy writing may require running the application with
  administrator privileges. A production version should implement a small
  privileged helper or configuration-profile workflow rather than asking the
  whole GUI to run as root.
- Linux mandatory policies under `/etc` likewise need root privileges. A
  production version should use a privileged helper instead of running the GUI
  as root.
- Windows has the original-style UAC fallback through `reg.exe`.
- The original Windows COM `Shell.Application` refresh signal is not reproduced
  on Linux/macOS. The UI provides the policy URL after saving. Chrome's
  documented policy itself supports dynamic refresh for these certificate
  policies.
- Browser-specific Linux policy roots for Edge/Brave/Chromium can vary by
  package/distribution. They are therefore fields of `BrowserDefinition` and
  should be made configurable in a later release.
- The certificate loader intentionally starts with DER. PEM support is easy
  to add but was not necessary to reproduce the original `.cer/.crt/.der`
  behavior.

## Security considerations

The certificate DER/base64 content is sensitive configuration data even though
it does not contain private keys. Do not make policy backup files world-readable.

On Linux, `/managed` should be protected from ordinary users as recommended by
Chrome's administrator documentation.
