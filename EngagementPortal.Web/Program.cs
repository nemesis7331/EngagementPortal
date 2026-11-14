using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("EngagementPortal");

if (string.IsNullOrWhiteSpace(connectionString))
{
    // Compatibilità con il formato "Connection strings" di Azure App Service.
    connectionString =
        Environment.GetEnvironmentVariable("SQLCONNSTR_EngagementPortal");
}

var useDatabase = !string.IsNullOrWhiteSpace(connectionString);

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter()));

builder.Services.AddSingleton<IDateProvider, SystemDateProvider>();

if (useDatabase)
{
    builder.Services.AddDbContext<PortalDbContext>(options =>
        options.UseSqlServer(
            connectionString,
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null)));

    builder.Services.AddScoped<IEngagementRepository,
        SqlEngagementRepository>();
    builder.Services.AddScoped<IClientRepository,
        SqlClientRepository>();
}
else
{
    // Fallback per sviluppo locale e test: nessun database configurato.
    builder.Services.AddSingleton<IEngagementRepository,
        InMemoryEngagementRepository>();
    builder.Services.AddSingleton<IClientRepository,
        InMemoryClientRepository>();
}

builder.Services.AddSingleton<EngagementWorkflow>();
builder.Services.AddSingleton<TodoResolver>();
builder.Services.AddSingleton<SolicitationService>();

var app = builder.Build();

if (useDatabase)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    DatabaseBootstrap.Initialize(db, app.Logger);
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Cyber Engagement Portal",
    storage = useDatabase ? "azure-sql" : "in-memory",
    version = typeof(Program).Assembly
        .GetName().Version?.ToString() ?? "unknown",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/health/ready", (IEngagementRepository repository) =>
    Results.Ok(new
    {
        status = "ready",
        storage = useDatabase ? "azure-sql" : "in-memory",
        engagements = repository.List(string.Empty, true).Count()
    }));

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/me", (HttpContext context) =>
    Results.Ok(GetUser(context)));

api.MapGet("/clients", (
    HttpContext context,
    IClientRepository clients,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);

    var result = clients.List()
        .Where(client => user.IsProvider || client.TenantId == user.TenantId)
        .Select(client => new
        {
            client.TenantId,
            client.Name,
            client.CreatedDate,
            client.Users,
            EngagementCount = repository
                .List(client.TenantId, crossTenant: false)
                .Count()
        });

    return Results.Ok(result);
});

api.MapPost("/clients", (
    CreateClientRequest request,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    var name = request.Name?.Trim();
    var tenantId = request.TenantId?.Trim();

    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("Il nome del cliente è obbligatorio.");

    if (tenantId is { Length: > 0 } &&
        (tenantId.Length < 3 || clients.Exists(tenantId)))
    {
        return tenantId.Length < 3
            ? Results.BadRequest(
                "L'identificativo del tenant deve avere almeno 3 caratteri.")
            : Results.Conflict();
    }

    var account = clients.Add(name, tenantId);

    return Results.Created(
        $"/api/clients/{account.TenantId}",
        new
        {
            account.TenantId,
            account.Name,
            account.CreatedDate,
            account.Users,
            EngagementCount = 0
        });
});

api.MapPut("/clients/{tenantId}", (
    string tenantId,
    RenameClientRequest request,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest("Il nome del cliente è obbligatorio.");

    var account = clients.Rename(tenantId, request.Name);

    return account is null
        ? Results.NotFound()
        : Results.Ok(account);
});

api.MapPost("/clients/{tenantId}/users", (
    string tenantId,
    AddClientUserRequest request,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    var account = clients.Get(tenantId);

    if (account is null)
        return Results.NotFound();

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) ||
        !email.Contains('@') ||
        !email.Contains('.'))
    {
        return Results.BadRequest(
            "Nome ed email valida sono obbligatori.");
    }

    if (account.Users.Any(item => string.Equals(
        item.Email,
        email,
        StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict();
    }

    var created = clients.AddUser(tenantId, name, email);
    return Results.Ok(created);
});

