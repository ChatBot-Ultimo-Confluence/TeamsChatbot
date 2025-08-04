using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Bot.Builder.TraceExtensions;

namespace ConfluenceChatBot.Adapters
{
    public class AdapterWithErrorHandler : BotFrameworkHttpAdapter
    {
        public AdapterWithErrorHandler(ILogger<BotFrameworkHttpAdapter> logger) : base()
        {
            OnTurnError = async (turnContext, exception) =>
            {
                logger.LogError(exception, "[OnTurnError] unhandled error : {Message}", exception.Message);
                await turnContext.SendActivityAsync("Oops! Something went wrong with the bot. Please try again.");
            };
        }
    }
}