using System.Security.Claims;

namespace AIFramework.SampleApi.Security;

internal static class TenantAccess
{
    public static bool IsAllowed(ClaimsPrincipal principal, Guid tenantId, Guid? scopeId, bool isDevelopment)
    {
        if (isDevelopment && principal.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        bool tenantMatches = principal.FindAll("tenant_id")
            .Any(claim => Guid.TryParse(claim.Value, out Guid value) && value == tenantId);
        if (!tenantMatches)
        {
            return false;
        }

        return scopeId is null || principal.FindAll("scope_id")
            .Any(claim => Guid.TryParse(claim.Value, out Guid value) && value == scopeId);
    }
}
