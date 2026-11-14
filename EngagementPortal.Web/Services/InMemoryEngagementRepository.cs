using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public interface IEngagementRepository
{
    IReadOnlyList<Engagement> List(string clientId, bool allClients);
    Engagement? Get(string id, string clientId, bool allClients);
    void Save(Engagement engagement);
}

public sealed class InMemoryEngagementRepository :
    IEngagementRepository
{
    private readonly object gate = new();
    private readonly List<Engagement> engagements = CreateSeed();

    public IReadOnlyList<Engagement> List(
        string clientId,
        bool allClients)
    {
        lock (gate)
        {
            return engagements
                .Where(e => allClients || e.ClientId == clientId)
                .ToList();
        }
    }

    public Engagement? Get(
        string id,
        string clientId,
        bool allClients)
    {
        lock (gate)
        {
            return engagements.FirstOrDefault(e =>
                e.Id == id &&
                (allClients || e.ClientId == clientId));
        }
    }

    public void Save(Engagement engagement)
    {
        lock (gate)
        {
            var index = engagements.FindIndex(e => e.Id == engagement.Id);

            if (index >= 0)
                engagements[index] = engagement;
            else
                engagements.Add(engagement);
        }
    }

    public static List<Engagement> CreateSeed() => SeedData.Engagements();
}
