using HealthCheck.Core.Models;
using HealthCheck.Core.Services;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HealthCheck.Services;

public sealed class ShareHealthChecker : IShareHealthChecker
{
    private static readonly SemaphoreSlim _gate = new(1, 1); // נעילה גלובלית לכל הריצות
    private readonly ShareHealthConfig _config;

    public ShareHealthChecker(ShareHealthConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<ShareHealthResult> RunChecksAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await RunChecksInternal();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ShareHealthResult> RunChecksInternal()
    {
        var result = new ShareHealthResult
        {
            ShareName = _config.ShareName,
            TestFileSizeMb = _config.TestFileSizeMb,
            SmallFilesCount = _config.SmallFilesCount
        };

        try
        {
            EnsureHealthDirectoryExists();

            UpdateCapacity(result);
            var testFilePath = Path.Combine(_config.HealthDirectory, "fs_health_test.bin");
            RunWriteTest(testFilePath, result);
            RunReadTest(testFilePath, result);
            RunSmallFilesTest(result);
            RunSmallIoLatencyTest(testFilePath, result);
            RunDirectoryListingTest(result);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.ToString();
        }

        return result;
    }

    private void EnsureHealthDirectoryExists()
    {
        if (!Directory.Exists(_config.HealthDirectory))
        {
            Directory.CreateDirectory(_config.HealthDirectory);
        }
    }

    #region Capacity

    private void UpdateCapacity(ShareHealthResult result)
    {
        // גרסה פשוטה – מניחים ש-SharePath הוא drive כמו Z:\
        var root = Path.GetPathRoot(_config.SharePath)
                   ?? throw new InvalidOperationException($"Cannot get root from '{_config.SharePath}'.");

        var drive = new DriveInfo(root);
        long totalBytes = drive.TotalSize;
        long freeBytes = drive.AvailableFreeSpace;

        result.TotalBytes = totalBytes;
        result.FreeBytes = freeBytes;
        result.FreeRatio = totalBytes == 0 ? 0 : (double)freeBytes / totalBytes;
    }

    #endregion

    #region Write / Read test

    private void RunWriteTest(string testFilePath, ShareHealthResult result)
    {
        long fileSizeBytes = (long)_config.TestFileSizeMb * 1024L * 1024L;
        var buffer = new byte[1024 * 1024]; // 1MB
        var rnd = new Random(42);
        rnd.NextBytes(buffer);

        var sw = Stopwatch.StartNew();
        try
        {
            using (var fs = new FileStream(
                       testFilePath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: buffer.Length,
                       options: FileOptions.SequentialScan))
            {
                long written = 0;
                while (written < fileSizeBytes)
                {
                    int toWrite = (int)Math.Min(buffer.Length, fileSizeBytes - written);
                    fs.Write(buffer, 0, toWrite);
                    written += toWrite;
                }
            }

            sw.Stop();
            double seconds = sw.Elapsed.TotalSeconds;
            if (seconds <= 0) seconds = 0.000001;

            double throughput = fileSizeBytes / seconds;

            result.WriteDurationSeconds = seconds;
            result.WriteThroughputBytesPerSecond = throughput;
        }
        catch
        {
            sw.Stop();
            throw;
        }
    }

    private void RunReadTest(string testFilePath, ShareHealthResult result)
    {
        if (!File.Exists(testFilePath))
        {
            result.ReadDurationSeconds = null;
            result.ReadThroughputBytesPerSecond = null;
            result.ReadUnbufferedDurationSeconds = null;
            result.ReadUnbufferedThroughputBytesPerSecond = null;
            return;
        }

        var fileInfo = new FileInfo(testFilePath);
        long fileSize = fileInfo.Length;

        // 1) קריאה רגילה (cached)
        {
            byte[] buffer = new byte[1024 * 1024]; // 1MB
            var sw = Stopwatch.StartNew();
            long totalRead = 0;

            using (var fs = new FileStream(
                       testFilePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize: buffer.Length,
                       options: FileOptions.SequentialScan))
            {
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalRead += read;
                }
            }

            sw.Stop();
            double seconds = sw.Elapsed.TotalSeconds;
            if (seconds <= 0) seconds = 0.000001;

            double throughput = totalRead / seconds;

            result.ReadDurationSeconds = seconds;
            result.ReadThroughputBytesPerSecond = throughput;
        }

        // 2) קריאה unbuffered (ללא cache של file system)
        {
            const int sectorSize = 4096;
            long alignedSize = fileSize - (fileSize % sectorSize);

            if (alignedSize <= 0)
            {
                // אם מסיבה כלשהי הגודל לא מתאים – לא מודדים unbuffered
                result.ReadUnbufferedDurationSeconds = null;
                result.ReadUnbufferedThroughputBytesPerSecond = null;
                return;
            }

            byte[] buffer = new byte[sectorSize * 16];
            var sw = Stopwatch.StartNew();
            long totalRead = 0;

            using (var fs = OpenUnbufferedRead(testFilePath))
            {
                while (totalRead < alignedSize)
                {
                    int toRead = (int)Math.Min(buffer.Length, alignedSize - totalRead);
                    int read = fs.Read(buffer, 0, toRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }
            }

            sw.Stop();
            double seconds = sw.Elapsed.TotalSeconds;
            if (seconds <= 0) seconds = 0.000001;

            double throughput = totalRead / seconds;

            result.ReadUnbufferedDurationSeconds = seconds;
            result.ReadUnbufferedThroughputBytesPerSecond = throughput;
        }
    }


    #endregion

    #region Small files test

    private void RunSmallFilesTest(ShareHealthResult result)
    {
        int count = _config.SmallFilesCount;
        if (count <= 0) return;

        // CREATE
        var createSw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(_config.HealthDirectory, $"small_{Guid.NewGuid():N}.tmp");
            using (File.Create(path)) { }
        }
        createSw.Stop();

        double createSec = createSw.Elapsed.TotalSeconds;
        if (createSec <= 0) createSec = 0.000001;

        result.SmallCreateDurationSeconds = createSec;
        result.SmallCreateOpsPerSecond = count / createSec;

        // DELETE
        var deleteSw = Stopwatch.StartNew();
        foreach (var file in Directory.EnumerateFiles(_config.HealthDirectory, "small_*.tmp"))
        {
            File.Delete(file);
        }
        deleteSw.Stop();

        double deleteSec = deleteSw.Elapsed.TotalSeconds;
        if (deleteSec <= 0) deleteSec = 0.000001;

        result.SmallDeleteDurationSeconds = deleteSec;
        result.SmallDeleteOpsPerSecond = count / deleteSec;
    }

