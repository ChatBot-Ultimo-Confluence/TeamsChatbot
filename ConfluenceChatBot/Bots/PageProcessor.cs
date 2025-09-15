using ConfluenceChatBot.Services;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using System.Text.RegularExpressions;

namespace ConfluenceChatBot.Bots
{
    /// <summary>
    /// Processes Confluence pages:
    /// - Fetches page content
    /// - Generates embeddings and inserts into PgVector
    /// - Performs semantic search using embeddings and Ollama chat
    /// </summary>
    public class PageProcessor
    {
        private readonly ConfluenceService _confluenceService;
        private readonly EmbeddingService _embeddingService;
        private readonly PgVectorService _pgVectorService;
        private readonly IChatCompletionService _chatCompletionService;
        private readonly Kernel _kernel;

        private const int MaxCharsPerSection = 500;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PageProcessor(
            ConfluenceService confluenceService,
            EmbeddingService embeddingService,
            PgVectorService pgVectorService,
            IChatCompletionService chatCompletionService,
            Kernel kernel)
        {
            _confluenceService = confluenceService;
            _embeddingService = embeddingService;
            _pgVectorService = pgVectorService;
            _chatCompletionService = chatCompletionService;
            _kernel = kernel;
        }

        // --------------------------------------------------------------------
        // Page Processing
        // --------------------------------------------------------------------

        /// <summary>
        /// Fetches a Confluence page by ID, generates embeddings, and inserts into the database.
        /// </summary>
        /// <param name="pageId">The Confluence page ID.</param>
        public async Task ProcessPageAndInsertAsync(string pageId)
        {
            try
            {
                var (pageTitle, pageContent, version) = await _confluenceService.GetPageContentAsync(pageId);

                if (string.IsNullOrEmpty(pageContent))
                {
                    Console.WriteLine("No content fetched from Confluence.");
                    return;
                }

                await _pgVectorService.InsertEmbeddingOptimizedAsync(pageId, pageTitle, pageContent, version);
                Console.WriteLine("Page content and embedding inserted into the database.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing page {pageId}: {ex.Message}");
                throw;
            }
        }

        // --------------------------------------------------------------------
        // Semantic Search
        // --------------------------------------------------------------------

        /// <summary>
        /// Performs semantic search on the stored embeddings and returns a friendly answer.
        /// </summary>
        /// <param name="query">The user query.</param>
        /// <returns>Answer string from semantic search.</returns>
        public async Task<string> SearchSemanticDataAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Query is required.";

            // Get top sections from PgVector
            var results = await _pgVectorService.SearchSimilarAsync(query, 10);
            if (!results.Any())
                return "Sorry, I couldn’t find any matching information in the documents.";

            // Prepare context
            var processedSections = results.Select(r =>
            {
                string text = r.content.Length > MaxCharsPerSection
                    ? r.content.Substring(0, MaxCharsPerSection) + "..."
                    : r.content;
                return $"## {r.section}\n{text}";
            });

            var context = string.Join("\n\n", processedSections);

            // Chat history with system prompt
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(@"
                You are a helpful assistant.
                Use only the provided context to answer.
                If the context does not contain the answer, politely say you could not find that information in the documents.
                Do not mention 'context' directly in your answer.
                Keep answers short, clear, and friendly.
                If the context contains an example or a format, return it directly.
            ");

            chatHistory.AddUserMessage($@"
                <context>
                {context}
                </context>

                Question: {query}
            ");

            // Chat completion settings
            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "temperature", 0.2 },
                    { "top_p", 0.95 },
                    { "max_tokens", 1024 },
                    { "stream", true }
                }
            };

            try
            {
                // Call Ollama chat service
                var result = await _chatCompletionService.GetChatMessageContentAsync(chatHistory, settings, _kernel);
                string answer = result?.Content ?? "Sorry, I couldn’t find that information.";

                // Clean code block formatting
                answer = Regex.Replace(answer, @"```(?:plaintext)?\n?", "", RegexOptions.IgnoreCase)
                              .Replace("```", "")
                              .Trim();

                return string.IsNullOrWhiteSpace(answer)
                    ? "Sorry, I couldn’t find that information."
                    : answer;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Semantic search failed: {ex.Message}");
                return "An error occurred during semantic search.";
            }
        }
    }
}
