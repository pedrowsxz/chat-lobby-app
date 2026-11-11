using System.Linq.Expressions;
using Lobby.Web.Models;
using Lobby.Web.Services;

namespace Lobby.Web.ViewModels;

// What the hub sends to browsers and what the history partial renders.
// Also carries a small conversation summary (type and name); the demo responder uses it,
// while the browser re-fetches the sidebar for a conversation it hasn't seen yet.
public record MessageDto(
    int Id,
    int ConversationId,
    string SenderId,
    string SenderUserName,
    string SenderDisplayName,
    string? SenderAvatarFileName,
    string Body,
    DateTimeOffset SentAt,
    Guid ClientMessageId,
    ConversationType ConversationType,
    string? ConversationName)
{
    public string? SenderAvatarUrl => SenderAvatarFileName is null ? null : Display.AvatarUrl(SenderAvatarFileName);
    public string SenderInitials => Display.Initials(SenderDisplayName);
    public int SenderHue => Display.Hue(SenderId);

    // One projection used by every query, so EF selects exactly these columns in one SQL statement.
    public static readonly Expression<Func<Message, MessageDto>> FromMessage = m => new MessageDto(
        m.Id, m.ConversationId, m.SenderId, m.Sender.UserName!, m.Sender.DisplayName, m.Sender.AvatarFileName,
        m.Body, m.SentAt, m.ClientMessageId, m.Conversation.Type, m.Conversation.Name);
}

public record MessagePage(List<MessageDto> Messages, bool HasMore);
