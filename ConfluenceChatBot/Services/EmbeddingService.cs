using System.Text.Json;

namespace ConfluenceChatBot.Services
{
    /// <summary>
    /// Service responsible for generating text embeddings by calling an external embedding API.
    /// </summary>
    public class EmbeddingService
    {
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Initializes the embedding service with an injected <see cref="HttpClient"/>.
        /// </summary>
        public EmbeddingService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // --------------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------------

        /// <summary>
        /// Generates an embedding vector for the given text.
        /// Calls the Ollama embedding API running locally.
        /// </summary>
        /// <param name="text">The input text to embed.</param>
        /// <returns>A float array representing the embedding vector.</returns>
        /// <exception cref="HttpRequestException">Thrown if the API request fails.</exception>
        /// <exception cref="JsonException">Thrown if the API response is invalid.</exception>
        /// <exception cref="Exception">Thrown for all other unexpected errors.</exception>
        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                prompt = text,
                model = "nomic-embed-text"
            };

            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    "http://localhost:11434/api/embeddings",
                    requestBody);

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
                Console.Error.WriteLine($"HTTP request failed: {httpEx.Message}");
                throw;
            }
            catch (JsonException jsonEx)
            {
                Console.Error.WriteLine($"JSON parsing failed: {jsonEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error generating embedding: {ex.Message}");
                throw;
            }
        }
    }
}
