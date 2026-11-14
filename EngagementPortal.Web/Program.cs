using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IEngagementRepository,
    InMemoryEngagementRepository>();
builder.Services.AddSingleton<EngagementWorkflow>();
builder.Services.AddSingleton<TodoResolver>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

static UserContext GetUser(HttpContext context)
{
    var role = context.Request.Query["role"].FirstOrDefault();

    if (role is not (
        Roles.Client or
        Roles.Cybersec or
        Roles.Cyberdefence or
        Roles.Manager or
        Roles.Auditor))
    {
        role = Roles.Cyberdefence;
    }

    var tenantFromHeader =
        context.Request.Headers["X-MS-CLIENT-PRINCIPAL-IDP"]
            .FirstOrDefault();

    var tenant = context.Request.Query["tenant"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(tenant))
    {
        tenant = role == Roles.Client
            ? "tenant-contoso"
            : "tenant-provider";
    }

    var name =
        context.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"]
            .FirstOrDefault()
        ?? "Demo User";

    return new UserContext(name, tenant, role);
}

var api = app.MapGroup("/api");

api.MapGet("/me", (HttpContext context) =>
{
    return Results.Ok(GetUser(context));
});

api.MapGet("/engagements", (
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    TodoResolver todos) =>
{
    var user = GetUser(context);

    var result = repository
        .List(user.TenantId, user.IsProvider)
        .Select(e => new
        {
            e.Id,
            e.Name,
            e.ClientName,
            e.Type,
            e.Owner,
            Phase = workflow.Resolve(e),
            StepIndex = EngagementWorkflow.StepIndex(
                workflow.Resolve(e)),
            Todo = todos.Resolve(e, user.Role)
        });

    return Results.Ok(result);
});

api.MapGet("/engagements/{id}", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    TodoResolver todos) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    var visibleAudit = engagement.AuditLog
        .Where(log =>
            user.IsProvider ||
            log.Visibility == AuditVisibility.All)
        .Select(log => new
        {
            log.Date,
            log.Author,
            log.AuthorRole,
            Text = user.IsProvider
                ? log.Text
                : log.ClientText?? log.Text
        });

    return Results.Ok(new
    {
        Engagement = engagement,
        Phase = workflow.Resolve(engagement),
        Todo = todos.Resolve(engagement, user.Role),
        Audit = visibleAudit
    });
});

api.MapGet("/dashboard/todo", (
    HttpContext context,
    IEngagementRepository repository,
    TodoResolver todos) =>
{
    var user = GetUser(context);

    var result = repository
        .List(user.TenantId, user.IsProvider)
        .Select(e => new
        {
            e.Id,
            e.Name,
            e.ClientName,
            Todo = todos.Resolve(e, user.Role)
        })
        .Where(item => item.Todo is not null);

    return Results.Ok(result);
});

