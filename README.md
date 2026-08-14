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
| 2 | Configuration (`settings.json`) | ⏳ |
| 3 | Moteur de transcription Whisper | ⏳ |
| 4 | Capture du micro | ⏳ |
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

### 2. Compiler

```powershell
dotnet build -c Release
```

### 3. Lancer

L'exécutable est produit dans `src/HexWin/bin/x64/Release/net9.0-windows/HexWin.exe`.

## Développement

```powershell
dotnet build -c Release     # compiler (aucun avertissement toléré)
dotnet test                 # tests unitaires
```

Les tests d'intégration exigent un modèle Whisper et ne tournent pas en CI :

```powershell
dotnet test --filter Category=Integration
```

### Organisation du code

L'architecture sépare délibérément deux couches, pour une raison de testabilité
(voir le schéma ci-dessus, source éditable dans [docs/architecture.excalidraw](docs/architecture.excalidraw)) :

- **Les coquilles Windows** (`KeyboardHook`, `AudioRecorder`, `WhisperEngine`,
  `TextInjector`) branchent des API système et ne décident rien. Elles ne sont
  pas testables en automatique — pas de micro ni de session interactive sur un
  serveur d'intégration continue — et se vérifient à la main.
- **La logique pure** (`ChordDetector`, `RecordingGuards`, `TranscriptCleaner`,
  `AppSettings`) contient toutes les décisions, et se teste sans Windows. C'est
  là que vivent les bugs coûteux : répétition automatique du clavier,
  relâchement de touche dans le désordre, touche restée enfoncée après un
  verrouillage de session.

### Contribution

`main` reste toujours verte. Une branche par changement (`feat/`, `fix/`,
`chore/`, `ci/`, `docs/`), messages en
[Conventional Commits](https://www.conventionalcommits.org/fr/), fusion en
*squash*.
