---
name: Bug report
about: Report something not working
labels: bug
---

## What happens

<!-- What you observe, and what you expected instead. -->

## Diagnostic output

HexWin ships one diagnostic mode per layer. Run them in this order and paste
what they print: that is what tells us which layer to look at, instead of
guessing.

```powershell
# 1. Is the microphone picking anything up?
.\HexWin.exe --record test.wav
```

```
<!-- paste the output -->
```

```powershell
# 2. Does the engine transcribe that file?
.\HexWin.exe --transcribe test.wav
```

```
<!-- paste the output -->
```

```powershell
# 3. Does the hotkey fire?
.\HexWin.exe --watch-hotkey
```

```
<!-- paste the output -->
```

## Log

Contents of `%LOCALAPPDATA%\HexWin\hexwin.log`, and `crash.log` if it exists.

Each dictation records its duration, the **captured audio level** and the number
of characters produced. Those three numbers together usually locate the fault.

```
<!-- paste the last lines -->
```

## Environment

- Windows version:
- Machine model:
- CPU:
- `settings.json` (with anything personal removed):
