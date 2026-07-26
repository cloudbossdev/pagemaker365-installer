# Runtime Secret Provisioning Contract

Status: implemented locally; live sandbox validation pending

Tracking issue: [#7](https://github.com/cloudbossdev/pagemaker365-installer/issues/7)

## Purpose

Customer install package contract `0.3` defines the runtime values that the installer must provision without placing raw values in the package, resumable state, command line, environment, logs, callbacks, reports, or support bundles.

## Required Package Metadata

`secrets.runtimeSecrets` must contain exactly these required API settings:

| App setting | Key Vault secret | Source | Owner | Target |
| --- | --- | --- | --- | --- |
| `DATABASE_URL` | Package-defined Azure Key Vault name | `operator` | `customer` | `api` |
| `API_ENTRA_CLIENT_SECRET` | Package-defined Azure Key Vault name | `operator` | `customer` | `api` |
| `API_SESSION_SECRET` | Package-defined Azure Key Vault name | `installerGenerated` | `customer` | `api` |

Each entry also supplies a customer-facing label, purpose, required flag, and minimum length. App-setting names and Key Vault secret names must be unique. The package-level `secrets.keyVaultName` must match `azure.resourceNames.keyVaultName`.

Raw values are prohibited. A package that omits this metadata, declares another setting, uses an unsupported source/owner/target, or uses a contract version other than `0.3` fails before Azure mutation.

## Protected Data Flow

1. The Install page renders only entries whose signed source is `operator`.
2. WPF `PasswordBox.SecurePassword` is copied into an in-memory `SecureString`; it is not data-bound or added to persisted installer state.
3. Installer-generated material is created with a cryptographically secure random number generator immediately before runtime configuration.
4. The parent process sends metadata followed by protected values to a noninteractive PowerShell child through redirected standard input. Values are not command arguments or environment variables.
5. PowerShell submits the values as an ARM `@secure()` object to `infra/runtime-configuration.bicep`.
6. ARM creates customer-owned `Microsoft.KeyVault/vaults/secrets` resources in the package-named vault.
7. The API App Service receives only Key Vault reference strings and uses the package-named user-assigned managed identity through `keyVaultReferenceIdentity`.
8. The installer refreshes App Service references and requires every declared setting to report `Resolved` before validation is unlocked.
9. All in-memory materials and password controls are cleared after the attempt, whether it passes or fails.

## Identity And Authorization

The Azure operator uses the existing deployment role contract: `Owner`, or `Contributor` plus `Role Based Access Control Administrator`, or `Contributor` plus `User Access Administrator`, at the target subscription. Secret resources are created through the ARM deployment plane; the operator is not granted a permanent Key Vault data-plane secret role.

The deployed user-assigned managed identity receives `Key Vault Secrets User` at the vault scope. This read-only runtime role can resolve secret references but cannot set or delete secret values.

## Evidence And Lifecycle Order

The install lifecycle is:

1. `install_started`
2. Azure deployment
3. `azure_deployment_completed`
4. protected runtime configuration and reference resolution
5. `runtime_configured`
6. smoke tests and `smoke_tests_completed`
7. `install_completed`

Configuration failure emits sanitized `install_failed` evidence and does not unlock validation. Portal callback delivery failure remains in the installer outbox and does not redefine the Azure or runtime result.

The runtime configuration artifact contains secret names, app-setting names, deployment metadata, reference status, and `valuesPersisted: false`. It contains no values.

## Verification

Automated verification covers:

- `0.3` schema and exact runtime setting validation
- rejection of legacy or incomplete contracts
- operator/generated UI separation
- saved-session non-persistence
- generated minimum length
- redirected-standard-input transport
- ARM secure parameter and Key Vault resource wiring
- App Service managed-identity Key Vault references
- final-evidence inclusion of the sanitized runtime configuration artifact

Live acceptance still requires a freshly generated and signed `0.3` staging package, an Azure deployment, reference resolution, runtime smoke tests, callback review, and a scan of all generated local and portal evidence.
