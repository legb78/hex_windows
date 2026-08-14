using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace HexWin.Output;

/// <summary>
/// Copie du presse-papiers, à restaurer après un collage.
///
/// <para><b>Pourquoi une copie et non une référence.</b>
/// <c>Clipboard.GetDataObject()</c> ne rend pas les données mais un objet qui
/// pointe vers le contenu courant du presse-papiers. Dès qu'on écrit par
/// dessus, cette référence ne désigne plus rien : la « restaurer » vide le
/// presse-papiers au lieu de le rétablir. Il faut donc extraire les données
/// tant qu'elles sont encore là.</para>
///
/// <para>Trois formes sont couvertes — texte, image, liste de fichiers — ce
/// qui recouvre l'essentiel des usages. Un format exotique n'est pas
/// sauvegardé : mieux vaut ne rien restaurer que d'écrire n'importe quoi.</para>
/// </summary>
internal sealed class ClipboardSnapshot
{
    private readonly string? _text;
    private readonly Image? _image;
    private readonly StringCollection? _files;

    private ClipboardSnapshot(string? text, Image? image, StringCollection? files)
    {
        _text = text;
        _image = image;
        _files = files;
    }

    /// <summary>Vrai si quelque chose a pu être copié, donc restauré ensuite.</summary>
    public bool HasContent => _text is not null || _image is not null || _files is not null;

    /// <summary>
    /// Extrait le contenu courant du presse-papiers. Rend un instantané vide
    /// si le presse-papiers est verrouillé par une autre application : la
    /// dictée reste prioritaire sur la restauration.
    /// </summary>
    public static ClipboardSnapshot Capture()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                return new ClipboardSnapshot(Clipboard.GetText(), null, null);
            }

            if (Clipboard.ContainsImage())
            {
                return new ClipboardSnapshot(null, Clipboard.GetImage(), null);
            }

            if (Clipboard.ContainsFileDropList())
            {
                return new ClipboardSnapshot(null, null, Clipboard.GetFileDropList());
            }
        }
        catch (ExternalException)
        {
            // Presse-papiers occupé par une autre application.
        }

        return new ClipboardSnapshot(null, null, null);
    }

    /// <summary>
    /// Réécrit le contenu sauvegardé. Sans effet si rien n'avait pu l'être.
    /// </summary>
    public void Restore()
    {
        if (!HasContent)
        {
            return;
        }

        try
        {
            if (_files is not null)
            {
                Clipboard.SetFileDropList(_files);
                return;
            }

            object? content = (object?)_text ?? _image;

            if (content is not null)
            {
                // copy: true demande à Windows de conserver les données après
                // la fin du processus. Sans ce drapeau, le presse-papiers de
                // l'utilisateur se viderait à la fermeture de l'application —
                // ce qui est exactement ce qu'on cherchait à éviter.
                Clipboard.SetDataObject(content, copy: true);
            }
        }
        catch (ExternalException)
        {
            // Presse-papiers occupé : on renonce sans bruit plutôt que de
            // faire échouer une dictée par ailleurs réussie.
        }
    }
}
