# HexWin

Local voice dictation for Windows. Hold **`Ctrl` + `Windows`**, speak, release —
the text lands at your cursor.

Everything runs **on your machine**. No cloud service, no subscription, no
network connection needed once the model is downloaded.

![Architecture](docs/architecture.png)

## How fast it is

Measured on a Dell Pro Max 16 (Core Ultra 7 255H), on CPU:

| Speech length | Wait after release |
|---------------|--------------------|
| 0.6 s | 0.05 s |
| 1.6 s | 0.08 s |
| 5 s | 0.19 s |
| 40 s | 1.58 s |

The engine is **Parakeet TDT v3** by NVIDIA — the same one
[Hex](https://github.com/kitlangton/Hex) uses on macOS. It identifies the spoken
language on its own among 25 European languages, so there is no language setting
to get wrong.

Whisper was tried first here, and dropped after measurement. It is an
autoregressive encoder-decoder, which imposes a **fixed cost of about 1.4 s per
transcription** regardless of how short the audio is — and on a one-sentence
dictation, the normal case, that cost is paid in full. Parakeet is a transducer
and has no such bottleneck. Same recordings, Parakeet on CPU against Whisper on
GPU: **2.29 s to 0.19 s** on a five-second clip.

## Install

### If you would rather not compile anything

Download the archive from the [latest release](https://github.com/legb78/hex_windows/releases),
unzip it, then in PowerShell:

```powershell
.\get-model.ps1     # about 480 MB, once
.\HexWin.exe
```

Nothing else to install — not even .NET, which is bundled inside the executable.

> **Windows will warn you the first time.** The executable is not code-signed,
> so SmartScreen shows "Windows protected your PC". Click **More info**, then
> **Run anyway**. That is expected for an unsigned binary downloaded from the
> internet; the alternative is a paid signing certificate.
>
> Some antivirus products flag the app too, because it installs a **global
> keyboard hook**. That hook is what makes push-to-talk work at all: HexWin has
> to see your hotkey whichever window has focus. It looks at nothing else and
> stores no keystrokes. The code responsible is
> [KeyboardHook.cs](src/HexWin/Input/KeyboardHook.cs) — short, and worth reading
> if you would rather check than trust.

### If you compile it yourself

You need the **.NET 9 SDK** (<https://dotnet.microsoft.com/download>).

```powershell
.\scripts\get-model.ps1     # downloads the model
dotnet build -c Release
.\src\HexWin\bin\x64\Release\net9.0-windows\HexWin.exe
```

## Using it

The tray icon shows the current state:

| Colour | State |
|--------|-------|
| Grey | Loading the model, a few seconds |
| Blue | Ready |
| Red | Recording |
| Orange | Transcribing |
| Crossed grey | Model not found |

Hold `Ctrl` + `Windows`, speak, release. The text arrives at your cursor.

Right-clicking the icon opens the settings file and the log folder, and offers a
**start with Windows** toggle. Worth enabling: the app does not come back on its
own after a reboot otherwise.

## Settings

Everything lives in `settings.json`, next to the executable. Comments are
allowed, and **an invalid value falls back to its default** rather than
preventing startup.

| Setting | What it does |
|---------|--------------|
| `hotkey` | Keys to hold. `Fn` cannot be used: it is handled by the keyboard controller and emits no code Windows can see. |
| `insertion` | `Paste` (clipboard, instant) or `Type` (simulated keystrokes, for apps that ignore pasting). |
| `provider` | `cpu`, the only one available: the published native libraries are built for CPU only. |
| `threads` | Threads allocated to decoding. |
| `minRecordingMilliseconds` | Below this, the keypress is treated as accidental. |
| `maxRecordingSeconds` | Stops recording if the key stays held. |
| `unloadAfterMinutes` | Frees the model after this long without dictating, reclaiming about 1 GB. `0` keeps it resident. Reloading starts when you *press* the hotkey, so it overlaps with you speaking. |

## When something goes wrong

Four diagnostic modes. Run them in this order — each isolates one layer, which
is how you find out where the fault actually is instead of guessing.

```powershell
# 1. Is the microphone picking anything up?
.\HexWin.exe --record test.wav

# 2. Does the engine transcribe that file?
.\HexWin.exe --transcribe test.wav

# 3. Does the hotkey fire?
.\HexWin.exe --watch-hotkey

# 4. Does insertion reach the target window?
.\HexWin.exe --inject "some text"
```

Start with the first. A muted microphone, or one blocked by the privacy
settings, produces a perfectly valid file of the right duration that is
completely silent — and the engine then invents a sentence from it. Without the
level reading you would go looking for the fault in the transcription, which is
the wrong end entirely.

The log lives in `%LOCALAPPDATA%\HexWin` and records, for every dictation, the
duration, **the captured level** and the number of characters produced. Those
three numbers together tell you whether the microphone heard you, whether the
engine understood you, and whether the text made it out.

### Known limits

- **Elevated windows**: a non-elevated app cannot send keystrokes to a window
  running as administrator (Windows UIPI isolation). Run HexWin elevated if you
  need to dictate into one.
- **Antivirus**: a global keyboard hook is a flagged pattern. An exclusion on
  the executable may be needed.
- **Unsigned binary**: see the SmartScreen note above.

## Development

```powershell
dotnet build -c Release     # no warnings tolerated
dotnet test                 # unit tests
```

`dotnet test` runs everything. The integration tests load the real engine, so
they **skip themselves with a message** when the model has not been downloaded
yet — a fresh clone gives you a green run and a count of what was skipped,
rather than failures that say nothing about the code.

Download the model and they run for real. CI excludes them up front, since a
runner has no reason to spend minutes discovering they would skip.

### How the code is organised

The architecture deliberately separates two layers, for testability (see the
diagram above; editable source in [docs/architecture.excalidraw](docs/architecture.excalidraw)):

- **The Windows shells** (`KeyboardHook`, `AudioRecorder`, `ParakeetEngine`,
  `TextInjector`) wire up system APIs and decide nothing. They cannot be tested
  automatically — a CI runner has no microphone and no interactive session, and
  Windows marks program-generated keystrokes as injected, which the hook ignores
  on purpose. They are verified by hand.
- **The pure logic** (`ChordDetector`, `RecordingGuards`, `TranscriptCleaner`,
  `AppSettings`, `DictationCoordinator`, `IdlePolicy`) holds every decision and
  is tested without Windows. This is where the expensive bugs live: keyboard
  auto-repeat, keys released out of order, a key left stuck after a session
  lock, a second dictation triggered while one is still transcribing.

If you add a decision, it belongs in the pure layer.

### Contributing

Two long-lived branches:

| Branch | Role |
|--------|------|
| `main` | Published releases. Only ever advances from `develop`. |
| `develop` | Integration. Work branches are merged here. |

One branch per change, created **from `develop`** and merged back into it:
`feat/`, `fix/`, `chore/`, `ci/`, `docs/`. Messages follow
[Conventional Commits](https://www.conventionalcommits.org/), squash-merged.

```powershell
git checkout develop
git pull
git checkout -b feat/my-topic
gh pr create --base develop
```

Full details in [CONTRIBUTING.md](CONTRIBUTING.md).

### Publishing a release

```powershell
.\scripts\publish.ps1        # builds the self-contained executable locally
```

`Directory.Build.props` holds `<Version>` and is the single source of truth.
Merging into `main` publishes that version, and does nothing if the tag already
exists — so releasing means bumping the number in a pull request.

## Licence

HexWin is released under the [Apache 2.0](LICENSE) licence.

The recognition engine is **Parakeet TDT 0.6B v3** by NVIDIA, under CC-BY-4.0:
commercial use permitted, attribution required. The other components
(sherpa-onnx, ONNX Runtime, NAudio, .NET) are Apache 2.0 or MIT.

Attributions in full: [NOTICE](NOTICE).
