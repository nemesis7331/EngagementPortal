namespace EngagementPortal.Web.Services;

public interface IDateProvider
{
    DateOnly Today { get; }

    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDateProvider : IDateProvider
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
