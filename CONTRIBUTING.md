# Contributing to HexWin

Thanks for the interest. This is what to know before opening a pull request.

## Setting up

You need the **.NET 9 SDK** and Windows. The `net9.0-windows` target uses
Windows Forms and the Win32 API, so the project does not build anywhere else.

```powershell
.\scripts\get-model.ps1     # recognition model, about 480 MB
dotnet build -c Release
dotnet test
```

## How the code is split, and why

The architecture separates two layers, for testability.

**The Windows shells** — `KeyboardHook`, `AudioRecorder`, `ParakeetEngine`,
`TextInjector`, `RecordingOverlay`, `CueTones` — wire up system APIs and decide
nothing. They cannot be tested
automatically: a CI runner has no microphone and no interactive session, and
Windows marks program-generated keystrokes as injected, which the hook ignores
on purpose.

**The pure logic** — `ChordDetector`, `RecordingGuards`, `TranscriptCleaner`,
`AppSettings`, `DictationCoordinator`, `IdlePolicy`, `FeedbackPolicy` — holds
every decision and
is tested without Windows.

> **If you add a decision, it belongs in the pure layer.** That is where the
> expensive bugs live: keyboard auto-repeat, keys released out of order, a key
> left stuck after a session lock, a second dictation triggered while one is
> still transcribing. Each of those is painful to reproduce by hand and trivial
> to describe as a test.

## Tests

```powershell
dotnet test                                  # everything
dotnet test --filter Category=Integration    # the engine ones only
```

The integration tests load the real engine. Without the model on disk they
**skip themselves with a message** rather than fail, so a fresh clone gives a
green run: nobody has to tell real failures apart from a missing 578 MB
download. Fetch the model and they run for real.

CI excludes them up front — a runner has no reason to spend minutes discovering
they would skip.

A test should explain **why** the case matters, not only what it checks. One
line recalling the real situation it covers is worth more than a long name.

## Verifying what cannot be tested

Any change touching the keyboard, the microphone, insertion or the tray needs a
manual check. The diagnostic modes exist for that:

```powershell
.\HexWin.exe --record test.wav       # is the microphone picking anything up?
.\HexWin.exe --transcribe test.wav   # does the engine transcribe?
.\HexWin.exe --watch-hotkey          # does the hotkey fire?
.\HexWin.exe --inject "some text"    # does insertion land?
.\HexWin.exe --test-feedback         # do the start and end cues fire?
```

Describe in the pull request what you tried and what you observed.

## Git flow

| Branch | Role |
|--------|------|
| `main` | Published releases. Only ever advances from `develop`. |
| `develop` | Integration. |

Both are protected: no direct pushes, no force pushes, no deletion, and a pull
request with green CI is required. There is no bypass, for anyone.

One branch per change, created from `develop` and merged back into it.

```powershell
git checkout develop
git pull
git checkout -b feat/my-topic
gh pr create --base develop
```

Prefixes: `feat/`, `fix/`, `chore/`, `ci/`, `docs/`.

## Commit messages

[Conventional Commits](https://www.conventionalcommits.org/), in English.

```
feat(audio): capture the microphone through NAudio

The body explains WHY, not what — the diff already says what. What was tried,
what failed, the constraint that forced this solution over another.
```

## What the build enforces

`TreatWarningsAsErrors` is on: **a single warning fails the build**, locally and
in CI. This is not negotiable. It is what keeps the code clean without anyone
having to think about it.

## Releasing

`Directory.Build.props` holds `<Version>` and is the single source of truth.
Merging into `main` publishes that version and does nothing if the tag already
exists, so releasing means bumping the number in a pull request.

## Licence

By contributing, you agree that your contribution is distributed under the
project's Apache 2.0 licence.
