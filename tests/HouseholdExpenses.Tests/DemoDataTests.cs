using System.Net.Http.Headers;
using System.Net.Http.Json;
using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Core.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HouseholdExpenses.Tests;

[Collection(ApiCollection.Name)]
public class DemoDataTests(ApiFixture api)
{
    private async Task SeedAsync()
    {
        using var scope = api.Services.CreateScope();
        await DemoDataSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            NullLogger.Instance);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_anything()
    {
        await SeedAsync();
        await SeedAsync();

        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var demoEmails = DemoDataSeeder.Users.Select(u => u.Email).ToList();
        Assert.Equal(3, users.Users.Count(u => demoEmails.Contains(u.Email!)));
    }

    [Fact]
    public async Task Demo_user_can_log_in_and_sees_a_household_with_balances_that_net_to_zero()
    {
        await SeedAsync();
        var client = api.CreateClient();

        var login = await client.PostJsonAsync("/api/auth/login",
            new LoginRequest(DemoDataSeeder.Users[0].Email, DemoDataSeeder.Password));
        var auth = await login.ReadAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var households = await (await client.GetAsync("/api/households")).ReadAsync<List<HouseholdSummaryDto>>();
        var household = Assert.Single(households);
        Assert.Equal(3, household.MemberCount);

        var summary = await (await client.GetAsync($"/api/households/{household.Id}/balances")).ReadAsync<BalanceSummaryDto>();
        Assert.Equal(0m, summary.Balances.Sum(b => b.Net));
        Assert.NotEmpty(summary.SuggestedPayments); // interesting to look at, not already settled up

        var expenses = await (await client.GetAsync($"/api/households/{household.Id}/expenses")).ReadAsync<List<ExpenseDto>>();
        Assert.Equal(7, expenses.Count);
    }
}
