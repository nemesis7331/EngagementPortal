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
    builder.Services.AddScoped<IAppSettingsRepository,
        SqlAppSettingsRepository>();
}
else
{
    // Fallback per sviluppo locale e test: nessun database configurato.
    builder.Services.AddSingleton<IEngagementRepository,
        InMemoryEngagementRepository>();
    builder.Services.AddSingleton<IClientRepository,
        InMemoryClientRepository>();
    builder.Services.AddSingleton<IAppSettingsRepository,
        InMemoryAppSettingsRepository>();
}

builder.Services.AddSingleton<EngagementWorkflow>();
builder.Services.AddSingleton<TodoResolver>();
builder.Services.AddSingleton<SolicitationService>();
builder.Services.AddHostedService<MailAutomationService>();

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
        .Where(client => user.IsInternal || client.Id == user.ClientId)
        .Select(client => new
        {
            client.Id,
            client.Name,
            client.Website,
            client.CreatedDate,
            client.Users,
            client.DistributionList,
            client.DeliverablesDistributionList,
            client.Team,
            EngagementCount = repository
                .List(client.Id, allClients: false)
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

    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("Il nome del cliente è obbligatorio.");

    if (!TryNormalizeWebsite(request.Website, out var website))
        return Results.BadRequest("Il sito web deve essere un indirizzo valido.");

    var account = clients.Add(name, website);

    return Results.Created(
        $"/api/clients/{account.Id}",
        new
        {
            account.Id,
            account.Name,
            account.Website,
            account.CreatedDate,
            account.Users,
            account.DistributionList,
            account.DeliverablesDistributionList,
            account.Team,
            EngagementCount = 0
        });
});

api.MapPut("/clients/{clientId}", (
    string clientId,
    RenameClientRequest request,
    HttpContext context,
    IClientRepository clients,
    IEngagementRepository repository) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest("Il nome del cliente è obbligatorio.");

    if (!TryNormalizeWebsite(request.Website, out var website))
        return Results.BadRequest("Il sito web deve essere un indirizzo valido.");

    var account = clients.Update(clientId, request.Name, website);

    if (account is null)
        return Results.NotFound();

    // Il nome è denormalizzato sugli engagement: allinearlo subito evita
    // che la lista continui a mostrare la vecchia ragione sociale.
    foreach (var engagement in repository.List(clientId, allClients: false))
    {
        if (engagement.ClientName == account.Name)
            continue;

        engagement.ClientName = account.Name;
        repository.Save(engagement);
    }

    return Results.Ok(account);
});

api.MapPost("/clients/{clientId}/users", (
    string clientId,
    AddClientUserRequest request,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    var account = clients.Get(clientId);

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

    var created = clients.AddUser(clientId, name, email);
    return Results.Ok(created);
});

api.MapDelete("/clients/{clientId}/users/{userId}", (
    string clientId,
    string userId,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    return clients.RemoveUser(clientId, userId)
        ? Results.NoContent()
        : Results.NotFound();
});

// Base comune della distribution list: destinatari validi per tutti gli
// engagement del cliente. La gestisce il Manager nell'anagrafica.
api.MapPost("/clients/{clientId}/distribution-list", (
    string clientId,
    DistributionEntryRequest request,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    var account = clients.Get(clientId);

    if (account is null)
        return Results.NotFound();

    var list = SelectsDeliverables(context)
        ? account.DeliverablesDistributionList
        : account.DistributionList;

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) ||
        !email.Contains('@') ||
        !email.Contains('.'))
    {
        return Results.BadRequest("Nome ed email valida sono obbligatori.");
    }

    if (list.Any(entry => string.Equals(
        entry.Email,
        email,
        StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict();
    }

    var entry = new DistributionEntry(
        $"DL-{Guid.NewGuid():N}"[..10],
        name,
        email,
        "Cliente");

    list.Add(entry);
    clients.Save(account);
    return Results.Ok(entry);
});

