# Security Policy

## Supported versions

Only the [latest release](https://github.com/legb78/hex_windows/releases/latest)
is supported. Fixes go out as a new version; older ones are not patched.

## Reporting a vulnerability

**Do not open a public issue.** Use
[Report a vulnerability](https://github.com/legb78/hex_windows/security/advisories/new)
in the Security tab. The report stays private between you and the maintainer
until a fix ships.

Expect a first reply within a week. This is a one-person project run on spare
time, so there is no bounty and no guaranteed turnaround — but a real report
will be taken seriously, and you will be credited in the advisory unless you
ask otherwise.

Useful in a report: the version, what an attacker gains, and the shortest path
you know to reproduce it.

## What is worth reporting

HexWin listens to every keystroke and speaks to the clipboard, so its surface
is worth stating plainly:

- The **global keyboard hook** sees all key events, including those typed into
  other applications. Anything that lets it leak, log, or forward them.
- The **clipboard path** used by `Paste` insertion. Dictations are kept out of
  clipboard history on purpose; a way around that is a bug worth reporting.
- **Audio and transcripts**, which are supposed to stay on the machine. Any
  path that sends them elsewhere.
- **`scripts/get-model.ps1`**, which downloads the recognition model. Anything
  that lets a different payload take its place.
- **Privilege or path handling** around the published executable and its
  settings file.

## What is not a vulnerability

- The **SmartScreen warning** on first launch. The executable is unsigned; this
  is documented in the README, not a flaw to be reported.
- **Antivirus flagging the keyboard hook.** A global hook is a pattern scanners
  watch for, by design.
- The inability to insert text into **elevated windows**. That is Windows UIPI
  doing its job.
