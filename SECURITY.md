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
- It never uploads or shares characters, and never sends usage data or crash reports.

Fixes go into the next release; only the latest release is supported.