api.MapDelete("/clients/{clientId}/distribution-list/{entryId}", (
    string clientId,
    string entryId,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    var account = clients.Get(clientId);

    if (account is null)
        return Results.NotFound();

    var list = SelectsDeliverables(context)
        ? account.DeliverablesDistributionList
        : account.DistributionList;

    var entry = list.FirstOrDefault(item => item.Id == entryId);

    if (entry is null)
        return Results.NotFound();

    list.Remove(entry);
    clients.Save(account);
    return Results.NoContent();
});

// Team esterno del cliente: base comune a tutti i suoi engagement.
api.MapPost("/clients/{clientId}/team", (
    string clientId,
    TeamMemberRequest request,
    HttpContext context,
    IClientRepository clients,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (!Team.CanInvite(user.Role))
        return Forbidden();

    if (user.Role == Roles.Client && user.ClientId != clientId)
        return Forbidden();

    var account = clients.Get(clientId);

    if (account is null)
        return Results.NotFound();

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();
    var organization = request.Organization?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) ||
        !email.Contains('@') ||
        !email.Contains('.') ||
        organization is not ("Cliente" or "Partner" or "Altro"))
    {
        return Results.BadRequest(
            "Nome, email valida e organizzazione sono obbligatori.");
    }

    if (account.Team.Any(member => string.Equals(
        member.Email,
        email,
        StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict();
    }

    var created = new TeamMember(
        $"TM-{Guid.NewGuid():N}"[..10],
        name,
        email,
        organization,
        request.Role?.Trim(),
        user.Name,
        clock.UtcNow);

    account.Team.Add(created);
    clients.Save(account);
    return Results.Ok(created);
});

api.MapDelete("/clients/{clientId}/team/{memberId}", (
    string clientId,
    string memberId,
    HttpContext context,
    IClientRepository clients) =>
{
    var user = GetUser(context);

    if (user.Role == Roles.Client && user.ClientId != clientId)
        return Forbidden();

    var account = clients.Get(clientId);

    if (account is null)
        return Results.NotFound();

    var member = account.Team
        .FirstOrDefault(item => item.Id == memberId);

    if (member is null)
        return Results.NotFound();

    if (!Team.CanRemove(user.Role, member))
        return Forbidden();

    account.Team.Remove(member);
    clients.Save(account);
    return Results.NoContent();
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
        .List(ClientScope(user), SeesAllClients(user))
        .Select(e => Summary(e, user, workflow, todos, clock));

    return Results.Ok(result);
});

api.MapPost("/engagements", (
    CreateEngagementRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role is not (Roles.PMO or Roles.Manager))
        return Forbidden();

    if (string.IsNullOrWhiteSpace(request.Name) ||
        string.IsNullOrWhiteSpace(request.ClientId))
    {
        return Results.BadRequest("name e clientId sono obbligatori.");
    }

    var client = clients.Get(request.ClientId.Trim());

    if (client is null)
    {
        return Results.BadRequest(
            "clientId non corrisponde a un cliente esistente.");
    }

    var id = string.IsNullOrWhiteSpace(request.Id)
        ? $"ENG-{clock.Today.Year}-{repository.List(string.Empty, true).Count + 1:000}"
        : request.Id!.Trim();

    if (repository.Get(id, string.Empty, true) is not null)
        return Results.Conflict();

    var engagement = new Engagement
    {
        Id = id,
        ClientId = client.Id,
        // Il nome è letto dall'anagrafica: rinominare il cliente aggiorna
        // tutti gli engagement senza duplicare il dato.
        ClientName = client.Name,
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
        Detail(
            engagement,
            user,
            workflow,
            todos,
            clock,
            client.DistributionList,
            client.DeliverablesDistributionList,
            client.Team));
});

api.MapGet("/engagements/{id}", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    EngagementWorkflow workflow,
    TodoResolver todos,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var client = clients.Get(engagement.ClientId);

    return Results.Ok(Detail(
        engagement,
        user,
        workflow,
        todos,
        clock,
        client?.DistributionList ?? [],
        client?.DeliverablesDistributionList ?? [],
        client?.Team ?? []));
});

