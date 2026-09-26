<img src="src/Paperdoll.App/Assets/icon.svg" width="72" alt="">

# Paperdoll

A character editor for Space Station 14 that runs on your own computer. Make and edit characters
without joining a server, see them drawn as the game draws them, and save them as the `.yml` file
the lobby's character editor imports.

- **No server needed.** No player slot is taken while you work.
- **Your characters stay on your computer.** Paperdoll only downloads game data from GitHub. It
  never uploads, shares or collects character files, and it never reads other players' characters.
- **Knows your fork.** Species, markings, colours and loadouts come from the fork's own files on
  GitHub, so the options match what the game offers. Delta-V first, then upstream.
- **Uses the game's own format.** Export a character here, then use **Import** in the lobby editor.

Status: early. Delta-V and upstream (and other forks on the new appearance model) can be
downloaded, drawn facing any way, edited and exported.

The first start opens Fork > Forks: pick a fork and press Download. Fork files are kept in
your local application data folder (`~/.local/share/Paperdoll/store` on Linux), about 10 to 30 MB
per fork, and files forks share are kept once. With git 2.45 or newer installed Paperdoll uses it;
otherwise it downloads through the GitHub API, which allows only 60 requests an hour.

## Building

Needs the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/Paperdoll.App
```

## Layout

- `src/Paperdoll.Core`: everything that is not UI (downloading fork data, reading prototypes,
  character rules, import and export).
- `src/Paperdoll.App`: the desktop program (Avalonia).
- `tests/Paperdoll.Core.Tests`: tests. Tests that need private character files look in
  `tests/private/`, which git ignores, and are skipped when the files are missing. Tests that
  download from GitHub run only with `PAPERDOLL_NETWORK_TESTS=1`.
- `tests/Paperdoll.App.Tests`: a headless screenshot of the window, taken when
  `PAPERDOLL_SCREENSHOT_OUT` names a folder.

## Licence

Paperdoll's code is MIT (see [LICENSE](LICENSE)).

Not affiliated with Space Wizards or any fork. Game sprites are downloaded from each fork's
repository when needed and keep their own licences, shown with each sprite.
