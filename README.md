# Service Bus Explorer

A cross-platform desktop app for browsing and managing Azure Service Bus namespaces, on Windows, macOS and Linux.
It's a rewrite on .NET 10 and [Avalonia](https://avaloniaui.net/) of the Windows-only
[Service Bus Explorer](https://github.com/paolosalvatori/ServiceBusExplorer), using the current
`Azure.Messaging.ServiceBus` SDK. The layout follows the original: File / Edit / Actions / View / Help menus, the
namespace tree with right-click menus, a "View Queue" panel with Description, Messages and Deadletter tabs and its
button bar, and the Log window.

## What it does

- **Connect** with a connection string or Microsoft Entra ID (browser sign-in, device code, Azure CLI, or
  `DefaultAzureCredential`), or to the local Service Bus emulator. Saved connections keep their connection string in
  the macOS Keychain, Windows DPAPI or the Linux keyring.
- **Browse** queues, topics, subscriptions and rules with message counts and filtering. Listen- or Send-only
  credentials work too: add entities by name.
- **Messages:** peek, peek-lock receive with complete / abandon / defer / dead-letter, receive-and-delete, and send
  with any properties, copies, TTL or a schedule. Bodies show as JSON, XML, text or hex.
- **Dead letters:** resubmit (optionally edited), delete or purge. Receive deferred messages, cancel scheduled ones.
- **Sessions:** find sessions, peek and receive per session, and edit session state.
- **Manage entities:** create, edit, enable or disable and delete queues, topics, subscriptions and SQL or
  correlation rules. Export and import definitions as JSON.

- **Read-only connections:** tick **Read-only** in the connect dialog for production namespaces. You can browse,
  peek and export, while sending, receiving, resubmitting, deleting, purging and every entity change are refused by
  the app's services, not just hidden in the UI.

Not included yet: Event Hubs, Relay, Event Grid, Notification Hubs, and the load-test tools of the original.

## Install

Download the package for your platform from the [releases](../../releases) page.

| Platform | Package | Notes |
|---|---|---|
| Windows | `ServiceBusExplorer-<version>-win-x64.zip` (or `win-arm64`) | One `.exe`; no install needed. SmartScreen may warn because it isn't code-signed. |
| macOS | `ServiceBusExplorer-<version>-osx-arm64.zip` (Apple Silicon) or `osx-x64` (Intel) | Unzip and move to Applications. It's signed ad hoc, not notarized, so the first time run `xattr -dr com.apple.quarantine "/Applications/Service Bus Explorer.app"`, or right-click it and choose **Open**. |
| Linux | `ServiceBusExplorer-<version>-linux-x64.tar.gz` (or `linux-arm64`) | Run `./ServiceBusExplorer`, or `./install.sh` to add it to your applications menu. Install `libsecret-tools` (for `secret-tool`) to keep connection strings in your keyring. |

## Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json` pins 10.0.402 or a later 10.0
feature band).

```sh
dotnet run --project src/SbExplorer.App
dotnet test --solution ServiceBusExplorer.slnx
```

## Local emulator

`docker/` runs the [Azure Service Bus emulator](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator)
with sample entities, so you can try everything without an Azure subscription.

```sh
docker/start-emulator.sh                      # first start takes a minute or two
dotnet run docker/seed.cs                     # clears and fills the sample entities with messages
```

The script puts a random password for the emulator's SQL Server container in `docker/.env`, which git ignores. To
stop the emulator, run `docker compose -f docker/emulator.yml --env-file docker/.env down`.

In the app, choose **File > Connect**, click **Local emulator**, then **OK** (or **Save** it first). The emulator caps
message time-to-live at one hour, so
run the seed script again when the messages expire. It also reports zero for most message counts and doesn't keep user
metadata. Those are emulator limits, not app bugs.

The integration tests (`tests/SbExplorer.IntegrationTests`) use the emulator and skip themselves when it isn't
running. Set `SBX_REQUIRE_EMULATOR=1` to make a missing emulator fail them instead, as CI does.

On macOS, `SBX_KEYCHAIN_TESTS=1` also runs the test that writes to and removes an entry from your login keychain.

## Package

```sh
build/publish.sh osx-arm64          # or win-x64, win-arm64, osx-x64, linux-x64, linux-arm64
build/publish.sh linux-x64 0.2.0    # optional version, defaults to <Version> in Directory.Build.props
```

Packages land in `artifacts/dist/`. Windows and Linux builds work from any OS. macOS bundles are best built on macOS,
which signs them ad hoc. Pushing a tag such as `v0.2.0` runs `.github/workflows/release.yml`, which builds all six
packages and attaches them to a GitHub release.

On macOS the keychain remembers which build saved a connection string, so a different build (for example
`dotnet run` and then the packaged app) asks once for your keychain password before reading it. Choose
**Always Allow** to stop the prompt for that build.

## Project layout

| Path | Contents |
|---|---|
| `src/SbExplorer.Core` | Models and interfaces with no UI or Azure dependencies: connections, entities, messages, formatting, import/export |
| `src/SbExplorer.Infrastructure` | Azure SDK implementations, Entra credentials, OS secret stores |
| `src/SbExplorer.App` | The Avalonia app: views, view models, dialogs |
| `tests/` | Unit tests for each project, headless UI tests, and emulator integration tests |
| `build/` | Packaging script, macOS `Info.plist`, Linux desktop entry, icon generator |
| `docker/` | Emulator compose file, entity config and seed script |

## License

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
