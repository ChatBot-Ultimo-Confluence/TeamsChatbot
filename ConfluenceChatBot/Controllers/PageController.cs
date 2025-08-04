using ConfluenceChatBot.Bots;
using ConfluenceChatBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ConfluenceChatBot.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PageController : ControllerBase
    {
        private readonly PageProcessor _pageProcessor;
        private readonly EmbeddingService _embeddingService;
        private readonly PgVectorService _pgVectorService;

        public PageController(
            EmbeddingService embeddingService,
            PgVectorService pgVectorService,
            PageProcessor pageProcessor)
        {
            _embeddingService = embeddingService;
            _pgVectorService = pgVectorService;
            _pageProcessor = pageProcessor;
        }

        [HttpPost("InsertData/{pageId}")]
        public async Task<IActionResult> InsertConfluencePageDataAsync(string pageId)
        {
            await _pageProcessor.ProcessPageAndInsertAsync(pageId);
            return Ok("Page processed and inserted.");
        }

        [HttpGet("SearchData")]
        public async Task<IActionResult> SearchConfluenceDataAsync([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query is required.");

            var response = await _pageProcessor.SearchSemanticDataAsync(query);

            if (response == "No similar content found.")
                return NotFound(response);
            if (response == "Query is required.")
                return BadRequest(response);
            if (response == "An error occurred during semantic search.")
                return StatusCode(500, response);

            return Ok(response);
        }
    }
}