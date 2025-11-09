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

var conn = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=chatapp.db";

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(conn));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

/* ============================================================
   ✅ SWAGGER — PRODUCTION'DA DA AÇIK
   ============================================================ */
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

/* ============================================================
   ✅ SENTIMENT SERVİSİ
   ============================================================ */

async Task<string> AnalyzeSentimentAsync(string text)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

    var primaryUrl = "https://ozgur1-sentiment-analyzer.hf.space/api/predict";
    var fallbackUrl = "https://ozgur1-sentiment-analyzer.hf.space/run/predict";

    var payload = new { data = new[] { text } };

    async Task<(bool ok, string label)> TryCall(string url)
    {
        try
        {
            var resp = await client.PostAsJsonAsync(url, payload);
            if (!resp.IsSuccessStatusCode) return (false, "neutral");

            var raw = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty("data", out var arr))
            {
                var label = arr[0].GetProperty("label").GetString() ?? "neutral";
                return (true,
                    label.ToLower().Contains("pos") ? "positive" :
                    label.ToLower().Contains("neg") ? "negative" : "neutral"
                );
            }
        }
        catch { }

        return (false, "neutral");
    }

    var a = await TryCall(primaryUrl);
    if (a.ok) return a.label;

    var b = await TryCall(fallbackUrl);
    if (b.ok) return b.label;

    return "neutral";
}

string GetEmoji(string s) => s switch
{
    "positive" => "😃",
    "negative" => "😠",
    _ => "😐"
};

/* ============================================================
   ✅ USER API’LERİ
   ============================================================ */

app.MapPost("/api/users/register", async (AppDbContext db, UserCreateDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Nickname))
        return Results.BadRequest("Nickname boş olamaz.");

    if (await db.Users.AnyAsync(u => u.Nickname == dto.Nickname))
        return Results.Conflict(new { message = "Bu rumuz zaten kullanılıyor." });

    var user = new User { Nickname = dto.Nickname };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", user);
}).WithOpenApi();

app.MapPost("/api/users/login", async (AppDbContext db, UserCreateDto dto) =>
{
    var u = await db.Users.FirstOrDefaultAsync(x => x.Nickname == dto.Nickname);
    return u is null ? Results.NotFound("Kullanıcı bulunamadı.") : Results.Ok(u);
}).WithOpenApi();

app.MapGet("/api/users", async (AppDbContext db) =>
{
    return Results.Ok(await db.Users.OrderBy(u => u.Id).ToListAsync());
}).WithOpenApi();

/* ============================================================
   ✅ GET/CREATE CONVERSATION
   ============================================================ */

async Task<Conversation> GetOrCreateConversation(AppDbContext db, int a, int b)
{
    var convId = await db.ConversationMembers
        .Where(cm => cm.UserId == a || cm.UserId == b)
        .GroupBy(cm => cm.ConversationId)
        .Where(g => g.Select(x => x.UserId).Distinct().Count() == 2)
        .Select(g => g.Key)
        .FirstOrDefaultAsync();

    if (convId != 0)
        return await db.Conversations.FindAsync(convId) ?? new Conversation();

    var conv = new Conversation();
    db.Conversations.Add(conv);
    await db.SaveChangesAsync();

    db.ConversationMembers.Add(new ConversationMember { ConversationId = conv.Id, UserId = a });
    db.ConversationMembers.Add(new ConversationMember { ConversationId = conv.Id, UserId = b });
    await db.SaveChangesAsync();

    return conv;
}

/* ============================================================
   ✅ MESAJ GÖNDER
   ============================================================ */

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

/* ============================================================
   ✅ SOL PANEL — TÜM KONVOYU DÖNER
   ============================================================ */

app.MapGet("/api/conversations/of-user/{userId}", async (AppDbContext db, int userId) =>
{
    var convIds = await db.ConversationMembers
        .Where(c => c.UserId == userId)
        .Select(c => c.ConversationId)
        .Distinct()
        .ToListAsync();

    var result = new List<object>();

    foreach (var cid in convIds)
    {
        var other = await db.ConversationMembers
            .Where(cm => cm.ConversationId == cid && cm.UserId != userId)
            .Join(db.Users, cm => cm.UserId, u => u.Id,
                (cm, u) => new { u.Id, u.Nickname })
            .FirstOrDefaultAsync();

        var last = await db.Messages
            .Where(m => m.ConversationId == cid)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new { m.Content, m.SentAt, m.SenderId })
            .FirstOrDefaultAsync();

        result.Add(new
        {
            conversationId = cid,
            otherUser = other,
            lastMessage = last?.Content,
            lastSentAt = last?.SentAt,
            lastSenderId = last?.SenderId
        });
    }

    return Results.Ok(result.OrderByDescending(r =>
        (DateTime?)r.GetType().GetProperty("lastSentAt")!.GetValue(r)
    ));
}).WithOpenApi();

/* ============================================================
   ✅ BİR KONVOYU MESAJLARI
   ============================================================ */

app.MapGet("/api/messages/conversation/{conversationId}", async (AppDbContext db, int conversationId) =>
{
    var msgs = await db.Messages
        .Where(m => m.ConversationId == conversationId)
        .OrderBy(m => m.SentAt)
        .ToListAsync();

    return Results.Ok(msgs);
}).WithOpenApi();

app.Run();

/* ============================================================
   ✅ MODELLER
   ============================================================ */

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
