using System.Net;
using System.Net.Http.Json;
using HouseholdExpenses.Core.Contracts;

namespace HouseholdExpenses.Tests;

[Collection(ApiCollection.Name)]
public class ApiIntegrationTests(ApiFixture api)
{
    private static readonly DateOnly Today = new(2026, 3, 15);

    private static SaveExpenseRequest Expense(string payerId, decimal amount, params string[] participants) =>
        new("Groceries run", amount, Today, ExpenseCategory.Groceries, payerId, SplitType.Equal,
            participants.Select(p => new SplitParticipantRequest(p, null)).ToList());

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_callers()
    {
        var response = await api.CreateClient().GetAsync("/api/households");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_then_login_returns_a_token()
    {
        var (_, user) = await api.RegisterAsync("Login");
        var client = api.CreateClient();

        var ok = await client.PostJsonAsync("/api/auth/login", new LoginRequest(user.Email, "Passw0rd!"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var bad = await client.PostJsonAsync("/api/auth/login", new LoginRequest(user.Email, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Weak_password_is_rejected_with_validation_problem()
    {
        var response = await api.CreateClient().PostJsonAsync("/api/auth/register",
            new RegisterRequest("weak@example.com", "short", "Weak"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_or_touch_households_they_do_not_belong_to()
    {
        var (alice, _) = await api.RegisterAsync("Alice");
        var (mallory, _) = await api.RegisterAsync("Mallory");

        var household = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 1")))
            .ReadAsync<HouseholdDetailDto>();

        // Mallory's list is empty and direct access looks like "not found".
        Assert.Empty(await (await mallory.GetAsync("/api/households")).ReadAsync<List<HouseholdSummaryDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/households/{household.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/households/{household.Id}/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/households/{household.Id}/balances")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.DeleteAsync($"/api/households/{household.Id}")).StatusCode);

        var sneaky = await mallory.PostJsonAsync($"/api/households/{household.Id}/expenses", Expense("x", 10m, "x"));
        Assert.Equal(HttpStatusCode.NotFound, sneaky.StatusCode);
    }

    [Fact]
    public async Task Invite_code_lets_a_second_user_join_and_only_owner_can_delete()
    {
        var (alice, _) = await api.RegisterAsync("Alice");
        var (bob, _) = await api.RegisterAsync("Bob");

        var created = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 2")))
            .ReadAsync<HouseholdDetailDto>();
        Assert.Equal(HouseholdRole.Owner, created.MyRole);

        var joined = await (await bob.PostJsonAsync("/api/households/join", new JoinHouseholdRequest(created.InviteCode.ToLowerInvariant())))
            .ReadAsync<HouseholdDetailDto>();
        Assert.Equal(2, joined.Members.Count);
        Assert.Equal(HouseholdRole.Member, joined.MyRole);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/households/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/households/{created.Id}")).StatusCode);

        var badCode = await bob.PostJsonAsync("/api/households/join", new JoinHouseholdRequest("NOPE1234"));
        Assert.Equal(HttpStatusCode.NotFound, badCode.StatusCode);
    }

    [Fact]
    public async Task Expense_flow_produces_correct_balances_and_settlement_clears_them()
    {
        var (alice, a) = await api.RegisterAsync("Alice");
        var (bob, b) = await api.RegisterAsync("Bob");
        var (carol, c) = await api.RegisterAsync("Carol");

        var h = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 3"))).ReadAsync<HouseholdDetailDto>();
        await bob.PostJsonAsync("/api/households/join", new JoinHouseholdRequest(h.InviteCode));
        await carol.PostJsonAsync("/api/households/join", new JoinHouseholdRequest(h.InviteCode));

        // Alice pays 30 split three ways -> she is owed 20, Bob and Carol owe 10 each.
        var created = await alice.PostJsonAsync($"/api/households/{h.Id}/expenses", Expense(a.Id, 30m, a.Id, b.Id, c.Id));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Bob pays 30 for himself and Carol only by exact amounts -> Carol owes Bob 20.
        var exact = new SaveExpenseRequest("Dinner", 30m, Today, ExpenseCategory.Dining, b.Id, SplitType.Exact,
            [new(b.Id, 10m), new(c.Id, 20m)]);
        Assert.Equal(HttpStatusCode.Created, (await bob.PostJsonAsync($"/api/households/{h.Id}/expenses", exact)).StatusCode);

        var summary = await (await carol.GetAsync($"/api/households/{h.Id}/balances")).ReadAsync<BalanceSummaryDto>();
        decimal Net(string id) => summary.Balances.Single(x => x.UserId == id).Net;
        Assert.Equal(20m, Net(a.Id));   // +30 paid -10 share
        Assert.Equal(10m, Net(b.Id));   // +30 paid -10 -10 shares
        Assert.Equal(-30m, Net(c.Id));  // -10 -20
        Assert.Equal(2, summary.SuggestedPayments.Count);
        Assert.All(summary.SuggestedPayments, p => Assert.Equal(c.Id, p.FromUserId));

        // Carol pays everything back.
        foreach (var p in summary.SuggestedPayments)
        {
            var s = await carol.PostJsonAsync($"/api/households/{h.Id}/settlements",
                new SaveSettlementRequest(p.FromUserId, p.ToUserId, p.Amount, Today, "Venmo"));
            Assert.Equal(HttpStatusCode.Created, s.StatusCode);
        }

        var after = await (await alice.GetAsync($"/api/households/{h.Id}/balances")).ReadAsync<BalanceSummaryDto>();
        Assert.All(after.Balances, x => Assert.Equal(0m, x.Net));
        Assert.Empty(after.SuggestedPayments);
    }

    [Fact]
    public async Task Expense_history_can_be_filtered_by_month_category_and_person()
    {
        var (alice, a) = await api.RegisterAsync("Alice");
        var (bob, b) = await api.RegisterAsync("Bob");
        var h = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 4"))).ReadAsync<HouseholdDetailDto>();
        await bob.PostJsonAsync("/api/households/join", new JoinHouseholdRequest(h.InviteCode));

        async Task Add(string desc, DateOnly date, ExpenseCategory cat, string payer, params string[] who) =>
            (await alice.PostJsonAsync($"/api/households/{h.Id}/expenses",
                new SaveExpenseRequest(desc, 20m, date, cat, payer, SplitType.Equal,
                    who.Select(w => new SplitParticipantRequest(w, null)).ToList()))).EnsureSuccessStatusCode();

        await Add("March rent", new(2026, 3, 1), ExpenseCategory.Rent, a.Id, a.Id, b.Id);
        await Add("March food", new(2026, 3, 9), ExpenseCategory.Groceries, b.Id, a.Id, b.Id);
        await Add("April food", new(2026, 4, 2), ExpenseCategory.Groceries, a.Id, a.Id);

        async Task<List<ExpenseDto>> Query(string qs) =>
            await (await alice.GetAsync($"/api/households/{h.Id}/expenses{qs}")).ReadAsync<List<ExpenseDto>>();

        Assert.Equal(3, (await Query("")).Count);
        Assert.Equal(2, (await Query("?month=2026-03")).Count);
        Assert.Equal(2, (await Query("?category=Groceries")).Count);
        Assert.Single(await Query("?month=2026-03&category=Groceries"));
        Assert.Equal(2, (await Query($"?userId={b.Id}")).Count); // only expenses Bob paid or shares
        Assert.Equal("April food", (await Query("")).First().Description); // newest first

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"/api/households/{h.Id}/expenses?month=March")).StatusCode);
    }

    [Fact]
    public async Task Invalid_splits_and_non_members_are_rejected_with_400()
    {
        var (alice, a) = await api.RegisterAsync("Alice");
        var (_, outsider) = await api.RegisterAsync("Outsider");
        var h = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 5"))).ReadAsync<HouseholdDetailDto>();

        var badPercent = new SaveExpenseRequest("x", 10m, Today, ExpenseCategory.Other, a.Id, SplitType.Percentage,
            [new(a.Id, 60m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync($"/api/households/{h.Id}/expenses", badPercent)).StatusCode);

        var nonMember = Expense(a.Id, 10m, a.Id, outsider.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync($"/api/households/{h.Id}/expenses", nonMember)).StatusCode);

        var negative = Expense(a.Id, -5m, a.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync($"/api/households/{h.Id}/expenses", negative)).StatusCode);

        var self = await alice.PostJsonAsync($"/api/households/{h.Id}/settlements",
            new SaveSettlementRequest(a.Id, a.Id, 5m, Today, null));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task Expense_can_be_edited_and_deleted()
    {
        var (alice, a) = await api.RegisterAsync("Alice");
        var h = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 6"))).ReadAsync<HouseholdDetailDto>();

        var created = await (await alice.PostJsonAsync($"/api/households/{h.Id}/expenses", Expense(a.Id, 10m, a.Id))).ReadAsync<ExpenseDto>();

        var edited = await alice.PutAsJsonAsync($"/api/households/{h.Id}/expenses/{created.Id}",
            Expense(a.Id, 25m, a.Id) with { Description = "Edited" }, ApiFixture.Json);
        var dto = await edited.ReadAsync<ExpenseDto>();
        Assert.Equal("Edited", dto.Description);
        Assert.Equal(25m, dto.Splits.Single().Amount);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/households/{h.Id}/expenses/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/households/{h.Id}/expenses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Leaving_requires_a_settled_balance()
    {
        var (alice, a) = await api.RegisterAsync("Alice");
        var (bob, b) = await api.RegisterAsync("Bob");
        var h = await (await alice.PostJsonAsync("/api/households", new CreateHouseholdRequest("Flat 7"))).ReadAsync<HouseholdDetailDto>();
        await bob.PostJsonAsync("/api/households/join", new JoinHouseholdRequest(h.InviteCode));
        await alice.PostJsonAsync($"/api/households/{h.Id}/expenses", Expense(a.Id, 20m, a.Id, b.Id));

        Assert.Equal(HttpStatusCode.BadRequest, (await bob.DeleteAsync($"/api/households/{h.Id}/members/me")).StatusCode);

        await bob.PostJsonAsync($"/api/households/{h.Id}/settlements", new SaveSettlementRequest(b.Id, a.Id, 10m, Today, null));
        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync($"/api/households/{h.Id}/members/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/households/{h.Id}")).StatusCode);
    }
}
