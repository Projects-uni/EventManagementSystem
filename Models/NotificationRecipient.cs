using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EventManagementSystem.Models
{
    [Table("notification_recipients")]
    public class NotificationRecipient : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("notification_id")]
        public Guid NotificationId { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; }

        [Column("read_at")]
        public DateTime? ReadAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
