using ConfluenceChatBot.Bots;
using Microsoft.AspNetCore.Mvc;

namespace ConfluenceChatBot.Controllers
{
    /// <summary>
    /// Controller for handling Confluence page operations:
    /// inserting page content and performing semantic search.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PageController : ControllerBase
    {
        private readonly PageProcessor _pageProcessor;

        /// <summary>
        /// Initializes the controller with the required <see cref="PageProcessor"/>.
        /// </summary>
        public PageController(PageProcessor pageProcessor)
        {
            _pageProcessor = pageProcessor;
        }

        // --------------------------------------------------------------------
        // Insert Page Data
        // --------------------------------------------------------------------

        /// <summary>
        /// Processes a Confluence page and inserts its content into the database.
        /// </summary>
        /// <param name="pageId">The ID of the Confluence page to process.</param>
        /// <returns>HTTP 200 OK if processed successfully.</returns>
        [HttpPost("InsertData/{pageId}")]
        public async Task<IActionResult> InsertConfluencePageDataAsync(string pageId)
        {
            await _pageProcessor.ProcessPageAndInsertAsync(pageId);
            return Ok("Page processed and inserted.");
        }

        // --------------------------------------------------------------------
        // Semantic Search
        // --------------------------------------------------------------------

        /// <summary>
        /// Searches the vector database for content similar to the user's query.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <returns>
        /// HTTP 200 with search results,
        /// 400 if query is missing,
        /// 404 if no similar content found,
        /// or 500 if an error occurs during search.
        /// </returns>
        [HttpGet("SearchData")]
        public async Task<IActionResult> SearchConfluenceDataAsync([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query is required.");

            var response = await _pageProcessor.SearchSemanticDataAsync(query);

            return response switch
            {
                "No similar content found." => NotFound(response),
                "Query is required." => BadRequest(response),
                "An error occurred during semantic search." => StatusCode(500, response),
                _ => Ok(response)
            };
        }
    }
}
