using ConfluenceChatBot.Bots;
using ConfluenceChatBot.Services;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using ConfluenceChatBot.Models;

namespace ConfluenceChatBot.Extensions
{
    /// <summary>
    /// Extension methods to register application services, Semantic Kernel, and HttpClients (ROP style).
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        // --------------------------------------------------------------------
        // Application Services
        // --------------------------------------------------------------------
       
        public static Result<IServiceCollection> AddAppServices(this IServiceCollection services)
        {
            return Result<IServiceCollection>.Ok(services)
                .Bind(s =>
                {
                    s.AddSingleton<ConfluenceService>();
                    s.AddSingleton<EmbeddingService>();
                    s.AddSingleton<PgVectorService>();
                    s.AddSingleton<PageProcessor>();
                    return Result<IServiceCollection>.Ok(s);
                });
        }
        // --------------------------------------------------------------------
        // Semantic Kernel Integration
        // --------------------------------------------------------------------
        public static Result<IServiceCollection> AddSemanticKernel(this IServiceCollection services, IConfiguration config)
        {
            return Result<IServiceCollection>.Ok(services)
                .Bind(s =>
                {
                    s.AddSingleton<Kernel>(sp =>
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

                    s.AddSingleton<IChatCompletionService>(sp =>
                    {
                        var kernel = sp.GetRequiredService<Kernel>();
                        return kernel.GetRequiredService<IChatCompletionService>();
                    });

                    return Result<IServiceCollection>.Ok(s);
                });
        }

        // --------------------------------------------------------------------
        // HttpClient Registrations
        // --------------------------------------------------------------------
        public static Result<IServiceCollection> AddHttpClients(this IServiceCollection services)
        {
            return Result<IServiceCollection>.Ok(services)
                .Bind(s =>
                {
                    // Default HttpClient
                    s.AddHttpClient();

                    // Named HttpClient for Ollama API
                    s.AddHttpClient("ollama", client =>
                    {
                        client.BaseAddress = new Uri("http://localhost:11434");
                    });

                    return Result<IServiceCollection>.Ok(s);
                });
        }
    }
}

//using ConfluenceChatBot.Bots;
//using ConfluenceChatBot.Services;
//using Microsoft.SemanticKernel.ChatCompletion;
//using Microsoft.SemanticKernel;

//namespace ConfluenceChatBot.Extensions
//{
//    /// <summary>
//    /// Extension methods to register application services, Semantic Kernel, and HttpClients.
//    /// </summary>
//    public static class ServiceCollectionExtensions
//    {
//        // --------------------------------------------------------------------
//        // Application Services
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Registers application-specific services into the DI container.
//        /// </summary>
//        public static IServiceCollection AddAppServices(this IServiceCollection services)
//        {
//            try
//            {
//                services.AddSingleton<ConfluenceService>();
//                services.AddSingleton<EmbeddingService>();
//                services.AddSingleton<PgVectorService>();
//                services.AddSingleton<PageProcessor>();
//                return services;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"[Startup Error - AddAppServices] {ex.Message}");
//                throw;
//            }
//        }

//        // --------------------------------------------------------------------
//        // Semantic Kernel Integration
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Registers Semantic Kernel and Ollama chat completion services.
//        /// </summary>
//        public static IServiceCollection AddSemanticKernel(this IServiceCollection services, IConfiguration config)
//        {
//            try
//            {
//                services.AddSingleton<Kernel>(sp =>
//                {
//                    var kernelBuilder = Kernel.CreateBuilder();

//#pragma warning disable SKEXP0070
//                    kernelBuilder.AddOllamaChatCompletion(
//                        modelId: config["Ollama:ModelId"] ?? "phi3:mini",
//                        endpoint: new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434")
//                    );
//#pragma warning restore SKEXP0070

//                    return kernelBuilder.Build();
//                });

//                services.AddSingleton<IChatCompletionService>(sp =>
//                {
//                    var kernel = sp.GetRequiredService<Kernel>();
//                    return kernel.GetRequiredService<IChatCompletionService>();
//                });

//                return services;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"[Startup Error - AddSemanticKernel] {ex.Message}");
//                throw;
//            }
//        }

//        // --------------------------------------------------------------------
//        // HttpClient Registrations
//        // --------------------------------------------------------------------

//        /// <summary>
//        /// Registers HttpClient instances required by the application.
//        /// </summary>
//        public static IServiceCollection AddHttpClients(this IServiceCollection services)
//        {
//            try
//            {
//                // Default HttpClient
//                services.AddHttpClient();

//                // Named HttpClient for Ollama API
//                services.AddHttpClient("ollama", client =>
//                {
//                    client.BaseAddress = new Uri("http://localhost:11434");
//                });

//                return services;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"[Startup Error - AddHttpClients] {ex.Message}");
//                throw;
//            }
//        }
//    }
//}
