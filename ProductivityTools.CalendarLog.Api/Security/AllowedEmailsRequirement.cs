using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ProductivityTools.CalendarLog.Api.Security;

public sealed class AllowedEmailsRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedEmails { get; }

    public AllowedEmailsRequirement(IEnumerable<string> allowedEmails)
    {
        AllowedEmails = allowedEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Mirrors ProductivityTools.Spendings.WebApi AllowedEmailValidator:
/// if an allow-list is configured, only verified e-mails on that list may access protected endpoints.
/// </summary>
public sealed class AllowedEmailsHandler : AuthorizationHandler<AllowedEmailsRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AllowedEmailsRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (requirement.AllowedEmails.Count == 0)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var email = context.User.FindFirst("email")?.Value
            ?? context.User.FindFirst(ClaimTypes.Email)?.Value;
        var emailVerified = context.User.FindFirst("email_verified")?.Value;

        if (!string.IsNullOrWhiteSpace(email)
            && string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase)
            && requirement.AllowedEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
