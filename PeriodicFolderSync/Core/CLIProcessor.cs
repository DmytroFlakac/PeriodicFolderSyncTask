﻿using PeriodicFolderSync.Interfaces;
using System.CommandLine;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace PeriodicFolderSync.Core
{
    public class CLIProcessor(
        ISynchronizer synchronizer,
        IScheduler scheduler,
        ILogger<ICLIProcessor> logger,
        IAdminPrivilegeHandler adminHandler,
        ILogConfigurationProvider logConfigProvider)
        : ICLIProcessor
    {
        private readonly ISynchronizer _synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
        private readonly IScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        private readonly ILogger<ICLIProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IAdminPrivilegeHandler _adminHandler = adminHandler ?? throw new ArgumentNullException(nameof(adminHandler));
        private readonly ILogConfigurationProvider _logConfigProvider = logConfigProvider ?? throw new ArgumentNullException(nameof(logConfigProvider));

        public async Task<int> ProcessAsync(string?[] args)
        {
            var rootCommand = new RootCommand("PeriodicFolderSync - Synchronize folders periodically");

            var sourceOption = new Option<string>(
                aliases: ["--source", "-s"],
                description: "Source directory path")
            {
                IsRequired = true
            };

            var destinationOption = new Option<string>(
                aliases: ["--destination", "-d"],
                description: "Destination directory path")
            {
                IsRequired = true
            };

            var intervalOption = new Option<string>(
                aliases: ["--interval", "-i"],
                description: "Sync interval in minutes or time format (15s, 1m, 1h, 1d, 1y)");

            var adminOption = new Option<bool>(
                aliases: ["--admin"],
                description: "Run with administrator privileges");

            var logFileOption = new Option<string>(
                aliases: ["--log-file", "-l"],
                description: "Custom log file path");

            rootCommand.AddOption(sourceOption);
            rootCommand.AddOption(destinationOption);
            rootCommand.AddOption(intervalOption);
            rootCommand.AddOption(adminOption);
            rootCommand.AddOption(logFileOption);  

            rootCommand.SetHandler(async (source, destination, interval, runAsAdmin, logFilePath) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(logFilePath))
                    {
                        if (logFilePath.EndsWith("/") || logFilePath.EndsWith("\\"))
                        {
                            string sourceName = Path.GetFileName(source.TrimEnd('\\', '/'));
                            string destName = Path.GetFileName(destination.TrimEnd('\\', '/'));
                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            
                            logFilePath = Path.Combine(logFilePath, $"sync_{sourceName}_to_{destName}_{timestamp}.log");
                        }
                        
                        string? directory = Path.GetDirectoryName(logFilePath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        
                        _logConfigProvider.SetLogFilePath(logFilePath);
                        _logger.LogInformation($"Using custom log file: {logFilePath}");
                    }
                    else
                    {
                        logFilePath = _logConfigProvider.CreateDynamicLogFileName(source, destination);
                        _logConfigProvider.SetLogFilePath(logFilePath);
                    }
                    
                    
                    if (runAsAdmin && !_adminHandler.IsRunningAsAdmin())
                    {
                        _adminHandler.RestartAsAdmin(args);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(source))
                    {
                        _logger.LogError("Source directory is required");
                        Environment.ExitCode = 1;
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(destination))
                    {
                        _logger.LogError("Destination directory is required");
                        Environment.ExitCode = 1;
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(interval))
                    {
                        TimeSpan intervalTimeSpan;
                        
                        try
                        {
                            intervalTimeSpan = ParseTimeInterval(interval);
                        }
                        catch
                        {
                            if (!int.TryParse(interval, out var minutes) || minutes <= 0)
                            {
                                _logger.LogError($"Invalid interval format: {interval}. Use a number of minutes or time format like 15s, 1h, 1d, 1y");
                                throw new ArgumentException("Invalid interval format");
                            }
                            intervalTimeSpan = TimeSpan.FromMinutes(minutes);
                        }
                        
                        await _scheduler.Start(source, destination, intervalTimeSpan);
                        
                        _logger.LogInformation($"Press Ctrl+C to stop the scheduler");
                        
                        using var cts = new CancellationTokenSource();
                        Console.CancelKeyPress += (sender, e) => 
                        {
                            e.Cancel = true; 
                            _logger.LogInformation("Ctrl+C pressed. Stopping scheduler...");
                            _scheduler.Stop();
                            _logger.LogInformation("Scheduler stopped");
                            cts.Cancel();
                        };
                        
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                        }
                        catch (TaskCanceledException)
                        {
                            // Task was cancelled, exit gracefully
                        }
                    }
                    else
                    {
                        await _synchronizer.SynchronizeAsync(source, destination);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error: {ex.Message}");
                    throw;
                }
            }, sourceOption, destinationOption, intervalOption, adminOption, logFileOption);

            return await rootCommand.InvokeAsync(args!);
        }

        /// <summary>
        /// Parses time intervals in formats like "15s", "1h", "1d", "1y"
        /// </summary>
        /// <param name="input">String representation of time interval</param>
        /// <returns>Parsed TimeSpan</returns>
        private TimeSpan ParseTimeInterval(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Time interval cannot be empty", nameof(input));

            var regex = new Regex(@"^(\d+)([smhdy])$", RegexOptions.IgnoreCase);
            var match = regex.Match(input);

            if (!match.Success)
                throw new ArgumentException($"Invalid time interval format: {input}. Expected formats: 15s, 1m, 1h, 1d, 1y", nameof(input));

            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToLower();

            return unit switch
            {
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                "y" => TimeSpan.FromDays(value * 365), 
                _ => throw new ArgumentException($"Unsupported time unit: {unit}", nameof(input))
            };
        }

        /// <summary> Reads a line of input from the console.(this method is virtual to allow mocking in unit tests) </summary>
        /// <returns> The next line of characters from the input stream, or null if no more lines are available.</returns>
        protected virtual string? ReadLineFromConsole()
        {
            return Console.ReadLine();
        }

        public Task<string?[]> GetInteractiveInputIfNeededAsync(string?[] args)
        {
            if (args.Length > 0)
            {
                return Task.FromResult(args);
            }

            Console.WriteLine("No arguments provided. Please enter the required parameters:");
            
            Console.Write("Source folder path: ");
            string? sourceFolder = ReadLineFromConsole();
            
            Console.Write("Destination folder path: ");
            string? destinationFolder = ReadLineFromConsole();
            
            Console.Write("Sync interval (e.g. '5m' for 5 minutes, '1h' for 1 hour, or 'once' for one-time sync): ");
            string? intervalInput = ReadLineFromConsole();
            
            Console.Write("Custom log file path (leave empty for default): ");
            string? logFilePath = ReadLineFromConsole();
            
            Console.Write("Run with administrator privileges? (y/n): ");
            string? adminInput = ReadLineFromConsole();
            bool runAsAdmin = adminInput?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ?? false;
            
            var argsList = new List<string?>();
            argsList.Add("--source");
            argsList.Add(sourceFolder);
            argsList.Add("--destination");
            argsList.Add(destinationFolder);
            
            if (!string.IsNullOrWhiteSpace(intervalInput) && !intervalInput.Equals("once", StringComparison.OrdinalIgnoreCase))
            {
                argsList.Add("--interval");
                argsList.Add(intervalInput);
            }
            
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                argsList.Add("--log-file");
                argsList.Add(logFilePath);
            }
            
            if (runAsAdmin)
            {
                argsList.Add("--admin");
            }
            
            return Task.FromResult(argsList.ToArray());
        }
    }
}