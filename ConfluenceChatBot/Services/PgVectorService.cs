using HtmlAgilityPack;
using Npgsql;
using Pgvector;
using System.Net;
using System.Text.RegularExpressions;
using ConfluenceChatBot.Models;

namespace ConfluenceChatBot.Services
{
    /// <summary>
    /// Provides operations for storing, searching, and managing embeddings in PostgreSQL with pgvector (ROP style).
    /// </summary>
    public class PgVectorService
    {
        private readonly string _connectionString;
        private readonly EmbeddingService _embeddingService;

        public PgVectorService(IConfiguration configuration, EmbeddingService embeddingService)
        {
            var pgConfig = configuration.GetSection("PostgreSQL");
            _connectionString =
                $"Host={pgConfig["Host"]};Port={pgConfig["Port"]};Database={pgConfig["Database"]};" +
                $"Username={pgConfig["Username"]};Password={pgConfig["Password"]}";

            _embeddingService = embeddingService;

            EnsureVectorIndex();
        }

        // --------------------------------------------------------------------
        // Setup
        // --------------------------------------------------------------------
        private Result<bool> EnsureVectorIndex()
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Open();

                using var cmd = new NpgsqlCommand(@"
            CREATE INDEX IF NOT EXISTS idx_confluence_embeddings_vector
            ON confluence_embeddings
            USING ivfflat (embedding vector_l2_ops) WITH (lists = 100);", conn);

                cmd.ExecuteNonQuery();

                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"EnsureVectorIndex failed: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------
        // Insert / Upsert
        // --------------------------------------------------------------------
        public async Task<Result<bool>> InsertEmbeddingOptimizedAsync(
            string pageId,
            string title,
            string htmlContent,
            int version,
            int batchSize = 5)
        {
            try
            {
                var plainText = CleanContentWithCode(htmlContent);
                var sections = SplitSections(plainText);

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var batches = sections
                    .Select((s, i) => new { s, i })
                    .GroupBy(x => x.i / batchSize)
                    .Select(g => g.Select(x => x.s).ToList())
                    .ToList();

                foreach (var batch in batches)
                {
                    var tasks = batch.Select(async section =>
                    {
                        var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(section.content);
                        return embeddingResult.IsSuccess
                            ? new { section.header, section.content, embedding = embeddingResult.Value }
                            : null;
                    }).ToList();

                    var results = await Task.WhenAll(tasks);

                    foreach (var r in results.Where(r => r != null))
                    {
                        await using var cmd = new NpgsqlCommand(@"
                            INSERT INTO confluence_embeddings (page_id, title, section, content, embedding, version)
                            VALUES (@pageId, @title, @section, @content, @embedding::vector, @version)
                            ON CONFLICT (page_id, section, version) DO UPDATE
                            SET content = EXCLUDED.content,
                                embedding = EXCLUDED.embedding,
                                title = EXCLUDED.title", conn);

                        cmd.Parameters.AddWithValue("pageId", pageId);
                        cmd.Parameters.AddWithValue("title", title);
                        cmd.Parameters.AddWithValue("section", r.header);
                        cmd.Parameters.AddWithValue("content", r.content);
                        cmd.Parameters.AddWithValue("embedding", new Vector(r.embedding));
                        cmd.Parameters.AddWithValue("version", version);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"InsertEmbeddingOptimized failed: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------
        // Search
        // --------------------------------------------------------------------
        public async Task<Result<IEnumerable<(string section, string content)>>> SearchSimilarAsync(
            string userQuery,
            int topK)
        {
            try
            {
                var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(userQuery);
                if (!embeddingResult.IsSuccess)
                    return Result<IEnumerable<(string, string)>>.Fail(embeddingResult.Error);

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = $@"
                    SELECT section, content, embedding <=> @e AS similarity
                    FROM confluence_embeddings
                    ORDER BY similarity ASC
                    LIMIT {topK};";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("e", new Vector(embeddingResult.Value!));
                cmd.Parameters["e"].DataTypeName = "vector";

                var results = new List<(string section, string content)>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var section = reader.GetString(0);
                    var content = Regex.Replace(reader.GetString(1), @"\s{2,}", " ").Trim();
                    results.Add((section, content));
                }

                return Result<IEnumerable<(string, string)>>.Ok(results);
            }
            catch (Exception ex)
            {
                return Result<IEnumerable<(string, string)>>.Fail($"SearchSimilarAsync failed: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------
        // Versions & Cleanup
        // --------------------------------------------------------------------
        public async Task<Result<int?>> GetPageVersionAsync(string pageId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(
                    "SELECT MAX(version) FROM confluence_embeddings WHERE page_id = @pageId",
                    conn);

                cmd.Parameters.AddWithValue("pageId", pageId);

                var result = await cmd.ExecuteScalarAsync();
                if (result == DBNull.Value) return Result<int?>.Ok(null);

                return Result<int?>.Ok(Convert.ToInt32(result));
            }
            catch (Exception ex)
            {
                return Result<int?>.Fail($"GetPageVersionAsync failed: {ex.Message}");
            }
        }

        public async Task<Result<bool>> DeleteOldVersionsAsync(string pageId, int currentVersion)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"
                    DELETE FROM confluence_embeddings
                    WHERE page_id = @pageId
                    AND version < @currentVersion";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("pageId", pageId);
                cmd.Parameters.AddWithValue("currentVersion", currentVersion);

                await cmd.ExecuteNonQueryAsync();
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"DeleteOldVersionsAsync failed: {ex.Message}");
            }
        }

        public async Task<Result<bool>> DeletePagesByIdsAsync(HashSet<string> pageIds)
        {
            if (!pageIds.Any()) return Result<bool>.Ok(true);

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var ids = string.Join(",", pageIds.Select(id => $"'{id}'"));
                var sql = $"DELETE FROM confluence_embeddings WHERE page_id IN ({ids})";

                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();

                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"DeletePagesByIdsAsync failed: {ex.Message}");
            }
        }

        public async Task<Result<List<(string Id, int Version)>>> GetAllPageIdsAndVersionsAsync()
        {
            try
            {
                var pages = new List<(string, int)>();

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = "SELECT page_id, MAX(version) AS version FROM confluence_embeddings GROUP BY page_id";
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    pages.Add((reader.GetString(0), reader.GetInt32(1)));

                return Result<List<(string, int)>>.Ok(pages);
            }
            catch (Exception ex)
            {
                return Result<List<(string, int)>>.Fail($"GetAllPageIdsAndVersionsAsync failed: {ex.Message}");
            }
        }
        // --------------------------------------------------------------------
        // Helpers: Content Processing
        // --------------------------------------------------------------------

        /// <summary>
        /// Cleans HTML content while preserving Confluence code snippets.
        /// </summary>
        private string CleanContentWithCode(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent)) return "";

            htmlContent = RemoveCData(htmlContent);

            // Extract and temporarily replace code snippets
            var codeSnippets = ExtractConfluenceCodeSnippets(htmlContent);
            for (int i = 0; i < codeSnippets.Count; i++)
                htmlContent = htmlContent.Replace(codeSnippets[i], $"__CODE_BLOCK_{i}__");

            // Remove HTML tags and decode
            htmlContent = WebUtility.HtmlDecode(htmlContent);
            htmlContent = Regex.Replace(htmlContent, "<.*?>", " ");

            // Remove emojis/special symbols
            htmlContent = Regex.Replace(htmlContent,
                @"[\u2700-\u27BF]|[\uE000-\uF8FF]|[\uD83C-\uDBFF\uDC00-\uDFFF]",
                " ");

            // Normalize whitespace
            htmlContent = Regex.Replace(htmlContent, @"\s{2,}", " ").Trim();

            // Restore code snippets as Markdown
            for (int i = 0; i < codeSnippets.Count; i++)
                htmlContent = htmlContent.Replace($"__CODE_BLOCK_{i}__", $"\n```\n{codeSnippets[i]}\n```\n");

            return htmlContent;
        }
        /// <summary>
        /// Splits content into sections based on headings or step numbers.
        /// </summary>
        private List<(string header, string content)> SplitSections(string content)
        {
            return Regex.Split(content, @"(?<=\d\.\s)|(?<=##\s)|(?<=###\s)")
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s =>
                        {
                            string header = s.Length > 50 ? s.Substring(0, 50) : s;
                            return (header, s.Trim());
                        })
                        .ToList();
        }

