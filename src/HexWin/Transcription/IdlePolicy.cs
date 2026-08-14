namespace HexWin.Transcription;

/// <summary>
/// Décide quand le modèle doit être libéré faute d'usage.
///
/// Le modèle occupe environ un gigaoctet une fois chargé. Le garder résident
/// donne une réponse en quelques dizaines de millisecondes ; le libérer rend
/// cette mémoire au système au prix d'un rechargement de quelques secondes à
/// la dictée suivante.
///
/// Ce coût est largement masqué en pratique : le rechargement est déclenché
/// dès l'<i>enfoncement</i> de la touche, donc pendant que l'utilisateur
/// parle, et non au relâchement.
///
/// Logique pure : l'horloge est fournie par l'appelant, ce qui rend chaque
/// scénario descriptible en test sans attendre réellement.
/// </summary>
public readonly record struct IdlePolicy(TimeSpan Timeout)
{
    /// <summary>Un délai nul ou négatif garde le modèle résident indéfiniment.</summary>
    public bool IsEnabled => Timeout > TimeSpan.Zero;

    public static IdlePolicy FromMinutes(int minutes) =>
        new(minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero);

    /// <summary>
    /// Vrai s'il faut libérer le modèle maintenant.
    /// </summary>
    /// <param name="sinceLastUse">Temps écoulé depuis la dernière dictée.</param>
    /// <param name="isBusy">
    /// Vrai si une dictée est en cours. Libérer à cet instant ferait échouer
    /// la transcription que l'utilisateur attend — le délai d'inactivité peut
    /// tomber pile pendant qu'il parle.
    /// </param>
    public bool ShouldUnload(TimeSpan sinceLastUse, bool isBusy)
    {
        if (!IsEnabled || isBusy)
        {
            return false;
        }

        return sinceLastUse >= Timeout;
    }
}
