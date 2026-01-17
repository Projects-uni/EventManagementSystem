using Microsoft.EntityFrameworkCore;
using EventManagementSystem.Models;


namespace EventManagementSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // Add DbSets you use
        public DbSet<User> Users { get; set; }
        public DbSet<Event> Events { get; set; }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Location> Locations { get; set; }

        public DbSet<Invitation> Invitations { get; set; }


        // If you have these models, uncomment:
        public DbSet<Participant> Participants { get; set; }

        public DbSet<EventNotification> EventNotifications { get; set; }
        public DbSet<NotificationRecipient> NotificationRecipients { get; set; }

        
    }
}
