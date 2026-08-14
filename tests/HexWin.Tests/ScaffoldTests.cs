using Xunit;

namespace HexWin.Tests;

/// <summary>
/// Test volontairement trivial : il ne vérifie rien du produit, il prouve que
/// la chaîne de test tourne (découverte xUnit, référence au projet, exécution
/// en CI). Il sera supprimé dès que de vrais tests le remplaceront, en PR 2.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void La_chaine_de_test_est_operationnelle()
    {
        Assert.True(true);
    }
}
