using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ConfluenceChatBot.Models;

namespace ConfluenceChatBot.Services
{
    /// <summary>
    /// Service to interact with Confluence REST API (ROP style).
    /// </summary>
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

        // --------------------------------------------------------------------
        // Get single page
        // --------------------------------------------------------------------
        public async Task<Result<ConfluencePage>> GetPageContentAsync(string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId))
                return Result<ConfluencePage>.Fail("Page ID cannot be null or empty.");

            var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content/{pageId}?expand=body.storage,version,metadata";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return Result<ConfluencePage>.Fail($"API call failed: {response.StatusCode}");

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                var page = new ConfluencePage
                {
                    Id = jsonData.GetProperty("id").GetString() ?? "",
                    Title = jsonData.GetProperty("title").GetString() ?? "Untitled",
                    Version = jsonData.GetProperty("version").GetProperty("number").GetInt32(),
                    Content = jsonData.GetProperty("body").GetProperty("storage").GetProperty("value").GetString() ?? "",
                    LastModified = jsonData.GetProperty("version").GetProperty("when").GetDateTime()
                };

                return Result<ConfluencePage>.Ok(page);
            }
            catch (Exception ex)
            {
                return Result<ConfluencePage>.Fail($"Error fetching page {pageId}: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------
        // Get all pages in a space
        // --------------------------------------------------------------------
        public async Task<Result<List<ConfluencePage>>> GetAllPagesInSpaceAsync(string spaceKey)
        {
            if (string.IsNullOrWhiteSpace(spaceKey))
                return Result<List<ConfluencePage>>.Fail("Space key cannot be null or empty.");

            var pages = new List<ConfluencePage>();
            int start = 0;
            const int limit = 50;

            try
            {
                while (true)
                {
                    var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content?spaceKey={spaceKey}&limit={limit}&start={start}&expand=body.storage,version,metadata";

                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

                    using var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                        return Result<List<ConfluencePage>>.Fail($"API call failed: {response.StatusCode}");

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                    if (!jsonData.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                        break;

                    foreach (var item in results.EnumerateArray())
                    {
                        pages.Add(new ConfluencePage
                        {
                            Id = item.GetProperty("id").GetString() ?? "",
                            Title = item.GetProperty("title").GetString() ?? "",
                            Version = item.GetProperty("version").GetProperty("number").GetInt32(),
                            Content = item.GetProperty("body").GetProperty("storage").GetProperty("value").GetString() ?? "",
                            LastModified = item.GetProperty("version").GetProperty("when").GetDateTime()
                        });
                    }

                    start += limit;

                    if (!jsonData.TryGetProperty("size", out var sizeProp) || sizeProp.GetInt32() < limit)
                        break;
                }

                return Result<List<ConfluencePage>>.Ok(pages);
            }
            catch (Exception ex)
            {
                return Result<List<ConfluencePage>>.Fail($"Error fetching space {spaceKey}: {ex.Message}");
            }
        }
    }
}



//using System.Net.Http.Headers;
//using System.Text;
//using System.Text.Json;
//using Microsoft.Extensions.Configuration;
//using ConfluenceChatBot.Models;

//namespace ConfluenceChatBot.Services
//{
//    /// <summary>
//    /// Service to interact with Confluence REST API.
//    /// Provides methods to fetch pages, content, and updates from a space.
//    /// </summary>
//    public class ConfluenceService
//    {
//        private readonly HttpClient _httpClient;
//        private readonly string _baseUrl;
//        private readonly string _authToken;

//        // --------------------------------------------------------------------
//        // Constructor & Authentication
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Initializes the service with an HttpClient and configuration settings.
//        /// </summary>
//        public ConfluenceService(HttpClient httpClient, IConfiguration configuration)
//        {
//            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

//            _baseUrl = configuration["Confluence:BaseUrl"]
//                       ?? throw new ArgumentNullException("Confluence:BaseUrl");

//            var email = configuration["Confluence:Username"]
//                        ?? throw new ArgumentNullException("Confluence:Username");

//            var token = configuration["Confluence:ApiToken"]
//                        ?? throw new ArgumentNullException("Confluence:ApiToken");

//            _authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
//        }


//        // --------------------------------------------------------------------
//        // Page Content Retrieval
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Fetches the content and version of a specific Confluence page.
//        /// </summary>
//        public async Task<(string Title, string Content, int version)> GetPageContentAsync(string pageId)
//        {
//            if (string.IsNullOrEmpty(pageId))
//                throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));

//            var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content/{pageId}?expand=body.storage,version,metadata";

//            try
//            {
//                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
//                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

//                using var response = await _httpClient.SendAsync(request);
//                response.EnsureSuccessStatusCode();

//                var jsonResponse = await response.Content.ReadAsStringAsync();
//                var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

//                string title = jsonData.GetProperty("title").GetString() ?? "Untitled";
//                var versionProperty = jsonData.GetProperty("version");
//                int version = versionProperty.GetProperty("number").GetInt32();

//                string content = "";
//                if (jsonData.TryGetProperty("body", out var body) &&
//                    body.TryGetProperty("storage", out var storage) &&
//                    storage.TryGetProperty("value", out var value))
//                {
//                    content = value.GetString() ?? "";
//                }

//                return (title, content, version);
//            }
//            catch (Exception ex)
//            {
//                Console.Error.WriteLine($"Error fetching Confluence page: {ex.Message}");
//                throw;
//            }
//        }

//        /// <summary>
//        /// Gets all pages in a Confluence space.
//        /// </summary>
//        public async Task<List<ConfluencePage>> GetAllPagesInSpaceAsync(string spaceKey)
//        {
//            if (string.IsNullOrEmpty(spaceKey))
//                throw new ArgumentException("Space key cannot be null or empty.", nameof(spaceKey));

//            var pages = new List<ConfluencePage>();
//            int start = 0;
//            const int limit = 50;

//            while (true)
//            {
//                var requestUrl = $"{_baseUrl.TrimEnd('/')}/rest/api/content?spaceKey={spaceKey}&limit={limit}&start={start}&expand=body.storage,version,title";

//                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
//                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authToken);

//                using var response = await _httpClient.SendAsync(request);
//                response.EnsureSuccessStatusCode();

//                var jsonResponse = await response.Content.ReadAsStringAsync();
//                var jsonData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

//                if (!jsonData.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
//                    break;

//                foreach (var item in results.EnumerateArray())
//                {
//                    var id = item.GetProperty("id").GetString()!;
//                    var title = item.GetProperty("title").GetString()!;
//                    var version = item.GetProperty("version").GetProperty("number").GetInt32();
//                    var content = item.GetProperty("body").GetProperty("storage").GetProperty("value").GetString() ?? "";

//                    pages.Add(new ConfluencePage
//                    {
//                        Id = id,
//                        Title = title,
//                        Version = version,
//                        Content = content
//                    });
//                }

//                start += limit;
//                if (!jsonData.TryGetProperty("size", out var sizeProp) || sizeProp.GetInt32() < limit)
//                    break;
//            }

//            return pages;
//        }
//    }
//}
