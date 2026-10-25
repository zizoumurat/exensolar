using System.Text.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
#if DEBUG
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
#endif
builder.Services.AddSingleton<SiteStateStore>();
builder.Services.AddSingleton<ContactSubmissionStore>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "ExenSolar.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.LoginPath = "/admin/login.html";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddDataProtection();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-forms", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    var fileName = path.Trim('/') switch
    {
        "" => "index.html",
        "hakkimizda" or "hakkımızda" => "about.html",
        "hizmetlerimiz" or "services" => "our-projects.html",
        "referanslar" or "references" => "references.html",
        "medya" => "media.html",
        "iletisim" or "iletişim" or "contact" => "contact.html",
        "index.html" or "about.html" or "services.html" or "references.html" or "media.html" or "contact.html" or "our-projects.html" => path.Trim('/'),
        _ => null
    };
    if (fileName is null) { await next(); return; }
    var htmlPath = Path.Combine(app.Environment.WebRootPath, fileName);
    if (!File.Exists(htmlPath)) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    var html = await File.ReadAllTextAsync(htmlPath);
    html = html.Replace("</body>", "<script src=\"/cms.js\"></script></body>", StringComparison.OrdinalIgnoreCase);
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(html);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPost("/api/admin/login", async (HttpContext context, IConfiguration config) =>
{
    var input = await context.Request.ReadFromJsonAsync<LoginRequest>();
    var expectedUsername = config["ExenSolar:AdminUsername"] ?? config["Umbraco:CMS:Unattended:UnattendedUserName"];
    var expectedPassword = config["ExenSolar:AdminPassword"] ?? config["Umbraco:CMS:Unattended:UnattendedUserPassword"];
    var usernameMatches = !string.IsNullOrWhiteSpace(expectedUsername)
        && string.Equals(input?.Username?.Trim(), expectedUsername.Trim(), StringComparison.OrdinalIgnoreCase);
    var suppliedBytes = System.Text.Encoding.UTF8.GetBytes(input?.Password ?? "");
    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedPassword ?? "");
    if (!usernameMatches || expectedBytes.Length == 0 || !CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes)) return Results.Unauthorized();
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, expectedUsername!.Trim())], CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new { authenticated = true });
});
app.MapPost("/api/admin/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.Ok(); }).RequireAuthorization();
app.MapGet("/api/admin/state", async (SiteStateStore store) => Results.Json(await store.ReadAsync())).RequireAuthorization();
app.MapPut("/api/admin/state", async (HttpRequest request, SiteStateStore store) =>
{
    var state = await request.ReadFromJsonAsync<SiteState>();
    if (state is null) return Results.BadRequest(new { message = "İçerik okunamadı." });
    await store.SaveAsync(state);
    return Results.Ok(new { saved = true });
}).RequireAuthorization();
app.MapGet("/api/public/state", async (SiteStateStore store) => Results.Json(await store.ReadAsync()));
app.MapGet("/api/public/form-token", (IDataProtectionProvider protection) =>
{
    var protector = protection.CreateProtector("ExenSolar.PublicForms.v1");
    return Results.Ok(new { token = protector.Protect($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}") });
});
app.MapGet("/api/public/captcha", (IDataProtectionProvider protection) =>
{
    var random = Random.Shared;
    var left = random.Next(2, 10);
    var right = random.Next(1, 9);
    var protector = protection.CreateProtector("ExenSolar.PublicCaptcha.v1");
    var token = protector.Protect($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:{left + right}:{Guid.NewGuid():N}");
    return Results.Ok(new { token, question = $"{left} + {right} = ?" });
});
app.MapPost("/api/public/contact-message", (HttpRequest request, IDataProtectionProvider protection, ContactSubmissionStore store) =>
    PublicFormEndpoints.SubmitAsync("contact", request, protection, store)).RequireRateLimiting("public-forms");
app.MapPost("/api/public/quote-request", (HttpRequest request, IDataProtectionProvider protection, ContactSubmissionStore store) =>
    PublicFormEndpoints.SubmitAsync("quote", request, protection, store)).RequireRateLimiting("public-forms");
app.MapGet("/api/admin/messages/{kind}", async (string kind, ContactSubmissionStore store) =>
{
    if (kind is not ("contact" or "quote")) return Results.NotFound();
    return Results.Ok(await store.ListAsync(kind));
}).RequireAuthorization();
app.MapPost("/api/admin/messages/{id:guid}/read", async (Guid id, ContactSubmissionStore store) =>
    await store.MarkReadAsync(id) ? Results.Ok(new { updated = true }) : Results.NotFound()).RequireAuthorization();
app.MapDelete("/api/admin/messages/{id:guid}", async (Guid id, ContactSubmissionStore store) =>
    await store.DeleteAsync(id) ? Results.Ok(new { deleted = true }) : Results.NotFound()).RequireAuthorization();
app.MapPost("/api/admin/upload", async (HttpRequest request, IWebHostEnvironment env) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0)
        return Results.BadRequest(new { message = "Yüklenecek görsel seçilmedi." });
    var file = request.Form.Files[0];
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg" };
    if (!allowed.Contains(extension) || file.Length > 12 * 1024 * 1024)
        return Results.BadRequest(new { message = "JPG, PNG, WEBP, GIF veya SVG; en fazla 12 MB yükleyebilirsiniz." });
    var folder = Path.Combine(env.WebRootPath, "uploads");
    Directory.CreateDirectory(folder);
    var name = $"{Guid.NewGuid():N}{extension}";
    await using var stream = File.Create(Path.Combine(folder, name));
    await file.CopyToAsync(stream);
    return Results.Ok(new { url = $"/uploads/{name}" });
}).RequireAuthorization();

app.Run();

public static class PublicFormEndpoints
{
    public static async Task<IResult> SubmitAsync(string kind, HttpRequest request, IDataProtectionProvider protection, ContactSubmissionStore store)
    {
        var bodyLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = 16 * 1024;
        if (request.ContentLength is > 16384) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        PublicMessageRequest? input;
        try { input = await request.ReadFromJsonAsync<PublicMessageRequest>(); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { message = "Form bilgileri okunamadı." }); }
        if (input is null) return Results.BadRequest(new { message = "Form bilgileri eksik." });
        if (!string.IsNullOrWhiteSpace(input.Website)) return Results.Ok(new { sent = true });
        if (input.Human != true || !IsCaptchaValid(input, protection))
            return Results.BadRequest(new { message = "Lütfen insan doğrulamasını tamamlayın." });

        var protector = protection.CreateProtector("ExenSolar.PublicForms.v1");
        string? issuedText;
        try { issuedText = protector.Unprotect(input.Token ?? string.Empty); }
        catch (CryptographicException) { return Results.BadRequest(new { message = "Form doğrulaması geçersiz. Sayfayı yenileyip tekrar deneyin." }); }
        var issuedTimestamp = issuedText?.Split(':', 2)[0];
        if (!long.TryParse(issuedTimestamp, out var issuedAt)
            || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - issuedAt is < 1000 or > 7200000)
            return Results.BadRequest(new { message = "Form doğrulaması zaman aşımına uğradı. Sayfayı yenileyip tekrar deneyin." });

        var name = input.Name?.Trim() ?? string.Empty;
        var email = input.Email?.Trim() ?? string.Empty;
        var message = input.Message?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 160) return Results.BadRequest(new { message = "Lütfen adınızı kontrol edin." });
        if (email.Length > 254 || !IsValidEmail(email)) return Results.BadRequest(new { message = "Geçerli bir e-posta adresi girin." });
        if (message.Length is < 3 or > 4000) return Results.BadRequest(new { message = "Mesaj 3 ile 4000 karakter arasında olmalı." });
        if ((input.Phone?.Length ?? 0) > 80 || (input.Address?.Length ?? 0) > 300 || (input.ZipCode?.Length ?? 0) > 30 || (input.Referral?.Length ?? 0) > 100)
            return Results.BadRequest(new { message = "Form alanlarından biri izin verilen uzunluğu aşıyor." });

        var record = new SubmissionMessage
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = name,
            Email = email,
            Phone = input.Phone?.Trim() ?? string.Empty,
            Address = input.Address?.Trim() ?? string.Empty,
            ZipCode = input.ZipCode?.Trim() ?? string.Empty,
            Referral = input.Referral?.Trim() ?? string.Empty,
            Message = message,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };
        await store.AddAsync(record);
        return Results.Ok(new { sent = true });
    }

    private static bool IsValidEmail(string email)
    {
        try { return string.Equals(new MailAddress(email).Address, email, StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }

    private static bool IsCaptchaValid(PublicMessageRequest input, IDataProtectionProvider protection)
    {
        try
        {
            var challenge = protection.CreateProtector("ExenSolar.PublicCaptcha.v1").Unprotect(input.CaptchaToken ?? string.Empty);
            var parts = challenge.Split(':', 3);
            return parts.Length == 3
                && long.TryParse(parts[0], out var issuedAt)
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - issuedAt is >= 0 and <= 900000
                && int.TryParse(parts[1], out var expected)
                && int.TryParse(input.CaptchaAnswer, out var answer)
                && expected == answer;
        }
        catch (CryptographicException) { return false; }
    }
}

public sealed class PublicMessageRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? Referral { get; set; }
    public string? Message { get; set; }
    public string? Website { get; set; }
    public string? Token { get; set; }
    public string? CaptchaToken { get; set; }
    public string? CaptchaAnswer { get; set; }
    public bool? Human { get; set; }
}

