using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ConfluenceChatBot.Services
{
    /// <summary>
    /// Service to interact with Confluence REST API.
    /// Provides methods to fetch pages, content, and updates from a space.
    /// </summary>
    public class ConfluenceService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _authToken;

        // --------------------------------------------------------------------
        // Constructor & Authentication
        // --------------------------------------------------------------------

        /// <summary>
        /// Initializes the service with an HttpClient and configuration settings.
        /// </summary>
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

        // --------------------------------------------------------------------
        // Models
        // --------------------------------------------------------------------

        /// <summary>
        /// Represents a Confluence page.
        /// </summary>
        public class ConfluencePage
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public int Version { get; set; }
            public DateTime LastModified { get; set; }

            public static implicit operator string(ConfluencePage v)
            {
                throw new NotImplementedException();
            }
        }

        // --------------------------------------------------------------------
        // Page Content Retrieval
        // --------------------------------------------------------------------

        /// <summary>
        /// Fetches the content and version of a specific Confluence page.
        /// </summary>
        public async Task<(string Title, string Content, int version)> GetPageContentAsync(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
                throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));

            var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content/{pageId}?expand=body.storage,version,metadata";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                string title = jsonData.GetProperty("title").GetString() ?? "Untitled";
                var versionProperty = jsonData.GetProperty("version");
                int version = versionProperty.GetProperty("number").GetInt32();

                string content = "";
                if (jsonData.TryGetProperty("body", out var body) &&
                    body.TryGetProperty("storage", out var storage) &&
                    storage.TryGetProperty("value", out var value))
                {
                    content = value.GetString() ?? "";
                }

                return (title, content, version);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching Confluence page: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets all pages in a Confluence space.
        /// </summary>
        public async Task<List<ConfluencePage>> GetAllPagesInSpaceAsync(string spaceKey)
        {
            if (string.IsNullOrEmpty(spaceKey))
                throw new ArgumentException("Space key cannot be null or empty.", nameof(spaceKey));

            var pages = new List<ConfluencePage>();
            int start = 0;
            const int limit = 50;

            while (true)
            {
                var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content?spaceKey={spaceKey}&limit={limit}&start={start}&expand=body.storage,version,title";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                if (!jsonData.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    break;

                foreach (var item in results.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString()!;
                    var title = item.GetProperty("title").GetString()!;
                    var version = item.GetProperty("version").GetProperty("number").GetInt32();
                    var content = item.GetProperty("body").GetProperty("storage").GetProperty("value").GetString() ?? "";

                    pages.Add(new ConfluencePage
                    {
                        Id = id,
                        Title = title,
                        Version = version,
                        Content = content
                    });
                }

                start += limit;
                if (!jsonData.TryGetProperty("size", out var sizeProp) || sizeProp.GetInt32() < limit)
                    break;
            }

            return pages;
        }
    }
}
