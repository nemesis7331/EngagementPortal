using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Le due liste base del cliente e il loro legame con le persone del portale.
/// L'appartenenza si riconosce dal <see cref="DistributionEntry.PersonId"/>:
/// l'email serve solo per le voci create prima che il legame esistesse, che
/// vengono agganciate alla persona alla prima modifica.
/// </summary>
public static class ClientDistribution
{
    public static (string Name, string Email, string Organization)? FindPerson(
        ClientAccount account,
        string personId)
    {
        var user = account.Users.FirstOrDefault(item => item.Id == personId);

        if (user is not null)
            return (user.Name, user.Email, "Cliente");

        var member = account.Team.FirstOrDefault(item => item.Id == personId);

        return member is null
            ? null
            : (member.Name, member.Email, member.Organization);
    }

    public static DistributionEntry? Find(
        IReadOnlyList<DistributionEntry> list,
        string? personId,
        string email) =>
        list.FirstOrDefault(entry =>
            (!string.IsNullOrWhiteSpace(personId) &&
                entry.PersonId == personId) ||
            string.Equals(
                entry.Email,
                email,
                StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Accende la casella: crea la voce se manca, altrimenti non fa nulla.
    /// Una voce storica agganciata per email viene legata alla persona.
    /// </summary>
    public static void Include(
        List<DistributionEntry> list,
        string? personId,
        string name,
        string email,
        string organization)
    {
        var entry = Find(list, personId, email);

        if (entry is null)
        {
            list.Add(new DistributionEntry(
                $"DL-{Guid.NewGuid():N}"[..10],
                name,
                email,
                organization,
                personId));

            return;
        }

        if (entry.PersonId is null && !string.IsNullOrWhiteSpace(personId))
            list[list.IndexOf(entry)] = entry with { PersonId = personId };
    }

    /// <summary>Spegne la casella: toglie la voce dalla lista indicata.</summary>
    public static void Exclude(
        List<DistributionEntry> list,
        string? personId,
        string email)
    {
        var entry = Find(list, personId, email);

        if (entry is not null)
            list.Remove(entry);
    }

    /// <summary>
    /// Utenza o partecipante rimosso: sparisce da entrambe le liste, così non
    /// resta un indirizzo orfano che continua a ricevere email.
    /// </summary>
    public static void RemovePerson(
        ClientAccount account,
        string personId,
        string email)
    {
        Exclude(account.DistributionList, personId, email);
        Exclude(account.DeliverablesDistributionList, personId, email);
    }
}
