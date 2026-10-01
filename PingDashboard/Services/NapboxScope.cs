using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingDashboard.Data;
using PingDashboard.Models;

namespace PingDashboard.Services;

/// <summary>
/// Helpers for scoping napbox / client visibility by the current user's role
/// and napbox assignments.
///   • Admin        → sees everything (null = "no restriction").
///   • Technician   → sees only the napboxes assigned to them.
/// </summary>
public static class NapboxScope
{
    /// <summary>
    /// Returns the set of napbox names the user may see, or null if the user is
    /// an Admin (meaning: no restriction — show all).
    /// </summary>
    public static async Task<HashSet<string>?> GetAllowedNapboxesAsync(
        AppDbContext db, ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Admin))
            return null; // no restriction

        var username = user.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return new HashSet<string>(); // not signed in → nothing

        var names = await db.Users
            .Where(u => u.Username == username)
            .SelectMany(u => u.Napboxes.Select(n => n.NapboxName))
            .ToListAsync();

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}