        /// <summary>
        /// Extracts code snippets from Confluence HTML (ac:structured-macro).
        /// </summary>
        public List<string> ExtractConfluenceCodeSnippets(string confluenceHtml)
        {
            var snippets = new List<string>();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(confluenceHtml);

            var macroNodes = htmlDoc.DocumentNode.Descendants()
                .Where(n => n.Name == "ac:structured-macro" &&
                            n.Attributes["ac:name"]?.Value == "code")
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

        /// <summary>
        /// Removes CDATA blocks from text.
        /// </summary>
        private string RemoveCData(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();
            return Regex.Replace(
                text,
                @"<!\[CDATA\[(.*?)\]\]>",
                m => m.Groups[1].Value.Trim(),
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
    }
}

//using HtmlAgilityPack;
//using Npgsql;
//using Pgvector;
//using System.Net;
//using System.Text.RegularExpressions;

//namespace ConfluenceChatBot.Services
//{
//    /// <summary>
//    /// Provides operations for storing, searching, and managing embeddings in PostgreSQL with pgvector.
//    /// </summary>
//    public class PgVectorService
//    {
//        private readonly string _connectionString;
//        private readonly EmbeddingService _embeddingService;

//        public PgVectorService(IConfiguration configuration, EmbeddingService embeddingService)
//        {
//            var pgConfig = configuration.GetSection("PostgreSQL");
//            _connectionString =
//                $"Host={pgConfig["Host"]};Port={pgConfig["Port"]};Database={pgConfig["Database"]};" +
//                $"Username={pgConfig["Username"]};Password={pgConfig["Password"]}";

//            _embeddingService = embeddingService;

//            EnsureVectorIndex();
//        }

//        // --------------------------------------------------------------------
//        // Database Setup
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Ensures that the vector index exists on the embeddings table.
//        /// </summary>
//        private void EnsureVectorIndex()
//        {
//            using var conn = new NpgsqlConnection(_connectionString);
//            conn.Open();

//            using var cmd = new NpgsqlCommand(@"
//                CREATE INDEX IF NOT EXISTS idx_confluence_embeddings_vector
//                ON confluence_embeddings
//                USING ivfflat (embedding vector_l2_ops) WITH (lists = 100);
//            ", conn);

//            cmd.ExecuteNonQuery();
//        }

//        // --------------------------------------------------------------------
//        // Insert / Upsert
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Inserts embeddings for a Confluence page, splitting content into sections.
//        /// Existing rows are upserted if conflicts occur.
//        /// </summary>
//        public async Task InsertEmbeddingOptimizedAsync(
//            string pageId,
//            string title,
//            string htmlContent,
//            int version,
//            int batchSize = 5)
//        {
//            // Clean content and preserve code snippets
//            var plainText = CleanContentWithCode(htmlContent);

//            // Split into sections by headers or steps
//            var sections = SplitSections(plainText);

//            await using var conn = new NpgsqlConnection(_connectionString);
//            await conn.OpenAsync();

//            // Group sections into batches for embedding
//            var batches = sections
//                .Select((s, i) => new { s, i })
//                .GroupBy(x => x.i / batchSize)
//                .Select(g => g.Select(x => x.s).ToList())
//                .ToList();

//            foreach (var batch in batches)
//            {
//                // Generate embeddings in parallel
//                var tasks = batch.Select(async section =>
//                {
//                    try
//                    {
//                        var embedding = await _embeddingService.GenerateEmbeddingAsync(section.content);
//                        if (embedding == null || embedding.Length == 0) return null;
//                        return new { section.header, section.content, embedding };
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.Error.WriteLine($"Embedding generation failed: {ex.Message}");
//                        return null;
//                    }
//                }).ToList();

//                var results = await Task.WhenAll(tasks);

//                // Upsert each section
//                foreach (var r in results.Where(r => r != null))
//                {
//                    await using var cmd = new NpgsqlCommand(@"
//                        INSERT INTO confluence_embeddings (page_id, title, section, content, embedding, version)
//                        VALUES (@pageId, @title, @section, @content, @embedding::vector, @version)
//                        ON CONFLICT (page_id, section, version) DO UPDATE
//                        SET content = EXCLUDED.content,
//                            embedding = EXCLUDED.embedding,
//                            title = EXCLUDED.title", conn);

//                    cmd.Parameters.AddWithValue("pageId", pageId);
//                    cmd.Parameters.AddWithValue("title", title);
//                    cmd.Parameters.AddWithValue("section", r.header);
//                    cmd.Parameters.AddWithValue("content", r.content);
//                    cmd.Parameters.AddWithValue("embedding", new Vector(r.embedding));
//                    cmd.Parameters.AddWithValue("version", version);

//                    await cmd.ExecuteNonQueryAsync();
//                }
//            }
//        }

//        // --------------------------------------------------------------------
//        // Search
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Searches for similar content sections using vector similarity.
//        /// </summary>
//        public async Task<IEnumerable<(string section, string content)>> SearchSimilarAsync(
//            string userQuery,
//            int topK)
//        {
//            try
//            {
//                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(userQuery);
//                if (queryEmbedding == null || queryEmbedding.Length == 0)
//                    throw new Exception("Embedding generation failed or empty.");

//                await using var conn = new NpgsqlConnection(_connectionString);
//                await conn.OpenAsync();

//                var query = $@"
//                    SELECT section, content, embedding <=> @e AS similarity
//                    FROM confluence_embeddings
//                    ORDER BY similarity ASC
//                    LIMIT {topK};";

//                await using var cmd = new NpgsqlCommand(query, conn);
//                cmd.Parameters.AddWithValue("e", new Vector(queryEmbedding));
//                cmd.Parameters["e"].DataTypeName = "vector";

//                var results = new List<(string section, string content)>();

//                await using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    var section = reader.GetString(0);
//                    var content = Regex.Replace(reader.GetString(1), @"\s{2,}", " ").Trim();

//                    results.Add((section, content));
//                }

//                return results;
//            }
//            catch (Exception ex)
//            {
//                Console.Error.WriteLine($"Error searching embeddings: {ex.Message}");
//                return Enumerable.Empty<(string, string)>();
//            }
//        }

//        // --------------------------------------------------------------------
//        // Version Management
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Gets the latest stored version for a page.
//        /// </summary>
//        public async Task<int?> GetPageVersionAsync(string pageId)
//        {
//            await using var conn = new NpgsqlConnection(_connectionString);
//            await conn.OpenAsync();

//            var cmd = new NpgsqlCommand(
//                "SELECT MAX(version) FROM confluence_embeddings WHERE page_id = @pageId",
//                conn);

//            cmd.Parameters.AddWithValue("pageId", pageId);

//            var result = await cmd.ExecuteScalarAsync();
//            if (result == DBNull.Value) return null;
//            return Convert.ToInt32(result);
//        }

//        /// <summary>
//        /// Deletes all older versions of a page, keeping only the current version.
//        /// </summary>
//        public async Task DeleteOldVersionsAsync(string pageId, int currentVersion)
//        {
//            await using var conn = new NpgsqlConnection(_connectionString);
//            await conn.OpenAsync();

//            var sql = @"
//                DELETE FROM confluence_embeddings
//                WHERE page_id = @pageId
//                AND version < @currentVersion";

//            await using var cmd = new NpgsqlCommand(sql, conn);
//            cmd.Parameters.AddWithValue("pageId", pageId);
//            cmd.Parameters.AddWithValue("currentVersion", currentVersion);

//            await cmd.ExecuteNonQueryAsync();
//        }

//        /// <summary>
//        /// Deletes embeddings for pages no longer present in Confluence.
//        /// </summary>
//        public async Task DeletePagesByIdsAsync(HashSet<string> pageIds)
//        {
//            if (!pageIds.Any()) return;

//            await using var conn = new NpgsqlConnection(_connectionString);
//            await conn.OpenAsync();

//            var ids = string.Join(",", pageIds.Select(id => $"'{id}'"));
//            var sql = $"DELETE FROM confluence_embeddings WHERE page_id IN ({ids})";

//            await using var cmd = new NpgsqlCommand(sql, conn);
//            await cmd.ExecuteNonQueryAsync();
//        }

//        /// <summary>
//        /// Gets all page IDs and their latest versions.
//        /// </summary>
//        public async Task<List<(string Id, int Version)>> GetAllPageIdsAndVersionsAsync()
//        {
//            var pages = new List<(string, int)>();

//            await using var conn = new NpgsqlConnection(_connectionString);
//            await conn.OpenAsync();

//            var sql = "SELECT page_id, MAX(version) AS version FROM confluence_embeddings GROUP BY page_id";

//            await using var cmd = new NpgsqlCommand(sql, conn);
//            await using var reader = await cmd.ExecuteReaderAsync();

//            while (await reader.ReadAsync())
//            {
//                pages.Add((reader.GetString(0), reader.GetInt32(1)));
//            }

//            return pages;
//        }

//// --------------------------------------------------------------------
//// Helpers: Content Processing
//// --------------------------------------------------------------------

///// <summary>
///// Cleans HTML content while preserving Confluence code snippets.
///// </summary>
//private string CleanContentWithCode(string htmlContent)
//{
//    if (string.IsNullOrWhiteSpace(htmlContent)) return "";

//    htmlContent = RemoveCData(htmlContent);

//    // Extract and temporarily replace code snippets
//    var codeSnippets = ExtractConfluenceCodeSnippets(htmlContent);
//    for (int i = 0; i < codeSnippets.Count; i++)
//        htmlContent = htmlContent.Replace(codeSnippets[i], $"__CODE_BLOCK_{i}__");

//    // Remove HTML tags and decode
//    htmlContent = WebUtility.HtmlDecode(htmlContent);
//    htmlContent = Regex.Replace(htmlContent, "<.*?>", " ");

//    // Remove emojis/special symbols
//    htmlContent = Regex.Replace(htmlContent,
//        @"[\u2700-\u27BF]|[\uE000-\uF8FF]|[\uD83C-\uDBFF\uDC00-\uDFFF]",
//        " ");

//    // Normalize whitespace
//    htmlContent = Regex.Replace(htmlContent, @"\s{2,}", " ").Trim();

//    // Restore code snippets as Markdown
//    for (int i = 0; i < codeSnippets.Count; i++)
//        htmlContent = htmlContent.Replace($"__CODE_BLOCK_{i}__", $"\n```\n{codeSnippets[i]}\n```\n");

//    return htmlContent;
//}

//        /// <summary>
//        /// Splits content into sections based on headings or step numbers.
//        /// </summary>
//        private List<(string header, string content)> SplitSections(string content)
//        {
//            return Regex.Split(content, @"(?<=\d\.\s)|(?<=##\s)|(?<=###\s)")
//                        .Where(s => !string.IsNullOrWhiteSpace(s))
//                        .Select(s =>
//                        {
//                            string header = s.Length > 50 ? s.Substring(0, 50) : s;
//                            return (header, s.Trim());
//                        })
//                        .ToList();
//        }

//        /// <summary>
//        /// Extracts code snippets from Confluence HTML (ac:structured-macro).
//        /// </summary>
//        public List<string> ExtractConfluenceCodeSnippets(string confluenceHtml)
//        {
//            var snippets = new List<string>();
//            var htmlDoc = new HtmlDocument();
//            htmlDoc.LoadHtml(confluenceHtml);

//            var macroNodes = htmlDoc.DocumentNode.Descendants()
//                .Where(n => n.Name == "ac:structured-macro" &&
//                            n.Attributes["ac:name"]?.Value == "code")
//                .ToList();

//            foreach (var macro in macroNodes)
//            {
//                var body = macro.Descendants().FirstOrDefault(n => n.Name == "ac:plain-text-body");
//                var commentNode = body?.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Comment);

//                if (commentNode != null)
//                {
//                    var code = WebUtility.HtmlDecode(commentNode.InnerHtml.Trim());
//                    if (!string.IsNullOrWhiteSpace(code))
//                        snippets.Add(code);
//                }
//            }

//            return snippets;
//        }

//        /// <summary>
//        /// Removes CDATA blocks from text.
//        /// </summary>
//        private string RemoveCData(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text)) return text;

//            text = text.Trim();
//            return Regex.Replace(
//                text,
//                @"<!\[CDATA\[(.*?)\]\]>",
//                m => m.Groups[1].Value.Trim(),
//                RegexOptions.Singleline | RegexOptions.IgnoreCase);
//        }
//    }
//}
