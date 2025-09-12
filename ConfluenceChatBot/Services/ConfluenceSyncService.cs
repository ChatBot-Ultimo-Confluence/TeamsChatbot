using ConfluenceChatBot.Services;

namespace ConfluenceChatBot.BackgroundServices
{
    /// <summary>
    /// Background service that synchronizes Confluence pages with the vector database.
    /// Fetches all pages periodically, generates embeddings, and updates the DB.
    /// </summary>
    public class ConfluenceSyncService : BackgroundService
    {
        private readonly ConfluenceService _confluenceService;
        private readonly PgVectorService _pgVectorService;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Initializes the sync service with required dependencies.
        /// </summary>
        public ConfluenceSyncService(
            ConfluenceService confluenceService,
            PgVectorService pgVectorService)
        {
            _confluenceService = confluenceService;
            _pgVectorService = pgVectorService;
        }

        // --------------------------------------------------------------------
        // Background Execution
        // --------------------------------------------------------------------

        /// <summary>
        /// Executes the periodic synchronization loop until the service is cancelled.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int pageCount = 0;

                    // Fetch all pages from the Confluence space
                    var pages = await _confluenceService.GetAllPagesInSpaceAsync("SB1");

                    // Fetch current DB page IDs and their latest versions
                    var dbPages = await _pgVectorService.GetAllPageIdsAndVersionsAsync();
                    var dbPageDict = dbPages.ToDictionary(p => p.Id, p => p.Version);

                    // Track Confluence page IDs to detect removed pages
                    var confluencePageIds = new HashSet<string>();

                    foreach (var page in pages)
                    {
                        confluencePageIds.Add(page.Id);
                        dbPageDict.TryGetValue(page.Id, out int dbVersion);

                        // Update only if Confluence version is newer
                        if (dbVersion < page.Version)
                        {
                            if (dbVersion > 0)
                                await _pgVectorService.DeleteOldVersionsAsync(page.Id, page.Version);

                            await _pgVectorService.InsertEmbeddingOptimizedAsync(
                                page.Id,
                                page.Title,
                                page.Content,
                                page.Version);

                            pageCount++;
                            Console.WriteLine($"[Updated] Page: {page.Title} (v{page.Version})");
                        }
                    }

                    // Delete embeddings for pages removed from Confluence
                    var removedPageIds = dbPageDict.Keys.Except(confluencePageIds).ToHashSet();
                    if (removedPageIds.Any())
                    {
                        await _pgVectorService.DeletePagesByIdsAsync(removedPageIds);
                        Console.WriteLine($"Deleted embeddings for {removedPageIds.Count} removed pages.");
                    }

                    Console.WriteLine($"Sync completed. {pageCount} pages updated.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error updating pages: {ex.Message}");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