api.MapGet("/dashboard/todo", (
    HttpContext context,
    IEngagementRepository repository,
    TodoResolver todos) =>
{
    var user = GetUser(context);

    var result = repository
        .List(ClientScope(user), SeesAllClients(user))
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

api.MapPut("/engagements/{id}/financials", (
    string id,
    FinancialsRequest request,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (Roles.PMO or Roles.Manager))
        return Forbidden();

    if (request.Cost is null || request.Margin is null)
        return Results.BadRequest("Costo e margine sono obbligatori.");

    if (request.Cost < 0)
        return Results.BadRequest("Il costo non può essere negativo.");

    if (request.Margin is < 0 or > 100)
        return Results.BadRequest(
            "Il margine è una percentuale tra 0 e 100.");

    if (!CanEditFinancials(engagement, user, workflow))
    {
        return Results.BadRequest(
            "Costi e margine si inseriscono nella prima fase.");
    }

    engagement.Cost = request.Cost;
    engagement.Margin = request.Margin;
    engagement.FinancialsSetByRole = user.Role;
    engagement.FinancialsSetBy = user.Name;

    // Il Manager che inserisce i dati li approva contestualmente; il PMO
    // lascia la voce in attesa di approvazione.
    engagement.FinancialsApproved = user.Role == Roles.Manager;
    engagement.FinancialsApprovedBy =
        user.Role == Roles.Manager ? user.Name : null;
    engagement.FinancialsApprovedDate =
        user.Role == Roles.Manager ? clock.UtcNow : null;

    engagement.AddAudit(
        user.Name,
        user.Role,
        user.Role == Roles.Manager
            ? "Costi e margine aggiornati e approvati"
            : "Costi e margine aggiornati (in attesa di approvazione)",
        AuditVisibility.ProviderOnly,
        user.Role == Roles.Manager
            ? "Dati economici approvati"
            : "Dati economici aggiornati");

    repository.Save(engagement);
    return Results.Ok(new
    {
        status = "ok",
        cost = engagement.Cost,
        margin = engagement.Margin,
        financialsDone = engagement.FinancialsDone,
        financialsApproved = engagement.FinancialsApproved
    });
});

api.MapPost("/engagements/{id}/financials/approve", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role != Roles.Manager)
        return Forbidden();

    if (!CanApproveFinancials(engagement, user, workflow))
    {
        return Results.BadRequest(
            "Non ci sono costi e margine da approvare in questa fase.");
    }

    engagement.FinancialsApproved = true;
    engagement.FinancialsApprovedBy = user.Name;
    engagement.FinancialsApprovedDate = clock.UtcNow;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Costi e margine approvati",
        AuditVisibility.ProviderOnly,
        "Dati economici approvati");

    repository.Save(engagement);
    return Results.Ok(new
    {
        status = "ok",
        financialsApproved = engagement.FinancialsApproved,
        financialsApprovedBy = engagement.FinancialsApprovedBy
    });
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
        user.Role == Roles.Manager;

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
            ? "Proposta approvata"
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
        canEdit = CanEditCredentials(engagement, user, workflow)
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

    if (!CanEditCredentials(engagement, user, workflow))
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
    IClientRepository clients,
    IAppSettingsRepository settings,
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

    var client = clients.Get(engagement.ClientId);
    var baseList = client?.DistributionList ?? [];
    var baseDeliverables = client?.DeliverablesDistributionList ?? [];
    var recipients = Distribution.EffectiveDeliverables(
        engagement,
        baseDeliverables,
        baseList);

    if (engagement.Mail.Deliver && recipients.Count > 0)
    {
        var standard = MailTemplates.Standard(
            settings.Get<MailTemplateSet>(MailTemplates.StandardKey));
        var resolved = MailTemplates.Resolve(
            "deliver",
            engagement,
            standard,
            client?.MailTemplates,
            clock.Today);

        engagement.MailLog.Add(new MailLogEntry(
            $"M-{Guid.NewGuid():N}"[..10],
            "deliver",
            clock.UtcNow,
            string.Join("; ", recipients.Select(entry => entry.Email)),
            true,
            "All",
            resolved.Subject,
            resolved.Body,
            resolved.Source));
    }

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", published = unpublished.Count });
});

