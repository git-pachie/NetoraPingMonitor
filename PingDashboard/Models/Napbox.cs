using System.ComponentModel.DataAnnotations;

namespace PingDashboard.Models;

/// <summary>A NAP (Network Access Point) box stored in SQLite.</summary>
public class Napbox
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string NapboxName { get; set; } = string.Empty;
}
