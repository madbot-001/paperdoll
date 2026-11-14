# Security

If you find a way Paperdoll could harm someone's computer or data, please report it privately
rather than in a public issue: on this repository's Security tab, choose "Report a
vulnerability". For example: a file that makes it crash, hang or write somewhere it shouldn't,
or a way to make it connect somewhere it shouldn't.

Please don't include anyone's real character file unless its owner agrees. A made-up character
that shows the problem is best.

What Paperdoll is meant to do, to compare a problem against:

- It reads character files as data only. Nothing in one is run, used as a file path or passed to
  git, and files far larger than any export are refused unread.
- It connects only to GitHub, to download game data, and, for Fork > Match a Server, to the
  Space Station 14 hub for its list of servers and to the server you pick, to read which
  version it runs from its public info page.
- Of the SS14 launcher's files, Paperdoll opens only `content.db`, read-only and with SQLite's
  `immutable=1`, so it takes no lock and makes no `-wal`, `-shm` or `-journal` file beside it.
  It never writes, creates or deletes anything in the launcher's folders, and never opens the
  launcher's `settings.db` (which holds its login), `override_assets.db`, its logs or the game's
  `data` folder. It opens `content.db` only when the Forks window lists the forks (to show when
  the launcher last ran each one and which builds it holds) and when you copy a fork from the
  launcher (From launcher... in the Forks window, SS14 launcher at first start, or Update on a
  fork copied from it). At first start it only checks whether `content.db` is there, without
  opening it.
- In `content.db` it reads only the `ContentVersion` table (`Id`, `Hash`, `ForkId`,
  `ForkVersion`, `LastUsed`), the `ContentManifest` table (`Id`, `VersionId`, `Path`,
  `ContentId`) and the `Content` table (`Id`, `Hash`, `Size`, `Compression`, `Data`), never
  `RunningClient` or `InterruptedDownload`. Before reading builds, it checks from SQLite's own
  description of those tables that they have those columns, of the types the launcher gives
  them, and refuses a file of another shape.
- Copying a fork takes the files of one build under `Prototypes`, `Locale/en-US` and
  `Textures` into the `launcher` folder of Paperdoll's own store, and writes nowhere else: every
  file written is checked to be inside that folder first, and none is written over. Each file is
  decompressed, stopping one byte past the size recorded for it, and checked against its
  BLAKE2b-256 hash. Paths are refused when rooted or holding `..`, a backslash, a colon, a
  control character or a name Windows keeps for a device; files over 32 MB, more than 2 GB in
  all, packed sprites over 16384 pixels a side or 32 million pixels, and meta.json files over
  1 MB are refused too. The copy also stops when the number of files read differs from the
  number the build lists, or when the size or last-change time of `content.db` after the copy
  differs from before it. Any of these stops the copy, and what was copied of it is deleted; a
  copy from before is kept. A copy records how many files and bytes it holds, and one that no
  longer adds up is never read. A copied fork is then read from Paperdoll's copy as a game folder
  is, without the launcher, and Remove deletes only that copy. Nothing read from the launcher is
  sent anywhere.
- A fork can be read from a game folder you choose, a copy or git clone of the game. Paperdoll
  only reads it: it never writes, creates or deletes anything there and never runs git in it.
  A folder inside Paperdoll's own store, or holding it, is refused.
  It reads the game's files under `Resources`, and from `.git` only the files that say which
  commit is checked out and the remote's address. When `.git` is a file naming a git folder elsewhere, as in a git
  worktree, those two are read from there, as git itself does. Before each listing and each read
  it checks the path for links and follows none inside the folder; a link another program puts
  in place in the instant between that check and the listing or read can still be followed that
  once, and such a read still gets at most one plain file of up to 32 MB, and a listing only
  names, sizes and dates. Files larger than 32 MB are refused unread, and a file that does not
  open within 15 seconds is given up on.
- It never uploads or shares characters, and never sends usage data or crash reports.

Fixes go into the next release; only the latest release is supported.
