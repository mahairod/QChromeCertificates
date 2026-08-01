# Chrome Certificate Policy Manager

Windows GUI for two Google Chrome certificate policies:

- `CAPlatformIntegrationEnabled`
- `CACertificatesWithConstraints`

The application writes policies for the current Windows user under:

```text
HKCU\Software\Policies\Google\Chrome
```

It does not install or remove certificates from Windows stores. Public roots from Chrome Root Store are not affected.

## Usage

1. Run `ChromeCertificatePolicyManager.exe` and confirm the Windows UAC prompt. Administrator rights are required to write Chrome policies.
2. Select whether Chrome should trust manually added Windows certificates for HTTPS.
3. Add a CA certificate file. It appears in the list without DNS constraints.
4. Select it and click `Изменить ограничения…`, or double-click the row, then specify permitted DNS names and/or CIDR networks.
5. Save and click `Повторно загрузить правила Chrome`. For manual verification, copy `chrome://policy/` from the dialog and paste it into Chrome. Saving is blocked while a certificate has no DNS or CIDR constraints.

To permit both a base domain and its subdomains, add both forms:

```text
gosuslugi.ru
.gosuslugi.ru
```

Chrome will display that it is managed because local enterprise policies are configured.

Use `Экспорт…` and `Импорт…` to save and restore policy backups. Before overwriting an existing configuration or resetting policies, the application offers to export the current state.

## Build

The application version is stored in `VERSION`. Run `build.cmd` to embed the version, Windows file metadata, manifest, and `app.ico` into the executable. The script uses the C# compiler included with .NET Framework in Windows.

Run `verify-build.ps1` after building to verify the embedded version, file metadata, manifest, and icon. Pass `-RunSelfTest` to also test registry serialization, DNS and CIDR normalization, and backup round-tripping.

GitHub Actions builds every push and pull request. A tag matching `v<VERSION>` creates a GitHub Release with the executable and its SHA-256 checksum.

## Test

```text
ChromeCertificatePolicyManager.exe --self-test --registry-path Software\ChromeCertificatePolicyManager\Tests
```

The self-test uses a temporary subkey and does not modify Chrome policies.

## License

This project is released under the [Unlicense](UNLICENSE).
