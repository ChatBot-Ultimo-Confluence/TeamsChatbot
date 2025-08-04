using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ConfluenceChatBot.Services
{
    public class ConfluenceService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _authToken;

        public ConfluenceService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            _baseUrl = configuration["Confluence:BaseUrl"]
                       ?? throw new ArgumentNullException("Confluence:BaseUrl");

            var email = configuration["Confluence:Username"]
                        ?? throw new ArgumentNullException("Confluence:Username");

            var token = configuration["Confluence:ApiToken"]
                        ?? throw new ArgumentNullException("Confluence:ApiToken");

            _authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
        }

        public async Task<string> GetPageContentAsync(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
                throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));

            var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content/{pageId}?expand=body.storage";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();

                var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                if (jsonData.TryGetProperty("body", out var body) &&
                    body.TryGetProperty("storage", out var storage) &&
                    storage.TryGetProperty("value", out var value))
                {
                    return value.GetString() ?? string.Empty;
                }

                // If structure unexpected, return empty or throw
                return string.Empty;
            }
            catch (HttpRequestException httpEx)
            {
                Console.Error.WriteLine($"HTTP request error while fetching Confluence page: {httpEx.Message}");
                throw;
            }
            catch (JsonException jsonEx)
            {
                Console.Error.WriteLine($"JSON parsing error in Confluence response: {jsonEx.Message}");
                throw;
            }
        }
    }
}