api.MapGet("/engagements/{id}/distribution-list", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var client = clients.Get(engagement.ClientId);
    var baseList = client?.DistributionList ?? [];
    var baseDeliverables = client?.DeliverablesDistributionList ?? [];

    return Results.Ok(new
    {
        @base = baseList,
        engagement = engagement.DistributionList,
        effective = Distribution.Effective(engagement, baseList),
        deliverablesBase = baseDeliverables,
        deliverablesEngagement = engagement.DeliverablesDistributionList,
        deliverablesEffective = Distribution.EffectiveDeliverables(
            engagement,
            baseDeliverables,
            baseList),
        deliverablesFallback = Distribution.UsesNotificationFallback(
            engagement,
            baseDeliverables)
    });
});

api.MapPost("/engagements/{id}/distribution-list", (
    string id,
    DistributionEntryRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients) =>
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

    var client = clients.Get(engagement.ClientId);
    var baseList = client?.DistributionList ?? [];
    var baseDeliverables = client?.DeliverablesDistributionList ?? [];
    var deliverables = SelectsDeliverables(context);
    var list = deliverables
        ? engagement.DeliverablesDistributionList
        : engagement.DistributionList;
    var effective = deliverables
        ? Distribution.EffectiveDeliverables(
            engagement,
            baseDeliverables,
            baseList)
        : Distribution.Effective(engagement, baseList);

    if (effective.Any(entry =>
        string.Equals(entry.Email, email, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict();

    var entry = new DistributionEntry(
        $"DL-{Guid.NewGuid():N}"[..10],
        name,
        email,
        organization);

    list.Add(entry);

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Destinatario {(deliverables ? "deliverables" : "notifiche")} aggiunto: {name}",
        AuditVisibility.All,
        "Distribution list aggiornata");

    repository.Save(engagement);
    return Results.Ok(entry);
});

api.MapDelete("/engagements/{id}/distribution-list/{entryId}", (
    string id,
    string entryId,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!CanWrite(user))
        return Forbidden();

    var deliverables = SelectsDeliverables(context);
    var list = deliverables
        ? engagement.DeliverablesDistributionList
        : engagement.DistributionList;
    var entry = list.FirstOrDefault(item => item.Id == entryId);

    if (entry is null)
        return Results.NotFound();

    if (user.Role == Roles.Client && entry.Organization != "Cliente")
        return Forbidden();

    var client = clients.Get(engagement.ClientId);
    var baseList = client?.DistributionList ?? [];
    var baseDeliverables = client?.DeliverablesDistributionList ?? [];
    var effective = deliverables
        ? Distribution.EffectiveDeliverables(
            engagement,
            baseDeliverables,
            baseList)
        : Distribution.Effective(engagement, baseList);
    var clientRecipients = effective
        .Count(item => item.Organization == "Cliente");

    if (entry.Organization == "Cliente" && clientRecipients <= 1)
        return Results.BadRequest(
            "Deve rimanere almeno un destinatario Cliente.");

    list.Remove(entry);

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Destinatario {(deliverables ? "deliverables" : "notifiche")} rimosso: {entry.Name}",
        AuditVisibility.All,
        "Distribution list aggiornata");

    repository.Save(engagement);
    return Results.NoContent();
});

