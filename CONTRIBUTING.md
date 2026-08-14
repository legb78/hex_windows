# Contribuer à HexWin

Merci de l'intérêt porté au projet. Ce document décrit ce qu'il faut savoir
avant d'ouvrir une *pull request*.

## Mettre en place l'environnement

Il faut le **SDK .NET 9** et Windows : la cible `net9.0-windows` utilise
Windows Forms et l'API Win32, le projet ne compile pas ailleurs.

```powershell
.\scripts\get-model.ps1     # modèle de reconnaissance, ~480 Mo
dotnet build -c Release
dotnet test
```

## Le découpage du code, et pourquoi il est ainsi

L'architecture sépare deux couches, pour une raison de testabilité.

**Les coquilles Windows** — `KeyboardHook`, `AudioRecorder`, `ParakeetEngine`,
`TextInjector` — branchent des API système et ne décident rien. Elles ne sont
pas testables en automatique : un serveur d'intégration continue n'a ni micro,
ni session interactive, et Windows marque comme telles les frappes injectées
par un programme, que le hook ignore délibérément.

**La logique pure** — `ChordDetector`, `RecordingGuards`, `TranscriptCleaner`,
`AppSettings`, `DictationCoordinator` — contient toutes les décisions et se
teste sans Windows.

> **Si vous ajoutez une décision, elle va dans la couche pure.** C'est là que
> vivent les bugs coûteux : répétition automatique du clavier, relâchement de
> touche dans le désordre, touche restée enfoncée après un verrouillage de
> session, seconde dictée déclenchée pendant qu'une transcription tourne.
> Chacun de ces cas est pénible à provoquer à la main, et trivial à décrire
> en test.

## Les tests

```powershell
dotnet test                                  # unitaires, ce que lance la CI
dotnet test --filter Category=Integration    # charge réellement le moteur
```

Les tests d'intégration sont exclus de la CI : le modèle pèse 578 Mo. Lancez-les
localement si vous touchez au moteur.

Un test doit expliquer **pourquoi** le cas compte, pas seulement ce qu'il
vérifie. Un commentaire d'une ligne rappelant la situation réelle qu'il couvre
vaut mieux qu'un nom à rallonge.

## Vérifier ce qui n'est pas testable

Toute modification touchant au clavier, au micro, à l'insertion ou à la barre
système demande un essai manuel. Les modes de diagnostic sont là pour ça :

```powershell
.\HexWin.exe --record test.wav     # le micro capte-t-il ?
.\HexWin.exe --transcribe test.wav # le moteur transcrit-il ?
.\HexWin.exe --watch-hotkey        # le raccourci se déclenche-t-il ?
.\HexWin.exe --inject "du texte"   # l'insertion aboutit-elle ?
```

Décrivez dans la PR ce que vous avez essayé et ce que vous avez observé.

## Le flux git

| Branche | Rôle |
|---------|------|
| `main` | Versions publiées. N'avance que depuis `develop`. |
| `develop` | Intégration. |

Une branche par changement, créée depuis `develop` et fusionnée vers `develop`.

```powershell
git checkout develop
git pull
git checkout -b feat/mon-sujet
gh pr create --base develop
```

Préfixes : `feat/`, `fix/`, `chore/`, `ci/`, `docs/`.

## Les messages de commit

[Conventional Commits](https://www.conventionalcommits.org/fr/), en français.

```
feat(audio): capture du micro via NAudio

Le corps explique POURQUOI, pas quoi — le diff dit déjà quoi.
Ce qui a été essayé, ce qui a échoué, la contrainte qui a imposé
cette solution plutôt qu'une autre.
```

## Ce que la compilation impose

`TreatWarningsAsErrors` est actif : **le moindre avertissement fait échouer la
compilation**, en local comme en CI. Ce n'est pas négociable, c'est ce qui garde
le code propre sans avoir à y penser.

## Licence

En contribuant, vous acceptez que votre contribution soit distribuée sous la
licence Apache 2.0 du projet.
