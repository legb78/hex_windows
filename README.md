# HexWin

Dictée vocale locale pour Windows. On maintient **`Ctrl` + `Windows`**, on parle,
on relâche : le texte s'insère dans le champ actif.

Tout tourne **sur la machine** — aucun envoi vers un service en ligne, aucun
abonnement, aucune connexion nécessaire une fois le modèle téléchargé.

![Architecture](docs/architecture.png)

## État

En cours de construction. Ce que chaque étape apporte :

| PR | Contenu | État |
|----|---------|------|
| 1 | Socle du projet et CI | ✅ |
| 2 | Configuration (`settings.json`) | ✅ |
| 3 | Moteur de transcription Parakeet | ✅ |
| 4 | Capture du micro | ✅ |
| 5 | Raccourci clavier global | ⏳ |
| 6 | Insertion du texte | ⏳ |
| 7 | Icône de barre système, application complète | ⏳ |
| 8 | Exécutable téléchargeable | ⏳ |

## Installation

> Rien de tout ceci ne demande de savoir programmer. Trois commandes, une fois.

### 1. Le SDK .NET

Vérifier qu'il est présent :

```powershell
dotnet --version
```

Si la commande est inconnue, installer le **SDK .NET 9** depuis
<https://dotnet.microsoft.com/download>.

### 2. Télécharger le modèle

```powershell
.\scripts\get-model.ps1
```

Environ 480 Mo à télécharger, 578 Mo une fois installés. C'est
**Parakeet TDT v3** de NVIDIA — le même moteur que Hex sur macOS. Il reconnaît
seul la langue parlée parmi 25 langues européennes, français compris : il n'y
a aucun réglage de langue à faire.

### 3. Compiler

```powershell
dotnet build -c Release
```

### Vérifier que tout fonctionne

```powershell
# transcrire un fichier et mesurer le temps
.\src\HexWin\bin\x64\Release\net9.0-windows\HexWin.exe --transcribe mon-fichier.wav

# enregistrer 5 s au micro et mesurer le niveau capté
.\src\HexWin\bin\x64\Release\net9.0-windows\HexWin.exe --record test.wav
```

Le second est le premier réflexe quand la dictée ne rend rien : un micro coupé
ou interdit par les réglages de confidentialité produit un fichier valide et
parfaitement silencieux.

### 4. Lancer

L'exécutable est produit dans `src/HexWin/bin/x64/Release/net9.0-windows/HexWin.exe`.

## Développement

```powershell
dotnet build -c Release     # compiler (aucun avertissement toléré)
dotnet test                 # tests unitaires
```

Les tests d'intégration exigent le modèle Parakeet et ne tournent pas en CI :

```powershell
dotnet test --filter Category=Integration
```

### Organisation du code

L'architecture sépare délibérément deux couches, pour une raison de testabilité
(voir le schéma ci-dessus, source éditable dans [docs/architecture.excalidraw](docs/architecture.excalidraw)) :

- **Les coquilles Windows** (`KeyboardHook`, `AudioRecorder`, `ParakeetEngine`,
  `TextInjector`) branchent des API système et ne décident rien. Elles ne sont
  pas testables en automatique — pas de micro ni de session interactive sur un
  serveur d'intégration continue — et se vérifient à la main.
- **La logique pure** (`ChordDetector`, `RecordingGuards`, `TranscriptCleaner`,
  `AppSettings`) contient toutes les décisions, et se teste sans Windows. C'est
  là que vivent les bugs coûteux : répétition automatique du clavier,
  relâchement de touche dans le désordre, touche restée enfoncée après un
  verrouillage de session.

### Contribution

Deux branches au long cours :

| Branche | Rôle |
|---------|------|
| `main` | Versions publiées. N'avance que depuis `develop`, au moment d'une release. |
| `develop` | Intégration. C'est là que les branches de travail sont fusionnées. |

Une branche par changement, créée **depuis `develop`** et fusionnée vers
`develop` : `feat/`, `fix/`, `chore/`, `ci/`, `docs/`. Messages en
[Conventional Commits](https://www.conventionalcommits.org/fr/), fusion en
*squash*.

```powershell
git checkout develop
git pull
git checkout -b feat/mon-sujet
# ... travail, commits ...
gh pr create --base develop
```

La CI tourne sur les PR vers `main` comme vers `develop`.
