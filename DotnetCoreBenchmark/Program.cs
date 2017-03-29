using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DotnetCoreBenchmark
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Task.Run(async () => await new Program("http://127.0.0.1:5001").Run()).GetAwaiter().GetResult();
        }
        private readonly string _serverUrl;

        public Program(string serverUrl)
        {
            _serverUrl = serverUrl;
        }

        private async Task Run()
        {
            var kill = await StartServer(_serverUrl);

            try
            {
                // Warm up the server
                var simpleResults = await RunSimpleTests(10);
                var proxyResults = await RunProxyTests(10);

                simpleResults = await RunSimpleTests(100);
                proxyResults = await RunProxyTests(100);

                Console.WriteLine(new String('=', 80));
                PrintResultsSummary(simpleResults, "Hello world requests", false);
                Console.WriteLine(new String('-', 80));
                PrintResultsSummary(proxyResults, "Requests with inner HttpClient request", true);
                Console.WriteLine(new String('=', 80));

                File.WriteAllText("test-output.json", JsonConvert.SerializeObject(new
                {
                    Simple = simpleResults,
                    Proxy = proxyResults
                }, Formatting.Indented));
            }
            finally
            {
                await kill();
            }
        }

        private void PrintResultsSummary(List<TestResult> results, string title, bool includeInnerTimes)
        {
            var successfulResults = results.Where(result => result.RequestWasSuccessful).ToList();
            var times = successfulResults.Select(result => result.RequestTime.TotalMilliseconds);

            Console.WriteLine($"Summary: {title}");
            Console.WriteLine($"{successfulResults.Count}/{results.Count} succeeded.");
            Console.WriteLine($"Total request times:");
            PrintStats(times);

            if (includeInnerTimes)
            {
                var innerTimes = successfulResults.Select(result =>
                {
                    var ms = result.InnerRequestTime?.TotalMilliseconds;
                    if (ms == null)
                    {
                        throw new Exception($"{nameof(result.InnerRequestTime)} should not be null.");
                    }
                    return ms.Value;
                });

                Console.WriteLine($"Inner request times:");
                PrintStats(innerTimes);

                var percentages = successfulResults.Select(result =>
                {
                    var innerTime = result.InnerRequestTime?.TotalMilliseconds;
                    if (innerTime == null)
                    {
                        throw new Exception($"{nameof(result.InnerRequestTime)} should not be null.");
                    }

                    return (innerTime.Value / result.RequestTime.TotalMilliseconds) * 100;
                });

                Console.WriteLine($"Percentages of total request time spent in inner request:");
                PrintStats(percentages, "%");
            }
        }

        private void PrintStats(IEnumerable<double> times, string label = "ms")
        {
            Console.WriteLine($"  Avg: {times.Average()}{label}");
            Console.WriteLine($"  Min: {times.Min()}{label}");
            Console.WriteLine($"  Max: {times.Max()}{label}");
        }

        private async Task<List<TestResult>> RunSimpleTests(int requests)
        {
            var client = new HttpClient();
            var results = new List<TestResult>();

            for (var i = 0; i < requests; i++)
            {
                var timer = Stopwatch.StartNew();
                var response = await client.GetAsync($"{_serverUrl}/hello");
                timer.Stop();

                results.Add(new TestResult
                {
                    RequestTime = timer.Elapsed,
                    RequestWasSuccessful = response.IsSuccessStatusCode,
                    InnerRequestTime = null,
                });
            }

            return results;
        }

        private async Task<List<TestResult>> RunProxyTests(int requests)
        {
            var client = new HttpClient();
            var results = new List<TestResult>();

            for (var i = 0; i < requests; i++)
            {
                var timer = Stopwatch.StartNew();
                var response = await client.GetAsync($"{_serverUrl}/proxy");
                timer.Stop();

                TimeSpan? innerRequestTime = null;

                if (response.IsSuccessStatusCode)
                {
                    var ticks = int.Parse(await response.Content.ReadAsStringAsync());
                    innerRequestTime = TimeSpan.FromTicks(ticks);
                }

                results.Add(new TestResult
                {
                    RequestTime = timer.Elapsed,
                    RequestWasSuccessful = response.IsSuccessStatusCode,
                    InnerRequestTime = innerRequestTime,
                });
            }

            return results;
        }

        private class TestResult
        {
            public TimeSpan RequestTime { get; set; }
            public TimeSpan? InnerRequestTime { get; set; }
            public bool RequestWasSuccessful { get; set; }
        }

        private static string GetProjectDirectory()
        {
            // TODO
            return "/Users/lyleshearer/Code/dotnet-core-perf-comparison/1.1.1/DotnetCoreBenchmark";
        }

        private static async Task<Func<Task>> StartServer(string url)
        {
            var serverDirectory = Path.Combine(GetProjectDirectory(), "../DotnetCoreServer");
            var dllPath = Path.Combine(serverDirectory, "bin/Release/netcoreapp1.1/DotnetCoreServer.dll");

            await AsyncProcess.Run("dotnet", "restore", serverDirectory);
            await AsyncProcess.Run("dotnet", "build -c Release", serverDirectory);

            var environment = new Dictionary<string, string>
            {
                ["ASPNETCORE_URLS"] = url
            };

            var cancel = new CancellationTokenSource();
            var started = false;
            // Use exec instead of run so that there's no child process that doesn't get killed
            var run = AsyncProcess.Run("dotnet", $"exec {dllPath}", token: cancel.Token, environmentOverrides: environment, onStandardOutput: line =>
            {
                if (line.Contains("Now listening on:"))
                {
                    Console.WriteLine(line);
                    started = true;
                }
            });

            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            while (true)
            {
                if (run.IsCanceled || run.IsFaulted)
                {
                    // This should throw
                    await run;
                }
                if (started)
                {
                    // Return cancel function
                    return () =>
                    {
                        cancel.Cancel();
                        return run;
                    };
                }
                if (timeout.IsCancellationRequested)
                {
                    throw new Exception("Timed out.");
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
