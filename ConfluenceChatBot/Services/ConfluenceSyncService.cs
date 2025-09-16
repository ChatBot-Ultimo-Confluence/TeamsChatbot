using ConfluenceChatBot.Services;
using ConfluenceChatBot.Models;
using System.Linq;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var syncResult = await SyncPagesAsync("SB1");

                if (!syncResult.IsSuccess)
                {
                    Console.Error.WriteLine($"[Sync Error] {syncResult.Error}");
                }
                else
                {
                    Console.WriteLine($"Sync completed. {syncResult.Value} pages updated.");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        // --------------------------------------------------------------------
        // ROP Workflow
        // --------------------------------------------------------------------

        private async Task<Result<int>> SyncPagesAsync(string spaceKey)
        {
            return await _confluenceService.GetAllPagesInSpaceAsync(spaceKey)
                .BindAsync(async pages =>
                {
                    // unwrap db pages result
                    var dbPagesResult = await _pgVectorService.GetAllPageIdsAndVersionsAsync();
                    if (!dbPagesResult.IsSuccess)
                        return Result<int>.Fail(dbPagesResult.Error);

                    var dbPageDict = dbPagesResult.Value!
                        .ToDictionary(p => p.Id, p => p.Version);

                    var confluencePageIds = new HashSet<string>();
                    int updatedCount = 0;

                    foreach (var page in pages)
                    {
                        confluencePageIds.Add(page.Id);
                        dbPageDict.TryGetValue(page.Id, out int dbVersion);

                        if (dbVersion < page.Version)
                        {
                            var updateResult = await UpdatePageAsync(page, dbVersion);
                            if (!updateResult.IsSuccess)
                                return Result<int>.Fail(updateResult.Error);

                            updatedCount++;
                        }
                    }

                    var removedPageIds = dbPageDict.Keys.Except(confluencePageIds).ToHashSet();
                    if (removedPageIds.Any())
                    {
                        var deleteResult = await SafeDeletePagesAsync(removedPageIds);
                        if (!deleteResult.IsSuccess)
                            return Result<int>.Fail(deleteResult.Error);
                    }

                    return Result<int>.Ok(updatedCount);
                });
        }
        private async Task<Result<bool>> UpdatePageAsync(ConfluencePage page, int dbVersion)
        {
            try
            {
                if (dbVersion > 0)
                    await _pgVectorService.DeleteOldVersionsAsync(page.Id, page.Version);

                await _pgVectorService.InsertEmbeddingOptimizedAsync(
                    page.Id,
                    page.Title,
                    page.Content,
                    page.Version);

                Console.WriteLine($"[Updated] Page: {page.Title} (v{page.Version})");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"Failed to update page {page.Id}: {ex.Message}");
            }
        }

        private async Task<Result<bool>> SafeDeletePagesAsync(HashSet<string> removedPageIds)
        {
            try
            {
                await _pgVectorService.DeletePagesByIdsAsync(removedPageIds);
                Console.WriteLine($"Deleted embeddings for {removedPageIds.Count} removed pages.");
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"Failed to delete removed pages: {ex.Message}");
            }
        }
    }
}

//using ConfluenceChatBot.Services;

//namespace ConfluenceChatBot.BackgroundServices
//{
//    /// <summary>
//    /// Background service that synchronizes Confluence pages with the vector database.
//    /// Fetches all pages periodically, generates embeddings, and updates the DB.
//    /// </summary>
//    public class ConfluenceSyncService : BackgroundService
//    {
//        private readonly ConfluenceService _confluenceService;
//        private readonly PgVectorService _pgVectorService;
//        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

//        /// <summary>
//        /// Initializes the sync service with required dependencies.
//        /// </summary>
//        public ConfluenceSyncService(ConfluenceService confluenceService, PgVectorService pgVectorService)
//        {
//            _confluenceService = confluenceService;
//            _pgVectorService = pgVectorService;
//        }

//        // --------------------------------------------------------------------
//        // Background Execution
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Executes the periodic synchronization loop until the service is cancelled.
//        /// </summary>
//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            while (!stoppingToken.IsCancellationRequested)
//            {
//                try
//                {
//                    int pageCount = 0;

//                    // Fetch all pages from the Confluence space
//                    var pages = await _confluenceService.GetAllPagesInSpaceAsync("SB1");

//                    // Fetch current DB page IDs and their latest versions
//                    var dbPages = await _pgVectorService.GetAllPageIdsAndVersionsAsync();
//                    var dbPageDict = dbPages.ToDictionary(p => p.Id, p => p.Version);

//                    // Track Confluence page IDs to detect removed pages
//                    var confluencePageIds = new HashSet<string>();

//                    foreach (var page in pages)
//                    {
//                        confluencePageIds.Add(page.Id);
//                        dbPageDict.TryGetValue(page.Id, out int dbVersion);

//                        // Update only if Confluence version is newer
//                        if (dbVersion < page.Version)
//                        {
//                            if (dbVersion > 0)
//                                await _pgVectorService.DeleteOldVersionsAsync(page.Id, page.Version);

//                            await _pgVectorService.InsertEmbeddingOptimizedAsync(
//                                page.Id,
//                                page.Title,
//                                page.Content,
//                                page.Version);

//                            pageCount++;
//                            Console.WriteLine($"[Updated] Page: {page.Title} (v{page.Version})");
//                        }
//                    }

//                    // Delete embeddings for pages removed from Confluence
//                    var removedPageIds = dbPageDict.Keys.Except(confluencePageIds).ToHashSet();
//                    if (removedPageIds.Any())
//                    {
//                        await _pgVectorService.DeletePagesByIdsAsync(removedPageIds);
//                        Console.WriteLine($"Deleted embeddings for {removedPageIds.Count} removed pages.");
//                    }

//                    Console.WriteLine($"Sync completed. {pageCount} pages updated.");
//                }
//                catch (Exception ex)
//                {
//                    Console.Error.WriteLine($"Error updating pages: {ex.Message}");
//                }

//                await Task.Delay(_interval, stoppingToken);
//            }
//        }
//    }
//}
