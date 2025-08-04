using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Npgsql.Internal.Postgres;
using Pgvector;
using System.Net;
using System.Text.RegularExpressions;

namespace ConfluenceChatBot.Services
{
    public class PgVectorService
    {
        private readonly string _connectionString;
        private readonly EmbeddingService _embeddingService;

        public PgVectorService(IConfiguration configuration, EmbeddingService embeddingService)
        {
            var pgConfig = configuration.GetSection("PostgreSQL");
            _connectionString = $"Host={pgConfig["Host"]};Port={pgConfig["Port"]};Database={pgConfig["Database"]};Username={pgConfig["Username"]};Password={pgConfig["Password"]}";
            _embeddingService = embeddingService;
        }

        public async Task InsertEmbeddingAsync(string pageId, string title, string content)
        {
            var chunks = SplitByHeaderSections(content);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var chunk in chunks)
            {
                try
                {
                    var updatedContent = InsertInlineCodeSnippets(chunk.content);
                    var embeddings = await _embeddingService.GenerateEmbeddingAsync(updatedContent);

                    if (embeddings == null || embeddings.Length != 768)
                        throw new Exception("Embedding size mismatch.");

                    await using var cmd = new NpgsqlCommand(@"
                        INSERT INTO confluence_embeddings (page_id, title, content, section, embedding)
                        VALUES (@pageId, @title, @content, @section, @embedding::vector)", conn);

                    cmd.Parameters.AddWithValue("pageId", pageId);
                    cmd.Parameters.AddWithValue("title", title);
                    cmd.Parameters.AddWithValue("content", updatedContent);
                    cmd.Parameters.AddWithValue("section", chunk.header);
                    cmd.Parameters.AddWithValue("embedding", embeddings);

                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error inserting section embedding: {ex.Message}");
                }
            }
        }

        private string InsertInlineCodeSnippets(string htmlContent)
        {
            var codeSnippets = ExtractConfluenceCodeSnippets(htmlContent);
            var updatedContent = htmlContent;

            foreach (var snippet in codeSnippets)
            {
                var start = snippet.Substring(0, Math.Min(snippet.Length, 30));
                var index = updatedContent.IndexOf(start);

                if (index >= 0)
                {
                    updatedContent = updatedContent.Remove(index, start.Length)
                                                   .Insert(index, snippet);
                }
            }

            return updatedContent;
        }

        public List<string> ExtractConfluenceCodeSnippets(string confluenceHtml)
        {
            var snippets = new List<string>();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(confluenceHtml);

            var macroNodes = htmlDoc.DocumentNode.Descendants()
                .Where(n => n.Name == "ac:structured-macro" && n.Attributes["ac:name"]?.Value == "code")
                .ToList();

            foreach (var macro in macroNodes)
            {
                var body = macro.Descendants().FirstOrDefault(n => n.Name == "ac:plain-text-body");
                var commentNode = body?.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Comment);

                if (commentNode != null)
                {
                    var code = WebUtility.HtmlDecode(commentNode.InnerHtml.Trim());
                    if (!string.IsNullOrWhiteSpace(code))
                        snippets.Add(code);
                }
            }

            return snippets;
        }

        private List<(string header, string content)> SplitByHeaderSections(string htmlContent)
        {
            var results = new List<(string header, string content)>();
            var matches = Regex.Split(htmlContent, "(<h[23]>.*?</h[23]>)", RegexOptions.IgnoreCase);

            string currentHeader = "Introduction";
            string currentContent = "";

            foreach (var part in matches)
            {
                if (Regex.IsMatch(part, "<h[23]>", RegexOptions.IgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(currentContent))
                    {
                        results.Add((currentHeader, currentContent.Trim()));
                        currentContent = string.Empty;
                    }
                    currentHeader = StripHtml(part);
                }
                else
                {
                    currentContent += part;
                }
            }

            if (!string.IsNullOrWhiteSpace(currentContent))
                results.Add((currentHeader, currentContent.Trim()));

            return results;
        }

        private string StripHtml(string input)
        {
            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        public async Task<IEnumerable<(string section, string content)>> SearchSimilarAsync(string userQuery, int topK)
        {
            try
            {
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(userQuery);

                if (queryEmbedding == null || queryEmbedding.Length != 768)
                    throw new Exception("Embedding generation failed or incorrect dimensions.");

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var query = $@"
            SELECT section, content, embedding <=> @e AS similarity
            FROM confluence_embeddings
            ORDER BY similarity ASC
            LIMIT {topK};";

                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("e", new Vector(queryEmbedding));
                cmd.Parameters["e"].DataTypeName = "vector";

                var results = new List<(string section, string content)>();
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    results.Add((reader.GetString(0), reader.GetString(1)));
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error searching embeddings: {ex.Message}");
                throw;
            }
        }
    }
}