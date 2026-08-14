namespace HexWin.Input;

/// <summary>Ce que le raccourci demande à l'application de faire.</summary>
public enum ChordAction
{
    None,

    /// <summary>Toutes les touches sont enfoncées : démarrer l'enregistrement.</summary>
    Start,

    /// <summary>Une touche a été relâchée : transcrire ce qui a été dit.</summary>
    Stop,

    /// <summary>Une touche étrangère est intervenue : abandonner sans transcrire.</summary>
    Cancel,
}

/// <summary>Décision prise pour un événement clavier.</summary>
/// <param name="Action">Ce que l'application doit faire.</param>
/// <param name="Swallow">
/// Si vrai, l'événement ne doit pas être transmis à Windows.
/// </param>
/// <param name="NeutralizeStartMenu">
/// Si vrai, l'appelant doit injecter une touche neutre pour empêcher
/// l'ouverture du menu Démarrer. Voir la remarque sur l'ordre d'appui.
/// </param>
public readonly record struct ChordDecision(
    ChordAction Action,
    bool Swallow,
    bool NeutralizeStartMenu = false)
{
    public static readonly ChordDecision Ignore = new(ChordAction.None, Swallow: false);
}

/// <summary>
/// Machine à états du raccourci « maintenir pour dicter ».
///
/// Logique entièrement pure : elle ne connaît ni Win32, ni le clavier, elle
/// se contente de recevoir des codes de touches et de décider. C'est ce qui
/// permet de la tester, alors que le hook lui-même ne l'est pas.
///
/// <para><b>La règle qui gouverne tout : l'avalage doit être équilibré.</b>
/// Un événement d'enfoncement avalé impose d'avaler le relâchement
/// correspondant, et réciproquement. Rompre cet équilibre laisse Windows
/// convaincu qu'une touche modificatrice est toujours enfoncée : le clavier
/// devient inutilisable jusqu'à ce qu'on represse la touche fantôme.</para>
///
/// <para>Sur le menu Démarrer : Windows l'ouvre quand la touche Windows est
/// pressée puis relâchée sans qu'aucune autre touche n'intervienne. Comme on
/// avale la touche qui complète le raccourci, Windows ne voit jamais cet
/// appui — sauf si l'utilisateur a pressé Windows <i>en premier</i>, auquel
/// cas l'appui a déjà été transmis. D'où
/// <see cref="ChordDecision.NeutralizeStartMenu"/>, qui demande alors à
/// l'appelant d'injecter une touche sans effet.</para>
/// </summary>
public sealed class ChordDetector
{
    /// <summary>
    /// Codes acceptés pour chaque touche du raccourci. Un « slot » par touche
    /// demandée ; « Ctrl » accepte la touche gauche comme la droite.
    /// </summary>
    private readonly int[][] _requirements;

    /// <summary>Code réellement enfoncé qui satisfait chaque slot, ou 0.</summary>
    private readonly int[] _satisfiedBy;

    /// <summary>Touches dont on a avalé l'enfoncement.</summary>
    private readonly HashSet<int> _swallowed = [];

    public ChordDetector(IEnumerable<string> keyNames)
    {
        ArgumentNullException.ThrowIfNull(keyNames);

        _requirements = [.. keyNames.Select(VirtualKeys.Resolve)];

        if (_requirements.Length == 0)
        {
            throw new ArgumentException("Le raccourci doit comporter au moins une touche.", nameof(keyNames));
        }

        _satisfiedBy = new int[_requirements.Length];
    }

    /// <summary>Vrai entre le moment où le raccourci est complet et son relâchement.</summary>
    public bool IsActive { get; private set; }

    public ChordDecision OnKeyDown(int virtualKey)
    {
        // Répétition automatique : maintenir une touche envoie des
        // enfoncements en rafale. Ils ne doivent surtout pas relancer
        // l'enregistrement, mais doivent rester avalés si l'appui initial
        // l'a été.
        if (IsAlreadySatisfying(virtualKey))
        {
            return new ChordDecision(ChordAction.None, _swallowed.Contains(virtualKey));
        }

        int slot = FindFreeSlot(virtualKey);

        if (slot < 0)
        {
            // Touche étrangère au raccourci. Pendant une dictée, elle
            // l'interrompt : Ctrl+Win+D crée un bureau virtuel, l'utilisateur
            // ne demandait pas une transcription.
            if (IsActive)
            {
                IsActive = false;
                return new ChordDecision(ChordAction.Cancel, Swallow: false);
            }

            return ChordDecision.Ignore;
        }

        _satisfiedBy[slot] = virtualKey;

        if (!AllSatisfied() || IsActive)
        {
            // Raccourci encore incomplet : on laisse passer. Avaler ici
            // casserait Ctrl+C, puisqu'on ne peut pas encore savoir si
            // l'utilisateur vise notre raccourci.
            return ChordDecision.Ignore;
        }

        IsActive = true;
        _swallowed.Add(virtualKey);

        return new ChordDecision(
            ChordAction.Start,
            Swallow: true,
            NeutralizeStartMenu: WindowsKeyAlreadyDelivered(virtualKey));
    }

    public ChordDecision OnKeyUp(int virtualKey)
    {
        bool swallow = _swallowed.Remove(virtualKey);
        int slot = FindSatisfiedSlot(virtualKey);

        if (slot < 0)
        {
            return new ChordDecision(ChordAction.None, swallow);
        }

        _satisfiedBy[slot] = 0;

        if (!IsActive)
        {
            return new ChordDecision(ChordAction.None, swallow);
        }

        IsActive = false;
        return new ChordDecision(ChordAction.Stop, swallow);
    }

    /// <summary>
    /// Oublie tout état en cours.
    ///
    /// Indispensable quand Windows cesse de livrer les relâchements : session
    /// verrouillée, changement d'utilisateur, boîte de dialogue élevée qui
    /// prend le clavier. Sans remise à zéro, une touche resterait
    /// éternellement « enfoncée » et le raccourci ne répondrait plus.
    /// </summary>
    public ChordDecision Reset()
    {
        bool wasActive = IsActive;

        Array.Clear(_satisfiedBy);
        _swallowed.Clear();
        IsActive = false;

        return wasActive
            ? new ChordDecision(ChordAction.Cancel, Swallow: false)
            : ChordDecision.Ignore;
    }

    private bool IsAlreadySatisfying(int virtualKey) =>
        Array.IndexOf(_satisfiedBy, virtualKey) >= 0;

    private int FindFreeSlot(int virtualKey)
    {
        for (int i = 0; i < _requirements.Length; i++)
        {
            if (_satisfiedBy[i] == 0 && Array.IndexOf(_requirements[i], virtualKey) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSatisfiedSlot(int virtualKey) => Array.IndexOf(_satisfiedBy, virtualKey);

    private bool AllSatisfied() => Array.IndexOf(_satisfiedBy, 0) < 0;

    /// <summary>
    /// Vrai si une touche Windows du raccourci a été transmise à Windows
    /// avant que le raccourci ne soit complet — c'est-à-dire si
    /// l'utilisateur l'a pressée en premier.
    /// </summary>
    private bool WindowsKeyAlreadyDelivered(int completingKey) =>
        _satisfiedBy.Any(key => key != 0 && key != completingKey && VirtualKeys.IsWindowsKey(key));
}
