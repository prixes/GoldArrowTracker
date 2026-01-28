using System.ComponentModel.DataAnnotations;

namespace GoldTracker.Server.Data;

public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string GoogleSubjectId { get; set; } = string.Empty;

    [Required]
    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
}
