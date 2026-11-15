using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    builder.Services.AddScoped<IUserRepository, SqlUserRepository>();
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
    builder.Services.AddSingleton<IUserRepository,
        InMemoryUserRepository>();
}

builder.Services.AddSingleton<EngagementWorkflow>();
builder.Services.AddSingleton<TodoResolver>();
builder.Services.AddSingleton<SolicitationService>();
builder.Services.AddScoped<ActivityTypes>();
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

// Sonda di readiness: dice se lo storage risponde, senza esporre dati.
app.MapGet("/health/ready", (IEngagementRepository repository) =>
{
    repository.List(string.Empty, true);

    return Results.Ok(new
    {
        status = "ready",
        storage = useDatabase ? "azure-sql" : "in-memory"
    });
});

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
    var accessibleEngagements = Access.AccessibleEngagements(repository, user);

    var result = clients.List()
        .Where(client =>
            Access.CanAccessClient(user, client.Id) ||
            accessibleEngagements.Any(engagement =>
                engagement.ClientId == client.Id))
        .Select(client =>
        {
            var full = Access.CanAccessClient(user, client.Id);

            return new
            {
                client.Id,
                client.Name,
                Website = full ? client.Website : null,
                client.CreatedDate,
                Users = full ? client.Users : [],
                DistributionList = full ? client.DistributionList : [],
                DeliverablesDistributionList = full
                    ? client.DeliverablesDistributionList
                    : [],
                Team = full ? client.Team : [],
                CanAssign = full,
                EngagementCount = accessibleEngagements
                    .Count(engagement => engagement.ClientId == client.Id)
            };
        });

    return Results.Ok(result);
});

// Directory utenti usata dal selettore demo. In produzione l'identità
// arriva da Entra ID e questo endpoint non viene esposto.
api.MapGet("/demo/users", (
    HttpContext context,
    IUserRepository users,
    IClientRepository clients) =>
{
    var current = GetUser(context);
    var internalUser = current.Role != Roles.Client;
    var result = new List<object>();

    if (internalUser)
    {
        foreach (var account in users.List().Where(user => user.Active))
        {
            result.Add(new
            {
                account.Id,
                account.Name,
                account.Email,
                account.Role,
                ClientId = (string?)null
            });
        }
    }

    foreach (var client in clients.List().Where(client =>
        internalUser || client.Id == current.ClientId))
    {
        foreach (var clientUser in client.Users)
        {
            result.Add(new
            {
                clientUser.Id,
                clientUser.Name,
                clientUser.Email,
                Role = Roles.Client,
                ClientId = (string?)client.Id
            });
        }
    }

    return Results.Ok(result);
});

