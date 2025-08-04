using ConfluenceChatBot.Services;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using System.Text.RegularExpressions;

namespace ConfluenceChatBot.Bots
{
    public class PageProcessor
    {
        private readonly ConfluenceService _confluenceService;
        private readonly EmbeddingService _embeddingService;
        private readonly PgVectorService _pgVectorService;
        private const int MaxSections = 10;
        private const int MaxCharsPerSection = 500;
        private readonly IChatCompletionService _chatCompletionService;
        private readonly Kernel _kernel;

        public PageProcessor(ConfluenceService confluenceService, EmbeddingService embeddingService, PgVectorService pgVectorService,
            IChatCompletionService chatCompletionService, Kernel kernel)
        {
            _confluenceService = confluenceService;
            _embeddingService = embeddingService;
            _pgVectorService = pgVectorService;
            _chatCompletionService = chatCompletionService;
            _kernel = kernel;
        }

        public async Task ProcessPageAndInsertAsync(string pageId)
        {
            try
            {
                string pageContent = await _confluenceService.GetPageContentAsync(pageId);
                if (string.IsNullOrEmpty(pageContent))
                {
                    Console.WriteLine("No content fetched from Confluence.");
                    return;
                }

                await _pgVectorService.InsertEmbeddingAsync(pageId, "test", pageContent);
                Console.WriteLine("Page content and embedding inserted into the database.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing page {pageId}: {ex.Message}");
                throw;
            }
        }

        public async Task<string> SearchSemanticDataAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Query is required.";

            var result = await _pgVectorService.SearchSimilarAsync(query, MaxSections);
            if (result == null || !result.Any())
                return "No similar content found.";

            var processedSections = result
                .Take(MaxSections)
                .Select(c =>
                {
                    var cleaned = CleanConfluenceContent(c.content);
                    if (cleaned.Length > MaxCharsPerSection)
                        cleaned = cleaned.Substring(0, MaxCharsPerSection) + "...";
                    return $"## {c.section}\n{cleaned}";
                });

            var prompt = GeneratePrompt(query, processedSections);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(@"
            You are a helpful and knowledgeable assistant.
            Only use the provided context below to answer the user's question.
            Respond using the same language and tone as the user's question.
            Write in a clear, friendly, and natural way — like you're speaking to a teammate.
            Present any structured information (like steps, lists, or checklists) in a readable and well-organized format.
            If the answer is not in the context, reply with: 'I could not find that in the provided context.'
            Do not invent or assume information not found in the context.
            ");
            chatHistory.AddUserMessage($"Question:\n{query}");
            chatHistory.AddUserMessage($"Context:\n{prompt}");
            chatHistory.AddUserMessage(prompt);

            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "temperature", 0.2 },
                    { "top_p", 0.95 },
                    { "max_tokens", 1024 }
                }
            };

            try
            {
                var results = await _chatCompletionService.GetChatMessageContentAsync(chatHistory, settings, _kernel);
                var chatMessage = results?.Content ?? "No answer.";

                var cleanResponse = Regex.Replace(chatMessage, @"```(?:plaintext)?\n?", "", RegexOptions.IgnoreCase)
                                         .Replace("```", "");

                return cleanResponse.Trim();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Semantic search failed: {ex.Message}");
                return "An error occurred during semantic search.";
            }
        }

        private string GeneratePrompt(string query, IEnumerable<string> sections)
        {
            var context = string.Join("\n\n", sections);
            return $"You are an assistant answering questions based on the following documents:\n<context>\n{context}\n</context>\n\nQuestion: {query}\nAnswer:";
        }

        private string CleanConfluenceContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";

            var codePattern = @"<ac:structured-macro[^>]*ac:name=""code""[^>]*>.*?<ac:plain-text-body><!\[CDATA\[(.*?)\]\]></ac:plain-text-body>.*?</ac:structured-macro>";
            content = Regex.Replace(content, codePattern, m =>
            {
                var code = m.Groups[1].Value.Trim();
                return $"```\n{code}\n```";
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            content = Regex.Replace(content, @"<(br|p)[^>]*>", "\n", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"</p>", "\n", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"<[^>]+>", "");
            content = System.Net.WebUtility.HtmlDecode(content);
            content = Regex.Replace(content, @"\n{2,}", "\n\n");

            return content.Trim();
        }

        // Static utility to strip remaining HTML (optional usage)
        public static string StripHtml(string input)
        {
            return string.IsNullOrWhiteSpace(input)
                ? string.Empty
                : Regex.Replace(input, "<.*?>", string.Empty);
        }

        // Optional: if needed elsewhere
        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}