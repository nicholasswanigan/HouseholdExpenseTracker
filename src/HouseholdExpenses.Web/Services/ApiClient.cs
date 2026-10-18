using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HouseholdExpenses.Core.Contracts;

namespace HouseholdExpenses.Web.Services;

/// <summary>Error from the API, already turned into a message that is safe to show to the user.</summary>
public class ApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Typed wrapper around the REST API: attaches the JWT and converts failures to <see cref="ApiException"/>.</summary>
public class ApiClient(HttpClient http, TokenStore tokens, JwtAuthStateProvider auth)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // ----- Auth -----
    public async Task LoginAsync(LoginRequest request) =>
        await auth.SignInAsync((await SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/login", request, authorize: false)).Token);

    public async Task RegisterAsync(RegisterRequest request) =>
        await auth.SignInAsync((await SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/register", request, authorize: false)).Token);

    // ----- Households -----
    public Task<List<HouseholdSummaryDto>> GetHouseholdsAsync() => GetAsync<List<HouseholdSummaryDto>>("api/households");
    public Task<HouseholdDetailDto> GetHouseholdAsync(Guid id) => GetAsync<HouseholdDetailDto>($"api/households/{id}");
    public Task<HouseholdDetailDto> CreateHouseholdAsync(string name) =>
        SendAsync<HouseholdDetailDto>(HttpMethod.Post, "api/households", new CreateHouseholdRequest(name));
    public Task<HouseholdDetailDto> JoinHouseholdAsync(string code) =>
        SendAsync<HouseholdDetailDto>(HttpMethod.Post, "api/households/join", new JoinHouseholdRequest(code));
    public Task LeaveHouseholdAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/households/{id}/members/me");
    public Task DeleteHouseholdAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/households/{id}");

    // ----- Expenses -----
    public Task<List<ExpenseDto>> GetExpensesAsync(Guid householdId, string? month, ExpenseCategory? category, string? userId)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(month)) query.Add($"month={Uri.EscapeDataString(month)}");
        if (category is not null) query.Add($"category={category}");
        if (!string.IsNullOrEmpty(userId)) query.Add($"userId={Uri.EscapeDataString(userId)}");
        var qs = query.Count > 0 ? "?" + string.Join("&", query) : "";
        return GetAsync<List<ExpenseDto>>($"api/households/{householdId}/expenses{qs}");
    }

    public Task<ExpenseDto> SaveExpenseAsync(Guid householdId, Guid? expenseId, SaveExpenseRequest request) =>
        expenseId is null
            ? SendAsync<ExpenseDto>(HttpMethod.Post, $"api/households/{householdId}/expenses", request)
            : SendAsync<ExpenseDto>(HttpMethod.Put, $"api/households/{householdId}/expenses/{expenseId}", request);

    public Task DeleteExpenseAsync(Guid householdId, Guid expenseId) =>
        SendAsync(HttpMethod.Delete, $"api/households/{householdId}/expenses/{expenseId}");

    // ----- Balances & settlements -----
    public Task<BalanceSummaryDto> GetBalancesAsync(Guid householdId) =>
        GetAsync<BalanceSummaryDto>($"api/households/{householdId}/balances");
    public Task<List<SettlementDto>> GetSettlementsAsync(Guid householdId) =>
        GetAsync<List<SettlementDto>>($"api/households/{householdId}/settlements");
    public Task<SettlementDto> CreateSettlementAsync(Guid householdId, SaveSettlementRequest request) =>
        SendAsync<SettlementDto>(HttpMethod.Post, $"api/households/{householdId}/settlements", request);
    public Task DeleteSettlementAsync(Guid householdId, Guid id) =>
        SendAsync(HttpMethod.Delete, $"api/households/{householdId}/settlements/{id}");

    // ----- Plumbing -----
    private Task<T> GetAsync<T>(string url) => SendAsync<T>(HttpMethod.Get, url);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null, bool authorize = true)
    {
        using var response = await RawSendAsync(method, url, body, authorize);
        return (await response.Content.ReadFromJsonAsync<T>(Json))
               ?? throw new ApiException("The server returned an empty response.");
    }

    private async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await RawSendAsync(method, url, body, authorize: true);
    }

    private async Task<HttpResponseMessage> RawSendAsync(HttpMethod method, string url, object? body, bool authorize)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        if (authorize)
        {
            var token = await tokens.GetAsync();
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Can't reach the server. Is the API running?");
        }

        if (response.IsSuccessStatusCode) return response;

        var message = await ReadErrorAsync(response);
        if (response.StatusCode == HttpStatusCode.Unauthorized && authorize)
        {
            await auth.SignOutAsync();
            message = "Your session has expired. Please sign in again.";
        }
        response.Dispose();
        throw new ApiException(message, response.StatusCode);
    }

    /// <summary>Digs a human-readable message out of an RFC 7807 problem response.</summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .SelectMany(p => p.Value.EnumerateArray().Select(e => e.GetString()))
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                var joined = string.Join(" ", messages);
                if (joined.Length > 0) return joined;
            }
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString()!;
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                return title.GetString()!;
        }
        catch (JsonException) { /* not JSON; fall through */ }

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.NotFound => "That could not be found.",
            _ => $"Request failed ({(int)response.StatusCode})."
        };
    }
}
