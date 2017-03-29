using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DotnetCoreBenchmark
{
    public static class AsyncProcess
    {
        public static async Task<Result> Run(
            string filename,
            string arguments,
            string workingDirectory = null,
            bool throwOnErrorExitCode = true,
            string errorMessage = null,
            CancellationToken? token = null,
            Action<string> onStandardOutput = null,
            Action<string> onStandardError = null,
            IDictionary<string, string> environmentOverrides = null
        )
        {
            // Output.Info($"Executing async command >{filename} {arguments}");

            var startInfo = new ProcessStartInfo(filename, arguments);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            if (environmentOverrides != null)
            {
                foreach (var kvp in environmentOverrides)
                {
                    startInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

            var results = await Run(startInfo, token, onStandardOutput, onStandardError);
            if (results.ExitCode != 0 && throwOnErrorExitCode && token?.IsCancellationRequested != true)
            {
                var commandLog = $"Command failed with exit code {results.ExitCode}: > {filename} {arguments}";

                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    Console.WriteLine($"ERROR: {errorMessage}");
                }

                Console.WriteLine($"{commandLog}");

                var indent = "        ==>";
                Console.WriteLine($"    StandardOutput:");
                results.StandardOutput.ForEach(line =>
                {
                    Console.WriteLine($"{indent}{line}");
                });

                Console.WriteLine($"    StandardError:");
                results.StandardError.ForEach(line =>
                {
                    Console.WriteLine($"{indent}{line}");
                });

                throw new Exception(commandLog);
            }
            return results;
        }

        private static async Task<Result> Run(
            ProcessStartInfo processStartInfo,
            CancellationToken? token = null,
            Action<string> onStandardOutput = null,
            Action<string> onStandardError = null)
        {
            var cancellationToken = token ?? CancellationToken.None;
            // force some settings in the start info so we can capture the output
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;

            var tcs = new TaskCompletionSource<Result>();

            var standardOutput = new List<string>();
            var standardError = new List<string>();

            var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            var standardOutputResults = new TaskCompletionSource<List<string>>();
            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    standardOutput.Add(args.Data);
                    if (onStandardOutput != null)
                    {
                        onStandardOutput(args.Data);
                    }
                }
                else
                {
                    standardOutputResults.TrySetResult(standardOutput);
                }
            };

            var standardErrorResults = new TaskCompletionSource<List<string>>();
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    standardError.Add(args.Data);
                    if (onStandardError != null)
                    {
                        onStandardError(args.Data);
                    }
                }
                else
                {
                    standardErrorResults.TrySetResult(standardError);
                }
            };

            process.Exited += (sender, args) =>
            {
                // Since the Exited event can happen asynchronously to the output and error events,
                // we use the task results for stdout/stderr to ensure they both closed

                tcs.TrySetResult(new Result
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = standardOutputResults.Task.Result,
                    StandardError = standardErrorResults.Task.Result,
                });
            };

            using (cancellationToken.Register(() =>
            {
                //  tcs.TrySetCanceled();
                try
                {
                    if (!process.HasExited)
                    {
                        standardErrorResults.TrySetResult(standardError);
                        standardOutputResults.TrySetResult(standardOutput);

                        process.Kill();
                    }
                }
                catch (InvalidOperationException) { }
            }))
            {
                // cancellationToken.ThrowIfCancellationRequested();

                bool? startResult = null;
                Exception startException = null;
                try
                {
                    startResult = process.Start();
                }
                catch (Exception e)
                {
                    startException = e;
                }

                if (startResult == true)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                else
                {
                    var commandString = $"{processStartInfo.FileName} {processStartInfo.Arguments}";
                    tcs.TrySetException(new InvalidOperationException($"Failed to start process for command: {commandString}", startException));
                }

                return await tcs.Task;
            }
        }

        public class Result
        {
            public int ExitCode { get; set; }
            public List<string> StandardOutput { get; set; }
            public List<string> StandardError { get; set; }
        }

        public class RunningProcess
        {
            private readonly Task<Result> _resultTask;
            public RunningProcess(Task<Result> resultTask)
            {
                _resultTask = resultTask;
            }

            public TaskAwaiter<Result> GetAwaiter()
            {
                return _resultTask.GetAwaiter();
            }
        }
    }
}