using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using AspNetCore.Controllers;

namespace WebApi
{
    public class Program
    {
        static readonly int RequestTimeOut = 20; // In seconds.

        public static int Main(string[] args)
        {
            int returnCode = 1;

            var cancellationToken = new CancellationTokenSource().Token;

            Console.WriteLine("Starting web host");
            var webHost = CreateHostBuilder(args).Build();
            webHost.RunAsync(cancellationToken);

            try
            {
                var requestTask = TestWebRequest();
                requestTask.Wait(new TimeSpan(0, 0, RequestTimeOut));
                returnCode = requestTask.Result;
            }
            catch (Exception e)
            {
                // If the server didn't start properly
                Console.WriteLine("Web request failed");
                Console.WriteLine(e.InnerException.ToString());
                return 1;
            }

            Console.WriteLine("Shutting down web host");
            webHost.StopAsync(new TimeSpan(0, 0, RequestTimeOut));
            
            return returnCode;
        }

        private async static Task<int> TestWebRequest()
        {
            HttpClient client = new HttpClient();
            Uri requestUri = new Uri("http://localhost:5000");
            Console.WriteLine($"Requesting {requestUri.ToString()}");
            string response = await client.GetStringAsync(requestUri);
            
            if (response == ValuesController.ServerResponse)
            {
                Console.WriteLine($"Success - Server responded with {ValuesController.ServerResponse}");
                return 0;
            }
            else
            {
                Console.WriteLine($"Failed - Server responded with {ValuesController.ServerResponse}");
                return 1;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