api.MapDelete("/clients/{tenantId}/users/{userId}", (
    string tenantId,
    string userId,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    return clients.RemoveUser(tenantId, userId)
        ? Results.NoContent()
        : Results.NotFound();
});

api.MapGet("/engagements", (
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    var result = repository
        .List(user.TenantId, user.IsProvider)
        .Select(e => Summary(e, user, workflow, todos, clock));

    return Results.Ok(result);
});

api.MapPost("/engagements", (
    CreateEngagementRequest request,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role is not (Roles.PMO or Roles.Manager))
        return Forbidden();

    if (string.IsNullOrWhiteSpace(request.Name) ||
        string.IsNullOrWhiteSpace(request.ClientName) ||
        string.IsNullOrWhiteSpace(request.TenantId))
    {
        return Results.BadRequest(
            "name, clientName e tenantId sono obbligatori.");
    }

    var id = string.IsNullOrWhiteSpace(request.Id)
        ? $"ENG-{clock.Today.Year}-{repository.List(string.Empty, true).Count + 1:000}"
        : request.Id!.Trim();

    if (repository.Get(id, string.Empty, true) is not null)
        return Results.Conflict();

    var engagement = new Engagement
    {
        Id = id,
        TenantId = request.TenantId.Trim(),
        ClientName = request.ClientName.Trim(),
        Name = request.Name.Trim(),
        Type = request.Type,
        Owner = user.Name,
        CreatedDate = clock.UtcNow
    };

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Engagement creato",
        AuditVisibility.All);

    repository.Save(engagement);

    return Results.Created(
        $"/api/engagements/{id}",
        Detail(engagement, user, workflow, todos, clock));
});

api.MapGet("/engagements/{id}", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    return engagement is null
        ? Results.NotFound()
        : Results.Ok(Detail(engagement, user, workflow, todos, clock));
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

api.MapPost("/engagements/{id}/manleva", async (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (
        Roles.Client or Roles.PMO))
        return Forbidden();

    var request = await ReadBody<DocumentRequest>(context.Request);

    engagement.HasManleva = true;
    engagement.ManlevaFile = string.IsNullOrWhiteSpace(request?.FileName)
        ? "Manleva_firmata.pdf"
        : Path.GetFileName(request.FileName);

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Manleva caricata",
        AuditVisibility.All,
        "Manleva firmata caricata");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/engagements/{id}/documento-ingaggio", async (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not Roles.PMO)
        return Forbidden();

    var request = await ReadBody<DocumentRequest>(context.Request);

    engagement.DocumentoIngaggioFile =
        string.IsNullOrWhiteSpace(request?.FileName)
            ? "Documento_di_ingaggio.pdf"
            : Path.GetFileName(request.FileName);

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Documento di ingaggio caricato",
        AuditVisibility.All,
        "Documento di ingaggio disponibile");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/engagements/{id}/order", async (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not Roles.PMO)
        return Forbidden();

    var request = await ReadBody<OrderRequest>(context.Request);
    var reference = string.IsNullOrWhiteSpace(request?.Ref)
        ? $"PO-{clock.Today:yyyyMMdd}"
        : request.Ref.Trim();

    engagement.OrderConfirmed = true;
    engagement.OrderRef = reference;

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Ordine {reference} registrato",
        AuditVisibility.ProviderOnly,
        "Ordine registrato");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", reference });
});

api.MapPost("/engagements/{id}/business-case", async (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not Roles.PMO)
        return Forbidden();

    var request = await ReadBody<DocumentRequest>(context.Request);

    engagement.BusinessCaseFile =
        string.IsNullOrWhiteSpace(request?.FileName)
            ? "Business_case.pdf"
            : Path.GetFileName(request.FileName);

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Business case caricato",
        AuditVisibility.ProviderOnly,
        "Business case disponibile");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapGet("/engagements/{id}/proposals", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    return engagement is null
        ? Results.NotFound()
        : Results.Ok(engagement.Proposals.Select(MapProposal));
});

