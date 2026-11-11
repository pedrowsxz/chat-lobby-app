using Lobby.Web.Models;

namespace Lobby.Web.ViewModels;

public record PersonSummary(string Id, string DisplayName, string? AvatarFileName, bool IsDemoBot, DateTimeOffset? LastSeenAt);

public class ConversationListItem
{
    public int Id { get; init; }
    public ConversationType Type { get; init; }
    public string? GroupName { get; init; }
    public PersonSummary? Other { get; init; } // the other person, for direct chats
    public DateTimeOffset LastActivityAt { get; init; }
    public string? LastMessagePreview { get; init; }
    public int UnreadCount { get; init; }
    public bool Online { get; set; } // filled from PresenceTracker, not the database

    public string Title => Type == ConversationType.Group ? GroupName! : Other?.DisplayName ?? "Conversa";
}

public class ConversationHeader
{
    public int Id { get; init; }
    public ConversationType Type { get; init; }
    public string? Name { get; init; }
    public int MemberCount { get; init; }
    public PersonSummary? Other { get; init; }
    public bool Online { get; set; }

    public string Title => Type == ConversationType.Group ? Name! : Other?.DisplayName ?? "Conversa";
}

public class ChatPageViewModel
{
    public required List<ConversationListItem> Conversations { get; init; }
    public int? ActiveConversationId { get; init; }
    public ConversationHeader? Header { get; init; }
    public MessagePage? Messages { get; init; }
    public Guid NewClientMessageId { get; init; } = Guid.NewGuid(); // for the no-JS form
    public string? PairLink { get; init; }
}

// Model for the _Messages partial (initial page, "load older" and reconnect catch-up).
public record MessageListModel(List<MessageDto> Messages, string CurrentUserId, DateTimeOffset Now);

public record SidebarModel(List<ConversationListItem> Conversations, int? ActiveConversationId, DateTimeOffset Now);

public class PersonViewModel
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
    public string? StatusMessage { get; init; }
    public string? AvatarFileName { get; init; }
    public bool IsDemoBot { get; init; }
    public bool IsGuest { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
    public bool Online { get; set; }
}
