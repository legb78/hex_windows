namespace HexWin.Output;

/// <summary>Une frappe unitaire à injecter : un caractère UTF-16, enfoncé ou relâché.</summary>
/// <param name="Unit">Unité de code UTF-16 envoyée telle quelle à Windows.</param>
/// <param name="IsKeyUp">Vrai pour le relâchement.</param>
public readonly record struct Keystroke(ushort Unit, bool IsKeyUp);

/// <summary>
/// Traduit un texte en frappes clavier simulées.
///
/// Le mode de repli quand le collage ne passe pas : certaines applications
/// — terminaux, machines virtuelles, jeux — ignorent le presse-papiers mais
/// acceptent les frappes.
///
/// L'envoi se fait en Unicode plutôt qu'en codes de touches : cela évite
/// complètement la question de la disposition du clavier. Sur un clavier
/// français, « a » et « q » ne sont pas là où un programme les attendrait, et
/// « é » n'a aucun code de touche sur un clavier américain. En envoyant
/// directement l'unité de code, le caractère arrive quelle que soit la
/// disposition active.
///
/// Logique pure, donc testable sans toucher au clavier.
/// </summary>
public static class UnicodeKeystrokes
{
    /// <summary>
    /// Construit la suite de frappes correspondant au texte.
    ///
    /// Chaque caractère donne deux frappes — enfoncement puis relâchement.
    /// Les caractères hors du plan multilingue de base, émojis compris,
    /// occupent deux unités UTF-16 qui doivent être envoyées séparément :
    /// Windows les recompose de lui-même.
    /// </summary>
    public static Keystroke[] Build(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var strokes = new List<Keystroke>(text.Length * 2);

        foreach (char unit in text)
        {
            // Les fins de ligne demandent la touche Entrée, pas un caractère :
            // envoyées en Unicode, elles n'insèrent rien du tout.
            if (unit == '\n')
            {
                strokes.Add(new Keystroke(Return, IsKeyUp: false));
                strokes.Add(new Keystroke(Return, IsKeyUp: true));
                continue;
            }

            // Le retour chariot d'une fin de ligne Windows est ignoré : le
            // saut est déjà produit par le caractère de nouvelle ligne.
            if (unit == '\r')
            {
                continue;
            }

            strokes.Add(new Keystroke(unit, IsKeyUp: false));
            strokes.Add(new Keystroke(unit, IsKeyUp: true));
        }

        return [.. strokes];
    }

    /// <summary>
    /// Code de la touche Entrée. Contrairement aux autres frappes, celle-ci
    /// est un code de touche virtuel et non une unité de code Unicode.
    /// </summary>
    public const ushort Return = 0x0D;

    /// <summary>Vrai si la frappe désigne la touche Entrée plutôt qu'un caractère.</summary>
    public static bool IsReturn(Keystroke stroke) => stroke.Unit == Return;
}