api.MapPost("/engagements/{id}/team", (
    string id,
    TeamMemberRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    // I partecipanti esterni possono essere invitati da chiunque
    // tranne l'Auditor.
    if (!Team.CanInvite(user.Role))
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var scope = (request.Scope ?? "engagement")
        .Trim()
        .ToLowerInvariant();

    if (scope is not ("client" or "engagement"))
        return Results.BadRequest("Ambito non valido.");

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();
    var organization = request.Organization?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) ||
        !email.Contains('@') ||
        !email.Contains('.') ||
        organization is not ("Cliente" or "Partner" or "Altro"))
    {
        return Results.BadRequest(
            "Nome, email valida e organizzazione sono obbligatori.");
    }

    var client = clients.Get(engagement.ClientId);
    var baseTeam = client?.Team ?? [];

    if (Team.Effective(engagement, baseTeam).Any(member =>
        string.Equals(member.Email, email, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict();

    var created = new TeamMember(
        $"TM-{Guid.NewGuid():N}"[..10],
        name,
        email,
        organization,
        request.Role?.Trim(),
        user.Name,
        clock.UtcNow);

    if (scope == "client")
    {
        if (client is null)
            return Results.NotFound();

        client.Team.Add(created);
        clients.Save(client);
    }
    else
    {
        engagement.Team.Add(created);
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Partecipante invitato: {name}",
        AuditVisibility.All,
        "Team aggiornato");

    repository.Save(engagement);
    return Results.Ok(created);
});

api.MapDelete("/engagements/{id}/team/{memberId}", (
    string id,
    string memberId,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var member = engagement.Team
        .FirstOrDefault(item => item.Id == memberId);
    var source = "engagement";

    if (member is null)
    {
        var client = clients.Get(engagement.ClientId);
        member = client?.Team.FirstOrDefault(item => item.Id == memberId);
        source = "client";
    }

    if (member is null)
        return Results.NotFound();

    // Rimuovono solo Manager e Cliente; il Cliente solo i partecipanti
    // con organizzazione "Cliente".
    if (!Team.CanRemove(user.Role, member))
        return Forbidden();

    if (source == "client")
    {
        var client = clients.Get(engagement.ClientId);

        if (client is null)
            return Results.NotFound();

        client.Team.Remove(member);
        clients.Save(client);
    }
    else
    {
        engagement.Team.Remove(member);
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Partecipante rimosso: {member.Name}",
        AuditVisibility.All,
        "Team aggiornato");

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
    IClientRepository clients,
    IAppSettingsRepository settings,
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

    var client = clients.Get(engagement.ClientId);
    var baseList = client?.DistributionList ?? [];
    var recipients = type == "deliver"
        ? Distribution.EffectiveDeliverables(
            engagement,
            client?.DeliverablesDistributionList ?? [],
            baseList)
        : Distribution.Effective(engagement, baseList);

    if (recipients.Count == 0)
        return Results.BadRequest(type == "deliver"
            ? "Distribution list deliverables vuota."
            : "Distribution list notifiche vuota.");

    if (type != "deliver" && engagement.AcceptedProposal is null)
        return Results.BadRequest("Nessuna attività pianificata.");

    if (type == "deliver" &&
        !engagement.Reports.Any(report => report.Published))
        return Results.BadRequest("Nessun deliverable pubblicato.");

    var standard = MailTemplates.Standard(
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey));
    var resolved = MailTemplates.Resolve(
        type,
        engagement,
        standard,
        client?.MailTemplates,
        clock.Today);

    var recipientList = string.Join(
        "; ",
        recipients.Select(entry => entry.Email));

    engagement.MailLog.Add(new MailLogEntry(
        $"M-{Guid.NewGuid():N}"[..10],
        type,
        clock.UtcNow,
        recipientList,
        false,
        "All",
        resolved.Subject,
        resolved.Body,
        resolved.Source));

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Email '{type}' inviata alla distribution list",
        AuditVisibility.All,
        $"Email ricevuta: {type}");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", recipients = recipientList });
});

