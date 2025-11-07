using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var conn = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=chatapp.db";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(conn));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


// ✅ SENTIMENT ANALİZİ — FULL YENİ SÜRÜM
async Task<string> AnalyzeSentimentAsync(string text)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

    var primaryUrl = "https://ozgur1-sentiment-analyzer.hf.space/api/predict";
    var fallbackUrl = "https://ozgur1-sentiment-analyzer.hf.space/run/predict";

    var payload = new { data = new[] { text } };

    async Task<(bool ok, string label, int status)> TryCall(string url)
    {
        Console.WriteLine($"[HF CALL] {url}");

        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsJsonAsync(url, payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HF ERROR CALL] {ex.Message}");
            return (false, "neutral", 0);
        }

        var raw = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"[HF RESPONSE] Status {(int)resp.StatusCode} :: {raw[..Math.Min(raw.Length, 300)]}");

        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(raw))
            return (false, "neutral", (int)resp.StatusCode);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // ✅ Beklenen format: { "data": [ { "label": "..."} ] }
            if (root.TryGetProperty("data", out var dataElem) &&
                dataElem.ValueKind == JsonValueKind.Array &&
                dataElem.GetArrayLength() > 0)
            {
                var first = dataElem[0];

                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("label", out var lbl))
                {
                    var label = lbl.GetString()?.ToLower() ?? "neutral";
                    if (label.Contains("pos")) return (true, "positive", (int)resp.StatusCode);
                    if (label.Contains("neg")) return (true, "negative", (int)resp.StatusCode);
                    return (true, "neutral", (int)resp.StatusCode);
                }

                if (first.ValueKind == JsonValueKind.String)
                {
                    var label = first.GetString()?.ToLower() ?? "neutral";
                    if (label.Contains("pos")) return (true, "positive", (int)resp.StatusCode);
                    if (label.Contains("neg")) return (true, "negative", (int)resp.StatusCode);
                    return (true, "neutral", (int)resp.StatusCode);
                }
            }

            // ✅ Router formatı: [[ { "label": "POSITIVE" } ]]
            if (root.ValueKind == JsonValueKind.Array &&
                root.GetArrayLength() > 0 &&
                root[0].ValueKind == JsonValueKind.Array)
            {
                var obj = root[0][0];
                if (obj.ValueKind == JsonValueKind.Object &&
                    obj.TryGetProperty("label", out var lbl2))
                {
                    var label = lbl2.GetString()?.ToLower() ?? "neutral";
                    if (label.Contains("pos") || label.Contains("label_2")) return (true, "positive", (int)resp.StatusCode);
                    if (label.Contains("neg") || label.Contains("label_0")) return (true, "negative", (int)resp.StatusCode);
                    return (true, "neutral", (int)resp.StatusCode);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HF PARSE ERROR] {ex.Message}");
            return (false, "neutral", (int)resp.StatusCode);
        }

        return (false, "neutral", (int)resp.StatusCode);
    }

    // 1) ana endpoint
    var r1 = await TryCall(primaryUrl);
    if (r1.ok) return r1.label;

    // 503 ise Space uyuyor olabilir → tekrar dene
    if (r1.status == 503 || r1.status == 502 || r1.status == 504)
    {
        Console.WriteLine("[HF RETRY] Uyku modundan uyanıyor...");
        await Task.Delay(2500);

        var r1b = await TryCall(primaryUrl);
        if (r1b.ok) return r1b.label;
    }

    // 2) fallback endpoint
    var r2 = await TryCall(fallbackUrl);
    if (r2.ok) return r2.label;

    return "neutral";
}

string GetEmoji(string sentiment) => sentiment switch
{
    "positive" => "😃",
    "negative" => "😠",
    _ => "😐"
};


// ✅ USER REGISTER
app.MapPost("/api/users/register", async (AppDbContext db, UserCreateDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Nickname))
        return Results.BadRequest("Nickname boş olamaz.");

    if (await db.Users.AnyAsync(u => u.Nickname == dto.Nickname))
        return Results.Conflict(new { message = "Bu rumuz zaten kullanılıyor." });

    var user = new User { Nickname = dto.Nickname, CreatedAt = DateTime.UtcNow };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", user);
}).WithOpenApi();


