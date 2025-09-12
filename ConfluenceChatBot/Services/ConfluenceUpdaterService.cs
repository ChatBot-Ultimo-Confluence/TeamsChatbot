using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.RegularExpressions;

namespace ConfluenceChatBot.Services
{
    public class ConfluenceUpdaterService : BackgroundService
    {
        private readonly ConfluenceService _confluenceService;
        private readonly PgVectorService _pgVectorService;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10); // polling interval

        public ConfluenceUpdaterService(
            ConfluenceService confluenceService,
            PgVectorService pgVectorService)
        {
            _confluenceService = confluenceService;
            _pgVectorService = pgVectorService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pages = await _confluenceService.GetAllPagesInSpaceAsync("SB1");

                    foreach (var page in pages)
                    {
                        var dbVersion = await _pgVectorService.GetPageVersionAsync(page.Id);

                        if (dbVersion == null || page.Version > dbVersion)
                        {
                            // Clean and insert page
                            await _pgVectorService.InsertEmbeddingOptimizedAsync(page.Id, page.Title, page.Content, page.Version);
                            Console.WriteLine($"Page updated: {page.Title}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Updater failed: {ex.Message}");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