api.MapGet("/engagements/{id}/mail-templates", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IAppSettingsRepository settings,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    // I template non sono visibili a Cliente, PMO e Auditor.
    if (user.Role is not (Roles.Manager or Roles.Pentester))
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var client = clients.Get(engagement.ClientId);
    var standardOverrides =
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey) ??
        new MailTemplateSet();
    var standard = MailTemplates.Standard(standardOverrides);

    var templates = new[] { "start", "end", "deliver" }.Select(type =>
    {
        var resolved = MailTemplates.Resolve(
            type,
            engagement,
            standard,
            client?.MailTemplates,
            clock.Today);

        return new
        {
            Type = type,
            Standard = standard.For(type),
            StandardCustom = standardOverrides.For(type) is { IsBlank: false },
            Client = client?.MailTemplates.For(type),
            Engagement = engagement.MailTemplates.For(type),
            Effective = new
            {
                resolved.Subject,
                resolved.Body,
                resolved.Source
            }
        };
    });

    return Results.Ok(new
    {
        Placeholders = MailTemplates.Placeholders,
        CanEditStandard = user.Role == Roles.Manager,
        CanEditClient = user.Role == Roles.Manager,
        CanEditEngagement = user.Role is Roles.Manager or Roles.Pentester,
        Templates = templates
    });
});

api.MapPut("/engagements/{id}/mail-templates/{type}", (
    string id,
    string type,
    MailTemplateUpdateRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IAppSettingsRepository settings) =>
{
    var user = GetUser(context);

    if (user.Role is not (Roles.Manager or Roles.Pentester))
        return Forbidden();

    if (type is not ("start" or "end" or "deliver"))
        return Results.BadRequest("Tipo email non valido.");

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var scope = (request.Scope ?? "engagement")
        .Trim()
        .ToLowerInvariant();

    if (scope is not ("standard" or "client" or "engagement"))
        return Results.BadRequest("Ambito non valido.");

    // Solo il Manager modifica template standard e di cliente; il Pentester
    // può personalizzare esclusivamente il template dell'engagement.
    if (scope != "engagement" && user.Role != Roles.Manager)
        return Forbidden();

    var subject = request.Subject?.Trim() ?? string.Empty;
    var body = request.Body?.Trim() ?? string.Empty;
    var clear = request.Reset || (subject.Length == 0 && body.Length == 0);

    var template = clear
        ? null
        : new MailTemplate { Subject = subject, Body = body };

    switch (scope)
    {
        case "standard":
        {
            var overrides =
                settings.Get<MailTemplateSet>(MailTemplates.StandardKey) ??
                new MailTemplateSet();
            overrides.Set(type, template);
            settings.Save(MailTemplates.StandardKey, overrides);
            break;
        }
        case "client":
        {
            var client = clients.Get(engagement.ClientId);

            if (client is null)
                return Results.NotFound();

            client.MailTemplates.Set(type, template);
            clients.Save(client);
            break;
        }
        default:
            engagement.MailTemplates.Set(type, template);
            break;
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Template email '{type}' aggiornato ({scope})",
        AuditVisibility.ProviderOnly,
        "Template email aggiornato");

    repository.Save(engagement);
    return Results.Ok(new
    {
        status = "ok",
        scope,
        reset = clear
    });
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
        repository.List(ClientScope(user), SeesAllClients(user)),
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
    IClientRepository clients,
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

    var recipients = Distribution.Effective(
        engagement,
        clients.Get(engagement.ClientId)?.DistributionList ?? []);

    if (recipients.Count == 0)
        return Results.BadRequest("Distribution list notifiche vuota.");

    engagement.MailLog.Add(new MailLogEntry(
        $"M-{Guid.NewGuid():N}"[..10],
        $"solicitation:{parts[1]}",
        clock.UtcNow,
        string.Join(
            "; ",
            recipients.Select(entry => entry.Email)),
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
        // Ruolo assente o non riconosciuto: si applica il profilo meno
        // privilegiato invece di assumere un utente interno.
        role = Roles.Client;
    }

    var clientId = context.Request.Query["client"].FirstOrDefault()?.Trim();

    if (role == Roles.Client)
    {
        // Demo: il cliente è un'identità fissa lato server. Il parametro
        // `client` è ignorato per un profilo Cliente, così il chiamante non
        // può scegliere a quale cliente accedere.
        clientId = "contoso";
    }
    else if (string.IsNullOrWhiteSpace(clientId))
    {
        clientId = null;
    }

    var name =
        context.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"]
            .FirstOrDefault()
        ?? "Demo User";

    return new UserContext(name, clientId, role);
}

static Engagement? Find(
    IEngagementRepository repository,
    string id,
    UserContext user) =>
    repository.Get(id, ClientScope(user), SeesAllClients(user));

// Un utente interno senza cliente selezionato vede tutti i clienti;
// il cliente vede solo il proprio.
static string ClientScope(UserContext user) =>
    user.ClientId ?? string.Empty;

static bool SeesAllClients(UserContext user) =>
    user.IsInternal && string.IsNullOrEmpty(user.ClientId);

static bool SelectsDeliverables(HttpContext context) =>
    string.Equals(
        context.Request.Query["kind"].FirstOrDefault(),
        "deliverables",
        StringComparison.OrdinalIgnoreCase);

// Accetta "https://example.com", "www.example.com" o "example.com" e
// restituisce l'URL normalizzato con schema. Vuoto = nessun sito.
static bool TryNormalizeWebsite(string? value, out string? website)
{
    website = null;

    if (string.IsNullOrWhiteSpace(value))
        return true;

    var text = value.Trim();

    if (!text.Contains("://", StringComparison.Ordinal))
        text = "https://" + text;

    if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp &&
         uri.Scheme != Uri.UriSchemeHttps) ||
        string.IsNullOrWhiteSpace(uri.Host) ||
        !uri.Host.Contains('.', StringComparison.Ordinal))
    {
        return false;
    }

    website = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    return true;
}

