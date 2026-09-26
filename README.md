<img src="src/Paperdoll.App/Assets/icon.svg" width="72" alt="">

# Paperdoll

A character editor for Space Station 14. You can make and edit characters without joining a
server, see them drawn the way the game draws them, and save them as the `.yml` file the lobby's
Import button takes.

Species, markings, colours, loadouts and traits are read from each fork's own repository, so the
choices match that fork. Delta-V and Euphoria get the most attention, then upstream. Fork > Match
a Server loads the exact version a server runs, read from its public info page, since servers
often lag behind a fork's newest code.

Paperdoll only downloads game data. Your characters stay on your computer: nothing is uploaded or
shared, and it never reads other players' characters.

It's early. Forks on the new appearance model (Delta-V, Euphoria, upstream and a few others) can
be downloaded, previewed from all four sides, edited and exported. Older forks aren't supported
yet. So far it has only been run on Linux.

## Running it

From a build: unpack it and run `Paperdoll` (Linux, macOS) or `Paperdoll.exe` (Windows). Nothing
else needs installing. The Windows and macOS builds haven't been tried on those systems yet, and
macOS may refuse to open an unsigned program downloaded from the internet until you allow it in
System Settings, Privacy & Security.

From source, with the .NET 10 SDK:

```sh
dotnet run --project src/Paperdoll.App
```

The first time, pick a fork under Fork > Forks and download it. Fork data goes in
`~/.local/share/Paperdoll/store` on Linux (your local application data folder elsewhere), 10 to
30 MB per fork. If you have git 2.45 or newer it's used; otherwise downloads go through the GitHub
API, which allows 60 requests an hour. File > Files on this computer shows where Paperdoll keeps
its files and how much room they take, and can clean up what old fork versions leave behind.

## Building and testing

```sh
dotnet build
dotnet test
```

`tools/publish.sh` makes the builds above in `publish/`, one per system.

Some tests are skipped unless you ask for them. `PAPERDOLL_NETWORK_TESTS=1` runs the ones that
download from GitHub. Exported characters you put in `tests/private/` (ignored by git) are opened
and checked against their forks. `PAPERDOLL_SCREENSHOT_OUT=<folder>` saves a screenshot of the
window.

## Layout

- `src/Paperdoll.Core`: everything that isn't UI (fork downloads, prototypes, character rules,
  drawing, import and export)
- `src/Paperdoll.App`: the Avalonia desktop app
- `tests/`: unit tests, and tests that drive the window headlessly

## Licence

MIT, see [LICENSE](LICENSE). Some logic is ported from Space Station 14 and RobustToolbox, which
are MIT too; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with Space Wizards or any fork. Sprites are downloaded from each fork when needed
and keep their own licences, listed in the Credits tab.