public sealed class SubmissionMessage
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "contact";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Referral { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class ContactSubmissionStore(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("SiteDatabase")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? configuration.GetConnectionString("umbracoDbDSN")
        ?? throw new InvalidOperationException("SQL Server bağlantı dizesi bulunamadı.");

    public async Task AddAsync(SubmissionMessage message)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        const string sql = "INSERT dbo.ExenSolarMessages (Id, Kind, Name, Email, Phone, Address, ZipCode, Referral, Message, CreatedAt, IsRead) VALUES (@id, @kind, @name, @email, @phone, @address, @zip, @referral, @message, SYSUTCDATETIME(), 0)";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", message.Id);
        command.Parameters.AddWithValue("@kind", message.Kind);
        command.Parameters.AddWithValue("@name", message.Name);
        command.Parameters.AddWithValue("@email", message.Email);
        command.Parameters.AddWithValue("@phone", message.Phone);
        command.Parameters.AddWithValue("@address", message.Address);
        command.Parameters.AddWithValue("@zip", message.ZipCode);
        command.Parameters.AddWithValue("@referral", message.Referral);
        command.Parameters.AddWithValue("@message", message.Message);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<SubmissionMessage>> ListAsync(string kind)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await using var command = new SqlCommand("SELECT Id, Kind, Name, Email, Phone, Address, ZipCode, Referral, Message, CreatedAt, IsRead FROM dbo.ExenSolarMessages WHERE Kind=@kind ORDER BY IsRead, CreatedAt DESC", connection);
        command.Parameters.AddWithValue("@kind", kind);
        var rows = new List<SubmissionMessage>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new SubmissionMessage { Id=reader.GetGuid(0), Kind=reader.GetString(1), Name=reader.GetString(2), Email=reader.GetString(3), Phone=reader.GetString(4), Address=reader.GetString(5), ZipCode=reader.GetString(6), Referral=reader.GetString(7), Message=reader.GetString(8), CreatedAt=reader.GetDateTime(9), IsRead=reader.GetBoolean(10) });
        return rows;
    }

    public async Task<bool> MarkReadAsync(Guid id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await using var command = new SqlCommand("UPDATE dbo.ExenSolarMessages SET IsRead=1 WHERE Id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await using var command = new SqlCommand("DELETE dbo.ExenSolarMessages WHERE Id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static async Task EnsureTableAsync(SqlConnection connection)
    {
        const string sql = "IF OBJECT_ID('dbo.ExenSolarMessages','U') IS NULL CREATE TABLE dbo.ExenSolarMessages (Id uniqueidentifier NOT NULL PRIMARY KEY, Kind nvarchar(20) NOT NULL, Name nvarchar(160) NOT NULL, Email nvarchar(254) NOT NULL, Phone nvarchar(80) NOT NULL, Address nvarchar(300) NOT NULL, ZipCode nvarchar(30) NOT NULL, Referral nvarchar(100) NOT NULL, Message nvarchar(4000) NOT NULL, CreatedAt datetime2 NOT NULL CONSTRAINT DF_ExenSolarMessages_CreatedAt DEFAULT SYSUTCDATETIME(), IsRead bit NOT NULL CONSTRAINT DF_ExenSolarMessages_IsRead DEFAULT 0)";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class LoginRequest { public string? Username { get; set; } public string? Password { get; set; } }

public sealed class SiteStateStore(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _connectionString = configuration.GetConnectionString("SiteDatabase")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? configuration.GetConnectionString("umbracoDbDSN")
        ?? throw new InvalidOperationException("SQL Server bağlantı dizesi bulunamadı. ConnectionStrings:SiteDatabase ayarlayın.");

    public async Task<SiteState> ReadAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await using var command = new SqlCommand("SELECT StateJson FROM dbo.ExenSolarSiteState WHERE Id = 1", connection);
        var json = await command.ExecuteScalarAsync() as string;
        if (!string.IsNullOrWhiteSpace(json))
            return JsonSerializer.Deserialize<SiteState>(json, JsonOptions) ?? SiteState.CreateDefault();
        var initial = SiteState.CreateDefault();
        await SaveAsync(initial);
        return initial;
    }

    public async Task SaveAsync(SiteState state)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        const string sql = "IF EXISTS (SELECT 1 FROM dbo.ExenSolarSiteState WHERE Id=1) UPDATE dbo.ExenSolarSiteState SET StateJson=@json, UpdatedAt=SYSUTCDATETIME() WHERE Id=1 ELSE INSERT dbo.ExenSolarSiteState (Id, StateJson, UpdatedAt) VALUES (1, @json, SYSUTCDATETIME())";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@json", json);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTableAsync(SqlConnection connection)
    {
        const string sql = "IF OBJECT_ID('dbo.ExenSolarSiteState','U') IS NULL CREATE TABLE dbo.ExenSolarSiteState (Id int NOT NULL PRIMARY KEY, StateJson nvarchar(max) NOT NULL, UpdatedAt datetime2 NOT NULL)";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class SiteState
{
    public SiteSettings Settings { get; set; } = new();
    public HomeContent Home { get; set; } = new();
    public AboutContent About { get; set; } = new();
    public List<ContentRecord> Services { get; set; } = [];
    public List<ContentRecord> References { get; set; } = [];
    public List<ContentRecord> Media { get; set; } = [];
    public ContactContent Contact { get; set; } = new();

    public static SiteState CreateDefault() => new()
    {
        Settings = new() { Email = "info@exensolar.tr", Logo = "/assets/img/exen_logo.png", FooterLogo = "/assets/img/exen_logo_2.png" },
        Home = new()
        {
            Eyebrow = "EXEN SOLAR", Title = "Exen SLR Enerji Sistemleri",
            Description = "Exen SLR Enerji Sistemleri, yenilenebilir enerjinin geleceğine yön veren, sahadaki tecrübesini mühendislik gücüyle birleştiren dinamik bir enerji şirketidir. 15 yılı aşkın proje yönetimi ve saha deneyimimizle, güneş enerji santralleri, enerji nakil hatları ve trafo merkezleri alanlarında yenilikçi, güvenilir ve sürdürülebilir çözümler sunuyoruz. Her projede; kalite, verimlilik ve iş güvenliğini merkezine alan yaklaşımımızla fark yaratıyoruz. Planlamadan devreye almaya, işletmeden bakım süreçlerine kadar tüm aşamalarda mükemmelliği hedefleyen bir iş anlayışıyla hareket ediyoruz.",
            Slides = [
                new() { Title="Enerjinin Akıllı Yol Haritası", Description="Güneş enerjisi, nakil hatları ve trafo merkezlerinde yenilikçi planlama ve kusursuz uygulama sunuyoruz.", Image="/assets/img/featured-area.jpg", ShowQuote=true },
                new() { Title="Enerjide Doğru Çözümün Adresi", Description="İşverenlerimize hem ekonomik hem sürdürülebilir, tüm aşamaları kontrol altında tutulan projeler sunuyoruz.", Image="/assets/img/featured-area-2.jpg", ShowQuote=true },
                new() { Title="Verimli Çözümler, Güçlü Sistemler", Description="Verimli Çözümler, Güçlü Sistemler", Image="/assets/img/featured-area-3.jpg", ShowQuote=true },
                new() { Title="Gücünü Güneşten Alan Gelecek", Description="Yenilenebilir enerji projelerinde mühendislik gücümüzle güvenilir ve uzun ömürlü çözümler üretiyoruz.", Image="/assets/img/featured-area-4.jpg", ShowQuote=true }
            ],
            Partners = new[] { "Huawei", "Fimer", "Sungrow", "Grup Arge" }.Select((n, i) => new ContentRecord { Title=n, Image=$"/assets/img/sponsor-{i+1}.jpg" }).ToList()
        },
        About = new() { Title="Hakkımızda", Eyebrow="EXEN SOLAR", Body="Exen SLR Enerji Sistemleri, yenilenebilir enerjinin geleceğine yön veren, sahadaki tecrübesini mühendislik gücüyle birleştiren dinamik bir enerji şirketidir. 15 yılı aşkın proje yönetimi ve saha deneyimimizle, güneş enerji santralleri, enerji nakil hatları ve trafo merkezleri alanlarında yenilikçi, güvenilir ve sürdürülebilir çözümler sunuyoruz. Her projede; kalite, verimlilik ve iş güvenliğini merkezine alan yaklaşımımızla fark yaratıyoruz.", Image1="/assets/img/about-1.jpg", Image2="/assets/img/about-2.jpg" },
        Services = [
            new() { Title="Güneş Enerji Santralleri Projelendirme ve Danışmanlık", Image="/assets/img/recent-projects-1.jpg" },
            new() { Title="Güneş Enerji Santralleri Çağrı Mektubu & Lisans Süreçleri", Image="/assets/img/recent-projects-2.jpg" },
            new() { Title="Güneş Enerji Santralleri Kurulum ve Montaj Hizmetleri", Image="/assets/img/recent-projects-3.jpg" },
            new() { Title="Yüksek Gerilim Trafo Tesisleri Kurulum ve Bakım Onarım Hizmetleri", Image="/assets/img/recent-projects-4.jpg" }
        ],
        References = [],
        Media = [],
        Contact = new() { Email="info@exensolar.tr", Address="Mücahitler Mah 52083 nolu Sk. No:42 Yasem İş Merkezi Kat:7 Daire:709 Şehitkamil/Gaziantep" }
    };
}

public sealed class SiteSettings { public string Email { get; set; }=""; public string Phone { get; set; }=""; public string Address { get; set; }=""; public string Logo { get; set; }=""; public string FooterLogo { get; set; }=""; }
public sealed class HomeContent { public string Eyebrow { get; set; }=""; public string Title { get; set; }=""; public string Description { get; set; }=""; public List<ContentRecord> Slides { get; set; }=[]; public List<ContentRecord> Partners { get; set; }=[]; }
public sealed class AboutContent { public string Title { get; set; }=""; public string Eyebrow { get; set; }=""; public string Body { get; set; }=""; public string Image1 { get; set; }=""; public string Image2 { get; set; }=""; }
public sealed class ContactContent { public string Email { get; set; }=""; public string Phone { get; set; }=""; public string Address { get; set; }=""; }
public sealed class ContentRecord { public string Id { get; set; }=Guid.NewGuid().ToString("N"); public string Title { get; set; }=""; public string Description { get; set; }=""; public string Image { get; set; }=""; public string Link { get; set; }=""; public bool ShowQuote { get; set; }=true; }