api.MapPost("/engagements/{id}/proposals", (
    string id,
    CreateProposalRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (
        Roles.Client or Roles.PMO))
        return Forbidden();

    if (!engagement.HasManleva || !engagement.OrderConfirmed)
    {
        return Results.BadRequest(
            "La pianificazione si sblocca dopo manleva e ordine.");
    }

    var today = clock.Today;
    var duration =
        request.EndDate.DayNumber - request.StartDate.DayNumber;

    if (request.StartDate < today ||
        request.EndDate < request.StartDate ||
        duration > 60)
    {
        return Results.BadRequest(
            "Date non valide: inizio non nel passato, fine successiva " +
            "all'inizio, durata massima 60 giorni.");
    }

    foreach (var pending in engagement.Proposals
        .Where(p => p.Status == ProposalStatus.Pending))
    {
        pending.Status = ProposalStatus.Superseded;
    }

    var proposal = new Proposal
    {
        Id = $"P-{Guid.NewGuid():N}"[..10],
        From = user.Role == Roles.Client
            ? ProposalParty.Client
            : ProposalParty.Provider,
        By = user.Name,
        Kind = engagement.AcceptedProposal is null
            ? "initial"
            : "resched",
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Date = today,
        Note = request.Note
    };

    engagement.Proposals.Add(proposal);

    engagement.AddAudit(
        user.Name,
        user.Role,
        proposal.Kind == "resched"
            ? "Richiesta di riprogrammazione creata"
            : "Proposta di pianificazione creata",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(MapProposal(proposal));
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
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var proposal = engagement.Proposals
        .FirstOrDefault(p => p.Id == proposalId);

    if (proposal is null)
        return Results.NotFound();

    var canDecide =
        proposal.Status == ProposalStatus.Pending &&
        ((proposal.From == ProposalParty.Client &&
          user.Role == Roles.PMO) ||
         (proposal.From == ProposalParty.Provider &&
          user.Role == Roles.Client));

    if (!canDecide)
        return Forbidden();

    if (!request.Accept &&
        string.IsNullOrWhiteSpace(request.Reason))
        return Results.BadRequest("Indica il motivo del rifiuto.");

    if (request.Accept)
    {
        foreach (var other in engagement.Proposals.Where(p =>
            p.Status == ProposalStatus.Accepted &&
            p.Id != proposal.Id))
        {
            other.Status = ProposalStatus.Replaced;
        }

        proposal.Status = ProposalStatus.Accepted;
        proposal.Reason = null;
    }
    else
    {
        proposal.Status = ProposalStatus.Rejected;
        proposal.Reason = request.Reason?.Trim();
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        request.Accept
            ? "Proposta accettata"
            : $"Proposta rifiutata: {proposal.Reason}",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(MapProposal(proposal));
});

api.MapGet("/engagements/{id}/scope", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    return engagement is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            scope = engagement.Scope,
            canEdit = CanEditPreparation(engagement, user, workflow)
        });
});

api.MapPut("/engagements/{id}/scope", (
    string id,
    ScopeRequest request,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!CanEditPreparation(engagement, user, workflow))
        return Forbidden();

    var scope = engagement.Scope;
    scope.Host = request.Host?.Trim();
    scope.IpRange = request.IpRange?.Trim();
    scope.OutOfScope = request.OutOfScope?.Trim();
    scope.Window = request.Window?.Trim();
    scope.Contact = request.Contact?.Trim();
    scope.Notes = request.Notes?.Trim();
    scope.Done = !string.IsNullOrWhiteSpace(scope.Host) &&
                 !string.IsNullOrWhiteSpace(scope.Contact);
    scope.By = user.Role;
    scope.Date = clock.UtcNow;

    engagement.ScopeDone = scope.Done;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Scope aggiornato",
        AuditVisibility.ProviderOnly,
        "Scope aggiornato");

    repository.Save(engagement);
    return Results.Ok(scope);
});

