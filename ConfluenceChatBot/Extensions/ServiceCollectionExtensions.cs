using ConfluenceChatBot.Bots;
using ConfluenceChatBot.Services;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;

namespace ConfluenceChatBot.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            try
            {
                services.AddSingleton<ConfluenceService>();
                services.AddSingleton<EmbeddingService>();
                services.AddSingleton<PgVectorService>();
                services.AddSingleton<PageProcessor>();
                return services;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Startup Error - AddAppServices] {ex.Message}");
                throw;
            }
        }

        public static IServiceCollection AddSemanticKernel(this IServiceCollection services, IConfiguration config)
        {
            try
            {
                services.AddSingleton<Kernel>(sp =>
                {
                    var kernelBuilder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0070
                    kernelBuilder.AddOllamaChatCompletion(
                        modelId: config["Ollama:ModelId"] ?? "phi3:mini",
                        endpoint: new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434")
                    );
#pragma warning restore SKEXP0070

                    return kernelBuilder.Build();
                });

                services.AddSingleton<IChatCompletionService>(sp =>
                {
                    var kernel = sp.GetRequiredService<Kernel>();
                    return kernel.GetRequiredService<IChatCompletionService>();
                });

                return services;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Startup Error - AddSemanticKernel] {ex.Message}");
                throw;
            }
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services)
        {
            try
            {
                services.AddHttpClient();
                services.AddHttpClient("ollama", client =>
                {
                    client.BaseAddress = new Uri("http://localhost:11434");
                });

                return services;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Startup Error - AddHttpClients] {ex.Message}");
                throw;
            }
        }
    }
}