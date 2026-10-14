# Net Blitz — Avalonia edition

A standalone C# / Avalonia desktop port of the 1999 MFC Net Blitz chess and bughouse game by Yannick Létourneau and Sébastien Lachance. The original C++ files are unchanged. This solution does not depend on MFC, Visual C++ 6, the Windows registry, or another project.

## Build and run

Install the **.NET 10 SDK**, then run these commands from this directory:

```powershell
dotnet restore NetBlitz.sln --configfile NuGet.Config
dotnet build NetBlitz.sln -c Release --no-restore
dotnet run --project src/NetBlitz.Desktop -c Release --no-build
dotnet test NetBlitz.sln -c Release --no-restore
```

Open `NetBlitz.sln` in Visual Studio or Rider and select `NetBlitz.Desktop` as the startup project. Dependencies are pinned to Avalonia **11.3.20**. No global templates or additional workloads are needed.

For the included Windows build, launch `artifacts/win-x64/NetBlitz.Desktop.exe`. Keep the entire folder together; the build includes its .NET runtime.

Create another Windows build with:

```powershell
dotnet publish src/NetBlitz.Desktop -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

The source also targets Avalonia-supported Linux and macOS desktops. Use `linux-x64`, `osx-x64`, or `osx-arm64` to publish on the appropriate platform. Windows compilation, headless rendering, and loopback networking have been tested here; native Linux/macOS execution has not. Consult Avalonia's [platform requirements](https://v11.docs.avaloniaui.net/docs/overview/supported-platforms).

## Play

- Choose **Classic chess** or **Bughouse**, then **Start game** for local pass-and-play. There is no computer opponent, matching the original game.
- Click a piece and its destination, or drag it. Legal destinations are highlighted. Arrow keys move the keyboard cursor; Enter/Space select or move, Escape/right-click cancel. Promotion opens a four-piece chooser.
- The interface uses the original light-blue/bright-blue board, navy controls, and yellow accents. Pieces use the Cburnett SVG set and render as vectors, including pools and promotion choices, so they remain sharp when resized or displayed at high DPI.
- In bughouse, select an available pool piece, then an empty destination. Captures go to the partner's pool. Captured promoted pieces revert to pawns; pawns cannot be dropped on the first or last rank.
- Team 1 is White A + Black B. Team 2 is Black A + White B. A win on either board wins the match.
- Default time control is 5 minutes, no increment, with **manual time calls**, as in the original defaults. Settings also provide increment, unlimited time, automatic flags, king capture, check highlighting, and last-move highlighting.
- The original bughouse waiting rule is enabled: a future piece that could block check prevents an immediate mate declaration, even with an empty pool. It can be disabled in Settings.
- A draw offer is sent with that player's next move. The other team can accept; moving declines it. Local games have an **Act as…** selector for draw offers, resignations, and time calls.
- The Moves tab pairs White and Black in two columns, one row per move number. Bughouse has a Board A / Board B selector. It uses coordinate notation and exports a text game record with the final FEN. This is not a PGN file. The display/export retains the most recent 500 plies.
- Settings persist in `%LOCALAPPDATA%/NetBlitz.Avalonia/settings.json` on Windows, or the platform's local application-data directory. The old registry settings are not changed.

## Network rooms

1. Choose the same mode on all computers.
2. One player selects **Host a room**, enters a nickname and TCP port (default **5010**), and shares their LAN address.
3. Other players select **Join a room** and enter that address and port. Use `127.0.0.1` for multiple instances on one computer.
4. Choose any unoccupied seat before starting. The host may switch everyone's colors between games.
5. The host starts when at least one player is seated on each team. Chess has two players. Bughouse supports two, three, or four; a player without a partner controls both seats on their team.

The host listens on IPv4 network interfaces. Allow its TCP port through the firewall **only on trusted networks**. No firewall or router changes are made by this application. For remote play, use a trusted VPN or other protected network. This protocol has no TLS, accounts, password protection, matchmaking, spectators, or reconnect/resume support. Any player disconnect aborts an active match without recording a result. Closing the host closes the room.

The host validates turns, moves, drops, seat ownership, game/ply sequence, settings permissions, and all clock results. Messages use bounded, length-prefixed JSON, send/read timeouts, a handshake limit, and per-client rate limiting. Team chat is routed only to teammates (the host is trusted and operates the server).

**The new network protocol cannot connect to the original MFC executable. All players must use this edition.**

## Solution layout

| Project | Responsibility |
| --- | --- |
| `NetBlitz.Core` | Board rules, piece pools, game state, time controls, results, snapshots |
| `NetBlitz.Network` | TCP framing, host authority, lobby, seat ownership, chat, disconnect handling |
| `NetBlitz.Desktop` | Avalonia windows, resizable boards, vector piece rendering, original blue palette, dialogs, local preferences |
| `NetBlitz.Tests` | Rules, known move trees, match behavior, real TCP loopback sessions, and rendered UI input tests |

Tests render the real Skia/Avalonia UI to `artifacts/screenshots/`. See [the migration notes](docs/MIGRATION.md) for source mapping, intentional changes, and remaining differences.

## Original materials

Original authorship and distribution text is preserved in [Original-Readme.txt](docs/Original-Readme.txt) and [Original-Lisezmoi.txt](docs/Original-Lisezmoi.txt). The original icon remains in use, and the original atlas is retained as a reference for the palette. No new redistribution license is asserted for those materials; see [NOTICE.md](NOTICE.md).

The displayed pieces are [Cburnett by Colin M. L. Burnett](https://github.com/lichess-org/lila/tree/5f25d6d433ae95ce1f6a92baababb14f98658596/public/piece/cburnett), licensed under GNU GPL version 2 or later. Unmodified source SVGs, the license, upstream notice, and attribution are in `src/NetBlitz.Desktop/Assets/Pieces/Cburnett` and are also included with the Windows build. Chess.com was reviewed as a visual reference; its proprietary piece images are not bundled. See [asset notes](docs/ASSETS.md).
