using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace EventManagementSystem.Models
{
    [Table("event_notifications")]
    public class EventNotification : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("event_id")]
        public Guid EventId { get; set; }

        [Column("created_by")]
        public Guid CreatedBy { get; set; }

        [Column("title")]
        public string Title { get; set; } = "";

        [Column("message")]
        public string Message { get; set; } = "";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