api.MapGet("/users", (
    HttpContext context,
    IUserRepository users,
    IClientRepository clients,
    IEngagementRepository engagements) =>
{
    var current = GetUser(context);

    if (current.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var allEngagements = engagements.List(string.Empty, true);
    var accessibleEngagements = Access.AccessibleEngagements(
        engagements,
        current);

    HashSet<string>? allowedClients = null;
    HashSet<string>? allowedEngagements = null;

    if (current.Role != Roles.Manager)
    {
        allowedEngagements = accessibleEngagements
            .Select(engagement => engagement.Id)
            .ToHashSet(StringComparer.Ordinal);
        allowedClients = accessibleEngagements
            .Select(engagement => engagement.ClientId)
            .Concat(clients.List()
                .Where(client => Access.CanAccessClient(current, client.Id))
                .Select(client => client.Id))
            .ToHashSet(StringComparer.Ordinal);
    }

    var providerUsers = users.List().Select(account =>
        MapUser(
            account,
            clients,
            allEngagements,
            allowedClients,
            allowedEngagements));
    var clientUsers = clients.List()
        .Where(client =>
            allowedClients is null || allowedClients.Contains(client.Id))
        .SelectMany(client =>
            client.Users.Select(user =>
                MapClientUser(user, client, allEngagements)));

    return Results.Ok(providerUsers.Concat(clientUsers));
});

api.MapPost("/users", (
    CreateUserRequest request,
    HttpContext context,
    IUserRepository users) =>
{
    var current = GetUser(context);

    if (current.Role != Roles.Manager)
        return Forbidden();

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();
    var role = request.Role?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) || !IsEmail(email) ||
        role is not (
            Roles.Manager or
            Roles.PMO or
            Roles.Pentester or
            Roles.Auditor))
    {
        return Results.BadRequest(
            "Nome, email valida e ruolo interno sono obbligatori.");
    }

    if (users.List().Any(user =>
        string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict();

    var created = users.Add(name, email, role);
    return Results.Ok(created);
});

api.MapPut("/users/{id}", (
    string id,
    UpdateUserRequest request,
    HttpContext context,
    IUserRepository users) =>
{
    var current = GetUser(context);

    if (current.Role != Roles.Manager)
        return Forbidden();

    var account = users.Get(id);

    if (account is null)
        return Results.NotFound();

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();
    var role = request.Role?.Trim();

    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !IsEmail(email))
    {
        return Results.BadRequest("Nome ed email valida sono obbligatori.");
    }

    if (role is not (
        Roles.Manager or
        Roles.PMO or
        Roles.Pentester or
        Roles.Auditor))
    {
        return Results.BadRequest("Ruolo interno non valido.");
    }

    if (users.List().Any(user =>
        user.Id != id &&
        string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("Esiste già un utente con questa email.");

    // Nessuno si toglie da solo l'accesso o si declassa.
    if (current.UserId == account.Id &&
        (role != Roles.Manager || request.Active == false))
    {
        return Results.BadRequest(
            "Non puoi cambiare il tuo ruolo né disattivare il tuo accesso.");
    }

    account.Name = name;
    account.Email = email;
    account.Role = role;

    if (request.Active.HasValue)
        account.Active = request.Active.Value;

    // Il Manager vede tutto: le assegnazioni esplicite sono ridondanti.
    if (role == Roles.Manager)
    {
        account.ClientIds.Clear();
        account.EngagementIds.Clear();
    }

    users.Save(account);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/users/{id}/assignments", (
    string id,
    UserAssignmentRequest request,
    HttpContext context,
    IUserRepository users,
    IClientRepository clients,
    IEngagementRepository engagements) =>
{
    var current = GetUser(context);

    if (current.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var account = users.Get(id);

    if (account is null)
        return Results.NotFound();

    if (account.Role == Roles.Manager)
        return Results.BadRequest(
            "Il Manager vede già tutti i clienti e gli engagement.");

    var type = request.Type?.Trim().ToLowerInvariant();
    var targetId = request.TargetId?.Trim();

    if (string.IsNullOrWhiteSpace(targetId))
        return Results.BadRequest("Destinazione mancante.");

    if (type == "client")
    {
        var targetClient = clients.Get(targetId);

        if (targetClient is null)
            return Results.NotFound();

        if (!Access.CanAccessClient(current, targetClient.Id))
            return Forbidden();

        if (!account.ClientIds.Contains(targetId))
            account.ClientIds.Add(targetId);

        // Un engagement esplicito sotto un cliente già assegnato è ridondante.
        account.EngagementIds.RemoveAll(engagementId =>
            engagements.List(string.Empty, true)
                .Any(item => item.Id == engagementId &&
                    item.ClientId == targetId));
    }
    else if (type == "engagement")
    {
        var engagement = engagements.Get(targetId, string.Empty, true);

        if (engagement is null)
            return Results.NotFound();

        if (!Access.CanAccess(current, engagement))
            return Forbidden();

        if (account.ClientIds.Contains(engagement.ClientId))
            return Results.Conflict();

        if (!account.EngagementIds.Contains(targetId))
            account.EngagementIds.Add(targetId);
    }
    else
    {
        return Results.BadRequest("Tipo assegnazione non valido.");
    }

    users.Save(account);
    return Results.Ok(new { status = "ok" });
});

api.MapDelete("/users/{id}/assignments/{type}/{targetId}", (
    string id,
    string type,
    string targetId,
    HttpContext context,
    IUserRepository users,
    IEngagementRepository engagements) =>
{
    var current = GetUser(context);

    if (current.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var account = users.Get(id);

    if (account is null)
        return Results.NotFound();

    if (string.Equals(type, "client", StringComparison.OrdinalIgnoreCase))
    {
        if (!Access.CanAccessClient(current, targetId))
            return Forbidden();

        account.ClientIds.Remove(targetId);
    }
    else if (string.Equals(type, "engagement", StringComparison.OrdinalIgnoreCase))
    {
        var engagement = engagements.Get(targetId, string.Empty, true);

        if (engagement is null)
            return Results.NotFound();

        if (!Access.CanAccess(current, engagement))
            return Forbidden();

        account.EngagementIds.Remove(targetId);
    }

    users.Save(account);
    return Results.NoContent();
});

api.MapGet("/activity-types", (
    HttpContext context,
    ActivityTypes activities) =>
{
    var current = GetUser(context);

    return current.Role == Roles.Client
        ? Forbidden()
        : Results.Ok(activities.All());
});

api.MapPut("/activity-types/{key}", (
    string key,
    ActivityTypeRequest request,
    HttpContext context,
    ActivityTypes activities) =>
{
    var current = GetUser(context);

    if (current.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    if (request.Reset)
        return activities.Reset(key)
            ? Results.Ok(new { status = "ok", reset = true })
            : Results.NotFound();

    if (!activities.Save(
        new ActivityTypeDefinition(
            key,
            request.Label ?? string.Empty,
            request.Short ?? string.Empty,
            request.Description ?? string.Empty,
            request.RetestPlanned,
            request.DeliverableTypes ?? []),
        out var error))
    {
        return error == "Tipo di attività non previsto."
            ? Results.NotFound()
            : Results.BadRequest(error);
    }

    return Results.Ok(new { status = "ok", reset = false });
});

api.MapGet("/admin/mailing-lists", (
    HttpContext context,
    IClientRepository clients,
    IEngagementRepository engagements,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var current = GetUser(context);

    // Il Pentester entra solo per il testo delle email dell'engagement: vede
    // gli engagement assegnati, non le liste base dei clienti.
    if (current.Role is not (Roles.Manager or Roles.PMO or Roles.Pentester))
        return Forbidden();

    var result = new List<object>();
    var includeClients = current.Role is Roles.Manager or Roles.PMO;

    foreach (var client in clients.List().Where(client =>
        includeClients && Access.CanAccessClient(current, client.Id)))
    {
        result.Add(new
        {
            Id = $"client:{client.Id}:notifications",
            Scope = "client",
            OwnerId = client.Id,
            OwnerName = client.Name,
            ClientId = client.Id,
            ClientName = client.Name,
            EngagementId = (string?)null,
            EngagementName = (string?)null,
            Kind = "notifications",
            KindLabel = "Notifiche",
            Recipients = client.DistributionList,
            Base = Array.Empty<DistributionEntry>(),
            Effective = client.DistributionList
        });
        result.Add(new
        {
            Id = $"client:{client.Id}:deliverables",
            Scope = "client",
            OwnerId = client.Id,
            OwnerName = client.Name,
            ClientId = client.Id,
            ClientName = client.Name,
            EngagementId = (string?)null,
            EngagementName = (string?)null,
            Kind = "deliverables",
            KindLabel = "Deliverables",
            Recipients = client.DeliverablesDistributionList,
            Base = Array.Empty<DistributionEntry>(),
            Effective = client.DeliverablesDistributionList
        });
    }

    foreach (var engagement in Access.VisibleEngagements(
        engagements,
        current))
    {
        var client = clients.Get(engagement.ClientId);
        var baseNotifications = client?.DistributionList ?? [];
        var baseDeliverables =
            client?.DeliverablesDistributionList ?? [];

        result.Add(new
        {
            Id = $"engagement:{engagement.Id}:notifications",
            Scope = "engagement",
            OwnerId = engagement.Id,
            OwnerName = $"{engagement.Id} · {engagement.Name}",
            ClientId = engagement.ClientId,
            ClientName = engagement.ClientName,
            EngagementId = engagement.Id,
            EngagementName = engagement.Name,
            // Servono alla pastiglia dell'engagement nella mailing list.
            EngagementType = engagement.Type.ToString(),
            StepIndex = EngagementWorkflow.StepIndex(
                workflow.Resolve(engagement, clock.Today)),
            StepCount = engagement.RetestPlanned ? 9 : 7,
            Kind = "notifications",
            KindLabel = "Notifiche",
            Recipients = engagement.DistributionList,
            Base = baseNotifications,
            Effective = Distribution.Effective(
                engagement,
                baseNotifications)
        });
        result.Add(new
        {
            Id = $"engagement:{engagement.Id}:deliverables",
            Scope = "engagement",
            OwnerId = engagement.Id,
            OwnerName = $"{engagement.Id} · {engagement.Name}",
            ClientId = engagement.ClientId,
            ClientName = engagement.ClientName,
            EngagementId = engagement.Id,
            EngagementName = engagement.Name,
            // Servono alla pastiglia dell'engagement nella mailing list.
            EngagementType = engagement.Type.ToString(),
            StepIndex = EngagementWorkflow.StepIndex(
                workflow.Resolve(engagement, clock.Today)),
            StepCount = engagement.RetestPlanned ? 9 : 7,
            Kind = "deliverables",
            KindLabel = "Deliverables",
            Recipients = engagement.DeliverablesDistributionList,
            Base = baseDeliverables,
            Effective = Distribution.EffectiveDeliverables(
                engagement,
                baseDeliverables,
                baseNotifications)
        });
    }

    return Results.Ok(result);
});

api.MapGet("/admin/mail-templates", (
    HttpContext context,
    IClientRepository clients,
    IAppSettingsRepository settings) =>
{
    var current = GetUser(context);

    if (current.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var standardOverrides =
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey) ??
        new MailTemplateSet();
    var standard = MailTemplates.Standard(standardOverrides);
    var types = MailTemplates.Types.Select(item => item.Key).ToList();

    return Results.Ok(new
    {
        Placeholders = MailTemplates.Placeholders,
        Types = MailTemplates.Types.Select(item => new
        {
            item.Key,
            item.Label,
            item.Group,
            item.Recipient,
            item.When
        }),
        standard = types.Select(type => new
        {
            Type = type,
            Subject = standard.For(type)?.Subject ?? string.Empty,
            Body = standard.For(type)?.Body ?? string.Empty,
            SubjectEn = standard.For(type)?.SubjectEn ?? string.Empty,
            BodyEn = standard.For(type)?.BodyEn ?? string.Empty,
            StandardCustom = standardOverrides.For(type) is { IsBlank: false }
        }),
        engagements = Access.VisibleEngagements(
                context.RequestServices.GetRequiredService<IEngagementRepository>(),
                current)
            .Select(engagement => new
            {
                engagement.Id,
                engagement.Name,
                engagement.ClientName
            }),
        clients = clients.List()
            .Where(client =>
                current.Role == Roles.Manager ||
                Access.CanAccessClient(current, client.Id))
            .Select(client => new
            {
                client.Id,
                client.Name,
                Templates = types.Select(type => new
                {
                    Type = type,
                    Subject = client.MailTemplates.For(type)?.Subject
                        ?? string.Empty,
                    Body = client.MailTemplates.For(type)?.Body
                        ?? string.Empty,
                    SubjectEn = client.MailTemplates.For(type)?.SubjectEn
                        ?? string.Empty,
                    BodyEn = client.MailTemplates.For(type)?.BodyEn
                        ?? string.Empty
                })
            }),
        CanManage = current.Role == Roles.Manager
    });
});

api.MapGet("/admin/mail-templates/engagement/{engagementId}", (
    string engagementId,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IAppSettingsRepository settings,
    IDateProvider clock) =>
{
    var current = GetUser(context);

    if (current.Role is not (Roles.Manager or Roles.PMO or Roles.Pentester))
        return Forbidden();

    var engagement = Find(repository, engagementId, current);

    if (engagement is null)
        return Results.NotFound();

    var client = clients.Get(engagement.ClientId);
    var standard = MailTemplates.Standard(
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey));

    return Results.Ok(new
    {
        engagement.Id,
        engagement.Name,
        engagement.Language,
        Types = MailTemplates.Types.Select(item => new
        {
            item.Key,
            item.Label,
            item.Group,
            item.Recipient,
            item.When
        }),
        Templates = MailTemplates.Types.Select(item =>
        {
            // Testo che verrebbe inviato senza questo override: serve a far
            // vedere cosa si eredita quando i campi sono vuoti.
            var inherited = MailTemplates.ResolveInherited(
                item.Key,
                engagement,
                standard,
                client?.MailTemplates,
                clock.Today);

            return new
            {
                Type = item.Key,
                Subject = engagement.MailTemplates.For(item.Key)?.Subject
                    ?? string.Empty,
                Body = engagement.MailTemplates.For(item.Key)?.Body
                    ?? string.Empty,
                SubjectEn = engagement.MailTemplates.For(item.Key)?.SubjectEn
                    ?? string.Empty,
                BodyEn = engagement.MailTemplates.For(item.Key)?.BodyEn
                    ?? string.Empty,
                Inherited = new
                {
                    inherited.Subject,
                    inherited.Body,
                    inherited.Source
                }
            };
        })
    });
});

api.MapPut("/admin/mail-templates/engagement/{engagementId}/{type}", (
    string engagementId,
    string type,
    AdminTemplateRequest request,
    HttpContext context,
    IEngagementRepository repository) =>
{
    var current = GetUser(context);

    // Il terzo livello si modifica dalla mailing list dell'engagement, dove
    // lavorano PMO e Pentester: qui non si toccano standard e cliente.
    if (current.Role is not (Roles.Manager or Roles.PMO or Roles.Pentester))
        return Forbidden();

    if (!MailTemplates.IsValidType(type))
        return Results.BadRequest("Tipo template non valido.");

    var engagement = Find(repository, engagementId, current);

    if (engagement is null)
        return Results.NotFound();

    var write = MailTemplates.Write(
        engagement.MailTemplates,
        type,
        request.Subject,
        request.Body,
        request.SubjectEn,
        request.BodyEn,
        request.Reset);

    engagement.MailTemplates = write.Set;

    engagement.AddAudit(
        current.Name,
        current.Role,
        $"Template email '{type}' aggiornato (engagement {engagement.Id})",
        AuditVisibility.ProviderOnly,
        "Template email aggiornato");

    repository.Save(engagement);

    return Results.Ok(new
    {
        status = "ok",
        scope = "engagement",
        reset = write.Reset
    });
});

api.MapPut("/admin/mail-templates/standard/{type}", (
    string type,
    AdminTemplateRequest request,
    HttpContext context,
    IAppSettingsRepository settings) =>
{
    var current = GetUser(context);

    if (current.Role != Roles.Manager)
        return Forbidden();

    if (!MailTemplates.IsValidType(type))
        return Results.BadRequest("Tipo template non valido.");

    var overrides = settings.Get<MailTemplateSet>(
        MailTemplates.StandardKey) ?? new MailTemplateSet();

    var write = MailTemplates.Write(
        overrides,
        type,
        request.Subject,
        request.Body,
        request.SubjectEn,
        request.BodyEn,
        request.Reset);

    settings.Save(MailTemplates.StandardKey, write.Set);
    return Results.Ok(new { status = "ok", reset = write.Reset });
});

api.MapPut("/admin/mail-templates/client/{clientId}/{type}", (
    string clientId,
    string type,
    AdminTemplateRequest request,
    HttpContext context,
    IClientRepository clients) =>
{
    var current = GetUser(context);

    if (current.Role != Roles.Manager)
        return Forbidden();

    if (!MailTemplates.IsValidType(type))
        return Results.BadRequest("Tipo template non valido.");

    var client = clients.Get(clientId);

    if (client is null)
        return Results.NotFound();

    var write = MailTemplates.Write(
        client.MailTemplates,
        type,
        request.Subject,
        request.Body,
        request.SubjectEn,
        request.BodyEn,
        request.Reset);

    clients.Save(client);
    return Results.Ok(new { status = "ok", reset = write.Reset });
});

api.MapPost("/clients", (
    CreateClientRequest request,
    HttpContext context,
    IClientRepository clients) =>{
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
        if (string.Equals(engagement.ClientName, account.Name, StringComparison.Ordinal))
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
        string.IsNullOrWhiteSpace(email) || !IsEmail(email))
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

    if (user.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var account = clients.Get(clientId);

    if (account is null)
        return Results.NotFound();

    if (!Access.CanAccessClient(user, account.Id))
        return Forbidden();

    var list = SelectsDeliverables(context)
        ? account.DeliverablesDistributionList
        : account.DistributionList;

    var name = request.Name?.Trim();
    var email = request.Email?.Trim();

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) || !IsEmail(email))
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

    if (user.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var account = clients.Get(clientId);

    if (account is null)
        return Results.NotFound();

    if (!Access.CanAccessClient(user, account.Id))
        return Forbidden();

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
        string.IsNullOrWhiteSpace(email) || !IsEmail(email) ||
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

    var result = Access.VisibleEngagements(repository, user)
        .Select(e => Summary(e, user, workflow, todos, clock));

    return Results.Ok(result);
});

api.MapPost("/engagements", (
    CreateEngagementRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IUserRepository users,
    EngagementWorkflow workflow,
    TodoResolver todos,
    ActivityTypes activities,
    SolicitationService solicitations,
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

    if (!Access.CanAccessClient(user, client.Id))
        return Forbidden();

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
        CreatedDate = clock.UtcNow,
        SetupDone = false,
        Language = "it",
        // Default dal tipo di attività: il Setup potrà cambiarlo.
        RetestPlanned = activities.Get(request.Type.ToString())?.RetestPlanned ?? false
    };
    engagement.DeliverableTypes.Clear();

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
            client.Team,
            users.List().Select(account => account.Email).ToList(),
            solicitations));
});

