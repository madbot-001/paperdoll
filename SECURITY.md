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
- Of the SS14 launcher's files it opens only `content.db`, read-only, and only to list the
  forks in the Forks window or to copy a fork you ask for. It never opens `settings.db`, which
  holds the launcher's login, and never writes, creates or deletes anything in the launcher's
  folders. At first start it only checks whether `content.db` is there.
- A fork copied from the launcher goes into Paperdoll's own folder and nowhere else. Every file
  is checked against the hash the launcher recorded for it, and odd paths and oversized files
  are refused. If anything does not add up, the copy stops and what was copied of it is
  deleted. Remove deletes only Paperdoll's copy.
- A fork can be read from a game folder you choose, a copy or git clone of the game. Paperdoll
  only reads it: it never writes there and never runs git in it. It follows no links inside the
  folder and refuses files over 32 MB. One known limit: a link another program swaps in between
  the check and the read can still be followed that once.
- It never uploads or shares characters, and never sends usage data or crash reports.

Fixes go into the next release; only the latest release is supported.
