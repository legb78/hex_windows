---
name: Anomalie
about: Signaler un dysfonctionnement
labels: bug
---

## Ce qui se passe

<!-- Ce que vous observez, et ce que vous attendiez à la place. -->

## Résultat des diagnostics

HexWin embarque un mode par couche. Lancez-les dans cet ordre et collez ce
qu'ils affichent : c'est ce qui permet de savoir de quel côté chercher.

```powershell
# 1. Le micro capte-t-il quelque chose ?
.\HexWin.exe --record test.wav
```

```
<!-- coller la sortie -->
```

```powershell
# 2. Le moteur transcrit-il ce fichier ?
.\HexWin.exe --transcribe test.wav
```

```
<!-- coller la sortie -->
```

```powershell
# 3. Le raccourci se déclenche-t-il ?
.\HexWin.exe --watch-hotkey
```

```
<!-- coller la sortie -->
```

## Journal

Contenu de `%LOCALAPPDATA%\HexWin\hexwin.log`, et de `crash.log` s'il existe.

```
<!-- coller les dernières lignes -->
```

## Environnement

- Version de Windows :
- Modèle de machine :
- Processeur :
- `settings.json` (sans donnée personnelle) :
