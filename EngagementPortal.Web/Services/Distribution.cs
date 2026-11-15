using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public static class Distribution
{
    /// <summary>
    /// Distribution list delle notifiche (inizio/fine attività, solleciti):
    /// base cliente + destinatari dell'engagement, deduplicata per email
    /// (la base vince sull'eventuale duplicato).
    /// </summary>
    public static IReadOnlyList<DistributionEntry> Effective(
        Engagement engagement,
        IEnumerable<DistributionEntry> baseList) =>
        Merge(baseList, engagement.DistributionList);

    /// <summary>
    /// Distribution list dei deliverables: base cliente + destinatari
    /// dell'engagement, deduplicata per email. Se entrambe sono vuote si
    /// usano i destinatari delle notifiche, così un engagement senza lista
    /// dedicata continua a consegnare i report.
    /// </summary>
    public static IReadOnlyList<DistributionEntry> EffectiveDeliverables(
        Engagement engagement,
        IEnumerable<DistributionEntry> baseDeliverables,
        IEnumerable<DistributionEntry>? baseNotifications = null)
    {
        var result = Merge(
            baseDeliverables,
            engagement.DeliverablesDistributionList);

        return result.Count > 0 || baseNotifications is null
            ? result
            : Effective(engagement, baseNotifications);
    }

    /// <summary>
    /// True quando non esiste una lista deliverables esplicita e si sta
    /// usando quella delle notifiche come fallback.
    /// </summary>
    public static bool UsesNotificationFallback(
        Engagement engagement,
        IEnumerable<DistributionEntry> baseDeliverables) =>
        !baseDeliverables.Any() &&
        engagement.DeliverablesDistributionList.Count == 0;

    /// <summary>Unisce due liste deduplicando per email.</summary>
    public static IReadOnlyList<DistributionEntry> Combine(
        IEnumerable<DistributionEntry> baseList,
        IEnumerable<DistributionEntry> engagementList) =>
        Merge(baseList, engagementList);

    private static List<DistributionEntry> Merge(
        IEnumerable<DistributionEntry> baseList,
        IEnumerable<DistributionEntry> engagementList)
    {
        var result = new List<DistributionEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in baseList.Concat(engagementList))
        {
            if (seen.Add(entry.Email))
                result.Add(entry);
        }

        return result;
    }
}