    private static FileStream OpenUnbufferedRead(string path)
    {
        const int sectorSize = 4096; // ברוב המערכות זה בדיוק זה

        SafeFileHandle handle = NativeFile.CreateFile(
            path,
            NativeFile.GENERIC_READ,
            NativeFile.FILE_SHARE_READ,
            IntPtr.Zero,
            NativeFile.OPEN_EXISTING,
            NativeFile.FILE_FLAG_NO_BUFFERING | NativeFile.FILE_FLAG_SEQUENTIAL_SCAN,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"CreateFile (unbuffered) failed for '{path}', error={error}.");
        }

        int bufferSize = sectorSize * 16; // חייב להיות כפולה של sector

        return new FileStream(
            handle,
            FileAccess.Read,
            bufferSize: bufferSize,
            isAsync: false);
    }
    private void RunSmallIoLatencyTest(string testFilePath, ShareHealthResult result)
    {
        try
        {
            var buffer = new byte[4096];
            new Random().NextBytes(buffer);

            // Write latency (4KB)
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(
                       testFilePath,
                       FileMode.OpenOrCreate,
                       FileAccess.Write,
                       FileShare.None))
            {
                fs.Position = 0;
                fs.Write(buffer, 0, buffer.Length);
                fs.Flush(true);
            }
            sw.Stop();
            result.SmallWriteLatencyMs = sw.Elapsed.TotalMilliseconds;

            // Read latency (4KB)
            sw.Restart();
            using (var fs = new FileStream(
                       testFilePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                fs.Position = 0;
                fs.Read(buffer, 0, buffer.Length);
            }
            sw.Stop();
            result.SmallReadLatencyMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (IOException ex)
        {
            result.IoErrorCount++;
            AppendError(result, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            result.IoErrorCount++;
            AppendError(result, ex);
        }
    }

    private void AppendError(ShareHealthResult result, Exception ex)
    {
        if (string.IsNullOrEmpty(result.ErrorMessage))
        {
            result.ErrorMessage = ex.ToString();
        }
        else
        {
            result.ErrorMessage += Environment.NewLine + ex;
        }
    }

    private void RunDirectoryListingTest(ShareHealthResult result)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            var dir = _config.SharePath; 
                                        
            _ = Directory.EnumerateFileSystemEntries(dir).ToList();

            sw.Stop();
            result.DirectoryListDurationSeconds = sw.Elapsed.TotalSeconds;
        }
        catch (IOException ex)
        {
            result.IoErrorCount++;
            AppendError(result, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            result.IoErrorCount++;
            AppendError(result, ex);
        }
    }

    #endregion
}