api.MapGet("/engagements/{id}", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IUserRepository users,
    EngagementWorkflow workflow,
    TodoResolver todos,
    SolicitationService solicitations,
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
        client?.Team ?? [],
        users.List().Select(account => account.Email).ToList(),
        solicitations));
});

api.MapGet("/dashboard/todo", (
    HttpContext context,
    IEngagementRepository repository,
    TodoResolver todos) =>
{
    var user = GetUser(context);

    var result = Access.VisibleEngagements(repository, user)
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
    IEngagementRepository repository) =>
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
        Roles.Client or Roles.PMO or Roles.Manager))
        return Forbidden();

    if (!engagement.HasManleva || !engagement.OrderConfirmed)
    {
        return Results.BadRequest(
            "La pianificazione si sblocca dopo manleva e ordine.");
    }

    if (!engagement.FinancialsDone || !engagement.FinancialsApproved)
    {
        return Results.BadRequest(
            "La pianificazione si sblocca dopo l'approvazione " +
            "di costi e margine.");
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

    // Il Manager non ha bisogno di approvazione: la sua proposta sostituisce
    // direttamente l'eventuale pianificazione già accettata.
    var manager = user.Role == Roles.Manager;
    var hasAccepted = engagement.AcceptedProposal is not null;

    if (manager)
    {
        foreach (var accepted in engagement.Proposals
            .Where(p => p.Status == ProposalStatus.Accepted))
        {
            accepted.Status = ProposalStatus.Replaced;
        }
    }

    var proposal = new Proposal
    {
        Id = $"P-{Guid.NewGuid():N}"[..10],
        From = user.Role == Roles.Client
            ? ProposalParty.Client
            : ProposalParty.Provider,
        By = user.Name,
        Kind = hasAccepted
            ? "resched"
            : "initial",
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Date = today,
        Note = request.Note,
        Status = manager
            ? ProposalStatus.Accepted
            : ProposalStatus.Pending
    };

    engagement.Proposals.Add(proposal);

    engagement.AddAudit(
        user.Name,
        user.Role,
        manager
            ? proposal.Kind == "resched"
                ? "Pianificazione riprogrammata dal Manager"
                : "Pianificazione confermata dal Manager"
            : proposal.Kind == "resched"
                ? "Richiesta di riprogrammazione creata"
                : "Proposta di pianificazione creata",
        AuditVisibility.All,
        manager
            ? "Pianificazione aggiornata dal Manager"
            : "Proposta di pianificazione ricevuta");

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

api.MapPut("/engagements/{id}/setup", (
    string id,
    SetupRequest request,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var user = GetUser(context);
    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (user.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var language = (request.Language ?? string.Empty)
        .Trim()
        .ToLowerInvariant();

    if (language is not ("it" or "en"))
        return Results.BadRequest("Lingua non valida: usa it o en.");

    var requested = (request.DeliverableTypes ?? [])
        .Where(type => !string.IsNullOrWhiteSpace(type))
        .Select(type => type.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (requested.Count == 0)
        return Results.BadRequest(
            "Seleziona almeno una tipologia di deliverable.");

    var canonical = new List<string>();

    foreach (var type in requested)
    {
        var match = DeliverableKinds.All.FirstOrDefault(kind =>
            string.Equals(kind, type, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return Results.BadRequest(
                $"Tipologia di deliverable non valida: {type}.");

        canonical.Add(match);
    }

    engagement.Language = language;
    engagement.DeliverableTypes.Clear();
    engagement.DeliverableTypes.AddRange(
        DeliverableKinds.All.Where(kind => canonical.Contains(kind)));
    engagement.SetupDone = true;

    // Il recheck si decide qui: resta modificabile finché il cliente non ha
    // richiesto il retest e finché l'attività non è conclusa.
    if (request.RetestPlanned is { } retestPlanned)
    {
        if (engagement.RetestRequestedDate is not null &&
            retestPlanned != engagement.RetestPlanned)
        {
            return Results.BadRequest(
                "Il retest è già stato richiesto dal cliente: il recheck non si può più cambiare.");
        }

        if (workflow.Resolve(engagement, clock.Today) == EngagementPhase.Done &&
            retestPlanned != engagement.RetestPlanned)
        {
            return Results.BadRequest(
                "L'attività è conclusa: il recheck non si può più cambiare.");
        }

        engagement.RetestPlanned = retestPlanned;
        engagement.RetestDecidedByName = user.Name;
        engagement.RetestDecidedDate = clock.UtcNow;
    }

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Setup aggiornato · lingua {language.ToUpperInvariant()} · " +
        $"{string.Join(", ", engagement.DeliverableTypes)} · " +
        (engagement.RetestPlanned ? "recheck previsto" : "senza recheck"),
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
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

    var requestedKind = request.Kind?.Trim();
    string kind;

    if (string.IsNullOrWhiteSpace(requestedKind))
    {
        kind = engagement.DeliverableTypes.FirstOrDefault() ??
            DeliverableKinds.Technical;
    }
    else
    {
        kind = DeliverableKinds.All.FirstOrDefault(candidate =>
            string.Equals(candidate, requestedKind,
                StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        if (kind.Length == 0)
            return Results.BadRequest(
                $"Tipologia di deliverable non valida: {requestedKind}.");

        if (engagement.DeliverableTypes.Count > 0 &&
            !engagement.DeliverableTypes.Any(configured =>
                string.Equals(configured, kind,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(
                $"La tipologia {kind} non è prevista dal setup.");
        }
    }

    var version = engagement.Reports
        .Where(report => string.Equals(
            report.Kind, kind, StringComparison.OrdinalIgnoreCase))
        .Select(report => report.Version)
        .DefaultIfEmpty(0)
        .Max() + 1;

    for (var index = 0; index < engagement.Reports.Count; index++)
    {
        var report = engagement.Reports[index];
        if (string.Equals(report.Kind, kind,
                StringComparison.OrdinalIgnoreCase) && report.IsLatest)
        {
            engagement.Reports[index] = report with { IsLatest = false };
        }
    }

    engagement.Reports.Add(new ReportFile(
        $"R-{Guid.NewGuid():N}"[..10],
        name,
        string.IsNullOrWhiteSpace(type) ? "PDF" : type,
        request.Size,
        clock.UtcNow,
        false,
        kind,
        version,
        true));

    var hadReview = engagement.ReviewStatus != ReviewStatus.None;
    engagement.ReviewStatus = ReviewStatus.None;

    engagement.AddAudit(
        user.Name,
        user.Role,
        hadReview
            ? $"Deliverable {kind} v{version} caricato; revisione azzerata"
            : $"Deliverable {kind} v{version} caricato come bozza",
        AuditVisibility.ProviderOnly);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", kind, version });
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

    if (user.Role == Roles.Client && !HasDemoOtp(context))
        return Results.BadRequest(
            "Verifica 2FA richiesta: inserisci un codice di 6 cifre.");

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Download {report.Name}",
        AuditVisibility.ProviderOnly,
        $"Download effettuato: {report.Name}");

    repository.Save(engagement);

    return ReportDownloadFile(
        engagement,
        report,
        user.Name,
        user.Role,
        clock,
        "Link valido 15 minuti. Copia tracciata e filigranata.");
});

api.MapPost("/engagements/{id}/reports/{reportId}/links", (
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

    if (user.Role is not (Roles.Client or Roles.PMO or Roles.Manager))
        return Forbidden();

    var report = engagement.Reports
        .FirstOrDefault(item => item.Id == reportId);

    if (report is null)
        return Results.NotFound();

    if (!report.Published)
        return Results.BadRequest(
            "Si possono generare link solo per i deliverable pubblicati.");

    var now = clock.UtcNow;
    var link = new ReportDownloadLink
    {
        Token = Guid.NewGuid().ToString("N"),
        ReportId = report.Id,
        CreatedBy = user.Name,
        CreatedAt = now,
        ExpiresAt = now.AddDays(5)
    };

    engagement.ReportLinks.Add(link);
    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Link monouso creato per {report.Name}",
        AuditVisibility.ProviderOnly,
        "Link di download monouso creato");

    repository.Save(engagement);

    return Results.Ok(new
    {
        token = link.Token,
        link.ReportId,
        reportName = report.Name,
        url = $"/api/downloads/{link.Token}",
        expiresAt = link.ExpiresAt,
        days = 5
    });
});

// Link pubblico monouso: il token è l'unica credenziale. Non richiede
// autenticazione, scade dopo 5 giorni e si invalida al primo download.
app.MapGet("/api/downloads/{token}", (
    string token,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var engagement = repository
        .List(string.Empty, allClients: true)
        .FirstOrDefault(item => item.ReportLinks.Any(link =>
            string.Equals(link.Token, token, StringComparison.Ordinal)));

    if (engagement is null)
        return Results.NotFound();

    var link = engagement.ReportLinks
        .FirstOrDefault(item => item.Token == token);

    if (link is null)
        return Results.NotFound();

    if (link.UsedAt is not null)
    {
        return Results.Text(
            "Link già utilizzato. Richiedine uno nuovo al provider.",
            "text/plain",
            Encoding.UTF8,
            StatusCodes.Status410Gone);
    }

    if (link.ExpiresAt <= clock.UtcNow)
    {
        return Results.Text(
            "Link scaduto. Richiedine uno nuovo al provider.",
            "text/plain",
            Encoding.UTF8,
            StatusCodes.Status410Gone);
    }

    var report = engagement.Reports
        .FirstOrDefault(item => item.Id == link.ReportId);

    if (report is null)
        return Results.NotFound();

    link.UsedAt = clock.UtcNow;

    engagement.AddAudit(
        "Link monouso",
        Roles.PMO,
        $"Download tramite link monouso: {report.Name}",
        AuditVisibility.All,
        $"Download effettuato tramite link monouso: {report.Name}");

    repository.Save(engagement);

    return ReportDownloadFile(
        engagement,
        report,
        "Link monouso",
        $"creato da {link.CreatedBy}",
        clock,
        "Link monouso valido 5 giorni, consumato al primo download.");
});

api.MapPost("/engagements/{id}/review/submit", (
    string id,
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

    var missingTypes = engagement.MissingReportTypes;

    if (missingTypes.Count > 0)
        return Results.BadRequest(
            "Mancano i deliverable: " + string.Join(", ", missingTypes) + ".");

    if (!engagement.Reports.Any(report => !report.Published))
        return Results.BadRequest("Nessun deliverable da inviare in revisione.");

    engagement.ReviewStatus = ReviewStatus.Pending;
    engagement.ReviewRequestedDate = clock.UtcNow;

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
    engagement.ReviewRequestedDate = null;

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

    var missingTypes = engagement.MissingReportTypes;

    if (missingTypes.Count > 0)
        return Results.BadRequest(
            "Mancano i deliverable: " + string.Join(", ", missingTypes) + ".");

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
        string.IsNullOrWhiteSpace(email) || !IsEmail(email) ||
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
        string.IsNullOrWhiteSpace(email) || !IsEmail(email) ||
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

    // Le email si inviano dal PMO: è la regola della matrice permessi e della
    // capability CanManageMail. Il Manager pubblica i report, e la pubblicazione
    // genera da sé l'email di consegna.
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

/* ====== RETEST: richiesta, date, esecuzione e invio del report ====== */

// Il retest nasce dal cliente, a fix completati: si richiede una sola volta.
api.MapPost("/engagements/{id}/retest/request", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Client)
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (!engagement.RetestPlanned)
        return Results.BadRequest(
            "Il retest non è previsto per questa attività.");

    if (engagement.RetestRequestedDate is not null)
        return Results.Conflict(
            "Il retest si può richiedere una sola volta.");

    if (!engagement.Reports.Any(report => report.Published))
        return Results.BadRequest(
            "Il retest si richiede a fix completati, dopo la pubblicazione del report.");

    engagement.RetestRequestedDate = clock.UtcNow;
    engagement.RetestRequestedByName = user.Name;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Retest richiesto dal cliente a fix completati",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

// Date del retest: proposta dal cliente o dal PMO, approvazione del Manager.
api.MapPost("/engagements/{id}/retest/propose", (
    string id,
    RetestProposalRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role is not (Roles.PMO or Roles.Client))
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (engagement.RetestRequestedDate is null)
        return Results.BadRequest(
            "Prima serve la richiesta di retest dal cliente.");

    if (!DateOnly.TryParse(request.StartDate, out var start) ||
        !DateOnly.TryParse(request.EndDate, out var end))
    {
        return Results.BadRequest(
            "Formato data non valido: usa yyyy-MM-dd per avvio e fine.");
    }

    if (end < start)
        return Results.BadRequest(
            "La data di fine non può precedere quella di avvio.");

    if (start < clock.Today)
        return Results.BadRequest(
            "L'avvio del retest non può essere nel passato.");

    if (end.DayNumber - start.DayNumber > 60)
        return Results.BadRequest(
            "Durata del retest troppo lunga: il massimo è 60 giorni.");

    // Una proposta alla volta: si attende la decisione del Manager invece di
    // sovrascrivere quella già in attesa.
    if (engagement.RetestProposals.Any(proposal =>
        proposal.Status == ProposalStatus.Pending))
    {
        return Results.Conflict(
            "C'è già una proposta di date per il retest in attesa di approvazione.");
    }

    engagement.RetestProposals.Add(new Proposal
    {
        Id = $"RE-{engagement.RetestProposals.Count + 1:00}",
        StartDate = start,
        EndDate = end,
        Status = ProposalStatus.Pending,
        Date = clock.Today,
        By = user.Name,
        Note = "Retest"
    });

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Date del retest proposte: {start:dd/MM/yyyy} – {end:dd/MM/yyyy}",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

api.MapPost("/engagements/{id}/retest/decide", (
    string id,
    RetestDecisionRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Manager)
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var pending = engagement.RetestProposals
        .FirstOrDefault(proposal => proposal.Status == ProposalStatus.Pending);

    if (pending is null)
        return Results.BadRequest("Nessuna proposta di date per il retest.");

    engagement.RetestProposals.Remove(pending);

    pending.Status = request.Approve
        ? ProposalStatus.Accepted
        : ProposalStatus.Rejected;
    pending.Reason = request.Approve
        ? null
        : "Date del retest rifiutate: serve una nuova proposta.";

    engagement.RetestProposals.Add(pending);
    engagement.AcceptedRetestProposal = request.Approve ? pending : null;

    engagement.AddAudit(
        user.Name,
        user.Role,
        request.Approve
            ? $"Date del retest approvate: {pending.StartDate:dd/MM/yyyy} – {pending.EndDate:dd/MM/yyyy}"
            : "Date del retest rifiutate: serve una nuova proposta",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

// Il Pentester conclude il retest e carica il report: nessuna revisione.
api.MapPost("/engagements/{id}/retest/report", (
    string id,
    RetestReportRequest request,
    HttpContext context,
    IEngagementRepository repository,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Pentester)
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (engagement.AcceptedRetestProposal is null)
        return Results.BadRequest(
            "Prima servono le date approvate del retest.");

    if (string.IsNullOrWhiteSpace(request.Description))
        return Results.BadRequest(
            "Serve una descrizione del report di retest.");

    // Un solo report: una volta caricato (e soprattutto dopo l'invio al
    // cliente) non si sovrascrive.
    if (engagement.RetestReportSentDate is not null)
        return Results.Conflict(
            "Il report di retest è già stato inviato al cliente: non si può sostituire.");

    if (engagement.RetestReport is not null)
        return Results.Conflict(
            "Il report di retest è già stato caricato.");

    engagement.RetestCompletedDate = clock.UtcNow;
    engagement.RetestReport = new ReportFile(
        $"RF-{Guid.NewGuid():N}"[..10],
        request.Description.Trim(),
        "PDF",
        0,
        clock.UtcNow,
        false,
        "Retest");

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Retest concluso e report caricato: {request.Description.Trim()}",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

// Invio del report di retest al cliente: nessuna approvazione richiesta.
api.MapPost("/engagements/{id}/retest/send", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IAppSettingsRepository settings,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.Pentester)
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (engagement.RetestReport is not { } report)
        return Results.BadRequest("Carica prima il report di retest.");

    if (engagement.RetestReportSentDate is not null)
        return Results.Conflict("Il report di retest è già stato inviato.");

    engagement.RetestReport = report with { Published = true };
    engagement.RetestReportSentDate = clock.UtcNow;

    var client = clients.Get(engagement.ClientId);
    var recipients = Solicitation.Recipients(
        engagement,
        [Roles.Client],
        client,
        []);

    var standard = MailTemplates.Standard(
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey));
    var resolved = MailTemplates.Resolve(
        "retestreport",
        engagement,
        standard,
        client?.MailTemplates,
        clock.Today);

    engagement.MailLog.Add(new MailLogEntry(
        $"M-{Guid.NewGuid():N}"[..10],
        "retestreport",
        clock.UtcNow,
        string.Join("; ", recipients.Select(entry => entry.Email)),
        false,
        "All",
        resolved.Subject,
        resolved.Body,
        resolved.Source));

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Report di retest inviato al cliente (senza revisione)",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

// Chiusura senza retest: il cliente non lo ha richiesto (o non si procede) e
// l'attività va comunque conclusa, senza restare in attesa per sempre.
api.MapPost("/engagements/{id}/retest/close", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    EngagementWorkflow workflow,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role is not (Roles.Manager or Roles.PMO))
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    if (workflow.Resolve(engagement, clock.Today) != EngagementPhase.Retest)
        return Results.BadRequest(
            "Il retest non è in attesa: non c'è niente da chiudere.");

    if (engagement.RetestReport is not null ||
        engagement.RetestReportSentDate is not null)
    {
        return Results.BadRequest(
            "Il retest è già stato eseguito: non c'è niente da chiudere.");
    }

    if (engagement.RetestClosedDate is not null)
        return Results.Conflict("Il retest è già stato chiuso senza esecuzione.");

    engagement.RetestClosedDate = clock.UtcNow;
    engagement.RetestClosedByName = user.Name;

    engagement.AddAudit(
        user.Name,
        user.Role,
        "Retest chiuso senza esecuzione: l'attività è conclusa",
        AuditVisibility.All);

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
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
        : Results.Ok(solicitations.All().Select(rule => new
        {
            rule.Key,
            rule.TemplateType,
            rule.TaskKey,
            rule.Label,
            rule.Recipient,
            rule.When,
            rule.ThresholdDays,
            rule.CountBusinessDays,
            rule.Enabled,
            rule.Description
        }));
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

    return solicitations.Update(key, request.Enabled)
        ? Results.Ok(solicitations.All()
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
        Access.VisibleEngagements(repository, user),
        clock.Today);

    return Results.Ok(due.Select(item => new
    {
        item.Key,
        item.RuleKey,
        item.TaskKey,
        item.Label,
        item.EngagementId,
        item.ClientName,
        item.Recipient,
        item.When,
        item.ThresholdDays,
        item.Days,
        item.DaysToStart,
        item.SentDate,
        item.SentOn,
        item.SentToday,
        canSend = user.Role == Roles.PMO && !item.SentToday
    }));
});

// Un solo sollecito al giorno per task: il registro tiene traccia dell'invio.
api.MapPost("/solicitations/{key}/send", (
    string key,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IUserRepository users,
    IAppSettingsRepository settings,
    SolicitationService solicitations,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (user.Role != Roles.PMO)
        return Forbidden();

    var parts = key.Split(':', 2);

    if (parts.Length != 2)
        return Results.BadRequest("Chiave sollecito non valida.");

    var rule = SolicitationService.Rule(parts[1]);

    if (rule is null)
        return Results.BadRequest("Regola di sollecito non valida.");

    var engagement = Find(repository, parts[0], user);

    if (engagement is null)
        return Results.NotFound();

    if (SolicitationService.LastSent(engagement, rule.TaskKey) == clock.Today)
        return Results.Conflict(
            "Hai già inviato questo sollecito oggi: se ne può mandare uno al giorno per task.");

    var due = solicitations
        .Due([engagement], clock.Today)
        .FirstOrDefault(item => item.RuleKey == rule.Key);

    var recipients = Solicitation.Recipients(
        engagement,
        [rule.Recipient],
        clients.Get(engagement.ClientId),
        users.List());

    if (recipients.Count == 0)
        return Results.BadRequest("Nessun destinatario disponibile per questo sollecito.");

    var standard = MailTemplates.Standard(
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey));
    var resolved = MailTemplates.RenderSolicitation(
        rule.TemplateType,
        engagement,
        standard,
        clients.Get(engagement.ClientId)?.MailTemplates,
        clock.Today,
        due?.Days ?? 0);

    Solicitation.Record(engagement, rule.TaskKey, resolved, recipients, clock);

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Sollecito '{rule.Label}' inviato a {rule.Recipient}",
        AuditVisibility.All,
        "Sollecito ricevuto");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok" });
});

// Sollecito manuale dall'engagement: sull'attesa in corso.
api.MapPost("/engagements/{id}/solicitations", (
    string id,
    HttpContext context,
    IEngagementRepository repository,
    IClientRepository clients,
    IUserRepository users,
    IAppSettingsRepository settings,
    SolicitationService solicitations,
    IDateProvider clock) =>
{
    var user = GetUser(context);

    if (!Roles.IsInternal(user.Role) || user.Role == Roles.Auditor)
        return Forbidden();

    var engagement = Find(repository, id, user);

    if (engagement is null)
        return Results.NotFound();

    var task = solicitations.Pending(engagement, clock.Today);

    if (task is null)
        return Results.BadRequest("Non c'è nulla in attesa su questa attività.");

    // Si sollecita qualcun altro: chi deve agire, agisce.
    if (task.OwnerRoles.Contains(user.Role))
        return Results.BadRequest("L'azione è in carico al tuo profilo.");

    if (SolicitationService.LastSent(engagement, task.Key) == clock.Today)
        return Results.Conflict(
            "Hai già inviato questo sollecito oggi: se ne può mandare uno al giorno per task.");

    var client = clients.Get(engagement.ClientId);

    var recipients = Solicitation.Recipients(
        engagement,
        task.OwnerRoles,
        client,
        users.List());

    if (recipients.Count == 0)
        return Results.BadRequest("Nessun destinatario disponibile per questo sollecito.");

    var standard = MailTemplates.Standard(
        settings.Get<MailTemplateSet>(MailTemplates.StandardKey));
    var resolved = MailTemplates.RenderSolicitation(
        "solmanual",
        engagement,
        standard,
        client?.MailTemplates,
        clock.Today,
        0,
        task.Label);

    Solicitation.Record(
        engagement,
        task.Key,
        resolved,
        recipients,
        clock);

    engagement.AddAudit(
        user.Name,
        user.Role,
        $"Sollecito manuale inviato: {task.Label}",
        AuditVisibility.All,
        "Sollecito ricevuto");

    repository.Save(engagement);
    return Results.Ok(new { status = "ok", task = task.Key });
});

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Email minima ma uniforme in tutto il portale: una @ seguita da un punto.</summary>
static bool IsEmail(string? value)
{
    var email = value?.Trim() ?? string.Empty;
    var at = email.IndexOf('@');

    return at > 0 &&
        at < email.Length - 1 &&
        email.IndexOf('.', at) > at + 1;
}

static UserContext GetUser(HttpContext context)
{
    var userId = context.Request.Query["user"].FirstOrDefault()?.Trim();

    if (!string.IsNullOrWhiteSpace(userId))
    {
        var users = context.RequestServices
            .GetService<IUserRepository>();
        var account = users?.Get(userId);
        var requestedClient = context.Request.Query["client"]
            .FirstOrDefault()?
            .Trim();

        if (account is { Active: true })
        {
            return new UserContext(
                account.Name,
                string.IsNullOrWhiteSpace(requestedClient)
                    ? null
                    : requestedClient,
                account.Role,
                account.Id,
                account.ClientIds,
                account.EngagementIds,
                Scoped: account.Role != Roles.Manager);
        }

        // Utente cliente: l'id arriva dalla tabella utenti del cliente.
        var clients = context.RequestServices
            .GetService<IClientRepository>();
        var clientAccount = clients?
            .List()
            .FirstOrDefault(client => client.Users.Any(user =>
                user.Id == userId));
        var clientUser = clientAccount?.Users
            .FirstOrDefault(user => user.Id == userId);

        if (clientUser is not null)
        {
            return new UserContext(
                clientUser.Name,
                clientAccount!.Id,
                Roles.Client,
                clientUser.Id);
        }
    }

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
        clientId = "anthropic";
    }
    else if (string.IsNullOrWhiteSpace(clientId))
    {
        clientId = null;
    }

    // Compatibilità con i link che usano solo role/client: si aggancia il
    // primo utente demo con quel ruolo, così valgono le assegnazioni.
    var directory = context.RequestServices
        .GetService<IUserRepository>();
    var demoUser = directory?
        .List()
        .FirstOrDefault(user =>
            user.Active && user.Role == role);

    if (demoUser is not null)
    {
        return new UserContext(
            demoUser.Name,
            clientId,
            role,
            demoUser.Id,
            demoUser.ClientIds,
            demoUser.EngagementIds,
            Scoped: role != Roles.Manager);
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
    UserContext user)
{
    var engagement = repository.Get(id, string.Empty, true);

    return engagement is not null && Access.CanAccess(user, engagement)
        ? engagement
        : null;
}

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
// destinatari, PMO coordina l'intera lista e il Manager può correggere
// i destinatari della reportistica. Pentester e Auditor sono in sola lettura.
static bool CanWrite(UserContext user) =>
    user.Role is Roles.Client or Roles.PMO or Roles.Manager;

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
        Owner = user.Role == Roles.Client
            ? null
            : engagement.Owner,
        Phase = phase,
        StepIndex = EngagementWorkflow.StepIndex(phase),
        // Passi totali: sette, piu i due di retest quando il recheck e previsto.
        StepCount = engagement.RetestPlanned ? 9 : 7,
        RetestPlanned = engagement.RetestPlanned,
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

/// <summary>
/// Blocco retest del dettaglio: dati e permessi per richiesta, date, esecuzione
/// e invio del report (senza revisione). Null se il recheck non è previsto e
/// non è mai stato avviato.
/// </summary>
static object? MapRetest(
    Engagement engagement,
    UserContext user,
    EngagementPhase phase,
    DateOnly today)
{
    var requested = engagement.RetestRequestedDate is not null;
    var pending = engagement.RetestProposals
        .FirstOrDefault(proposal => proposal.Status == ProposalStatus.Pending);
    var accepted = engagement.AcceptedRetestProposal;
    var sent = engagement.RetestReportSentDate is not null;

    var active = engagement.RetestPlanned || requested ||
        accepted is not null || engagement.RetestReport is not null || sent;

    if (!active)
        return null;

    return new
    {
        Planned = engagement.RetestPlanned,
        DecidedBy = engagement.RetestDecidedByName,
        DecidedDate = engagement.RetestDecidedDate?.ToString("yyyy-MM-dd"),
        Requested = requested,
        RequestedDate = engagement.RetestRequestedDate?.ToString("yyyy-MM-dd"),
        RequestedBy = engagement.RetestRequestedByName,
        Proposals = engagement.RetestProposals.Select(proposal => new
        {
            proposal.Id,
            StartDate = proposal.StartDate.ToString("yyyy-MM-dd"),
            EndDate = proposal.EndDate.ToString("yyyy-MM-dd"),
            Status = proposal.Status.ToString(),
            Date = proposal.Date.ToString("yyyy-MM-dd"),
            proposal.By,
            proposal.Note,
            proposal.Reason
        }),
        Accepted = accepted is null
            ? null
            : new
            {
                accepted.Id,
                StartDate = accepted.StartDate.ToString("yyyy-MM-dd"),
                EndDate = accepted.EndDate.ToString("yyyy-MM-dd"),
                accepted.By
            },
        CompletedDate = engagement.RetestCompletedDate?.ToString("yyyy-MM-dd"),
        Report = engagement.RetestReport is null
            ? null
            : new
            {
                engagement.RetestReport.Id,
                engagement.RetestReport.Name,
                published = engagement.RetestReport.Published,
                date = engagement.RetestReport.Date.ToString("yyyy-MM-dd")
            },
        SentDate = engagement.RetestReportSentDate?.ToString("yyyy-MM-dd"),
        ClosedDate = engagement.RetestClosedDate?.ToString("yyyy-MM-dd"),
        ClosedBy = engagement.RetestClosedByName,
        // Il flag si decide nel Setup e resta modificabile fino alla richiesta,
        // ma non su un'attività già conclusa.
        CanEditFlag = user.Role is (Roles.Manager or Roles.PMO) &&
            !requested &&
            phase != EngagementPhase.Done,
        CanClose = user.Role is (Roles.Manager or Roles.PMO) &&
            phase == EngagementPhase.Retest &&
            engagement.RetestReport is null &&
            engagement.RetestClosedDate is null,
        CanRequest = user.Role == Roles.Client &&
            engagement.RetestPlanned &&
            !requested &&
            phase == EngagementPhase.Retest,
        CanPropose = user.Role is (Roles.PMO or Roles.Client) &&
            requested && accepted is null,
        CanDecide = user.Role == Roles.Manager && pending is not null,
        CanLoadReport = user.Role == Roles.Pentester &&
            accepted is not null &&
            engagement.RetestReport is null,
        CanSend = user.Role == Roles.Pentester &&
            engagement.RetestReport is not null &&
            !sent
    };
}

static object? MapSolicitation(
    SolicitationTask? task,
    UserContext user,
    DateOnly today,
    bool canSend)
{
    if (task is null)
        return null;

    // Si sollecita qualcun altro: se l'azione è tua, non c'è niente da inviare.
    var allowed = canSend && !task.OwnerRoles.Contains(user.Role);

    return new
    {
        task.Key,
        task.Label,
        task.Recipient,
        task.SentDate,
        task.SentToday,
        CanSend = allowed && !task.SentToday,
        blockedReason = task.SentToday
            ? "Hai già inviato questo sollecito oggi: se ne può mandare uno al giorno per task."
            : task.OwnerRoles.Contains(user.Role)
                ? "L'azione è in carico al tuo profilo."
                : null
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
    IReadOnlyList<TeamMember> baseTeam,
    IReadOnlyList<string> providerEmails,
    SolicitationService solicitations)
{
    var phase = workflow.Resolve(engagement, clock.Today);

    var reports = user.Role == Roles.Client
        ? engagement.Reports.Where(report => report.Published).ToList()
        : engagement.Reports.ToList();

    var canViewFinancials = EngagementWorkflow.CanViewFinancials(user.Role);
    var isClient = user.Role == Roles.Client;

    IReadOnlyList<DistributionEntry> VisibleDistribution(
        IEnumerable<DistributionEntry> entries) =>
        isClient
            ? entries.Where(entry =>
                entry.Organization is "Cliente" or "Partner" or "Altro")
                .ToList()
            : entries.ToList();

    IReadOnlyList<TeamMember> VisibleTeam(
        IEnumerable<TeamMember> members) =>
        isClient
            ? members.Where(member =>
                member.Organization is "Cliente" or "Partner" or "Altro")
                .ToList()
            : members.ToList();

    var notificationBase = VisibleDistribution(baseDistribution);
    var notificationEngagement =
        VisibleDistribution(engagement.DistributionList);
    var deliverablesBase = VisibleDistribution(baseDeliverables);
    var deliverablesEngagement =
        VisibleDistribution(engagement.DeliverablesDistributionList);
    var notificationEffective = isClient
        ? Distribution.Combine(notificationBase, notificationEngagement)
        : Distribution.Effective(engagement, baseDistribution);
    var deliverablesEffective = isClient
        ? Distribution.Combine(deliverablesBase, deliverablesEngagement)
            is { Count: > 0 } explicitDeliverables
                ? explicitDeliverables
                : notificationEffective
        : Distribution.EffectiveDeliverables(
            engagement,
            baseDeliverables,
            baseDistribution);

    var teamBase = VisibleTeam(baseTeam);
    var team = VisibleTeam(engagement.Team);
    var teamEffective = isClient
        ? teamBase
            .Concat(team)
            .GroupBy(member => member.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList()
        : Team.Effective(engagement, baseTeam);

    var providerSet = new HashSet<string>(
        providerEmails,
        StringComparer.OrdinalIgnoreCase);
    var mailLog = (isClient
            ? engagement.MailLog.Where(entry => entry.Visibility == "All")
            : engagement.MailLog)
        .Select(entry => isClient
            ? entry with
            {
                Recipients = FilterRecipients(entry.Recipients, providerSet),
                Body = null
            }
            : entry)
        .ToList();

    return new
    {
        engagement = new
        {
            engagement.Id,
            engagement.ClientId,
            engagement.ClientName,
            engagement.Name,
            engagement.Type,
            engagement.SetupDone,
            engagement.Language,
            engagement.DeliverableTypes,
            engagement.ReportsComplete,
            engagement.MissingReportTypes,
            Owner = user.Role == Roles.Client
            ? null
            : engagement.Owner,
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
        // Passi totali: sette, piu i due di retest quando il recheck e previsto.
        StepCount = engagement.RetestPlanned ? 9 : 7,
        RetestPlanned = engagement.RetestPlanned,
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
        Solicitation = user.Role == Roles.Client
            ? null
            : MapSolicitation(
                solicitations.Pending(engagement, clock.Today),
                user,
                clock.Today,
                canSend: user.Role != Roles.Auditor),
        Retest = MapRetest(engagement, user, phase, clock.Today),
        Audit = VisibleAudit(engagement, user),
        Scope = engagement.Scope,
        Credentials = CanSeeCredentials(user)
            ? engagement.Credentials
            : null,
        DistributionList = notificationEngagement,
        DistributionBase = notificationBase,
        DistributionEffective = notificationEffective,
        DeliverablesDistributionList = deliverablesEngagement,
        DeliverablesBase = deliverablesBase,
        DeliverablesEffective = deliverablesEffective,
        DeliverablesFallback = isClient
            ? Distribution.Combine(
                deliverablesBase,
                deliverablesEngagement).Count == 0
            : Distribution.UsesNotificationFallback(
                engagement,
                baseDeliverables),
        Team = team,
        TeamBase = teamBase,
        TeamEffective = teamEffective,
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
            CanManageSetup = user.Role is Roles.Manager or Roles.PMO,
            CanManageMail = user.Role == Roles.PMO,
            CanManageSolicitations = user.Role == Roles.PMO,
            CanReview = user.Role == Roles.Manager,
            CanPublish = user.Role == Roles.Pentester,
            CanUploadManleva = user.Role is Roles.Client or Roles.PMO,
            CanManageEngagementDocs = user.Role == Roles.PMO,
            CanPropose = user.Role is Roles.Client or Roles.PMO or Roles.Manager,
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
            user.IsInternal
                ? entry.Author
                : entry.AuthorRole == Roles.Client
                    ? "Cliente"
                    : "Provider",
            user.IsInternal ? entry.AuthorRole : string.Empty,
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
    report.Published,
    report.Kind,
    report.Version,
    report.IsLatest
};

static object MapUser(
    UserAccount account,
    IClientRepository clients,
    IReadOnlyList<Engagement> engagements,
    IReadOnlySet<string>? allowedClients = null,
    IReadOnlySet<string>? allowedEngagements = null)
{
    var manager = account.Role == Roles.Manager;
    var clientIds = manager || allowedClients is null
        ? account.ClientIds.ToList()
        : account.ClientIds
            .Where(allowedClients.Contains)
            .ToList();
    var engagementIds = manager || allowedEngagements is null
        ? account.EngagementIds.ToList()
        : account.EngagementIds
            .Where(allowedEngagements.Contains)
            .ToList();

    var effectiveEngagements = manager
        ? engagements
        : engagements
            .Where(engagement =>
                clientIds.Contains(engagement.ClientId) ||
                engagementIds.Contains(engagement.Id))
            .ToList();
    var effectiveClientIds = manager
        ? clients.List().Select(client => client.Id).ToList()
        : effectiveEngagements
            .Select(engagement => engagement.ClientId)
            .Distinct()
            .ToList();

    return new
    {
        account.Id,
        account.Name,
        account.Email,
        account.Role,
        account.Active,
        account.CreatedDate,
        ClientIds = clientIds,
        EngagementIds = engagementIds,
        EffectiveClientIds = effectiveClientIds,
        EffectiveEngagementIds = effectiveEngagements
            .Select(engagement => engagement.Id)
            .ToList(),
        Clients = effectiveClientIds.Select(id => new
        {
            Id = id,
            Name = clients.Get(id)?.Name ?? id
        }),
        Engagements = effectiveEngagements.Select(engagement => new
        {
            engagement.Id,
            engagement.Name,
            engagement.ClientId
        }),
        IsManager = manager
    };
}

static object MapClientUser(
    ClientUser user,
    ClientAccount client,
    IReadOnlyList<Engagement> engagements)
{
    var clientEngagements = engagements
        .Where(engagement => engagement.ClientId == client.Id)
        .ToList();

    return new
    {
        user.Id,
        user.Name,
        user.Email,
        Role = Roles.Client,
        Active = true,
        CreatedDate = client.CreatedDate,
        ClientIds = new[] { client.Id },
        EngagementIds = Array.Empty<string>(),
        EffectiveClientIds = new[] { client.Id },
        EffectiveEngagementIds = clientEngagements
            .Select(engagement => engagement.Id)
            .ToList(),
        Clients = new[] { new { Id = client.Id, Name = client.Name } },
        Engagements = clientEngagements.Select(engagement => new
        {
            engagement.Id,
            engagement.Name,
            engagement.ClientId
        }),
        IsManager = false,
        IsClient = true
    };
}

static bool HasDemoOtp(HttpContext context)
{
    var otp = context.Request.Query["otp"].FirstOrDefault();

    return otp is { Length: 6 } && otp.All(char.IsDigit);
}

static string FilterRecipients(
    string recipients,
    IReadOnlySet<string> providerEmails) =>
    string.Join(
        "; ",
        (recipients ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value =>
                value.Length > 0 &&
                !providerEmails.Contains(value)));

static IResult ReportDownloadFile(
    Engagement engagement,
    ReportFile report,
    string actor,
    string role,
    IDateProvider clock,
    string note)
{
    var content =
        "Cyber Engagement Portal\n" +
        $"Engagement: {engagement.Id} - {engagement.Name}\n" +
        $"Deliverable: {report.Name}\n" +
        $"Utente: {actor} ({role})\n" +
        $"Generato: {clock.UtcNow:O}\n" +
        note;

    var safeName = string.Concat(
        Path.GetFileNameWithoutExtension(report.Name)
            .Split(Path.GetInvalidFileNameChars()));

    return Results.File(
        Encoding.UTF8.GetBytes(content),
        "text/plain",
        $"{safeName}.txt");
}

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
