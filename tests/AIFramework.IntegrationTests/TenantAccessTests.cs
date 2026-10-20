using System.Security.Claims;
using AIFramework.SampleApi.Security;

namespace AIFramework.IntegrationTests.Api;

public sealed class TenantAccessTests
{
    [Fact]
    public void AuthenticatedRequests_RequireMatchingTenantAndScopeClaims()
    {
        Guid tenant = Guid.NewGuid();
        Guid scope = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity(
        [new Claim("tenant_id", tenant.ToString("D")), new Claim("scope_id", scope.ToString("D"))],
        "test"));

        Assert.True(TenantAccess.IsAllowed(principal, tenant, scope, false));
        Assert.False(TenantAccess.IsAllowed(principal, Guid.NewGuid(), scope, false));
        Assert.False(TenantAccess.IsAllowed(principal, tenant, Guid.NewGuid(), false));
        Assert.True(TenantAccess.IsAllowed(principal, tenant, null, false));
    }

    [Fact]
    public void AnonymousRequests_AreOnlyAllowedInDevelopment()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity());
        Assert.True(TenantAccess.IsAllowed(principal, Guid.NewGuid(), null, true));
        Assert.False(TenantAccess.IsAllowed(principal, Guid.NewGuid(), null, false));
    }
}
