using Lobby.Web.Models;

namespace Lobby.Web.ViewModels;

public class NewGroupViewModel
{
    public string? Name { get; set; }
    public List<string> MemberIds { get; set; } = [];
    public List<PersonViewModel> Candidates { get; set; } = [];
    public string? Error { get; set; }
    public int MaxMembers => Conversation.MaxGroupMembers;
}

public class GroupDetailsViewModel
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required List<PersonViewModel> Members { get; init; }
    public required List<PersonViewModel> Candidates { get; init; }
    public string? Error { get; init; }
}