// ✅ LOGIN
app.MapPost("/api/users/login", async (AppDbContext db, UserCreateDto dto) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Nickname == dto.Nickname);
    return user is null ? Results.NotFound("Kullanıcı bulunamadı.") : Results.Ok(user);
}).WithOpenApi();


// ✅ USER LIST
app.MapGet("/api/users", async (AppDbContext db) =>
{
    return Results.Ok(await db.Users.OrderBy(u => u.Id).ToListAsync());
}).WithOpenApi();


// ✅ CONVERSATION HELPER
async Task<Conversation> GetOrCreateConversation(AppDbContext db, int a, int b)
{
    var convId = await db.ConversationMembers
        .Where(cm => cm.UserId == a || cm.UserId == b)
        .GroupBy(cm => cm.ConversationId)
        .Where(g => g.Select(x => x.UserId).Distinct().Count() == 2)
        .Select(g => g.Key)
        .FirstOrDefaultAsync();

    if (convId != 0)
        return await db.Conversations.FindAsync(convId)
            ?? new Conversation { IsGroup = false, CreatedAt = DateTime.UtcNow };

    var conv = new Conversation { IsGroup = false, CreatedAt = DateTime.UtcNow };
    db.Conversations.Add(conv);
    await db.SaveChangesAsync();

    db.ConversationMembers.AddRange(
        new ConversationMember { ConversationId = conv.Id, UserId = a, JoinedAt = DateTime.UtcNow },
        new ConversationMember { ConversationId = conv.Id, UserId = b, JoinedAt = DateTime.UtcNow }
    );
    await db.SaveChangesAsync();

    return conv;
}


// ✅ SEND MESSAGE
app.MapPost("/api/messages/user/{receiverId}", async (AppDbContext db, int receiverId, MessageDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Content))
        return Results.BadRequest("Mesaj boş olamaz.");

    var sender = await db.Users.FindAsync(dto.SenderId);
    var receiver = await db.Users.FindAsync(receiverId);

    if (sender is null || receiver is null)
        return Results.BadRequest("Geçersiz kullanıcı.");

    var conv = await GetOrCreateConversation(db, dto.SenderId, receiverId);

    var sentiment = await AnalyzeSentimentAsync(dto.Content);
    var emoji = GetEmoji(sentiment);

    var msg = new Message
    {
        ConversationId = conv.Id,
        SenderId = dto.SenderId,
        Content = dto.Content,
        Sentiment = sentiment,
        Emoji = emoji,
        SentAt = DateTime.UtcNow
    };

    db.Messages.Add(msg);
    await db.SaveChangesAsync();

    return Results.Created($"/api/messages/{msg.Id}", msg);
}).WithOpenApi();


// ✅ GET CHAT HISTORY
app.MapGet("/api/messages/user/{userAId}/{userBId}", async (AppDbContext db, int userAId, int userBId) =>
{
    var convId = await db.ConversationMembers
        .Where(m => m.UserId == userAId || m.UserId == userBId)
        .GroupBy(m => m.ConversationId)
        .Where(g => g.Select(x => x.UserId).Distinct().Count() == 2)
        .Select(g => g.Key)
        .FirstOrDefaultAsync();

    if (convId == 0)
        return Results.Ok(new List<Message>());

    var msgs = await db.Messages
        .Where(m => m.ConversationId == convId)
        .OrderBy(m => m.SentAt)
        .ToListAsync();

    return Results.Ok(msgs);
}).WithOpenApi();

app.Run();


// ✅ MODELS
public class User
{
    public int Id { get; set; }
    public string Nickname { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record UserCreateDto(string Nickname);

public class Conversation
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsGroup { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ConversationMember
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class Message
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int SenderId { get; set; }
    public string Content { get; set; } = "";
    public string Sentiment { get; set; } = "neutral";
    public string Emoji { get; set; } = "😐";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public record MessageDto(int SenderId, string Content);

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();
}
