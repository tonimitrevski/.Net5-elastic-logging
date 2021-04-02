using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Elasticsearch;
using Serilog.Sinks.Elasticsearch;
using System;
using System.Collections.Generic;

namespace SeriologKibana
{
    public class Program
    {
        public static int Main(string[] args)
        {
            IConfigurationRoot configuration = GetConfiguration();
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
                .Enrich.WithProperty("service", "Seriolog")
                .WriteTo.Console()
                .WriteTo.Elasticsearch(ConfigureElasticSink(configuration))
                .CreateLogger();

            try
            {
                Log.Information("Starting web host");
                CreateHostBuilder(args).Build().Run();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        private static ElasticsearchSinkOptions ConfigureElasticSink(IConfigurationRoot configuration)
        {
            return new ElasticsearchSinkOptions(new Uri(configuration["ElasticConfiguration:Uri"]))
            {
                MinimumLogEventLevel = LogEventLevel.Warning,
                CustomFormatter = new ExceptionAsObjectJsonFormatter(renderMessage: true),
                EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                                           EmitEventFailureHandling.WriteToFailureSink |
                                           EmitEventFailureHandling.RaiseCallback,
                BufferCleanPayload = (failingEvent, statuscode, exception) =>
                {
                    dynamic e = JObject.Parse(failingEvent);
                    return JsonConvert.SerializeObject(new Dictionary<string, object>()
                        {
                            { "@timestamp",e["@timestamp"]},
                            { "level","Error"},
                            { "message","Error: "+e.message},
                            { "messageTemplate",e.messageTemplate},
                            { "failingStatusCode", statuscode},
                            { "failingException", exception}
                        });
                },
            };
        }

        private static IConfigurationRoot GetConfiguration()
        {
            return new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile(
                    $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json",
                    optional: true)
                .Build();
        }
    }
}
