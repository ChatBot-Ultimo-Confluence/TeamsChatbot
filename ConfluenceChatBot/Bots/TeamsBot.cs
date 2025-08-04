using ConfluenceChatBot.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using System.Threading;
using System.Threading.Tasks;

namespace ConfluenceChatBot.Bots
{
    public class TeamsBot : ActivityHandler
    {
        private readonly ConfluenceService _confluenceService;
        private readonly EmbeddingService _embeddingService;
        private readonly PgVectorService _pgvectorService;

        public TeamsBot(ConfluenceService confluence, EmbeddingService embedding, PgVectorService pgvector)
        {
            _confluenceService = confluence;
            _embeddingService = embedding;
            _pgvectorService = pgvector;
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var pageContent = await _confluenceService.GetPageContentAsync("5210133");
            var embedding = await _embeddingService.GenerateEmbeddingAsync(pageContent);
            // var result = await _pgvectorService.SearchSimilarAsync(embedding);

            // await turnContext.SendActivityAsync(MessageFactory.Text($"Answer: {result}"), cancellationToken);
        }
    }
}