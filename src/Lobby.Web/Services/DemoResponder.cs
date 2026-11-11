using System.Threading.Channels;
using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

// DEMO ONLY. Makes the bot and the demo group answer, so one visitor alone can see real time.
// MessageService drops every saved message here; a background loop decides whether to reply,
// and replies through the real MessageService, so the reply takes the real broadcast path.
public class DemoReplyQueue(bool enabled)
{
    // Bounded and lossy on purpose: if replies pile up, new ones are dropped instead of using memory.
    private readonly Channel<MessageDto> _channel = Channel.CreateBounded<MessageDto>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Enqueue(MessageDto message)
    {
        if (enabled) _channel.Writer.TryWrite(message);
    }

    public ChannelReader<MessageDto> Reader => _channel.Reader;
}

public class DemoResponder(DemoReplyQueue queue, IServiceScopeFactory scopes, ILogger<DemoResponder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ReplyAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Demo reply failed for message {MessageId}", message.Id);
            }
        }
    }

    private async Task ReplyAsync(MessageDto message, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var members = await db.ConversationMembers.Where(m => m.ConversationId == message.ConversationId)
            .Select(m => new { m.UserId, m.User.IsSeeded, m.User.IsDemoBot }).ToListAsync(cancellationToken);

        // Only answer people. The bot and seeded users are "IsSeeded", so replies never trigger replies.
        if (members.FirstOrDefault(m => m.UserId == message.SenderId) is not { IsSeeded: false }) return;

        string? replierId = null;
        string reply = "";
        if (message.ConversationType == ConversationType.Direct && members.FirstOrDefault(m => m.IsDemoBot) is { } bot)
        {
            // Walk through the tips in order: the n-th bot message in this chat gets tip n.
            var botMessages = await db.Messages.CountAsync(m => m.ConversationId == message.ConversationId && m.SenderId == bot.UserId, cancellationToken);
            (replierId, reply) = (bot.UserId, DemoContent.BotReplies[botMessages % DemoContent.BotReplies.Length]);
        }
        else if (message.ConversationName == DemoKit.GroupName && Random.Shared.Next(2) == 0)
        {
            var seeded = members.Where(m => m.IsSeeded && !m.IsDemoBot).ToList();
            if (seeded.Count > 0)
                (replierId, reply) = (seeded[Random.Shared.Next(seeded.Count)].UserId, DemoContent.GroupReplies[Random.Shared.Next(DemoContent.GroupReplies.Length)]);
        }
        if (replierId is null) return;

        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 3000)), cancellationToken); // feels typed
        await scope.ServiceProvider.GetRequiredService<MessageService>().SendAsync(replierId, message.ConversationId, reply, Guid.NewGuid());
    }
}
