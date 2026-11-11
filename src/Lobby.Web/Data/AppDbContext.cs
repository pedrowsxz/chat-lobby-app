using Lobby.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Data;

// IdentityUserContext (not IdentityDbContext) because we have no roles: fewer tables.
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<ApplicationUser>(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // "Direct"/"Group" in the table instead of 0/1: readable in SQL and safe if the enum is reordered.
        configuration.Properties<ConversationType>().HaveConversion<string>().HaveMaxLength(10);
    }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
            user.Property(u => u.StatusMessage).HasMaxLength(ApplicationUser.StatusMessageMaxLength);
            user.Property(u => u.AvatarFileName).HasMaxLength(64);
            user.Property(u => u.GuestPairId).HasMaxLength(450);
            user.Property(u => u.PairToken).HasMaxLength(64);
            user.HasIndex(u => u.PairToken).IsUnique().HasFilter("[PairToken] IS NOT NULL");
            // Identity's e-mail index is not unique. Make it unique for users that have an e-mail,
            // so FindByEmailAsync (used by login) can never find two rows.
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        });

        // Delete rules. SQL Server refuses two cascade paths to the same row (user -> message and
        // user -> conversation -> message), so only "conversation -> its rows" cascades.
        // Every foreign key to a user is Restrict; guest cleanup deletes in an explicit order instead.
        builder.Entity<Conversation>(conversation =>
        {
            conversation.Property(c => c.Name).HasMaxLength(Conversation.NameMaxLength);
            conversation.Property(c => c.DirectKey).HasMaxLength(80);
            conversation.HasIndex(c => c.DirectKey).IsUnique().HasFilter("[DirectKey] IS NOT NULL");
            conversation.HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.Restrict);
            conversation.ToTable(t => t.HasCheckConstraint("CK_Conversations_TypeFields",
                "([Type] = 'Direct' AND [DirectKey] IS NOT NULL) OR ([Type] = 'Group' AND [Name] IS NOT NULL)"));
        });

        builder.Entity<ConversationMember>(member =>
        {
            member.HasKey(m => new { m.ConversationId, m.UserId });
            member.HasOne(m => m.Conversation).WithMany(c => c.Members).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            member.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Message>(message =>
        {
            message.Property(m => m.Body).HasMaxLength(Message.MaxBodyLength);
            message.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            message.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
            // Idempotent send: the same (sender, clientMessageId) can only be stored once.
            message.HasIndex(m => new { m.SenderId, m.ClientMessageId }).IsUnique();
            // History paging: "messages of conversation X with Id < N, newest first".
            message.HasIndex(m => new { m.ConversationId, m.Id });
        });
    }
}
