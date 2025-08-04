using System.Net.Http.Json;
using System.Text.Json;

namespace ConfluenceChatBot.Services
{
    public class EmbeddingService
    {
        private readonly HttpClient _httpClient;

        public EmbeddingService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                prompt = text,
                model = "nomic-embed-text"
            };

            try
            {
                using var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/embeddings", requestBody);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                if (!json.TryGetProperty("embedding", out var embeddingElement))
                    throw new Exception("Response JSON does not contain 'embedding' property.");

                var embeddingArray = embeddingElement
                    .EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();

                return embeddingArray;
            }
            catch (HttpRequestException httpEx)
            {
                // Log or handle HTTP errors (e.g., service unreachable)
                Console.Error.WriteLine($"HTTP request failed: {httpEx.Message}");
                throw;
            }
            catch (JsonException jsonEx)
            {
                // Handle JSON parsing errors
                Console.Error.WriteLine($"JSON parsing failed: {jsonEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                // Catch all other exceptions
                Console.Error.WriteLine($"Unexpected error generating embedding: {ex.Message}");
                throw;
            }
        }
    }
}