using System.ComponentModel.DataAnnotations;

namespace PingDashboard.Models;

/// <summary>Known role names.</summary>
public static class Roles
{
    public const string Admin      = "Admin";
    public const string Technician = "Technician";

    public static readonly string[] All = { Admin, Technician };
    public static bool IsValid(string? role) =>
        role == Admin || role == Technician;
}

/// <summary>An application user with a role and a salted password hash.</summary>
public class AppUser
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Base64 PBKDF2 hash of the password.</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 random salt used for the hash.</summary>
    [Required]
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>"Admin" or "Technician".</summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = Roles.Technician;

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Mobile { get; set; }

    /// <summary>Relative path (under wwwroot) to the uploaded profile image, if any.</summary>
    [MaxLength(300)]
    public string? ProfileImagePath { get; set; }

    /// <summary>Napboxes this user is assigned to (used to scope Technician visibility).</summary>
    public List<UserNapbox> Napboxes { get; set; } = new();
}

/// <summary>Join row linking a user to a napbox name they can see.</summary>
public class UserNapbox
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(200)]
    public string NapboxName { get; set; } = string.Empty;
}
