using PeriodicFolderSync.Interfaces;
using PeriodicFolderSync.Models;
using Microsoft.Extensions.Logging;

namespace PeriodicFolderSync.Core
{
    public class Synchronizer(
        IFolderSynchronizer folderSynchronizer,
        IFileSynchronizer fileSynchronizer,
        ILogger<ISynchronizer> logger
        )
        : ISynchronizer
    {
        private readonly IFolderSynchronizer _folderSynchronizer = folderSynchronizer ?? throw new ArgumentNullException(nameof(folderSynchronizer));
        private readonly IFileSynchronizer _fileSynchronizer = fileSynchronizer ?? throw new ArgumentNullException(nameof(fileSynchronizer));
        private readonly ILogger<ISynchronizer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task SynchronizeAsync(string source, string destination)
        {
            _logger.LogInformation($"Starting synchronization from {source} to {destination}");
            var stats = new SyncStatistics();

            

            await _folderSynchronizer.SynchronizeFoldersAsync(source, destination, stats);
            await _fileSynchronizer.SynchronizeFilesAsync(source, destination, stats);

            LogSummary(stats);
            _logger.LogInformation("Synchronization completed");
        }

        /// <summary>
        /// Logs a summary of the synchronization statistics.
        /// </summary>
        /// <param name="stats">The statistics object containing synchronization metrics.</param>
        private void LogSummary(SyncStatistics stats)
        {
            _logger.LogInformation(
                $"Synchronization summary: {stats.ChangedCount} files changed/added, " +
                $"{stats.FoldersChangedCount} folders added, " +
                $"{stats.FilesMoved} files moved/renamed individually, " +
                $"{stats.FoldersMovedCount} folders moved/renamed containing {stats.FilesInMovedFolders} files, " +
                $"{stats.DeletedFiles} files and {stats.DeletedFolders} folders deleted"
            );
        }
    }
}