api.MapGet("/engagements/{id}/credentials", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!CanSeeCredentials(user))
        return Forbidden();

    return Results.Ok(new
    {
        credentials = engagement.Credentials,
        canEdit = CanEditPreparation(engagement, user, workflow)
    });
});

api.MapPut("/engagements/{id}/credentials", (
    string id,
    CredentialsRequest request,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!CanSeeCredentials(user))
        return Forbidden();

    if (!CanEditPreparation(engagement, user, workflow))
        return Forbidden();

    var credentials = engagement.Credentials;
    credentials.Vpn = request.Vpn?.Trim();
    credentials.Users = request.Users?.Trim();
    credentials.DeliveryMethod = request.DeliveryMethod?.Trim();
    credentials.Notes = request.Notes?.Trim();
    credentials.Done =
        !string.IsNullOrWhiteSpace(credentials.Vpn) &&
        !string.IsNullOrWhiteSpace(credentials.Users) &&
        !string.IsNullOrWhiteSpace(credentials.DeliveryMethod);
    credentials.By = user.Role;
    credentials.Date = clock.UtcNow;

    engagement.CredentialsDone = credentials.Done;
    engagement.CredentialDeliveryMethod = credentials.DeliveryMethod;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Credenziali aggiornate",
        AuditVisibility.ProviderOnly,
        "Credenziali aggiornate");

    repository.Save(engagement);
    return Results.Ok(credentials);
});

api.MapGet("/engagements/{id}/reports", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var reports = user.Role == Roles.Client
        ? engagement.Reports.Where(report => report.Published)
        : engagement.Reports;

    return Results.Ok(reports.Select(MapReport));
});

api.MapPost("/engagements/{id}/reports", (
    string id,
    AddReportRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.Pentester)
        return Forbidden();

    var name = request.Name?.Trim();

    if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        return Results.BadRequest("Nome del deliverable non valido.");

    if (request.Size <= 0)
        return Results.BadRequest("Dimensione del deliverable non valida.");

    var type = string.IsNullOrWhiteSpace(request.Type)
        ? Path.GetExtension(name).TrimStart('.').ToUpperInvariant()
        : request.Type.Trim().ToUpperInvariant();

    engagement.Reports.Add(new ReportFile(
        $"R-{Guid.NewGuid():N}"[..10],
        name,
        string.IsNullOrWhiteSpace(type) ? "PDF" : type,
        request.Size,
        clock.UtcNow,
        false));

    var hadReview = engagement.ReviewStatus != ReviewStatus.None;
    engagement.ReviewStatus = ReviewStatus.None;

    engagement.AddAudit(
        user.Name,
        user.Role,
        hadReview
            ? "Deliverable caricato; revisione azzerata"
            : "Deliverable caricato come bozza",
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/engagements/{id}/reports/{reportId}/download", (
    string id,
    string reportId,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var report = engagement.Reports
        .FirstOrDefault(r => r.Id == reportId);

    if (report is null)
        return Results.NotFound();

    if (user.Role == Roles.Client && !report.Published)
        return Results.NotFound();

    var content =
        "Cyber Engagement Portal\n" +
        $"Engagement: {engagement.Id} - {engagement.Name}\n" +
        $"Deliverable: {report.Name}\n" +
        $"Utente: {user.Name} ({user.Role})\n" +
        $"Generato: {clock.UtcNow:O}\n" +
        "Link valido 15 minuti. Copia tracciata e filigranata.";

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Download {report.Name}",
        AuditVisibility.ProviderOnly,
        $"Download effettuato: {report.Name}");

    repository.Save(engagement);

    var safeName = string.Concat(
        Path.GetFileNameWithoutExtension(report.Name)
            .Split(Path.GetInvalidFileNameChars()));

    return Results.File(
        Encoding.UTF8.GetBytes(content),
        "text/plain",
        $"{safeName}.txt");
});

