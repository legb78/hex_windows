using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HexWin.Tray;

/// <summary>
/// Dessine les icônes de la barre système, une par état.
///
/// Elles sont produites par le programme plutôt que livrées en fichiers : ce
/// sont des pastilles de couleur, et les embarquer comme ressources
/// n'apporterait rien qu'un fichier binaire de plus à versionner.
///
/// L'état doit se lire d'un coup d'œil, à seize pixels de côté et sans
/// couleur fiable — la barre système peut être claire ou sombre. D'où des
/// teintes franchement distinctes plutôt que des nuances.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille GDI+ : produit des ressources graphiques du système.")]
internal sealed partial class TrayIcons : IDisposable
{
    private const int Size = 32;

    private readonly Dictionary<DictationState, Icon> _icons = [];

    public TrayIcons()
    {
        _icons[DictationState.Loading] = Build(Color.FromArgb(134, 142, 150));      // gris
        _icons[DictationState.Idle] = Build(Color.FromArgb(25, 113, 194));          // bleu
        _icons[DictationState.Recording] = Build(Color.FromArgb(224, 49, 49));      // rouge
        _icons[DictationState.Transcribing] = Build(Color.FromArgb(232, 89, 12));   // orange
        _icons[DictationState.Failed] = Build(Color.FromArgb(64, 64, 64), cross: true);
    }

    public Icon this[DictationState state] => _icons[state];

    private static Icon Build(Color color, bool cross = false)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 2, 2, Size - 5, Size - 5);

            if (cross)
            {
                using var pen = new Pen(Color.White, 4);
                graphics.DrawLine(pen, 11, 11, Size - 12, Size - 12);
                graphics.DrawLine(pen, Size - 12, 11, 11, Size - 12);
            }
        }

        nint handle = bitmap.GetHicon();

        try
        {
            // Icon.FromHandle ne s'approprie pas la poignée : on la duplique
            // pour pouvoir libérer l'originale sans invalider l'icône.
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        foreach (Icon icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
