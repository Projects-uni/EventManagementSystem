using System.ComponentModel.DataAnnotations;

namespace EventManagementSystem.Models.Dtos
{
    public class CreateNotificationDto
    {
        [Required, MaxLength(120)]
        public string Title { get; set; } = "";

        [Required, MaxLength(2000)]
        public string Message { get; set; } = "";
    }
}