api.MapPost("/engagements/{id}/review/submit", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.Pentester)
        return Forbidden();

    if (!engagement.Reports.Any(report => !report.Published))
        return Results.BadRequest("Nessun deliverable da inviare in revisione.");

    engagement.ReviewStatus = ReviewStatus.Pending;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Deliverables inviati in revisione al manager",
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/engagements/{id}/review/decide", (
    string id,
    ReviewDecisionRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.Manager ||
        engagement.ReviewStatus != ReviewStatus.Pending)
        return Forbidden();

    if (!request.Approve &&
        string.IsNullOrWhiteSpace(request.Comment))
        return Results.BadRequest("Indica le modifiche richieste.");

    engagement.ReviewStatus = request.Approve
        ? ReviewStatus.Approved
        : ReviewStatus.Changes;

    engagement.AddAudit(
        user.Name,
        user.Role,
        request.Approve
            ? "Deliverables approvati"
            : $"Modifiche richieste: {request.Comment}",
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/engagements/{id}/reports/publish", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.Pentester ||
        engagement.ReviewStatus != ReviewStatus.Approved)
        return Forbidden();

    var unpublished = engagement.Reports
        .Where(report => !report.Published)
        .ToList();

    if (unpublished.Count == 0)
        return Results.BadRequest("Nessun deliverable da pubblicare.");

    for (var index = 0; index < engagement.Reports.Count; index++)
    {
        engagement.Reports[index] =
            engagement.Reports[index] with { Published = true };
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Deliverables pubblicati",
        AuditVisibility.All);

    if (engagement.Mail.Deliver && engagement.DistributionList.Count > 0)
    {
        engagement.MailLog.Add(new MailLogEntry(
            $"M-{Guid.NewGuid():N}"[..10],
            "deliver",
            clock.UtcNow,
            string.Join(
                "; ",
                engagement.DistributionList.Select(entry => entry.Email)),
            true,
            "All"));
    }

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", published = unpublished.Count });
});

api.MapGet("/engagements/{id}/distribution-list", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    return engagement is null
        ? Results.NotFound()
        : Results.Ok(engagement.DistributionList);
});

api.MapPost("/engagements/{id}/distribution-list", (
    string id,
    DistributionEntryRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!CanWrite(user))
        return Forbidden();

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();
    var organization = user.Role == Roles.Client
        ? "Cliente"
        : request.Organization?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) ||
        !email.Contains('@') ||
        !email.Contains('.') ||
        organization is not (
            "Cliente" or
            "Provider PMO" or
            "Provider Pentester"))
    {
        return Results.BadRequest(
            "Nome, email valida e organizzazione sono obbligatori.");
    }

    if (engagement.DistributionList.Any(entry =>
        string.Equals(entry.Email, email, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict();

    var entry = new DistributionEntry(
        $"DL-{Guid.NewGuid():N}"[..10],
        name,
        email,
        organization);

    engagement.DistributionList.Add(entry);

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Destinatario aggiunto: {name}",
        AuditVisibility.All,
        "Distribution list aggiornata");

    repository.Save(engagement);
    return Results.Ok(entry);
});

api.MapDelete("/engagements/{id}/distribution-list/{entryId}", (
    string id,
    string entryId,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!CanWrite(user))
        return Forbidden();

    var entry = engagement.DistributionList
        .FirstOrDefault(item => item.Id == entryId);

    if (entry is null)
        return Results.NotFound();

    if (user.Role == Roles.Client && entry.Organization != "Cliente")
        return Forbidden();

    if (entry.Organization == "Cliente" &&
        engagement.DistributionList.Count(item =>
            item.Organization == "Cliente") <= 1)
        return Results.BadRequest(
            "Deve rimanere almeno un destinatario Cliente.");

    engagement.DistributionList.Remove(entry);

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Destinatario rimosso: {entry.Name}",
        AuditVisibility.All,
        "Distribution list aggiornata");

    repository.Save(engagement);
    return Results.NoContent();
});

