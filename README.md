# Paperdoll

A character editor for Space Station 14 that runs on your own computer. Make and edit characters
without joining a server, see them drawn as the game draws them, and save them as the `.yml` file
the lobby's character editor imports.

- **No server needed.** Nothing is sent anywhere, and no player slot is taken while you work.
- **Knows your fork.** Species, markings, colours and loadouts come from the fork's own files on
  GitHub, so the options match what the game offers. Delta-V first, then upstream.
- **Uses the game's own format.** Export a character here, then use **Import** in the lobby editor.

Status: early, not usable yet. See [docs/PLAN.md](docs/PLAN.md) and
[docs/RESEARCH.md](docs/RESEARCH.md).

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
  `tests/private/`, which git ignores, and are skipped when the files are missing.
- `docs/`: the plan and research notes.

## Licence

Paperdoll's code is MIT (see [LICENSE](LICENSE)).

Not affiliated with Space Wizards or any fork. Game sprites are downloaded from each fork's
repository when needed and keep their own licences, shown with each sprite.