static IResult Forbidden() =>
    Results.StatusCode(StatusCodes.Status403Forbidden);

// Scrittura sulla distribution list: il cliente gestisce i propri
// destinatari, PMO coordina l'intera lista. Gli altri profili
// (Pentester, Manager, Auditor) sono in sola lettura.
static bool CanWrite(UserContext user) =>
    user.Role is Roles.Client or Roles.PMO;

static bool CanSeeCredentials(UserContext user) =>
    EngagementWorkflow.CanSeeCredentials(user.Role);

static bool CanEditPreparation(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow) =>
    EngagementWorkflow.CanEditPreparation(
        engagement,
        user.Role,
        workflow.Resolve(engagement));

static bool CanEditCredentials(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow) =>
    EngagementWorkflow.CanEditCredentials(
        engagement,
        user.Role,
        workflow.Resolve(engagement));

static bool CanEditFinancials(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow) =>
    EngagementWorkflow.CanEditFinancials(
        engagement,
        user.Role,
        workflow.Resolve(engagement));

static bool CanApproveFinancials(
    Engagement engagement,
    UserContext user,
    EngagementWorkflow workflow) =>
    EngagementWorkflow.CanApproveFinancials(
        engagement,
        user.Role,
        workflow.Resolve(engagement));

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
        engagement.ClientId,
        engagement.Name,
        engagement.ClientName,
        engagement.Type,
        engagement.Owner,
        Phase = phase,
        StepIndex = EngagementWorkflow.StepIndex(phase),
        Todo = todos.Resolve(engagement, user.Role),
        ActionState = todos.ResolveAction(
            engagement,
            user.Role,
            clock.Today),
        PreparationAlert = EngagementWorkflow.PreparationAlertFor(
            engagement,
            phase,
            clock.Today),
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
    IDateProvider clock,
    IReadOnlyList<DistributionEntry> baseDistribution,
    IReadOnlyList<DistributionEntry> baseDeliverables,
    IReadOnlyList<TeamMember> baseTeam)
{
    var phase = workflow.Resolve(engagement, clock.Today);

    var reports = user.Role == Roles.Client
        ? engagement.Reports.Where(report => report.Published).ToList()
        : engagement.Reports.ToList();

    var canViewFinancials = EngagementWorkflow.CanViewFinancials(user.Role);

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
            engagement.ClientId,
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
            Cost = canViewFinancials ? engagement.Cost : null,
            Margin = canViewFinancials ? engagement.Margin : null,
            FinancialsDone = engagement.FinancialsDone,
            FinancialsApproved = engagement.FinancialsApproved,
            FinancialsPending = engagement.FinancialsPending,
            FinancialsSetByRole = canViewFinancials
                ? engagement.FinancialsSetByRole
                : null,
            FinancialsSetBy = canViewFinancials
                ? engagement.FinancialsSetBy
                : null,
            FinancialsApprovedBy = canViewFinancials
                ? engagement.FinancialsApprovedBy
                : null,
            FinancialsApprovedDate = canViewFinancials
                ? engagement.FinancialsApprovedDate
                : null,
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
        ActionState = todos.ResolveAction(
            engagement,
            user.Role,
            clock.Today),
        PreparationAlert = EngagementWorkflow.PreparationAlertFor(
            engagement,
            phase,
            clock.Today),
        PlanningNotice = user.Role is Roles.Client or Roles.PMO
            ? EngagementWorkflow.PlanningNotice(engagement, clock.Today)
            : null,
        TabStates = todos.ResolveTabs(engagement, user.Role, phase),
        Audit = VisibleAudit(engagement, user),
        Scope = engagement.Scope,
        Credentials = CanSeeCredentials(user)
            ? engagement.Credentials
            : null,
        DistributionList = engagement.DistributionList,
        DistributionBase = baseDistribution,
        DistributionEffective = Distribution.Effective(
            engagement,
            baseDistribution),
        DeliverablesDistributionList =
            engagement.DeliverablesDistributionList,
        DeliverablesBase = baseDeliverables,
        DeliverablesEffective = Distribution.EffectiveDeliverables(
            engagement,
            baseDeliverables,
            baseDistribution),
        DeliverablesFallback = Distribution.UsesNotificationFallback(
            engagement,
            baseDeliverables),
        Team = engagement.Team,
        TeamBase = baseTeam,
        TeamEffective = Team.Effective(engagement, baseTeam),
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
            IsInternal = user.IsInternal,
            CanWrite = CanWrite(user),
            CanDecide = user.Role == Roles.Manager,
            CanCredentials = CanSeeCredentials(user),
            CanEditCredentials = CanEditCredentials(
                engagement, user, workflow),
            CanEditFinancials = CanEditFinancials(
                engagement, user, workflow),
            CanViewFinancials = canViewFinancials,
            CanApproveFinancials = CanApproveFinancials(
                engagement, user, workflow),
            CanInviteTeam = Team.CanInvite(user.Role),
            CanRemoveTeam = user.Role is Roles.Manager or Roles.Client,
            CanManageMailTemplates = user.Role is Roles.Manager or Roles.Pentester,
            CanManageMail = user.Role == Roles.PMO,
            CanManageSolicitations = user.Role == Roles.PMO,
            CanReview = user.Role == Roles.Manager,
            CanPublish = user.Role == Roles.Pentester,
            CanUploadManleva = user.Role is Roles.Client or Roles.PMO,
            CanManageEngagementDocs = user.Role == Roles.PMO,
            CanPropose = user.Role is Roles.Client or Roles.PMO,
            CanUploadReports = user.Role == Roles.Pentester,
            CanManageClients = user.Role == Roles.Manager,
            CanViewClients = user.IsInternal,
            ReadOnly = user.Role == Roles.Auditor
        }
    };
}

static IEnumerable<AuditView> VisibleAudit(
    Engagement engagement,
    UserContext user) =>
    engagement.AuditLog
        .Where(entry =>
            user.IsInternal ||
            entry.Visibility == AuditVisibility.All)
        .Select(entry => new AuditView(
            entry.Date,
            entry.Author,
            entry.AuthorRole,
            user.IsInternal
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