api.MapGet("/engagements/{id}/mails", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var log = user.Role == Roles.Client
        ? engagement.MailLog.Where(entry => entry.Visibility == "All")
        : engagement.MailLog;

    return Results.Ok(new
    {
        settings = engagement.Mail,
        history = log.OrderByDescending(entry => entry.Date)
    });
});

api.MapPost("/engagements/{id}/mails/{type}/toggle", (
    string id,
    string type,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.PMO)
        return Forbidden();

    switch (type)
    {
        case "start":
            engagement.Mail.Start = !engagement.Mail.Start;
            break;
        case "end":
            engagement.Mail.End = !engagement.Mail.End;
            break;
        case "deliver":
            engagement.Mail.Deliver = !engagement.Mail.Deliver;
            break;
        default:
            return Results.BadRequest("Tipo email non valido.");
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Invio automatico '{type}' aggiornato",
        AuditVisibility.ProviderOnly,
        "Impostazioni email aggiornate");

    repository.Save(engagement);
    return Results.Ok(engagement.Mail);
});

api.MapPost("/engagements/{id}/mails/{type}/send", (
    string id,
    string type,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.PMO)
        return Forbidden();

    if (type is not ("start" or "end" or "deliver"))
        return Results.BadRequest("Tipo email non valido.");

    if (engagement.DistributionList.Count == 0)
        return Results.BadRequest("Distribution list vuota.");

    if (type != "deliver" && engagement.AcceptedProposal is null)
        return Results.BadRequest("Nessuna attività pianificata.");

    if (type == "deliver" &&
        !engagement.Reports.Any(report => report.Published))
        return Results.BadRequest("Nessun deliverable pubblicato.");

    var recipients = string.Join(
        "; ",
        engagement.DistributionList.Select(entry => entry.Email));

    engagement.MailLog.Add(new MailLogEntry(
        $"M-{Guid.NewGuid():N}"[..10],
        type,
        clock.UtcNow,
        recipients,
        false,
        "All"));

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Email '{type}' inviata alla distribution list",
        AuditVisibility.All,
        $"Email ricevuta: {type}");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", recipients });
});

api.MapGet("/engagements/{id}/log", (
    string id,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    return engagement is null
        ? Results.NotFound()
        : Results.Ok(VisibleAudit(engagement, user));
});

api.MapGet("/engagements/{id}/log/export", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    var rows = VisibleAudit(engagement, user)
        .Select(entry => string.Join(
            ";",
            Quote(entry.Date.ToString("yyyy-MM-dd")),
            Quote(entry.Date.ToString("HH:mm")),
            Quote(entry.Author),
            Quote(entry.AuthorRole),
            Quote(entry.Text)));

    var csv = string.Join(
        Environment.NewLine,
        new[] { "\"Data\";\"Ora\";\"Autore\";\"Profilo\";\"Evento\"" }
            .Concat(rows));

    return Results.File(
        Encoding.UTF8.GetBytes(csv),
        "text/csv",
        $"registro-{engagement.Id}-{clock.Today:yyyyMMdd}.csv");
});

api.MapGet("/solicitations/rules", (
    HttpContext context,
    SolicitationService solicitations) =>
{
    var user = GetUser(context);

    return user.Role == Roles.Client
        ? Forbidden()
        : Results.Ok(solicitations.Rules());
});

api.MapPut("/solicitations/rules/{key}", (
    string key,
    SolicitationRuleRequest request,
    HttpContext context,
    SolicitationService solicitations) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.PMO)
        return Forbidden();

    return solicitations.Update(
        key,
        request.Enabled,
        request.ThresholdDays)
        ? Results.Ok(solicitations.Rules()
            .First(rule => rule.Key == key))
        : Results.NotFound();
});

api.MapGet("/solicitations/due", (
    HttpContext context,
    IEngagementRepository repository,
    SolicitationService solicitations,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role == Roles.Client)
        return Forbidden();

    var due = solicitations.Due(
        repository.List(user.TenantId, user.IsProvider),
        clock.Today);

    return Results.Ok(due.Select(item => new
    {
        key = $"{item.EngagementId}:{item.Key}",
        ruleKey = item.Key,
        item.Label,
        item.EngagementId,
        item.ClientName,
        item.Days,
        item.ThresholdDays
    }));
});

