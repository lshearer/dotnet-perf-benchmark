using System;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotnetCoreServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            var hostUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

            app.Map("/hello", application =>
            {
                application.Run(async (context) =>
                {
                    await context.Response.WriteAsync("Hello World!");
                });
            });

            var client = new HttpClient();

            app.Map("/proxy", application =>
            {
                application.Run(async (context) =>
                {
                    var timer = Stopwatch.StartNew();
                    var response = await client.GetAsync($"{hostUrl}/hello");
                    timer.Stop();

                    await context.Response.WriteAsync(timer.ElapsedTicks.ToString());
                });
            });
        }
    }
}