api.MapPost("/engagements/{id}/manleva", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (Roles.Client or Roles.Cybersec))
        return Results.Forbid();

    engagement.HasManleva = true;
    engagement.AddAudit(
        user.Name,
        user.Role,
        "Manleva registrata",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPost("/engagements/{id}/order", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (
        Roles.Cybersec or Roles.Cyberdefence))
        return Results.Forbid();

    engagement.OrderConfirmed = true;
    engagement.AddAudit(
        user.Name,
        user.Role,
        "Order confirmed",
        AuditVisibility.ProviderOnly,
        "Order registered");

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPost("/engagements/{id}/proposals", (
    string id,
    CreateProposalRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (
        Roles.Client or
        Roles.Cybersec or
        Roles.Cyberdefence))
        return Results.Forbid();

    if (!engagement.HasManleva ||
        !engagement.OrderConfirmed)
        return Results.BadRequest(
            "Planning is locked until the manleva and order are complete.");

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var duration =
        request.EndDate.DayNumber -
        request.StartDate.DayNumber;

    if (request.StartDate < today ||
        request.EndDate < request.StartDate ||
        duration > 60)
    {
        return Results.BadRequest(
            "Invalid dates: start cannot be in the past, " +
            "end must follow start and duration cannot exceed 60 days.");
    }

    foreach (var pending in engagement.Proposals
        .Where(p => p.Status == ProposalStatus.Pending))
    {
        pending.Status = ProposalStatus.Superseded;
    }

    engagement.Proposals.Add(new Proposal
    {
        Id = $"P-{Guid.NewGuid():N}"[..10],
        From = user.Role == Roles.Client
            ? ProposalParty.Client
            : ProposalParty.Provider,
        By = user.Name,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Note = request.Note
    });

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Planning proposal created",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPost(
    "/engagements/{id}/proposals/{proposalId}/decide",
    (
        string id,
        string proposalId,
        DecideProposalRequest request,
        HttpContext context,
        IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    var proposal = engagement.Proposals
        .FirstOrDefault(p => p.Id == proposalId);

    if (proposal is null)
        return Results.NotFound();

    var canDecide =
        proposal.Status == ProposalStatus.Pending &&
        ((proposal.From == ProposalParty.Client &&
          user.Role == Roles.Cyberdefence) ||
         (proposal.From == ProposalParty.Provider &&
          user.Role == Roles.Client));

    if (!canDecide)
        return Results.Forbid();

    proposal.Status = request.Accept
        ? ProposalStatus.Accepted
        : ProposalStatus.Rejected;

    proposal.Reason =
        request.Accept? null: request.Reason;

    engagement.AddAudit(
        user.Name,
        user.Role,
        request.Accept
            ? "Proposal accepted"
            : "Proposal rejected",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPut("/engagements/{id}/preparation", (
    string id,
    PreparationRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (
        Roles.Client or Roles.Cyberdefence))
        return Results.Forbid();

    if (engagement.AcceptedProposal is null)
        return Results.BadRequest(
            "Preparation is locked until a proposal is accepted.");

    engagement.ScopeDone = request.ScopeDone;
    engagement.CredentialsDone = request.CredentialsDone;
    engagement.CredentialDeliveryMethod =
        request.DeliveryMethod;
    engagement.SecretReference =
        request.SecretReference;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Preparation updated",
        AuditVisibility.ProviderOnly,
        "Preparation data updated");

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPost("/engagements/{id}/reports", (
    string id,
    AddReportRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role!= Roles.Cyberdefence)
        return Results.Forbid();

    engagement.Reports.Add(new ReportFile(
        $"R-{Guid.NewGuid():N}"[..10],
        request.Name,
        request.Type,
        request.Size,
        DateTimeOffset.UtcNow,
        false));

    engagement.ReviewStatus = ReviewStatus.None;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Deliverable uploaded; review reset",
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPost("/engagements/{id}/review/submit", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role!= Roles.Cyberdefence ||
        engagement.Reports.Count == 0)
        return Results.Forbid();

    engagement.ReviewStatus = ReviewStatus.Pending;
    repository.Save(engagement);

    return Results.Ok(engagement);
});

api.MapPost("/engagements/{id}/review/decide", (
    string id,
    ReviewDecisionRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role!= Roles.Manager ||
        engagement.ReviewStatus!= ReviewStatus.Pending)
        return Results.Forbid();

    engagement.ReviewStatus = request.Approve
        ? ReviewStatus.Approved
        : ReviewStatus.Changes;

    engagement.AddAudit(
        user.Name,
        user.Role,
        request.Approve
            ? "Deliverables approved"
            : $"Changes requested: {request.Comment}",
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(engagement);
});

api.MapPost("/engagements/{id}/reports/publish", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = repository.Get(
        id,
        user.TenantId,
        user.IsProvider);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role!= Roles.Cyberdefence ||
        engagement.ReviewStatus!= ReviewStatus.Approved)
        return Results.Forbid();

    for (var index = 0;
         index < engagement.Reports.Count;
         index++)
    {
        engagement.Reports[index] =
            engagement.Reports[index] with
            {
                Published = true
            };
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Deliverables published",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(engagement);
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