api.MapPost("/solicitations/{key}/send", (
    string key,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.PMO)
        return Forbidden();

    var parts = key.Split(':', 2);

    if (parts.Length != 2)
        return Results.BadRequest("Chiave sollecito non valida.");

    var engagement = Find(repository, parts[0], user);

    if (engagement is null)
        return Results.NotFound();

    if (engagement.DistributionList.Count == 0)
        return Results.BadRequest("Distribution list vuota.");

    engagement.MailLog.Add(new MailLogEntry(
        $"M-{Guid.NewGuid():N}"[..10],
        $"solicitation:{parts[1]}",
        clock.UtcNow,
        string.Join(
            "; ",
            engagement.DistributionList.Select(entry => entry.Email)),
        false,
        "All"));

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Sollecito '{parts[1]}' inviato",
        AuditVisibility.All,
        "Sollecito ricevuto");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

app.MapFallbackToFile("index.html");

app.Run();

static UserContext GetUser(HttpContext context)
{
    var role = context.Request.Query["role"].FirstOrDefault();

    if (role is not (
        Roles.Client or
        Roles.PMO or
        Roles.Pentester or
        Roles.Manager or
        Roles.Auditor))
    {
        role = Roles.Pentester;
    }

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

static Engagement? Find(
    IEngagementRepository repository,
    string id,
    UserContext user) =>
    repository.Get(id, user.TenantId, user.IsProvider);

static IResult Forbidden() =>
    Results.StatusCode(StatusCodes.Status403Forbidden);

// Scrittura sulla distribution list: il cliente gestisce i propri
// destinatari, PMO coordina l'intera lista. Gli altri profili
// (Pentester, Manager, Auditor) sono in sola lettura.
static bool CanWrite(UserContext user) =>
    user.Role is Roles.Client or Roles.PMO;

static bool CanSeeCredentials(UserContext user) =>
    user.Role is Roles.Client or Roles.PMO or Roles.Pentester;

static bool CanEditPreparation(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow)
{
    if (engagement.AcceptedProposal is null)
        return false;

    var phase = workflow.Resolve(engagement);

    return user.Role switch
    {
        Roles.Client => phase is EngagementPhase.Prep or
            EngagementPhase.Scheduled,
        Roles.PMO => phase is EngagementPhase.Prep or
            EngagementPhase.Scheduled or
            EngagementPhase.Running,
        _ => false
    };
}

static object Summary(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock)
{
    var phase = workflow.Resolve(engagement, clock.Today);

    return new
    {
        engagement.Id,
        engagement.Name,
        engagement.ClientName,
        engagement.Type,
        engagement.Owner,
        Phase = phase,
        StepIndex = EngagementWorkflow.StepIndex(phase),
        Todo = todos.Resolve(engagement, user.Role),
        PeriodStart = engagement.AcceptedProposal?.StartDate,
        PeriodEnd = engagement.AcceptedProposal?.EndDate,
        ReportCount = user.Role == Roles.Client
            ? engagement.Reports.Count(report => report.Published)
            : engagement.Reports.Count,
        PublishedCount = engagement.Reports.Count(
            report => report.Published),
        PendingProposal = engagement.Proposals.Any(
            proposal => proposal.Status == ProposalStatus.Pending)
    };
}

static object Detail(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock)
{
    var phase = workflow.Resolve(engagement, clock.Today);

    var reports = user.Role == Roles.Client
        ? engagement.Reports.Where(report => report.Published).ToList()
        : engagement.Reports.ToList();

    var mailLog = user.Role == Roles.Client
        ? engagement.MailLog
            .Where(entry => entry.Visibility == "All")
            .ToList()
        : engagement.MailLog.ToList();

    return new
    {
        engagement = new
        {
            engagement.Id,
            engagement.TenantId,
            engagement.ClientName,
            engagement.Name,
            engagement.Type,
            engagement.Owner,
            engagement.CreatedDate,
            engagement.HasManleva,
            engagement.ManlevaFile,
            engagement.OrderConfirmed,
            OrderRef = user.Role == Roles.Client
                ? null
                : engagement.OrderRef,
            engagement.DocumentoIngaggioFile,
            BusinessCaseFile = user.Role == Roles.Client
                ? null
                : engagement.BusinessCaseFile,
            engagement.ScopeDone,
            engagement.CredentialsDone,
            ReviewStatus = engagement.ReviewStatus,
            Proposals = engagement.Proposals.Select(MapProposal),
            Reports = reports.Select(MapReport),
            AcceptedProposal = engagement.AcceptedProposal is null
                ? null
                : MapProposal(engagement.AcceptedProposal)
        },
        Phase = phase,
        StepIndex = EngagementWorkflow.StepIndex(phase),
        Todo = todos.Resolve(engagement, user.Role),
        Audit = VisibleAudit(engagement, user),
        Scope = engagement.Scope,
        Credentials = CanSeeCredentials(user)
            ? engagement.Credentials
            : null,
        DistributionList = engagement.DistributionList,
        Mail = new
        {
            start = new
            {
                engagement.Mail.Start,
                Sent = engagement.MailLog.Any(
                    entry => entry.Type == "start")
            },
            end = new
            {
                engagement.Mail.End,
                Sent = engagement.MailLog.Any(
                    entry => entry.Type == "end")
            },
            deliver = new
            {
                engagement.Mail.Deliver,
                Sent = engagement.MailLog.Any(
                    entry => entry.Type == "deliver")
            }
        },
        MailLog = mailLog.OrderByDescending(entry => entry.Date),
        Capabilities = new
        {
            IsClient = user.Role == Roles.Client,
            IsProvider = user.IsProvider,
            CanWrite = CanWrite(user),
            CanDecide = user.Role is Roles.Client or Roles.PMO,
            CanCredentials = CanSeeCredentials(user),
            CanManageMail = user.Role == Roles.PMO,
            CanManageSolicitations = user.Role == Roles.PMO,
            CanReview = user.Role == Roles.Manager,
            CanPublish = user.Role == Roles.Pentester,
            CanUploadManleva = user.Role is Roles.Client or Roles.PMO,
            CanManageEngagementDocs = user.Role == Roles.PMO,
            CanPropose = user.Role is Roles.Client or Roles.PMO,
            CanUploadReports = user.Role == Roles.Pentester,
            CanManageClients = user.Role == Roles.Manager,
            CanViewClients = user.IsProvider,
            ReadOnly = user.Role == Roles.Auditor
        }
    };
}

static IEnumerable<AuditView> VisibleAudit(
    Engagement engagement,
    UserContext user) =>
    engagement.AuditLog
        .Where(entry =>
            user.IsProvider ||
            entry.Visibility == AuditVisibility.All)
        .Select(entry => new AuditView(
            entry.Date,
            entry.Author,
            entry.AuthorRole,
            user.IsProvider
                ? entry.Text
                : entry.ClientText ?? entry.Text));

static object MapProposal(Proposal proposal) => new
{
    proposal.Id,
    proposal.From,
    proposal.By,
    proposal.Kind,
    proposal.StartDate,
    proposal.EndDate,
    proposal.Date,
    proposal.Note,
    proposal.Status,
    proposal.Reason
};

static object MapReport(ReportFile report) => new
{
    report.Id,
    report.Name,
    report.Type,
    report.Size,
    report.Date,
    report.Published
};

static async Task<T?> ReadBody<T>(HttpRequest request)
{
    if (request.ContentLength is 0)
        return default;

    try
    {
        return await JsonSerializer.DeserializeAsync<T>(
            request.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return default;
    }
}

public partial class Program;

public sealed record AuditView(
    DateTimeOffset Date,
    string Author,
    string AuthorRole,
    string Text);
