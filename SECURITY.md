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
- The Forks window reads, read-only, which forks the SS14 launcher on the computer has run and
  when, from its `content.db`. It opens no other launcher file and keeps or sends nothing it
  reads.
- A fork can be read from a game folder you choose, a copy or git clone of the game. Paperdoll
  only reads it: it never writes, creates or deletes anything there and never runs git in it.
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
